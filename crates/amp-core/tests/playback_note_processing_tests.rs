use amp_core::{
    input::{mapping::ChordPad, simulator::KeyboardOutput},
    player::{live_input::MidiMessage, NoteProcessingSettings},
    parse_key_stroke, KeyboardLayout, NoteKeyMapping, OutputKey, ParsedMidi, PlaybackEngine,
    PlaybackStatus, PlayerEvent, SynthMode, TempoMap, TimedNoteEvent,
};
use std::{
    sync::{Arc, Mutex},
    time::{Duration, Instant},
};

type KeyEvents = Arc<Mutex<Vec<(OutputKey, bool)>>>;

struct Output(KeyEvents);

impl KeyboardOutput for Output {
    fn key(&mut self, key: OutputKey, up: bool) -> Result<(), String> {
        self.0.lock().unwrap().push((key, up));
        Ok(())
    }
}

fn key(binding: &str) -> OutputKey {
    parse_key_stroke(binding).unwrap().key
}

fn wait(mut predicate: impl FnMut() -> bool) {
    let deadline = Instant::now() + Duration::from_secs(2);
    while !predicate() {
        assert!(Instant::now() < deadline, "playback did not reach expected state");
        std::thread::sleep(Duration::from_millis(2));
    }
}

fn event(time_us: u64, track_index: usize, pitch: u8, on: bool) -> TimedNoteEvent {
    TimedNoteEvent {
        time_us, track_index, channel: 0, note: pitch,
        velocity: if on { 90 } else { 0 }, is_note_on: on,
    }
}

fn song(mut events: Vec<TimedNoteEvent>) -> ParsedMidi {
    events.sort();
    ParsedMidi {
        title: "Note processing regression".into(),
        duration_us: events.last().map_or(1_000_000, |event| event.time_us + 50_000),
        bpm: 120.0, ppqn: 480, tracks: vec![], events, control_events: vec![],
        tempo_map: TempoMap::new(480, &[]), key_signatures: vec![],
    }
}

fn mapping() -> NoteKeyMapping {
    NoteKeyMapping::from_pairs(KeyboardLayout::Diatonic21,
        &[(60, 'a'), (64, 'd'), (65, 'f'), (67, 'g'), (69, 'h')])
        .with_chord_bindings(vec![ChordPad {
            pitch_classes: vec![0, 4, 7], stroke: parse_key_stroke("q").unwrap(),
        }])
}

struct Harness {
    engine: PlaybackEngine,
    output: KeyEvents,
    hits: Arc<Mutex<Vec<(u8, bool)>>>,
}

impl Harness {
    fn new(events: Vec<TimedNoteEvent>) -> Self {
        let output = Arc::new(Mutex::new(Vec::new()));
        let hits = Arc::new(Mutex::new(Vec::new()));
        let captured_hits = hits.clone();
        let engine = PlaybackEngine::new(Some(song(events)), mapping(), true, None, move |event| {
            if let PlayerEvent::NoteHit { note, is_on } = event {
                captured_hits.lock().unwrap().push((note, is_on));
            }
        });
        engine.set_keyboard_output(Some(Box::new(Output(output.clone()))));
        engine.set_note_timing(true, 0);
        engine.set_hold_delay_ms(20);
        engine.flush().unwrap();
        Self { engine, output, hits }
    }

    fn count(&self, binding: &str, up: bool) -> usize {
        self.output.lock().unwrap().iter().filter(|&&(output, released)|
            output == key(binding) && released == up).count()
    }

    fn run_to_end(&self) {
        self.engine.play();
        wait(|| self.count("a", false) + self.count("q", false) > 0);
        wait(|| self.engine.get_status() == PlaybackStatus::Stopped);
    }
}

fn settings(merge_notes: bool, auto_chord: bool) -> NoteProcessingSettings {
    NoteProcessingSettings {
        merge_notes, merge_tolerance_ms: 25, auto_chord,
        chord_detection_milliseconds: 30,
    }
}

fn triad(last_pitch: u8, onset: u64, end: u64) -> Vec<TimedNoteEvent> {
    vec![
        event(onset, 0, 60, true), event(onset + 10_000, 1, 64, true),
        event(onset + 20_000, 2, last_pitch, true),
        event(end, 0, 60, false), event(end, 1, 64, false),
        event(end, 2, last_pitch, false),
    ]
}

#[test]
fn disabled_processing_preserves_repeated_note_attacks_and_releases() {
    let h = Harness::new(vec![
        event(0, 0, 60, true), event(60_000, 0, 60, false),
        event(80_000, 0, 60, true), event(140_000, 0, 60, false),
    ]);
    h.engine.set_note_processing(settings(false, false));
    h.run_to_end();
    assert_eq!(*h.output.lock().unwrap(), vec![
        (key("a"), false), (key("a"), true), (key("a"), false), (key("a"), true),
    ]);
}

#[test]
fn merging_bridges_the_gap_without_retrigger_and_disabling_restores_original_notes() {
    let h = Harness::new(vec![
        event(0, 0, 60, true), event(60_000, 0, 60, false),
        event(80_000, 0, 60, true), event(140_000, 0, 60, false),
    ]);
    h.engine.set_note_processing(settings(true, false));
    h.run_to_end();
    assert_eq!(*h.output.lock().unwrap(), vec![(key("a"), false), (key("a"), true)]);
    assert_eq!(*h.hits.lock().unwrap(), vec![(60, true), (60, false)]);

    h.engine.stop();
    h.engine.set_note_processing(settings(false, false));
    h.engine.flush().unwrap();
    h.output.lock().unwrap().clear();
    h.hits.lock().unwrap().clear();
    h.run_to_end();
    assert_eq!(h.count("a", false), 2, "disabling merge must rebuild from original MIDI");
    assert_eq!(h.count("a", true), 2);
}

#[test]
fn disabling_merge_during_a_bridged_gap_releases_the_hold_and_keeps_playback_position() {
    let h = Harness::new(vec![
        event(0, 0, 60, true), event(60_000, 0, 60, false),
        event(500_000, 0, 60, true), event(560_000, 0, 60, false),
    ]);
    let mut processing = settings(true, false);
    processing.merge_tolerance_ms = 500;
    h.engine.set_note_processing(processing);
    h.engine.play();
    wait(|| h.engine.get_current_time_us() >= 160_000);
    assert_eq!(h.count("a", false), 1);
    assert_eq!(h.count("a", true), 0, "merged note spans the source gap");
    processing.merge_notes = false;
    h.engine.set_note_processing(processing);
    wait(|| h.count("a", true) == 1);
    assert!(h.engine.get_current_time_us() >= 160_000, "live changes must not restart the song");
    wait(|| h.count("a", false) == 2);
    wait(|| h.engine.get_status() == PlaybackStatus::Stopped);
    assert_eq!(h.count("a", true), 2);
}

#[test]
fn a_cross_track_chord_taps_one_pad_and_keeps_constituent_note_glow() {
    let h = Harness::new(triad(67, 0, 300_000));
    h.engine.set_note_processing(settings(false, true));
    h.engine.play();
    wait(|| h.count("q", true) == 1);
    wait(|| h.hits.lock().unwrap().iter().filter(|(_, on)| *on).count() == 3);
    assert_eq!(h.count("q", false), 1);
    for binding in ["a", "d", "g"] {
        assert_eq!(h.count(binding, false), 0, "chord members must not also press melody keys");
    }
    assert_eq!(h.engine.get_status(), PlaybackStatus::Playing,
        "pad must release before the held source notes end");
    let mut onsets: Vec<_> = h.hits.lock().unwrap().iter()
        .filter_map(|&(pitch, on)| on.then_some(pitch)).collect();
    onsets.sort_unstable();
    assert_eq!(onsets, vec![60, 64, 67]);
    wait(|| h.engine.get_status() == PlaybackStatus::Stopped);
    assert_eq!(h.count("q", false), 1, "source note offs cannot retrigger a pad");
}

#[test]
fn a_short_source_chord_does_not_cut_off_its_longer_pad_tap() {
    let h = Harness::new(triad(67, 0, 40_000));
    h.engine.set_hold_delay_ms(200);
    h.engine.set_note_processing(settings(false, true));
    h.engine.play();
    wait(|| h.count("q", false) == 1);
    let started = Instant::now();
    wait(|| h.hits.lock().unwrap().iter().filter(|(_, on)| !*on).count() == 3);
    assert_eq!(h.count("q", true), 0,
        "source NoteOff must not release the independently timed pad");
    wait(|| h.count("q", true) == 1);
    assert!(started.elapsed() >= Duration::from_millis(150));
    wait(|| h.engine.get_status() == PlaybackStatus::Stopped);
}

#[test]
fn disabled_unmatched_and_muted_chords_keep_individual_melody_output() {
    for scenario in ["disabled", "unmatched", "muted", "zero_volume"] {
        let unmatched = scenario == "unmatched";
        let h = Harness::new(triad(if unmatched { 69 } else { 67 }, 0, 160_000));
        h.engine.set_note_processing(settings(false, scenario != "disabled"));
        if scenario == "muted" { h.engine.set_track_muted(1, true); }
        if scenario == "zero_volume" { h.engine.set_track_volume(1, 0.0); }
        h.run_to_end();
        assert_eq!(h.count("q", false), 0, "{scenario}: incomplete/nonmatching chord used pad");
        assert_eq!(h.count("a", false), 1, "{scenario}");
        assert_eq!(h.count(if unmatched { "h" } else { "g" }, false), 1, "{scenario}");
        assert_eq!(h.count("d", false), usize::from(scenario != "muted" && scenario != "zero_volume"));
    }
}

#[test]
fn mid_chord_processing_and_track_changes_do_not_mix_pad_and_melody_output() {
    for change in ["disable", "mute", "zero_volume"] {
        let h = Harness::new(vec![
            event(0, 0, 60, true), event(60_000, 1, 64, true), event(120_000, 2, 67, true),
            event(180_000, 0, 60, false), event(180_000, 1, 64, false), event(180_000, 2, 67, false),
        ]);
        let processing = NoteProcessingSettings { chord_detection_milliseconds: 150, ..settings(false, true) };
        h.engine.set_note_processing(processing);
        h.engine.play();
        wait(|| h.count("q", false) == 1 && h.engine.get_current_time_us() >= 30_000);
        match change {
            "disable" => h.engine.set_note_processing(settings(false, false)),
            "mute" => h.engine.set_track_muted(2, true),
            _ => h.engine.set_track_volume(2, 0.0),
        }
        h.engine.flush().unwrap();
        wait(|| h.engine.get_status() == PlaybackStatus::Stopped);
        assert_eq!(h.count("q", false), 1, "{change}: replanning retriggered a started chord");
        for binding in ["a", "d", "g"] {
            assert_eq!(h.count(binding, false), 0,
                "{change}: later members of an already-played pad leaked melody output");
        }

        h.engine.stop();
        h.engine.set_note_processing(settings(false, false));
        h.engine.set_track_muted(2, false);
        h.engine.set_track_volume(2, 1.0);
        h.engine.flush().unwrap();
        h.output.lock().unwrap().clear();
        h.run_to_end();
        assert_eq!(h.count("q", false), 0, "{change}: stopped song retained old chord plan");
        for binding in ["a", "d", "g"] {
            assert_eq!(h.count(binding, false), 1, "{change}: disabled replay lost melody notes");
            assert_eq!(h.count(binding, true), 1, "{change}: disabled replay lost melody releases");
        }
    }
}

#[test]
fn auto_chord_preserves_raw_and_adapted_listen_note_glow_without_keyboard_output() {
    for mode in [SynthMode::Raw, SynthMode::Adapted] {
        let h = Harness::new(triad(67, 0, 120_000));
        h.engine.set_note_processing(settings(false, true));
        h.engine.set_listen_mode(true);
        h.engine.set_synth_mode(mode);
        h.engine.play();
        wait(|| h.hits.lock().unwrap().iter().filter(|(_, on)| *on).count() == 3);
        wait(|| h.engine.get_status() == PlaybackStatus::Stopped);
        assert!(h.output.lock().unwrap().is_empty(), "Listen Mode must never press chord keys");
        let mut hits = h.hits.lock().unwrap().clone();
        hits.sort_unstable();
        assert_eq!(hits, vec![(60, false), (60, true), (64, false), (64, true), (67, false), (67, true)],
            "Auto Chord must preserve every source note's existing listen/glow lifecycle");
    }
}

#[test]
fn a_chord_heard_in_listen_mode_does_not_freeze_later_game_notes_after_disabling_auto_chord() {
    let h = Harness::new(vec![
        event(0, 0, 60, true), event(60_000, 1, 64, true), event(120_000, 2, 67, true),
        event(180_000, 0, 60, false), event(180_000, 1, 64, false), event(180_000, 2, 67, false),
    ]);
    h.engine.set_note_processing(NoteProcessingSettings {
        chord_detection_milliseconds: 150, ..settings(false, true)
    });
    h.engine.set_listen_mode(true);
    h.engine.play();
    wait(|| h.engine.get_current_time_us() >= 30_000);
    assert!(h.output.lock().unwrap().is_empty());
    h.engine.set_note_processing(settings(false, false));
    h.engine.set_listen_mode(false);
    h.engine.flush().unwrap();
    wait(|| h.engine.get_status() == PlaybackStatus::Stopped);
    assert_eq!(h.count("q", false), 0);
    assert_eq!(h.count("d", false), 1, "an unheard pad cannot suppress future melody keys");
    assert_eq!(h.count("g", false), 1, "an unheard pad cannot suppress future melody keys");
}

#[test]
fn pad_cleanup_on_pause_stop_and_seek_does_not_restore_historical_taps() {
    for action in ["pause", "stop", "seek"] {
        let h = Harness::new(triad(67, 0, 1_000_000));
        h.engine.set_hold_delay_ms(500);
        h.engine.set_note_processing(settings(false, true));
        h.engine.play();
        wait(|| h.count("q", false) == 1);
        match action {
            "pause" => h.engine.pause(),
            "stop" => h.engine.stop(),
            _ => h.engine.seek(500_000, 1),
        }
        h.engine.flush().unwrap();
        wait(|| h.count("q", true) == 1);
        if action == "pause" { h.engine.play(); }
        // Let resumed/seek-restored held notes run. Their original pad onset is past.
        std::thread::sleep(Duration::from_millis(40));
        assert_eq!(h.count("q", false), 1, "{action}: restored a historical chord tap");
        h.engine.stop();
    }
}

#[test]
fn seeking_to_the_exact_chord_onset_still_dispatches_the_pad() {
    let h = Harness::new(triad(67, 100_000, 350_000));
    h.engine.set_note_processing(settings(false, true));
    h.engine.seek(100_000, 1);
    h.engine.flush().unwrap();
    assert!(h.output.lock().unwrap().is_empty(), "stopped seeks must remain silent");
    h.engine.play();
    wait(|| h.count("q", true) == 1);
    assert_eq!(h.count("q", false), 1);
}

#[test]
fn processing_setting_changes_leave_live_midi_notes_and_their_ownership_intact() {
    let h = Harness::new(vec![event(1_000_000, 0, 60, true), event(2_000_000, 0, 60, false)]);
    h.engine.midi_start(3);
    h.engine.midi_gate(true);
    h.engine.midi_message(3, MidiMessage::On { channel: 0, pitch: 60, velocity: 100 });
    wait(|| h.engine.midi_snapshot().pitches == [60]);
    for processing in [settings(true, false), settings(true, true), settings(false, false)] {
        h.engine.set_note_processing(processing);
        h.engine.flush().unwrap();
    }
    assert_eq!(h.engine.midi_snapshot().pitches, [60]);
    assert_eq!(h.count("a", false), 1);
    assert_eq!(h.count("a", true), 0, "song processing must not release live MIDI output");
    h.engine.midi_message(3, MidiMessage::Off { channel: 0, pitch: 60 });
    wait(|| h.engine.midi_snapshot().pitches.is_empty());
    assert_eq!(h.count("a", true), 1);
}

#[test]
fn auto_chord_does_not_replace_live_midi_input_with_song_chord_pads() {
    let h = Harness::new(vec![]);
    h.engine.set_note_processing(settings(true, true));
    h.engine.midi_start(4);
    h.engine.midi_gate(true);
    for pitch in [60, 64, 67] {
        h.engine.midi_message(4, MidiMessage::On { channel: 0, pitch, velocity: 100 });
    }
    wait(|| h.engine.midi_snapshot().pitches.len() == 3);
    assert_eq!(h.count("q", false), 0);
    for binding in ["a", "d", "g"] { assert_eq!(h.count(binding, false), 1); }
    h.engine.midi_stop();
    wait(|| !h.engine.midi_snapshot().active);
    for binding in ["a", "d", "g"] { assert_eq!(h.count(binding, true), 1); }
}
