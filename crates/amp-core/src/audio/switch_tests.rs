use super::*;
use std::{fs::File, time::Instant};

fn font(name: &str) -> Arc<SoundFont> {
    Arc::new(
        SoundFont::new(
            &mut File::open(format!(
                "{}/../../src-tauri/resources/{name}",
                env!("CARGO_MANIFEST_DIR")
            ))
            .unwrap(),
        )
        .unwrap(),
    )
}
fn stack(sf: Arc<SoundFont>) -> Box<FontStack> {
    Box::new(FontStack::new(vec![sf], &SynthesizerSettings::new(SAMPLE_RATE as i32)).unwrap())
}
fn engine(sf: Arc<SoundFont>) -> (SynthEngine, Sender<AudioCommand>) {
    let (tx, rx) = unbounded();
    (SynthEngine::new(Some(sf), rx), tx)
}
fn render(engine: &mut SynthEngine) -> Vec<f32> {
    let mut out = Vec::with_capacity(BLOCK_SIZE * 2);
    engine.render_block(&mut out);
    out
}

#[test]
fn reused_output_buffer_contains_only_the_current_audio_block() {
    let (_tx, rx) = unbounded();
    let mut engine = SynthEngine::new(None, rx);
    let mut output = vec![0.75; BLOCK_SIZE * 2];
    for _ in 0..64 {
        engine.render_block(&mut output);
        assert_eq!(
            output.len(),
            BLOCK_SIZE * 2,
            "old samples must be replaced, not appended"
        );
        assert!(output.iter().all(|sample| *sample == 0.0));
    }
}

#[test]
fn streaming_source_never_replays_old_audio_across_switch_stop_and_restart() {
    let tim = font("TimGM6mb.sf2");
    let gm = font("GeneralUser-GS.sf2");
    let (stream_engine, tx) = engine(tim.clone());
    let shared = Arc::new(Mutex::new(stream_engine));
    let mut source = SynthSource::new(shared.clone());
    let (mut reference, ref_tx) = engine(tim);
    for block in 0..48 {
        if block == 8 {
            lock_engine(&shared).replace_fonts(stack(gm.clone()));
            reference.replace_fonts(stack(gm.clone()));
        }
        for sender in [&tx, &ref_tx] {
            match block {
                0 | 24 => {
                    send(
                        sender,
                        AudioCommand::ProgramChange {
                            channel: 0,
                            program: 78,
                        },
                    );
                    send(
                        sender,
                        AudioCommand::NoteOn {
                            channel: 0,
                            note: 72,
                            velocity: 90,
                        },
                    );
                }
                12 => send(
                    sender,
                    AudioCommand::NoteOff {
                        channel: 0,
                        note: 72,
                    },
                ),
                16 => send(sender, AudioCommand::AllNotesOff),
                32 => send(sender, AudioCommand::Reset),
                _ => {}
            }
        }
        let expected = render(&mut reference);
        let actual: Vec<_> = source.by_ref().take(BLOCK_SIZE * 2).collect();
        assert_eq!(
            source.block_buffer.len(),
            BLOCK_SIZE * 2,
            "streaming buffer grew at block {block}"
        );
        assert!(
            actual == expected,
            "stream replayed stale or duplicated samples at block {block}"
        );
        if (16..24).contains(&block) || block >= 32 {
            assert!(
                actual.iter().all(|sample| *sample == 0.0),
                "stop/reset left stale audio at block {block}"
            );
        }
    }
}

fn send(tx: &Sender<AudioCommand>, cmd: AudioCommand) {
    tx.send(cmd).unwrap();
}

#[test]
fn sounding_note_is_sample_identical_across_handoff_and_new_notes_use_new_font() {
    let tim = font("TimGM6mb.sf2");
    let gm = font("GeneralUser-GS.sf2");
    let (mut switched, tx) = engine(tim.clone());
    let (mut reference, ref_tx) = engine(tim);
    for sender in [&tx, &ref_tx] {
        send(
            sender,
            AudioCommand::ProgramChange {
                channel: 0,
                program: 78,
            },
        );
        send(
            sender,
            AudioCommand::ControlChange {
                channel: 0,
                controller: 64,
                value: 127,
            },
        );
        send(
            sender,
            AudioCommand::NoteOn {
                channel: 0,
                note: 72,
                velocity: 90,
            },
        );
    }
    for _ in 0..8 {
        assert_eq!(render(&mut switched), render(&mut reference));
    }
    switched.replace_fonts(stack(gm.clone()));
    assert_eq!(switched.retiring.len(), 1);
    for _ in 0..8 {
        assert_eq!(render(&mut switched), render(&mut reference));
    }
    for sender in [&tx, &ref_tx] {
        send(
            sender,
            AudioCommand::NoteOff {
                channel: 0,
                note: 72,
            },
        );
    }
    for _ in 0..8 {
        assert_eq!(render(&mut switched), render(&mut reference));
    }
    assert!(
        switched.retiring[0].stack.has_pending_notes(),
        "sustain keeps retiring notes alive"
    );
    // A new whistle note in TimGM's gap must go only to the new SoundFont.
    send(
        &tx,
        AudioCommand::NoteOn {
            channel: 0,
            note: 88,
            velocity: 100,
        },
    );
    let (mut new_reference, new_tx) = engine(gm);
    send(
        &new_tx,
        AudioCommand::ProgramChange {
            channel: 0,
            program: 78,
        },
    );
    send(
        &new_tx,
        AudioCommand::ControlChange {
            channel: 0,
            controller: 64,
            value: 127,
        },
    );
    send(
        &new_tx,
        AudioCommand::NoteOn {
            channel: 0,
            note: 88,
            velocity: 100,
        },
    );
    for _ in 0..8 {
        let old = render(&mut reference);
        let new = render(&mut new_reference);
        let mixed = render(&mut switched);
        for ((a, b), actual) in old.into_iter().zip(new).zip(mixed) {
            assert!((actual - (a + b).clamp(-1.0, 1.0)).abs() < 0.000001);
        }
    }
    send(
        &tx,
        AudioCommand::NoteOff {
            channel: 0,
            note: 88,
        },
    );
    send(
        &tx,
        AudioCommand::ControlChange {
            channel: 0,
            controller: 64,
            value: 0,
        },
    );
    // Release and effect tails are allowed to finish, not cut at the switch.
    for _ in 0..10000 {
        render(&mut switched);
        if switched.retiring.is_empty() {
            break;
        }
    }
    assert!(switched.retiring.is_empty());
}

#[test]
fn held_silent_notes_are_not_reclaimed_and_panic_and_reset_silence_every_stack() {
    let sf = font("TimGM6mb.sf2");
    let (mut engine, tx) = engine(sf.clone());
    send(
        &tx,
        AudioCommand::ControlChange {
            channel: 0,
            controller: 7,
            value: 0,
        },
    );
    send(
        &tx,
        AudioCommand::ProgramChange {
            channel: 0,
            program: 78,
        },
    );
    send(
        &tx,
        AudioCommand::NoteOn {
            channel: 0,
            note: 72,
            velocity: 100,
        },
    );
    render(&mut engine);
    engine.replace_fonts(stack(sf.clone()));
    for _ in 0..100 {
        render(&mut engine);
    }
    assert_eq!(engine.retiring.len(), 1);
    send(&tx, AudioCommand::AllNotesOff);
    assert!(render(&mut engine).iter().all(|v| *v == 0.0));
    assert!(engine.retiring.is_empty());
    assert_eq!(engine.channel_programs[0], 78);
    // Panic preserves channel programs/volume for playback that follows.
    send(
        &tx,
        AudioCommand::ControlChange {
            channel: 0,
            controller: 7,
            value: 100,
        },
    );
    send(
        &tx,
        AudioCommand::NoteOn {
            channel: 0,
            note: 72,
            velocity: 100,
        },
    );
    assert!(render(&mut engine).iter().any(|v| v.abs() > 0.00001));
    engine.replace_fonts(stack(sf));
    send(&tx, AudioCommand::Reset);
    assert!(render(&mut engine).iter().all(|v| *v == 0.0));
    assert!(engine.retiring.is_empty());
    assert_eq!(engine.channel_programs, [0; 16]);
}

fn await_handoff(
    engine: &mut SynthEngine,
    rx: &Receiver<Result<(), String>>,
) -> Result<(), String> {
    let deadline = Instant::now() + Duration::from_secs(10);
    loop {
        render(engine);
        if let Ok(result) = rx.try_recv() {
            return result;
        }
        assert!(Instant::now() < deadline, "loader must acknowledge handoff");
        std::thread::sleep(Duration::from_millis(1));
    }
}

#[test]
fn preparation_is_background_acknowledged_latest_wins_and_failure_keeps_playback() {
    let sf = font("TimGM6mb.sf2");
    let (mut engine, tx) = engine(sf.clone());
    let loader = FontLoader::new(tx.clone(), SAMPLE_RATE);
    send(
        &tx,
        AudioCommand::NoteOn {
            channel: 0,
            note: 60,
            velocity: 100,
        },
    );
    render(&mut engine);
    let failed = loader
        .submit(FontInput::Paths(vec![std::path::PathBuf::from(
            "does-not-exist.sf2",
        )]))
        .unwrap();
    assert!(await_handoff(&mut engine, &failed).is_err());
    assert!(engine.retiring.is_empty());
    assert!(render(&mut engine).iter().any(|v| v.abs() > 0.00001));
    let mut pending = Vec::new();
    for _ in 0..8 {
        pending.push(loader.submit(FontInput::Fonts(vec![sf.clone()])).unwrap());
    }
    // Preparation alone cannot replace a live synth before the audio block.
    std::thread::sleep(Duration::from_millis(50));
    assert!(pending.last().unwrap().try_recv().is_err());
    await_handoff(&mut engine, pending.last().unwrap()).unwrap();
    for old in &pending[..pending.len() - 1] {
        assert!(old.recv_timeout(Duration::from_secs(1)).unwrap().is_err());
    }
    assert_eq!(
        engine.retiring.len(),
        1,
        "superseded stacks never become active"
    );
}

#[test]
fn handoff_restores_latest_controllers_and_applies_master_volume_to_both_stacks() {
    let sf = font("TimGM6mb.sf2");
    let (mut switched, tx) = engine(sf.clone());
    let (mut reference, ref_tx) = engine(sf.clone());
    for sender in [&tx, &ref_tx] {
        send(
            sender,
            AudioCommand::ProgramChange {
                channel: 0,
                program: 78,
            },
        );
        send(
            sender,
            AudioCommand::NoteOn {
                channel: 0,
                note: 72,
                velocity: 100,
            },
        );
    }
    render(&mut switched);
    render(&mut reference);
    // Pretend preparation took time: use the state at handoff, not at request time.
    let prepared = stack(sf);
    for sender in [&tx, &ref_tx] {
        for (controller, value) in [(7, 70), (10, 20), (11, 85)] {
            send(
                sender,
                AudioCommand::ControlChange {
                    channel: 0,
                    controller,
                    value,
                },
            );
        }
    }
    switched.process_pending_commands();
    reference.process_pending_commands();
    switched.replace_fonts(prepared);
    let actual = render(&mut switched);
    let expected = render(&mut reference);
    assert_eq!(actual, expected, "controllers reach retiring voices");
    let mut fresh = stack(font("TimGM6mb.sf2"));
    reference.restore_channels(&mut fresh);
    switched.synth.as_mut().unwrap().note_on(0, 74, 90);
    fresh.note_on(0, 74, 90);
    switched.master_volume = 0.25;
    let old = render(&mut reference);
    let mut l = vec![0.0; BLOCK_SIZE];
    let mut r = l.clone();
    fresh.render(&mut l, &mut r);
    let mixed = render(&mut switched);
    let default_volume = reference.master_volume;
    for i in 0..BLOCK_SIZE {
        for (j, new) in [(i * 2, l[i]), (i * 2 + 1, r[i])] {
            assert!(
                (mixed[j] - ((old[j] / default_volume + new) * 0.25).clamp(-1.0, 1.0)).abs()
                    < 0.000001
            );
        }
    }
}
