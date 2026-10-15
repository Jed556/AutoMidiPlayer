use serde::{Deserialize, Serialize};
use super::parser::ParsedMidi;
use super::transpose::TransposeMode;

pub static NOTE_NAMES: [&str; 12] = [
    "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B",
];

pub static MAJOR_KEY_PROFILE: [f64; 12] = [
    6.35, 2.23, 3.48, 2.33, 4.38, 4.09, 2.52, 5.19, 2.39, 3.66, 2.29, 2.88,
];

pub static MINOR_KEY_PROFILE: [f64; 12] = [
    6.33, 2.68, 3.52, 5.38, 2.60, 3.53, 2.54, 4.75, 3.98, 2.69, 3.34, 3.17,
];

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct KeyChangeMarker {
    #[serde(rename = "timeMs")]
    pub time_ms: u64,
    #[serde(rename = "keyOffset")]
    pub key_offset: i8,
    #[serde(default)]
    pub transpose: Option<TransposeMode>,
    #[serde(default)]
    pub label: String,
}

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq)]
pub struct RawKeySignature {
    pub tick: u64,
    pub time_us: u64,
    pub time_ms: u64,
    pub sf: i8,
    pub is_minor: bool,
    pub key_offset: i8,
    pub label: String,
}

pub fn normalize_detected_key_offset(pitch_class: i32) -> i8 {
    let tonic = pitch_class.rem_euclid(12);
    let transposition_to_c = (-tonic).rem_euclid(12);
    if transposition_to_c >= 6 {
        (transposition_to_c - 12) as i8
    } else {
        transposition_to_c as i8
    }
}

pub fn get_note_name(key_offset: i8) -> &'static str {
    let pitch_class = (key_offset).rem_euclid(12) as usize;
    NOTE_NAMES[pitch_class]
}

pub fn calculate_key_signature_offset(sf: i8, is_minor: bool) -> (i8, String) {
    let base = if is_minor { 9 } else { 0 };
    let tonic_pitch_class = (base + (sf as i32 * 7)).rem_euclid(12);
    let key_offset = normalize_detected_key_offset(tonic_pitch_class);
    let note_name = NOTE_NAMES[tonic_pitch_class as usize];
    let mode_str = if is_minor { "min" } else { "maj" };
    let label = format!("{} {}", note_name, mode_str);
    (key_offset, label)
}

/// Detect key changes using hybrid approach:
/// 1. Parse standard MIDI KeySignature meta-events if present.
/// 2. If absent, fallback to pitch-class histogram analysis.
pub fn detect_key_changes(parsed: &ParsedMidi) -> Vec<KeyChangeMarker> {
    if !parsed.key_signatures.is_empty() {
        let mut result = Vec::new();
        let mut last_offset: Option<i8> = None;

        for ks in &parsed.key_signatures {
            if let Some(prev) = last_offset {
                if prev == ks.key_offset {
                    continue;
                }
            }
            result.push(KeyChangeMarker {
                time_ms: ks.time_ms,
                key_offset: ks.key_offset,
                transpose: None,
                label: ks.label.clone(),
            });
            last_offset = Some(ks.key_offset);
        }

        if !result.is_empty() {
            return result;
        }
    }

    // Fallback: Pitch-class profile analysis (Krumhansl-Schmuckler)
    detect_from_pitch_profile(parsed)
}

fn detect_from_pitch_profile(parsed: &ParsedMidi) -> Vec<KeyChangeMarker> {
    if parsed.events.is_empty() {
        return Vec::new();
    }

    let duration_ms = parsed.duration_us / 1000;
    // For pieces longer than 20 seconds, divide into temporal windows (e.g. 15-second windows)
    let window_duration_ms = 15_000u64;

    if duration_ms <= window_duration_ms {
        // Single piece analysis
        if let Some((offset, label)) = analyze_window_pitch_class(&parsed.events, 0, parsed.duration_us) {
            return vec![KeyChangeMarker {
                time_ms: 0,
                key_offset: offset,
                transpose: None,
                label,
            }];
        }
        return Vec::new();
    }

    let mut result = Vec::new();
    let mut last_offset: Option<i8> = None;
    let mut current_window_start_ms = 0u64;

    while current_window_start_ms < duration_ms {
        let window_end_ms = (current_window_start_ms + window_duration_ms).min(duration_ms);
        let start_us = current_window_start_ms * 1000;
        let end_us = window_end_ms * 1000;

        if let Some((offset, label)) = analyze_window_pitch_class(&parsed.events, start_us, end_us) {
            if last_offset.map_or(true, |prev| prev != offset) {
                result.push(KeyChangeMarker {
                    time_ms: current_window_start_ms,
                    key_offset: offset,
                    transpose: None,
                    label,
                });
                last_offset = Some(offset);
            }
        }

        current_window_start_ms += window_duration_ms;
    }

    result
}

fn analyze_window_pitch_class(
    events: &[super::events::TimedNoteEvent],
    start_us: u64,
    end_us: u64,
) -> Option<(i8, String)> {
    let mut histogram = [0.0f64; 12];
    let mut count = 0usize;

    for ev in events {
        if ev.time_us >= start_us && ev.time_us < end_us && ev.is_note_on && ev.velocity > 0 {
            let pc = (ev.note % 12) as usize;
            histogram[pc] += ev.velocity as f64;
            count += 1;
        }
    }

    if count < 5 {
        return None;
    }

    let mut best_score = f64::NEG_INFINITY;
    let mut best_tonic = 0usize;
    let mut is_minor_detected = false;

    for tonic in 0..12 {
        let mut major_score = 0.0;
        let mut minor_score = 0.0;

        for interval in 0..12 {
            let weight = histogram[(tonic + interval) % 12];
            major_score += weight * MAJOR_KEY_PROFILE[interval];
            minor_score += weight * MINOR_KEY_PROFILE[interval];
        }

        if major_score > best_score {
            best_score = major_score;
            best_tonic = tonic;
            is_minor_detected = false;
        }

        if minor_score > best_score {
            best_score = minor_score;
            best_tonic = tonic;
            is_minor_detected = true;
        }
    }

    let key_offset = normalize_detected_key_offset(best_tonic as i32);
    let note_name = NOTE_NAMES[best_tonic];
    let mode_str = if is_minor_detected { "min" } else { "maj" };
    let label = format!("{} {}", note_name, mode_str);

    Some((key_offset, label))
}
