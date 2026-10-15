use std::collections::{HashMap, HashSet};
use enigo::Key;

/// A game binding is a physical key plus modifiers, not a text character.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub struct KeyStroke {
    pub key: Key,
    pub shift: bool,
    pub control: bool,
}

impl KeyStroke {
    pub fn plain(key: Key) -> Self {
        Self { key, shift: false, control: false }
    }

    /// Modifiers apply only to this key-down. Releasing them immediately keeps
    /// subsequent white notes in a mixed chord from inheriting Shift or Ctrl.
    /// The base key stays held until the engine's NoteOff or tap deadline.
    pub fn press_events(self) -> Vec<(Key, bool)> {
        let mut events = Vec::with_capacity(5);
        if self.control { events.push((Key::Control, false)); }
        if self.shift { events.push((Key::Shift, false)); }
        events.push((self.key, false));
        if self.shift { events.push((Key::Shift, true)); }
        if self.control { events.push((Key::Control, true)); }
        events
    }
}

/// Parse the same binding syntax as the original player: uppercase and US
/// shifted punctuation use Shift, ^x uses Ctrl, and ^ alone means Shift+6.
pub fn parse_key_stroke(binding: &str) -> Option<KeyStroke> {
    if binding == " " { return Some(KeyStroke::plain(Key::Space)); }
    let binding = binding.trim();
    if binding.is_empty() { return None; }
    if binding.len() > 1 && binding.starts_with('^') {
        let mut stroke = parse_key_stroke(&binding[1..])?;
        stroke.control = true;
        return Some(stroke);
    }
    if binding.chars().count() != 1 {
        return parse_key_str(binding).map(KeyStroke::plain);
    }
    let character = binding.chars().next()?;
    let shifted_base = match character {
        '!' => Some('1'), '@' => Some('2'), '#' => Some('3'),
        '$' => Some('4'), '%' => Some('5'), '^' => Some('6'),
        '&' => Some('7'), '*' => Some('8'), '(' => Some('9'), ')' => Some('0'),
        '_' => Some('-'), '+' => Some('='), '{' => Some('['), '}' => Some(']'),
        '|' => Some('\\'), ':' => Some(';'), '"' => Some('\''),
        '<' => Some(','), '>' => Some('.'), '?' => Some('/'), '~' => Some('`'),
        'A'..='Z' => Some(character.to_ascii_lowercase()),
        _ => None,
    };
    let base = shifted_base.unwrap_or(character);
    // Named OEM keys send physical punctuation keys on Windows rather than
    // asking the text-input path to interpret a printable symbol as a key.
    #[cfg(target_os = "windows")]
    let key = match base {
        '-' => Some(Key::OEMMinus), '=' => Some(Key::OEMPlus),
        '[' => Some(Key::OEM4), ']' => Some(Key::OEM6), '\\' => Some(Key::OEM5),
        ';' => Some(Key::OEM1), '\'' => Some(Key::OEM7), ',' => Some(Key::OEMComma),
        '.' => Some(Key::OEMPeriod), '/' => Some(Key::OEM2), '`' => Some(Key::OEM3),
        _ => char_to_key(base),
    }?;
    #[cfg(not(target_os = "windows"))]
    let key = char_to_key(base)?;
    Some(KeyStroke { key, shift: shifted_base.is_some(), control: false })
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, serde::Serialize, serde::Deserialize)]
pub enum KeyboardLayout {
    /// 21-Key diatonic 3-octave layout (Genshin, Heartopia, etc.)
    Diatonic21,
    /// 15-Key diatonic layout (Sky: Children of the Light)
    Diatonic15,
    /// 8-Pad drum layout (Djem-djem, Halcyon, etc.)
    Drum8,
    /// 36-Key chromatic layout
    Chromatic36,
}

pub const DIATONIC_21_NOTES: [u8; 21] = [
    48, 50, 52, 53, 55, 57, 59, // Octave 3 (Z X C V B N M)
    60, 62, 64, 65, 67, 69, 71, // Octave 4 (A S D F G H J)
    72, 74, 76, 77, 79, 81, 83, // Octave 5 (Q W E R T Y U)
];

#[derive(Debug, Clone)]
pub struct NoteKeyMapping {
    layout: KeyboardLayout,
    map: HashMap<u8, KeyStroke>,
    playable_notes: Vec<u8>,
    pedal_bindings: [Option<KeyStroke>; 3],
}

impl NoteKeyMapping {
    pub fn new(layout: KeyboardLayout) -> Self {
        let mut map = HashMap::new();
        let mut playable_notes = Vec::new();

        match layout {
            KeyboardLayout::Diatonic21 => {
                // Lower Octave (C3 - B3) -> Z X C V B N M
                let lower = [(48, 'z'), (50, 'x'), (52, 'c'), (53, 'v'), (55, 'b'), (57, 'n'), (59, 'm')];
                for (note, ch) in lower {
                    if let Some(key) = char_to_key(ch) {
                        map.insert(note, KeyStroke::plain(key));
                        playable_notes.push(note);
                    }
                }

                // Middle Octave (C4 - B4) -> A S D F G H J
                let middle = [(60, 'a'), (62, 's'), (64, 'd'), (65, 'f'), (67, 'g'), (69, 'h'), (71, 'j')];
                for (note, ch) in middle {
                    if let Some(key) = char_to_key(ch) {
                        map.insert(note, KeyStroke::plain(key));
                        playable_notes.push(note);
                    }
                }

                // Higher Octave (C5 - B5) -> Q W E R T Y U
                let upper = [(72, 'q'), (74, 'w'), (76, 'e'), (77, 'r'), (79, 't'), (81, 'y'), (83, 'u')];
                for (note, ch) in upper {
                    if let Some(key) = char_to_key(ch) {
                        map.insert(note, KeyStroke::plain(key));
                        playable_notes.push(note);
                    }
                }
            }
            KeyboardLayout::Diatonic15 => {
                let keys = [
                    (60, 'q'), (62, 'w'), (64, 'e'), (65, 'r'), (67, 't'),
                    (69, 'y'), (71, 'u'), (72, 'i'), (74, 'o'), (76, 'p'),
                    (77, 'a'), (79, 's'), (81, 'd'), (83, 'f'), (84, 'g'),
                ];
                for (note, ch) in keys {
                    if let Some(key) = char_to_key(ch) {
                        map.insert(note, KeyStroke::plain(key));
                        playable_notes.push(note);
                    }
                }
            }
            KeyboardLayout::Drum8 => {
                let keys = [
                    (36, '1'), (38, '2'), (42, '3'), (46, '4'),
                    (49, '5'), (51, '6'), (50, '7'), (45, '8'),
                ];
                for (note, ch) in keys {
                    if let Some(key) = char_to_key(ch) {
                        map.insert(note, KeyStroke::plain(key));
                        playable_notes.push(note);
                    }
                }
            }
            KeyboardLayout::Chromatic36 => {
                let chars: Vec<char> = "1234567890qwertyuiopasdfghjklzxcvbnm".chars().collect();
                for note in 48u8..=84u8 {
                    let idx = ((note - 48) as usize) % chars.len();
                    if let Some(key) = char_to_key(chars[idx]) {
                        map.insert(note, KeyStroke::plain(key));
                        playable_notes.push(note);
                    }
                }
            }
        }

        Self {
            layout,
            map,
            playable_notes,
            pedal_bindings: [None; 3],
        }
    }

    /// Creates a custom NoteKeyMapping from note pitch to character pairs.
    pub fn from_pairs(layout: KeyboardLayout, pairs: &[(u8, char)]) -> Self {
        let pairs: Vec<_> = pairs.iter().map(|&(note, ch)| (note, ch.to_string())).collect();
        Self::from_key_pairs(layout, &pairs)
    }

    /// Preserve complete config bindings, including ^1, ^A, and named keys.
    pub fn from_key_pairs(layout: KeyboardLayout, pairs: &[(u8, String)]) -> Self {
        let mut map = HashMap::new();
        let mut playable_notes = Vec::new();
        for (note, binding) in pairs {
            if let Some(stroke) = parse_key_stroke(binding) {
                map.insert(*note, stroke);
                playable_notes.push(*note);
            }
        }
        playable_notes.sort_unstable();
        playable_notes.dedup();
        Self {
            layout,
            map,
            playable_notes,
            pedal_bindings: [None; 3],
        }
    }

    /// Pedal order follows MIDI CC64 (sustain), CC66 (sostenuto), CC67 (soft).
    pub fn with_pedal_bindings(mut self, layout: &super::config::KeyboardLayoutJson) -> Self {
        self.pedal_bindings = [
            layout.sustain_key.as_deref(),
            layout.sostenuto_key.as_deref(),
            layout.una_corda_key.as_deref(),
        ].map(|binding| binding.and_then(parse_key_stroke));
        self
    }

    pub fn pedal_bindings(&self) -> &[Option<KeyStroke>; 3] {
        &self.pedal_bindings
    }

    /// Resolves a MIDI note to an Enigo physical Key, if mapped.
    pub fn get_key(&self, note: u8) -> Option<Key> {
        self.get_stroke(note).map(|stroke| stroke.key)
    }

    pub fn get_stroke(&self, note: u8) -> Option<KeyStroke> {
        self.map.get(&note).copied()
    }

    /// Returns the active keyboard layout.
    pub fn layout(&self) -> KeyboardLayout {
        self.layout
    }

    /// Returns a slice of all directly playable note numbers in this layout.
    pub fn playable_notes(&self) -> &[u8] {
        &self.playable_notes
    }

    /// Returns the minimum and maximum playable note pitches in this layout.
    pub fn pitch_range(&self) -> (u8, u8) {
        let min = *self.playable_notes.first().unwrap_or(&48);
        let max = *self.playable_notes.last().unwrap_or(&83);
        (min, max)
    }

    /// Returns all distinct physical keys in this layout (used for panic release).
    pub fn all_keys(&self) -> Vec<Key> {
        let mut keys = HashSet::new();
        for stroke in self.map.values().chain(self.pedal_bindings.iter().flatten()) {
            keys.insert(stroke.key);
            if stroke.shift { keys.insert(Key::Shift); }
            if stroke.control { keys.insert(Key::Control); }
        }
        keys.into_iter().collect()
    }

    /// Converts a Key to a printable char for logging.
    pub fn key_to_char(key: Key) -> char {
        key_to_char(key)
    }
}

pub fn char_to_key(c: char) -> Option<Key> {
    match c {
        'a'..='z' | 'A'..='Z' => {
            let lower = c.to_ascii_lowercase();
            match lower {
                'a' => Some(Key::A),
                'b' => Some(Key::B),
                'c' => Some(Key::C),
                'd' => Some(Key::D),
                'e' => Some(Key::E),
                'f' => Some(Key::F),
                'g' => Some(Key::G),
                'h' => Some(Key::H),
                'i' => Some(Key::I),
                'j' => Some(Key::J),
                'k' => Some(Key::K),
                'l' => Some(Key::L),
                'm' => Some(Key::M),
                'n' => Some(Key::N),
                'o' => Some(Key::O),
                'p' => Some(Key::P),
                'q' => Some(Key::Q),
                'r' => Some(Key::R),
                's' => Some(Key::S),
                't' => Some(Key::T),
                'u' => Some(Key::U),
                'v' => Some(Key::V),
                'w' => Some(Key::W),
                'x' => Some(Key::X),
                'y' => Some(Key::Y),
                'z' => Some(Key::Z),
                _ => None,
            }
        }
        '0' => Some(Key::Num0),
        '1' => Some(Key::Num1),
        '2' => Some(Key::Num2),
        '3' => Some(Key::Num3),
        '4' => Some(Key::Num4),
        '5' => Some(Key::Num5),
        '6' => Some(Key::Num6),
        '7' => Some(Key::Num7),
        '8' => Some(Key::Num8),
        '9' => Some(Key::Num9),
        _ => Some(Key::Unicode(c)),
    }
}

pub fn parse_key_str(s: &str) -> Option<Key> {
    let trimmed = s.trim();
    if trimmed.is_empty() {
        return None;
    }
    let lower = trimmed.to_lowercase();
    match lower.as_str() {
        "space" => Some(Key::Space),
        "enter" | "return" => Some(Key::Return),
        "tab" => Some(Key::Tab),
        "backspace" => Some(Key::Backspace),
        "escape" | "esc" => Some(Key::Escape),
        "shift" => Some(Key::Shift),
        "control" | "ctrl" => Some(Key::Control),
        "alt" => Some(Key::Alt),
        "capslock" | "caps_lock" => Some(Key::CapsLock),
        _ => {
            if trimmed.chars().count() == 1 {
                let c = trimmed.chars().next().unwrap();
                char_to_key(c)
            } else if trimmed.starts_with('^') {
                let rest = &trimmed[1..];
                if rest.chars().count() == 1 {
                    let c = rest.chars().next().unwrap();
                    char_to_key(c)
                } else {
                    parse_key_str(rest)
                }
            } else {
                None
            }
        }
    }
}

pub fn key_to_char(key: Key) -> char {
    match key {
        Key::A => 'a',
        Key::B => 'b',
        Key::C => 'c',
        Key::D => 'd',
        Key::E => 'e',
        Key::F => 'f',
        Key::G => 'g',
        Key::H => 'h',
        Key::I => 'i',
        Key::J => 'j',
        Key::K => 'k',
        Key::L => 'l',
        Key::M => 'm',
        Key::N => 'n',
        Key::O => 'o',
        Key::P => 'p',
        Key::Q => 'q',
        Key::R => 'r',
        Key::S => 's',
        Key::T => 't',
        Key::U => 'u',
        Key::V => 'v',
        Key::W => 'w',
        Key::X => 'x',
        Key::Y => 'y',
        Key::Z => 'z',
        Key::Num0 => '0',
        Key::Num1 => '1',
        Key::Num2 => '2',
        Key::Num3 => '3',
        Key::Num4 => '4',
        Key::Num5 => '5',
        Key::Num6 => '6',
        Key::Num7 => '7',
        Key::Num8 => '8',
        Key::Num9 => '9',
        Key::Unicode(c) => c,
        _ => '?',
    }
}
