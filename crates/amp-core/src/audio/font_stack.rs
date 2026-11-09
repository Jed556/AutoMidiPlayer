use rustysynth::{SoundFont, Synthesizer, SynthesizerSettings};
use std::sync::Arc;

/// One synth per bank; each note starts in exactly one bank. Controllers and
/// releases reach every synth so fallback voices retain normal MIDI semantics.
pub(super) struct FontStack {
    fonts: Vec<Arc<SoundFont>>,
    synths: Vec<Synthesizer>,
    banks: [i32; 16],
    programs: [i32; 16],
    left: Vec<f32>,
    right: Vec<f32>,
    held: [[bool; 128]; 16],
    sustain: [bool; 16],
    latched: [bool; 16],
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::fs::File;

    fn font(name: &str) -> Arc<SoundFont> {
        let path = format!(
            "{}/../../src-tauri/resources/soundfonts/{name}",
            env!("CARGO_MANIFEST_DIR")
        );
        Arc::new(SoundFont::new(&mut File::open(path).unwrap()).unwrap())
    }

    #[test]
    fn whistle_gap_uses_generaluser_and_renders_audio() {
        let mut stack = FontStack::new(
            vec![
                font("TimGM6mb.sf2"),
                font("GeneralUser-GS.sf2"),
                font("legacy.sf2"),
            ],
            &SynthesizerSettings::new(44100),
        )
        .unwrap();
        stack.process_midi_message(5, 0xC0, 78, 0);
        for key in 84..=93 {
            assert_eq!(stack.source(5, key, 100), Some(1));
        }
        assert_eq!(stack.source(5, 72, 100), Some(0));
        assert_eq!(stack.source(0, 60, 100), Some(0));
        stack.note_on(5, 88, 100);
        let mut l = vec![0.0; 4096];
        let mut r = l.clone();
        stack.render(&mut l, &mut r);
        assert!(l.iter().chain(&r).any(|v| v.abs() > 0.0001));
        stack.note_off(5, 88);
        stack.reset();
        assert_eq!(stack.programs[5], 0);
        assert_eq!(stack.banks[9], 128);
    }

    #[test]
    fn order_and_disabled_gap_filling() {
        let tim = font("TimGM6mb.sf2");
        let gm = font("legacy.sf2");
        let mut stack = FontStack::new(
            vec![gm.clone(), tim.clone()],
            &SynthesizerSettings::new(44100),
        )
        .unwrap();
        stack.process_midi_message(0, 0xC0, 78, 0);
        assert_eq!(stack.source(0, 88, 100), Some(0));
        let mut single = FontStack::new(vec![tim], &SynthesizerSettings::new(44100)).unwrap();
        single.process_midi_message(0, 0xC0, 78, 0);
        single.note_on(0, 88, 100);
        let mut l = vec![0.0; 1024];
        let mut r = l.clone();
        single.render(&mut l, &mut r);
        assert!(l.iter().chain(&r).all(|v| *v == 0.0));
    }

    #[test]
    fn bank_drum_and_velocity_coverage() {
        let tim = font("TimGM6mb.sf2");
        assert!(FontStack::covers(&tim, 0, 0, 60, 100));
        assert!(!FontStack::covers(&tim, 0, 78, 88, 100));
        assert!(!FontStack::covers(&tim, 0, 0, 60, 128));
        assert!(FontStack::covers(&tim, 128, 0, 38, 100));
        let mut stack = FontStack::new(
            vec![tim, font("legacy.sf2")],
            &SynthesizerSettings::new(44100),
        )
        .unwrap();
        stack.process_midi_message(0, 0xB0, 0, 127);
        assert_eq!(stack.source(0, 60, 100), Some(0));
        assert_eq!(stack.source(9, 38, 100), Some(0));
        stack.note_on(-1, 60, 100); // malformed channels must not panic
    }

    #[test]
    fn fallback_is_not_doubled_and_receives_controllers_and_releases() {
        let tim = font("TimGM6mb.sf2");
        let gm = font("GeneralUser-GS.sf2");
        let settings = SynthesizerSettings::new(44100);
        let mut stack = FontStack::new(vec![tim, gm.clone()], &settings).unwrap();
        let mut reference = FontStack::new(vec![gm], &settings).unwrap();
        for synth in [&mut stack, &mut reference] {
            synth.process_midi_message(5, 0xC0, 78, 0);
            synth.process_midi_message(5, 0xB0, 7, 80);
            synth.process_midi_message(5, 0xB0, 10, 20);
            synth.process_midi_message(5, 0xB0, 64, 127);
            synth.note_on(5, 88, 100);
        }
        let mut l = vec![0.0; 2048];
        let mut r = l.clone();
        let mut expected_l = l.clone();
        let mut expected_r = r.clone();
        stack.render(&mut l, &mut r);
        reference.render(&mut expected_l, &mut expected_r);
        assert_eq!(l, expected_l);
        assert_eq!(r, expected_r);
        for synth in [&mut stack, &mut reference] {
            synth.note_off(5, 88);
            synth.process_midi_message(5, 0xB0, 64, 0);
        }
        stack.render(&mut l, &mut r);
        reference.render(&mut expected_l, &mut expected_r);
        assert_eq!(l, expected_l);
        assert_eq!(r, expected_r);
    }
}

impl FontStack {
    pub fn new(fonts: Vec<Arc<SoundFont>>, settings: &SynthesizerSettings) -> Result<Self, String> {
        let synths = fonts
            .iter()
            .map(|f| Synthesizer::new(f, settings).map_err(|e| e.to_string()))
            .collect::<Result<Vec<_>, _>>()?;
        let mut banks = [0; 16];
        banks[9] = 128;
        if fonts.is_empty() {
            return Err("No SoundFonts selected".into());
        }
        Ok(Self {
            held: [[false; 128]; 16],
            sustain: [false; 16],
            latched: [false; 16],
            fonts,
            synths,
            banks,
            programs: [0; 16],
            left: vec![0.0; 256],
            right: vec![0.0; 256],
        })
    }

    fn covers(font: &SoundFont, bank: i32, program: i32, key: i32, velocity: i32) -> bool {
        font.get_presets()
            .iter()
            .filter(|p| p.get_bank_number() == bank && p.get_patch_number() == program)
            .any(|p| {
                p.get_regions().iter().any(|r| {
                    r.contains(key, velocity)
                        && font.get_instruments()[r.get_instrument_id()]
                            .get_regions()
                            .iter()
                            .any(|r| r.contains(key, velocity))
                })
            })
    }

    fn source(&self, channel: usize, key: i32, velocity: i32) -> Option<usize> {
        if self.fonts.len() == 1 {
            return Some(0);
        }
        let bank = self.banks[channel];
        let program = self.programs[channel];
        // Search the entire stack for the requested bank before trying GM.
        self.fonts
            .iter()
            .position(|f| Self::covers(f, bank, program, key, velocity))
            .or_else(|| {
                self.fonts.iter().position(|f| {
                    Self::covers(
                        f,
                        if bank >= 128 { 128 } else { 0 },
                        if bank >= 128 { 0 } else { program },
                        key,
                        velocity,
                    )
                })
            })
    }

    pub fn note_on(&mut self, channel: i32, key: i32, velocity: i32) {
        if !(0..16).contains(&channel) || !(0..128).contains(&key) {
            return;
        }
        if velocity == 0 {
            self.note_off(channel, key);
            return;
        }
        if let Some(i) = self.source(channel as usize, key, velocity) {
            self.held[channel as usize][key as usize] = true;
            self.synths[i].note_on(channel, key, velocity);
        }
    }

    pub fn note_off(&mut self, channel: i32, key: i32) {
        if !(0..16).contains(&channel) || !(0..128).contains(&key) {
            return;
        }
        let ch = channel as usize;
        if self.held[ch][key as usize] && self.sustain[ch] {
            self.latched[ch] = true;
        }
        self.held[ch][key as usize] = false;
        for s in &mut self.synths {
            s.note_off(channel, key);
        }
    }

    pub fn process_midi_message(&mut self, channel: i32, command: i32, a: i32, b: i32) {
        if !(0..16).contains(&channel) {
            return;
        }
        if command == 0x90 {
            self.note_on(channel, a, b);
            return;
        }
        if command == 0x80 {
            self.note_off(channel, a);
            return;
        }
        if command == 0xB0 {
            let ch = channel as usize;
            match a {
                64 => {
                    self.sustain[ch] = b >= 64;
                    if b < 64 {
                        self.latched[ch] = false;
                    }
                }
                121 => {
                    self.sustain[ch] = false;
                    self.latched[ch] = false;
                }
                120 => {
                    self.held[ch].fill(false);
                    self.latched[ch] = false;
                }
                123 => {
                    if self.sustain[ch] && self.held[ch].iter().any(|v| *v) {
                        self.latched[ch] = true;
                    }
                    self.held[ch].fill(false);
                }
                _ => {}
            }
        }
        if command == 0xC0 {
            self.programs[channel as usize] = a;
        }
        if command == 0xB0 && a == 0 {
            self.banks[channel as usize] = b + if channel == 9 { 128 } else { 0 };
        }
        for s in &mut self.synths {
            s.process_midi_message(channel, command, a, b);
        }
    }

    pub fn has_pending_notes(&self) -> bool {
        self.held.iter().flatten().any(|v| *v) || self.latched.iter().any(|v| *v)
    }

    pub fn reset(&mut self) {
        self.held = [[false; 128]; 16];
        self.sustain = [false; 16];
        self.latched = [false; 16];
        self.banks = [0; 16];
        self.banks[9] = 128;
        self.programs = [0; 16];
        for s in &mut self.synths {
            s.reset();
        }
    }

    pub fn render(&mut self, left: &mut [f32], right: &mut [f32]) {
        left.fill(0.0);
        right.fill(0.0);
        self.left.resize(left.len(), 0.0);
        self.right.resize(right.len(), 0.0);
        for s in &mut self.synths {
            s.render(&mut self.left, &mut self.right);
            for (out, sample) in left.iter_mut().zip(&self.left) {
                *out += sample;
            }
            for (out, sample) in right.iter_mut().zip(&self.right) {
                *out += sample;
            }
        }
    }
}
