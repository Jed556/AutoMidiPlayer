pub mod midi;
pub mod input;
pub mod player;
pub mod audio;
pub mod library;
pub mod paths;
pub mod settings;

pub use midi::{ParsedMidi, TrackInfo, TimedNoteEvent, TimedControlEvent, ControlEventKind, TempoMap, TransposeMode, detect_best_scale, build_scale_pitch_classes, process_note_pitch, parse_midi_file, parse_midi_bytes, MidiError, get_gm_instrument_name, GENERAL_MIDI_INSTRUMENTS, KeyChangeMarker, RawKeySignature, detect_key_changes};
pub use input::{
    char_to_key, parse_key_str, parse_key_stroke, KeyStroke, scan_games, scan_keyboard_layouts, ChordPadConfigJson, GameConfig,
    InstrumentConfigJson, KeySimulator, KeyboardLayout, KeyboardLayoutJson, LoadedGameBundle,
    NoteKeyMapping,
};
pub use player::{PlaybackEngine, PlaybackStatus, PlayerEvent, ActiveTrackNote, note_durations, OverlapBehavior};
pub use audio::{AudioDevice, AudioSynth, SynthMode, AudioError};
pub use library::{
    AlbumRecord, DirectoryWatcher, ExtractedMetadata, LibraryDatabase, PlaylistRecord,
    ScanSummary, SongDefaults, SongMetadataUpdate, SongRecord, SongSettingsUpdate, compute_file_hash,
    extract_metadata, import_files, sanitize_tag, scan_folder,
};
pub use rustysynth::SoundFont;
