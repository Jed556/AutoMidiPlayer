//! Song-only note preparation. The source MIDI and its control events remain unchanged.
use std::collections::{HashMap, VecDeque};

use crate::{input::{KeyStroke, mapping::ChordPad}, midi::TimedNoteEvent};

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct NoteProcessingSettings {
    pub merge_notes: bool,
    pub merge_tolerance_ms: u64,
    pub auto_chord: bool,
    pub chord_detection_milliseconds: u64,
}

impl Default for NoteProcessingSettings {
    fn default() -> Self {
        Self {
            merge_notes: crate::settings::defaults::value("/defaultMergeNotes"),
            merge_tolerance_ms: crate::settings::defaults::value("/defaultMergeToleranceMs"),
            auto_chord: crate::settings::defaults::value("/defaultAutoChord"),
            chord_detection_milliseconds: crate::settings::defaults::value("/defaultChordDetectionMilliseconds"),
        }
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub(super) struct ChordNote {
    pub chord_id: usize,
    pub stroke: KeyStroke,
    /// Only the first NoteOn presses the pad. Every other member is suppressed.
    pub trigger: bool,
}

/// Both sides of each pair use the same FIFO identity as the playback engine.
/// Unpaired MIDI events deliberately have no partner and are never removed.
fn partners(events: &[TimedNoteEvent]) -> Vec<Option<usize>> {
    let mut partners = vec![None; events.len()];
    let mut pending = HashMap::<(usize, u8, u8), VecDeque<usize>>::new();
    for (index, event) in events.iter().enumerate() {
        let pending = pending.entry((event.track_index, event.channel, event.note)).or_default();
        if event.is_note_on && event.velocity > 0 {
            pending.push_back(index);
        } else if let Some(start) = pending.pop_front() {
            partners[start] = Some(index);
            partners[index] = Some(start);
        }
    }
    partners
}

/// Merge complete, same-pitch notes within each original track and MIDI channel.
/// The first attack and furthest release survive; attack velocities are averaged
/// as in legacy. All surviving events retain their source order and timestamps.
pub(super) fn merge_notes(events: &[TimedNoteEvent], tolerance_ms: u64) -> Vec<TimedNoteEvent> {
    let partners = partners(events);
    let mut notes = HashMap::<(usize, u8, u8), Vec<(usize, usize)>>::new();
    for (index, event) in events.iter().enumerate() {
        if event.is_note_on && event.velocity > 0 {
            if let Some(end) = partners[index] {
                notes.entry((event.track_index, event.channel, event.note)).or_default().push((index, end));
            }
        }
    }
    let mut keep = vec![true; events.len()];
    let mut velocities = HashMap::new();
    let tolerance_us = tolerance_ms.saturating_mul(1000);
    for notes in notes.values() {
        let mut cursor = 0;
        while cursor < notes.len() {
            let (first, mut last) = notes[cursor];
            let mut velocity = u64::from(events[first].velocity);
            let mut count = 1;
            cursor += 1;
            while cursor < notes.len() {
                let (start, end) = notes[cursor];
                if events[start].time_us > events[last].time_us.saturating_add(tolerance_us) {
                    break;
                }
                keep[start] = false;
                velocity += u64::from(events[start].velocity);
                count += 1;
                if (events[end].time_us, end) >= (events[last].time_us, last) {
                    keep[last] = false;
                    last = end;
                } else {
                    keep[end] = false;
                }
                cursor += 1;
            }
            if count > 1 {
                velocities.insert(first, (velocity / count) as u8);
            }
        }
    }
    events.iter().enumerate().filter_map(|(index, event)| {
        if !keep[index] { return None; }
        let mut event = event.clone();
        if let Some(velocity) = velocities.get(&index) { event.velocity = *velocity; }
        Some(event)
    }).collect()
}

fn pitch_classes(notes: impl IntoIterator<Item = u8>) -> u16 {
    notes.into_iter().fold(0, |mask, pitch| mask | (1 << (pitch % 12)))
}

/// Plan pad replacement without modifying events used by the synthesizer or UI.
/// Chord membership spans enabled tracks, but only note starts within the first
/// member's tolerance form a group. Transposition/markers are evaluated at that
/// shared start time, matching legacy's chord-time interpretation.
pub(super) fn detect_chords<F, G>(
    events: &[TimedNoteEvent],
    pads: &[ChordPad],
    tolerance_ms: u64,
    enabled: F,
    transform: G,
) -> Vec<Option<ChordNote>>
where
    F: Fn(usize, &TimedNoteEvent) -> bool,
    G: Fn(u8, u64) -> Option<u8>,
{
    let mut result = vec![None; events.len()];
    if pads.is_empty() { return result; }
    let partners = partners(events);
    let candidates: Vec<_> = events.iter().enumerate().filter_map(|(index, event)| {
        (event.is_note_on && event.velocity > 0 && partners[index].is_some() && enabled(index, event))
            .then_some(index)
    }).collect();
    let pad_masks: Vec<_> = pads.iter().map(|pad| pitch_classes(pad.pitch_classes.iter().copied())).collect();
    let tolerance_us = tolerance_ms.saturating_mul(1000);
    let mut cursor = 0;
    let mut chord_id = 0;
    while cursor < candidates.len() {
        let first = cursor;
        let start_us = events[candidates[first]].time_us;
        cursor += 1;
        while cursor < candidates.len()
            && events[candidates[cursor]].time_us <= start_us.saturating_add(tolerance_us) {
            cursor += 1;
        }
        let members = &candidates[first..cursor];
        if members.len() < 3 { continue; }
        let Some(transformed) = members.iter().map(|index| transform(events[*index].note, start_us))
            .collect::<Option<Vec<_>>>() else { continue };
        let mask = pitch_classes(transformed);
        let Some(pad) = pad_masks.iter().position(|pad_mask| *pad_mask == mask && mask != 0) else { continue };
        for (member, &index) in members.iter().enumerate() {
            let assignment = ChordNote { chord_id, stroke: pads[pad].stroke, trigger: member == 0 };
            result[index] = Some(assignment);
            result[partners[index].expect("Complete candidate note")] = Some(ChordNote { trigger: false, ..assignment });
        }
        chord_id += 1;
    }
    result
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::input::parse_key_stroke;

    fn event(time_us: u64, note: u8, velocity: u8, is_note_on: bool, track_index: usize, channel: u8) -> TimedNoteEvent {
        TimedNoteEvent { time_us, note, velocity, is_note_on, channel, track_index }
    }

    fn note(events: &mut Vec<TimedNoteEvent>, start: u64, end: u64, pitch: u8, velocity: u8, track: usize, channel: u8) {
        events.push(event(start, pitch, velocity, true, track, channel));
        events.push(event(end, pitch, 0, false, track, channel));
    }

    fn sorted(mut events: Vec<TimedNoteEvent>) -> Vec<TimedNoteEvent> { events.sort(); events }

    fn pad(pitches: &[u8]) -> ChordPad {
        ChordPad { pitch_classes: pitches.to_vec(), stroke: parse_key_stroke("q").unwrap() }
    }

    #[test]
    fn merge_joins_overlap_and_tolerance_boundary_and_averages_attacks() {
        let mut events = Vec::new();
        note(&mut events, 0, 100_000, 60, 60, 0, 0);
        note(&mut events, 80_000, 120_000, 60, 90, 0, 0);
        note(&mut events, 145_000, 180_000, 60, 120, 0, 0);
        let events = sorted(events);
        assert_eq!(merge_notes(&events, 25), vec![event(0, 60, 90, true, 0, 0), event(180_000, 60, 0, false, 0, 0)]);
        assert_eq!(events.len(), 6, "Preparation must not mutate the source");
    }

    #[test]
    fn merge_leaves_notes_beyond_tolerance_and_other_tracks_channels_pitches_alone() {
        let mut events = Vec::new();
        note(&mut events, 0, 100_000, 60, 100, 0, 0);
        note(&mut events, 125_001, 180_000, 60, 100, 0, 0);
        note(&mut events, 100_000, 130_000, 60, 100, 1, 0);
        note(&mut events, 100_000, 130_000, 60, 100, 0, 1);
        note(&mut events, 100_000, 130_000, 61, 100, 0, 0);
        let events = sorted(events);
        assert_eq!(merge_notes(&events, 25), events);
    }

    #[test]
    fn merge_zero_tolerance_joins_touching_notes_but_preserves_positive_gaps() {
        let mut events = Vec::new();
        note(&mut events, 0, 10, 60, 100, 0, 0);
        note(&mut events, 10, 20, 60, 100, 0, 0);
        note(&mut events, 21, 30, 60, 100, 0, 0);
        assert_eq!(merge_notes(&sorted(events), 0), vec![
            event(0, 60, 100, true, 0, 0), event(20, 60, 0, false, 0, 0),
            event(21, 60, 100, true, 0, 0), event(30, 60, 0, false, 0, 0),
        ]);
    }

    #[test]
    fn merge_pairs_same_pitch_fifo_and_retains_zero_velocity_releases_and_orphans() {
        let events = vec![
            event(0, 60, 80, true, 0, 0), event(10, 60, 120, true, 0, 0),
            event(20, 60, 0, true, 0, 0), event(30, 60, 0, true, 0, 0),
            event(40, 60, 0, false, 0, 0), event(50, 61, 90, true, 0, 0),
        ];
        assert_eq!(partners(&events), vec![Some(2), Some(3), Some(0), Some(1), None, None]);
        assert_eq!(merge_notes(&events, 0), vec![
            event(0, 60, 100, true, 0, 0), event(30, 60, 0, true, 0, 0),
            event(40, 60, 0, false, 0, 0), event(50, 61, 90, true, 0, 0),
        ]);
    }

    fn chord_events() -> Vec<TimedNoteEvent> {
        let mut events = Vec::new();
        note(&mut events, 10_000, 200_000, 60, 100, 0, 0);
        note(&mut events, 20_000, 210_000, 64, 100, 1, 1);
        note(&mut events, 40_000, 220_000, 67, 100, 2, 2);
        sorted(events)
    }

    #[test]
    fn chords_span_tracks_channels_and_include_exact_tolerance_boundary() {
        let events = chord_events();
        let plan = detect_chords(&events, &[pad(&[0, 4, 7])], 30, |_, _| true, |pitch, _| Some(pitch));
        assert!(plan.iter().all(Option::is_some));
        assert_eq!(plan.iter().flatten().filter(|note| note.trigger).count(), 1);
        assert!(plan[0].unwrap().trigger);
        assert!(plan.iter().flatten().all(|note| note.chord_id == 0));
        assert!(detect_chords(&events, &[pad(&[0, 4, 7])], 29, |_, _| true, |pitch, _| Some(pitch))
            .iter().all(Option::is_none));
    }

    #[test]
    fn chord_sets_are_exact_and_do_not_turn_two_note_harmony_into_pads() {
        let mut events = chord_events();
        note(&mut events, 25_000, 200_000, 69, 100, 3, 0);
        let events = sorted(events);
        let plan = detect_chords(&events, &[pad(&[0, 4, 7])], 30, |_, _| true, |pitch, _| Some(pitch));
        assert!(plan.iter().all(Option::is_none), "An added pitch class must prevent a triad pad match");
        let plan = detect_chords(&events, &[pad(&[0, 4, 7])], 30,
            |_, event| event.track_index != 2 && event.track_index != 3, |pitch, _| Some(pitch));
        assert!(plan.iter().all(Option::is_none));
    }

    #[test]
    fn chord_matching_filters_muted_tracks_before_grouping() {
        let mut events = chord_events();
        note(&mut events, 0, 200_000, 69, 100, 3, 0);
        let events = sorted(events);
        let plan = detect_chords(&events, &[pad(&[0, 4, 7])], 30,
            |_, event| event.track_index != 3, |pitch, _| Some(pitch));
        for (index, event) in events.iter().enumerate() {
            assert_eq!(plan[index].is_some(), event.track_index != 3);
        }
    }

    #[test]
    fn chord_transform_uses_group_start_for_offsets_and_key_markers() {
        let events = chord_events();
        // A marker between chord members must not split the chord's interpretation.
        let plan = detect_chords(&events, &[pad(&[2, 6, 9])], 30, |_, _| true,
            |pitch, time| Some(pitch + if time < 30_000 { 2 } else { 5 }));
        assert!(plan.iter().all(Option::is_some));
        let plan = detect_chords(&events, &[pad(&[2, 6, 9])], 30, |_, _| true,
            |pitch, _| (pitch != 64).then_some(pitch + 2));
        assert!(plan.iter().all(Option::is_none), "An unplayable constituent must not create an incomplete chord");
    }

    #[test]
    fn doubled_octaves_share_one_chord_and_separate_groups_trigger_independently() {
        let mut events = chord_events();
        note(&mut events, 15_000, 200_000, 72, 100, 0, 0);
        note(&mut events, 500_000, 600_000, 60, 100, 0, 0);
        note(&mut events, 500_000, 600_000, 64, 100, 0, 0);
        note(&mut events, 500_000, 600_000, 67, 100, 0, 0);
        let events = sorted(events);
        let plan = detect_chords(&events, &[pad(&[0, 4, 7])], 30, |_, _| true, |pitch, _| Some(pitch));
        assert!(plan.iter().all(Option::is_some));
        assert_eq!(plan.iter().flatten().filter(|note| note.trigger).map(|note| note.chord_id).collect::<Vec<_>>(), vec![0, 1]);
    }

    #[test]
    fn absent_pads_disabled_tracks_and_unpaired_notes_leave_no_replacements() {
        let events = chord_events();
        assert!(detect_chords(&events, &[], 30, |_, _| true, |pitch, _| Some(pitch)).iter().all(Option::is_none));
        assert!(detect_chords(&events, &[pad(&[0, 4, 7])], 30, |_, _| false, |pitch, _| Some(pitch))
            .iter().all(Option::is_none));
        let incomplete = &events[..3];
        assert!(detect_chords(incomplete, &[pad(&[0, 4, 7])], 30, |_, _| true, |pitch, _| Some(pitch))
            .iter().all(Option::is_none));
    }
}
