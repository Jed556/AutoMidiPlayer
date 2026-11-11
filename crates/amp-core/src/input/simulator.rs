use enigo::{Direction, Enigo, Key, Keyboard, Settings};
use std::collections::HashSet;
use std::time::{Duration, Instant};
use super::mapping::KeyStroke;
use crate::diagnostics::{ServiceState, ServiceStatus};

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
}

/// One backend owns the physical keyboard. Wayland supplies a portal backend;
/// other desktops initialize Enigo only when output is requested.
pub trait KeyboardOutput: Send {
    fn key(&mut self, key: Key, up: bool) -> Result<(), String>;
    fn error(&self) -> Option<String> { None }
    fn failure_state(&self) -> ServiceState { ServiceState::Unavailable }
}

struct EnigoOutput(Enigo);
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
        self.0.key(key, if up { Direction::Release } else { Direction::Press }).map_err(|e| e.to_string())
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
        }
    }

    pub fn prepare(&mut self) -> Result<(), String> {
        if self.output.is_some() { return Ok(()); }
        #[cfg(target_os = "linux")]
        if std::env::var_os("WAYLAND_DISPLAY").is_some() {
            self.observation = ServiceStatus::new(ServiceState::PermissionRequired);
            return Err("Wayland keyboard output requires an authorized MIDI input session.".into());
        }
        self.output = Some(Box::new(EnigoOutput(Enigo::new(&Settings::default()).map_err(|error| {
            let state = if matches!(error, enigo::NewConError::NoPermission) { ServiceState::PermissionRequired } else { ServiceState::Unavailable };
            self.observation = ServiceStatus::with_detail(state, &error);
            #[cfg(target_os = "macos")]
            { format!("Keyboard output is unavailable: {error}. Enable Auto MIDI Player in System Settings > Privacy & Security > Accessibility, then press Start again.") }
            #[cfg(not(target_os = "macos"))]
            { error.to_string() }
        })?)));
        self.observation = ServiceStatus::new(ServiceState::Ready);
        Ok(())
    }

    pub fn set_output(&mut self, output: Option<Box<dyn KeyboardOutput>>) {
        self.release_everything();
        self.output = output;
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
        self.output.as_ref().is_some_and(|output| output.error().is_none())
    }

    // 0: song notes, 1: song pedals, 2: live notes, 3: live pedals, 4: previews.
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

    pub fn release_source(&mut self, source: usize) {
        let keys: Vec<_> = self.owners[source].iter().copied().collect();
        for key in keys { self.send_owned_key(source, key, true); }
    }

    pub fn release_everything(&mut self) {
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
        self.release_source(0);
        self.release_source(1);
        self.pressed_keys.clear();
        self.pedals.clear();
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
    use crate::input::parse_key_stroke;

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
        sim.send_owned_key(2, Key::A, false);
        sim.take_error();
        sim.set_output(None);
        assert_eq!(sim.status().state, ServiceState::Unavailable);
        assert_eq!(sim.status().detail.as_deref(), Some("output disconnected"));
        sim.observe(ServiceStatus::new(ServiceState::PermissionRequired));
        assert_eq!(sim.status().state, ServiceState::PermissionRequired);
        assert!(sim.output.is_none());
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
            (Key::Return, true), (Key::Control, false), (Key::Q, false), (Key::Control, true),
        ]);
        assert_eq!(output.reconcile(&bindings(), [false; 3], true), vec![(Key::Q, true)]);
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
