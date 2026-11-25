pub mod midi;
pub mod diagnostics;
pub mod input;
pub mod player;
pub mod audio;
pub mod library;
pub mod paths;
pub mod legacy_migration;
pub mod settings;

pub use midi::{
    build_scale_pitch_classes, detect_best_scale, detect_key_changes, get_gm_instrument_name,
    is_guitar_pro_path, is_supported_song_extension, is_supported_song_path, parse_midi_bytes,
    parse_midi_file, process_note_pitch, ControlEventKind, GuitarProBrush, GuitarProChordMark,
    GuitarProMeta, KeyChangeMarker, MidiError, ParsedMidi, RawKeySignature, TempoMap,
    TimedControlEvent, TimedNoteEvent, TrackInfo, TransposeMode, GENERAL_MIDI_INSTRUMENTS,
    GUITAR_PRO_EXTENSIONS, SUPPORTED_SONG_EXTENSIONS,
};
pub use input::{
    char_to_key, parse_key_str, parse_key_stroke, parse_wheel_binding, GuitarTimbre, GuitarTimbreMode, KeyStroke, scan_games, scan_keyboard_layouts, ChordConfigJson, FretLayoutJson, GameConfig, GuitarConfigJson,
    InstrumentConfigJson, KeySimulator, KeyboardLayout, KeyboardLayoutJson, LoadedGameBundle,
    NoteKeyMapping, StrumButtonsJson, StrumDirection, WheelBinding,
};
pub use player::{PlaybackEngine, PlaybackStatus, PlayerEvent, ActiveTrackNote, note_durations, OverlapBehavior};
pub use audio::{AudioDevice, AudioSynth, SynthMode, AudioError};
pub use library::{
    AlbumRecord, DirectoryWatcher, ExtractedMetadata, LibraryDatabase, PlaylistRecord,
    ScanSummary, SongDefaults, SongMetadataUpdate, SongRecord, SongSettingsUpdate, compute_file_hash,
    extract_metadata, import_files, sanitize_tag, scan_folder,
};
pub use rustysynth::SoundFont;
pub use enigo::Key as OutputKey;
