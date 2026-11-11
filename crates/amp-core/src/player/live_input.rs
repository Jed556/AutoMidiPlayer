use super::note_timing::{KeyPressAction, KeyReleases, OverlapBehavior};
use crate::{AudioSynth, KeySimulator, NoteKeyMapping, SynthMode};
use enigo::Key;
use std::collections::{HashMap, HashSet, VecDeque};
use std::time::{Duration, Instant};

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum MidiMessage {
    On {
        channel: u8,
        pitch: u8,
        velocity: u8,
    },
    Off {
        channel: u8,
        pitch: u8,
    },
    Control {
        channel: u8,
        controller: u8,
        value: u8,
    },
}

impl MidiMessage {
    pub fn parse(bytes: &[u8]) -> Option<Self> {
        let &[status, a, b] = bytes else { return None };
        if a > 127 || b > 127 {
            return None;
        }
        let channel = status & 15;
        match status >> 4 {
            9 if b > 0 => Some(Self::On {
                channel,
                pitch: a,
                velocity: b,
            }),
            8 | 9 => Some(Self::Off { channel, pitch: a }),
            11 if matches!(a, 64 | 66 | 67 | 120 | 121 | 123) => Some(Self::Control {
                channel,
                controller: a,
                value: b,
            }),
            _ => None,
        }
    }
}

#[derive(Clone, Debug, Default, PartialEq, Eq, serde::Serialize)]
#[serde(rename_all = "camelCase")]
pub struct LiveInputSnapshot {
    pub generation: u64,
    pub active: bool,
    pub audio_available: bool,
    pub keyboard_available: bool,
    pub listen_mode: bool,
    pub game_allowed: bool,
    pub pitches: Vec<u8>,
    pub error: Option<String>,
}

struct Note {
    channel: u8,
    pitch: u8,
    key: Option<Key>,
    audio: bool,
    deadline: Option<Instant>,
    tap_ms: u64,
}

#[derive(Default)]
pub(super) struct LiveInput {
    pub snapshot: LiveInputSnapshot,
    generation: u64,
    next_owner: usize,
    notes: HashMap<usize, Note>,
    fifo: HashMap<(u8, u8), VecDeque<usize>>,
    keys: KeyReleases,
    pedals: [[bool; 3]; 16],
    pedal_keys: HashSet<Key>,
}

pub(super) struct LiveSettings<'a> {
    pub mapping: &'a NoteKeyMapping,
    pub listen: bool,
    pub synth_mode: SynthMode,
    pub hold: bool,
    pub tap_ms: u64,
    pub preview_timing: bool,
    pub overlap: OverlapBehavior,
    pub pedals_enabled: bool,
}

impl LiveInput {
    pub fn start(&mut self, generation: u64, sim: &mut KeySimulator, synth: &Option<AudioSynth>) {
        self.clear(sim, synth);
        self.generation = generation;
        self.snapshot.generation = generation;
        self.snapshot.active = true;
        self.snapshot.error = None;
    }
    pub fn stop(&mut self, sim: &mut KeySimulator, synth: &Option<AudioSynth>) {
        self.clear(sim, synth);
        self.snapshot.active = false;
    }
    pub fn clear(&mut self, sim: &mut KeySimulator, synth: &Option<AudioSynth>) {
        sim.release_source(2);
        sim.release_source(3);
        if let Some(synth) = synth {
            synth.for_source(1).all_notes_off();
        }
        self.notes.clear();
        self.fifo.clear();
        self.keys.clear();
        self.pedals = [[false; 3]; 16];
        self.pedal_keys.clear();
        self.snapshot.pitches.clear();
    }
    pub fn gate(&mut self, allowed: bool, sim: &mut KeySimulator, synth: &Option<AudioSynth>) {
        if self.snapshot.game_allowed != allowed && !self.snapshot.listen_mode {
            self.clear(sim, synth);
        }
        self.snapshot.game_allowed = allowed;
    }
    pub fn message(
        &mut self,
        generation: u64,
        message: MidiMessage,
        now: Instant,
        settings: &LiveSettings,
        sim: &mut KeySimulator,
        synth: &Option<AudioSynth>,
    ) {
        self.snapshot.listen_mode = settings.listen;
        if !self.snapshot.active
            || generation != self.generation
            || (!settings.listen && !self.snapshot.game_allowed)
        {
            return;
        }
        let audio = synth.as_ref().map(|s| s.for_source(1));
        match message {
            MidiMessage::On {
                channel,
                pitch,
                velocity,
            } => {
                // Bound notes AND unmatched NoteOff placeholders. A bad device
                // must not grow the queue indefinitely or starve the engine.
                if self.fifo.values().map(VecDeque::len).sum::<usize>() >= 4096 {
                    self.stop(sim, synth);
                    self.snapshot.error =
                        Some("MIDI input overflow. Stop the device and start input again.".into());
                    return;
                }
                let stroke = settings.mapping.get_stroke(pitch);
                let raw_audio = settings.listen && settings.synth_mode == SynthMode::Raw;
                self.next_owner = self.next_owner.wrapping_add(1);
                let owner = self.next_owner;
                self.fifo
                    .entry((channel, pitch))
                    .or_default()
                    .push_back(owner);
                if stroke.is_none() && !raw_audio {
                    return;
                }
                let timed = !settings.listen || (!raw_audio && settings.preview_timing);
                let mut deadline =
                    (timed && !settings.hold).then(|| now + Duration::from_millis(settings.tap_ms));
                let key = (!settings.listen).then(|| stroke.map(|s| s.key)).flatten();
                if let Some(stroke) = stroke.filter(|_| !settings.listen) {
                    let (action, at) =
                        self.keys
                            .begin_stroke(stroke, owner, now, deadline, settings.overlap);
                    deadline = deadline.map(|d| at + d.saturating_duration_since(now));
                    match action {
                        KeyPressAction::Press => {
                            for (key, up) in stroke.press_events() {
                                sim.send_owned_key(2, key, up);
                            }
                        }
                        KeyPressAction::Retrigger => sim.send_owned_key(2, stroke.key, true),
                        _ => {}
                    }
                }
                if settings.listen {
                    if let Some(s) = &audio {
                        s.note_on(channel, pitch, velocity);
                    }
                }
                self.notes.insert(
                    owner,
                    Note {
                        channel,
                        pitch,
                        key,
                        audio: settings.listen,
                        deadline,
                        tap_ms: settings.tap_ms,
                    },
                );
            }
            MidiMessage::Off { channel, pitch } => {
                if let Some(owner) = self
                    .fifo
                    .get_mut(&(channel, pitch))
                    .and_then(VecDeque::pop_front)
                {
                    if self.notes.get(&owner).is_some_and(|n| n.deadline.is_none()) {
                        self.finish(owner, sim, &audio);
                    }
                }
                self.fifo.retain(|_, q| !q.is_empty());
            }
            MidiMessage::Control {
                channel,
                controller,
                value,
            } => {
                if matches!(controller, 120 | 123) {
                    let owners: Vec<_> = self
                        .notes
                        .iter()
                        .filter(|(_, n)| n.channel == channel)
                        .map(|(&o, _)| o)
                        .collect();
                    for owner in owners {
                        self.finish(owner, sim, &audio);
                    }
                    self.fifo.retain(|(ch, _), _| *ch != channel);
                    self.pedals[channel as usize] = [false; 3];
                } else if controller == 121 {
                    self.pedals[channel as usize] = [false; 3];
                } else if let Some(i) = [64, 66, 67].iter().position(|&cc| cc == controller) {
                    self.pedals[channel as usize][i] = value >= 64;
                }
                if settings.listen {
                    if let Some(s) = &audio {
                        // Do not forward channel panic to the shared synth: it
                        // would silence backing notes belonging to the song.
                        for (i, cc) in [64, 66, 67].into_iter().enumerate() {
                            s.control_change(
                                channel,
                                cc,
                                if self.pedals[channel as usize][i] {
                                    127
                                } else {
                                    0
                                },
                            );
                        }
                    }
                }
            }
        }
        self.sync_pedals(settings, sim);
        self.publish();
    }
    fn sync_pedals(&mut self, settings: &LiveSettings, sim: &mut KeySimulator) {
        let desired: HashSet<_> = settings
            .mapping
            .pedal_bindings()
            .iter()
            .enumerate()
            .filter_map(|(i, stroke)| {
                stroke.filter(|_| {
                    settings.pedals_enabled && !settings.listen && self.pedals.iter().any(|p| p[i])
                })
            })
            .map(|s| s.key)
            .collect();
        for key in self.pedal_keys.difference(&desired) {
            sim.send_owned_key(3, *key, true);
        }
        for stroke in settings.mapping.pedal_bindings().iter().flatten() {
            if desired.contains(&stroke.key) && !self.pedal_keys.contains(&stroke.key) {
                for (key, up) in stroke.press_events() {
                    sim.send_owned_key(3, key, up);
                }
            }
        }
        self.pedal_keys = desired;
    }
    fn finish(&mut self, owner: usize, sim: &mut KeySimulator, audio: &Option<AudioSynth>) {
        let Some(note) = self.notes.remove(&owner) else {
            return;
        };
        if let Some(key) = note.key {
            if self.keys.cancel_owner(key, owner) {
                sim.send_owned_key(2, key, true);
            }
        }
        if note.audio
            && !self
                .notes
                .values()
                .any(|n| n.audio && n.channel == note.channel && n.pitch == note.pitch)
        {
            if let Some(s) = audio {
                s.note_off(note.channel, note.pitch);
            }
        }
    }
    pub fn tick(
        &mut self,
        now: Instant,
        sim: &mut KeySimulator,
        synth: &Option<AudioSynth>,
        gap_ms: u64,
    ) {
        self.keys.set_repress_gap_ms(gap_ms);
        for (stroke, owners) in self.keys.take_presses(now) {
            for (key, up) in stroke.press_events() {
                sim.send_owned_key(2, key, up);
            }
            for owner in owners {
                if let Some(n) = self.notes.get_mut(&owner) {
                    n.deadline = n.deadline.map(|_| now + Duration::from_millis(n.tap_ms));
                }
            }
        }
        for key in self.keys.take_due(now) {
            sim.send_owned_key(2, key, true);
        }
        let due: Vec<_> = self
            .notes
            .iter()
            .filter(|(_, n)| n.deadline.is_some_and(|d| d <= now))
            .map(|(&o, _)| o)
            .collect();
        let audio = synth.as_ref().map(|s| s.for_source(1));
        for owner in due {
            self.finish(owner, sim, &audio);
        }
        self.publish();
    }
    fn publish(&mut self) {
        let mut pitches: Vec<_> = self
            .notes
            .values()
            .map(|n| n.pitch)
            .collect::<HashSet<_>>()
            .into_iter()
            .collect();
        pitches.sort_unstable();
        self.snapshot.pitches = pitches;
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::KeyboardLayout;
    fn settings(mapping: &NoteKeyMapping, hold: bool) -> LiveSettings<'_> {
        LiveSettings {
            mapping,
            listen: false,
            synth_mode: SynthMode::Adapted,
            hold,
            tap_ms: 50,
            preview_timing: false,
            overlap: OverlapBehavior::Retrigger,
            pedals_enabled: true,
        }
    }
    #[test]
    fn parser_filters_messages_and_normalizes_zero_velocity() {
        assert_eq!(
            MidiMessage::parse(&[0x91, 60, 0]),
            Some(MidiMessage::Off {
                channel: 1,
                pitch: 60
            })
        );
        for bytes in [
            &[0xf8][..],
            &[0x90, 255, 1],
            &[0xb0, 7, 100],
            &[0x90, 60],
            &[0xe0, 0, 64],
        ] {
            assert_eq!(MidiMessage::parse(bytes), None);
        }
    }
    #[test]
    fn holds_taps_stale_connections_and_focus_loss() {
        let mapping = NoteKeyMapping::new(KeyboardLayout::Diatonic21);
        let mut sim = KeySimulator::new();
        struct Sink;
        impl crate::input::simulator::KeyboardOutput for Sink {
            fn key(&mut self, _: Key, _: bool) -> Result<(), String> {
                Ok(())
            }
        }
        sim.set_output(Some(Box::new(Sink)));
        let mut input = LiveInput::default();
        input.start(1, &mut sim, &None);
        input.gate(true, &mut sim, &None);
        let now = Instant::now();
        let on = MidiMessage::On {
            channel: 0,
            pitch: 60,
            velocity: 100,
        };
        input.message(0, on, now, &settings(&mapping, true), &mut sim, &None);
        assert!(input.snapshot.pitches.is_empty());
        input.message(1, on, now, &settings(&mapping, false), &mut sim, &None);
        input.message(
            1,
            MidiMessage::Off {
                channel: 0,
                pitch: 60,
            },
            now,
            &settings(&mapping, false),
            &mut sim,
            &None,
        );
        assert_eq!(input.snapshot.pitches, [60]);
        input.tick(now + Duration::from_millis(51), &mut sim, &None, 20);
        assert!(input.snapshot.pitches.is_empty());
        input.message(1, on, now, &settings(&mapping, true), &mut sim, &None);
        input.tick(now + Duration::from_secs(10), &mut sim, &None, 20);
        assert_eq!(input.snapshot.pitches, [60]);
        input.gate(false, &mut sim, &None);
        assert!(input.snapshot.pitches.is_empty());
        assert!(input.snapshot.active);
    }

    #[test]
    fn exact_mapping_repeated_notes_channel_panic_and_disconnect() {
        let mapping = NoteKeyMapping::new(KeyboardLayout::Diatonic21);
        let mut sim = KeySimulator::new();
        let mut input = LiveInput::default();
        let mut settings = settings(&mapping, true);
        settings.listen = true; // silent test backend; no OS input
        let now = Instant::now();
        input.start(7, &mut sim, &None);
        let on = |channel, pitch| MidiMessage::On {
            channel,
            pitch,
            velocity: 100,
        };
        input.message(7, on(0, 61), now, &settings, &mut sim, &None);
        assert!(
            input.snapshot.pitches.is_empty(),
            "no smart snapping for physical input"
        );
        for ch in [0, 0, 1] {
            input.message(7, on(ch, 60), now, &settings, &mut sim, &None);
        }
        input.message(
            7,
            MidiMessage::Off {
                channel: 0,
                pitch: 60,
            },
            now,
            &settings,
            &mut sim,
            &None,
        );
        assert_eq!(input.notes.len(), 2);
        input.message(
            7,
            MidiMessage::Control {
                channel: 0,
                controller: 123,
                value: 0,
            },
            now,
            &settings,
            &mut sim,
            &None,
        );
        assert_eq!(
            input.notes.len(),
            1,
            "channel panic leaves other channels intact"
        );
        input.stop(&mut sim, &None);
        assert!(input.snapshot.pitches.is_empty());
        assert!(!input.snapshot.active);
        input.start(8, &mut sim, &None);
        input.message(7, on(1, 60), now, &settings, &mut sim, &None);
        assert!(
            input.snapshot.pitches.is_empty(),
            "old connection cannot reassert notes"
        );
        settings.synth_mode = SynthMode::Raw;
        input.message(8, on(0, 61), now, &settings, &mut sim, &None);
        assert_eq!(
            input.snapshot.pitches,
            [61],
            "raw Listen Mode stays chromatic"
        );
    }
}
