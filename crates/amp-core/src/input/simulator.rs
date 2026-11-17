use enigo::{Direction, Enigo, Key, Keyboard, Settings};
#[cfg(windows)]
use enigo::{Mouse, Coordinate, Button};
use super::{chord_wheel::{ChordWheelOutput, WheelButton}, config::ChordWheelConfigJson};
use std::collections::{HashMap, HashSet};
use std::time::{Duration, Instant};
use super::mapping::KeyStroke;
use crate::diagnostics::{ServiceState, ServiceStatus};

#[cfg(any(windows, test))]
fn matches_game_process(path: &str, processes: &[String]) -> bool {
    fn normalized(value: &str) -> String {
        let name = value.trim().trim_matches('"').rsplit(['/', '\\']).next().unwrap_or("").to_lowercase();
        name.strip_suffix(".exe").unwrap_or(&name).to_string()
    }
    let actual = normalized(path);
    !actual.is_empty() && processes.iter().any(|process| normalized(process) == actual)
}

#[cfg(any(target_os = "linux", test))]
fn wayland_session(display: Option<&std::ffi::OsStr>, session_type: Option<&std::ffi::OsStr>) -> bool {
    display.is_some_and(|value| !value.is_empty())
        || session_type.is_some_and(|value| value.to_string_lossy().eq_ignore_ascii_case("wayland"))
}

/// Keep physical pedal holds separate from timed note taps. Several MIDI
/// channels (or pedals sharing a binding) can own the same held game key.
#[derive(Default)]
struct PedalOutput {
    held: HashSet<Key>,
    last: Option<([Option<KeyStroke>; 3], [bool; 3], bool)>,
}

impl PedalOutput {
    fn reconcile(&mut self, bindings: &[Option<KeyStroke>; 3], down: [bool; 3], enabled: bool) -> Vec<(Key, bool)> {
        let snapshot = (*bindings, down, enabled);
        if self.last == Some(snapshot) { return Vec::new(); }
        self.last = Some(snapshot);
        let desired: Vec<_> = bindings.iter().zip(down)
            .filter_map(|(binding, down)| binding.filter(|_| enabled && down)).collect();
        let keys: HashSet<_> = desired.iter().map(|stroke| stroke.key).collect();
        let mut events: Vec<_> = self.held.difference(&keys).map(|&key| (key, true)).collect();
        for stroke in desired {
            if !self.held.contains(&stroke.key) {
                events.extend(stroke.press_events());
                self.held.insert(stroke.key);
            }
        }
        self.held = keys;
        events
    }

    fn clear(&mut self) {
        self.held.clear();
        self.last = None;
    }
}

pub struct KeySimulator {
    output: Option<Box<dyn KeyboardOutput>>,
    owners: [HashSet<Key>; 5],
    error: Option<String>,
    observation: ServiceStatus,
    hold_delay: Duration,
    pressed_keys: HashSet<Key>,
    last_press_time: Option<Instant>,
    pedals: PedalOutput,
    wheel: ChordWheelOutput,
    wheel_context: Option<String>,
    wheel_pages: HashMap<String, Option<usize>>,
}

/// One backend owns the physical keyboard. Wayland supplies a portal backend;
/// other desktops initialize Enigo only when output is requested.
pub trait KeyboardOutput: Send {
    fn key(&mut self, key: Key, up: bool) -> Result<(), String>;
    fn error(&self) -> Option<String> { None }
    fn failure_state(&self) -> ServiceState { ServiceState::Unavailable }
    fn wheel_move(&mut self, _processes: &[String], _point: [u16; 2]) -> Result<(), String> {
        Err("Chord wheel mouse output is currently supported on Windows only.".into())
    }
    fn wheel_button(&mut self, _button: WheelButton, _up: bool) -> Result<(), String> {
        Err("Chord wheel mouse output is unavailable.".into())
    }
    fn wheel_focused(&self) -> bool { false }
}

struct EnigoOutput {
    enigo: Enigo,
    #[cfg(windows)]
    wheel_window: Option<usize>,
}
#[cfg(target_os = "macos")]
#[link(name = "ApplicationServices", kind = "framework")]
extern "C" { fn AXIsProcessTrusted() -> bool; }

impl KeyboardOutput for EnigoOutput {
    fn failure_state(&self) -> ServiceState {
        #[cfg(target_os = "macos")]
        if !unsafe { AXIsProcessTrusted() } { return ServiceState::PermissionRequired; }
        ServiceState::Unavailable
    }
    fn key(&mut self, key: Key, up: bool) -> Result<(), String> {
        self.enigo.key(key, if up { Direction::Release } else { Direction::Press }).map_err(|e| e.to_string())
    }
    #[cfg(windows)]
    fn wheel_move(&mut self, processes: &[String], point: [u16; 2]) -> Result<(), String> {
        use windows_sys::Win32::{Foundation::{CloseHandle, POINT, RECT}, Graphics::Gdi::ClientToScreen,
            System::Threading::{OpenProcess, QueryFullProcessImageNameW, PROCESS_QUERY_LIMITED_INFORMATION},
            UI::WindowsAndMessaging::{GetForegroundWindow, GetWindowThreadProcessId, GetClientRect}};
        // SAFETY: all output pointers refer to live stack buffers. Process handles
        // are closed immediately; the foreground HWND is used only for this call.
        let (window, x, y) = unsafe {
            let window = GetForegroundWindow();
            let mut pid = 0;
            GetWindowThreadProcessId(window, &mut pid);
            let handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, 0, pid);
            if handle.is_null() { return Err("Focus the game window before chord playback.".into()); }
            let mut name = [0u16; 32768];
            let mut length = name.len() as u32;
            let found = QueryFullProcessImageNameW(handle, 0, name.as_mut_ptr(), &mut length);
            CloseHandle(handle);
            let path = String::from_utf16_lossy(&name[..length.min(name.len() as u32) as usize]);
            if found == 0 || !matches_game_process(&path, processes) {
                return Err("Focus the selected game window before chord playback.".into());
            }
            let mut rect = RECT::default();
            let mut origin = POINT::default();
            if GetClientRect(window, &mut rect) == 0 || ClientToScreen(window, &mut origin) == 0
                || rect.right <= 0 || rect.bottom <= 0 { return Err("Cannot locate the game area.".into()); }
            (window as usize, origin.x + ((rect.right - 1) as i64 * point[0] as i64 / 1000) as i32,
                origin.y + ((rect.bottom - 1) as i64 * point[1] as i64 / 1000) as i32)
        };
        self.wheel_window = Some(window);
        self.enigo.move_mouse(x, y, Coordinate::Abs).map_err(|e| e.to_string())
    }
    #[cfg(windows)]
    fn wheel_focused(&self) -> bool {
        // SAFETY: GetForegroundWindow has no arguments or ownership transfer.
        self.wheel_window.is_some_and(|window| unsafe {
            windows_sys::Win32::UI::WindowsAndMessaging::GetForegroundWindow() as usize == window
        })
    }
    #[cfg(windows)]
    fn wheel_button(&mut self, button: WheelButton, up: bool) -> Result<(), String> {
        if !up && !self.wheel_focused() { return Err("The game lost focus; chord playback was paused.".into()); }
        self.enigo.button(match button { WheelButton::Left => Button::Left, WheelButton::Right => Button::Right },
            if up { Direction::Release } else { Direction::Press }).map_err(|e| e.to_string())
    }
    fn error(&self) -> Option<String> {
        #[cfg(target_os = "macos")]
        if !unsafe { AXIsProcessTrusted() } {
            return Some("Keyboard permission was revoked. Enable AMP in System Settings > Privacy & Security > Accessibility, then press Start again.".into());
        }
        None
    }
}

impl KeySimulator {
    pub fn new() -> Self {
        Self {
            output: None,
            owners: std::array::from_fn(|_| HashSet::new()),
            error: None,
            observation: ServiceStatus::default(),
            hold_delay: Duration::from_millis(crate::settings::defaults::value("/keyPressDelayMs")),
            pressed_keys: HashSet::new(),
            last_press_time: None,
            pedals: PedalOutput::default(),
            wheel: ChordWheelOutput::default(),
            wheel_context: None,
            wheel_pages: HashMap::new(),
        }
    }

    pub fn prepare(&mut self) -> Result<(), String> {
        #[cfg(windows)]
        if self.output.is_some() { return Ok(()); }
        #[cfg(not(windows))]
        if let Some(output) = &self.output {
            // A created backend can lose its permission or portal session.
            // In particular, CGEventPost does not report Accessibility denial.
            if let Some(error) = output.error() {
                self.observation = ServiceStatus::with_detail(output.failure_state(), &error);
                return Err(error);
            }
            return Ok(());
        }
        #[cfg(target_os = "linux")]
        if wayland_session(std::env::var_os("WAYLAND_DISPLAY").as_deref(), std::env::var_os("XDG_SESSION_TYPE").as_deref()) {
            self.observation = ServiceStatus::new(ServiceState::PermissionRequired);
            return Err("Wayland keyboard output requires an authorized desktop keyboard session.".into());
        }
        self.output = Some(Box::new(EnigoOutput { enigo: Enigo::new(&Settings::default()).map_err(|error| {
            let state = if matches!(error, enigo::NewConError::NoPermission) { ServiceState::PermissionRequired } else { ServiceState::Unavailable };
            self.observation = ServiceStatus::with_detail(state, &error);
            #[cfg(target_os = "macos")]
            { format!("Keyboard output is unavailable: {error}. Enable Auto MIDI Player in System Settings > Privacy & Security > Accessibility, then press Start again.") }
            #[cfg(not(target_os = "macos"))]
            { error.to_string() }
        })?, #[cfg(windows)] wheel_window: None }));
        self.observation = ServiceStatus::new(ServiceState::Ready);
        Ok(())
    }

    pub fn set_output(&mut self, output: Option<Box<dyn KeyboardOutput>>) {
        self.release_everything();
        self.output = output;
        self.wheel.retire();
        // Failed releases belong to the retired backend. A replacement must
        // not inherit ownership for keys that it has never pressed.
        #[cfg(not(windows))]
        for keys in &mut self.owners { keys.clear(); }
        self.error = None;
        if self.output.is_some() { self.observation = ServiceStatus::new(ServiceState::Ready); }
        else if self.observation.state == ServiceState::Ready { self.observation = ServiceStatus::new(ServiceState::Inactive); }
    }

    pub fn observe(&mut self, status: ServiceStatus) { self.observation = status; }
    pub fn status(&self) -> ServiceStatus { self.observation.clone() }

    pub fn take_error(&mut self) -> Option<String> {
        let error = self.error.take().or_else(|| self.output.as_ref().and_then(|o| o.error()));
        if let Some(error) = &error {
            let state = if self.observation.state == ServiceState::PermissionRequired { ServiceState::PermissionRequired }
                else { self.output.as_ref().map_or(ServiceState::Unavailable, |output| output.failure_state()) };
            self.observation = ServiceStatus::with_detail(state, error);
        }
        error
    }

    pub fn is_ready(&self) -> bool {
        #[cfg(windows)]
        { self.output.as_ref().is_some_and(|output| output.error().is_none()) }
        #[cfg(not(windows))]
        {
        self.error.is_none() && self.output.as_ref().is_some_and(|output| output.error().is_none())
        }
    }

    // 0: song notes, 1: song pedals, 2: live notes, 3: live pedals, 4: previews.
    #[cfg(windows)]
    pub fn send_owned_key(&mut self, source: usize, key: Key, up: bool) {
        if up {
            if !self.owners[source].remove(&key) { return; }
        } else {
            self.owners[source].insert(key);
        }
        if self.owners.iter().enumerate().any(|(i, keys)| i != source && keys.contains(&key)) { return; }
        let result = self.prepare().and_then(|_| self.output.as_mut().unwrap().key(key, up));
        if let Err(error) = result { self.error = Some(error); }
    }

    #[cfg(not(windows))]
    pub fn send_owned_key(&mut self, source: usize, key: Key, up: bool) {
        if up && !self.owners[source].contains(&key) { return; }
        let shared = self.owners.iter().enumerate().any(|(i, keys)| i != source && keys.contains(&key));
        if shared {
            if up { self.owners[source].remove(&key); }
            else { self.owners[source].insert(key); }
            return;
        }
        let result = if up {
            // Revocation blocks new presses, but cleanup should still attempt
            // a key-up on the backend that owns the hold.
            self.output.as_mut().map_or(Ok(()), |output| output.key(key, true))
        } else {
            self.prepare().and_then(|_| self.output.as_mut().unwrap().key(key, false))
        };
        match result {
            Ok(()) => {
                if up { self.owners[source].remove(&key); }
                else { self.owners[source].insert(key); }
            }
            // Keep failed releases owned so stop/retirement can retry them.
            // Failed presses must not suppress another source's key-down.
            Err(error) => self.error = Some(error),
        }
    }

    pub fn release_source(&mut self, source: usize) {
        let keys: Vec<_> = self.owners[source].iter().copied().collect();
        for key in keys { self.send_owned_key(source, key, true); }
    }

    pub fn release_everything(&mut self) {
        self.cancel_wheel();
        for source in 0..5 { self.release_source(source); }
        self.pressed_keys.clear();
        self.pedals.clear();
    }

    /// Sets the minimum key hold delay in milliseconds; the initial value comes from defaults.json.
    pub fn set_hold_delay_ms(&mut self, ms: u64) {
        self.hold_delay = Duration::from_millis(ms);
    }

    /// Sends key down or key up using physical layout keys
    pub fn send_key(&mut self, key: Key, is_key_up: bool) {
        if is_key_up {
            self.pressed_keys.remove(&key);
        } else {
            self.pressed_keys.insert(key);
            self.last_press_time = Some(Instant::now());
        }
        self.send_owned_key(0, key, is_key_up);
    }

    pub fn press_stroke(&mut self, stroke: KeyStroke) {
        for (key, is_key_up) in stroke.press_events() {
            self.send_key(key, is_key_up);
        }
    }

    pub fn sync_pedals(&mut self, bindings: &[Option<KeyStroke>; 3], down: [bool; 3], enabled: bool) {
        for (key, is_key_up) in self.pedals.reconcile(bindings, down, enabled) {
            self.send_owned_key(1, key, is_key_up);
        }
    }

    /// Releases all currently held physical keys to ensure keys never get stuck
    pub fn release_all(&mut self, _all_keys: &[Key]) {
        self.cancel_wheel();
        self.release_source(0);
        self.release_source(1);
        self.pressed_keys.clear();
        self.pedals.clear();
    }

    pub fn prepare_wheel(&mut self, config: &ChordWheelConfigJson, processes: &[String], index: usize, owner: usize) {
        let result = self.prepare().and_then(|_| self.wheel.prepare(self.output.as_mut().unwrap().as_mut(),
            config, processes, Some(index), index / config.positions.len(), Some(owner), Instant::now()));
        if let Err(error) = result { self.cancel_wheel(); self.error = Some(error); }
    }

    pub fn strum_wheel(&mut self, owner: usize, hold_ms: u64) {
        let result = self.output.as_mut().ok_or_else(|| "Mouse output is unavailable".to_string())
            .and_then(|output| self.wheel.strum(output.as_mut(), owner, hold_ms, Instant::now()));
        if let Err(error) = result { self.cancel_wheel(); self.error = Some(error); }
    }
    pub fn pluck_wheel(&mut self, owner: usize, strokes: &[KeyStroke], hold_ms: u64) {
        let result = self.output.as_mut().ok_or_else(|| "String output is unavailable".to_string())
            .and_then(|output| self.wheel.pluck(output.as_mut(), owner, strokes, hold_ms, Instant::now()));
        if let Err(error) = result { self.cancel_wheel(); self.error = Some(error); }
    }

    pub fn wheel_preparation_ms(&self, config: &ChordWheelConfigJson, index: usize) -> u64 { self.wheel.preparation_ms(config, index) }
    pub fn wheel_ready(&self, owner: usize) -> bool { self.wheel.ready(owner, Instant::now()) }
    pub fn wheel_state(&self) -> super::chord_wheel::ChordWheelState { self.wheel.state() }
    pub fn set_wheel_context(&mut self, context: Option<String>) {
        if let Some(context) = context {
            if self.wheel_context.as_ref().is_some_and(|previous| previous != &context) {
                self.cancel_wheel();
                if let Some(previous) = &self.wheel_context { self.wheel_pages.insert(previous.clone(), self.wheel.state().page); }
                if let Some(Some(page)) = self.wheel_pages.get(&context) { self.wheel.assume_page(*page); }
                else { self.wheel.forget_page(); }
            }
            self.wheel_context = Some(context);
        }
    }
    pub fn set_wheel_page(&mut self, config: &ChordWheelConfigJson, processes: &[String], page: usize, send: bool) -> Result<(), String> {
        if page >= config.page_count { return Err("Chord page is unavailable".into()); }
        if self.wheel.is_active() { return Err("The chord wheel is busy".into()); }
        if !send { self.wheel.assume_page(page); return Ok(()); }
        self.prepare()?;
        let result = self.wheel.prepare(self.output.as_mut().unwrap().as_mut(), config, processes, None, page, None, Instant::now());
        if let Err(error) = &result { self.cancel_wheel(); self.error = Some(error.clone()); }
        result
    }

    pub fn tick_wheel(&mut self) {
        if let Some(output) = &mut self.output {
            if let Err(error) = self.wheel.tick(output.as_mut(), Instant::now()) {
                self.cancel_wheel(); self.error = Some(error);
            }
        }
    }

    pub fn wheel_active(&self) -> bool { self.wheel.is_active() }
    pub fn cancel_wheel(&mut self) {
        if let Some(output) = &mut self.output {
            if let Err(error) = self.wheel.cancel(output.as_mut()) { self.error = Some(error); }
        }
    }
}

impl Default for KeySimulator {
    fn default() -> Self {
        Self::new()
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::input::{char_to_key, parse_key_stroke};

    fn key(c: char) -> Key { char_to_key(c).unwrap() }

    #[test]
    fn wheel_process_target_accepts_game_aliases_and_custom_executable_paths() {
        let names = vec!["GameMain".into(), "Alternate.EXE".into(), "C:\\Games\\CustomGame.exe".into()];
        assert!(matches_game_process("C:\\Game\\GAMEMAIN.EXE", &names));
        assert!(matches_game_process("alternate.exe", &names));
        assert!(matches_game_process("D:\\Other\\CustomGame.EXE", &names));
        assert!(!matches_game_process("unrelated.exe", &names));
        assert!(!matches_game_process("GameMain.exe", &[]));
    }

    #[test]
    fn wheel_page_state_is_scoped_to_its_game_and_layout_context() {
        let mut simulator = KeySimulator::new();
        simulator.set_wheel_context(Some("game-a/wheel".into()));
        simulator.wheel.assume_page(2);
        simulator.set_wheel_context(None); // The same game switches to keyboard mode.
        simulator.set_wheel_context(Some("game-a/wheel".into()));
        assert_eq!(simulator.wheel_state().page, Some(2));
        simulator.set_wheel_context(Some("game-b/wheel".into()));
        assert_eq!(simulator.wheel_state().page, None);
        simulator.set_wheel_context(Some("game-a/wheel".into()));
        assert_eq!(simulator.wheel_state().page, Some(2));
    }

    #[test]
    fn wayland_detection_ignores_empty_display_and_accepts_session_type() {
        use std::ffi::OsStr;
        assert!(!wayland_session(None, None));
        assert!(!wayland_session(Some(OsStr::new("")), Some(OsStr::new("x11"))));
        assert!(wayland_session(Some(OsStr::new("wayland-0")), None));
        assert!(wayland_session(None, Some(OsStr::new("Wayland"))));
    }

    #[cfg(windows)]
    #[test]
    fn windows_preserves_existing_backend_prepare_and_failure_ownership() {
        use std::sync::{Arc, Mutex};
        struct Output(Arc<Mutex<Vec<(Key, bool)>>>);
        impl KeyboardOutput for Output {
            fn key(&mut self, key: Key, up: bool) -> Result<(), String> {
                self.0.lock().unwrap().push((key, up));
                if up { Ok(()) } else { Err("output failed".into()) }
            }
        }
        let events = Arc::new(Mutex::new(Vec::new()));
        let mut sim = KeySimulator::new();
        sim.set_output(Some(Box::new(Output(events.clone()))));
        let key = key('a');
        sim.send_owned_key(0, key, false);
        assert!(sim.owners[0].contains(&key));
        assert!(sim.is_ready(), "Windows readiness retains its backend health semantics");
        sim.send_owned_key(2, key, false);
        assert_eq!(*events.lock().unwrap(), [(key, false)]);
        sim.release_source(0);
        sim.release_source(2);
        assert_eq!(*events.lock().unwrap(), [(key, false), (key, true)]);
        assert_eq!(sim.take_error().as_deref(), Some("output failed"));

        struct Revoked;
        impl KeyboardOutput for Revoked {
            fn key(&mut self, _: Key, _: bool) -> Result<(), String> { Ok(()) }
            fn error(&self) -> Option<String> { Some("unavailable".into()) }
        }
        sim.set_output(Some(Box::new(Revoked)));
        assert!(sim.prepare().is_ok(), "Windows keeps existing-backend preparation unchanged");
        assert!(!sim.is_ready());
    }

    #[test]
    fn source_release_preserves_other_keys_and_reports_output_failure() {
        use std::sync::{Arc, Mutex};
        struct Output(Arc<Mutex<Vec<(Key, bool)>>>);
        impl KeyboardOutput for Output {
            fn key(&mut self, key: Key, up: bool) -> Result<(), String> {
                self.0.lock().unwrap().push((key, up)); Ok(())
            }
        }
        let events = Arc::new(Mutex::new(Vec::new()));
        let mut sim = KeySimulator::new();
        sim.set_output(Some(Box::new(Output(events.clone()))));
        let key = parse_key_stroke("a").unwrap().key;
        sim.send_key(key, false);
        sim.send_owned_key(2, key, false);
        sim.release_all(&[key]); // seek, pause, stop, EOF: song only
        assert_eq!(*events.lock().unwrap(), [(key, false)]);
        sim.send_owned_key(4, key, false);
        sim.release_source(2);
        assert_eq!(events.lock().unwrap().len(), 1);
        sim.release_source(4);
        assert_eq!(*events.lock().unwrap(), [(key, false), (key, true)]);
        struct Denied;
        impl KeyboardOutput for Denied {
            fn key(&mut self, _: Key, _: bool) -> Result<(), String> { Err("permission denied".into()) }
        }
        sim.set_output(Some(Box::new(Denied)));
        sim.send_owned_key(2, key, false);
        assert_eq!(sim.take_error().as_deref(), Some("permission denied"));
    }

    #[test]
    fn status_reads_are_passive_and_failures_survive_backend_retirement() {
        struct Failing;
        impl KeyboardOutput for Failing {
            fn key(&mut self, _: Key, _: bool) -> Result<(), String> { Err("output disconnected".into()) }
        }
        let mut sim = KeySimulator::new();
        for _ in 0..3 { assert_eq!(sim.status().state, ServiceState::NotChecked); }
        assert!(sim.output.is_none());
        sim.set_output(Some(Box::new(Failing)));
        assert_eq!(sim.status().state, ServiceState::Ready);
        sim.send_owned_key(2, key('a'), false);
        sim.take_error();
        sim.set_output(None);
        assert_eq!(sim.status().state, ServiceState::Unavailable);
        assert_eq!(sim.status().detail.as_deref(), Some("output disconnected"));
        sim.observe(ServiceStatus::new(ServiceState::PermissionRequired));
        assert_eq!(sim.status().state, ServiceState::PermissionRequired);
        assert!(sim.output.is_none());
    }

    #[cfg(not(windows))]
    #[test]
    fn prepare_rejects_revoked_permissions_without_sending_native_events() {
        struct Revoked;
        impl KeyboardOutput for Revoked {
            fn key(&mut self, _: Key, _: bool) -> Result<(), String> {
                panic!("revoked output must not receive key events");
            }
            fn error(&self) -> Option<String> { Some("keyboard permission revoked".into()) }
            fn failure_state(&self) -> ServiceState { ServiceState::PermissionRequired }
        }
        let mut sim = KeySimulator::new();
        sim.set_output(Some(Box::new(Revoked)));
        assert!(!sim.is_ready());
        assert_eq!(sim.prepare().unwrap_err(), "keyboard permission revoked");
        assert_eq!(sim.status().state, ServiceState::PermissionRequired);
        sim.send_owned_key(2, key('a'), false);
        assert_eq!(sim.take_error().as_deref(), Some("keyboard permission revoked"));
        assert!(sim.owners[2].is_empty());
    }

    #[cfg(not(windows))]
    #[test]
    fn failed_press_does_not_claim_a_key_or_suppress_another_source() {
        use std::sync::{Arc, Mutex};
        struct Output(Arc<Mutex<Vec<(Key, bool)>>>);
        impl KeyboardOutput for Output {
            fn key(&mut self, key: Key, up: bool) -> Result<(), String> {
                let mut events = self.0.lock().unwrap();
                events.push((key, up));
                if events.len() == 1 { Err("temporary output failure".into()) }
                else { Ok(()) }
            }
        }
        let events = Arc::new(Mutex::new(Vec::new()));
        let mut sim = KeySimulator::new();
        sim.set_output(Some(Box::new(Output(events.clone()))));
        let key = key('a');
        sim.send_owned_key(0, key, false);
        assert!(!sim.is_ready());
        assert!(sim.owners[0].is_empty());
        sim.send_owned_key(2, key, false);
        sim.release_source(2);
        assert_eq!(*events.lock().unwrap(), [(key, false), (key, false), (key, true)]);
        assert_eq!(sim.take_error().as_deref(), Some("temporary output failure"));
        assert!(sim.is_ready());
    }

    #[cfg(not(windows))]
    #[test]
    fn failed_release_is_retried_before_backend_replacement() {
        use std::sync::{Arc, Mutex};
        struct Output(Arc<Mutex<Vec<(Key, bool)>>>);
        impl KeyboardOutput for Output {
            fn key(&mut self, key: Key, up: bool) -> Result<(), String> {
                let mut events = self.0.lock().unwrap();
                events.push((key, up));
                if events.len() == 2 { Err("temporary release failure".into()) }
                else { Ok(()) }
            }
        }
        let events = Arc::new(Mutex::new(Vec::new()));
        let mut sim = KeySimulator::new();
        sim.set_output(Some(Box::new(Output(events.clone()))));
        let key = key('a');
        sim.send_owned_key(0, key, false);
        sim.release_source(0);
        assert!(sim.owners[0].contains(&key));
        sim.set_output(None);
        assert_eq!(*events.lock().unwrap(), [(key, false), (key, true), (key, true)]);
        assert!(sim.owners.iter().all(HashSet::is_empty));
    }

    #[cfg(not(windows))]
    #[test]
    fn replacement_does_not_inherit_unreleased_keys_from_the_old_backend() {
        struct Disconnected;
        impl KeyboardOutput for Disconnected {
            fn key(&mut self, _: Key, up: bool) -> Result<(), String> {
                if up { Err("disconnected".into()) } else { Ok(()) }
            }
        }
        let mut sim = KeySimulator::new();
        sim.set_output(Some(Box::new(Disconnected)));
        sim.send_owned_key(0, key('a'), false);
        sim.set_output(None);
        assert!(sim.owners.iter().all(HashSet::is_empty));
    }

    fn bindings() -> [Option<KeyStroke>; 3] {
        ["space", "Enter", "^q"].map(parse_key_stroke)
    }

    #[test]
    fn game_pedals_hold_until_cc_release_instead_of_using_note_tap_deadlines() {
        let mut output = PedalOutput::default();
        assert_eq!(output.reconcile(&bindings(), [true, false, false], true), vec![(Key::Space, false)]);
        // Repeated control messages and playback ticks must not retrigger the pedal.
        assert!(output.reconcile(&bindings(), [true, false, false], true).is_empty());
        assert_eq!(output.reconcile(&bindings(), [true, true, false], true), vec![(Key::Return, false)]);
        assert_eq!(output.reconcile(&bindings(), [false, true, false], true), vec![(Key::Space, true)]);
        assert_eq!(output.reconcile(&bindings(), [false, false, true], true), vec![
            (Key::Return, true), (Key::Control, false), (key('q'), false), (Key::Control, true),
        ]);
        assert_eq!(output.reconcile(&bindings(), [false; 3], true), vec![(key('q'), true)]);
    }

    #[test]
    fn disabling_game_output_releases_pedals_and_reenabling_restores_the_current_midi_state() {
        let mut output = PedalOutput::default();
        let down = [true, false, false];
        assert_eq!(output.reconcile(&bindings(), down, true), vec![(Key::Space, false)]);
        // Used for the pedal toggle, pause, stopped seeks, Listen Mode and disabled input.
        assert_eq!(output.reconcile(&bindings(), down, false), vec![(Key::Space, true)]);
        assert!(output.reconcile(&bindings(), down, false).is_empty());
        assert_eq!(output.reconcile(&bindings(), down, true), vec![(Key::Space, false)]);
        // A seek past the pedal-up, stop or song completion clears the hold.
        assert_eq!(output.reconcile(&bindings(), [false; 3], true), vec![(Key::Space, true)]);
    }

    #[test]
    fn absent_bindings_are_ignored_and_shared_pedal_keys_release_only_after_all_owners() {
        let mut output = PedalOutput::default();
        assert!(output.reconcile(&[None; 3], [true; 3], true).is_empty());
        let shared = [parse_key_stroke("space"), parse_key_stroke("space"), None];
        assert_eq!(output.reconcile(&shared, [true, true, false], true), vec![(Key::Space, false)]);
        assert!(output.reconcile(&shared, [false, true, false], true).is_empty());
        assert_eq!(output.reconcile(&shared, [false; 3], true), vec![(Key::Space, true)]);
    }

    #[test]
    fn changing_layouts_releases_the_old_pedal_before_pressing_the_new_binding() {
        let mut output = PedalOutput::default();
        output.reconcile(&bindings(), [true, false, false], true);
        let replacement = [parse_key_stroke("Tab"), None, None];
        assert_eq!(output.reconcile(&replacement, [true, false, false], true), vec![
            (Key::Space, true), (Key::Tab, false),
        ]);
        assert_eq!(output.reconcile(&[None; 3], [true; 3], true), vec![(Key::Tab, true)]);
    }

    #[test]
    fn releasing_all_keys_invalidates_cached_holds_for_seek_and_resume() {
        let mut output = PedalOutput::default();
        let down = [true, false, false];
        output.reconcile(&bindings(), down, true);
        output.clear(); // The simulator has sent key-up for every mapped key.
        assert_eq!(output.reconcile(&bindings(), down, true), vec![(Key::Space, false)]);
    }
}
