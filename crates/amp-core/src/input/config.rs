use serde::{Deserialize, Serialize};
use std::collections::HashMap;
use std::fs;
use std::path::Path;
use walkdir::WalkDir;

/// Metadata for a game definition stored in `Games/<GameName>/game.json`
#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct GameConfig {
    pub id: String,
    pub display_name: String,
    #[serde(default)]
    pub short_name: Option<String>,
    #[serde(default)]
    pub process_names: Vec<String>,
    #[serde(default)]
    pub window_names: Vec<String>,
    #[serde(default)]
    pub icon: Option<String>,
    #[serde(default)]
    pub default_instrument: Option<String>,
    #[serde(default)]
    pub default_layout: Option<String>,
}

/// Pre-composed chord shared by pad, wheel, and guitar presentations.
#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ChordConfigJson {
    pub name: String,
    pub pitch_classes: Vec<u8>,
    #[serde(default)]
    pub bass_pitch_class: Option<u8>,
    /// String 1 first. A null cell is unknown; a muted string is explicitly labelled.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub string_pitches: Option<Vec<Option<StringPitch>>>,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub enum StringPitch {
    #[serde(rename = "muted")]
    Muted,
    #[serde(untagged)]
    Pitch(u8),
}

/// Guitar pitch data belongs to the instrument, not its physical controls.
#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct GuitarConfigJson {
    /// Open-string MIDI pitches, ordered from highest to lowest string.
    pub tuning: Vec<u8>,
    pub fret_count: u8,
}

impl GuitarConfigJson {
    pub fn is_valid(&self) -> bool {
        !self.tuning.is_empty() && self.tuning.len() <= 6 && self.fret_count > 0
            && self.tuning.iter().all(|pitch| u16::from(*pitch) + u16::from(self.fret_count) <= 127)
    }
}

/// Strum up/down bindings accept mouse buttons (`"mouseLeft"` / `"mouseRight"`) or keyboard keys.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct StrumButtonsJson {
    pub up: String,
    pub down: String,
}

impl Default for StrumButtonsJson {
    fn default() -> Self {
        Self { up: "mouseLeft".into(), down: "mouseRight".into() }
    }
}

impl StrumButtonsJson {
    pub fn is_valid(&self) -> bool {
        super::chord_wheel::parse_wheel_binding(&self.up).is_some()
            && super::chord_wheel::parse_wheel_binding(&self.down).is_some()
    }

    pub fn binding_for(&self, direction: super::chord_wheel::StrumDirection) -> Option<super::chord_wheel::WheelBinding> {
        let raw = match direction {
            super::chord_wheel::StrumDirection::Up => &self.up,
            super::chord_wheel::StrumDirection::Down => &self.down,
        };
        super::chord_wheel::parse_wheel_binding(raw)
    }
}

/// Fret bindings reuse an existing wheel layout's calibration.
#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct FretLayoutJson {
    pub string_keys: Vec<String>,
    pub wheel_layout: String,
    /// Chords <-> Frets toggle key. Omit to inherit the linked wheel's mode key.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub mode_key: Option<String>,
    /// Previous and next page keys. Omit to inherit the linked wheel's keys.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub page_keys: Option<[String; 2]>,
    /// Up and down strum mouse/key bindings. Omit to inherit the linked wheel's bindings.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub strum_buttons: Option<StrumButtonsJson>,
    /// Timbre toggle key (e.g. Clean <-> Distortion). Omit to inherit the linked wheel's key.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub timbre_key: Option<String>,
}

impl FretLayoutJson {
    pub fn is_valid(&self) -> bool {
        !self.wheel_layout.trim().is_empty() && !self.string_keys.is_empty() && self.string_keys.len() <= 6
            && self.string_keys.iter().all(|binding| super::mapping::parse_key_stroke(binding).is_some())
            && self.mode_key.as_ref().is_none_or(|key| super::mapping::parse_key_stroke(key).is_some())
            && self.page_keys.as_ref().is_none_or(|keys| keys.iter().all(|key| super::mapping::parse_key_stroke(key).is_some()))
            && self.strum_buttons.as_ref().is_none_or(StrumButtonsJson::is_valid)
            && self.timbre_key.as_ref().is_none_or(|key| super::mapping::parse_key_stroke(key).is_some())
    }
}

/// Mouse positions are thousandths of the game window's client area.
#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ChordWheelConfigJson {
    pub positions: Vec<[u16; 2]>,
    pub strum_position: [u16; 2],
    pub move_delay_ms: u16,
    pub select_hold_ms: u16,
    pub after_select_ms: u16,
    pub page_count: usize,
    pub page_keys: [String; 2],
    pub page_hold_ms: u16,
    pub page_wait_ms: u16,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub strum_buttons: Option<StrumButtonsJson>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub mode_key: Option<String>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub timbre_key: Option<String>,
}

impl ChordWheelConfigJson {
    pub fn strum_binding(&self, direction: super::chord_wheel::StrumDirection) -> Option<super::chord_wheel::WheelBinding> {
        match &self.strum_buttons {
            Some(buttons) => buttons.binding_for(direction),
            None => Some(super::chord_wheel::WheelBinding::Mouse(direction.button())),
        }
    }

    pub fn is_valid(&self) -> bool {
        !self.positions.is_empty()
            && self.positions.iter().chain(std::iter::once(&self.strum_position))
                .flatten().all(|&coordinate| coordinate <= 1000)
            && (1..=1000).contains(&self.move_delay_ms)
            && (1..=1000).contains(&self.select_hold_ms)
            && (1..=1000).contains(&self.after_select_ms)
            && (1..=12).contains(&self.page_count)
            && self.page_keys.iter().all(|key| super::mapping::parse_key_stroke(key).is_some())
            && (1..=1000).contains(&self.page_hold_ms)
            && (1..=1000).contains(&self.page_wait_ms)
            && self.strum_buttons.as_ref().is_none_or(StrumButtonsJson::is_valid)
            && self.mode_key.as_ref().is_none_or(|key| super::mapping::parse_key_stroke(key).is_some())
            && self.timbre_key.as_ref().is_none_or(|key| super::mapping::parse_key_stroke(key).is_some())
    }
}

/// Visual appearance is independent of the playable pitch set and keyboard bindings.
#[derive(Debug, Clone, Copy, Serialize, Deserialize, PartialEq, Eq)]
#[serde(rename_all = "lowercase")]
pub enum InstrumentLayoutStyle {
    Sky,
    Genshin,
    Heartopia,
    Drums,
    Guitar,
    None,
}

/// Instrument configuration stored in `Games/<GameName>/Instruments/**/*.json`
#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", from = "InstrumentConfigSource")]
pub struct InstrumentConfigJson {
    pub id: String,
    pub name: String,
    #[serde(default)]
    pub category: Option<String>,
    #[serde(rename = "type", default)]
    pub instrument_type: Option<String>,
    #[serde(default)]
    pub layout_style: Option<InstrumentLayoutStyle>,
    /// Rows of MIDI pitches; null cells reserve an empty position in the visual layout.
    #[serde(default)]
    pub keys: Option<Vec<Vec<Option<u8>>>>,
    #[serde(default)]
    pub notes: Vec<u8>,
    #[serde(default)]
    pub keyboard_layouts: Vec<String>,
    #[serde(default)]
    pub chord_layouts: Vec<String>,
    #[serde(default)]
    pub fret_layouts: Vec<String>,
    #[serde(default)]
    pub chords: Vec<ChordConfigJson>,
    #[serde(default)]
    pub guitar: Option<GuitarConfigJson>,
    #[serde(default)]
    pub chord_mode: ChordMode,
    #[serde(default)]
    pub pluck_strings: bool,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub paged_chords: Option<bool>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub paged_frets: Option<bool>,
    #[serde(default)]
    pub supports_pedals: Option<bool>,
    #[serde(default)]
    pub supports_chords: Option<bool>,
    /// Relative subfolder (e.g. "Pianos", "Lyres") when discovered in nested folders
    #[serde(default)]
    pub subfolder: Option<String>,
    /// Absolute or relative path to this instrument JSON on disk
    #[serde(default)]
    pub file_path: Option<String>,
}

impl InstrumentConfigJson {
    pub fn all_layouts(&self) -> impl Iterator<Item = &String> {
        self.keyboard_layouts.iter().chain(&self.chord_layouts).chain(&self.fret_layouts)
    }

    pub fn supports_keyboard_layout(&self, id: &str) -> bool {
        self.keyboard_layouts.iter().any(|layout| layout.eq_ignore_ascii_case(id))
    }

    pub fn supports_chord_layout(&self, id: &str) -> bool {
        self.chord_layouts.iter().any(|layout| layout.eq_ignore_ascii_case(id))
    }

    pub fn supports_fret_layout(&self, id: &str) -> bool {
        self.fret_layouts.iter().any(|layout| layout.eq_ignore_ascii_case(id))
    }

    pub fn normalize_keys(&mut self) {
        if let Some(rows) = &self.keys {
            self.notes = rows.iter().flatten().filter_map(|pitch| *pitch).collect();
        }
    }
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
struct InstrumentConfigSource {
    id: String,
    name: String,
    #[serde(default)]
    category: Option<String>,
    #[serde(rename = "type", default)]
    instrument_type: Option<String>,
    #[serde(default)]
    layout_style: Option<InstrumentLayoutStyle>,
    #[serde(default)]
    keys: Option<Vec<Vec<Option<u8>>>>,
    #[serde(default)]
    notes: Vec<u8>,
    #[serde(default)]
    keyboard_layouts: Option<Vec<String>>,
    #[serde(default)]
    chord_layouts: Option<Vec<String>>,
    #[serde(default)]
    fret_layouts: Vec<String>,
    /// Read compatibility for existing app-data JSON; never serialized.
    #[serde(default)]
    supported_layouts: Vec<String>,
    #[serde(default, alias = "chordPads")]
    chords: Vec<ChordConfigJson>,
    #[serde(default)]
    guitar: Option<GuitarConfigJson>,
    #[serde(default)]
    chord_mode: ChordMode,
    #[serde(default)]
    pluck_strings: bool,
    #[serde(default)]
    paged_chords: Option<bool>,
    #[serde(default)]
    paged_frets: Option<bool>,
    #[serde(default)]
    supports_pedals: Option<bool>,
    #[serde(default)]
    supports_chords: Option<bool>,
    #[serde(default)]
    subfolder: Option<String>,
    #[serde(default)]
    file_path: Option<String>,
}

impl From<InstrumentConfigSource> for InstrumentConfigJson {
    fn from(source: InstrumentConfigSource) -> Self {
        // Old definitions used one list for melody and chords. An explicit new
        // chordLayouts array, including [], is always authoritative.
        let chord_layouts = source.chord_layouts.unwrap_or_else(|| {
            if source.chords.is_empty() { Vec::new() } else { source.supported_layouts.clone() }
        });
        let mut instrument = Self { id: source.id, name: source.name, category: source.category,
            instrument_type: source.instrument_type, layout_style: source.layout_style, keys: source.keys,
            notes: source.notes, keyboard_layouts: source.keyboard_layouts.unwrap_or(source.supported_layouts),
            chord_layouts, fret_layouts: source.fret_layouts, chords: source.chords, guitar: source.guitar, chord_mode: source.chord_mode,
            pluck_strings: source.pluck_strings, paged_chords: source.paged_chords, paged_frets: source.paged_frets,
            supports_pedals: source.supports_pedals, supports_chords: source.supports_chords,
            subfolder: source.subfolder, file_path: source.file_path };
        instrument.normalize_keys();
        instrument
    }
}

/// Physical chord interaction selected by the instrument; detection is shared.
#[derive(Debug, Default, Clone, Copy, Serialize, Deserialize, PartialEq, Eq)]
#[serde(rename_all = "lowercase")]
pub enum ChordMode {
    #[default]
    Pads,
    Wheel,
}

/// Modular keyboard layout stored in `KeyboardLayouts/*.json`
#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", from = "KeyboardLayoutSource")]
pub struct KeyboardLayoutJson {
    pub id: String,
    pub name: String,
    #[serde(default)]
    pub file_path: Option<String>,
    #[serde(default)]
    pub keys: Vec<String>,
    #[serde(default)]
    pub key_rows: Option<Vec<Vec<Option<String>>>>,
    #[serde(default)]
    pub chord_keys: Option<Vec<String>>,
    #[serde(default)]
    pub chord_wheel: Option<ChordWheelConfigJson>,
    #[serde(default)]
    pub fretboard: Option<FretLayoutJson>,
    #[serde(default)]
    pub sustain_key: Option<String>,
    #[serde(default)]
    pub sostenuto_key: Option<String>,
    #[serde(default)]
    pub una_corda_key: Option<String>,
}

#[derive(Debug, Deserialize)]
#[serde(untagged)]
enum KeyboardKeysSource {
    Flat(Vec<String>),
    Matrix(Vec<Vec<Option<String>>>),
}

impl Default for KeyboardKeysSource {
    fn default() -> Self { Self::Flat(Vec::new()) }
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
struct KeyboardLayoutSource {
    id: String,
    name: String,
    #[serde(default)]
    file_path: Option<String>,
    #[serde(default)]
    keys: KeyboardKeysSource,
    #[serde(default)]
    key_rows: Option<Vec<Vec<Option<String>>>>,
    #[serde(default)]
    chord_keys: Option<Vec<String>>,
    #[serde(default)]
    chord_wheel: Option<ChordWheelConfigJson>,
    #[serde(default)]
    fretboard: Option<FretLayoutJson>,
    #[serde(default)]
    sustain_key: Option<String>,
    #[serde(default)]
    sostenuto_key: Option<String>,
    #[serde(default)]
    una_corda_key: Option<String>,
}

impl From<KeyboardLayoutSource> for KeyboardLayoutJson {
    fn from(source: KeyboardLayoutSource) -> Self {
        let (keys, key_rows) = match source.keys {
            KeyboardKeysSource::Flat(keys) => (keys, source.key_rows),
            KeyboardKeysSource::Matrix(rows) => {
                let keys = rows.iter().flatten().filter_map(Clone::clone).collect();
                (keys, Some(rows))
            }
        };
        Self { id: source.id, name: source.name, file_path: source.file_path, keys, key_rows,
            chord_keys: source.chord_keys, chord_wheel: source.chord_wheel, fretboard: source.fretboard, sustain_key: source.sustain_key,
            sostenuto_key: source.sostenuto_key, una_corda_key: source.una_corda_key }
    }
}

/// Fully assembled bundle for a game with its detected icon and instruments
#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct LoadedGameBundle {
    pub config: GameConfig,
    pub icon_path: Option<String>,
    pub instruments: Vec<InstrumentConfigJson>,
    pub folder_path: String,
    #[serde(default)]
    pub config_path: Option<String>,
}

/// Scans a directory of keyboard layouts (e.g. `%LocalAppData%/Jed556/AutoMIDIPlayer/KeyboardLayouts`)
pub fn scan_keyboard_layouts(layouts_dir: &Path) -> HashMap<String, KeyboardLayoutJson> {
    let mut layouts = HashMap::new();
    if !layouts_dir.exists() || !layouts_dir.is_dir() {
        return layouts;
    }

    if let Ok(entries) = fs::read_dir(layouts_dir) {
        for entry in entries.flatten() {
            let path = entry.path();
            if path.is_file() && path.extension().and_then(|e| e.to_str()) == Some("json") {
                if let Ok(content) = fs::read_to_string(&path) {
                    if let Ok(mut layout) = serde_json::from_str::<KeyboardLayoutJson>(&content) {
                        layout.file_path = Some(path.to_string_lossy().into_owned());
                        let stem = path.file_stem().and_then(|s| s.to_str()).unwrap_or("").to_lowercase();
                        if layout.id.is_empty() {
                            layout.id = stem.clone();
                        }
                        let id_lower = layout.id.to_lowercase();
                        layouts.insert(id_lower, layout.clone());
                        if !stem.is_empty() && !layouts.contains_key(&stem) {
                            layouts.insert(stem, layout);
                        }
                    }
                }
            }
        }
    }

    layouts
}

/// Scans a directory of games (e.g. `%LocalAppData%/Jed556/AutoMIDIPlayer/Games`), including recursive instrument discovery
pub fn scan_games(games_dir: &Path) -> Vec<LoadedGameBundle> {
    let mut bundles = Vec::new();
    if !games_dir.exists() || !games_dir.is_dir() {
        return bundles;
    }

    let Ok(entries) = fs::read_dir(games_dir) else {
        return bundles;
    };

    for entry in entries.flatten() {
        let game_folder = entry.path();
        if !game_folder.is_dir() {
            continue;
        }

        let game_json_path = game_folder.join("game.json");
        let config: GameConfig = if game_json_path.exists() {
            if let Ok(content) = fs::read_to_string(&game_json_path) {
                if let Ok(cfg) = serde_json::from_str::<GameConfig>(&content) {
                    cfg
                } else {
                    continue;
                }
            } else {
                continue;
            }
        } else {
            // Check for any single .json in the game root as fallback
            let mut found_config = None;
            if let Ok(sub_entries) = fs::read_dir(&game_folder) {
                for sub in sub_entries.flatten() {
                    let p = sub.path();
                    if p.is_file() && p.extension().and_then(|e| e.to_str()) == Some("json") {
                        if let Ok(content) = fs::read_to_string(&p) {
                            if let Ok(cfg) = serde_json::from_str::<GameConfig>(&content) {
                                found_config = Some(cfg);
                                break;
                            }
                        }
                    }
                }
            }
            if let Some(cfg) = found_config {
                cfg
            } else {
                continue;
            }
        };

        // Icon resolution:
        let icon_path = resolve_game_icon(&game_folder, config.icon.as_deref());

        // Recursive Instruments scan
        let instruments_dir = game_folder.join("Instruments");
        let mut instruments = Vec::new();

        if instruments_dir.exists() && instruments_dir.is_dir() {
            for entry in WalkDir::new(&instruments_dir).into_iter().flatten() {
                let path = entry.path();
                if path.is_file() && path.extension().and_then(|e| e.to_str()) == Some("json") {
                    if let Ok(content) = fs::read_to_string(path) {
                        if let Ok(mut inst) = serde_json::from_str::<InstrumentConfigJson>(&content) {
                            // Determine subfolder relative to Instruments
                            let subfolder = path
                                .strip_prefix(&instruments_dir)
                                .ok()
                                .and_then(|rel| rel.parent())
                                .filter(|p| !p.as_os_str().is_empty())
                                .map(|p| p.to_string_lossy().to_string());

                            if inst.subfolder.is_none() {
                                inst.subfolder = subfolder.clone();
                            }
                            if inst.category.is_none() {
                                inst.category = subfolder;
                            }
                            inst.file_path = Some(path.to_string_lossy().to_string());
                            instruments.push(inst);
                        }
                    }
                }
            }
        }

        // Sort instruments by category, then by name
        instruments.sort_by(|a, b| {
            let cat_a = a.category.as_deref().unwrap_or("");
            let cat_b = b.category.as_deref().unwrap_or("");
            cat_a.cmp(cat_b).then_with(|| a.name.cmp(&b.name))
        });

        bundles.push(LoadedGameBundle {
            config,
            icon_path,
            instruments,
            folder_path: game_folder.to_string_lossy().to_string(),
            config_path: Some(game_json_path.to_string_lossy().into_owned()),
        });
    }

    // Sort bundles by display_name
    bundles.sort_by(|a, b| a.config.display_name.cmp(&b.config.display_name));
    bundles
}

/// Resolves the icon path for a game folder
pub(super) fn resolve_game_icon(game_folder: &Path, icon_name: Option<&str>) -> Option<String> {
    if let Some(name) = icon_name {
        let explicit = game_folder.join(name);
        if explicit.exists() {
            return Some(explicit.to_string_lossy().to_string());
        }
    }

    // Auto-detect first matching image file
    let valid_exts = ["png", "jpg", "jpeg", "webp", "ico"];
    if let Ok(entries) = fs::read_dir(game_folder) {
        for entry in entries.flatten() {
            let p = entry.path();
            if p.is_file() {
                if let Some(ext) = p.extension().and_then(|e| e.to_str()).map(|s| s.to_lowercase()) {
                    if valid_exts.contains(&ext.as_str()) {
                        return Some(p.to_string_lossy().to_string());
                    }
                }
            }
        }
    }

    None
}
