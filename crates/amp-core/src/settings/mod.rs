mod legacy;
pub mod defaults;

use crate::{KeyboardLayoutJson, LibraryDatabase, LoadedGameBundle};
use serde_json::{json, Value};
use std::{
    collections::{BTreeMap, HashMap},
    fs,
    io::Write,
    path::PathBuf,
    sync::Mutex,
};

/// Application preferences belong in settings.json; the library DB owns songs,
/// playlists, queue entries, and per-instrument song settings.
pub struct SettingsStore {
    path: PathBuf,
    document: Mutex<Value>,
}

pub(crate) fn merge(target: &mut Value, patch: &Value) {
    if let (Some(target), Some(patch)) = (target.as_object_mut(), patch.as_object()) {
        for (key, value) in patch {
            if value.is_object() && target.get(key).is_some_and(Value::is_object) {
                merge(target.get_mut(key).unwrap(), value);
            } else {
                target.insert(key.clone(), value.clone());
            }
        }
    }
}

impl SettingsStore {
    pub fn defaults() -> Value {
        defaults::document().clone()
    }

    pub fn default_path() -> PathBuf {
        crate::paths::app_data_dir().join("settings.json")
    }

    pub fn open(
        path: PathBuf,
        db: &LibraryDatabase,
        games: &[LoadedGameBundle],
        layouts: &HashMap<String, KeyboardLayoutJson>,
    ) -> Result<Self, String> {
        let existing: Value = match fs::read(&path) {
            Ok(bytes) => serde_json::from_slice(&bytes)
                .map_err(|e| format!("Cannot read {}: {e}", path.display()))?,
            Err(e) if e.kind() == std::io::ErrorKind::NotFound => json!({}),
            Err(e) => return Err(e.to_string()),
        };
        if !existing.is_object() {
            return Err("settings.json must contain an object".into());
        }
        if existing
            .get("_migrations")
            .is_some_and(|value| !value.is_object())
        {
            return Err("settings.json migration metadata must contain an object".into());
        }
        let mut document = existing.clone();
        let mut inherited = Self::defaults();
        let database_preferences = db
            .legacy_app_settings()
            .map_err(|e| e.to_string())?
            .unwrap_or_else(|| json!({}));
        merge(&mut inherited, &database_preferences);
        let mut legacy_values = existing
            .get("_legacyUserConfig")
            .cloned()
            .and_then(|raw| serde_json::from_value::<BTreeMap<String, String>>(raw).ok());
        let config_path = path.with_file_name("user.config");
        let imported = existing
            .get("_migrations")
            .and_then(|m| m.get("userConfig"))
            .and_then(Value::as_bool)
            == Some(true);
        let importing = !imported && config_path.is_file();
        if importing {
            let raw = legacy::read(&config_path)?;
            // The legacy file seeds the new preferences; an existing settings.json
            // always wins. Rewrite-only settings from the DB are retained as well.
            merge(&mut inherited, &legacy::map(&raw, games, layouts, db)?);
            db.import_legacy_queue(
                raw.get("QueueSongIds")
                    .map(String::as_str)
                    .unwrap_or_default(),
            )
            .map_err(|e| e.to_string())?;
            inherited["_legacyUserConfig"] = json!(raw);
            legacy_values = Some(raw);
            inherited["_migrations"]["userConfig"] = json!(true);
        }
        // Config IDs are case-insensitive. Resolve archived selections once
        // layouts are available, including settings.json files imported earlier.
        let resolve_selections = !games.is_empty()
            && !layouts.is_empty()
            && existing
                .pointer("/_migrations/userConfigSelections")
                .and_then(Value::as_bool)
                != Some(true)
            && legacy_values.is_some();
        if resolve_selections {
            merge(
                &mut inherited,
                &legacy::selections(legacy_values.as_ref().unwrap(), games, layouts),
            );
        }
        merge(&mut inherited, &existing);
        if importing {
            inherited["_migrations"]["userConfig"] = json!(true);
        }
        if resolve_selections {
            inherited["_migrations"]["userConfigSelections"] = json!(true);
        }
        inherited["_migrations"]["databasePreferences"] = json!(true);
        let changed = inherited != document;
        document = inherited;
        let store = Self {
            path,
            document: Mutex::new(document),
        };
        if changed || !store.path.is_file() {
            let current = store.document.lock().map_err(|e| e.to_string())?;
            store.persist(&current)?;
        }
        // The file has been flushed and atomically installed before removing
        // the old table. A crash between these operations is safe to retry.
        db.remove_legacy_app_settings().map_err(|e| e.to_string())?;
        Ok(store)
    }

    pub fn get(&self) -> Result<Value, String> {
        let mut settings = self.document.lock().map_err(|e| e.to_string())?.clone();
        settings
            .as_object_mut()
            .unwrap()
            .retain(|key, _| !key.starts_with('_'));
        Ok(settings)
    }

    pub fn update(&self, patch: &Value) -> Result<(), String> {
        if !patch.is_object()
            || patch
                .as_object()
                .unwrap()
                .keys()
                .any(|key| key.starts_with('_'))
        {
            return Err("Settings must be an object without reserved keys".into());
        }
        let mut current = self.document.lock().map_err(|e| e.to_string())?;
        let mut next = current.clone();
        merge(&mut next, patch);
        self.persist(&next)?;
        *current = next;
        Ok(())
    }

    pub fn reset(&self) -> Result<(), String> {
        let mut current = self.document.lock().map_err(|e| e.to_string())?;
        let mut next = Self::defaults();
        for (key, value) in current.as_object().unwrap() {
            if key.starts_with('_') {
                next[key] = value.clone();
            }
        }
        self.persist(&next)?;
        *current = next;
        Ok(())
    }

    fn persist(&self, document: &Value) -> Result<(), String> {
        let parent = self
            .path
            .parent()
            .ok_or("Settings path requires a parent directory")?;
        fs::create_dir_all(parent).map_err(|e| e.to_string())?;
        let temporary = parent.join(format!(".settings-{}.tmp", uuid::Uuid::new_v4()));
        let result = (|| -> Result<(), String> {
            let mut file = fs::OpenOptions::new()
                .write(true)
                .create_new(true)
                .open(&temporary)
                .map_err(|e| e.to_string())?;
            serde_json::to_writer_pretty(&mut file, document).map_err(|e| e.to_string())?;
            file.write_all(b"\n")
                .and_then(|_| file.sync_all())
                .map_err(|e| e.to_string())?;
            drop(file);
            fs::rename(&temporary, &self.path)
                .map_err(|e| format!("Cannot save {}: {e}", self.path.display()))
        })();
        if result.is_err() {
            let _ = fs::remove_file(temporary);
        }
        result
    }
}
