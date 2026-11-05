use amp_core::{KeyboardLayout, NoteKeyMapping, ParsedMidi, PlaybackEngine, PlayerEvent, SynthMode, TempoMap, TimedNoteEvent};
use std::{sync::mpsc, time::{Duration, Instant}};

fn event(time_us: u64, track_index: usize, note: u8, is_note_on: bool) -> TimedNoteEvent {
    TimedNoteEvent { time_us, track_index, channel: 0, note,
        velocity: if is_note_on { 90 } else { 0 }, is_note_on }
}

fn player(events: Vec<TimedNoteEvent>, listen: bool) -> (PlaybackEngine, mpsc::Receiver<PlayerEvent>) {
    let song = ParsedMidi {
        title: "Track toggle regression".into(), duration_us: events.last().unwrap().time_us + 100_000,
        bpm: 120.0, ppqn: 480, tracks: vec![], events, control_events: vec![],
        tempo_map: TempoMap::new(480, &[]), key_signatures: vec![],
    };
    let (tx, rx) = mpsc::channel();
    let engine = PlaybackEngine::new(Some(song), NoteKeyMapping::new(KeyboardLayout::Diatonic21),
        false, None, move |event| { let _ = tx.send(event); });
    engine.set_listen_mode(listen);
    engine.set_synth_mode(SynthMode::Raw);
    engine.set_note_timing(true, 0);
    (engine, rx)
}

fn until(rx: &mpsc::Receiver<PlayerEvent>, matches: impl Fn(&PlayerEvent) -> bool) -> PlayerEvent {
    let deadline = Instant::now() + Duration::from_millis(1200);
    loop {
        let event = rx.recv_timeout(deadline.saturating_duration_since(Instant::now())).expect("playback event");
        if matches(&event) { return event; }
    }
}

#[test]
fn rapid_track_toggles_release_held_notes_immediately_and_preserve_other_tracks() {
    for listen in [false, true] {
        let (engine, rx) = player(vec![
            event(0, 0, 60, true), event(0, 1, 60, true),
            event(5_000_000, 0, 60, false), event(5_000_000, 1, 60, false),
        ], listen);
        engine.play();
        until(&rx, |e| matches!(e, PlayerEvent::ActiveTrackNotes(notes) if notes.len() == 2));
        for _ in 0..20 {
            engine.set_track_muted(0, true);
            engine.set_track_muted(0, false);
        }
        let remaining = until(&rx, |e| matches!(e, PlayerEvent::ActiveTrackNotes(notes) if notes.len() == 1));
        assert!(matches!(remaining, PlayerEvent::ActiveTrackNotes(notes) if notes[0].track_index == 1));
        engine.set_track_muted(1, true);
        until(&rx, |e| matches!(e, PlayerEvent::ActiveNotes(notes) if notes.is_empty()));
    }
}

#[test]
fn skipped_note_ons_keep_their_fifo_slot_after_a_track_is_enabled() {
    for mode in ["muted", "zero_volume", "rounded_velocity", "muted_after_on"] {
        let (engine, rx) = player(vec![
            event(0, 0, 60, true), event(200_000, 0, 60, true),
            event(400_000, 0, 60, false), event(700_000, 0, 60, false),
            event(1_000_000, 1, 67, true), event(1_100_000, 1, 67, false),
        ], false);
        match mode {
            "muted" => engine.set_track_muted(0, true),
            "zero_volume" => engine.set_track_volume(0, 0.0),
            "rounded_velocity" => engine.set_track_volume(0, 0.001),
            _ => {}
        }
        engine.play();
        if mode == "muted_after_on" {
            until(&rx, |e| matches!(e, PlayerEvent::NoteHit { note: 60, is_on: true }));
            engine.set_track_muted(0, true);
            until(&rx, |e| matches!(e, PlayerEvent::NoteHit { note: 60, is_on: false }));
        }
        until(&rx, |e| matches!(e, PlayerEvent::TimeUpdate { time_us, .. } if *time_us > 0));
        engine.set_track_volume(0, 1.0);
        engine.set_track_muted(0, false);
        until(&rx, |e| matches!(e, PlayerEvent::NoteHit { note: 60, is_on: true }));
        let off = until(&rx, |e| matches!(e, PlayerEvent::NoteHit { note: 60, is_on: false }));
        assert!(matches!(off, PlayerEvent::NoteHit { is_on: false, .. }));
        assert!(engine.get_current_time_us() >= 700_000, "skipped note stole the newer note's release");
    }
}

#[test]
fn zero_volume_does_not_drop_the_release_of_an_already_sounding_note() {
    let (engine, rx) = player(vec![
        event(0, 0, 60, true), event(300_000, 0, 60, false),
        event(5_000_000, 1, 67, true), event(5_100_000, 1, 67, false),
    ], true);
    engine.play();
    until(&rx, |e| matches!(e, PlayerEvent::NoteHit { note: 60, is_on: true }));
    engine.set_track_volume(0, 0.0);
    until(&rx, |e| matches!(e, PlayerEvent::NoteHit { note: 60, is_on: false }));
}
