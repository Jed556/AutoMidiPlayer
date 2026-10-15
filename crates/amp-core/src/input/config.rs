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

/// Pre-composed chord pad definition on an instrument
#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ChordPadConfigJson {
    pub name: String,
    pub pitch_classes: Vec<u8>,
}

/// Visual appearance is independent of the playable pitch set and keyboard bindings.
#[derive(Debug, Clone, Copy, Serialize, Deserialize, PartialEq, Eq)]
#[serde(rename_all = "lowercase")]
pub enum InstrumentLayoutStyle {
    Sky,
    Genshin,
    Heartopia,
    Drums,
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
    pub supported_layouts: Vec<String>,
    #[serde(default)]
    pub chord_pads: Vec<ChordPadConfigJson>,
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
    supported_layouts: Vec<String>,
    #[serde(default)]
    chord_pads: Vec<ChordPadConfigJson>,
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
        let mut instrument = Self { id: source.id, name: source.name, category: source.category,
            instrument_type: source.instrument_type, layout_style: source.layout_style, keys: source.keys,
            notes: source.notes, supported_layouts: source.supported_layouts, chord_pads: source.chord_pads,
            supports_pedals: source.supports_pedals, supports_chords: source.supports_chords,
            subfolder: source.subfolder, file_path: source.file_path };
        instrument.normalize_keys();
        instrument
    }
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
            chord_keys: source.chord_keys, sustain_key: source.sustain_key,
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

/// Scans a directory of keyboard layouts (e.g. `%LocalAppData%/AutoMidiPlayer/KeyboardLayouts`)
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

/// Scans a directory of games (e.g. `%LocalAppData%/AutoMidiPlayer/Games`), including recursive instrument discovery
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
