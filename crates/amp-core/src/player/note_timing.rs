use std::collections::{HashMap, VecDeque};
use std::time::{Duration, Instant};
use enigo::Key;
use crate::midi::TimedNoteEvent;
use crate::input::KeyStroke;

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

/// Source notes whose NoteOn was processed before a seek/resume boundary, but
/// whose matching NoteOff has not been processed. Preserve FIFO ownership even
/// for skipped taps so their future NoteOff cannot consume a newer note owner.
pub(super) fn pending_note_owners(events: &[TimedNoteEvent], end_index: usize) -> Vec<usize> {
    let mut pending = HashMap::<(usize, u8, u8), VecDeque<usize>>::new();
    for (index, event) in events[..end_index].iter().enumerate() {
        let owners = pending.entry((event.track_index, event.channel, event.note)).or_default();
        if event.is_note_on && event.velocity > 0 {
            owners.push_back(index);
        } else {
            owners.pop_front();
        }
    }
    let mut owners: Vec<_> = pending.into_values().flatten().collect();
    owners.sort_unstable();
    owners
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
pub(super) enum KeyPressAction { Press, Retrigger, Deferred, Ignore }

// By default, leave the key up for longer than a 60 Hz game frame. Back-to-back key-up/down
// can otherwise be observed as one uninterrupted hold by a polling game.
const REPRESS_GAP: Duration = Duration::from_millis(20);

struct PendingPress {
    stroke: KeyStroke,
    at: Instant,
    owners: Vec<usize>,
}

/// Re-pressed keys retain every active note owner. A NoteOff or tap deadline
/// releases the physical key only after all owners finish; ignored overlaps
/// never acquire ownership.
pub(super) struct KeyReleases {
    pressed: HashMap<Key, HashMap<usize, Option<Instant>>>,
    pending: HashMap<Key, PendingPress>,
    repress_gap: Duration,
}

impl Default for KeyReleases {
    fn default() -> Self {
        Self { pressed: HashMap::new(), pending: HashMap::new(), repress_gap: REPRESS_GAP }
    }
}

impl KeyReleases {
    pub fn set_repress_gap_ms(&mut self, ms: u64) {
        self.repress_gap = Duration::from_millis(ms.min(1000));
    }

    /// Schedule physical output without sleeping the playback thread. The returned
    /// time is the scheduled press time, also used to start the new note's tap timer.
    pub fn begin_stroke(&mut self, stroke: KeyStroke, owner: usize, now: Instant,
        deadline: Option<Instant>, behavior: OverlapBehavior) -> (KeyPressAction, Instant) {
        let action = self.begin_press(stroke.key, owner, deadline, behavior);
        if action != KeyPressAction::Retrigger { return (action, now); }
        let already_pending = self.pending.contains_key(&stroke.key);
        let pending = self.pending.entry(stroke.key).or_insert(PendingPress {
            stroke, at: now + self.repress_gap, owners: Vec::new(),
        });
        // Simultaneous owners share one physical attack; later arrivals must not
        // keep postponing it. Use the latest stroke's modifiers for the attack.
        pending.stroke = stroke;
        if !pending.owners.contains(&owner) { pending.owners.push(owner); }
        let at = pending.at;
        if let Some(deadline) = deadline {
            self.retime(stroke.key, owner, at + deadline.saturating_duration_since(now));
        }
        (if already_pending { KeyPressAction::Deferred } else { action }, at)
    }

    pub fn take_presses(&mut self, now: Instant) -> Vec<(KeyStroke, Vec<usize>)> {
        let mut due = Vec::new();
        self.pending.retain(|key, press| {
            if press.at > now { return true; }
            // Even if the worker wakes late, a tap still gets its full hold time
            // after key-down. Older owners keep their original release times.
            if let Some(owners) = self.pressed.get_mut(key) {
                for owner in &press.owners {
                    if let Some(Some(deadline)) = owners.get_mut(owner) {
                        *deadline += now.saturating_duration_since(press.at);
                    }
                }
                due.push((press.stroke, std::mem::take(&mut press.owners)));
            }
            false
        });
        due
    }

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
        self.pending.remove(&key);
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
        self.pending.retain(|key, _| self.pressed.contains_key(key));
        due
    }

    pub fn next_deadline(&self) -> Option<Instant> {
        self.pressed.values().flat_map(|owners| owners.values().copied().flatten())
            .chain(self.pending.values().map(|press| press.at)).min()
    }

    pub fn clear(&mut self) { self.pressed.clear(); self.pending.clear(); }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::time::Duration;

    #[test]
    fn configured_repress_gap_controls_output_and_preserves_the_original_hold() {
        let now = Instant::now();
        let stroke = KeyStroke::plain(Key::A);
        for (ms, expected) in [(0, 0), (1, 1), (65, 65), (1000, 1000), (2000, 1000)] {
            let mut releases = KeyReleases::default();
            releases.set_repress_gap_ms(ms);
            releases.begin_stroke(stroke, 1, now, None, OverlapBehavior::Retrigger);
            let (action, press_at) = releases.begin_stroke(stroke, 2, now, None, OverlapBehavior::Retrigger);
            assert_eq!(action, KeyPressAction::Retrigger);
            assert_eq!(press_at, now + Duration::from_millis(expected));
            if expected > 0 {
                assert!(releases.take_presses(press_at - Duration::from_micros(1)).is_empty());
            }
            assert_eq!(releases.take_presses(press_at), vec![(stroke, vec![2])]);
            assert!(!releases.note_off(Key::A, 2));
            assert!(releases.note_off(Key::A, 1));
            releases.clear();
            releases.begin_stroke(stroke, 3, now, None, OverlapBehavior::Retrigger);
            assert_eq!(releases.begin_stroke(stroke, 4, now, None, OverlapBehavior::Retrigger).1, press_at,
                "clearing playback state must preserve the configured gap");
        }
    }

    #[test]
    fn seek_owners_keep_fifo_order_for_same_pitch_overlaps_and_separate_tracks() {
        let event = |time_us, track_index, channel, is_note_on| TimedNoteEvent {
            time_us, track_index, channel, note: 60, is_note_on,
            velocity: if is_note_on { 80 } else { 0 },
        };
        let events = [
            event(0, 0, 0, true), event(10, 0, 0, true),
            event(20, 1, 0, true), event(30, 0, 1, true),
            event(40, 0, 0, false), event(50, 0, 0, false),
            event(60, 1, 0, false), event(70, 0, 1, false),
        ];
        assert_eq!(pending_note_owners(&events, 0), Vec::<usize>::new());
        assert_eq!(pending_note_owners(&events, 4), vec![0, 1, 2, 3]);
        assert_eq!(pending_note_owners(&events, 5), vec![1, 2, 3]);
        assert_eq!(pending_note_owners(&events, 6), vec![2, 3]);
        assert!(pending_note_owners(&events, 8).is_empty());
    }

    #[test]
    fn orange_c4_repress_has_a_release_gap_without_losing_the_42_second_owner() {
        // C4 from track 0: 47.442s..90.093s; track 1 overlaps at
        // 64.884s..65.545s and 65.581s..66.905s (same MIDI channel).
        let mut releases = KeyReleases::default();
        let stroke = KeyStroke::plain(Key::A);
        let origin = Instant::now();
        let at = |us| origin + Duration::from_micros(us);
        assert_eq!(releases.begin_stroke(stroke, 445, at(47_442_000), None, OverlapBehavior::Retrigger).0,
            KeyPressAction::Press);
        assert!(releases.take_due(at(64_883_682)).is_empty());
        for (owner, start, end) in [(614, 64_883_682, 65_545_019), (626, 65_581_356, 66_905_483)] {
            let (action, press_at) = releases.begin_stroke(stroke, owner, at(start), None, OverlapBehavior::Retrigger);
            assert_eq!(action, KeyPressAction::Retrigger); // send key-up now
            assert_eq!(press_at, at(start) + REPRESS_GAP);
            assert_eq!(releases.next_deadline(), Some(press_at));
            assert!(releases.take_presses(press_at - Duration::from_micros(1)).is_empty());
            assert_eq!(releases.take_presses(press_at), vec![(stroke, vec![owner])]);
            assert!(releases.take_due(at(end)).is_empty());
            assert!(!releases.note_off(stroke.key, owner), "short note must not cut off the original hold");
        }
        assert!(releases.take_due(at(90_092_999)).is_empty());
        assert!(releases.note_off(stroke.key, 445));
        assert!(releases.next_deadline().is_none());
    }

    #[test]
    fn deferred_repress_keeps_chords_immediate_and_tap_duration_starts_at_key_down() {
        let mut releases = KeyReleases::default();
        let now = Instant::now();
        let stroke = KeyStroke::plain(Key::A);
        let tap = Duration::from_millis(35);
        releases.begin_stroke(stroke, 1, now, None, OverlapBehavior::Retrigger);
        let (action, press_at) = releases.begin_stroke(stroke, 2, now, Some(now + tap), OverlapBehavior::Retrigger);
        assert_eq!(action, KeyPressAction::Retrigger);
        assert!(!releases.note_off(Key::A, 1));
        let chord = KeyStroke::plain(Key::B);
        assert_eq!(releases.begin_stroke(chord, 3, now, None, OverlapBehavior::Retrigger),
            (KeyPressAction::Press, now));
        assert!(releases.take_due(press_at).is_empty());
        assert_eq!(releases.take_presses(press_at), vec![(stroke, vec![2])]);
        assert!(releases.take_due(now + tap).is_empty());
        assert_eq!(releases.next_deadline(), Some(press_at + tap));
        assert_eq!(releases.take_due(press_at + tap), vec![Key::A]);
        assert!(releases.note_off(Key::B, 3));
    }

    #[test]
    fn simultaneous_overlaps_share_an_attack_without_extending_the_release_gap() {
        let mut releases = KeyReleases::default();
        let now = Instant::now();
        let stroke = KeyStroke::plain(Key::A);
        releases.begin_stroke(stroke, 1, now, None, OverlapBehavior::Retrigger);
        let (_, press_at) = releases.begin_stroke(stroke, 2, now, None, OverlapBehavior::Retrigger);
        let shifted = KeyStroke { shift: true, ..stroke };
        let later = now + Duration::from_millis(10);
        assert_eq!(releases.begin_stroke(shifted, 3, later, None, OverlapBehavior::Retrigger),
            (KeyPressAction::Deferred, press_at));
        assert_eq!(releases.next_deadline(), Some(press_at));
        assert_eq!(releases.take_presses(press_at), vec![(shifted, vec![2, 3])]);
        assert!(releases.take_presses(press_at).is_empty());
        assert!(!releases.note_off(Key::A, 1));
        assert!(!releases.note_off(Key::A, 3));
        assert!(releases.note_off(Key::A, 2));
    }

    #[test]
    fn stopped_or_finished_owners_cancel_pending_attacks_and_hold_mode_still_ignores_overlaps() {
        let now = Instant::now();
        let stroke = KeyStroke::plain(Key::A);
        for cleanup in [0, 1, 2] {
            let mut releases = KeyReleases::default();
            releases.begin_stroke(stroke, 1, now, None, OverlapBehavior::Retrigger);
            releases.begin_stroke(stroke, 2, now, None, OverlapBehavior::Retrigger);
            match cleanup {
                0 => releases.clear(), // stop, pause, seek, panic, output or layout change
                1 => {
                    assert!(!releases.note_off(Key::A, 1));
                    assert!(releases.note_off(Key::A, 2));
                }
                _ => {
                    releases.retime(Key::A, 1, now);
                    releases.retime(Key::A, 2, now);
                    assert_eq!(releases.take_due(now), vec![Key::A]);
                }
            }
            assert!(releases.next_deadline().is_none());
            assert!(releases.take_presses(now + REPRESS_GAP).is_empty());
        }
        let mut releases = KeyReleases::default();
        releases.begin_stroke(stroke, 1, now, None, OverlapBehavior::Hold);
        assert_eq!(releases.begin_stroke(stroke, 2, now, None, OverlapBehavior::Hold).0, KeyPressAction::Ignore);
        assert!(releases.next_deadline().is_none());
        assert!(releases.take_presses(now + REPRESS_GAP).is_empty());
        assert!(releases.note_off(Key::A, 1));
    }

    #[test]
    fn late_worker_wakeup_preserves_a_repressed_taps_full_hold_time() {
        let mut releases = KeyReleases::default();
        let now = Instant::now();
        let stroke = KeyStroke::plain(Key::A);
        let tap = Duration::from_millis(20);
        releases.begin_stroke(stroke, 1, now, None, OverlapBehavior::Retrigger);
        releases.begin_stroke(stroke, 2, now, Some(now + tap), OverlapBehavior::Retrigger);
        assert!(!releases.note_off(Key::A, 1));
        let late = now + Duration::from_millis(100);
        assert_eq!(releases.take_presses(late), vec![(stroke, vec![2])]);
        assert!(releases.take_due(late).is_empty());
        assert_eq!(releases.next_deadline(), Some(late + tap));
        assert_eq!(releases.take_due(late + tap), vec![Key::A]);
    }

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
