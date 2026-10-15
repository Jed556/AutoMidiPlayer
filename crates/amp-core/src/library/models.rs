use serde::{Deserialize, Serialize};

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
    pub detect_chord_pads: Option<bool>,
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

fn default_true() -> bool {
    true
}

fn default_speed() -> f64 {
    1.0
}

fn default_transpose_smart() -> String {
    "Smart".to_string()
}

fn default_tolerance() -> u32 {
    25
}

/// Song defaults configuration applied when newly adding / importing songs
#[derive(Debug, Clone, Serialize, Deserialize, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct SongDefaults {
    #[serde(default)]
    pub default_base_key_offset: i32,
    #[serde(default)]
    pub default_key_offset: i32,
    #[serde(default = "default_true")]
    pub auto_detect_base_key: bool,
    #[serde(default = "default_transpose_smart")]
    pub default_transpose: String,
    #[serde(default = "default_speed")]
    pub default_speed: f64,
    #[serde(default = "default_true")]
    pub default_merge_notes: bool,
    #[serde(default = "default_tolerance")]
    pub default_merge_tolerance_ms: u32,
    #[serde(default)]
    pub default_hold_notes: bool,
    #[serde(default)]
    pub default_tap_threshold_ms: u32,
}

impl Default for SongDefaults {
    fn default() -> Self {
        Self {
            default_base_key_offset: 0,
            default_key_offset: 0,
            auto_detect_base_key: true,
            default_transpose: "Smart".to_string(),
            default_speed: 1.0,
            default_merge_notes: true,
            default_merge_tolerance_ms: 25,
            default_hold_notes: false,
            default_tap_threshold_ms: 0,
        }
    }
}

impl SongDefaults {
    pub fn transpose_mode_int(&self) -> i32 {
        match self.default_transpose.trim().to_lowercase().as_str() {
            "ignore" => 0,
            "smart" => 1,
            "up" => 2,
            "down" => 3,
            _ => 1,
        }
    }
}

