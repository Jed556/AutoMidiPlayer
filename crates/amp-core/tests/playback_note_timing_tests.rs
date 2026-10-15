use amp_core::{KeyboardLayout, NoteKeyMapping, ParsedMidi, PlaybackEngine, PlayerEvent, SynthMode, TempoMap, TimedNoteEvent};
use std::{sync::mpsc, time::{Duration, Instant}};

fn player(duration_us: u64) -> (PlaybackEngine, mpsc::Receiver<(bool, Instant)>) {
    let event = |time_us, is_note_on| TimedNoteEvent {
        time_us, note: 60, velocity: if is_note_on { 90 } else { 0 },
        is_note_on, channel: 0, track_index: 0,
    };
    let song = ParsedMidi {
        title: "Note timing regression".into(), duration_us: duration_us + 100_000,
        bpm: 120.0, ppqn: 480, tracks: vec![], events: vec![event(0, true), event(duration_us, false)],
        control_events: vec![], tempo_map: TempoMap::new(480, &[]), key_signatures: vec![],
    };
    let (tx, rx) = mpsc::channel();
    let engine = PlaybackEngine::new(Some(song), NoteKeyMapping::new(KeyboardLayout::Diatonic21),
        false, None, move |event| {
            if let PlayerEvent::NoteHit { note: 60, is_on } = event { let _ = tx.send((is_on, Instant::now())); }
        });
    engine.set_hold_delay_ms(20);
    (engine, rx)
}

fn next(rx: &mpsc::Receiver<(bool, Instant)>) -> (bool, Instant) {
    rx.recv_timeout(Duration::from_secs(3)).expect("note output")
}

#[test]
fn adapted_listen_and_game_visualizers_release_taps_before_the_source_note_off() {
    for listen in [false, true] {
        let (engine, rx) = player(1_000_000);
        engine.set_listen_mode(listen);
        engine.set_note_timing(true, 1000);
        engine.play();
        let (on, started) = next(&rx);
        let (off, ended) = next(&rx);
        assert!(on);
        assert!(!off);
        assert!(ended.duration_since(started) < Duration::from_millis(400));
    }
}

#[test]
fn a_100ms_threshold_keeps_a_330ms_note_held() {
    let (engine, rx) = player(330_000);
    engine.set_note_timing(true, 100);
    engine.play();
    assert!(next(&rx).0);
    assert!(rx.recv_timeout(Duration::from_millis(150)).is_err());
    assert!(!next(&rx).0);
}

#[test]
fn raising_the_threshold_releases_a_note_already_held() {
    let (engine, rx) = player(2_000_000);
    engine.set_note_timing(true, 100);
    engine.play();
    assert!(next(&rx).0);
    assert!(rx.recv_timeout(Duration::from_millis(60)).is_err());
    engine.set_note_timing(true, 2500);
    assert!(!rx.recv_timeout(Duration::from_millis(400)).expect("retimed note release").0);
}

#[test]
fn raw_listen_preserves_midi_duration_and_speed_affects_threshold_classification() {
    let (engine, rx) = player(330_000);
    engine.set_listen_mode(true);
    engine.set_synth_mode(SynthMode::Raw);
    engine.set_note_timing(false, 1000);
    engine.play();
    assert!(next(&rx).0);
    assert!(rx.recv_timeout(Duration::from_millis(150)).is_err());
    assert!(!next(&rx).0);

    let (engine, rx) = player(1_000_000);
    engine.set_speed(5.0);
    engine.set_note_timing(true, 200);
    engine.play();
    assert!(next(&rx).0);
    let start = Instant::now();
    assert!(!next(&rx).0);
    assert!(start.elapsed() < Duration::from_millis(150));
}
