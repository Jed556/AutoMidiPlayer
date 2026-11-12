use amp_core::player::live_input::MidiMessage;
use amp_core::{settings::SettingsStore, PlaybackEngine};
use midir::{Ignore, MidiInput, MidiInputConnection};
use std::collections::HashSet;
use std::sync::{
    atomic::{AtomicBool, Ordering},
    mpsc, Arc, Mutex,
};
use std::time::{Duration, Instant};
use tauri::{AppHandle, Emitter, Manager, State};

#[cfg(target_os = "linux")]
#[path = "midi_input/wayland.rs"]
mod wayland;

#[derive(Clone, Debug, PartialEq, Eq, serde::Serialize)]
#[serde(rename_all = "camelCase")]
pub struct Port {
    id: String,
    name: String,
}

#[derive(Clone, Debug, Default, PartialEq, Eq, serde::Serialize)]
#[serde(rename_all = "camelCase")]
pub struct InputState {
    ports: Vec<Port>,
    selected_port_id: Option<String>,
    connected: bool,
    active: bool,
    starting: bool,
    listen_mode: bool,
    output_available: bool,
    requires_session: bool,
    status: String,
    pitches: Vec<u8>,
    error: Option<String>,
}

enum Action {
    Refresh,
    Select(Option<String>),
    Start,
    Stop,
    Shutdown,
}
enum Command {
    Request(Action, mpsc::Sender<Result<InputState, String>>),
    #[cfg(target_os = "linux")]
    Authorized(
        u64,
        Result<Box<dyn amp_core::input::simulator::KeyboardOutput>, wayland::AuthorizationFailure>,
    ),
}

pub struct MidiInputService {
    tx: mpsc::Sender<Command>,
    state: Arc<Mutex<InputState>>,
    thread: Mutex<Option<std::thread::JoinHandle<()>>>,
    focused_windows: Arc<Mutex<HashSet<String>>>,
}

fn is_wayland() -> bool {
    cfg!(target_os = "linux") && std::env::var_os("WAYLAND_DISPLAY").is_some()
}

fn ports() -> Result<Vec<Port>, String> {
    let input = MidiInput::new("AMP MIDI device discovery").map_err(|e| e.to_string())?;
    Ok(input
        .ports()
        .iter()
        .filter_map(|p| {
            input
                .port_name(p)
                .ok()
                .map(|name| Port { id: p.id(), name })
        })
        .collect())
}

impl MidiInputService {
    pub fn new(
        app: AppHandle,
        engine: Arc<PlaybackEngine>,
        settings: Arc<SettingsStore>,
        monitor: Arc<Mutex<crate::process_monitor::ProcessMonitor>>,
    ) -> Self {
        let (tx, rx) = mpsc::channel();
        let mut state = InputState {
            requires_session: is_wayland(),
            status: "idle".into(),
            ..Default::default()
        };
        state.selected_port_id = settings.get().ok().and_then(|s| {
            s.get("midiInputPortId")
                .and_then(|v| v.as_str())
                .map(str::to_owned)
        });
        let state = Arc::new(Mutex::new(state));
        let shared = state.clone();
        // Start closed until the native window reports leaving AMP. Keep this
        // event-driven: querying GTK from the worker can deadlock app shutdown.
        let focused_windows = Arc::new(Mutex::new(HashSet::from(["main".to_owned()])));
        let focused = Arc::clone(&focused_windows);
        let sender = tx.clone();
        let thread = std::thread::Builder::new()
            .name("midi-input-service".into())
            .spawn(move || {
                let mut worker = Worker {
                    app,
                    engine,
                    settings,
                    monitor,
                    state: shared.lock().unwrap().clone(),
                    connection: None,
                    epoch: 0,
                    portal: false,
                    authorization_cancel: None,
                };
                worker.refresh();
                let mut last_ports = Instant::now();
                let mut last_focus = Instant::now() - Duration::from_secs(1);
                let mut statuses = Vec::new();
                let mut previous = None;
                loop {
                    match rx.recv_timeout(Duration::from_millis(20)) {
                        Ok(Command::Request(Action::Shutdown, reply)) => {
                            worker.stop();
                            let _ = reply.send(Ok(worker.state.clone()));
                            break;
                        }
                        Ok(Command::Request(action, reply)) => {
                            let result = match action {
                                Action::Refresh => {
                                    worker.refresh();
                                    Ok(())
                                }
                                Action::Select(id) => {
                                    worker.stop();
                                    worker.state.selected_port_id = id.clone();
                                    worker
                                        .settings
                                        .update(&serde_json::json!({ "midiInputPortId": id }))
                                        .map_err(|e| e.to_string())
                                }
                                Action::Start => worker.start(&sender),
                                Action::Stop => {
                                    worker.stop();
                                    Ok(())
                                }
                                Action::Shutdown => unreachable!(),
                            };
                            if let Err(error) = &result {
                                worker.fail(error.clone());
                            }
                            let _ = reply.send(result.map(|_| worker.state.clone()));
                        }
                        #[cfg(target_os = "linux")]
                        Ok(Command::Authorized(epoch, result)) => {
                            if epoch == worker.epoch && worker.state.starting {
                                match result {
                                    Ok(output) => {
                                    if focused.lock().map(|windows| !windows.is_empty()).unwrap_or(true)
                                        && !worker.engine.midi_snapshot().listen_mode {
                                        worker.engine.pause();
                                    }
                                        worker.engine.set_keyboard_output(Some(output));
                                        worker.portal = true;
                                        if let Err(error) = worker.connect() {
                                            worker.fail(error);
                                        }
                                    }
                                    Err(error) => {
                                        worker.fail(error.0.detail.clone().unwrap_or_else(|| "Wayland keyboard output is unavailable".into()));
                                        worker.engine.observe_keyboard(error.0);
                                    },
                                }
                            }
                        }
                        Err(mpsc::RecvTimeoutError::Disconnected) => {
                            worker.stop();
                            break;
                        }
                        Err(mpsc::RecvTimeoutError::Timeout) => {}
                    }
                    if last_ports.elapsed() >= Duration::from_secs(1) {
                        worker.refresh();
                        statuses = worker
                            .monitor
                            .lock()
                            .map(|mut m| m.check_statuses())
                            .unwrap_or_default();
                        last_ports = Instant::now();
                    }
                    if last_focus.elapsed() >= Duration::from_millis(50) {
                        let allowed = if worker.state.requires_session {
                            worker.portal
                                && focused
                                    .lock()
                                    .map(|windows| windows.is_empty())
                                    .unwrap_or(false)
                        } else {
                            let game = worker.settings.get().ok().and_then(|s| {
                                s.get("activeGameId")
                                    .and_then(|v| v.as_str())
                                    .map(str::to_owned)
                            });
                            let pid = statuses
                                .iter()
                                .find(|s| Some(&s.id) == game.as_ref())
                                .or_else(|| statuses.first())
                                .and_then(|s| s.pid);
                            pid.is_some() && pid == crate::process_monitor::foreground_pid()
                        };
                        worker.engine.midi_gate(allowed && worker.state.active);
                        last_focus = Instant::now();
                    }
                    let snapshot = worker.engine.midi_snapshot();
                    worker.state.listen_mode = snapshot.listen_mode;
                    worker.state.output_available = if snapshot.listen_mode {
                        snapshot.audio_available
                    } else {
                        snapshot.keyboard_available
                    };
                    if worker.state.active && snapshot.generation == worker.epoch {
                        if !snapshot.active {
                            if let Some(error) = snapshot.error {
                                worker.fail(error);
                            } else {
                                worker.stop();
                            }
                        } else {
                            worker.state.pitches = snapshot.pitches;
                            worker.state.status = if snapshot.listen_mode {
                                "listening"
                            } else if snapshot.game_allowed {
                                "playing"
                            } else {
                                "suspended"
                            }
                            .into();
                        }
                    }
                    if worker.state.requires_session
                        && worker.state.active
                        && !snapshot.listen_mode
                        && !worker.portal
                    {
                        worker.fail(
                            "Start MIDI Input to authorize keyboard access on Wayland.".into(),
                        );
                    }
                    if worker.portal && snapshot.listen_mode {
                        worker.engine.set_keyboard_output(None);
                        worker.portal = false;
                    }
                    *shared.lock().unwrap() = worker.state.clone();
                    if previous.as_ref() != Some(&worker.state) {
                        let _ = worker.app.emit("midi-input:state", &worker.state);
                        previous = Some(worker.state.clone());
                    }
                }
            })
            .expect("start MIDI input service");
        Self {
            tx,
            state,
            thread: Mutex::new(Some(thread)),
            focused_windows,
        }
    }
    fn request(&self, action: Action) -> Result<InputState, String> {
        let (tx, rx) = mpsc::channel();
        self.tx
            .send(Command::Request(action, tx))
            .map_err(|e| e.to_string())?;
        rx.recv().map_err(|e| e.to_string())?
    }
    pub fn window_focus(&self, label: &str, focused: bool) {
        if let Ok(mut windows) = self.focused_windows.lock() {
            if focused {
                windows.insert(label.to_owned());
            } else {
                windows.remove(label);
            }
        }
    }
    pub fn has_keyboard_session(&self) -> bool {
        self.state.lock().map(|state| state.requires_session && (state.active || state.starting)).unwrap_or(false)
    }
    pub fn reset(&self) {
        let _ = self.request(Action::Select(None));
    }
    pub fn shutdown(&self) {
        let _ = self.request(Action::Shutdown);
        if let Some(thread) = self.thread.lock().unwrap().take() {
            let _ = thread.join();
        }
    }
}

struct Worker {
    app: AppHandle,
    engine: Arc<PlaybackEngine>,
    settings: Arc<SettingsStore>,
    monitor: Arc<Mutex<crate::process_monitor::ProcessMonitor>>,
    state: InputState,
    connection: Option<MidiInputConnection<()>>,
    epoch: u64,
    portal: bool,
    authorization_cancel: Option<Arc<AtomicBool>>,
}

impl Worker {
    fn refresh(&mut self) {
        match ports() {
            Ok(ports) => {
                let missing = self
                    .state
                    .selected_port_id
                    .as_ref()
                    .is_some_and(|id| !ports.iter().any(|p| &p.id == id));
                self.state.ports = ports;
                if missing && (self.state.active || self.state.starting) {
                    self.fail(
                        "The selected MIDI device was disconnected. Reconnect it and refresh MIDI inputs."
                            .into(),
                    );
                }
            }
            Err(error) => self.fail(format!("MIDI device discovery failed: {error}")),
        }
    }
    fn stop(&mut self) {
        if let Some(cancel) = self.authorization_cancel.take() {
            cancel.store(true, Ordering::Release);
            if self.state.starting { self.engine.observe_keyboard(amp_core::diagnostics::ServiceStatus::new(amp_core::diagnostics::ServiceState::Inactive)); }
        }
        self.epoch = self.epoch.wrapping_add(1);
        // Invalidate old callbacks before closing a potentially busy connection.
        self.engine.midi_stop();
        self.connection.take();
        if self.portal {
            if !self.engine.midi_snapshot().listen_mode { self.engine.pause(); }
            self.engine.set_keyboard_output(None);
            self.portal = false;
        }
        self.state.active = false;
        self.state.starting = false;
        self.state.connected = false;
        self.state.pitches.clear();
        self.state.status = "idle".into();
        self.state.error = None;
    }
    fn fail(&mut self, error: String) {
        self.stop();
        self.state.error = Some(error);
        self.state.status = "error".into();
    }
    fn start(&mut self, _sender: &mpsc::Sender<Command>) -> Result<(), String> {
        self.stop();
        self.state.error = None;
        let Some(id) = &self.state.selected_port_id else {
            return Err("Select a MIDI input device first.".into());
        };
        if !self.state.ports.iter().any(|p| &p.id == id) {
            return Err("The selected MIDI input device is unavailable.".into());
        }
        let snapshot = self.engine.midi_snapshot();
        if snapshot.listen_mode {
            if !snapshot.audio_available {
                return Err("Audio output is unavailable. Check the system audio device.".into());
            }
        } else if self.state.requires_session {
            #[cfg(target_os = "linux")]
            {
                self.state.starting = true;
                self.state.status = "permission".into();
                self.engine.observe_keyboard(amp_core::diagnostics::ServiceStatus::new(amp_core::diagnostics::ServiceState::Checking));
                let epoch = self.epoch;
                let sender = _sender.clone();
                let cancel = Arc::new(AtomicBool::new(false));
                self.authorization_cancel = Some(cancel.clone());
                std::thread::spawn(move || {
                    let output = wayland::connect(cancel);
                    let _ = sender.send(Command::Authorized(epoch, output));
                });
                return Ok(());
            }
        } else {
            self.engine.prepare_keyboard()?;
        }
        self.connect()
    }
    fn connect(&mut self) -> Result<(), String> {
        let mut input = MidiInput::new("AMP physical MIDI input").map_err(|e| e.to_string())?;
        input.ignore(Ignore::Sysex | Ignore::Time | Ignore::ActiveSense);
        let port = input
            .find_port_by_id(self.state.selected_port_id.as_deref().unwrap_or_default())
            .ok_or("The MIDI device is no longer available.")?;
        let engine = self.engine.clone();
        let generation = self.epoch;
        self.engine.midi_start(generation);
        let connection = input
            .connect(
                &port,
                "AMP MIDI input",
                move |_, bytes, _| {
                    if let Some(message) = MidiMessage::parse(bytes) {
                        engine.midi_message(generation, message);
                    }
                },
                (),
            )
            .map_err(|e| e.to_string())?;
        self.connection = Some(connection);
        self.state.connected = true;
        self.state.active = true;
        self.state.starting = false;
        self.state.status = "suspended".into();
        Ok(())
    }
}

#[tauri::command]
pub fn get_midi_input_state(state: State<'_, crate::AppState>) -> InputState {
    state.midi_input.state.lock().unwrap().clone()
}

async fn request(app: AppHandle, action: Action) -> Result<InputState, String> {
    tauri::async_runtime::spawn_blocking(move || {
        app.state::<crate::AppState>().midi_input.request(action)
    })
    .await
    .map_err(|e| e.to_string())?
}
#[tauri::command]
pub async fn refresh_midi_inputs(app: AppHandle) -> Result<InputState, String> {
    request(app, Action::Refresh).await
}
#[tauri::command]
pub async fn select_midi_input(
    app: AppHandle,
    port_id: Option<String>,
) -> Result<InputState, String> {
    request(app, Action::Select(port_id)).await
}
#[tauri::command]
pub async fn start_midi_input(app: AppHandle) -> Result<InputState, String> {
    request(app, Action::Start).await
}
#[tauri::command]
pub async fn stop_midi_input(app: AppHandle) -> Result<InputState, String> {
    request(app, Action::Stop).await
}
