use amp_core::{
    input::{chord_wheel::WheelButton, simulator::KeyboardOutput},
    parse_key_stroke,
    player::{engine::ArrangementState, NoteProcessingSettings},
    InstrumentConfigJson, KeyboardLayout, KeyboardLayoutJson, NoteKeyMapping, OutputKey,
    ParsedMidi, PlaybackEngine, PlaybackStatus, PlayerEvent, TempoMap, TimedNoteEvent,
};
use std::{
    sync::{
        atomic::{AtomicBool, Ordering},
        Arc, Mutex,
    },
    time::{Duration, Instant},
};

#[derive(Clone, Debug, PartialEq, Eq)]
enum Input {
    Key(OutputKey, bool),
    Move([u16; 2]),
    Button(WheelButton, bool),
}

type Inputs = Arc<Mutex<Vec<(Instant, Input)>>>;

struct Output {
    inputs: Inputs,
    focused: Arc<AtomicBool>,
    stall_next_selector: Arc<AtomicBool>,
}

impl Output {
    fn record(&self, input: Input) {
        self.inputs.lock().unwrap().push((Instant::now(), input));
    }
}

impl KeyboardOutput for Output {
    fn key(&mut self, key: OutputKey, up: bool) -> Result<(), String> {
        self.record(Input::Key(key, up));
        Ok(())
    }
    fn wheel_move(&mut self, processes: &[String], point: [u16; 2]) -> Result<(), String> {
        assert!(processes.contains(&"HTGame".to_string()));
        self.record(Input::Move(point));
        Ok(())
    }
    fn wheel_button(&mut self, button: WheelButton, up: bool) -> Result<(), String> {
        self.record(Input::Button(button, up));
        if button == WheelButton::Left && !up && self.stall_next_selector.swap(false, Ordering::AcqRel) {
            std::thread::sleep(Duration::from_millis(180));
        }
        Ok(())
    }
    fn wheel_focused(&self) -> bool {
        self.focused.load(Ordering::Acquire)
    }
}

fn notes(at: u64, pitches: &[u8]) -> Vec<TimedNoteEvent> {
    pitches
        .iter()
        .flat_map(|&note| {
            [
                TimedNoteEvent {
                    time_us: at,
                    note,
                    velocity: 90,
                    is_note_on: true,
                    channel: 0,
                    track_index: 0,
                },
                TimedNoteEvent {
                    time_us: at + 100_000,
                    note,
                    velocity: 0,
                    is_note_on: false,
                    channel: 0,
                    track_index: 0,
                },
            ]
        })
        .collect()
}

struct Harness {
    engine: PlaybackEngine,
    inputs: Inputs,
    focused: Arc<AtomicBool>,
    stall_next_selector: Arc<AtomicBool>,
    errors: Arc<Mutex<Vec<String>>>,
    arrangements: Arc<Mutex<Vec<ArrangementState>>>,
    layout: KeyboardLayoutJson,
}

impl Harness {
    fn wait(&self, description: &str, mut predicate: impl FnMut() -> bool) {
        let deadline = Instant::now() + Duration::from_secs(5);
        while !predicate() {
            assert!(
                Instant::now() < deadline,
                "timed out waiting for {description}; status: {:?}; errors: {:?}; inputs: {:?}",
                self.engine.get_status(),
                self.errors.lock().unwrap(),
                self.events()
            );
            std::thread::sleep(Duration::from_millis(2));
        }
    }

    fn new(events: Vec<TimedNoteEvent>) -> Self { Self::new_mode(events, false) }

    fn new_mode(events: Vec<TimedNoteEvent>, fret_mode: bool) -> Self {
        Self::with_mode(events, fret_mode, false, false)
    }

    fn with_mode(events: Vec<TimedNoteEvent>, fret_mode: bool, pluck: bool, mixed: bool) -> Self {
        Self::with_layouts(events, fret_mode, pluck, mixed, |_, _| {})
    }

    fn with_layouts(
        mut events: Vec<TimedNoteEvent>,
        fret_mode: bool,
        pluck: bool,
        mixed: bool,
        configure: impl FnOnce(&mut KeyboardLayoutJson, &mut KeyboardLayoutJson),
    ) -> Self {
        let instrument: InstrumentConfigJson = serde_json::from_str(include_str!(
            "../../../src-tauri/resources/defaults/Games/Neverness to Everness/Instruments/Strings/Guitar.json"
        )).unwrap();
        let mut layout: KeyboardLayoutJson = serde_json::from_str(include_str!(
            "../../../src-tauri/resources/defaults/KeyboardLayouts/NTE_Chord_Wheel.json"
        ))
        .unwrap();
        let base = NoteKeyMapping::from_pairs(KeyboardLayout::Chromatic36, &[]);
        let mut fret_layout = serde_json::from_str(include_str!(
            "../../../src-tauri/resources/defaults/KeyboardLayouts/NTE_Fret_Wheel.json"
        )).unwrap();
        configure(&mut layout, &mut fret_layout);
        let mapping = if fret_mode {
            base.with_frets(&instrument, &fret_layout, layout.chord_wheel.as_ref().unwrap())
        } else if pluck {
            base.with_guitar_chords(&instrument, &layout, &fret_layout, mixed)
        } else { base.with_chord_pads(&instrument, &layout) }.with_game_target(
                &serde_json::from_str(include_str!(
                    "../../../src-tauri/resources/defaults/Games/Neverness to Everness/game.json"
                ))
                .unwrap(),
            );
        if fret_mode { assert!(mapping.frets().is_some()); }
        else { assert_eq!(mapping.chord_pads().len(), 24); }
        events.sort();
        let song = ParsedMidi {
            title: "Wheel playback regression".into(),
            duration_us: events
                .last()
                .map_or(1_000_000, |event| event.time_us + 50_000),
            bpm: 120.0,
            ppqn: 480,
            tracks: vec![],
            events,
            control_events: vec![],
            tempo_map: TempoMap::new(480, &[]),
            key_signatures: vec![],
            guitar_pro: None,
        };
        let inputs = Arc::new(Mutex::new(Vec::new()));
        let focused = Arc::new(AtomicBool::new(true));
        let stall_next_selector = Arc::new(AtomicBool::new(false));
        let errors = Arc::new(Mutex::new(Vec::new()));
        let captured_errors = errors.clone();
        let arrangements = Arc::new(Mutex::new(Vec::new()));
        let captured_arrangements = arrangements.clone();
        let engine = PlaybackEngine::new(Some(song), mapping, true, None, move |event| {
            match event {
                PlayerEvent::ChordWheelChanged(state) => {
                    if let Some(error) = state.error {
                        captured_errors.lock().unwrap().push(error);
                    }
                }
                PlayerEvent::Arrangement(state) => captured_arrangements.lock().unwrap().push(state),
                _ => {}
            }
        });
        engine.set_keyboard_output(Some(Box::new(Output {
            inputs: inputs.clone(),
            focused: focused.clone(),
            stall_next_selector: stall_next_selector.clone(),
        })));
        engine.set_listen_mode(false);
        engine.set_note_timing(true, 0);
        engine.set_hold_delay_ms(20);
        engine.set_note_processing(NoteProcessingSettings {
            merge_notes: false,
            merge_tolerance_ms: 25,
            chord_detection_milliseconds: 30,
        });
        engine.flush().unwrap();
        Self {
            engine,
            inputs,
            focused,
            stall_next_selector,
            errors,
            arrangements,
            layout,
        }
    }

    fn events(&self) -> Vec<Input> {
        self.inputs
            .lock()
            .unwrap()
            .iter()
            .map(|(_, input)| input.clone())
            .collect()
    }
    fn play_to_end(&self) {
        self.engine.request_play().recv_timeout(Duration::from_secs(5)).unwrap().unwrap();
        self.wait("playback completion", || self.engine.get_status() == PlaybackStatus::Stopped);
        assert!(self.errors.lock().unwrap().is_empty());
    }
    fn attacks(&self) -> Vec<ArrangementState> {
        self.arrangements.lock().unwrap().iter().filter(|s| s.slot.is_some()).cloned().collect()
    }
    fn count(&self, expected: Input) -> usize {
        self.events()
            .iter()
            .filter(|input| **input == expected)
            .count()
    }
    fn strums(&self) -> Vec<Instant> {
        self.inputs
            .lock()
            .unwrap()
            .iter()
            .filter_map(|(at, input)| {
                (*input == Input::Button(WheelButton::Right, false)).then_some(*at)
            })
            .collect()
    }
    fn finish(&self, expected_strums: usize) {
        self.engine.play();
        self.wait("strums", || {
            self.strums().len() == expected_strums
                || self.engine.get_status() == PlaybackStatus::Paused
        });
        assert_ne!(
            self.engine.get_status(),
            PlaybackStatus::Paused,
            "wheel playback paused: {:?}; inputs: {:?}",
            self.errors.lock().unwrap(),
            self.events()
        );
        self.wait("playback completion", || {
            self.engine.get_status() == PlaybackStatus::Stopped
        });
        let errors = self.errors.lock().unwrap().clone();
        assert!(errors.is_empty(), "{errors:?}");
    }
}

#[test]
fn first_chord_selects_and_releases_before_strumming_without_piano_keys() {
    let h = Harness::new(notes(0, &[55, 59, 62])); // G, root-position bass.
    h.finish(1);
    let events = h.events();
    let wheel = h.layout.chord_wheel.as_ref().unwrap();
    let selector = events
        .iter()
        .position(|event| *event == Input::Move(wheel.positions[0]))
        .unwrap();
    let select_down = events
        .iter()
        .position(|event| *event == Input::Button(WheelButton::Left, false))
        .unwrap();
    let select_up = events
        .iter()
        .position(|event| *event == Input::Button(WheelButton::Left, true))
        .unwrap();
    let strum_down = events
        .iter()
        .position(|event| *event == Input::Button(WheelButton::Right, false))
        .unwrap();
    let strum_up = events
        .iter()
        .position(|event| *event == Input::Button(WheelButton::Right, true))
        .unwrap();
    assert!(
        selector < select_down
            && select_down < select_up
            && select_up < strum_down
            && strum_down < strum_up
    );
    assert_eq!(events[strum_down - 1], Input::Move(wheel.strum_position));
    assert!(!events.iter().any(|event| matches!(event, Input::Key(_, _))));
}

#[test]
fn fret_mode_plucks_exact_strings_at_open_twelfth_and_twenty_fourth_frets() {
    for (pitches, bindings, page) in [(&[40, 45][..], &["1", "2"][..], 4),
        (&[71, 76][..], &["5", "6"][..], 1), (&[88][..], &["6"][..], 3)] {
        let h = Harness::new_mode(notes(0, pitches), true);
        h.engine.play();
        h.wait("fret plucks", || bindings.iter().all(|binding| h.count(Input::Key(parse_key_stroke(binding).unwrap().key, false)) == 1));
        h.wait("fret completion", || h.engine.get_status() == PlaybackStatus::Stopped);
        assert!(h.errors.lock().unwrap().is_empty());
        assert_eq!(h.engine.chord_wheel_state().unwrap().page, Some(page));
        for binding in bindings { assert_eq!(h.count(Input::Key(parse_key_stroke(binding).unwrap().key, true)), 1); }
        assert!(h.strums().is_empty(), "fret playback must use string keys, not mouse strums");
    }
}

#[test]
fn pausing_fret_playback_releases_all_plucked_strings() {
    let h = Harness::new_mode(notes(0, &[40, 45]), true);
    h.engine.set_hold_delay_ms(200);
    h.engine.play();
    let key = parse_key_stroke("1").unwrap().key;
    h.wait("string key down", || h.count(Input::Key(key, false)) == 1);
    h.engine.pause(); h.engine.flush().unwrap();
    assert_eq!(h.count(Input::Key(key, true)), 1);
    assert_eq!(h.count(Input::Key(parse_key_stroke("2").unwrap().key, true)), 1);
    assert!(!h.engine.chord_wheel_state().unwrap().busy);
}

#[test]
fn fret_groups_requiring_different_selectors_keep_the_melody_and_report_the_omission() {
    let h = Harness::new_mode(notes(0, &[60, 61]), true);
    h.play_to_end();
    let attacks = h.attacks();
    assert_eq!(attacks.len(), 1);
    let attack = &attacks[0];
    assert_eq!((attack.omitted_melody, attack.omitted_accompaniment), (0, 1));
    assert_eq!(attack.strings.count_ones(), 1);
    let string = attack.strings.trailing_zeros() as usize;
    let fret = (attack.slot.unwrap() + 1) % 25;
    assert_eq!([64, 59, 55, 50, 45, 40][string] + fret, 61);
    let key = parse_key_stroke(&(6 - string).to_string()).unwrap().key;
    assert_eq!(h.count(Input::Key(key, false)), 1);
    assert_eq!(h.count(Input::Key(key, true)), 1);
}

#[test]
fn chord_shape_plucks_follow_the_midi_attacks_on_the_chord_wheel() {
    let h = Harness::with_mode([notes(0, &[59]), notes(60_000, &[40])].concat(), false, true, false);
    h.play_to_end();
    let attacks = h.attacks();
    assert_eq!(attacks.iter().map(|s| (s.mode, s.slot, s.strings)).collect::<Vec<_>>(),
        [(Some("chords"), Some(5), 2), (Some("chords"), Some(5), 32)]);
    assert!(h.strums().is_empty());
    for binding in ["5", "1"] {
        let key = parse_key_stroke(binding).unwrap().key;
        assert_eq!(h.count(Input::Key(key, false)), 1);
        assert_eq!(h.count(Input::Key(key, true)), 1);
    }
    assert_eq!(h.count(Input::Button(WheelButton::Left, false)), 1);
}

#[test]
fn mixed_switches_wheels_and_remembers_the_first_chord_page() {
    let chord = &[60, 64, 67, 71]; // Cmaj7, chord page two.
    let h = Harness::with_mode([notes(0, chord), notes(1_000_000, &[88]), notes(2_000_000, chord)].concat(), false, true, true);
    assert!(h.engine.request_play().recv().unwrap().unwrap_err().contains("Synchronize"));
    h.engine.sync_guitar_mode(false).unwrap();
    h.play_to_end();
    let attacks = h.attacks();
    assert_eq!(attacks.iter().map(|s| (s.mode, s.page, s.strings)).collect::<Vec<_>>(),
        [(Some("chords"), Some(1), 0), (Some("frets"), Some(3), 1), (Some("chords"), Some(1), 0)]);
    assert_eq!(h.strums().len(), 2);
    let mode_key = parse_key_stroke("z").unwrap().key;
    assert_eq!(h.count(Input::Key(mode_key, false)), 2);
    assert_eq!(h.count(Input::Key(mode_key, true)), 2);
    assert_eq!(h.count(Input::Key(parse_key_stroke("t").unwrap().key, false)), 1);
    assert_eq!(h.engine.guitar_mode().unwrap(), Some(false));
}

#[test]
fn manual_pagination_sends_each_wheels_configured_previous_and_next_keys() {
    let h = Harness::with_layouts(vec![], false, true, true, |chord_layout, fret_layout| {
        chord_layout.chord_wheel.as_mut().unwrap().page_keys = ["b".into(), "n".into()];
        fret_layout.fretboard.as_mut().unwrap().page_keys = Some(["PageUp".into(), "PageDown".into()]);
    });
    for fret in [false, true] {
        h.engine.sync_guitar_mode(fret).unwrap();
        h.engine.set_chord_wheel_page(0, false).unwrap();
        for page in [1, 0] {
            h.engine.set_chord_wheel_page(page, true).unwrap();
            h.wait("manual page", || {
                let wheel = h.engine.chord_wheel_state().unwrap();
                !wheel.busy && wheel.page == Some(page)
            });
        }
    }
    let expected = ["n", "b", "PageDown", "PageUp"].into_iter().flat_map(|binding| {
        let key = parse_key_stroke(binding).unwrap().key;
        [Input::Key(key, false), Input::Key(key, true)]
    }).collect::<Vec<_>>();
    let keys = h.events().into_iter().filter(|input| matches!(input, Input::Key(..))).collect::<Vec<_>>();
    assert_eq!(keys, expected);
}

#[test]
fn interrupted_mode_press_is_released_and_requires_resynchronization() {
    let h = Harness::with_mode(notes(0, &[88]), false, true, true);
    h.engine.sync_guitar_mode(false).unwrap();
    h.engine.play();
    let mode_key = parse_key_stroke("z").unwrap().key;
    h.wait("mode key down", || h.count(Input::Key(mode_key, false)) == 1);
    h.focused.store(false, Ordering::Release);
    h.wait("focus-loss pause", || h.engine.get_status() == PlaybackStatus::Paused);
    assert_eq!(h.count(Input::Key(mode_key, true)), 1);
    assert_eq!(h.engine.guitar_mode().unwrap(), None);
    assert!(h.attacks().is_empty());
    assert!(h.engine.request_play().recv().unwrap().unwrap_err().contains("Synchronize"));
}

#[test]
fn manual_mode_switch_blocks_play_until_settled_and_reselecting_the_same_mode_sends_no_key() {
    let h = Harness::with_mode(notes(0, &[88]), false, true, true);
    h.engine.sync_guitar_mode(false).unwrap();
    let mode_key = parse_key_stroke("z").unwrap();
    std::thread::scope(|scope| {
        let switch = scope.spawn(|| h.engine.switch_guitar_mode(true, mode_key));
        h.wait("manual mode key down", || h.count(Input::Key(mode_key.key, false)) == 1);
        assert!(h.engine.request_play().recv_timeout(Duration::from_secs(2)).unwrap().is_err());
        switch.join().unwrap().unwrap();
    });
    h.engine.switch_guitar_mode(true, mode_key).unwrap();
    assert_eq!(h.count(Input::Key(mode_key.key, false)), 1);
    assert_eq!(h.count(Input::Key(mode_key.key, true)), 1);
    assert_eq!(h.engine.guitar_mode().unwrap(), Some(true));
    h.play_to_end();
    assert_eq!(h.attacks().len(), 1);
    assert_eq!(h.count(Input::Key(mode_key.key, false)), 1);
}

#[test]
fn interrupted_page_press_releases_key_and_invalidates_only_that_wheel() {
    let h = Harness::new_mode(notes(0, &[40]), true);
    h.engine.play();
    let page_key = parse_key_stroke("r").unwrap().key;
    h.wait("page key down", || h.count(Input::Key(page_key, false)) == 1);
    h.focused.store(false, Ordering::Release);
    h.wait("focus-loss pause", || h.engine.get_status() == PlaybackStatus::Paused);
    assert_eq!(h.count(Input::Key(page_key, true)), 1);
    assert_eq!(h.engine.chord_wheel_state().unwrap().page, None);
    assert_eq!(h.engine.guitar_mode().unwrap(), Some(true));
    assert!(h.attacks().is_empty());
}

#[test]
fn a_missed_selection_is_counted_once_and_playback_continues_at_the_next_attack() {
    let g = &[55, 59, 62];
    let events = [notes(0, g), notes(600_000, &[48, 52, 55]), notes(1_350_000, g)].concat();
    let h = Harness::with_mode(events, false, true, false);
    h.engine.play();
    h.wait("first strum", || h.strums().len() == 1);
    h.stall_next_selector.store(true, Ordering::Release);
    h.wait("final strum", || h.strums().len() == 2);
    h.wait("completion after skipped attack", || h.engine.get_status() == PlaybackStatus::Stopped);
    assert!(h.errors.lock().unwrap().is_empty());
    let attacks = h.attacks();
    assert_eq!(attacks.iter().map(|s| s.slot).collect::<Vec<_>>(), [Some(0), Some(0)]);
    assert_eq!((attacks[1].omitted_melody, attacks[1].omitted_accompaniment), (1, 2));
    assert!(h.strums()[1].duration_since(h.strums()[0]) >= Duration::from_millis(1_250));
}

#[test]
fn seeking_inside_a_strummed_chord_does_not_replay_its_remaining_midi_members() {
    let events = [notes(0, &[48]), notes(60_000, &[52]), notes(120_000, &[55]), notes(650_000, &[40])].concat();
    let h = Harness::with_mode(events, false, true, false);
    h.engine.set_note_processing(NoteProcessingSettings {
        merge_notes: false,
        merge_tolerance_ms: 25,
        chord_detection_milliseconds: 150,
    });
    h.engine.play();
    h.wait("first strum", || h.strums().len() == 1);
    h.engine.pause();
    h.engine.seek(10_000, 1);
    h.engine.flush().unwrap();
    h.play_to_end();
    let attacks = h.attacks();
    assert_eq!(attacks.len(), 2, "{attacks:?}");
    assert_eq!(attacks[0].strings, 0);
    assert_eq!(attacks[1].strings, 32);
    assert_eq!(h.strums().len(), 1);
}

#[test]
fn repeated_chords_reuse_the_selection_and_keep_each_strum_attack() {
    let h = Harness::new([notes(0, &[55, 59, 62]), notes(100_000, &[55, 59, 62])].concat());
    h.finish(2);
    assert_eq!(h.count(Input::Button(WheelButton::Left, false)), 1);
    assert_eq!(h.count(Input::Button(WheelButton::Right, false)), 2);
}

#[test]
fn automatic_pages_prepare_early_and_preserve_source_attack_intervals() {
    let events = [
        notes(0, &[55, 59, 62]),
        notes(800_000, &[60, 64, 67, 71]),
        notes(1_600_000, &[55, 59, 62]),
    ]
    .concat();
    let h = Harness::new(events);
    h.finish(3);
    let next = parse_key_stroke("t").unwrap().key;
    let previous = parse_key_stroke("r").unwrap().key;
    assert_eq!(h.count(Input::Key(next, false)), 1);
    assert_eq!(h.count(Input::Key(next, true)), 1);
    assert_eq!(h.count(Input::Key(previous, false)), 1);
    assert_eq!(h.count(Input::Key(previous, true)), 1);
    assert_eq!(h.engine.chord_wheel_state().unwrap().page, Some(0));
    for pair in h.strums().windows(2) {
        let interval = pair[1].duration_since(pair[0]);
        assert!(
            (Duration::from_millis(700)..=Duration::from_millis(900)).contains(&interval),
            "page preparation changed the 800ms musical interval: {interval:?}"
        );
    }
}

#[test]
fn power_dyads_and_slash_chords_select_their_later_pages() {
    for (pitches, expected_page) in [
        (&[60, 67][..], 2),
        (&[59, 62, 67][..], 3),
        (&[47, 50, 55][..], 3),
    ] {
        let h = Harness::new(notes(0, pitches));
        h.finish(1);
        assert_eq!(
            h.engine.chord_wheel_state().unwrap().page,
            Some(expected_page)
        );
        assert_eq!(
            h.count(Input::Key(
                parse_key_stroke(if expected_page == 3 { "r" } else { "t" })
                    .unwrap()
                    .key,
                false
            )),
            if expected_page == 3 { 1 } else { expected_page }
        );
        let point = h.layout.chord_wheel.as_ref().unwrap().positions[0];
        assert_eq!(
            h.count(Input::Move(point)),
            1,
            "the selected chord is the first slot of its page"
        );
    }
}

#[test]
fn pause_and_stop_cancel_prepared_attacks_and_release_selector_buttons() {
    for pause in [false, true] {
        let h = Harness::new(notes(0, &[55, 59, 62]));
        h.engine.play();
        h.wait("selector press", || {
            h.count(Input::Button(WheelButton::Left, false)) == 1
        });
        if pause {
            h.engine.pause();
        } else {
            h.engine.stop();
        }
        h.engine.flush().unwrap();
        assert_eq!(
            h.engine.get_status(),
            if pause {
                PlaybackStatus::Paused
            } else {
                PlaybackStatus::Stopped
            }
        );
        assert_eq!(h.count(Input::Button(WheelButton::Left, true)), 1);
        std::thread::sleep(Duration::from_millis(100));
        assert!(
            h.strums().is_empty(),
            "a cancelled selector must never produce a delayed strum"
        );
        assert!(!h.engine.chord_wheel_state().unwrap().busy);
    }
}

#[test]
fn focus_loss_during_selection_pauses_and_releases_mouse_ownership() {
    let h = Harness::new(notes(0, &[55, 59, 62]));
    h.engine.play();
    h.wait("selector press", || {
        h.count(Input::Button(WheelButton::Left, false)) == 1
    });
    h.focused.store(false, Ordering::Release);
    h.wait("focus-loss pause", || {
        h.engine.get_status() == PlaybackStatus::Paused
    });
    assert_eq!(h.count(Input::Button(WheelButton::Left, true)), 1);
    assert!(h.strums().is_empty());
    assert!(h
        .errors
        .lock()
        .unwrap()
        .iter()
        .any(|error| error.contains("focus")));
}

#[test]
fn pausing_and_resuming_backtracks_to_reselect_active_ring_slot() {
    let h = Harness::with_mode(
        [notes(0, &[59]), notes(400_000, &[40])].concat(),
        false,
        true,
        false,
    );
    let first_string = parse_key_stroke("5").unwrap().key;
    h.engine.play();
    h.wait("first pluck", || h.count(Input::Key(first_string, false)) == 1);
    h.engine.pause();
    h.engine.flush().unwrap();
    assert_eq!(h.engine.chord_wheel_state().unwrap().selected, None);

    // Resuming after pause must backtrack and re-select slot 5 before the next pluck on slot 5.
    let second_string = parse_key_stroke("1").unwrap().key;
    h.engine.request_play().recv_timeout(Duration::from_secs(5)).unwrap().unwrap();
    h.wait("backtracked ring selection", || {
        h.engine.chord_wheel_state().unwrap().selected == Some(5)
    });
    h.wait("playback completion", || h.engine.get_status() == PlaybackStatus::Stopped);
    assert!(h.errors.lock().unwrap().is_empty());
    assert_eq!(h.count(Input::Button(WheelButton::Left, false)), 2);
    assert_eq!(h.count(Input::Key(second_string, false)), 1);
}
