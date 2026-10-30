use serde::{Deserialize, Serialize};
use std::str::FromStr;

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct SongSettingsContext {
    pub game_id: String,
    pub instrument_id: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(tag = "kind", rename_all = "camelCase")]
pub enum SongSettingsScope {
    Global,
    Game { #[serde(rename = "gameId")] game_id: String },
    Instrument {
        #[serde(rename = "gameId")] game_id: String,
        #[serde(rename = "instrumentId")] instrument_id: String,
    },
}

impl SongSettingsScope {
    pub fn validate(&self) -> Result<(), String> {
        match self {
            Self::Global => Ok(()),
            Self::Game { game_id } if !game_id.trim().is_empty() => Ok(()),
            Self::Instrument { game_id, instrument_id }
                if !game_id.trim().is_empty() && !instrument_id.trim().is_empty() => Ok(()),
            _ => Err("A game and instrument are required for scoped song settings".into()),
        }
    }
}

/// Song record representation mirroring AutoMidiPlayer.db 'Songs' table
#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct SongRecord {
    pub id: String,
    pub base_key: Option<i32>,
    pub key: i32,
    pub path: String,
    pub file_hash: Option<String>,
    pub title: Option<String>,
    pub artist: Option<String>,
    pub album: Option<String>,
    pub album_id: Option<String>,
    pub date_added: Option<String>,
    pub transpose: i32,
    pub speed: Option<f64>,
    pub bpm: Option<f64>,
    pub disabled_tracks: Option<String>,
    pub merge_notes: Option<bool>,
    pub merge_milliseconds: Option<u32>,
    pub hold_notes: Option<bool>,
    #[serde(default)]
    pub tap_threshold_ms: Option<u32>,
    pub image_path: Option<String>,
    pub cached_duration_ms: Option<i64>,
    pub cached_native_bpm: Option<f64>,
    pub auto_chord: Option<bool>,
    pub chord_detection_milliseconds: Option<u32>,
    pub key_changes: Option<String>,
    pub enable_key_change_markers: Option<bool>,
    pub is_missing: bool,
}

/// Album record representation for future-proofing song album categorization
#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct AlbumRecord {
    pub id: String,
    pub title: String,
    pub artist: Option<String>,
    pub cover_art_path: Option<String>,
    pub release_year: Option<i32>,
    pub date_added: String,
}

/// Playlist record representation matching Svelte PlaylistInfo & SQLite Playlists table
#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct PlaylistRecord {
    pub id: String,
    pub title: String,
    pub subtitle: Option<String>,
    pub icon: Option<String>,
    pub gradient: Option<String>,
    pub banner_color: Option<String>,
    pub game_id: Option<String>,
    pub date_created: String,
    pub song_count: usize,
}

/// Settings payload for debounced auto-persist from player/studio controls
#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct SongSettingsUpdate {
    pub key: i32,
    pub base_key: Option<i32>,
    pub transpose: i32,
    pub disabled_tracks: Option<String>,
    pub key_changes: Option<String>,
    #[serde(default)]
    pub speed: Option<f64>,
    #[serde(default)]
    pub hold_notes: Option<bool>,
    #[serde(default)]
    pub tap_threshold_ms: Option<u32>,
    #[serde(default)]
    pub auto_chord: Option<bool>,
    #[serde(default)]
    pub chord_detection_milliseconds: Option<u32>,
    #[serde(default)]
    pub enable_key_change_markers: Option<bool>,
}

/// Metadata payload for explicit Save button from EditDialog.svelte
#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct SongMetadataUpdate {
    pub title: Option<String>,
    pub artist: Option<String>,
    pub album: Option<String>,
    pub album_id: Option<String>,
    pub bpm: Option<f64>,
}

/// All fields edited together by the song details dialog.
#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct SongDetailsUpdate {
    pub title: String,
    pub artist: String,
    pub album: String,
    pub base_key: Option<i32>,
    pub key: i32,
    pub transpose: i32,
    pub bpm: Option<f64>,
    pub speed: f64,
    pub merge_notes: bool,
    pub merge_milliseconds: u32,
    pub hold_notes: bool,
    #[serde(default)]
    pub tap_threshold_ms: u32,
    pub date_added: String,
}

/// Song defaults configuration applied when newly adding / importing songs
#[derive(Debug, Clone, Serialize, Deserialize, PartialEq)]
#[serde(default, rename_all = "camelCase")]
pub struct SongDefaults {
    pub default_base_key_offset: i32,
    pub default_key_offset: i32,
    pub auto_detect_base_key: bool,
    pub default_transpose: String,
    pub default_speed: f64,
    pub default_merge_notes: bool,
    pub default_merge_tolerance_ms: u32,
    pub default_hold_notes: bool,
    pub default_tap_threshold_ms: u32,
    pub default_auto_chord: bool,
    pub default_chord_detection_milliseconds: u32,
    pub default_enable_key_change_markers: bool,
}

impl Default for SongDefaults {
    fn default() -> Self {
        Self {
            default_base_key_offset: crate::settings::defaults::value("/defaultBaseKeyOffset"),
            default_key_offset: crate::settings::defaults::value("/defaultKeyOffset"),
            auto_detect_base_key: crate::settings::defaults::value("/autoDetectBaseKey"),
            default_transpose: crate::settings::defaults::value("/defaultTranspose"),
            default_speed: crate::settings::defaults::value("/defaultSpeed"),
            default_merge_notes: crate::settings::defaults::value("/defaultMergeNotes"),
            default_merge_tolerance_ms: crate::settings::defaults::value("/defaultMergeToleranceMs"),
            default_hold_notes: crate::settings::defaults::value("/defaultHoldNotes"),
            default_tap_threshold_ms: crate::settings::defaults::value("/defaultTapThresholdMs"),
            default_auto_chord: crate::settings::defaults::value("/defaultAutoChord"),
            default_chord_detection_milliseconds: crate::settings::defaults::value("/defaultChordDetectionMilliseconds"),
            default_enable_key_change_markers: crate::settings::defaults::value("/defaultEnableKeyChangeMarkers"),
        }
    }
}

impl SongDefaults {
    pub fn transpose_mode_int(&self) -> i32 {
        use crate::midi::TransposeMode;
        match TransposeMode::from_str(&self.default_transpose).unwrap_or_default() {
            TransposeMode::Ignore => 0,
            TransposeMode::Smart => 1,
            TransposeMode::Up => 2,
            TransposeMode::Down => 3,
        }
    }
}
