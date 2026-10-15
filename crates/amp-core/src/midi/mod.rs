pub mod events;
pub mod tempo_map;
pub mod parser;
pub mod transpose;
pub mod detect;

pub use events::{TimedNoteEvent, TimedControlEvent, ControlEventKind};
pub use tempo_map::TempoMap;
pub use parser::{ParsedMidi, TrackInfo, parse_midi_file, parse_midi_bytes, MidiError, get_gm_instrument_name, GENERAL_MIDI_INSTRUMENTS};
pub use transpose::{TransposeMode, detect_best_scale, build_scale_pitch_classes, process_note_pitch};
pub use detect::{KeyChangeMarker, RawKeySignature, detect_key_changes, normalize_detected_key_offset, get_note_name, calculate_key_signature_offset};
