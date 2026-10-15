use std::collections::HashMap;
use std::path::Path;
use midly::{Smf, Timing, TrackEventKind, MetaMessage, MidiMessage};
use super::events::{TimedNoteEvent, TimedControlEvent, ControlEventKind};
use super::tempo_map::TempoMap;

/// Standard 128 General MIDI 1 instruments
pub static GENERAL_MIDI_INSTRUMENTS: [&str; 128] = [
    "Acoustic Grand Piano", "Bright Acoustic Piano", "Electric Grand Piano", "Honky-tonk Piano",
    "Electric Piano 1", "Electric Piano 2", "Harpsichord", "Clavi",
    "Celesta", "Glockenspiel", "Music Box", "Vibraphone", "Marimba", "Xylophone", "Tubular Bells", "Dulcimer",
    "Drawbar Organ", "Percussive Organ", "Rock Organ", "Church Organ", "Reed Organ", "Accordion", "Harmonica", "Tango Accordion",
    "Acoustic Guitar (nylon)", "Acoustic Guitar (steel)", "Electric Guitar (jazz)", "Electric Guitar (clean)", "Electric Guitar (muted)", "Overdriven Guitar", "Distortion Guitar", "Guitar harmonics",
    "Acoustic Bass", "Electric Bass (finger)", "Electric Bass (pick)", "Fretless Bass", "Slap Bass 1", "Slap Bass 2", "Synth Bass 1", "Synth Bass 2",
    "Violin", "Viola", "Cello", "Contrabass", "Tremolo Strings", "Pizzicato Strings", "Orchestral Harp", "Timpani",
    "String Ensemble 1", "String Ensemble 2", "SynthStrings 1", "SynthStrings 2", "Choir Aahs", "Voice Oohs", "Synth Voice", "Orchestra Hit",
    "Trumpet", "Trombone", "Tuba", "Muted Trumpet", "French Horn", "Brass Section", "SynthBrass 1", "SynthBrass 2",
    "Soprano Sax", "Alto Sax", "Tenor Sax", "Baritone Sax", "Oboe", "English Horn", "Bassoon", "Clarinet",
    "Piccolo", "Flute", "Recorder", "Pan Flute", "Blown Bottle", "Shakuhachi", "Whistle", "Ocarina",
    "Lead 1 (square)", "Lead 2 (sawtooth)", "Lead 3 (calliope)", "Lead 4 (chiff)", "Lead 5 (charang)", "Lead 6 (voice)", "Lead 7 (fifths)", "Lead 8 (bass + lead)",
    "Pad 1 (new age)", "Pad 2 (warm)", "Pad 3 (polysynth)", "Pad 4 (choir)", "Pad 5 (bowed)", "Pad 6 (metallic)", "Pad 7 (halo)", "Pad 8 (sweep)",
    "FX 1 (rain)", "FX 2 (soundtrack)", "FX 3 (crystal)", "FX 4 (atmosphere)", "FX 5 (brightness)", "FX 6 (goblins)", "FX 7 (echoes)", "FX 8 (sci-fi)",
    "Sitar", "Banjo", "Shamisen", "Koto", "Kalimba", "Bag pipe", "Fiddle", "Shanai",
    "Tinkle Bell", "Agogo", "Steel Drums", "Woodblock", "Taiko Drum", "Melodic Tom", "Synth Drum", "Reverse Cymbal",
    "Guitar Fret Noise", "Breath Noise", "Seashore", "Bird Tweet", "Telephone Ring", "Helicopter", "Applause", "Gunshot"
];

pub fn get_gm_instrument_name(program: u8) -> &'static str {
    let idx = (program as usize) % 128;
    GENERAL_MIDI_INSTRUMENTS[idx]
}

#[derive(Debug, thiserror::Error)]
pub enum MidiError {
    #[error("Failed to read file: {0}")]
    Io(#[from] std::io::Error),
    #[error("Failed to parse MIDI format: {0}")]
    Midly(#[from] midly::Error),
    #[error("Unsupported timing mode (only Metrical / PPQN is supported)")]
    UnsupportedTiming,
}

#[derive(Debug, Clone, serde::Serialize, serde::Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct TrackInfo {
    pub index: usize,
    pub name: String,
    pub instrument_name: String,
    pub program_id: Option<u8>,
    pub channel: u8,
    pub note_count: usize,
    pub min_pitch: Option<u8>,
    pub max_pitch: Option<u8>,
    pub avg_pitch: Option<u8>,
    pub start_ms: f64,
    pub duration_ms: f64,
    pub black_key_ratio: f64,
    pub frequent_notes_ratio: f64,
    pub average_duration_ms: f64,
}

#[derive(Debug, Clone)]
pub struct ParsedMidi {
    pub title: String,
    pub duration_us: u64,
    pub bpm: f64,
    pub ppqn: u16,
    pub tracks: Vec<TrackInfo>,
    pub events: Vec<TimedNoteEvent>,
    pub control_events: Vec<TimedControlEvent>,
    pub tempo_map: TempoMap,
    pub key_signatures: Vec<super::detect::RawKeySignature>,
}

pub fn parse_midi_file<P: AsRef<Path>>(path: P) -> Result<ParsedMidi, MidiError> {
    let bytes = std::fs::read(path)?;
    parse_midi_bytes(&bytes)
}

pub fn parse_midi_bytes(bytes: &[u8]) -> Result<ParsedMidi, MidiError> {
    let smf = Smf::parse(bytes)?;

    let ppqn = match smf.header.timing {
        Timing::Metrical(num) => num.as_int(),
        _ => return Err(MidiError::UnsupportedTiming),
    };

    // First pass: collect all tempo events, track names, instrument names, program changes, and key signatures
    let mut tempo_events: Vec<(u64, u32)> = Vec::new();
    let mut track_names: Vec<String> = Vec::new();
    let mut track_instruments: Vec<String> = Vec::new();
    let mut track_program_changes: HashMap<usize, u8> = HashMap::new();
    let mut channel_program_changes: HashMap<u8, u8> = HashMap::new();
    let mut raw_key_sigs: Vec<(u64, i8, bool)> = Vec::new();

    for (track_idx, track) in smf.tracks.iter().enumerate() {
        let mut tick = 0u64;
        let mut track_name = String::new();
        let mut track_instrument = String::new();

        for event in track {
            tick += event.delta.as_int() as u64;

            match event.kind {
                TrackEventKind::Meta(meta) => match meta {
                    MetaMessage::Tempo(tempo) => {
                        tempo_events.push((tick, tempo.as_int()));
                    }
                    MetaMessage::TrackName(raw_name) => {
                        if let Ok(name_str) = std::str::from_utf8(raw_name) {
                            track_name = name_str.trim().to_string();
                        }
                    }
                    MetaMessage::InstrumentName(raw_inst) => {
                        if let Ok(inst_str) = std::str::from_utf8(raw_inst) {
                            track_instrument = inst_str.trim().to_string();
                        }
                    }
                    MetaMessage::KeySignature(sf, is_minor) => {
                        raw_key_sigs.push((tick, sf, is_minor));
                    }
                    _ => {}
                },
                TrackEventKind::Midi { channel, message } => {
                    if let MidiMessage::ProgramChange { program } = message {
                        let ch = channel.as_int();
                        let prog = program.as_int();
                        track_program_changes.entry(track_idx).or_insert(prog);
                        channel_program_changes.entry(ch).or_insert(prog);
                    }
                }
                _ => {}
            }
        }

        track_names.push(track_name);
        track_instruments.push(track_instrument);
    }

    let tempo_map = TempoMap::new(ppqn, &tempo_events);
    let initial_bpm = tempo_map.initial_bpm();

    // Second pass: extract note/control events and precalculate detailed track statistics
    let mut note_events: Vec<TimedNoteEvent> = Vec::new();
    let mut control_events: Vec<TimedControlEvent> = Vec::new();
    let mut track_infos: Vec<TrackInfo> = Vec::new();

    for (track_idx, track) in smf.tracks.iter().enumerate() {
        let mut tick = 0u64;
        let mut note_count = 0usize;
        let mut default_channel = 0u8;

        // Collect track notes with start and end times for statistics: (pitch, start_us, end_us)
        let mut track_notes: Vec<(u8, u64, u64)> = Vec::new();
        let mut active_notes: HashMap<(u8, u8), u64> = HashMap::new(); // (channel, pitch) -> start_us

        for event in track {
            tick += event.delta.as_int() as u64;

            if let TrackEventKind::Midi { channel, message } = event.kind {
                let ch = channel.as_int();
                default_channel = ch;

                match message {
                    MidiMessage::NoteOn { key, vel } => {
                        let pitch = key.as_int();
                        let velocity = vel.as_int();
                        let is_on = velocity > 0;
                        let time_us = tempo_map.tick_to_us(tick);

                        if is_on {
                            note_count += 1;
                            if let Some(prev_start) = active_notes.insert((ch, pitch), time_us) {
                                track_notes.push((pitch, prev_start, time_us));
                            }
                        } else if let Some(prev_start) = active_notes.remove(&(ch, pitch)) {
                            track_notes.push((pitch, prev_start, time_us));
                        }

                        note_events.push(TimedNoteEvent {
                            time_us,
                            note: pitch,
                            velocity,
                            is_note_on: is_on,
                            channel: ch,
                            track_index: track_idx,
                        });
                    }
                    MidiMessage::NoteOff { key, vel } => {
                        let pitch = key.as_int();
                        let velocity = vel.as_int();
                        let time_us = tempo_map.tick_to_us(tick);

                        if let Some(prev_start) = active_notes.remove(&(ch, pitch)) {
                            track_notes.push((pitch, prev_start, time_us));
                        }

                        note_events.push(TimedNoteEvent {
                            time_us,
                            note: pitch,
                            velocity,
                            is_note_on: false,
                            channel: ch,
                            track_index: track_idx,
                        });
                    }
                    MidiMessage::Controller { controller, value } => {
                        let time_us = tempo_map.tick_to_us(tick);
                        control_events.push(TimedControlEvent {
                            time_us,
                            channel: ch,
                            kind: ControlEventKind::ControlChange {
                                controller: controller.as_int(),
                                value: value.as_int(),
                            },
                        });
                    }
                    MidiMessage::ProgramChange { program } => {
                        let time_us = tempo_map.tick_to_us(tick);
                        control_events.push(TimedControlEvent {
                            time_us,
                            channel: ch,
                            kind: ControlEventKind::ProgramChange {
                                program: program.as_int(),
                            },
                        });
                    }
                    _ => {}
                }
            }
        }

        // Close any remaining dangling note-ons
        let track_end_us = tempo_map.tick_to_us(tick);
        for ((_ch, pitch), start_us) in active_notes {
            let end_us = track_end_us.max(start_us + 200_000);
            track_notes.push((pitch, start_us, end_us));
        }

        // Calculate detailed statistics
        let (
            min_pitch,
            max_pitch,
            avg_pitch,
            start_ms,
            duration_ms,
            black_key_ratio,
            frequent_notes_ratio,
            average_duration_ms,
        ) = if track_notes.is_empty() {
            (None, None, None, 0.0, 0.0, 0.0, 0.0, 0.0)
        } else {
            let min_p = track_notes.iter().map(|n| n.0).min();
            let max_p = track_notes.iter().map(|n| n.0).max();
            let avg_p = Some((track_notes.iter().map(|n| n.0 as u32).sum::<u32>() / track_notes.len() as u32) as u8);

            let min_start_us = track_notes.iter().map(|n| n.1).min().unwrap_or(0);
            let max_end_us = track_notes.iter().map(|n| n.2).max().unwrap_or(min_start_us);
            let s_ms = (min_start_us as f64) / 1000.0;
            let d_ms = ((max_end_us.saturating_sub(min_start_us)) as f64) / 1000.0;

            let black_keys = track_notes.iter().filter(|n| matches!(n.0 % 12, 1 | 3 | 6 | 8 | 10)).count();
            let bk_ratio = (black_keys as f64 / track_notes.len() as f64) * 100.0;

            let total_dur_us: u64 = track_notes.iter().map(|n| n.2.saturating_sub(n.1)).sum();
            let avg_dur_ms = (total_dur_us as f64 / track_notes.len() as f64) / 1000.0;

            let mut pitch_counts = HashMap::new();
            for n in &track_notes {
                *pitch_counts.entry(n.0).or_insert(0usize) += 1;
            }
            let avg_occ = (track_notes.len() as f64) / (pitch_counts.len() as f64);
            let freq_count: usize = pitch_counts.values().filter(|&&c| (c as f64) > avg_occ).sum();
            let freq_ratio = (freq_count as f64 / track_notes.len() as f64) * 100.0;

            (min_p, max_p, avg_p, s_ms, d_ms, bk_ratio, freq_ratio, avg_dur_ms)
        };

        // Determine Instrument Name
        let program_id = track_program_changes.get(&track_idx).copied();
        let instrument_name = if default_channel == 9 {
            "Standard Drum Kit".to_string()
        } else if let Some(prog) = program_id {
            get_gm_instrument_name(prog).to_string()
        } else if let Some(prog) = channel_program_changes.get(&default_channel) {
            get_gm_instrument_name(*prog).to_string()
        } else if !track_instruments[track_idx].is_empty() {
            track_instruments[track_idx].clone()
        } else if !track_names[track_idx].is_empty() && !track_names[track_idx].to_lowercase().starts_with("track") {
            track_names[track_idx].clone()
        } else {
            format!("Ch {}", default_channel + 1)
        };

        let name = if !track_names[track_idx].is_empty() {
            track_names[track_idx].clone()
        } else {
            format!("Track {}", track_idx + 1)
        };

        track_infos.push(TrackInfo {
            index: track_idx,
            name,
            instrument_name,
            program_id,
            channel: default_channel,
            note_count,
            min_pitch,
            max_pitch,
            avg_pitch,
            start_ms,
            duration_ms,
            black_key_ratio,
            frequent_notes_ratio,
            average_duration_ms,
        });
    }

    // Sort all events chronologically
    note_events.sort();
    control_events.sort();

    let duration_us = note_events.last().map(|e| e.time_us).unwrap_or(0);
    let title = track_names.into_iter().find(|n| !n.is_empty()).unwrap_or_else(|| "Untitled MIDI".to_string());

    // Convert raw key signatures to metric timestamps and normalized labels
    let mut key_signatures = Vec::new();
    for (tick, sf, is_minor) in raw_key_sigs {
        let time_us = tempo_map.tick_to_us(tick);
        let time_ms = time_us / 1000;
        let (key_offset, label) = super::detect::calculate_key_signature_offset(sf, is_minor);
        key_signatures.push(super::detect::RawKeySignature {
            tick,
            time_us,
            time_ms,
            sf,
            is_minor,
            key_offset,
            label,
        });
    }
    key_signatures.sort_by_key(|k| k.time_us);

    Ok(ParsedMidi {
        title,
        duration_us,
        bpm: initial_bpm,
        ppqn,
        tracks: track_infos,
        events: note_events,
        control_events,
        tempo_map,
        key_signatures,
    })
}
