#![cfg(not(windows))]

use amp_core::{
    diagnostics::ServiceState, input::simulator::KeyboardOutput,
    KeyboardLayout, NoteKeyMapping, OutputKey, ParsedMidi, PlaybackEngine,
    PlaybackStatus, PlayerEvent, TempoMap, TimedNoteEvent,
};
use std::{
    sync::{atomic::{AtomicBool, Ordering}, Arc, Mutex},
    time::{Duration, Instant},
};

type KeyEvents = Arc<Mutex<Vec<(OutputKey, bool)>>>;

struct Output { keys: KeyEvents, revoked: Arc<AtomicBool> }
impl KeyboardOutput for Output {
    fn key(&mut self, key: OutputKey, up: bool) -> Result<(), String> {
        self.keys.lock().unwrap().push((key, up));
        Ok(())
    }
    fn error(&self) -> Option<String> {
        self.revoked.load(Ordering::Acquire).then(|| "keyboard access revoked".into())
    }
    fn failure_state(&self) -> ServiceState { ServiceState::PermissionRequired }
}

fn wait(mut predicate: impl FnMut() -> bool) {
    let deadline = Instant::now() + Duration::from_secs(2);
    while !predicate() {
        assert!(Instant::now() < deadline, "playback did not reach expected state");
        std::thread::sleep(Duration::from_millis(2));
    }
}

fn song() -> ParsedMidi {
    let event = |time_us, is_note_on| TimedNoteEvent {
        time_us, note: 60, velocity: if is_note_on { 90 } else { 0 },
        is_note_on, channel: 0, track_index: 0,
    };
    ParsedMidi {
        title: "Keyboard recovery regression".into(), duration_us: 2_000_000,
        bpm: 120.0, ppqn: 480, tracks: vec![],
        events: vec![event(0, true), event(1_500_000, false)],
        control_events: vec![], tempo_map: TempoMap::new(480, &[]), key_signatures: vec![],
        guitar_pro: None,
    }
}

fn player(listen: bool) -> (PlaybackEngine, KeyEvents, Arc<AtomicBool>, Arc<Mutex<Vec<PlayerEvent>>>) {
    let keys = Arc::new(Mutex::new(Vec::new()));
    let revoked = Arc::new(AtomicBool::new(false));
    let events = Arc::new(Mutex::new(Vec::new()));
    let captured = events.clone();
    let engine = PlaybackEngine::new(Some(song()), NoteKeyMapping::new(KeyboardLayout::Diatonic21),
        true, None, move |event| captured.lock().unwrap().push(event));
    engine.set_keyboard_output(Some(Box::new(Output { keys: keys.clone(), revoked: revoked.clone() })));
    engine.set_listen_mode(listen);
    engine.set_note_timing(true, 0);
    engine.play();
    wait(|| engine.get_current_time_us() > 0);
    (engine, keys, revoked, events)
}

#[test]
fn keyboard_revocation_pauses_game_playback_releases_notes_and_preserves_position() {
    let (engine, keys, revoked, events) = player(false);
    assert!(keys.lock().unwrap().iter().any(|(_, up)| !up));
    revoked.store(true, Ordering::Release);
    wait(|| engine.get_status() == PlaybackStatus::Paused);
    engine.flush().unwrap();
    let paused_at = engine.get_current_time_us();
    assert!(paused_at > 0 && paused_at < 1_500_000);
    assert!(keys.lock().unwrap().iter().any(|(_, up)| *up), "held game key must be released");
    assert_eq!(engine.keyboard_status().state, ServiceState::PermissionRequired);
    assert_eq!(engine.midi_snapshot().error.as_deref(), Some("keyboard access revoked"));
    let events = events.lock().unwrap();
    assert!(events.iter().any(|event| matches!(event, PlayerEvent::ActiveNotes(notes) if notes.is_empty())));
    assert!(events.iter().any(|event| matches!(event, PlayerEvent::ActiveTrackNotes(notes) if notes.is_empty())));
    assert!(events.iter().any(|event| matches!(event, PlayerEvent::TimeUpdate { time_us, .. } if *time_us == paused_at)));
    drop(events);
    std::thread::sleep(Duration::from_millis(60));
    assert_eq!(engine.get_current_time_us(), paused_at);

    let recovered = Arc::new(Mutex::new(Vec::new()));
    engine.set_keyboard_output(Some(Box::new(Output {
        keys: recovered.clone(), revoked: Arc::new(AtomicBool::new(false)),
    })));
    engine.play();
    wait(|| recovered.lock().unwrap().iter().any(|(_, up)| !up));
    assert_eq!(engine.get_status(), PlaybackStatus::Playing);
    assert!(engine.get_current_time_us() >= paused_at);
}

#[test]
fn keyboard_revocation_does_not_pause_a_listen_mode_song() {
    let (engine, keys, revoked, events) = player(true);
    assert!(keys.lock().unwrap().is_empty());
    let before = engine.get_current_time_us();
    revoked.store(true, Ordering::Release);
    wait(|| engine.midi_snapshot().error.as_deref() == Some("keyboard access revoked"));
    wait(|| engine.get_current_time_us() > before + 30_000);
    assert_eq!(engine.get_status(), PlaybackStatus::Playing);
    assert!(!events.lock().unwrap().iter().any(|event|
        matches!(event, PlayerEvent::StateChanged(PlaybackStatus::Paused))));
}
