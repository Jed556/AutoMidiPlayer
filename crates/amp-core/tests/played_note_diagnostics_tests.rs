use amp_core::{
    input::simulator::KeyboardOutput, KeyboardLayout, NoteKeyMapping, OutputKey,
    ParsedMidi, PlaybackEngine, PlayerEvent, TempoMap, TimedNoteEvent,
};
use std::sync::{atomic::{AtomicUsize, Ordering}, Arc, Mutex};
use std::time::{Duration, Instant};

struct Capture(Mutex<Vec<(String, String)>>);
static LOG: Capture = Capture(Mutex::new(Vec::new()));
impl log::Log for Capture {
    fn enabled(&self, _: &log::Metadata) -> bool { true }
    fn log(&self, record: &log::Record) {
        if record.target().ends_with("_diagnostics") {
            self.0.lock().unwrap().push((record.target().into(), record.args().to_string()));
        }
    }
    fn flush(&self) {}
}
struct Output;
impl KeyboardOutput for Output {
    fn key(&mut self, _: OutputKey, _: bool) -> Result<(), String> { Ok(()) }
}

#[test]
fn note_logging_is_opt_in_reports_mapping_and_drift_and_can_be_disabled_live() {
    log::set_logger(&LOG).unwrap();
    log::set_max_level(log::LevelFilter::Info);
    let song = ParsedMidi {
        title: "Diagnostics regression".into(), duration_us: 80_000, bpm: 120.0, ppqn: 480,
        tracks: vec![], control_events: vec![], key_signatures: vec![],
        tempo_map: TempoMap::new(480, &[]),
        events: vec![
            TimedNoteEvent { time_us: 0, track_index: 0, channel: 0, note: 60, velocity: 80, is_note_on: true },
            TimedNoteEvent { time_us: 30_000, track_index: 0, channel: 0, note: 60, velocity: 0, is_note_on: false },
        ],
    };
    let finished = Arc::new(AtomicUsize::new(0));
    let observed = finished.clone();
    let engine = PlaybackEngine::new(None,
        NoteKeyMapping::from_pairs(KeyboardLayout::Diatonic21, &[(62, 's')]), true, None,
        move |event| { if matches!(event, PlayerEvent::Finished) { observed.fetch_add(1, Ordering::SeqCst); } });
    engine.set_keyboard_output(Some(Box::new(Output)));
    engine.set_key_offset(2);
    engine.set_track_volume(0, 0.5);
    engine.set_note_timing(true, 0);
    let play = |expected| {
        engine.load_song(song.clone());
        engine.play();
        engine.flush().unwrap();
        let until = Instant::now() + Duration::from_secs(3);
        while finished.load(Ordering::SeqCst) < expected {
            assert!(Instant::now() < until, "diagnostics test playback stalled");
            std::thread::sleep(Duration::from_millis(2));
        }
    };
    play(1);
    assert!(LOG.0.lock().unwrap().is_empty());
    engine.set_note_logging(true);
    play(2);
    let records = LOG.0.lock().unwrap().clone();
    assert!(records.iter().any(|(target, message)| target.ends_with("note_diagnostics")
        && message.contains("NOTE_ON") && message.contains("original=60")
        && message.contains("mapped=Some(62)") && message.contains("velocity=40")
        && message.contains("output=game") && message.contains("key=Some(")));
    assert!(records.iter().any(|(_, message)| message.contains("NOTE_OFF")));
    assert!(records.iter().any(|(target, message)| target.ends_with("scheduler_diagnostics")
        && message.contains("scheduled_us=0") && message.contains("actual_us=") && message.contains("drift_us=")));
    engine.set_note_logging(false);
    play(3);
    assert_eq!(LOG.0.lock().unwrap().len(), records.len());
}
