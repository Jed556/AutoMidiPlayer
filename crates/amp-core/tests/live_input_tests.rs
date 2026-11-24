use amp_core::input::simulator::KeyboardOutput;
use amp_core::player::live_input::MidiMessage;
use amp_core::{
    parse_midi_bytes, KeyboardLayout, NoteKeyMapping, OutputKey, PlaybackEngine, PlaybackStatus,
    PlayerEvent,
};
use std::{
    sync::{
        atomic::{AtomicBool, Ordering},
        Arc, Mutex,
    },
    time::{Duration, Instant},
};

struct Output(Arc<Mutex<Vec<(OutputKey, bool)>>>);
impl KeyboardOutput for Output {
    fn key(&mut self, key: OutputKey, up: bool) -> Result<(), String> {
        self.0.lock().unwrap().push((key, up));
        Ok(())
    }
}
fn wait(mut predicate: impl FnMut() -> bool) {
    let deadline = Instant::now() + Duration::from_secs(2);
    while !predicate() {
        assert!(
            Instant::now() < deadline,
            "engine did not reach the expected state"
        );
        std::thread::sleep(Duration::from_millis(2));
    }
}
fn on(pitch: u8) -> MidiMessage {
    MidiMessage::On {
        channel: 0,
        pitch,
        velocity: 100,
    }
}
fn song(ticks: u8) -> amp_core::ParsedMidi {
    let mut bytes = b"MThd\0\0\0\x06\0\0\0\x01\0\x60MTrk\0\0\0\x0c".to_vec();
    bytes.extend([0, 0x90, 60, 100, ticks, 0x80, 60, 0, 0, 0xff, 0x2f, 0]);
    parse_midi_bytes(&bytes).unwrap()
}

#[test]
fn live_input_works_without_song_and_survives_backing_pause_seek_stop_and_finish() {
    let events = Arc::new(Mutex::new(Vec::new()));
    let finished = Arc::new(AtomicBool::new(false));
    let song_finished = Arc::clone(&finished);
    let engine = PlaybackEngine::new(
        None,
        NoteKeyMapping::new(KeyboardLayout::Diatonic21),
        true,
        None,
        move |event| {
            if matches!(event, PlayerEvent::Finished) {
                song_finished.store(true, Ordering::Release);
            }
        },
    );
    engine.set_keyboard_output(Some(Box::new(Output(events.clone()))));
    engine.set_note_timing(true, 0);
    engine.midi_start(7);
    engine.midi_gate(true);
    engine.midi_message(7, on(60));
    wait(|| engine.midi_snapshot().pitches == [60]);
    assert_eq!(events.lock().unwrap().len(), 1);

    engine.load_song(song(96));
    engine.request_play().recv_timeout(Duration::from_secs(2)).unwrap().unwrap();
    wait(|| engine.get_status() == PlaybackStatus::Playing);
    engine.pause();
    wait(|| engine.get_status() == PlaybackStatus::Paused);
    assert_eq!(engine.midi_snapshot().pitches, [60]);
    assert!(!events.lock().unwrap().iter().any(|(_, up)| *up));
    engine.seek(0, 1);
    engine.stop();
    wait(|| engine.get_status() == PlaybackStatus::Stopped);
    assert_eq!(engine.midi_snapshot().pitches, [60]);

    finished.store(false, Ordering::Release);
    engine.load_song(song(1));
    engine.request_play().recv_timeout(Duration::from_secs(2)).unwrap().unwrap();
    wait(|| finished.load(Ordering::Acquire) && engine.get_status() == PlaybackStatus::Stopped);
    assert_eq!(engine.midi_snapshot().pitches, [60]);
    assert!(!events.lock().unwrap().iter().any(|(_, up)| *up));
    engine.midi_message(
        7,
        MidiMessage::Off {
            channel: 0,
            pitch: 60,
        },
    );
    wait(|| engine.midi_snapshot().pitches.is_empty());
    assert_eq!(
        events.lock().unwrap().iter().filter(|(_, up)| *up).count(),
        1
    );
}

#[test]
fn panic_deactivates_input_and_old_callbacks_cannot_restart_it() {
    let engine = PlaybackEngine::new(
        None,
        NoteKeyMapping::new(KeyboardLayout::Diatonic21),
        false,
        None,
        |_| {},
    );
    engine.set_listen_mode(true);
    engine.set_note_timing(true, 0);
    engine.midi_start(5);
    engine.midi_message(5, on(60));
    wait(|| engine.midi_snapshot().pitches == [60]);
    engine.panic();
    wait(|| !engine.midi_snapshot().active);
    engine.midi_message(5, on(64));
    engine.midi_start(6);
    engine.midi_message(5, on(64));
    wait(|| engine.midi_snapshot().generation == 6);
    assert!(engine.midi_snapshot().pitches.is_empty());
    engine.midi_message(6, on(67));
    wait(|| engine.midi_snapshot().pitches == [67]);
}

#[test]
fn native_output_revocation_deactivates_input_without_another_midi_packet() {
    use std::sync::atomic::{AtomicBool, Ordering};
    struct Revocable(Arc<AtomicBool>);
    impl KeyboardOutput for Revocable {
        fn key(&mut self, _: OutputKey, _: bool) -> Result<(), String> { Ok(()) }
        fn error(&self) -> Option<String> {
            self.0.load(Ordering::Acquire).then(|| "keyboard access revoked".to_owned())
        }
    }
    let revoked = Arc::new(AtomicBool::new(false));
    let engine = PlaybackEngine::new(None, NoteKeyMapping::new(KeyboardLayout::Diatonic21), true, None, |_| {});
    engine.set_keyboard_output(Some(Box::new(Revocable(revoked.clone()))));
    engine.set_note_timing(true, 0);
    engine.midi_start(9); engine.midi_gate(true); engine.midi_message(9, on(60));
    wait(|| engine.midi_snapshot().pitches == [60]);
    revoked.store(true, Ordering::Release);
    wait(|| !engine.midi_snapshot().active);
    assert!(engine.midi_snapshot().pitches.is_empty());
    assert_eq!(engine.midi_snapshot().error.as_deref(), Some("keyboard access revoked"));
}

#[cfg(not(windows))]
#[test]
fn keyboard_replacement_recovers_without_restarting_a_midi_connection() {
    struct Revoked;
    impl KeyboardOutput for Revoked {
        fn key(&mut self, _: OutputKey, _: bool) -> Result<(), String> { Ok(()) }
        fn error(&self) -> Option<String> { Some("keyboard access revoked".into()) }
    }
    let engine = PlaybackEngine::new(None, NoteKeyMapping::new(KeyboardLayout::Diatonic21), true, None, |_| {});
    engine.set_keyboard_output(Some(Box::new(Revoked)));
    engine.flush().unwrap();
    assert_eq!(engine.midi_snapshot().error.as_deref(), Some("keyboard access revoked"));
    assert!(!engine.midi_snapshot().keyboard_available);

    engine.set_keyboard_output(Some(Box::new(Output(Arc::default()))));
    engine.flush().unwrap();
    let snapshot = engine.midi_snapshot();
    assert!(snapshot.keyboard_available, "a healthy song-only output must be usable after retry");
    assert!(snapshot.error.is_none());
    assert!(!snapshot.active, "preparing game output must not start physical MIDI input");
    engine.prepare_keyboard().unwrap();
    assert!(engine.midi_snapshot().keyboard_available);
}

#[cfg(not(windows))]
#[test]
fn keyboard_recovery_preserves_midi_errors_and_barriers_publish_output_mode() {
    let engine = PlaybackEngine::new(None, NoteKeyMapping::new(KeyboardLayout::Diatonic21), true, None, |_| {});
    engine.set_keyboard_output(Some(Box::new(Output(Arc::default()))));
    engine.set_listen_mode(true);
    engine.midi_start(1);
    engine.flush().unwrap();
    assert!(engine.midi_snapshot().listen_mode);

    // Fill unmatched attacks without overflowing the transport queue. This
    // stops input with a real MIDI error independently of keyboard readiness.
    for batch in 0..33 {
        for _ in 0..if batch == 32 { 1 } else { 128 } {
            engine.midi_message(1, on(60));
        }
        engine.flush().unwrap();
    }
    let overflow = engine.midi_snapshot().error.expect("MIDI overflow must be reported");
    assert!(overflow.contains("MIDI input overflow"));
    engine.set_keyboard_output(Some(Box::new(Output(Arc::default()))));
    engine.prepare_keyboard().unwrap();
    assert!(engine.midi_snapshot().keyboard_available);
    assert_eq!(engine.midi_snapshot().error.as_deref(), Some(overflow.as_str()));

    for listen in [false, true, false] {
        engine.set_listen_mode(listen);
        engine.flush().unwrap();
        assert_eq!(engine.midi_snapshot().listen_mode, listen,
            "flush must publish prior mode commands before returning");
    }
}
