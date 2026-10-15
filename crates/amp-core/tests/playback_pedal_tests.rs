use amp_core::{
    input::{KeyboardLayout, NoteKeyMapping},
    midi::{ControlEventKind, ParsedMidi, TempoMap, TimedControlEvent},
    player::{engine::PedalState, PlaybackEngine, PlayerEvent},
};
use std::{sync::mpsc, time::Duration};

fn control(time_us: u64, channel: u8, controller: u8, value: u8) -> TimedControlEvent {
    TimedControlEvent {
        time_us, channel, kind: ControlEventKind::ControlChange { controller, value },
    }
}

fn engine(controls: Vec<TimedControlEvent>) -> (PlaybackEngine, mpsc::Receiver<PedalState>) {
    let song = ParsedMidi {
        title: "Pedal regression".into(), duration_us: 10_000_000, bpm: 120.0,
        ppqn: 480, tracks: vec![], events: vec![], control_events: controls,
        tempo_map: TempoMap::new(480, &[]), key_signatures: vec![],
    };
    let (tx, rx) = mpsc::channel();
    let engine = PlaybackEngine::new(
        Some(song), NoteKeyMapping::from_pairs(KeyboardLayout::Diatonic21, &[]),
        false, None, move |event| {
            if let PlayerEvent::PedalsChanged(state) = event { let _ = tx.send(state); }
        },
    );
    (engine, rx)
}

fn next(rx: &mpsc::Receiver<PedalState>) -> PedalState {
    rx.recv_timeout(Duration::from_secs(2)).expect("pedal event")
}

#[test]
fn midi_controls_light_each_pedal_without_notes_or_audio() {
    let (engine, rx) = engine(vec![
        control(10_000, 0, 64, 64), control(30_000, 0, 66, 127),
        control(50_000, 0, 67, 64), control(70_000, 0, 64, 63),
        control(90_000, 0, 66, 0), control(110_000, 0, 67, 0),
    ]);
    engine.play();
    assert_eq!(next(&rx), PedalState::default());
    assert_eq!(next(&rx), PedalState { sustain: true, ..Default::default() });
    assert_eq!(next(&rx), PedalState { sustain: true, sostenuto: true, una_corda: false });
    assert_eq!(next(&rx), PedalState { sustain: true, sostenuto: true, una_corda: true });
    assert_eq!(next(&rx), PedalState { sustain: false, sostenuto: true, una_corda: true });
    assert_eq!(next(&rx), PedalState { una_corda: true, ..Default::default() });
    assert_eq!(next(&rx), PedalState::default());
    assert_eq!(next(&rx), PedalState::default()); // completion releases indicators
}

#[test]
fn seek_reconstructs_channels_and_reset_controllers_releases_only_its_channel() {
    let (engine, rx) = engine(vec![
        control(100_000, 0, 64, 127), control(200_000, 1, 64, 127),
        control(300_000, 0, 121, 0), control(400_000, 1, 64, 0),
    ]);
    engine.seek(350_000, 1);
    assert!(next(&rx).sustain); // channel 1 is still holding sustain
    engine.seek(450_000, 2);
    assert_eq!(next(&rx), PedalState::default());
    engine.seek(50_000, 3);
    assert_eq!(next(&rx), PedalState::default());
    engine.seek(250_000, 4);
    assert!(next(&rx).sustain);
    engine.stop();
    assert_eq!(next(&rx), PedalState::default());
}

#[test]
fn pause_clears_pedals_and_resume_restores_them() {
    let (engine, rx) = engine(vec![
        control(0, 0, 67, 127), control(9_000_000, 0, 67, 0),
    ]);
    engine.seek(1_000_000, 1);
    assert!(next(&rx).una_corda);
    engine.play();
    assert!(next(&rx).una_corda);
    engine.pause();
    assert_eq!(next(&rx), PedalState::default());
    engine.play();
    assert!(next(&rx).una_corda);
    engine.panic();
    assert_eq!(next(&rx), PedalState::default());
}
