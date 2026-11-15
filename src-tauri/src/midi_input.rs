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
    PrepareOutput,
    Shutdown,
}
enum Command {
    Request(Action, mpsc::Sender<Result<InputState, String>>),
    CancelOutputPreparation,
    #[cfg(target_os = "linux")]
    Authorized(
        u64,
        Result<Box<dyn amp_core::input::simulator::KeyboardOutput>, wayland::AuthorizationFailure>,
    ),
}

/// Coalesce song and physical-input requests into one permission dialog, and
/// reject late completions after a stop, reset, or replacement request.
#[derive(Default)]
struct KeyboardAuthorization {
    epoch: u64,
    cancel: Option<Arc<AtomicBool>>,
}

impl KeyboardAuthorization {
    #[cfg(any(target_os = "linux", test))]
    fn begin(&mut self) -> Option<(u64, Arc<AtomicBool>)> {
        if self.cancel.is_some() { return None; }
        self.epoch = self.epoch.wrapping_add(1);
        let cancel = Arc::new(AtomicBool::new(false));
        self.cancel = Some(cancel.clone());
        Some((self.epoch, cancel))
    }

    #[cfg(any(target_os = "linux", test))]
    fn complete(&mut self, epoch: u64) -> bool {
        if epoch != self.epoch || self.cancel.is_none() { return false; }
        self.cancel = None;
        true
    }

    fn cancel(&mut self) {
        self.epoch = self.epoch.wrapping_add(1);
        if let Some(cancel) = self.cancel.take() {
            cancel.store(true, Ordering::Release);
        }
    }

    fn is_pending(&self) -> bool { self.cancel.is_some() }
}

pub struct MidiInputService {
    tx: mpsc::Sender<Command>,
    state: Arc<Mutex<InputState>>,
    thread: Mutex<Option<std::thread::JoinHandle<()>>>,
    focused_windows: Arc<Mutex<HashSet<String>>>,
    keyboard_session: Arc<AtomicBool>,
}

pub(crate) fn is_wayland() -> bool {
    cfg!(target_os = "linux") && (std::env::var_os("WAYLAND_DISPLAY").is_some_and(|value| !value.is_empty())
        || std::env::var("XDG_SESSION_TYPE").is_ok_and(|value| value.eq_ignore_ascii_case("wayland")))
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
        let keyboard_session = Arc::new(AtomicBool::new(false));
        let worker_session = keyboard_session.clone();
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
                    authorization: KeyboardAuthorization::default(),
                    pending_midi_start: false,
                    output_waiters: Vec::new(),
                    keyboard_session: worker_session,
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
                            worker.close_keyboard_session();
                            let _ = reply.send(Ok(worker.state.clone()));
                            break;
                        }
                        Ok(Command::Request(action, reply)) => {
                            if matches!(action, Action::PrepareOutput) {
                                worker.prepare_output(&sender, reply);
                                continue;
                            }
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
                                Action::PrepareOutput => unreachable!(),
                            };
                            if let Err(error) = &result {
                                worker.fail(error.clone());
                            }
                            let _ = reply.send(result.map(|_| worker.state.clone()));
                        }
                        Ok(Command::CancelOutputPreparation) => {
                            worker.reply_output_waiters(Err("Keyboard output authorization was cancelled.".into()));
                            if !worker.pending_midi_start && worker.authorization.is_pending() {
                                worker.close_keyboard_session();
                            }
                        }
                        #[cfg(target_os = "linux")]
                        Ok(Command::Authorized(epoch, result)) => {
                            if worker.authorization.complete(epoch) {
                                match result {
                                    Ok(output) => {
                                    if focused.lock().map(|windows| !windows.is_empty()).unwrap_or(true)
                                        && !worker.engine.midi_snapshot().listen_mode {
                                        worker.engine.pause();
                                    }
                                        worker.engine.set_keyboard_output(Some(output));
                                        worker.portal = true;
                                        if let Err(error) = worker.engine.flush() {
                                            worker.reply_output_waiters(Err(error.clone()));
                                            worker.close_keyboard_session();
                                            worker.fail(error);
                                            continue;
                                        }
                                        worker.keyboard_session.store(true, Ordering::Release);
                                        if worker.pending_midi_start {
                                            worker.pending_midi_start = false;
                                            if let Err(error) = worker.connect() {
                                                worker.fail(error);
                                            }
                                        }
                                        worker.reply_output_waiters(Ok(()));
                                    }
                                    Err(error) => {
                                        let detail = error.0.detail.clone().unwrap_or_else(|| "Wayland keyboard output is unavailable".into());
                                        worker.fail(detail.clone());
                                        worker.engine.observe_keyboard(error.0);
                                        worker.reply_output_waiters(Err(detail));
                                    },
                                }
                            }
                        }
                        Err(mpsc::RecvTimeoutError::Disconnected) => {
                            worker.stop();
                            worker.close_keyboard_session();
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
                        worker.close_keyboard_session();
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
            keyboard_session,
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
        self.keyboard_session.load(Ordering::Acquire)
    }
    pub fn is_app_focused(&self) -> bool {
        self.focused_windows.lock().map(|windows| !windows.is_empty()).unwrap_or(true)
    }
    pub fn cancel_prepare_output(&self) {
        let _ = self.tx.send(Command::CancelOutputPreparation);
    }
    /// Authorize game output independently of a physical MIDI device. Portal
    /// permission prompts run outside both the UI and playback threads.
    pub async fn prepare_game_output(self: &Arc<Self>) -> Result<(), String> {
        let service = self.clone();
        tauri::async_runtime::spawn_blocking(move || service.request(Action::PrepareOutput).map(|_| ()))
            .await.map_err(|error| error.to_string())?
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
    authorization: KeyboardAuthorization,
    pending_midi_start: bool,
    output_waiters: Vec<mpsc::Sender<Result<InputState, String>>>,
    keyboard_session: Arc<AtomicBool>,
}

impl Worker {
    fn reply_output_waiters(&mut self, result: Result<(), String>) {
        for reply in self.output_waiters.drain(..) {
            let _ = reply.send(result.clone().map(|_| self.state.clone()));
        }
    }

    fn close_keyboard_session(&mut self) {
        self.authorization.cancel();
        self.keyboard_session.store(false, Ordering::Release);
        if self.portal {
            self.engine.set_keyboard_output(None);
            self.portal = false;
        }
        self.reply_output_waiters(Err("Keyboard output authorization was cancelled.".into()));
    }

    fn prepare_output(&mut self, sender: &mpsc::Sender<Command>, reply: mpsc::Sender<Result<InputState, String>>) {
        if !self.state.requires_session {
            let result = self.engine.prepare_keyboard().map(|_| self.state.clone());
            let _ = reply.send(result);
            return;
        }
        if self.portal && self.engine.midi_snapshot().keyboard_available {
            let _ = reply.send(Ok(self.state.clone()));
            return;
        }
        self.output_waiters.push(reply);
        self.authorize_keyboard(sender);
    }

    fn authorize_keyboard(&mut self, _sender: &mpsc::Sender<Command>) {
        #[cfg(target_os = "linux")]
        {
            let Some((epoch, cancel)) = self.authorization.begin() else { return; };
            let sender = _sender.clone();
            self.engine.observe_keyboard(amp_core::diagnostics::ServiceStatus::new(amp_core::diagnostics::ServiceState::Checking));
            std::thread::spawn(move || {
                let output = wayland::connect(cancel);
                let _ = sender.send(Command::Authorized(epoch, output));
            });
        }
    }

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
        self.pending_midi_start = false;
        // An authorized portal also serves song playback. Changing or stopping
        // a MIDI device must not revoke the song's keyboard output.
        if self.output_waiters.is_empty() && self.authorization.is_pending() {
            self.close_keyboard_session();
            self.engine.observe_keyboard(amp_core::diagnostics::ServiceStatus::new(amp_core::diagnostics::ServiceState::Inactive));
        }
        self.epoch = self.epoch.wrapping_add(1);
        // Invalidate old callbacks before closing a potentially busy connection.
        self.engine.midi_stop();
        self.connection.take();
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
                if self.portal && snapshot.keyboard_available {
                    return self.connect();
                }
                self.state.starting = true;
                self.state.status = "permission".into();
                self.pending_midi_start = true;
                self.authorize_keyboard(_sender);
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

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn concurrent_output_requests_share_one_permission_dialog() {
        let mut authorization = KeyboardAuthorization::default();
        let (epoch, cancel) = authorization.begin().unwrap();
        assert!(authorization.begin().is_none());
        assert!(!cancel.load(Ordering::Acquire));
        assert!(authorization.complete(epoch));
        assert!(!authorization.is_pending());
        // A duplicate completion cannot install another backend.
        assert!(!authorization.complete(epoch));
    }

    #[test]
    fn stopped_authorization_cannot_replace_a_new_keyboard_session() {
        let mut authorization = KeyboardAuthorization::default();
        let (old_epoch, old_cancel) = authorization.begin().unwrap();
        authorization.cancel();
        assert!(old_cancel.load(Ordering::Acquire));
        let (new_epoch, new_cancel) = authorization.begin().unwrap();
        assert!(!authorization.complete(old_epoch));
        assert!(authorization.is_pending());
        assert!(!new_cancel.load(Ordering::Acquire));
        assert!(authorization.complete(new_epoch));
    }
}
