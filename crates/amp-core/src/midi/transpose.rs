use std::collections::HashSet;
use crate::midi::events::TimedNoteEvent;

/// The 7 Greek musical modes defined by semitone intervals from the tonic.
pub const SMART_MODES: [&[u8]; 7] = [
    &[0, 2, 4, 5, 7, 9, 11], // 0: Ionian (Major)
    &[0, 2, 3, 5, 7, 9, 10], // 1: Dorian
    &[0, 1, 3, 5, 7, 8, 10], // 2: Phrygian
    &[0, 2, 4, 6, 7, 9, 11], // 3: Lydian
    &[0, 2, 4, 5, 7, 9, 10], // 4: Mixolydian
    &[0, 2, 3, 5, 7, 8, 10], // 5: Aeolian (Natural Minor)
    &[0, 1, 3, 5, 6, 8, 10], // 6: Locrian
];

pub const MODE_NAMES: [&str; 7] = [
    "Ionian (Major)",
    "Dorian",
    "Phrygian",
    "Lydian",
    "Mixolydian",
    "Aeolian (Minor)",
    "Locrian",
];

#[derive(Debug, Clone, Copy, PartialEq, Eq, serde::Serialize, serde::Deserialize)]
pub enum TransposeMode {
    /// Notes out of range or unmapped are ignored
    Ignore,
    /// Fold notes by octaves upwards into range
    Up,
    /// Fold notes by octaves downwards into range
    Down,
    /// Smart mode: fit notes to the instrument's scale and fold by octaves
    Smart,
}

impl Default for TransposeMode {
    fn default() -> Self {
        crate::settings::defaults::value("/defaultTranspose")
    }
}

impl std::str::FromStr for TransposeMode {
    type Err = ();

    fn from_str(s: &str) -> Result<Self, Self::Err> {
        match s.to_lowercase().as_str() {
            "ignore" => Ok(Self::Ignore),
            "up" => Ok(Self::Up),
            "down" => Ok(Self::Down),
            "smart" => Ok(Self::Smart),
            _ => Ok(Self::default()),
        }
    }
}

/// Detects the best matching musical scale (tonic + mode index) from note events using pitch-class histogram.
pub fn detect_best_scale(notes: &[TimedNoteEvent], center_note: u8) -> (u8, usize) {
    detect_scale_from_pitches(notes.iter().map(|note| note.note), center_note)
}

fn detect_scale_from_pitches(notes: impl Iterator<Item = u8>, center_note: u8) -> (u8, usize) {
    let mut histogram = [0.0f64; 12];
    let center = center_note as f64;

    for note in notes {
        let pc = (note % 12) as usize;
        let weight = 1.0 / (1.0 + (note as f64 - center).abs());
        histogram[pc] += weight;
    }

    let mut best_score = f64::NEG_INFINITY;
    let mut best_tonic = 0u8;
    let mut best_mode = 0usize;

    for tonic in 0u8..12 {
        for (mode_idx, intervals) in SMART_MODES.iter().enumerate() {
            let mut score = 0.0f64;
            for &interval in *intervals {
                let pc = ((tonic + interval) % 12) as usize;
                score += histogram[pc];
            }

            if score > best_score {
                best_score = score;
                best_tonic = tonic;
                best_mode = mode_idx;
            }
        }
    }

    (best_tonic, best_mode)
}

/// Builds a set of all pitch classes (0..=11) in the scale defined by tonic and mode.
pub fn build_scale_pitch_classes(tonic: u8, mode_index: usize) -> HashSet<u8> {
    let mode_idx = mode_index.min(SMART_MODES.len() - 1);
    let mut set = HashSet::with_capacity(7);
    for &interval in SMART_MODES[mode_idx] {
        set.insert((tonic + interval) % 12);
    }
    set
}

/// Folds a note into [min_note, max_note] by adding or subtracting octaves (12 semitones).
pub fn fold_note_by_octaves(note: i32, min_note: i32, max_note: i32) -> i32 {
    let mut folded = note;
    while folded < min_note {
        folded += 12;
    }
    while folded > max_note {
        folded -= 12;
    }

    if folded >= min_note && folded <= max_note {
        folded
    } else if (folded - min_note).abs() <= (folded - max_note).abs() {
        min_note
    } else {
        max_note
    }
}

/// Transposes and maps a raw note pitch according to semitone offset, transposition mode, and instrument range.
/// Returns the adapted note number, or None if the note should be ignored.
pub fn process_note_pitch(
    raw_pitch: u8,
    semitone_offset: i8,
    mode: TransposeMode,
    min_playable: u8,
    max_playable: u8,
    playable_notes: &[u8],
) -> Option<u8> {
    if playable_notes.is_empty() {
        return None;
    }
    let shifted = (raw_pitch as i32) + (semitone_offset as i32);
    let min_p = min_playable as i32;
    let max_p = max_playable as i32;

    match mode {
        TransposeMode::Ignore => {
            if shifted >= min_p && shifted <= max_p && playable_notes.contains(&(shifted as u8)) {
                Some(shifted as u8)
            } else {
                None
            }
        }
        TransposeMode::Up => {
            let folded = fold_note_by_octaves(shifted, min_p, max_p) as u8;
            if playable_notes.contains(&folded) {
                Some(folded)
            } else {
                None
            }
        }
        TransposeMode::Down => {
            let folded = fold_note_by_octaves(shifted, min_p, max_p) as u8;
            if playable_notes.contains(&folded) {
                Some(folded)
            } else {
                None
            }
        }
        TransposeMode::Smart => {
            // First fold by octaves into playable pitch boundaries
            let folded = fold_note_by_octaves(shifted, min_p, max_p) as u8;

            // If the folded note is directly playable, return it
            if playable_notes.contains(&folded) {
                return Some(folded);
            }

            // Match legacy Smart: infer the instrument's scale around this folded
            // pitch, independently of the song's key and untransposed notes.
            let (tonic, mode_index) = detect_scale_from_pitches(playable_notes.iter().copied(), folded);
            let scale_pitch_classes = build_scale_pitch_classes(tonic, mode_index);
            let candidates: Vec<u8> = playable_notes
                .iter()
                .copied()
                .filter(|n| scale_pitch_classes.contains(&(n % 12)))
                .collect();

            let pool = if !candidates.is_empty() {
                &candidates
            } else {
                playable_notes
            };

            let mut best = pool[0];
            let mut best_score = f64::INFINITY;

            for &candidate in pool {
                let target_dist = (candidate as f64 - folded as f64).abs();
                let octave_dist = ((candidate / 12) as f64 - (folded / 12) as f64).abs();
                let score = target_dist + (octave_dist * 0.15);

                if score < best_score || ((score - best_score).abs() < 0.000001
                    && target_dist < (best as f64 - folded as f64).abs()) {
                    best_score = score;
                    best = candidate;
                }
            }

            Some(best)
        }
    }
}
