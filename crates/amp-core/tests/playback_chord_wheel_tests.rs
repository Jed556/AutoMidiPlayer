use amp_core::{
    input::{chord_wheel::WheelButton, simulator::KeyboardOutput},
    parse_key_stroke,
    player::NoteProcessingSettings,
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
    errors: Arc<Mutex<Vec<String>>>,
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

    fn new_mode(mut events: Vec<TimedNoteEvent>, fret_mode: bool) -> Self {
        let instrument: InstrumentConfigJson = serde_json::from_str(include_str!(
            "../../../src-tauri/resources/defaults/Games/Neverness to Everness/Instruments/Strings/Guitar.json"
        )).unwrap();
        let layout: KeyboardLayoutJson = serde_json::from_str(include_str!(
            "../../../src-tauri/resources/defaults/KeyboardLayouts/NTE_Chord_Wheel.json"
        ))
        .unwrap();
        let base = NoteKeyMapping::from_pairs(KeyboardLayout::Chromatic36, &[]);
        let mapping = if fret_mode {
            let fret_layout = serde_json::from_str(include_str!(
                "../../../src-tauri/resources/defaults/KeyboardLayouts/NTE_Fret_Wheel.json"
            )).unwrap();
            base.with_frets(&instrument, &fret_layout, layout.chord_wheel.as_ref().unwrap())
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
        };
        let inputs = Arc::new(Mutex::new(Vec::new()));
        let focused = Arc::new(AtomicBool::new(true));
        let errors = Arc::new(Mutex::new(Vec::new()));
        let captured_errors = errors.clone();
        let engine = PlaybackEngine::new(Some(song), mapping, true, None, move |event| {
            if let PlayerEvent::ChordWheelChanged(state) = event {
                if let Some(error) = state.error {
                    captured_errors.lock().unwrap().push(error);
                }
            }
        });
        engine.set_keyboard_output(Some(Box::new(Output {
            inputs: inputs.clone(),
            focused: focused.clone(),
        })));
        engine.set_listen_mode(false);
        engine.set_note_timing(true, 0);
        engine.set_hold_delay_ms(20);
        engine.set_note_processing(NoteProcessingSettings {
            merge_notes: false,
            merge_tolerance_ms: 25,
            auto_chord: true,
            chord_detection_milliseconds: 30,
        });
        engine.flush().unwrap();
        Self {
            engine,
            inputs,
            focused,
            errors,
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
        h.engine.set_note_processing(NoteProcessingSettings { auto_chord: false, ..NoteProcessingSettings::default() });
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
fn fret_groups_requiring_different_selectors_do_not_send_approximate_notes() {
    let h = Harness::new_mode(notes(0, &[60, 61]), true);
    h.engine.play();
    h.wait("unsupported group completion", || h.engine.get_status() == PlaybackStatus::Stopped);
    assert!(h.events().is_empty());
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
