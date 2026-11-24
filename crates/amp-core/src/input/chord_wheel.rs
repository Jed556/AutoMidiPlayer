//! Prepare selector/page changes ahead of the musical onset, without sleeping.
use super::{
    config::ChordWheelConfigJson,
    mapping::{parse_key_stroke, KeyStroke},
    simulator::KeyboardOutput,
};
use std::time::{Duration, Instant};

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum WheelButton {
    Left,
    Right,
}

#[derive(
    Debug,
    Default,
    Clone,
    Copy,
    PartialEq,
    Eq,
    Hash,
    PartialOrd,
    Ord,
    serde::Serialize,
    serde::Deserialize,
)]
#[serde(rename_all = "lowercase")]
pub enum StrumDirection {
    #[default]
    Down,
    Up,
}

impl StrumDirection {
    pub fn button(self) -> WheelButton {
        match self {
            Self::Down => WheelButton::Right,
            Self::Up => WheelButton::Left,
        }
    }

    pub fn as_str(self) -> &'static str {
        match self {
            Self::Down => "down",
            Self::Up => "up",
        }
    }
}

#[derive(Debug, Clone, PartialEq, Eq, serde::Serialize)]
#[serde(rename_all = "camelCase")]
pub struct ChordWheelState {
    pub page: Option<usize>,
    pub selected: Option<usize>,
    pub busy: bool,
    pub error: Option<String>,
}

enum Stage {
    PageDown,
    PageUp,
    PageWait,
    Move,
    SelectDown,
    SelectUp,
    Ready,
    StrumUp,
}
struct Attack {
    owner: Option<usize>,
    config: ChordWheelConfigJson,
    processes: Vec<String>,
    target_page: usize,
    slot: Option<usize>,
    stage: Stage,
    at: Instant,
    page_delta: isize,
    expired: bool,
}

pub(super) struct ChordWheelOutput {
    attack: Option<Attack>,
    held: Option<WheelButton>,
    page_key: Option<KeyStroke>,
    page: Option<usize>,
    selected: Option<usize>,
    held_strings: Vec<KeyStroke>,
}

impl Default for ChordWheelOutput {
    fn default() -> Self {
        Self {
            attack: None,
            held: None,
            page_key: None,
            page: Some(0),
            selected: None,
            held_strings: Vec::new(),
        }
    }
}

/// Real-time input budget shared by the arrangement search and the transport.
pub(crate) fn wheel_transition_ms(
    config: &ChordWheelConfigJson,
    index: usize,
    current_page: Option<usize>,
    same_selector: bool,
    switch: bool,
) -> u64 {
    if same_selector && !switch {
        // Prepare the new owner before entering the transport's final sub-ms wait.
        return 2;
    }
    let target = index / config.positions.len();
    let pages = current_page.filter(|&page| page < config.page_count && target < config.page_count)
        .map_or(config.page_count.saturating_sub(1), |page| {
            let distance = page.abs_diff(target);
            distance.min(config.page_count - distance)
        });
    let switch_ms = if switch { super::simulator::MODE_HOLD_MS + super::simulator::MODE_SETTLE_MS } else { 0 };
    let slack_ms = 45 + 20 * pages as u64 + 25 * u64::from(switch);
    switch_ms + pages as u64 * u64::from(config.page_hold_ms + config.page_wait_ms)
        + u64::from(config.move_delay_ms + config.select_hold_ms + config.after_select_ms) + slack_ms
}

impl ChordWheelOutput {
    pub fn state(&self) -> ChordWheelState {
        ChordWheelState {
            page: self.page,
            selected: self.selected,
            busy: self.is_active(),
            error: None,
        }
    }
    pub fn selected(&self) -> Option<usize> {
        self.selected
    }
    pub fn reconcile_in_flight(
        &mut self,
        target_page: usize,
        index: usize,
        owner: Option<usize>,
    ) -> bool {
        let Some(attack) = &mut self.attack else {
            return false;
        };
        let slot = index % attack.config.positions.len();
        if (attack.expired || attack.owner.is_none()) && !matches!(attack.stage, Stage::StrumUp) {
            if attack.target_page == target_page && attack.slot == Some(slot) {
                attack.owner = owner;
                attack.expired = false;
                return true;
            }
            if self.held.is_none() && self.page_key.is_none() && self.held_strings.is_empty() {
                self.attack = None;
            }
        }
        false
    }
    pub fn assume_page(&mut self, page: usize) {
        self.page = Some(page);
        self.selected = None;
    }
    pub fn forget_page(&mut self) {
        self.page = None;
        self.selected = None;
    }
    pub fn retire(&mut self) {
        self.attack = None;
        self.held = None;
        self.page_key = None;
        self.selected = None;
        self.held_strings.clear();
    }
    pub fn preparation_ms(&self, config: &ChordWheelConfigJson, index: usize) -> u64 {
        wheel_transition_ms(config, index, self.page, self.selected == Some(index), false)
    }
    pub fn prepare(
        &mut self,
        output: &mut dyn KeyboardOutput,
        config: &ChordWheelConfigJson,
        processes: &[String],
        index: Option<usize>,
        target_page: usize,
        owner: Option<usize>,
        now: Instant,
    ) -> Result<(), String> {
        if self.is_active() {
            return Err("The chord wheel is busy.".into());
        }
        if config.page_count == 1 && self.page != Some(0) {
            self.assume_page(0);
        }
        if self.page.is_none_or(|page| page >= config.page_count) {
            return Err("Set the current game page before resuming chord playback.".into());
        }
        if target_page >= config.page_count {
            return Err("Chord wheel page is unavailable.".into());
        }
        if processes.is_empty() {
            return Err("The selected game has no process target for chord playback.".into());
        }
        output.wheel_move(processes, config.strum_position)?;
        let already_selected =
            index.is_some() && self.selected == index && self.page == Some(target_page);
        if !already_selected {
            self.selected = None;
        }
        self.attack = Some(Attack {
            owner,
            config: config.clone(),
            processes: processes.to_vec(),
            target_page,
            slot: index.map(|index| index % config.positions.len()),
            stage: if already_selected {
                Stage::Ready
            } else {
                Stage::PageDown
            },
            at: now,
            page_delta: 0,
            expired: false,
        });
        self.tick(output, now)
    }
    pub fn tick(&mut self, output: &mut dyn KeyboardOutput, now: Instant) -> Result<(), String> {
        let Some(attack) = &mut self.attack else {
            return Ok(());
        };
        if !output.wheel_focused() {
            return Err("The game lost focus; chord playback was paused.".into());
        }
        if now < attack.at {
            return Ok(());
        }
        // Holds and settle intervals are measured from actual dispatch.
        match attack.stage {
            Stage::PageDown | Stage::PageWait => {
                let page = self.page.ok_or("The current game page is unknown")?;
                if page == attack.target_page {
                    if attack.slot.is_none() {
                        self.attack = None;
                        return Ok(());
                    }
                    attack.stage = Stage::Move;
                } else {
                    let count = attack.config.page_count;
                    let forward = (attack.target_page + count - page) % count;
                    let backward = (page + count - attack.target_page) % count;
                    attack.page_delta = if forward <= backward { 1 } else { -1 };
                    let stroke = parse_key_stroke(
                        &attack.config.page_keys[usize::from(attack.page_delta > 0)],
                    )
                    .ok_or("Invalid chord page key")?;
                    self.page_key = Some(stroke);
                    for (key, up) in stroke.press_events() {
                        output.key(key, up)?;
                    }
                    attack.stage = Stage::PageUp;
                    attack.at = now + Duration::from_millis(attack.config.page_hold_ms.into());
                }
            }
            Stage::PageUp => {
                if let Some(stroke) = self.page_key {
                    output.key(stroke.key, true)?;
                }
                self.page_key = None;
                self.page = self.page.map(|page| {
                    (page as isize + attack.page_delta)
                        .rem_euclid(attack.config.page_count as isize) as usize
                });
                attack.stage = Stage::PageWait;
                attack.at = now + Duration::from_millis(attack.config.page_wait_ms.into());
            }
            Stage::Move => {
                output.wheel_move(
                    &attack.processes,
                    attack.config.positions[attack.slot.unwrap()],
                )?;
                attack.stage = Stage::SelectDown;
                attack.at = now + Duration::from_millis(attack.config.move_delay_ms.into());
            }
            Stage::SelectDown => {
                self.held = Some(WheelButton::Left);
                output.wheel_button(WheelButton::Left, false)?;
                attack.stage = Stage::SelectUp;
                attack.at = now + Duration::from_millis(attack.config.select_hold_ms.into());
            }
            Stage::SelectUp => {
                output.wheel_button(WheelButton::Left, true)?;
                self.held = None;
                self.selected = attack
                    .slot
                    .map(|slot| attack.target_page * attack.config.positions.len() + slot);
                attack.stage = Stage::Ready;
                attack.at = now + Duration::from_millis(attack.config.after_select_ms.into());
            }
            Stage::Ready if attack.expired || attack.owner.is_none() => self.attack = None,
            Stage::Ready => {}
            Stage::StrumUp => {
                if let Some(button) = self.held {
                    output.wheel_button(button, true)?;
                    self.held = None;
                }
                for stroke in &self.held_strings { output.key(stroke.key, true)?; }
                self.held_strings.clear();
                self.attack = None;
            }
        }
        Ok(())
    }
    pub fn ready(&self, owner: usize, now: Instant) -> bool {
        self.attack.as_ref().is_some_and(|attack| {
            !attack.expired && attack.owner == Some(owner) && matches!(attack.stage, Stage::Ready) && now >= attack.at
        })
    }
    /// Finish any in-flight physical key release but never play a missed attack late.
    pub fn expire(&mut self, owner: usize) {
        if let Some(attack) = self.attack.as_mut().filter(|a| a.owner == Some(owner)) {
            attack.expired = true;
        }
    }
    #[cfg(test)]
    pub fn strum(
        &mut self,
        output: &mut dyn KeyboardOutput,
        owner: usize,
        hold_ms: u64,
        now: Instant,
    ) -> Result<(), String> {
        self.strum_direction(output, owner, StrumDirection::Down, hold_ms, now)
    }

    pub fn strum_direction(
        &mut self,
        output: &mut dyn KeyboardOutput,
        owner: usize,
        direction: StrumDirection,
        hold_ms: u64,
        now: Instant,
    ) -> Result<(), String> {
        if !self.ready(owner, now) {
            return Err("Chord selection could not finish before the next note. Reduce playback speed or mouse timing.".into());
        }
        let attack = self.attack.as_mut().unwrap();
        output.wheel_move(&attack.processes, attack.config.strum_position)?;
        let button = direction.button();
        self.held = Some(button);
        output.wheel_button(button, false)?;
        attack.stage = Stage::StrumUp;
        attack.at = now + Duration::from_millis(hold_ms.max(1));
        Ok(())
    }
    pub fn cancel(&mut self, output: &mut dyn KeyboardOutput) -> Result<(), String> {
        self.attack = None;
        self.selected = None;
        let mut error = None;
        if let Some(stroke) = self.page_key {
            self.page = None;
            for key in [
                Some(stroke.key),
                stroke.shift.then_some(enigo::Key::Shift),
                stroke.control.then_some(enigo::Key::Control),
            ]
            .into_iter()
            .flatten()
            {
                if let Err(failure) = output.key(key, true) {
                    error = Some(failure);
                }
            }
            if error.is_none() {
                self.page_key = None;
            }
        }
        if let Some(button) = self.held {
            match output.wheel_button(button, true) {
                Ok(()) => self.held = None,
                Err(failure) => error = Some(failure),
            }
        }
        self.held_strings.retain(|stroke| {
            let mut failed = false;
            for key in [
                Some(stroke.key),
                stroke.shift.then_some(enigo::Key::Shift),
                stroke.control.then_some(enigo::Key::Control),
            ].into_iter().flatten() {
                if let Err(failure) = output.key(key, true) {
                    error = Some(failure);
                    failed = true;
                }
            }
            failed
        });
        error.map_or(Ok(()), Err)
    }
    pub fn pluck(&mut self, output: &mut dyn KeyboardOutput, owner: usize,
        strokes: &[KeyStroke], hold_ms: u64, now: Instant) -> Result<(), String> {
        if !self.ready(owner, now) { return Err("Fret selection could not finish before the next note. Reduce playback speed or input timing.".into()); }
        if !output.wheel_focused() { return Err("The game lost focus; fret playback was paused.".into()); }
        for &stroke in strokes {
            self.held_strings.push(stroke);
            for (key, up) in stroke.press_events() { output.key(key, up)?; }
        }
        let attack = self.attack.as_mut().unwrap();
        attack.stage = Stage::StrumUp;
        attack.at = now + Duration::from_millis(hold_ms.max(1));
        Ok(())
    }
    pub fn is_active(&self) -> bool {
        self.attack.is_some() || self.held.is_some() || self.page_key.is_some() || !self.held_strings.is_empty()
    }
    pub fn next_deadline(&self, now: Instant) -> Option<Instant> {
        let attack = self.attack.as_ref()?;
        match attack.stage {
            Stage::Ready if !attack.expired && attack.owner.is_some() && now >= attack.at => None,
            _ => Some(attack.at),
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use enigo::Key;

    #[derive(Debug, PartialEq, Eq)]
    enum Event {
        Move([u16; 2]),
        Key(Key, bool),
        Button(WheelButton, bool),
    }

    struct Output {
        events: Vec<Event>,
        focused: bool,
        fail_release: bool,
    }

    impl Default for Output {
        fn default() -> Self {
            Self {
                events: Vec::new(),
                focused: true,
                fail_release: false,
            }
        }
    }

    impl KeyboardOutput for Output {
        fn key(&mut self, key: Key, up: bool) -> Result<(), String> {
            self.events.push(Event::Key(key, up));
            Ok(())
        }

        fn wheel_move(&mut self, processes: &[String], point: [u16; 2]) -> Result<(), String> {
            assert!(processes.contains(&"HTGame".to_string()));
            self.events.push(Event::Move(point));
            Ok(())
        }

        fn wheel_button(&mut self, button: WheelButton, up: bool) -> Result<(), String> {
            self.events.push(Event::Button(button, up));
            if up && std::mem::take(&mut self.fail_release) {
                Err("release failed".into())
            } else {
                Ok(())
            }
        }

        fn wheel_focused(&self) -> bool {
            self.focused
        }
    }

    fn config() -> ChordWheelConfigJson {
        ChordWheelConfigJson {
            positions: vec![
                [500, 100],
                [800, 300],
                [800, 700],
                [500, 900],
                [200, 700],
                [200, 300],
            ],
            strum_position: [900, 650],
            move_delay_ms: 5,
            select_hold_ms: 7,
            after_select_ms: 3,
            page_count: 4,
            page_keys: ["r".into(), "t".into()],
            page_hold_ms: 10,
            page_wait_ms: 20,
        }
    }

    fn processes() -> Vec<String> {
        let game: crate::input::config::GameConfig = serde_json::from_str(include_str!(
            "../../../../src-tauri/resources/defaults/Games/Neverness to Everness/game.json"
        ))
        .unwrap();
        game.process_names
    }

    fn at(start: Instant, ms: u64) -> Instant {
        start + Duration::from_millis(ms)
    }
    fn key(binding: &str) -> Key {
        parse_key_stroke(binding).unwrap().key
    }

    fn prepare_first_page(
        wheel: &mut ChordWheelOutput,
        output: &mut Output,
        start: Instant,
        owner: usize,
    ) {
        wheel
            .prepare(
                output,
                &config(),
                &processes(),
                Some(0),
                0,
                Some(owner),
                start,
            )
            .unwrap();
        wheel.tick(output, start).unwrap();
        wheel.tick(output, at(start, 5)).unwrap();
        wheel.tick(output, at(start, 12)).unwrap();
    }

    #[test]
    fn page_changes_finish_before_selector_click_and_strum_keeps_its_own_deadline() {
        let start = Instant::now();
        let config = config();
        let mut output = Output::default();
        let mut wheel = ChordWheelOutput::default();

        wheel
            .prepare(
                &mut output,
                &config,
                &processes(),
                Some(14),
                2,
                Some(42),
                start,
            )
            .unwrap();
        wheel.tick(&mut output, at(start, 9)).unwrap();
        assert_eq!(
            output.events,
            [
                Event::Move(config.strum_position),
                Event::Key(key("t"), false)
            ]
        );
        wheel.tick(&mut output, at(start, 10)).unwrap();
        assert_eq!(wheel.state().page, Some(1));
        wheel.tick(&mut output, at(start, 29)).unwrap();
        assert_eq!(
            output.events.len(),
            3,
            "page settle time cannot send another page key early"
        );
        wheel.tick(&mut output, at(start, 30)).unwrap();
        wheel.tick(&mut output, at(start, 40)).unwrap();
        assert_eq!(wheel.state().page, Some(2));
        wheel.tick(&mut output, at(start, 60)).unwrap();
        wheel.tick(&mut output, at(start, 60)).unwrap();
        wheel.tick(&mut output, at(start, 65)).unwrap();
        wheel.tick(&mut output, at(start, 72)).unwrap();
        assert!(!wheel.ready(42, at(start, 74)));
        assert!(wheel.ready(42, at(start, 75)));
        wheel.strum(&mut output, 42, 11, at(start, 75)).unwrap();
        wheel.tick(&mut output, at(start, 85)).unwrap();
        assert!(
            wheel.is_active(),
            "a source NoteOff must not shorten the mouse hold"
        );
        wheel.tick(&mut output, at(start, 86)).unwrap();

        assert_eq!(
            output.events,
            [
                Event::Move(config.strum_position),
                Event::Key(key("t"), false),
                Event::Key(key("t"), true),
                Event::Key(key("t"), false),
                Event::Key(key("t"), true),
                Event::Move(config.positions[2]),
                Event::Button(WheelButton::Left, false),
                Event::Button(WheelButton::Left, true),
                Event::Move(config.strum_position),
                Event::Button(WheelButton::Right, false),
                Event::Button(WheelButton::Right, true),
            ]
        );
        assert!(!wheel.is_active());
    }

    #[test]
    fn manual_page_changes_take_the_shortest_wrapped_path_without_clicking_or_strumming() {
        let start = Instant::now();
        let config = config();
        let mut output = Output::default();
        let mut wheel = ChordWheelOutput::default();
        wheel.assume_page(3);
        wheel
            .prepare(&mut output, &config, &processes(), None, 0, None, start)
            .unwrap();
        for ms in [10, 30, 40, 60, 70, 90] {
            wheel.tick(&mut output, at(start, ms)).unwrap();
        }
        assert_eq!(wheel.state().page, Some(0));
        assert!(!wheel.is_active());
        assert_eq!(
            output.events,
            [
                Event::Move(config.strum_position),
                Event::Key(key("t"), false),
                Event::Key(key("t"), true),
            ]
        );
    }

    #[test]
    fn strums_require_the_prepared_owner_and_the_full_selector_settle_time() {
        let start = Instant::now();
        let mut output = Output::default();
        let mut wheel = ChordWheelOutput::default();
        prepare_first_page(&mut wheel, &mut output, start, 7);
        let before = output.events.len();
        assert!(!wheel.ready(7, at(start, 14)));
        assert!(!wheel.ready(8, at(start, 15)));
        assert!(wheel.strum(&mut output, 7, 10, at(start, 14)).is_err());
        assert!(wheel.strum(&mut output, 8, 10, at(start, 15)).is_err());
        assert_eq!(
            output.events.len(),
            before,
            "an early or unrelated source note cannot send mouse input"
        );
        assert!(wheel.strum(&mut output, 7, 10, at(start, 15)).is_ok());
    }

    #[test]
    fn cancelling_a_partial_page_releases_the_key_and_requires_page_resynchronization() {
        let start = Instant::now();
        let mut output = Output::default();
        let mut wheel = ChordWheelOutput::default();
        wheel
            .prepare(
                &mut output,
                &config(),
                &processes(),
                Some(18),
                3,
                Some(4),
                start,
            )
            .unwrap();
        wheel.cancel(&mut output).unwrap();
        assert_eq!(output.events.last(), Some(&Event::Key(key("r"), true)));
        assert_eq!(wheel.state().page, None);
        assert!(!wheel.is_active());
        let before = output.events.len();
        assert!(wheel
            .prepare(
                &mut output,
                &config(),
                &processes(),
                Some(0),
                0,
                Some(5),
                start
            )
            .is_err());
        wheel.tick(&mut output, at(start, 1000)).unwrap();
        assert_eq!(
            output.events.len(),
            before,
            "cancelled preparation cannot resume itself"
        );
        wheel.assume_page(2);
        wheel
            .prepare(
                &mut output,
                &config(),
                &processes(),
                Some(12),
                2,
                Some(5),
                start,
            )
            .unwrap();
        assert_eq!(wheel.state().page, Some(2));
    }

    #[test]
    fn focus_loss_during_selection_and_stop_during_strum_release_the_owned_button() {
        let start = Instant::now();
        let mut output = Output::default();
        let mut wheel = ChordWheelOutput::default();
        wheel
            .prepare(
                &mut output,
                &config(),
                &processes(),
                Some(0),
                0,
                Some(1),
                start,
            )
            .unwrap();
        wheel.tick(&mut output, start).unwrap();
        wheel.tick(&mut output, at(start, 5)).unwrap();
        output.focused = false;
        assert!(wheel
            .tick(&mut output, at(start, 6))
            .unwrap_err()
            .contains("focus"));
        wheel.cancel(&mut output).unwrap();
        assert_eq!(
            output.events.last(),
            Some(&Event::Button(WheelButton::Left, true))
        );
        assert_eq!(wheel.state().page, Some(0));
        output.focused = true;
        prepare_first_page(&mut wheel, &mut output, at(start, 20), 2);
        wheel.strum(&mut output, 2, 100, at(start, 35)).unwrap();
        wheel.cancel(&mut output).unwrap();
        assert_eq!(
            output.events.last(),
            Some(&Event::Button(WheelButton::Right, true))
        );
        let before = output.events.len();
        wheel.tick(&mut output, at(start, 1000)).unwrap();
        assert_eq!(output.events.len(), before);
        assert!(!wheel.is_active());
    }

    #[test]
    fn failed_mouse_release_stays_owned_until_cleanup_can_retry() {
        let start = Instant::now();
        let mut output = Output::default();
        let mut wheel = ChordWheelOutput::default();
        prepare_first_page(&mut wheel, &mut output, start, 1);
        wheel.strum(&mut output, 1, 100, at(start, 15)).unwrap();
        output.fail_release = true;
        assert_eq!(wheel.cancel(&mut output).unwrap_err(), "release failed");
        assert!(
            wheel.is_active(),
            "failed cleanup must retain ownership for retry"
        );
        wheel.cancel(&mut output).unwrap();
        assert!(!wheel.is_active());
        assert_eq!(
            output
                .events
                .iter()
                .filter(|event| **event == Event::Button(WheelButton::Right, true))
                .count(),
            2
        );
    }

    #[test]
    fn late_scheduler_ticks_preserve_minimum_selector_and_mouse_holds() {
        let start = Instant::now();
        let mut output = Output::default();
        let mut wheel = ChordWheelOutput::default();
        wheel
            .prepare(
                &mut output,
                &config(),
                &processes(),
                Some(0),
                0,
                Some(1),
                start,
            )
            .unwrap();
        wheel.tick(&mut output, start).unwrap();
        wheel.tick(&mut output, at(start, 100)).unwrap();
        let before = output.events.len();
        wheel.tick(&mut output, at(start, 106)).unwrap();
        assert_eq!(
            output.events.len(),
            before,
            "late mouse-down still receives its full hold"
        );
        wheel.tick(&mut output, at(start, 107)).unwrap();
        assert!(!wheel.ready(1, at(start, 109)));
        wheel.strum(&mut output, 1, 20, at(start, 110)).unwrap();
        wheel.tick(&mut output, at(start, 129)).unwrap();
        assert!(wheel.is_active());
        wheel.tick(&mut output, at(start, 130)).unwrap();
        assert!(!wheel.is_active());
    }

    #[test]
    fn repeated_chord_strums_reuse_the_selected_slot_without_another_click() {
        let start = Instant::now();
        let mut output = Output::default();
        let mut wheel = ChordWheelOutput::default();
        prepare_first_page(&mut wheel, &mut output, start, 1);
        wheel.strum(&mut output, 1, 10, at(start, 15)).unwrap();
        wheel.tick(&mut output, at(start, 25)).unwrap();
        let before = output.events.len();
        assert_eq!(wheel.preparation_ms(&config(), 0), 2);
        wheel
            .prepare(
                &mut output,
                &config(),
                &processes(),
                Some(0),
                0,
                Some(2),
                at(start, 30),
            )
            .unwrap();
        assert!(wheel.ready(2, at(start, 30)));
        wheel.strum(&mut output, 2, 10, at(start, 30)).unwrap();
        assert!(!output.events[before..]
            .iter()
            .any(|event| matches!(event, Event::Button(WheelButton::Left, _))));
        assert!(output.events[before..].contains(&Event::Button(WheelButton::Right, false)));
    }

    #[test]
    fn wheel_refuses_input_without_inherited_game_processes() {
        let start = Instant::now();
        let mut output = Output::default();
        let mut wheel = ChordWheelOutput::default();
        assert!(wheel
            .prepare(&mut output, &config(), &[], Some(0), 0, Some(1), start)
            .is_err());
        assert!(output.events.is_empty());
    }

    #[test]
    fn up_strum_clicks_and_releases_left_mouse_button_at_strum_position() {
        let start = Instant::now();
        let config = config();
        let mut output = Output::default();
        let mut wheel = ChordWheelOutput::default();
        prepare_first_page(&mut wheel, &mut output, start, 9);
        let before = output.events.len();
        wheel
            .strum_direction(&mut output, 9, StrumDirection::Up, 12, at(start, 15))
            .unwrap();
        assert!(wheel.is_active());
        wheel.tick(&mut output, at(start, 27)).unwrap();
        assert!(!wheel.is_active());
        assert_eq!(
            output.events[before..],
            [
                Event::Move(config.strum_position),
                Event::Button(WheelButton::Left, false),
                Event::Button(WheelButton::Left, true),
            ]
        );
    }

    #[test]
    fn next_deadline_tracks_pending_stages_and_clears_once_ready() {
        let start = Instant::now();
        let config = config();
        let mut output = Output::default();
        let mut wheel = ChordWheelOutput::default();
        assert_eq!(wheel.next_deadline(start), None);

        wheel
            .prepare(&mut output, &config, &processes(), Some(0), 0, Some(3), start)
            .unwrap();
        assert_eq!(wheel.next_deadline(start), Some(start));
        wheel.tick(&mut output, start).unwrap();
        assert_eq!(wheel.next_deadline(start), Some(at(start, 5)));
        wheel.tick(&mut output, at(start, 5)).unwrap();
        assert_eq!(wheel.next_deadline(at(start, 5)), Some(at(start, 12)));
        wheel.tick(&mut output, at(start, 12)).unwrap();
        assert_eq!(wheel.next_deadline(at(start, 12)), Some(at(start, 15)));
        assert_eq!(wheel.next_deadline(at(start, 15)), None);

        wheel.strum(&mut output, 3, 20, at(start, 15)).unwrap();
        assert_eq!(wheel.next_deadline(at(start, 15)), Some(at(start, 35)));
    }

    #[test]
    fn transition_budget_scales_slack_with_pages_and_mode_switches() {
        let config = config();
        let base = u64::from(config.move_delay_ms + config.select_hold_ms + config.after_select_ms);
        let page = u64::from(config.page_hold_ms + config.page_wait_ms);
        let switch = super::super::simulator::MODE_HOLD_MS + super::super::simulator::MODE_SETTLE_MS;

        assert_eq!(wheel_transition_ms(&config, 0, Some(0), true, false), 2);
        assert_eq!(wheel_transition_ms(&config, 0, Some(0), false, false), base + 45);
        assert_eq!(wheel_transition_ms(&config, 6, Some(0), false, false), page + base + 65);
        assert_eq!(wheel_transition_ms(&config, 6, Some(0), false, true), switch + page + base + 90);
    }

    #[test]
    fn next_deadline_keeps_due_cleanup_for_expired_and_ownerless_ready_attacks() {
        let start = Instant::now();
        let config = config();
        let mut output = Output::default();
        let mut wheel = ChordWheelOutput::default();
        prepare_first_page(&mut wheel, &mut output, start, 3);
        assert_eq!(wheel.next_deadline(at(start, 15)), None);

        wheel.expire(3);
        assert_eq!(wheel.next_deadline(at(start, 15)), Some(at(start, 15)));
        wheel.tick(&mut output, at(start, 15)).unwrap();
        assert!(!wheel.is_active());
        assert_eq!(wheel.next_deadline(at(start, 15)), None);

        wheel
            .prepare(&mut output, &config, &processes(), Some(1), 0, None, at(start, 20))
            .unwrap();
        wheel.tick(&mut output, at(start, 20)).unwrap();
        wheel.tick(&mut output, at(start, 25)).unwrap();
        wheel.tick(&mut output, at(start, 32)).unwrap();
        assert_eq!(wheel.next_deadline(at(start, 35)), Some(at(start, 35)));
        wheel.tick(&mut output, at(start, 35)).unwrap();
        assert!(!wheel.is_active());
        assert_eq!(wheel.next_deadline(at(start, 35)), None);
    }
}
