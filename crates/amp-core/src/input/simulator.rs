use enigo::{Direction, Enigo, Key, Keyboard, Settings};
use std::collections::HashSet;
use std::time::{Duration, Instant};
use super::mapping::KeyStroke;

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
    enigo: Enigo,
    hold_delay: Duration,
    pressed_keys: HashSet<Key>,
    last_press_time: Option<Instant>,
    pedals: PedalOutput,
}

impl KeySimulator {
    pub fn new() -> Self {
        let enigo = Enigo::new(&Settings::default()).expect("Failed to initialize Enigo input simulator");
        Self {
            enigo,
            hold_delay: Duration::from_millis(crate::settings::defaults::value("/keyPressDelayMs")),
            pressed_keys: HashSet::new(),
            last_press_time: None,
            pedals: PedalOutput::default(),
        }
    }

    /// Sets the minimum key hold delay in milliseconds; the initial value comes from defaults.json.
    pub fn set_hold_delay_ms(&mut self, ms: u64) {
        self.hold_delay = Duration::from_millis(ms);
    }

    /// Sends key down or key up using physical layout keys
    pub fn send_key(&mut self, key: Key, is_key_up: bool) {
        if is_key_up {
            self.pressed_keys.remove(&key);
            let _ = self.enigo.key(key, Direction::Release);
        } else {
            self.pressed_keys.insert(key);
            self.last_press_time = Some(Instant::now());
            let _ = self.enigo.key(key, Direction::Press);
        }
    }

    pub fn press_stroke(&mut self, stroke: KeyStroke) {
        for (key, is_key_up) in stroke.press_events() {
            self.send_key(key, is_key_up);
        }
    }

    pub fn sync_pedals(&mut self, bindings: &[Option<KeyStroke>; 3], down: [bool; 3], enabled: bool) {
        for (key, is_key_up) in self.pedals.reconcile(bindings, down, enabled) {
            self.send_key(key, is_key_up);
        }
    }

    /// Releases all currently held physical keys to ensure keys never get stuck
    pub fn release_all(&mut self, all_keys: &[Key]) {
        for &key in all_keys {
            let _ = self.enigo.key(key, Direction::Release);
        }
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
