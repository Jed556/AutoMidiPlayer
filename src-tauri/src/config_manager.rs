use amp_core::{
    KeyboardLayoutJson, LoadedGameBundle,
};
use amp_core::input::config_scan::{scan_games_checked, scan_layouts_checked, ConfigIssue};
use include_dir::{include_dir, Dir};
use log::{info, warn};
use std::collections::HashMap;
use std::fs;
use std::io::Write;
use std::path::{Path, PathBuf};

static DEFAULTS_DIR: Dir = include_dir!("$CARGO_MANIFEST_DIR/resources/defaults");

pub struct ConfigManager {
    base_dir: PathBuf,
    games_dir: PathBuf,
    layouts_dir: PathBuf,
    pub games: Vec<LoadedGameBundle>,
    pub layouts: HashMap<String, KeyboardLayoutJson>,
    pub issues: Vec<ConfigIssue>,
    loaded: bool,
}

#[derive(Clone, serde::Serialize)]
#[serde(rename_all = "camelCase")]
pub struct ConfigSnapshot {
    pub games: Vec<LoadedGameBundle>,
    pub layouts: HashMap<String, KeyboardLayoutJson>,
    pub issues: Vec<ConfigIssue>,
}

impl ConfigManager {
    pub fn new() -> Self {
        let base_dir = get_appdata_dir();
        let games_dir = base_dir.join("Games");
        let layouts_dir = base_dir.join("KeyboardLayouts");

        let mut mgr = Self {
            base_dir,
            games_dir,
            layouts_dir,
            games: Vec::new(),
            layouts: HashMap::new(),
            issues: Vec::new(),
            loaded: false,
        };

        mgr.extract_defaults_if_missing();
        mgr.reload();
        mgr
    }

    /// Extracts bundled defaults into %LocalAppData%\Jed556\AutoMIDIPlayer without overwriting existing files
    pub fn extract_defaults_if_missing(&self) {
        let _ = fs::create_dir_all(&self.games_dir);
        let _ = fs::create_dir_all(&self.layouts_dir);

        extract_dir_recursive(&DEFAULTS_DIR, &self.base_dir);
    }

    /// Reloads all keyboard layouts and game definitions from disk
    pub fn reload(&mut self) {
        info!("[ConfigManager] Reloading configs from \"{}\"", self.base_dir.display());
        let (layouts, mut issues) = scan_layouts_checked(&self.layouts_dir);
        let (games, game_issues) = scan_games_checked(&self.games_dir);
        issues.extend(game_issues);
        for issue in &issues {
            warn!("[ConfigManager] {}: {}", issue.path, issue.message);
        }
        // Commit games and layouts together; a partial save must not destroy a working mapping.
        // At startup there is no previous snapshot, so load the valid entries and report the rest.
        if issues.is_empty() || !self.loaded {
            self.layouts = layouts;
            self.games = games;
            self.loaded = true;
        } else {
            // Malformed edits retain their last valid data, but a deleted file is unavailable.
            self.games.retain(|game| game.config_path.as_ref().is_none_or(|path| Path::new(path).is_file()));
            for game in &mut self.games {
                game.instruments.retain(|inst| inst.file_path.as_ref().is_none_or(|path| Path::new(path).is_file()));
            }
            self.layouts.retain(|_, layout| layout.file_path.as_ref().is_none_or(|path| Path::new(path).is_file()));
        }
        self.issues = issues;
        info!(
            "[ConfigManager] Loaded {} games and {} keyboard layouts",
            self.games.len(),
            self.layouts.len()
        );
    }

    /// Stage defaults before recycling existing game, instrument, and layout files.
    pub fn reset(&mut self) -> Result<(), String> {
        self.reset_with(|paths| trash::delete_all(paths).map_err(|e| e.to_string()))
    }

    fn reset_with(&mut self, recycle: impl FnOnce(&[PathBuf]) -> Result<(), String>) -> Result<(), String> {
        let staging = self.base_dir.join(format!(".config-reset-{}", std::time::SystemTime::now()
            .duration_since(std::time::UNIX_EPOCH).map_err(|e| e.to_string())?.as_nanos()));
        let result: Result<(), String> = (|| {
            extract_defaults_checked(&DEFAULTS_DIR, &staging)?;
            let paths: Vec<_> = [&self.games_dir, &self.layouts_dir].into_iter()
                .filter(|path| path.exists()).cloned().collect();
            recycle(&paths).map_err(|e| format!("Unable to recycle configurations: {e}"))?;
            for name in ["Games", "KeyboardLayouts"] {
                fs::rename(staging.join(name), self.base_dir.join(name))
                    .map_err(|e| format!("Unable to restore {name}: {e}"))?;
            }
            self.loaded = false;
            self.reload();
            Ok(())
        })();
        if result.is_ok() {
            let _ = fs::remove_dir_all(&staging);
        }
        result.map_err(|error| format!("{error}. Staged defaults: {}", staging.display()))
    }

    pub fn snapshot(&self) -> ConfigSnapshot {
        ConfigSnapshot { games: self.games.clone(), layouts: self.layouts.clone(), issues: self.issues.clone() }
    }

    pub fn watch_root(&self) -> &Path { &self.base_dir }

    /// Resolve only a registered app-data layout; callers cannot supply a file path.
    fn layout_path(&self, layout_id: &str) -> Result<PathBuf, String> {
        let layout = self.layouts.get(&layout_id.to_lowercase()).ok_or("Keyboard layout not found")?;
        let path = Path::new(layout.file_path.as_deref().ok_or("Keyboard layout has no file")?)
            .canonicalize().map_err(|e| format!("Cannot open keyboard layout: {e}"))?;
        let root = self.layouts_dir.canonicalize().map_err(|e| e.to_string())?;
        let base = self.base_dir.canonicalize().map_err(|e| e.to_string())?;
        if !root.starts_with(&base) || path.parent() != Some(root.as_path()) {
            return Err("Only keyboard layouts inside app data can be edited".into());
        }
        Ok(path)
    }

    /// Bundled files are read-only references for resetting the three pedal fields.
    pub fn layout_pedal_defaults(&self, layout_id: &str) -> Result<serde_json::Value, String> {
        let layout = self.layouts.get(&layout_id.to_lowercase()).ok_or("Keyboard layout not found")?;
        let default = DEFAULTS_DIR.get_dir("KeyboardLayouts").into_iter().flat_map(|dir| dir.files())
            .filter_map(|file| serde_json::from_slice::<serde_json::Value>(file.contents()).ok())
            .find(|value| value.get("id").and_then(|id| id.as_str())
                .is_some_and(|id| id.eq_ignore_ascii_case(&layout.id)))
            .unwrap_or(serde_json::Value::Null);
        Ok(["sustainKey", "sostenutoKey", "unaCordaKey"].into_iter()
            .map(|field| (field.to_string(), default.get(field).cloned().unwrap_or(serde_json::Value::Null)))
            .collect::<serde_json::Map<_, _>>().into())
    }

    pub fn save_layout_pedals(
        &mut self,
        layout_id: &str,
        bindings: Option<&serde_json::Map<String, serde_json::Value>>,
    ) -> Result<(), String> {
        let path = self.layout_path(layout_id)?;
        let defaults = self.layout_pedal_defaults(layout_id)?;
        let bindings = bindings.unwrap_or_else(|| defaults.as_object().unwrap());
        for (field, value) in bindings {
            if !["sustainKey", "sostenutoKey", "unaCordaKey"].contains(&field.as_str())
                || (!value.is_null() && value.as_str().and_then(amp_core::parse_key_stroke).is_none())
            {
                return Err(format!("Invalid pedal binding: {field}"));
            }
        }
        // Patch the latest file, preserving key matrices, custom metadata and other edits.
        let mut document: serde_json::Value = serde_json::from_slice(&fs::read(&path).map_err(|e| e.to_string())?)
            .map_err(|e| format!("Cannot read keyboard layout: {e}"))?;
        let original: KeyboardLayoutJson = serde_json::from_value(document.clone()).map_err(|e| e.to_string())?;
        let registered = &self.layouts[&layout_id.to_lowercase()];
        if !original.id.is_empty() && !original.id.eq_ignore_ascii_case(&registered.id) {
            return Err("Keyboard layout changed on disk; reload configurations before editing".into());
        }
        for (field, value) in bindings { document[field] = value.clone(); }
        let mut updated: KeyboardLayoutJson = serde_json::from_value(document.clone()).map_err(|e| e.to_string())?;
        if updated.id.is_empty() { updated.id = registered.id.clone(); }
        updated.file_path = registered.file_path.clone();
        let temporary = self.layouts_dir.join(format!(".pedal-bindings-{}-{}.tmp", std::process::id(),
            std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH).map_err(|e| e.to_string())?.as_nanos()));
        let result = (|| {
            let mut file = fs::OpenOptions::new().write(true).create_new(true).open(&temporary).map_err(|e| e.to_string())?;
            serde_json::to_writer_pretty(&mut file, &document).map_err(|e| e.to_string())?;
            file.write_all(b"\n").and_then(|_| file.sync_all()).map_err(|e| e.to_string())?;
            drop(file);
            fs::rename(&temporary, &path).map_err(|e| format!("Cannot save keyboard layout: {e}"))
        })();
        if result.is_err() { let _ = fs::remove_file(&temporary); }
        result?;
        // Keep ID and filename aliases current even if another config has an unrelated error.
        for layout in self.layouts.values_mut().filter(|layout| layout.id.eq_ignore_ascii_case(&updated.id)) {
            *layout = updated.clone();
        }
        Ok(())
    }

    /// Returns a list of process names across all loaded games
    pub fn all_process_names(&self) -> Vec<String> {
        let mut names = Vec::new();
        for bundle in &self.games {
            for proc in &bundle.config.process_names {
                if !names.contains(proc) {
                    names.push(proc.clone());
                }
            }
        }
        names
    }

    /// Opens the Games directory in Windows File Explorer
    pub fn open_games_folder(&self) -> Result<(), String> {
        open_folder_in_explorer(&self.games_dir)
    }

    /// Opens the KeyboardLayouts directory in Windows File Explorer
    pub fn open_layouts_folder(&self) -> Result<(), String> {
        open_folder_in_explorer(&self.layouts_dir)
    }
}

pub fn get_appdata_dir() -> PathBuf {
    amp_core::paths::app_data_dir()
}

fn extract_defaults_checked(dir: &Dir, root: &Path) -> Result<(), String> {
    for file in dir.files() {
        let path = root.join(file.path());
        if let Some(parent) = path.parent() {
            fs::create_dir_all(parent).map_err(|e| e.to_string())?;
        }
        fs::write(path, file.contents()).map_err(|e| e.to_string())?;
    }
    for child in dir.dirs() {
        extract_defaults_checked(child, root)?;
    }
    Ok(())
}

fn extract_dir_recursive(embedded_dir: &Dir, target_root: &Path) {
    for file in embedded_dir.files() {
        let rel_path = file.path();
        let target_file_path = target_root.join(rel_path);

        if !target_file_path.exists() {
            if let Some(parent) = target_file_path.parent() {
                let _ = fs::create_dir_all(parent);
            }
            if let Err(e) = fs::write(&target_file_path, file.contents()) {
                warn!("[ConfigManager] Failed to extract \"{}\": {}", target_file_path.display(), e);
            }
        }
    }

    for dir in embedded_dir.dirs() {
        extract_dir_recursive(dir, target_root);
    }
}

fn open_folder_in_explorer(path: &Path) -> Result<(), String> {
    #[cfg(windows)]
    {
        let _ = fs::create_dir_all(path);
        std::process::Command::new("explorer")
            .arg(path.as_os_str())
            .spawn()
            .map_err(|e| format!("Failed to open explorer: {}", e))?;
        Ok(())
    }
    #[cfg(not(windows))]
    {
        let _ = fs::create_dir_all(path);
        std::process::Command::new(if cfg!(target_os = "macos") { "open" } else { "xdg-open" })
            .arg(path.as_os_str())
            .spawn()
            .map_err(|e| format!("Failed to open folder: {}", e))?;
        Ok(())
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn pedal_test_manager() -> ConfigManager {
        let root = std::env::temp_dir().join(format!("amp_pedal_edit_{}_{}", std::process::id(),
            std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH).unwrap().as_nanos()));
        let mut manager = ConfigManager { base_dir: root.clone(), games_dir: root.join("Games"),
            layouts_dir: root.join("KeyboardLayouts"), games: Vec::new(), layouts: HashMap::new(),
            issues: Vec::new(), loaded: false };
        manager.extract_defaults_if_missing();
        manager.reload();
        manager
    }

    #[test]
    fn pedal_edits_and_reset_update_only_appdata_layout_fields() {
        let mut manager = pedal_test_manager();
        let id = "QWERTY_61Keys";
        let path = manager.layouts_dir.join(format!("{id}.json"));
        let source = Path::new(env!("CARGO_MANIFEST_DIR")).join(format!("resources/defaults/KeyboardLayouts/{id}.json"));
        let source_bytes = fs::read(&source).unwrap();
        let other = manager.layouts_dir.join("QWERTY_88Keys.json");
        let other_bytes = fs::read(&other).unwrap();
        fs::write(manager.base_dir.join("settings.json"), "{\"keep\":true}").unwrap();
        // An edit made on disk after scanning must survive saving a pedal.
        let mut document: serde_json::Value = serde_json::from_slice(&fs::read(&path).unwrap()).unwrap();
        document["customMetadata"] = serde_json::json!({ "keep": [1, 2, 3] });
        document["keys"][0][0] = serde_json::json!("a");
        fs::write(&path, serde_json::to_vec(&document).unwrap()).unwrap();
        manager.save_layout_pedals(id, serde_json::json!({ "sostenutoKey": "Shift" }).as_object()).unwrap();
        let saved: serde_json::Value = serde_json::from_slice(&fs::read(&path).unwrap()).unwrap();
        document["sostenutoKey"] = serde_json::json!("Shift");
        assert_eq!(saved, document);
        assert_eq!(manager.layouts[&id.to_lowercase()].sostenuto_key.as_deref(), Some("Shift"));
        manager.save_layout_pedals(id, serde_json::json!({ "sustainKey": null }).as_object()).unwrap();
        manager.reload();
        assert_eq!(manager.layouts[&id.to_lowercase()].sustain_key, None);
        manager.save_layout_pedals(id, None).unwrap();
        manager.reload();
        assert_eq!(manager.layouts[&id.to_lowercase()].sustain_key.as_deref(), Some("space"));
        assert_eq!(manager.layouts[&id.to_lowercase()].sostenuto_key, None);
        let reset: serde_json::Value = serde_json::from_slice(&fs::read(&path).unwrap()).unwrap();
        assert_eq!(reset["keys"], document["keys"]);
        assert_eq!(reset["customMetadata"], document["customMetadata"]);
        assert_eq!(fs::read(source).unwrap(), source_bytes);
        assert_eq!(fs::read(other).unwrap(), other_bytes);
        assert_eq!(fs::read_to_string(manager.base_dir.join("settings.json")).unwrap(), "{\"keep\":true}");
        assert_eq!(manager.base_dir.parent(), Some(std::env::temp_dir().as_path()));
        fs::remove_dir_all(manager.base_dir).unwrap();
    }

    #[test]
    fn pedal_edits_reject_invalid_keys_missing_files_and_paths_outside_appdata() {
        let mut manager = pedal_test_manager();
        let id = "QWERTY_61Keys";
        let path = manager.layout_path(id).unwrap();
        let original = fs::read(&path).unwrap();
        for patch in [serde_json::json!({ "keys": [] }), serde_json::json!({ "sustainKey": "not-a-key" }),
            serde_json::json!({ "sustainKey": false })] {
            assert!(manager.save_layout_pedals(id, patch.as_object()).is_err());
            assert_eq!(fs::read(&path).unwrap(), original);
        }
        assert!(manager.save_layout_pedals("../anything", None).is_err());
        let source = Path::new(env!("CARGO_MANIFEST_DIR")).join(format!("resources/defaults/KeyboardLayouts/{id}.json"));
        manager.layouts.get_mut(&id.to_lowercase()).unwrap().file_path = Some(source.to_string_lossy().into_owned());
        assert!(manager.save_layout_pedals(id, None).unwrap_err().contains("app data"));
        manager.layouts.get_mut(&id.to_lowercase()).unwrap().file_path = Some(path.to_string_lossy().into_owned());
        fs::write(&path, "{").unwrap();
        assert!(manager.save_layout_pedals(id, None).is_err());
        assert_eq!(fs::read_to_string(&path).unwrap(), "{");
        fs::remove_file(&path).unwrap();
        assert!(manager.save_layout_pedals(id, None).is_err());
        assert!(!path.exists());
        assert_eq!(manager.base_dir.parent(), Some(std::env::temp_dir().as_path()));
        fs::remove_dir_all(manager.base_dir).unwrap();
    }

    #[test]
    fn custom_layout_reset_clears_pedals_and_preserves_matrix_and_aliases() {
        let mut manager = pedal_test_manager();
        let path = manager.layouts_dir.join("custom-file.json");
        let document = serde_json::json!({ "id": "CustomPiano", "name": "Custom", "keys": [["q", null, "Q"]],
            "sustainKey": "Tab", "custom": true });
        fs::write(&path, serde_json::to_vec(&document).unwrap()).unwrap();
        manager.reload();
        manager.save_layout_pedals("custom-file", serde_json::json!({ "sostenutoKey": "Control" }).as_object()).unwrap();
        assert_eq!(manager.layouts["custompiano"].sostenuto_key.as_deref(), Some("Control"));
        assert_eq!(manager.layouts["custom-file"].sostenuto_key.as_deref(), Some("Control"));
        manager.save_layout_pedals("CustomPiano", None).unwrap();
        let saved: serde_json::Value = serde_json::from_slice(&fs::read(&path).unwrap()).unwrap();
        assert_eq!(saved["keys"], document["keys"]);
        assert_eq!(saved["custom"], true);
        assert!(saved["sustainKey"].is_null());
        assert!(saved["sostenutoKey"].is_null());
        assert_eq!(manager.base_dir.parent(), Some(std::env::temp_dir().as_path()));
        fs::remove_dir_all(manager.base_dir).unwrap();
    }

    #[test]
    fn reset_restores_defaults_removes_custom_entries_and_preserves_other_data() {
        let root = std::env::temp_dir().join(format!("amp_config_reset_{}_{}", std::process::id(),
            std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH).unwrap().as_nanos()));
        let mut manager = ConfigManager { base_dir: root.clone(), games_dir: root.join("Games"),
            layouts_dir: root.join("KeyboardLayouts"), games: Vec::new(), layouts: HashMap::new(),
            issues: Vec::new(), loaded: false };
        manager.extract_defaults_if_missing();
        let layout = manager.layouts_dir.join("custom.json");
        fs::write(&layout, "custom").unwrap();
        let custom_instrument = manager.games_dir.join("Custom/Instruments/custom.json");
        fs::create_dir_all(custom_instrument.parent().unwrap()).unwrap();
        fs::write(&custom_instrument, "custom").unwrap();
        fs::write(root.join("settings.db"), "keep").unwrap();
        // A failed recycle must leave the original configuration files intact.
        assert!(manager.reset_with(|_| Err("recycle failed".into())).is_err());
        assert!(custom_instrument.is_file());
        let recovery = root.join("recovery");
        fs::create_dir_all(&recovery).unwrap();
        manager.reset_with(|paths| {
            for path in paths {
                fs::rename(path, recovery.join(path.file_name().unwrap())).unwrap();
            }
            Ok(())
        }).unwrap();
        assert!(!layout.exists());
        assert!(!custom_instrument.exists());
        assert!(recovery.join("Games/Custom/Instruments/custom.json").is_file());
        assert_eq!(fs::read_to_string(root.join("settings.db")).unwrap(), "keep");
        assert!(manager.issues.is_empty(), "{:?}", manager.issues);
        assert!(!manager.games.is_empty());
        assert!(!manager.layouts.is_empty());
        fn assert_defaults(dir: &Dir, root: &Path) {
            for file in dir.files() {
                assert_eq!(fs::read(root.join(file.path())).unwrap(), file.contents());
            }
            for child in dir.dirs() { assert_defaults(child, root); }
        }
        assert_defaults(&DEFAULTS_DIR, &root);
        assert_eq!(root.parent(), Some(std::env::temp_dir().as_path()));
        fs::remove_dir_all(root).unwrap();
    }

    #[test]
    fn deletions_are_applied_even_when_an_unrelated_edit_is_malformed() {
        let root = std::env::temp_dir().join(format!("amp_config_deletion_{}_{}", std::process::id(),
            std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH).unwrap().as_nanos()));
        let games_dir = root.join("Games");
        let layouts_dir = root.join("KeyboardLayouts");
        let instruments = games_dir.join("NTE/Instruments");
        fs::create_dir_all(&instruments).unwrap();
        fs::create_dir_all(&layouts_dir).unwrap();
        let game = games_dir.join("NTE/game.json");
        let guitar = instruments.join("Guitar.json");
        let piano = instruments.join("Piano.json");
        let layout = layouts_dir.join("QWERTY.json");
        fs::write(&game, r#"{"id":"NTE","displayName":"NTE"}"#).unwrap();
        fs::write(&guitar, r#"{"id":"guitar","name":"Guitar","notes":[60]}"#).unwrap();
        fs::write(&piano, r#"{"id":"piano","name":"Piano","notes":[60]}"#).unwrap();
        fs::write(&layout, r#"{"id":"qwerty","name":"QWERTY","keys":["a"]}"#).unwrap();
        let mut manager = ConfigManager { base_dir: root.clone(), games_dir, layouts_dir,
            games: Vec::new(), layouts: HashMap::new(), issues: Vec::new(), loaded: false };
        manager.reload();
        assert!(manager.issues.is_empty());
        fs::write(piano, "{").unwrap();
        fs::remove_file(guitar).unwrap();
        manager.reload();
        assert!(!manager.issues.is_empty());
        assert_eq!(manager.games[0].instruments.len(), 1);
        assert_eq!(manager.games[0].instruments[0].id, "piano");
        fs::remove_file(layout).unwrap();
        manager.reload();
        assert!(manager.layouts.is_empty());
        fs::remove_file(game).unwrap();
        manager.reload();
        assert!(manager.games.is_empty());
        assert_eq!(root.parent(), Some(std::env::temp_dir().as_path()));
        fs::remove_dir_all(root).unwrap();
    }

    #[test]
    fn reload_rejects_invalid_edits_and_recovers_after_fixes_or_removals() {
        let root = std::env::temp_dir().join(format!("amp_config_manager_{}_{}", std::process::id(),
            std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH).unwrap().as_nanos()));
        let games_dir = root.join("Games");
        let layouts_dir = root.join("KeyboardLayouts");
        let instruments = games_dir.join("NTE/Instruments");
        fs::create_dir_all(&instruments).unwrap();
        fs::create_dir_all(&layouts_dir).unwrap();
        let game = games_dir.join("NTE/game.json");
        let guitar = instruments.join("Guitar.json");
        fs::write(&game, r#"{"id":"NTE","displayName":"NTE"}"#).unwrap();
        let valid = r#"{"id":"guitar","name":"Guitar","notes":[60]}"#;
        fs::write(&guitar, valid).unwrap();
        let mut manager = ConfigManager { base_dir: root.clone(), games_dir, layouts_dir,
            games: Vec::new(), layouts: HashMap::new(), issues: Vec::new(), loaded: false };
        manager.reload();
        assert!(manager.issues.is_empty());
        assert_eq!(manager.games[0].instruments[0].notes, [60]);
        fs::write(&guitar, "{").unwrap();
        manager.reload();
        assert!(manager.issues[0].message.contains("Malformed JSON"));
        assert_eq!(manager.games[0].instruments[0].notes, [60]);
        fs::write(&guitar, valid.replace("60", "72")).unwrap();
        let duplicate = instruments.join("Copy.json");
        fs::write(&duplicate, valid).unwrap();
        manager.reload();
        assert!(manager.issues.iter().any(|i| i.message.contains("Duplicate instrument ID")));
        assert_eq!(manager.games[0].instruments[0].notes, [60]);
        fs::remove_file(duplicate).unwrap();
        manager.reload();
        assert!(manager.issues.is_empty());
        assert_eq!(manager.games[0].instruments[0].notes, [72]);
        fs::remove_file(guitar).unwrap();
        manager.reload();
        assert!(manager.games[0].instruments.is_empty());
        fs::remove_file(game).unwrap();
        manager.reload();
        assert!(manager.games.is_empty());
        assert_eq!(root.parent(), Some(std::env::temp_dir().as_path()));
        fs::remove_dir_all(root).unwrap();
    }
}
