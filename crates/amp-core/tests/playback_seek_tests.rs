use amp_core::{
    input::{KeyboardLayout, NoteKeyMapping},
    midi::{ParsedMidi, TempoMap, TimedNoteEvent},
    player::{PlaybackEngine, PlayerEvent},
};
use std::sync::mpsc;
use std::time::{Duration, Instant};

fn event(time_us: u64, track_index: usize, note: u8, is_note_on: bool) -> TimedNoteEvent {
    TimedNoteEvent { time_us, track_index, channel: 0, note,
        velocity: if is_note_on { 80 } else { 0 }, is_note_on }
}

fn held_player(events: Vec<TimedNoteEvent>) -> (PlaybackEngine, mpsc::Receiver<PlayerEvent>) {
    let song = ParsedMidi {
        title: "Held note seek regression".into(),
        duration_us: events.last().unwrap().time_us + 100_000,
        bpm: 120.0, ppqn: 480, tracks: vec![], events, control_events: vec![],
        tempo_map: TempoMap::new(480, &[]), key_signatures: vec![],
    };
    let (tx, rx) = mpsc::channel();
    let engine = PlaybackEngine::new(Some(song), NoteKeyMapping::new(KeyboardLayout::Diatonic21),
        false, None, move |event| { let _ = tx.send(event); });
    engine.set_note_timing(true, 0);
    (engine, rx)
}

fn until(rx: &mpsc::Receiver<PlayerEvent>, matches: impl Fn(&PlayerEvent) -> bool) -> PlayerEvent {
    let deadline = Instant::now() + Duration::from_secs(2);
    loop {
        let event = rx.recv_timeout(deadline.saturating_duration_since(Instant::now())).expect("playback event");
        if matches(&event) { return event; }
    }
}

#[test]
fn starting_mid_hold_restores_its_owner_after_a_later_same_pitch_note_finishes() {
    // Like orange.mid: the long and short notes are in separate tracks on channel 0.
    let (engine, rx) = held_player(vec![
        event(0, 0, 60, true), event(10_100_000, 1, 60, true),
        event(10_200_000, 1, 60, false), event(10_400_000, 0, 60, false),
    ]);
    engine.seek(10_000_000, 1);
    until(&rx, |e| matches!(e, PlayerEvent::TimeUpdate { seek_id: 1, .. }));
    // Moving the playhead while stopped must stay silent.
    assert!(rx.recv_timeout(Duration::from_millis(40)).is_err());
    engine.play();
    until(&rx, |e| matches!(e, PlayerEvent::NoteHit { note: 60, is_on: true }));
    let restored = until(&rx, |e| matches!(e, PlayerEvent::ActiveTrackNotes(_)));
    assert!(matches!(restored, PlayerEvent::ActiveTrackNotes(notes) if notes.len() == 1 && notes[0].track_index == 0));
    until(&rx, |e| matches!(e, PlayerEvent::ActiveTrackNotes(notes) if notes.len() == 2));
    let remaining = until(&rx, |e| matches!(e, PlayerEvent::ActiveTrackNotes(notes) if notes.len() < 2));
    assert!(matches!(remaining, PlayerEvent::ActiveTrackNotes(notes) if notes.len() == 1 && notes[0].track_index == 0));
    until(&rx, |e| matches!(e, PlayerEvent::ActiveTrackNotes(notes) if notes.is_empty()));
}

#[test]
fn seeking_to_a_chord_onset_dispatches_every_note_at_that_timestamp() {
    let (engine, rx) = held_player(vec![
        event(1_000_000, 0, 60, true), event(1_000_000, 0, 64, true), event(1_000_000, 0, 67, true),
        event(1_500_000, 0, 60, false), event(1_500_000, 0, 64, false), event(1_500_000, 0, 67, false),
    ]);
    engine.seek(1_000_000, 1);
    engine.play();
    let active = until(&rx, |e| matches!(e, PlayerEvent::ActiveNotes(notes) if !notes.is_empty()));
    let PlayerEvent::ActiveNotes(mut notes) = active else { unreachable!() };
    notes.sort_unstable();
    assert_eq!(notes, vec![60, 64, 67]);
}

#[test]
fn a_playing_seek_and_pause_resume_restore_held_notes_before_the_next_event() {
    let (engine, rx) = held_player(vec![event(0, 0, 60, true), event(10_400_000, 0, 60, false)]);
    engine.play();
    until(&rx, |e| matches!(e, PlayerEvent::ActiveNotes(notes) if notes == &[60]));
    engine.seek(10_000_000, 1);
    until(&rx, |e| matches!(e, PlayerEvent::TimeUpdate { seek_id: 1, time_us: 10_000_000 }));
    until(&rx, |e| matches!(e, PlayerEvent::ActiveNotes(notes) if notes == &[60]));
    engine.pause();
    until(&rx, |e| matches!(e, PlayerEvent::StateChanged(amp_core::PlaybackStatus::Paused)));
    assert!(rx.recv_timeout(Duration::from_millis(40)).is_err());
    engine.play();
    until(&rx, |e| matches!(e, PlayerEvent::NoteHit { note: 60, is_on: true }));
    until(&rx, |e| matches!(e, PlayerEvent::ActiveNotes(notes) if notes == &[60]));
    until(&rx, |e| matches!(e, PlayerEvent::ActiveNotes(notes) if notes.is_empty()));
}

#[test]
fn seek_restoration_does_not_replay_taps_expired_notes_or_muted_tracks() {
    let (engine, rx) = held_player(vec![
        event(0, 0, 60, true), event(0, 1, 64, true),
        event(950_000, 2, 67, true), event(1_000_000, 0, 60, false),
        event(1_050_000, 2, 67, false), event(1_300_000, 1, 64, false),
    ]);
    engine.set_note_timing(true, 200);
    engine.set_track_muted(1, true);
    engine.seek(1_000_000, 1);
    engine.play();
    loop {
        let next = rx.recv_timeout(Duration::from_secs(2)).unwrap();
        assert!(!matches!(next, PlayerEvent::NoteHit { is_on: true, .. }));
        assert!(!matches!(&next, PlayerEvent::ActiveNotes(notes) if !notes.is_empty()));
        if matches!(next, PlayerEvent::Finished) { break; }
    }
}

#[test]
fn a_restored_hold_uses_its_full_duration_and_original_onset_marker() {
    let (engine, rx) = held_player(vec![event(0, 0, 60, true), event(10_100_000, 0, 60, false)]);
    // There are only 100ms left at the seek target. This remains a long hold,
    // and the marker encountered during its sustain must not change its key.
    engine.set_note_timing(true, 200);
    engine.set_key_markers(vec![amp_core::midi::KeyChangeMarker {
        time_ms: 5_000, key_offset: 12, transpose: None, label: "Later octave".into(),
    }]);
    engine.seek(10_000_000, 1);
    engine.play();
    let hit = until(&rx, |e| matches!(e, PlayerEvent::NoteHit { is_on: true, .. }));
    assert!(matches!(hit, PlayerEvent::NoteHit { note: 60, .. }));
    until(&rx, |e| matches!(e, PlayerEvent::NoteHit { note: 60, is_on: false }));
}

#[test]
fn skipped_source_notes_cannot_consume_a_newer_same_track_note_off_owner() {
    for muted in [false, true] {
        let (engine, rx) = held_player(vec![
            event(0, 0, 60, true), event(10_200_000, 0, 60, true),
            event(10_300_000, 0, 60, false), event(10_500_000, 0, 60, false),
        ]);
        if muted { engine.set_track_muted(0, true); }
        else { engine.set_note_timing(false, 0); }
        engine.seek(10_000_000, 1);
        engine.play();
        // Wait for restoration to finish before enabling new notes.
        until(&rx, |e| matches!(e, PlayerEvent::TimeUpdate { time_us, .. } if *time_us > 10_000_000));
        if muted { engine.set_track_muted(0, false); }
        else { engine.set_note_timing(true, 0); }
        until(&rx, |e| matches!(e, PlayerEvent::ActiveNotes(notes) if notes == &[60]));
        until(&rx, |e| matches!(e, PlayerEvent::TimeUpdate { time_us, .. } if *time_us > 10_350_000));
        let off = until(&rx, |e| matches!(e, PlayerEvent::NoteHit { note: 60, is_on: false }));
        assert!(matches!(off, PlayerEvent::NoteHit { is_on: false, .. }));
        assert!(engine.get_current_time_us() >= 10_500_000);
    }
}

#[test]
fn backward_seek_resets_sync_clock_and_echoes_latest_request() {
    let song = ParsedMidi {
        title: "Seek regression".into(),
        duration_us: 20_000_000,
        bpm: 120.0,
        ppqn: 480,
        tracks: vec![],
        events: vec![TimedNoteEvent {
            time_us: 19_000_000, track_index: 0, channel: 0,
            note: 60, velocity: 80, is_note_on: true,
        }],
        control_events: vec![],
        tempo_map: TempoMap::new(480, &[]),
        key_signatures: vec![],
    };
    let (tx, rx) = mpsc::channel();
    // Empty key mapping, input disabled, and no audio device: no external effects.
    let engine = PlaybackEngine::new(
        Some(song), NoteKeyMapping::from_pairs(KeyboardLayout::Diatonic21, &[]),
        false, None, move |event| {
            if let PlayerEvent::TimeUpdate { time_us, seek_id } = event {
                let _ = tx.send((time_us, seek_id));
            }
        },
    );
    engine.seek(10_000_000, 1);
    assert_eq!(rx.recv_timeout(Duration::from_secs(2)).unwrap(), (10_000_000, 1));
    engine.seek(1_000_000, 2);
    assert_eq!(rx.recv_timeout(Duration::from_secs(2)).unwrap(), (1_000_000, 2));
    engine.seek(15_000_000, 1); // delayed stale command must not win
    engine.play();
    let (time, revision) = rx.recv_timeout(Duration::from_secs(2)).unwrap();
    assert_eq!(revision, 2);
    assert!((1_000_000..3_000_000).contains(&time));
}
