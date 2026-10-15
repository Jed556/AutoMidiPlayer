use amp_core::{
    input::{KeyboardLayout, NoteKeyMapping},
    midi::{ParsedMidi, TempoMap, TimedNoteEvent},
    player::{PlaybackEngine, PlayerEvent},
};
use std::sync::mpsc;
use std::time::Duration;

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
