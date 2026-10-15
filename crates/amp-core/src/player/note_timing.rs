use std::collections::{HashMap, VecDeque};
use std::time::Instant;
use enigo::Key;
use crate::midi::TimedNoteEvent;

/// Pair before playback so short notes can be classified at NoteOn, including after seeks.
pub fn note_durations(events: &[TimedNoteEvent]) -> Vec<Option<u64>> {
    let mut durations = vec![None; events.len()];
    let mut pending = HashMap::<(usize, u8, u8), VecDeque<usize>>::new();
    for (index, event) in events.iter().enumerate() {
        let notes = pending.entry((event.track_index, event.channel, event.note)).or_default();
        if event.is_note_on && event.velocity > 0 {
            notes.push_back(index);
        } else if let Some(start) = notes.pop_front() {
            durations[start] = Some(event.time_us.saturating_sub(events[start].time_us));
        }
    }
    durations
}

pub(super) fn is_tap(hold_notes: bool, threshold_ms: u64, duration_us: Option<u64>, speed: f64) -> bool {
    // Use the same 200ms fallback as Studio for an unpaired MIDI NoteOn.
    !hold_notes || duration_us.unwrap_or(200_000) as f64 / speed <= threshold_ms as f64 * 1000.0
}

#[derive(Debug, Default, Clone, Copy, PartialEq, Eq, serde::Serialize, serde::Deserialize)]
#[serde(rename_all = "lowercase")]
pub enum OverlapBehavior {
    Hold,
    #[default]
    Retrigger,
}

#[derive(Debug, PartialEq, Eq)]
pub(super) enum KeyPressAction { Press, Retrigger, Ignore }

/// Re-pressed keys retain every active note owner. A NoteOff or tap deadline
/// releases the physical key only after all owners finish; ignored overlaps
/// never acquire ownership.
#[derive(Default)]
pub(super) struct KeyReleases {
    pressed: HashMap<Key, HashMap<usize, Option<Instant>>>,
}

impl KeyReleases {
    pub fn begin_press(&mut self, key: Key, owner: usize, deadline: Option<Instant>, behavior: OverlapBehavior) -> KeyPressAction {
        if self.pressed.contains_key(&key) && behavior == OverlapBehavior::Hold {
            return KeyPressAction::Ignore;
        }
        if self.press(key, owner, deadline) { KeyPressAction::Retrigger } else { KeyPressAction::Press }
    }

    pub fn press(&mut self, key: Key, owner: usize, deadline: Option<Instant>) -> bool {
        let owners = self.pressed.entry(key).or_default();
        let was_pressed = !owners.is_empty();
        owners.insert(owner, deadline);
        was_pressed
    }

    pub fn note_off(&mut self, key: Key, owner: usize) -> bool {
        let Some(owners) = self.pressed.get_mut(&key) else { return false };
        if !owners.get(&owner).is_some_and(Option::is_none) { return false; }
        owners.remove(&owner);
        if !owners.is_empty() { return false; }
        self.pressed.remove(&key);
        true
    }

    pub fn retime(&mut self, key: Key, owner: usize, deadline: Instant) {
        if let Some(at) = self.pressed.get_mut(&key).and_then(|owners| owners.get_mut(&owner)) {
            *at = Some(deadline);
        }
    }

    pub fn take_due(&mut self, now: Instant) -> Vec<Key> {
        let mut due = Vec::new();
        self.pressed.retain(|&key, owners| {
            owners.retain(|_, deadline| !deadline.is_some_and(|at| at <= now));
            if owners.is_empty() { due.push(key); }
            !owners.is_empty()
        });
        due
    }

    pub fn next_deadline(&self) -> Option<Instant> {
        self.pressed.values().flat_map(|owners| owners.values().copied().flatten()).min()
    }

    pub fn clear(&mut self) { self.pressed.clear(); }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::time::Duration;

    #[test]
    fn keep_held_ignores_overlaps_without_changing_owner_or_deadline() {
        let mut releases = KeyReleases::default();
        let key = Key::A;
        let now = Instant::now();
        assert_eq!(releases.begin_press(key, 1, None, OverlapBehavior::Hold), KeyPressAction::Press);
        assert_eq!(releases.begin_press(key, 2, Some(now), OverlapBehavior::Hold), KeyPressAction::Ignore);
        assert!(releases.take_due(now).is_empty());
        assert!(!releases.note_off(key, 2));
        assert!(releases.note_off(key, 1));
        // An ignored note still sounding must not prevent the next real press.
        assert_eq!(releases.begin_press(key, 3, None, OverlapBehavior::Hold), KeyPressAction::Press);
        assert!(!releases.note_off(key, 2));
        assert!(releases.note_off(key, 3));
    }

    #[test]
    fn retrigger_keeps_key_held_until_all_overlaps_end_in_any_order() {
        for order in [[1, 2, 3], [1, 3, 2], [2, 1, 3], [2, 3, 1], [3, 1, 2], [3, 2, 1]] {
            let mut releases = KeyReleases::default();
            assert_eq!(releases.begin_press(Key::A, 1, None, OverlapBehavior::Retrigger), KeyPressAction::Press);
            assert_eq!(releases.begin_press(Key::A, 2, None, OverlapBehavior::Retrigger), KeyPressAction::Retrigger);
            assert_eq!(releases.begin_press(Key::A, 3, None, OverlapBehavior::Retrigger), KeyPressAction::Retrigger);
            for (index, owner) in order.into_iter().enumerate() {
                assert_eq!(releases.note_off(Key::A, owner), index == 2, "release order {order:?}");
                assert!(!releases.note_off(Key::A, owner), "duplicate NoteOff must not release another owner");
            }
        }
    }

    #[test]
    fn changing_overlap_behavior_applies_to_next_press_and_preserves_other_chord_keys() {
        let mut releases = KeyReleases::default();
        let now = Instant::now();
        releases.begin_press(Key::A, 1, Some(now), OverlapBehavior::Hold);
        releases.begin_press(Key::B, 2, None, OverlapBehavior::Hold);
        assert_eq!(releases.begin_press(Key::A, 3, None, OverlapBehavior::Retrigger), KeyPressAction::Retrigger);
        assert_eq!(releases.begin_press(Key::A, 4, None, OverlapBehavior::Hold), KeyPressAction::Ignore);
        assert!(releases.take_due(now).is_empty());
        assert!(releases.note_off(Key::B, 2));
        assert!(releases.note_off(Key::A, 3));
        releases.clear();
        assert_eq!(releases.begin_press(Key::A, 5, None, OverlapBehavior::Hold), KeyPressAction::Press);
    }

    #[test]
    fn threshold_includes_boundary_and_uses_playback_speed() {
        assert!(is_tap(false, 0, Some(5_000_000), 1.0));
        assert!(is_tap(true, 100, Some(100_000), 1.0));
        assert!(!is_tap(true, 100, Some(100_001), 1.0));
        assert!(is_tap(true, 100, Some(200_000), 2.0));
        assert!(!is_tap(true, 0, Some(1), 1.0));
        assert!(!is_tap(true, 100, None, 1.0));
        assert!(is_tap(true, 200, None, 1.0));
    }

    #[test]
    fn taps_ignore_note_off_and_expire_in_wall_clock_time() {
        let mut releases = KeyReleases::default();
        let key = Key::Unicode('a');
        let now = Instant::now();
        releases.press(key, 1, Some(now + Duration::from_millis(35)));
        assert!(!releases.note_off(key, 1));
        assert!(releases.take_due(now + Duration::from_millis(34)).is_empty());
        assert_eq!(releases.take_due(now + Duration::from_millis(35)), vec![key]);
        assert!(releases.next_deadline().is_none());
    }

    #[test]
    fn expired_tap_and_stale_note_off_do_not_release_a_remaining_hold() {
        let mut releases = KeyReleases::default();
        let key = Key::Unicode('a');
        let now = Instant::now();
        assert!(!releases.press(key, 1, Some(now)));
        assert!(releases.press(key, 2, None));
        assert!(releases.take_due(now + Duration::from_secs(1)).is_empty());
        assert!(!releases.note_off(key, 1));
        assert!(releases.note_off(key, 2));
        releases.press(key, 3, Some(now));
        releases.clear();
        assert!(releases.take_due(now).is_empty());
    }

    #[test]
    fn chord_keys_release_independently_and_zero_delay_is_immediate() {
        let mut releases = KeyReleases::default();
        let now = Instant::now();
        let tap = Key::Unicode('a');
        let hold = Key::Unicode('b');
        releases.press(tap, 1, Some(now));
        releases.press(hold, 2, None);
        assert_eq!(releases.take_due(now), vec![tap]);
        assert!(!releases.note_off(hold, 1));
        assert!(releases.note_off(hold, 2));
    }

    #[test]
    fn live_threshold_changes_retime_each_overlapping_note_independently() {
        let mut releases = KeyReleases::default();
        let now = Instant::now();
        releases.press(Key::Q, 1, None);
        releases.press(Key::Q, 2, None);
        releases.retime(Key::Q, 1, now);
        assert!(releases.take_due(now).is_empty());
        assert!(!releases.note_off(Key::Q, 1));
        releases.retime(Key::Q, 2, now);
        assert_eq!(releases.take_due(now), vec![Key::Q]);
    }

    #[test]
    fn overlapping_taps_release_only_after_the_last_deadline() {
        let mut releases = KeyReleases::default();
        let now = Instant::now();
        let early = now + Duration::from_millis(20);
        let late = now + Duration::from_millis(80);
        releases.begin_press(Key::A, 1, Some(late), OverlapBehavior::Retrigger);
        releases.begin_press(Key::A, 2, Some(early), OverlapBehavior::Retrigger);
        assert_eq!(releases.next_deadline(), Some(early));
        assert!(!releases.note_off(Key::A, 2));
        assert!(releases.take_due(early).is_empty());
        assert_eq!(releases.next_deadline(), Some(late));
        assert_eq!(releases.take_due(late), vec![Key::A]);
        assert!(releases.next_deadline().is_none());
    }

    #[test]
    fn short_retrigger_tap_does_not_cut_off_an_older_hold() {
        let mut releases = KeyReleases::default();
        let now = Instant::now();
        releases.begin_press(Key::A, 1, None, OverlapBehavior::Retrigger);
        releases.begin_press(Key::A, 2, Some(now), OverlapBehavior::Retrigger);
        assert!(releases.take_due(now).is_empty());
        assert!(!releases.note_off(Key::A, 2));
        assert!(releases.note_off(Key::A, 1));
    }

    #[test]
    fn durations_pair_overlapping_notes_by_track_channel_and_pitch() {
        let event = |time_us, track_index, channel, is_note_on, velocity| TimedNoteEvent {
            time_us, track_index, channel, note: 60, is_note_on, velocity,
        };
        let durations = note_durations(&[
            event(0, 0, 0, true, 80), event(10, 0, 0, true, 80),
            event(20, 1, 0, true, 80), event(30, 0, 1, true, 80),
            event(40, 0, 0, false, 0), event(60, 0, 0, true, 0),
            event(90, 1, 0, false, 0), event(100, 0, 1, false, 0),
        ]);
        assert_eq!(&durations[..4], &[Some(40), Some(50), Some(70), Some(70)]);
    }
}
