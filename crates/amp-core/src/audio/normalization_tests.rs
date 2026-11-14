use super::*;
use std::fs::File;

fn font(name: &str) -> Arc<SoundFont> {
    let path = format!(
        "{}/../../src-tauri/resources/soundfonts/{name}",
        env!("CARGO_MANIFEST_DIR")
    );
    Arc::new(SoundFont::new(&mut File::open(path).unwrap()).unwrap())
}

fn engine(font: Arc<SoundFont>, normalized: bool) -> (SynthEngine, Sender<AudioCommand>) {
    let (tx, rx) = unbounded();
    let mut engine = SynthEngine::new(Some(font), rx);
    if !normalized {
        engine.normalizer = VolumeNormalizer::new(SAMPLE_RATE, false);
    }
    (engine, tx)
}

fn send(tx: &Sender<AudioCommand>, command: AudioCommand) {
    tx.send(command).unwrap();
}

fn start_solo(tx: &Sender<AudioCommand>, program: u8, note: u8) {
    send(
        tx,
        AudioCommand::ProgramChange {
            channel: 0,
            program,
        },
    );
    send(
        tx,
        AudioCommand::NoteOn {
            channel: 0,
            note,
            velocity: 80,
        },
    );
}

fn render(engine: &mut SynthEngine) -> Vec<f32> {
    let mut output = Vec::with_capacity(BLOCK_SIZE * 2);
    engine.render_block(&mut output);
    output
}

fn measure(engine: &mut SynthEngine, blocks: usize) -> (f64, f32) {
    let mut energy = 0.0_f64;
    let mut peak = 0.0_f32;
    for _ in 0..blocks {
        for sample in render(engine) {
            energy += (sample as f64).powi(2);
            peak = peak.max(sample.abs());
        }
    }
    ((energy / (blocks * BLOCK_SIZE * 2) as f64).sqrt(), peak)
}

fn start_whistle(tx: &Sender<AudioCommand>, channel: u8) {
    send(
        tx,
        AudioCommand::ProgramChange {
            channel,
            program: 78,
        },
    );
    send(
        tx,
        AudioCommand::NoteOn {
            channel,
            note: 72,
            velocity: 80,
        },
    );
}

fn start_whistle_stack(tx: &Sender<AudioCommand>) {
    // Equal velocities deliberately isolate stacking from performance strength.
    for channel in (0..16).filter(|channel| *channel != 9) {
        start_whistle(tx, channel);
    }
}

#[test]
fn bundled_solos_receive_useful_bounded_normalization_at_unity_synth_gain() {
    assert!(crate::settings::defaults::value::<bool>(
        "/volumeNormalization"
    ));
    for name in ["Roland GM.sf2", "GeneralUser GS.sf2"] {
        let font = font(name);
        for (instrument, program, note) in [("piano", 0, 60), ("whistle", 78, 72)] {
            let (mut normalized, normalized_tx) = engine(font.clone(), true);
            let (mut dry, dry_tx) = engine(font.clone(), false);
            for tx in [&normalized_tx, &dry_tx] {
                start_solo(tx, program, note);
            }
            let mut normalized_energy = 0.0_f64;
            let mut dry_energy = 0.0_f64;
            let blocks = 260;
            for _ in 0..blocks {
                normalized_energy += render(&mut normalized)
                    .iter()
                    .map(|sample| (*sample as f64).powi(2))
                    .sum::<f64>();
                dry_energy += render(&mut dry)
                    .iter()
                    .map(|sample| (*sample as f64).powi(2))
                    .sum::<f64>();
            }
            let samples = (blocks * BLOCK_SIZE * 2) as f64;
            let normalized_rms = (normalized_energy / samples).sqrt();
            let dry_rms = (dry_energy / samples).sqrt();
            let lift_db = 20.0 * (normalized_rms / dry_rms).log10();
            println!("{name}, solo {instrument}: dry RMS {dry_rms:.6}, normalized RMS {normalized_rms:.6}, lift {lift_db:.2} dB");
            assert!(
                dry_rms > 0.0001,
                "{name} {instrument} must render audible reference samples"
            );
            if instrument == "piano" {
                assert!(lift_db > 1.0,
                    "{name} quiet piano should still receive useful lift with unity synth gain: {lift_db:.2} dB");
            } else {
                // Unity gain can already put a solo instrument near the target;
                // normalization should not add gain simply to meet a lift quota.
                assert!(
                    lift_db >= -1.0,
                    "{name} moderate solo whistle should retain a useful level: {lift_db:.2} dB"
                );
            }
            assert!(
                lift_db <= 30.20,
                "{name} {instrument} must respect the bounded gain"
            );
        }
    }
}

#[test]
fn equal_velocity_stacks_stay_in_a_comfortable_range_and_master_still_scales() {
    for name in ["Roland GM.sf2", "GeneralUser GS.sf2"] {
        let font = font(name);
        let mut results = Vec::new();
        for volume in [1.0, 2.0] {
            let (mut solo, solo_tx) = engine(font.clone(), true);
            let (mut stacked, stacked_tx) = engine(font.clone(), true);
            let (mut dry_stack, dry_tx) = engine(font.clone(), false);
            for tx in [&solo_tx, &stacked_tx, &dry_tx] {
                send(tx, AudioCommand::SetMasterVolume(volume));
            }
            start_whistle(&solo_tx, 0);
            start_whistle_stack(&stacked_tx);
            start_whistle_stack(&dry_tx);
            // Allow the solo's upward gain to settle; reduction must also work
            // when fifteen coherent voices require much more than 12 dB down.
            for _ in 0..600 {
                render(&mut solo);
                render(&mut stacked);
                render(&mut dry_stack);
            }
            let (solo_rms, _) = measure(&mut solo, 173);
            let (stacked_rms, peak) = measure(&mut stacked, 173);
            let (dry_rms, _) = measure(&mut dry_stack, 173);
            let difference_db = 20.0 * (stacked_rms / solo_rms).log10();
            println!("{name}, equal-velocity solo/15 voices at {}%: solo RMS {solo_rms:.6}, stacked RMS {stacked_rms:.6}, dry stack RMS {dry_rms:.6}, difference {difference_db:.2} dB", volume * 100.0);
            // A normal-volume band intentionally preserves differences instead
            // of forcing every passage to the exact same loudness.
            assert!(
                (0.08..=0.21).contains(&(solo_rms / volume as f64)),
                "{name}: a solo should retain a comfortable listening level: {solo_rms}"
            );
            assert!((0.08..=0.21).contains(&(stacked_rms / volume as f64)),
                "{name}: dense average level should be controlled before Master Volume: {stacked_rms}");
            assert!(
                dry_rms > stacked_rms * 2.0,
                "{name}: the reference must contain a substantially louder unnormalized stack"
            );
            assert!(peak <= 0.95001);
            results.push((solo_rms, stacked_rms));
        }
        for (full, doubled) in [(results[0].0, results[1].0), (results[0].1, results[1].1)] {
            assert!((doubled / full - 2.0).abs() < 0.02,
                "{name}: Master Volume must multiply the normalized level instead of being undone by normalization");
        }
    }
}

#[test]
fn sudden_equal_velocity_stacking_is_reduced_within_the_first_tenth_of_a_second() {
    for name in ["Roland GM.sf2", "GeneralUser GS.sf2"] {
        let (mut audio, tx) = engine(font(name), true);
        send(&tx, AudioCommand::SetMasterVolume(1.0));
        start_whistle(&tx, 0);
        for _ in 0..600 {
            render(&mut audio);
        }
        let (solo_rms, _) = measure(&mut audio, 80);
        for channel in (1..16).filter(|channel| *channel != 9) {
            start_whistle(&tx, channel);
        }
        // Inspect the 52–104 ms window, after the brief lookahead/attack but
        // while the former 80 ms reduction would still leave a loud burst.
        for _ in 0..9 {
            render(&mut audio);
        }
        let (onset_rms, peak) = measure(&mut audio, 9);
        println!("{name}, sudden 1-to-15 voice stack: solo RMS {solo_rms:.6}, early dense RMS {onset_rms:.6}");
        assert!(
            onset_rms <= 0.30 && onset_rms < solo_rms * 3.0,
            "{name}: stacked onsets must not linger at peak-limited loudness: {onset_rms}"
        );
        assert!(peak <= 0.95001);
        for _ in 0..200 {
            render(&mut audio);
        }
        let (settled_rms, _) = measure(&mut audio, 80);
        assert!(
            settled_rms < 0.22,
            "{name}: dense sustain should remain normalized, not just peak limited"
        );
    }
}

#[test]
fn short_quiet_piano_notes_receive_lift_during_the_first_note_and_phrase() {
    for name in ["Roland GM.sf2", "GeneralUser GS.sf2"] {
        let font = font(name);
        let (mut normalized, normalized_tx) = engine(font.clone(), true);
        let (mut dry, dry_tx) = engine(font, false);
        for tx in [&normalized_tx, &dry_tx] {
            send(tx, AudioCommand::SetMasterVolume(1.0));
        }
        let mut first_normalized_energy = 0.0_f64;
        let mut first_dry_energy = 0.0_f64;
        let mut phrase_normalized_energy = 0.0_f64;
        let mut phrase_dry_energy = 0.0_f64;
        for (index, note) in [60, 64, 67].into_iter().enumerate() {
            for tx in [&normalized_tx, &dry_tx] {
                send(
                    tx,
                    AudioCommand::NoteOn {
                        channel: 0,
                        note,
                        velocity: 35,
                    },
                );
            }
            // About 122 ms held, then 99 ms released. Long sustain-only tests
            // miss an upward gain response that is too slow for real melodies.
            for _ in 0..21 {
                let a = render(&mut normalized);
                let b = render(&mut dry);
                assert!(a.iter().all(|v| v.is_finite() && v.abs() <= 0.95001));
                let a_energy = a.iter().map(|v| (*v as f64).powi(2)).sum::<f64>();
                let b_energy = b.iter().map(|v| (*v as f64).powi(2)).sum::<f64>();
                phrase_normalized_energy += a_energy;
                phrase_dry_energy += b_energy;
                if index == 0 {
                    first_normalized_energy += a_energy;
                    first_dry_energy += b_energy;
                }
            }
            for tx in [&normalized_tx, &dry_tx] {
                send(tx, AudioCommand::NoteOff { channel: 0, note });
            }
            for _ in 0..17 {
                phrase_normalized_energy += render(&mut normalized)
                    .iter()
                    .map(|v| (*v as f64).powi(2))
                    .sum::<f64>();
                phrase_dry_energy += render(&mut dry)
                    .iter()
                    .map(|v| (*v as f64).powi(2))
                    .sum::<f64>();
            }
        }
        assert!(
            first_dry_energy > 0.000001,
            "{name}: quiet reference must be audible"
        );
        let first_lift_db = 10.0 * (first_normalized_energy / first_dry_energy).log10();
        let phrase_lift_db = 10.0 * (phrase_normalized_energy / phrase_dry_energy).log10();
        println!("{name}, short quiet piano: first-note lift {first_lift_db:.2} dB, phrase lift {phrase_lift_db:.2} dB");
        assert!(
            first_lift_db > 3.0,
            "{name}: quiet notes need useful lift within the first note"
        );
        assert!(
            phrase_lift_db > 6.0,
            "{name}: a short quiet melody must not wait seconds for useful lift"
        );
        assert!(
            first_lift_db <= 30.20 && phrase_lift_db <= 30.20,
            "{name}: quiet-note lift must remain bounded"
        );
    }
}

#[test]
fn normal_volume_whistles_keep_their_level_and_master_multiplier() {
    for name in ["Roland GM.sf2", "GeneralUser GS.sf2"] {
        let font = font(name);
        // The fonts have different recorded levels, so use a strike strength
        // that places each reference inside the normal-volume band.
        let velocity = if name == "Roland GM.sf2" { 100 } else { 115 };
        let mut results = Vec::new();
        for volume in [1.0, 2.0] {
            let (mut normalized, normalized_tx) = engine(font.clone(), true);
            let (mut dry, dry_tx) = engine(font.clone(), false);
            for tx in [&normalized_tx, &dry_tx] {
                send(tx, AudioCommand::SetMasterVolume(volume));
                send(
                    tx,
                    AudioCommand::ProgramChange {
                        channel: 0,
                        program: 78,
                    },
                );
                send(
                    tx,
                    AudioCommand::NoteOn {
                        channel: 0,
                        note: 72,
                        velocity,
                    },
                );
            }
            for _ in 0..300 {
                render(&mut normalized);
                render(&mut dry);
            }
            let (normalized_rms, _) = measure(&mut normalized, 173);
            let (dry_rms, _) = measure(&mut dry, 173);
            let change_db = 20.0 * (normalized_rms / dry_rms).log10();
            println!("{name}, normal whistle at {}%: dry RMS {dry_rms:.6}, normalized RMS {normalized_rms:.6}, change {change_db:.3} dB", volume * 100.0);
            assert!(
                (0.125..=0.20).contains(&(dry_rms / volume as f64)),
                "{name}: reference must exercise normal-range audio"
            );
            assert!(
                change_db.abs() < 0.5,
                "{name}: normal-range music should keep its level, got {change_db:.3} dB"
            );
            results.push(normalized_rms);
        }
        assert!(
            (results[1] / results[0] - 2.0).abs() < 0.02,
            "{name}: normal-range audio must still respond to Master Volume"
        );
    }
}

#[test]
fn live_toggle_becomes_sample_exact_dry_and_reset_preserves_disabled_setting() {
    let font = font("GeneralUser GS.sf2");
    let (mut normalized, normalized_tx) = engine(font.clone(), true);
    let (mut dry, dry_tx) = engine(font, false);
    for tx in [&normalized_tx, &dry_tx] {
        start_solo(tx, 78, 72);
    }
    let mut was_different = false;
    for _ in 0..60 {
        was_different |= render(&mut normalized) != render(&mut dry);
    }
    assert!(
        was_different,
        "the default enabled setting must affect sounding notes"
    );

    send(&normalized_tx, AudioCommand::SetVolumeNormalization(false));
    // Allow the 20 ms crossfade and the shared 256-frame lookahead to settle.
    for _ in 0..6 {
        render(&mut normalized);
        render(&mut dry);
    }
    for _ in 0..40 {
        assert_eq!(render(&mut normalized), render(&mut dry));
    }

    for tx in [&normalized_tx, &dry_tx] {
        send(tx, AudioCommand::Reset);
    }
    assert!(
        render(&mut normalized).iter().all(|sample| *sample == 0.0),
        "reset must clear buffered audio"
    );
    assert!(render(&mut dry).iter().all(|sample| *sample == 0.0));
    for tx in [&normalized_tx, &dry_tx] {
        start_solo(tx, 0, 60);
    }
    let mut audible = false;
    for _ in 0..40 {
        let normalized_output = render(&mut normalized);
        audible |= normalized_output.iter().any(|sample| sample.abs() > 0.0001);
        assert_eq!(
            normalized_output,
            render(&mut dry),
            "reset must preserve the disabled preference"
        );
    }
    assert!(audible);
}

#[test]
fn dense_bundled_chords_at_two_hundred_percent_volume_have_protected_peaks() {
    for name in ["Roland GM.sf2", "GeneralUser GS.sf2"] {
        let font = font(name);
        let (mut normalized, normalized_tx) = engine(font.clone(), true);
        let (mut dry, dry_tx) = engine(font, false);
        for tx in [&normalized_tx, &dry_tx] {
            send(tx, AudioCommand::SetMasterVolume(2.0));
            // Identical simultaneous piano voices on separate MIDI channels
            // produce the dense, coherent stack that would otherwise clip.
            for channel in (0..16).filter(|channel| *channel != 9) {
                for note in [60, 64, 67] {
                    send(
                        tx,
                        AudioCommand::NoteOn {
                            channel,
                            note,
                            velocity: 127,
                        },
                    );
                }
            }
        }
        let mut protected_peak = 0.0_f32;
        let mut dry_peak = 0.0_f32;
        for _ in 0..200 {
            for sample in render(&mut normalized) {
                assert!(sample.is_finite());
                protected_peak = protected_peak.max(sample.abs());
            }
            for sample in render(&mut dry) {
                dry_peak = dry_peak.max(sample.abs());
            }
        }
        println!("{name}, dense chord at 200%: dry peak {dry_peak:.6}, protected peak {protected_peak:.6}");
        assert!(
            dry_peak > 0.95,
            "the reference stack must exercise peak protection for {name}"
        );
        assert!(
            protected_peak > 0.1,
            "protection must retain audible output"
        );
        assert!(
            protected_peak <= 0.95001,
            "{name} dense peaks must stay below the ceiling: {protected_peak}"
        );
    }
}
