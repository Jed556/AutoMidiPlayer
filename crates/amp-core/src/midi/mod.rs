pub mod events;
pub mod guitar_pro;
pub mod tempo_map;
pub mod parser;
pub mod transpose;
pub mod detect;

pub use events::{TimedNoteEvent, TimedControlEvent, ControlEventKind};
pub use guitar_pro::{GuitarProBrush, GuitarProChordMark, GuitarProMeta};
pub use tempo_map::TempoMap;
pub use parser::{
    get_gm_instrument_name, is_guitar_pro_path, is_supported_song_extension,
    is_supported_song_path, parse_midi_bytes, parse_midi_file, MidiError, ParsedMidi, TrackInfo,
    GENERAL_MIDI_INSTRUMENTS, GUITAR_PRO_EXTENSIONS, SUPPORTED_SONG_EXTENSIONS,
};
pub use transpose::{TransposeMode, detect_best_scale, build_scale_pitch_classes, process_note_pitch};
pub use detect::{KeyChangeMarker, RawKeySignature, detect_key_changes, normalize_detected_key_offset, get_note_name, calculate_key_signature_offset};
