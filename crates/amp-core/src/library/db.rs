use std::path::{Path, PathBuf};
use std::sync::{Arc, Mutex};
use rusqlite::{params, Connection, OptionalExtension};
use serde_json::Value;
use uuid::Uuid;
use chrono::Utc;
use crate::library::models::{AlbumRecord, PlaylistRecord, SongDetailsUpdate, SongMetadataUpdate, SongRecord, SongSettingsUpdate};
use crate::library::models::{SongSettingsContext, SongSettingsScope};

// One source for schema validation and additive upgrades, including early WPF libraries.
const SONG_COLUMNS: &[(&str, &str)] = &[
    ("BaseKey", "INTEGER"), ("Key", "INTEGER NOT NULL DEFAULT 0"),
    ("FileHash", "TEXT"), ("Title", "TEXT"), ("Artist", "TEXT"), ("Album", "TEXT"),
    ("AlbumId", "TEXT REFERENCES Albums(Id) ON DELETE SET NULL"), ("DateAdded", "TEXT"),
    ("Transpose", "INTEGER NOT NULL DEFAULT 0"), ("Speed", "REAL DEFAULT 1.0"), ("Bpm", "REAL"),
    ("DisabledTracks", "TEXT"), ("MergeNotes", "INTEGER"), ("MergeMilliseconds", "INTEGER"),
    ("HoldNotes", "INTEGER"), ("TapThresholdMs", "INTEGER DEFAULT 0"), ("ImagePath", "TEXT"),
    ("CachedDurationMs", "INTEGER"), ("CachedNativeBpm", "REAL"), ("AutoChord", "INTEGER"),
    ("ChordDetectionMilliseconds", "INTEGER"), ("KeyChanges", "TEXT"),
    ("EnableKeyChangeMarkers", "INTEGER DEFAULT 1"),
];
const LEGACY_SONG_SETTINGS_MIGRATION: &str = "legacy-instrument-song-settings-v1";
const MARKER_LABEL_MIGRATION: &str = "marker-label-offsets-v1";
const TECHNICAL_SONG_FIELDS: &[&str] = &[
    "baseKey", "key", "transpose", "speed", "bpm", "disabledTracks", "mergeNotes",
    "mergeMilliseconds", "holdNotes", "tapThresholdMs", "autoChord",
    "chordDetectionMilliseconds", "keyChanges", "enableKeyChangeMarkers",
];

fn merge_json_object(target: &mut Value, patch: &Value) {
    let (Value::Object(target), Value::Object(patch)) = (target, patch) else {
        return;
    };

    for (key, patch_value) in patch {
        match (target.get_mut(key), patch_value) {
            (Some(existing), Value::Object(_)) if existing.is_object() => {
                merge_json_object(existing, patch_value);
            }
            _ => {
                target.insert(key.clone(), patch_value.clone());
            }
        }
    }
}

// Strip only a trailing offset that agrees with KeyOffset. Keep octave digits,
// custom label text, timing, transpose, and any unknown marker metadata intact.
fn normalize_marker_labels(json: &str, suffix: &regex::Regex) -> Option<String> {
    let mut markers: Value = serde_json::from_str(json).ok()?;
    let mut changed = false;
    for marker in markers.as_array_mut()? {
        let Some(offset) = marker.get("KeyOffset").and_then(Value::as_i64) else { continue; };
        let Some(label) = marker.get_mut("Label") else { continue; };
        let Some(original) = label.as_str() else { continue; };
        let mut name = original;
        while let Some(captures) = suffix.captures(name) {
            if captures[1].parse::<i64>().ok() != Some(offset) { break; }
            let text = name[..captures.get(0).unwrap().start()].trim_end();
            if text.trim().is_empty() { break; }
            name = text;
        }
        if name != original {
            *label = Value::String(name.to_owned());
            changed = true;
        }
    }
    changed.then(|| markers.to_string())
}

pub struct LibraryDatabase {
    pub(super) conn: Arc<Mutex<Connection>>,
    db_path: PathBuf,
}

impl LibraryDatabase {
    /// Returns default location at %LocalAppData%\Jed556\AutoMIDIPlayer\AutoMidiPlayer.db
    pub fn default_path() -> PathBuf {
        crate::paths::app_data_dir().join("AutoMidiPlayer.db")
    }

    /// Opens or creates database at specified path and runs migrations
    pub fn new(path: Option<PathBuf>) -> Result<Self, rusqlite::Error> {
        Self::new_with_migration_callback(path, || {})
    }

    /// Report migrations only for an existing database that needs schema changes.
    pub fn new_with_migration_callback(
        path: Option<PathBuf>,
        on_migration: impl FnOnce(),
    ) -> Result<Self, rusqlite::Error> {
        let db_path = path.unwrap_or_else(Self::default_path);
        let existed = db_path.is_file();
        if let Some(parent) = db_path.parent() {
            let _ = std::fs::create_dir_all(parent);
        }

        let conn = Connection::open(&db_path)?;
        if existed && Self::needs_migration(&conn)? {
            on_migration();
        }
        let db = Self {
            conn: Arc::new(Mutex::new(conn)),
            db_path,
        };

        db.init_schema()?;
        Ok(db)
    }

    fn needs_migration(conn: &Connection) -> Result<bool, rusqlite::Error> {
        let table_count: i64 = conn.query_row(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table'
             AND name IN ('Albums', 'Songs', 'Playlists', 'PlaylistSongs', 'Queue', 'SongSettings', 'LibraryFileIssues', 'LibraryMigrations')",
            [], |row| row.get(0),
        )?;
        if table_count != 8 { return Ok(true); }
        let mut pragma = conn.prepare("PRAGMA table_info(Songs)")?;
        let columns = pragma.query_map([], |row| row.get::<_, String>(1))?
            .collect::<Result<Vec<_>, _>>()?;
        if SONG_COLUMNS.iter().any(|(required, _)| !columns.iter().any(|column| column.eq_ignore_ascii_case(required))) {
            return Ok(true);
        }
        let marker_labels_applied: bool = conn.query_row("SELECT EXISTS(SELECT 1 FROM LibraryMigrations WHERE Id = ?1)",
            [MARKER_LABEL_MIGRATION], |row| row.get(0))?;
        Ok(!marker_labels_applied)
    }

    pub fn path(&self) -> &Path {
        &self.db_path
    }

    /// Ensure tables and columns exist while maintaining 100% backward compatibility
    fn init_schema(&self) -> Result<(), rusqlite::Error> {
        let mut connection = self.conn.lock().unwrap();

        // Enforce SQLite foreign key cascades
        connection.execute("PRAGMA foreign_keys = ON;", [])?;
        let transaction = connection.transaction()?;
        let conn = &transaction;
        conn.execute_batch("CREATE TABLE IF NOT EXISTS LibraryMigrations (
            Id TEXT PRIMARY KEY,
            AppliedAt TEXT NOT NULL
        );")?;
        let aliases_applied: bool = conn.query_row("SELECT EXISTS(SELECT 1 FROM LibraryMigrations WHERE Id = 'song-column-aliases-v1')",
            [], |row| row.get(0))?;

        // 1. Create Albums table
        conn.execute(
            "CREATE TABLE IF NOT EXISTS Albums (
                Id TEXT PRIMARY KEY,
                Title TEXT NOT NULL UNIQUE,
                Artist TEXT,
                CoverArtPath TEXT,
                ReleaseYear INTEGER,
                DateAdded TEXT NOT NULL
            );",
            [],
        )?;

        // 2. Create Songs table (matches original AutoMidiPlayer.db)
        conn.execute(
            "CREATE TABLE IF NOT EXISTS Songs (
                Id TEXT PRIMARY KEY,
                BaseKey INTEGER,
                Key INTEGER NOT NULL DEFAULT 0,
                Path TEXT NOT NULL UNIQUE,
                FileHash TEXT,
                Title TEXT,
                Artist TEXT,
                Album TEXT,
                AlbumId TEXT,
                DateAdded TEXT,
                Transpose INTEGER NOT NULL DEFAULT 0,
                Speed REAL DEFAULT 1.0,
                Bpm REAL,
                DisabledTracks TEXT,
                MergeNotes INTEGER,
                MergeMilliseconds INTEGER,
                HoldNotes INTEGER,
                TapThresholdMs INTEGER DEFAULT 0,
                ImagePath TEXT,
                CachedDurationMs INTEGER,
                CachedNativeBpm REAL,
                AutoChord INTEGER,
                ChordDetectionMilliseconds INTEGER,
                KeyChanges TEXT,
                EnableKeyChangeMarkers INTEGER DEFAULT 1,
                FOREIGN KEY (AlbumId) REFERENCES Albums(Id) ON DELETE SET NULL
            );",
            [],
        )?;

        // 3. Upgrade in place without replacing IDs, relationships, or song data.
        let mut columns = {
            let mut pragma = conn.prepare("PRAGMA table_info(Songs)")?;
            let rows = pragma.query_map([], |row| row.get::<_, String>(1))?;
            rows.collect::<Result<Vec<_>, _>>()?
        };
        for (old, new) in [("Author", "Artist"), ("DefaultKey", "BaseKey"), ("DetectChordPads", "AutoChord")] {
            if let Some(index) = columns.iter().position(|c| c.eq_ignore_ascii_case(old)) {
                if columns.iter().any(|c| c.eq_ignore_ascii_case(new)) {
                    // A partial older upgrade may contain both; prefer the canonical value.
                    if !aliases_applied {
                        conn.execute(&format!("UPDATE Songs SET {new} = COALESCE({new}, {old}) WHERE {old} IS NOT NULL"), [])?;
                    }
                } else {
                    conn.execute(&format!("ALTER TABLE Songs RENAME COLUMN {old} TO {new}"), [])?;
                    columns[index] = new.into();
                }
            }
        }
        for (name, definition) in SONG_COLUMNS {
            if !columns.iter().any(|c| c.eq_ignore_ascii_case(name)) {
                conn.execute(&format!("ALTER TABLE Songs ADD COLUMN {name} {definition}"), [])?;
            }
        }

        // 4. Create Playlists table
        conn.execute(
            "CREATE TABLE IF NOT EXISTS Playlists (
                Id TEXT PRIMARY KEY,
                Title TEXT NOT NULL,
                Subtitle TEXT,
                Icon TEXT DEFAULT 'MusicNote2',
                Gradient TEXT,
                BannerColor TEXT,
                GameId TEXT,
                DateCreated TEXT NOT NULL
            );",
            [],
        )?;

        // 5. Create PlaylistSongs join table
        conn.execute(
            "CREATE TABLE IF NOT EXISTS PlaylistSongs (
                PlaylistId TEXT NOT NULL,
                SongId TEXT NOT NULL,
                SortOrder INTEGER NOT NULL DEFAULT 0,
                PRIMARY KEY (PlaylistId, SongId),
                FOREIGN KEY (PlaylistId) REFERENCES Playlists(Id) ON DELETE CASCADE,
                FOREIGN KEY (SongId) REFERENCES Songs(Id) ON DELETE CASCADE
            );",
            [],
        )?;

        // 6. Seed default playlists if table is empty
        let playlist_count: i64 = conn.query_row("SELECT COUNT(*) FROM Playlists", [], |r| r.get(0))?;
        if playlist_count == 0 {
            let now = Utc::now().to_rfc3339();
            let _ = conn.execute(
                "INSERT INTO Playlists (Id, Title, Subtitle, Icon, Gradient, BannerColor, DateCreated)
                 VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7)",
                params![
                    "liked-songs",
                    "Liked Songs",
                    "Favorite songs",
                    "Heart",
                    "linear-gradient(135deg, #450af5 0%, #8e8ee5 100%)",
                    "rgba(69, 10, 245, 0.45)",
                    now
                ],
            );
        }

        // 7. Create Queue table
        conn.execute(
            "CREATE TABLE IF NOT EXISTS Queue (
                SongId TEXT PRIMARY KEY,
                SortOrder INTEGER NOT NULL,
                FOREIGN KEY (SongId) REFERENCES Songs(Id) ON DELETE CASCADE
            );",
            [],
        )?;

        // Existing Songs columns remain the global defaults for old libraries.
        conn.execute_batch("CREATE TABLE IF NOT EXISTS SongSettings (
            SongId TEXT NOT NULL REFERENCES Songs(Id) ON DELETE CASCADE,
            GameId TEXT NOT NULL,
            InstrumentId TEXT NOT NULL DEFAULT '',
            Settings TEXT NOT NULL,
            PRIMARY KEY (SongId, GameId, InstrumentId)
        );")?;
        conn.execute_batch("CREATE TABLE IF NOT EXISTS LibraryFileIssues (
            Path TEXT PRIMARY KEY,
            Data TEXT NOT NULL
        );")?;
        conn.execute("INSERT OR IGNORE INTO LibraryMigrations (Id, AppliedAt) VALUES ('song-column-aliases-v1', ?1)",
            [Utc::now().to_rfc3339()])?;
        Self::migrate_marker_labels(conn)?;
        transaction.commit()
    }

    /// Applies inside the schema transaction, including to profiles copied by
    /// the previous migration version. Existing per-instrument offsets win.
    fn migrate_marker_labels(conn: &Connection) -> Result<(), rusqlite::Error> {
        let applied: bool = conn.query_row("SELECT EXISTS(SELECT 1 FROM LibraryMigrations WHERE Id = ?1)",
            [MARKER_LABEL_MIGRATION], |row| row.get(0))?;
        if applied { return Ok(()); }
        let suffix = regex::Regex::new(r"\s*\(([+-]?\d+)\)\s*$").unwrap();
        let songs = {
            let mut stmt = conn.prepare("SELECT Id, KeyChanges FROM Songs WHERE KeyChanges IS NOT NULL")?;
            let rows = stmt.query_map([], |row| Ok((row.get::<_, String>(0)?, row.get::<_, String>(1)?)))?;
            rows.collect::<Result<Vec<_>, _>>()?
        };
        for (id, json) in songs {
            if let Some(markers) = normalize_marker_labels(&json, &suffix) {
                conn.execute("UPDATE Songs SET KeyChanges = ?1 WHERE Id = ?2", params![markers, id])?;
            }
        }
        let profiles = {
            let mut stmt = conn.prepare("SELECT SongId, GameId, InstrumentId, Settings FROM SongSettings")?;
            let rows = stmt.query_map([], |row| Ok((row.get::<_, String>(0)?, row.get::<_, String>(1)?,
                row.get::<_, String>(2)?, row.get::<_, String>(3)?)))?;
            rows.collect::<Result<Vec<_>, _>>()?
        };
        for (song, game, instrument, json) in profiles {
            let Ok(mut settings) = serde_json::from_str::<Value>(&json) else { continue; };
            let Some(stored) = settings.get_mut("keyChanges") else { continue; };
            let Some(markers) = stored.as_str().and_then(|json| normalize_marker_labels(json, &suffix)) else { continue; };
            *stored = Value::String(markers);
            conn.execute("UPDATE SongSettings SET Settings = ?1 WHERE SongId = ?2 AND GameId = ?3 AND InstrumentId = ?4",
                params![settings.to_string(), song, game, instrument])?;
        }
        conn.execute("INSERT INTO LibraryMigrations (Id, AppliedAt) VALUES (?1, ?2)",
            params![MARKER_LABEL_MIGRATION, Utc::now().to_rfc3339()])?;
        Ok(())
    }

    /// Copy legacy technical settings once to every existing instrument. Shared
    /// metadata stays in Songs, which also remains the explicit global baseline.
    /// Run only after all game configs have loaded successfully; an empty target
    /// list leaves the upgrade pending so unavailable configs can be retried.
    pub fn migrate_legacy_song_settings(
        &self,
        contexts: &[SongSettingsContext],
        on_migration: impl FnOnce(),
    ) -> Result<usize, rusqlite::Error> {
        if contexts.is_empty() { return Ok(0); }
        for context in contexts {
            SongSettingsScope::Instrument { game_id: context.game_id.clone(), instrument_id: context.instrument_id.clone() }
                .validate().map_err(rusqlite::Error::InvalidParameterName)?;
        }
        let mut conn = self.conn.lock().unwrap();
        let transaction = conn.transaction()?;
        let applied: bool = transaction.query_row("SELECT EXISTS(SELECT 1 FROM LibraryMigrations WHERE Id = ?1)",
            [LEGACY_SONG_SETTINGS_MIGRATION], |row| row.get(0))?;
        if applied { return Ok(0); }
        on_migration();
        let songs = {
            let mut stmt = transaction.prepare("SELECT Id, BaseKey, Key, Transpose, Speed, Bpm, DisabledTracks,
                MergeNotes, MergeMilliseconds, HoldNotes, TapThresholdMs, AutoChord,
                ChordDetectionMilliseconds, KeyChanges, EnableKeyChangeMarkers FROM Songs")?;
            let rows = stmt.query_map([], |row| Ok((row.get::<_, String>(0)?, serde_json::json!({
                "baseKey": row.get::<_, Option<i32>>(1)?, "key": row.get::<_, i32>(2)?,
                "transpose": row.get::<_, Option<i32>>(3)?.unwrap_or(0), "speed": row.get::<_, Option<f64>>(4)?,
                "bpm": row.get::<_, Option<f64>>(5)?, "disabledTracks": row.get::<_, Option<String>>(6)?,
                "mergeNotes": row.get::<_, Option<bool>>(7)?, "mergeMilliseconds": row.get::<_, Option<u32>>(8)?,
                "holdNotes": row.get::<_, Option<bool>>(9)?, "tapThresholdMs": row.get::<_, Option<u32>>(10)?,
                "autoChord": row.get::<_, Option<bool>>(11)?, "chordDetectionMilliseconds": row.get::<_, Option<u32>>(12)?,
                "keyChanges": row.get::<_, Option<String>>(13)?, "enableKeyChangeMarkers": row.get::<_, Option<bool>>(14)?,
            }))))?;
            rows.collect::<Result<Vec<_>, _>>()?
        };
        let mut count = 0;
        for context in contexts {
            for (id, legacy) in &songs {
                let mut settings = legacy.clone();
                // Preserve newer game/instrument overrides, including explicit nulls.
                let mut stmt = transaction.prepare("SELECT Settings FROM SongSettings WHERE SongId = ?1
                    AND GameId = ?2 AND (InstrumentId = '' OR InstrumentId = ?3) ORDER BY (InstrumentId = '') DESC")?;
                let rows = stmt.query_map(params![id, context.game_id, context.instrument_id], |row| row.get::<_, String>(0))?;
                for row in rows {
                    let patch: Value = serde_json::from_str(&row?).map_err(|e| rusqlite::Error::ToSqlConversionFailure(Box::new(e)))?;
                    if !patch.is_object() { return Err(rusqlite::Error::InvalidParameterName("SongSettings must contain an object".into())); }
                    merge_json_object(&mut settings, &patch);
                }
                settings.as_object_mut().unwrap().retain(|key, _| TECHNICAL_SONG_FIELDS.contains(&key.as_str()));
                transaction.execute("INSERT INTO SongSettings (SongId, GameId, InstrumentId, Settings) VALUES (?1, ?2, ?3, ?4)
                    ON CONFLICT (SongId, GameId, InstrumentId) DO UPDATE SET Settings = excluded.Settings",
                    params![id, context.game_id, context.instrument_id, settings.to_string()])?;
                count += 1;
            }
        }
        transaction.execute("INSERT INTO LibraryMigrations (Id, AppliedAt) VALUES (?1, ?2)",
            params![LEGACY_SONG_SETTINGS_MIGRATION, Utc::now().to_rfc3339()])?;
        transaction.commit()?;
        Ok(count)
    }

    /// Resolve global defaults, then game settings, then instrument settings.
    pub fn resolve_song_settings(&self, songs: Vec<SongRecord>, context: Option<&SongSettingsContext>) -> Result<Vec<SongRecord>, rusqlite::Error> {
        let Some(context) = context else { return Ok(songs); };
        let conn = self.conn.lock().unwrap();
        let mut stmt = conn.prepare("SELECT SongId, Settings FROM SongSettings
            WHERE GameId = ?1 AND (InstrumentId = '' OR InstrumentId = ?2)
            ORDER BY (InstrumentId = '') DESC")?;
        let mut patches = std::collections::HashMap::<String, Vec<Value>>::new();
        for row in stmt.query_map(params![context.game_id, context.instrument_id], |row| {
            Ok((row.get::<_, String>(0)?, row.get::<_, String>(1)?))
        })? {
            let (id, json) = row?;
            let patch = serde_json::from_str(&json).map_err(|e| rusqlite::Error::ToSqlConversionFailure(Box::new(e)))?;
            patches.entry(id).or_default().push(patch);
        }
        songs.into_iter().map(|song| {
            let Some(patches) = patches.get(&song.id) else { return Ok(song); };
            let mut resolved = serde_json::to_value(song).map_err(|e| rusqlite::Error::ToSqlConversionFailure(Box::new(e)))?;
            for patch in patches { merge_json_object(&mut resolved, patch); }
            serde_json::from_value(resolved).map_err(|e| rusqlite::Error::ToSqlConversionFailure(Box::new(e)))
        }).collect()
    }

    pub fn update_song_settings_scoped(&self, id: &str, settings: &SongSettingsUpdate, scope: &SongSettingsScope) -> Result<(), rusqlite::Error> {
        let mut patch = serde_json::json!({
            "key": settings.key, "baseKey": settings.base_key, "transpose": settings.transpose,
            "disabledTracks": settings.disabled_tracks, "keyChanges": settings.key_changes,
        });
        for (key, value) in [
            ("speed", settings.speed.map(Value::from)),
            ("holdNotes", settings.hold_notes.map(Value::from)),
            ("tapThresholdMs", settings.tap_threshold_ms.map(Value::from)),
            ("autoChord", settings.auto_chord.map(Value::from)),
            ("chordDetectionMilliseconds", settings.chord_detection_milliseconds.map(Value::from)),
            ("enableKeyChangeMarkers", settings.enable_key_change_markers.map(Value::from)),
        ] {
            if let Some(value) = value { patch[key] = value; }
        }
        let mut conn = self.conn.lock().unwrap();
        let tx = conn.transaction()?;
        Self::write_song_settings_patch(&tx, id, scope, &patch)?;
        tx.commit()
    }

    pub fn update_song_details_scoped(&self, id: &str, details: &SongDetailsUpdate, album_id: Option<&str>, scope: &SongSettingsScope) -> Result<(), rusqlite::Error> {
        let mut conn = self.conn.lock().unwrap();
        let tx = conn.transaction()?;
        let changed = tx.execute("UPDATE Songs SET Title = ?1, Artist = NULLIF(?2, ''), Album = NULLIF(?3, ''),
            AlbumId = ?4, DateAdded = ?5 WHERE Id = ?6",
            params![details.title, details.artist, details.album, album_id, details.date_added, id])?;
        if changed == 0 { return Err(rusqlite::Error::QueryReturnedNoRows); }
        let patch = serde_json::json!({
            "baseKey": details.base_key, "key": details.key, "transpose": details.transpose,
            "bpm": details.bpm, "speed": details.speed, "mergeNotes": details.merge_notes,
            "mergeMilliseconds": details.merge_milliseconds, "holdNotes": details.hold_notes,
            "tapThresholdMs": details.tap_threshold_ms,
        });
        Self::write_song_settings_patch(&tx, id, scope, &patch)?;
        tx.commit()
    }

    fn write_song_settings_patch(conn: &Connection, id: &str, scope: &SongSettingsScope, patch: &Value) -> Result<(), rusqlite::Error> {
        scope.validate().map_err(|e| rusqlite::Error::InvalidParameterName(e))?;
        let exists: bool = conn.query_row("SELECT EXISTS(SELECT 1 FROM Songs WHERE Id = ?1)", [id], |row| row.get(0))?;
        if !exists { return Err(rusqlite::Error::QueryReturnedNoRows); }
        let (game_id, instrument_id) = match scope {
            SongSettingsScope::Global => ("", ""),
            SongSettingsScope::Game { game_id } => (game_id.as_str(), ""),
            SongSettingsScope::Instrument { game_id, instrument_id } => (game_id.as_str(), instrument_id.as_str()),
        };
        if matches!(scope, SongSettingsScope::Global) {
            // Keys are internal, typed payload fields; identifiers never come from user input.
            for (key, value) in patch.as_object().unwrap() {
                let sql_value = match value {
                    Value::Null => rusqlite::types::Value::Null,
                    Value::Bool(v) => rusqlite::types::Value::Integer(i64::from(*v)),
                    Value::Number(v) => rusqlite::types::Value::Real(v.as_f64().unwrap()),
                    Value::String(v) => rusqlite::types::Value::Text(v.clone()),
                    _ => return Err(rusqlite::Error::InvalidParameterName(key.clone())),
                };
                conn.execute(&format!("UPDATE Songs SET {key} = ?1 WHERE Id = ?2"), params![sql_value, id])?;
            }
        } else {
            let stored: Option<String> = conn.query_row("SELECT Settings FROM SongSettings WHERE SongId = ?1 AND GameId = ?2 AND InstrumentId = ?3",
                params![id, game_id, instrument_id], |row| row.get(0)).optional()?;
            let mut settings = stored.map(|json| serde_json::from_str::<Value>(&json))
                .transpose().map_err(|e| rusqlite::Error::ToSqlConversionFailure(Box::new(e)))?
                .unwrap_or_else(|| serde_json::json!({}));
            merge_json_object(&mut settings, patch);
            conn.execute("INSERT INTO SongSettings (SongId, GameId, InstrumentId, Settings) VALUES (?1, ?2, ?3, ?4)
                ON CONFLICT (SongId, GameId, InstrumentId) DO UPDATE SET Settings = excluded.Settings",
                params![id, game_id, instrument_id, settings.to_string()])?;
        }
        // A broad save applies its edited fields to all instruments in that scope.
        // Keep unrelated Studio settings (tracks and key markers) intact.
        if !matches!(scope, SongSettingsScope::Instrument { .. }) {
            let mut stmt = conn.prepare("SELECT GameId, InstrumentId, Settings FROM SongSettings
                WHERE SongId = ?1 AND (?2 = '' OR (GameId = ?2 AND InstrumentId != ''))")?;
            let rows = stmt.query_map(params![id, game_id], |row| Ok((row.get::<_, String>(0)?, row.get::<_, String>(1)?, row.get::<_, String>(2)?)))?
                .collect::<Result<Vec<_>, _>>()?;
            for (game, instrument, json) in rows {
                let mut settings: Value = serde_json::from_str(&json).map_err(|e| rusqlite::Error::ToSqlConversionFailure(Box::new(e)))?;
                for key in patch.as_object().unwrap().keys() { settings.as_object_mut().unwrap().remove(key); }
                if settings.as_object().unwrap().is_empty() {
                    conn.execute("DELETE FROM SongSettings WHERE SongId = ?1 AND GameId = ?2 AND InstrumentId = ?3", params![id, game, instrument])?;
                } else {
                    conn.execute("UPDATE SongSettings SET Settings = ?4 WHERE SongId = ?1 AND GameId = ?2 AND InstrumentId = ?3", params![id, game, instrument, settings.to_string()])?;
                }
            }
        }
        Ok(())
    }

    // Read once when moving preferences from older rewrite builds to settings.json.
    pub fn legacy_app_settings(&self) -> Result<Option<Value>, rusqlite::Error> {
        let conn = self.conn.lock().unwrap();
        let exists: bool = conn.query_row("SELECT EXISTS(SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'AppSettings')", [], |row| row.get(0))?;
        if !exists { return Ok(None); }
        let raw: Option<String> = conn.query_row("SELECT Value FROM AppSettings WHERE Key = 'renderer'", [], |row| row.get(0)).optional()?;
        raw.map(|raw| {
            let value: Value = serde_json::from_str(&raw).map_err(|e| rusqlite::Error::ToSqlConversionFailure(Box::new(e)))?;
            if !value.is_object() { return Err(rusqlite::Error::InvalidParameterName("Legacy preferences must contain an object".into())); }
            Ok(value)
        }).transpose()
    }

    pub fn remove_legacy_app_settings(&self) -> Result<(), rusqlite::Error> {
        self.conn.lock().unwrap().execute_batch("DROP TABLE IF EXISTS AppSettings")
    }

    pub fn canonical_song_id(&self, id: &str) -> Result<Option<String>, rusqlite::Error> {
        self.conn.lock().unwrap().query_row("SELECT Id FROM Songs WHERE lower(Id) = lower(?1)", [id], |row| row.get(0)).optional()
    }

    /// Preserve an existing rewrite queue. Legacy GUIDs may use different casing.
    pub fn import_legacy_queue(&self, ids: &str) -> Result<(), rusqlite::Error> {
        let mut conn = self.conn.lock().unwrap();
        let transaction = conn.transaction()?;
        let count: i64 = transaction.query_row("SELECT COUNT(*) FROM Queue", [], |row| row.get(0))?;
        if count == 0 {
            for (index, id) in ids.split([',', ';']).map(str::trim).filter(|id| !id.is_empty()).enumerate() {
                transaction.execute("INSERT OR IGNORE INTO Queue (SongId, SortOrder) SELECT Id, ?2 FROM Songs WHERE lower(Id) = lower(?1)", params![id, index as i64])?;
            }
        }
        transaction.commit()
    }

    /// Retrieve all songs from database with active missing-file check
    pub fn get_all_songs(&self) -> Result<Vec<SongRecord>, rusqlite::Error> {
        let conn = self.conn.lock().unwrap();
        let mut stmt = conn.prepare(
            "SELECT Id, BaseKey, Key, Path, FileHash, Title, Artist, Album, AlbumId, DateAdded,
                    Transpose, Speed, Bpm, DisabledTracks, MergeNotes, MergeMilliseconds, HoldNotes,
                    ImagePath, CachedDurationMs, CachedNativeBpm, AutoChord, ChordDetectionMilliseconds,
                    KeyChanges, EnableKeyChangeMarkers, TapThresholdMs
             FROM Songs
             ORDER BY DateAdded DESC",
        )?;

        let rows = stmt.query_map([], |row| {
            let path_str: String = row.get(3)?;
            let is_missing = !Path::new(&path_str).exists();

            Ok(SongRecord {
                id: row.get(0)?,
                base_key: row.get(1)?,
                key: row.get(2)?,
                path: path_str,
                file_hash: row.get(4)?,
                title: row.get(5)?,
                artist: row.get(6)?,
                album: row.get(7)?,
                album_id: row.get(8)?,
                date_added: row.get(9)?,
                transpose: row.get::<_, Option<i32>>(10)?.unwrap_or(0),
                speed: row.get(11)?,
                bpm: row.get(12)?,
                disabled_tracks: row.get(13)?,
                merge_notes: row.get::<_, Option<i32>>(14)?.map(|v| v != 0),
                merge_milliseconds: row.get(15)?,
                hold_notes: row.get::<_, Option<i32>>(16)?.map(|v| v != 0),
                tap_threshold_ms: row.get(24)?,
                image_path: row.get(17)?,
                cached_duration_ms: row.get(18)?,
                cached_native_bpm: row.get(19)?,
                auto_chord: row.get::<_, Option<i32>>(20)?.map(|v| v != 0),
                chord_detection_milliseconds: row.get(21)?,
                key_changes: row.get(22)?,
                enable_key_change_markers: row.get::<_, Option<i32>>(23)?.map(|v| v != 0),
                is_missing,
            })
        })?;

        let mut songs = Vec::new();
        for r in rows {
            songs.push(r?);
        }
        Ok(songs)
    }

    /// Find song by ID
    pub fn get_song_by_id(&self, id: &str) -> Result<Option<SongRecord>, rusqlite::Error> {
        let conn = self.conn.lock().unwrap();
        let mut stmt = conn.prepare(
            "SELECT Id, BaseKey, Key, Path, FileHash, Title, Artist, Album, AlbumId, DateAdded,
                    Transpose, Speed, Bpm, DisabledTracks, MergeNotes, MergeMilliseconds, HoldNotes,
                    ImagePath, CachedDurationMs, CachedNativeBpm, AutoChord, ChordDetectionMilliseconds,
                    KeyChanges, EnableKeyChangeMarkers, TapThresholdMs
             FROM Songs
             WHERE Id = ?1",
        )?;

        let song = stmt.query_row([id], |row| {
            let path_str: String = row.get(3)?;
            let is_missing = !Path::new(&path_str).exists();

            Ok(SongRecord {
                id: row.get(0)?,
                base_key: row.get(1)?,
                key: row.get(2)?,
                path: path_str,
                file_hash: row.get(4)?,
                title: row.get(5)?,
                artist: row.get(6)?,
                album: row.get(7)?,
                album_id: row.get(8)?,
                date_added: row.get(9)?,
                transpose: row.get::<_, Option<i32>>(10)?.unwrap_or(0),
                speed: row.get(11)?,
                bpm: row.get(12)?,
                disabled_tracks: row.get(13)?,
                merge_notes: row.get::<_, Option<i32>>(14)?.map(|v| v != 0),
                merge_milliseconds: row.get(15)?,
                hold_notes: row.get::<_, Option<i32>>(16)?.map(|v| v != 0),
                tap_threshold_ms: row.get(24)?,
                image_path: row.get(17)?,
                cached_duration_ms: row.get(18)?,
                cached_native_bpm: row.get(19)?,
                auto_chord: row.get::<_, Option<i32>>(20)?.map(|v| v != 0),
                chord_detection_milliseconds: row.get(21)?,
                key_changes: row.get(22)?,
                enable_key_change_markers: row.get::<_, Option<i32>>(23)?.map(|v| v != 0),
                is_missing,
            })
        }).optional()?;

        Ok(song)
    }

    /// Find song by file path
    pub fn get_song_by_path(&self, path: &str) -> Result<Option<SongRecord>, rusqlite::Error> {
        let conn = self.conn.lock().unwrap();
        let mut stmt = conn.prepare(
            "SELECT Id, BaseKey, Key, Path, FileHash, Title, Artist, Album, AlbumId, DateAdded,
                    Transpose, Speed, Bpm, DisabledTracks, MergeNotes, MergeMilliseconds, HoldNotes,
                    ImagePath, CachedDurationMs, CachedNativeBpm, AutoChord, ChordDetectionMilliseconds,
                    KeyChanges, EnableKeyChangeMarkers, TapThresholdMs
             FROM Songs
             WHERE Path = ?1",
        )?;

        let song = stmt.query_row([path], |row| {
            let path_str: String = row.get(3)?;
            let is_missing = !Path::new(&path_str).exists();

            Ok(SongRecord {
                id: row.get(0)?,
                base_key: row.get(1)?,
                key: row.get(2)?,
                path: path_str,
                file_hash: row.get(4)?,
                title: row.get(5)?,
                artist: row.get(6)?,
                album: row.get(7)?,
                album_id: row.get(8)?,
                date_added: row.get(9)?,
                transpose: row.get::<_, Option<i32>>(10)?.unwrap_or(0),
                speed: row.get(11)?,
                bpm: row.get(12)?,
                disabled_tracks: row.get(13)?,
                merge_notes: row.get::<_, Option<i32>>(14)?.map(|v| v != 0),
                merge_milliseconds: row.get(15)?,
                hold_notes: row.get::<_, Option<i32>>(16)?.map(|v| v != 0),
                tap_threshold_ms: row.get(24)?,
                image_path: row.get(17)?,
                cached_duration_ms: row.get(18)?,
                cached_native_bpm: row.get(19)?,
                auto_chord: row.get::<_, Option<i32>>(20)?.map(|v| v != 0),
                chord_detection_milliseconds: row.get(21)?,
                key_changes: row.get(22)?,
                enable_key_change_markers: row.get::<_, Option<i32>>(23)?.map(|v| v != 0),
                is_missing,
            })
        }).optional()?;

        Ok(song)
    }

    /// Find song by SHA-256 hash
    pub fn get_song_by_hash(&self, hash: &str) -> Result<Option<SongRecord>, rusqlite::Error> {
        let conn = self.conn.lock().unwrap();
        let mut stmt = conn.prepare(
            "SELECT Id, BaseKey, Key, Path, FileHash, Title, Artist, Album, AlbumId, DateAdded,
                    Transpose, Speed, Bpm, DisabledTracks, MergeNotes, MergeMilliseconds, HoldNotes,
                    ImagePath, CachedDurationMs, CachedNativeBpm, AutoChord, ChordDetectionMilliseconds,
                    KeyChanges, EnableKeyChangeMarkers, TapThresholdMs
             FROM Songs
             WHERE FileHash = ?1",
        )?;

        let song = stmt.query_row([hash], |row| {
            let path_str: String = row.get(3)?;
            let is_missing = !Path::new(&path_str).exists();

            Ok(SongRecord {
                id: row.get(0)?,
                base_key: row.get(1)?,
                key: row.get(2)?,
                path: path_str,
                file_hash: row.get(4)?,
                title: row.get(5)?,
                artist: row.get(6)?,
                album: row.get(7)?,
                album_id: row.get(8)?,
                date_added: row.get(9)?,
                transpose: row.get::<_, Option<i32>>(10)?.unwrap_or(0),
                speed: row.get(11)?,
                bpm: row.get(12)?,
                disabled_tracks: row.get(13)?,
                merge_notes: row.get::<_, Option<i32>>(14)?.map(|v| v != 0),
                merge_milliseconds: row.get(15)?,
                hold_notes: row.get::<_, Option<i32>>(16)?.map(|v| v != 0),
                tap_threshold_ms: row.get(24)?,
                image_path: row.get(17)?,
                cached_duration_ms: row.get(18)?,
                cached_native_bpm: row.get(19)?,
                auto_chord: row.get::<_, Option<i32>>(20)?.map(|v| v != 0),
                chord_detection_milliseconds: row.get(21)?,
                key_changes: row.get(22)?,
                enable_key_change_markers: row.get::<_, Option<i32>>(23)?.map(|v| v != 0),
                is_missing,
            })
        }).optional()?;

        Ok(song)
    }

    /// Insert a new song or update an existing one
    pub fn upsert_song(&self, song: &SongRecord) -> Result<(), rusqlite::Error> {
        let conn = self.conn.lock().unwrap();
        // Legacy EF databases only guarantee that Id is unique; Path has no
        // UNIQUE constraint. Resolve the existing path's ID in the same statement
        // so rescanning preserves references without requiring a schema rebuild.
        conn.execute(
            "INSERT INTO Songs (
                Id, BaseKey, Key, Path, FileHash, Title, Artist, Album, AlbumId, DateAdded,
                Transpose, Speed, Bpm, DisabledTracks, MergeNotes, MergeMilliseconds, HoldNotes,
                ImagePath, CachedDurationMs, CachedNativeBpm, AutoChord, ChordDetectionMilliseconds,
                KeyChanges, EnableKeyChangeMarkers, TapThresholdMs
            ) VALUES (
                COALESCE((SELECT Id FROM Songs WHERE Path = ?4 LIMIT 1), ?1),
                ?2, ?3, ?4, ?5, ?6, ?7, ?8, ?9, ?10,
                ?11, ?12, ?13, ?14, ?15, ?16, ?17,
                ?18, ?19, ?20, ?21, ?22,
                ?23, ?24, ?25
            )
            ON CONFLICT(Id) DO UPDATE SET
                Path = excluded.Path,
                FileHash = excluded.FileHash,
                Title = COALESCE(excluded.Title, Songs.Title),
                Artist = COALESCE(excluded.Artist, Songs.Artist),
                Album = COALESCE(excluded.Album, Songs.Album),
                AlbumId = COALESCE(excluded.AlbumId, Songs.AlbumId),
                CachedDurationMs = COALESCE(excluded.CachedDurationMs, Songs.CachedDurationMs),
                CachedNativeBpm = COALESCE(excluded.CachedNativeBpm, Songs.CachedNativeBpm)",
            params![
                song.id,
                song.base_key,
                song.key,
                song.path,
                song.file_hash,
                song.title,
                song.artist,
                song.album,
                song.album_id,
                song.date_added,
                song.transpose,
                song.speed,
                song.bpm,
                song.disabled_tracks,
                song.merge_notes.map(|b| if b { 1 } else { 0 }),
                song.merge_milliseconds,
                song.hold_notes.map(|b| if b { 1 } else { 0 }),
                song.image_path,
                song.cached_duration_ms,
                song.cached_native_bpm,
                song.auto_chord.map(|b| if b { 1 } else { 0 }),
                song.chord_detection_milliseconds,
                song.key_changes,
                song.enable_key_change_markers.map(|b| if b { 1 } else { 0 }),
                song.tap_threshold_ms,
            ],
        )?;

        Ok(())
    }

    /// Relink a moved file without replacing its song ID or cascading saved settings.
    pub fn update_song_path(&self, id: &str, path: &str) -> Result<(), rusqlite::Error> {
        let conn = self.conn.lock().unwrap();
        conn.execute("UPDATE Songs SET Path = ?1 WHERE Id = ?2", params![path, id])?;
        Ok(())
    }

    /// Auto-persist per-song settings (key, transpose, disabled tracks, key changes)
    pub fn update_song_settings(&self, id: &str, settings: &SongSettingsUpdate) -> Result<(), rusqlite::Error> {
        let conn = self.conn.lock().unwrap();
        conn.execute(
            "UPDATE Songs SET
                Key = ?1,
                BaseKey = ?2,
                Transpose = ?3,
                DisabledTracks = ?4,
                KeyChanges = ?5,
                Speed = COALESCE(?6, Speed),
                HoldNotes = COALESCE(?7, HoldNotes),
                AutoChord = COALESCE(?8, AutoChord),
                EnableKeyChangeMarkers = COALESCE(?9, EnableKeyChangeMarkers),
                TapThresholdMs = COALESCE(?10, TapThresholdMs),
                ChordDetectionMilliseconds = COALESCE(?11, ChordDetectionMilliseconds)
             WHERE Id = ?12",
            params![
                settings.key,
                settings.base_key,
                settings.transpose,
                settings.disabled_tracks,
                settings.key_changes,
                settings.speed,
                settings.hold_notes.map(|b| if b { 1 } else { 0 }),
                settings.auto_chord.map(|b| if b { 1 } else { 0 }),
                settings.enable_key_change_markers.map(|b| if b { 1 } else { 0 }),
                settings.tap_threshold_ms,
                settings.chord_detection_milliseconds,
                id
            ],
        )?;
        Ok(())
    }

    /// Explicit save from Edit Song Details dialog
    pub fn update_song_metadata(&self, id: &str, metadata: &SongMetadataUpdate) -> Result<(), rusqlite::Error> {
        let conn = self.conn.lock().unwrap();
        conn.execute(
            "UPDATE Songs SET
                Title = COALESCE(?1, Title),
                Artist = COALESCE(?2, Artist),
                Album = COALESCE(?3, Album),
                AlbumId = COALESCE(?4, AlbumId),
                Bpm = COALESCE(?5, Bpm)
             WHERE Id = ?6",
            params![
                metadata.title,
                metadata.artist,
                metadata.album,
                metadata.album_id,
                metadata.bpm,
                id
            ],
        )?;
        Ok(())
    }

    /// Save the complete editor draft atomically, including clearing a BPM override.
    pub fn update_song_details(&self, id: &str, details: &SongDetailsUpdate, album_id: Option<&str>) -> Result<(), rusqlite::Error> {
        let conn = self.conn.lock().unwrap();
        let changed = conn.execute(
            "UPDATE Songs SET Title = ?1, Artist = NULLIF(?2, ''), Album = NULLIF(?3, ''),
                AlbumId = ?4, BaseKey = ?5, Key = ?6, Transpose = ?7, Bpm = ?8,
                Speed = ?9, MergeNotes = ?10, MergeMilliseconds = ?11, HoldNotes = ?12,
                DateAdded = ?13, TapThresholdMs = ?15 WHERE Id = ?14",
            params![details.title, details.artist, details.album, album_id, details.base_key,
                details.key, details.transpose, details.bpm, details.speed, details.merge_notes,
                details.merge_milliseconds, details.hold_notes, details.date_added, id, details.tap_threshold_ms],
        )?;
        if changed == 0 { return Err(rusqlite::Error::QueryReturnedNoRows); }
        Ok(())
    }

    /// Delete song from database
    pub fn delete_song(&self, id: &str) -> Result<(), rusqlite::Error> {
        let conn = self.conn.lock().unwrap();
        conn.execute("DELETE FROM Songs WHERE Id = ?1", [id])?;
        Ok(())
    }

    // --- Albums ---

    /// Gets or creates album by title and returns its ID
    pub fn get_or_create_album(&self, title: &str, artist: Option<&str>) -> Result<String, rusqlite::Error> {
        let conn = self.conn.lock().unwrap();
        let existing: Option<String> = conn
            .query_row("SELECT Id FROM Albums WHERE Title = ?1", [title], |r| r.get(0))
            .optional()?;

        if let Some(id) = existing {
            return Ok(id);
        }

        let new_id = Uuid::new_v4().to_string();
        let now = Utc::now().to_rfc3339();
        conn.execute(
            "INSERT INTO Albums (Id, Title, Artist, DateAdded) VALUES (?1, ?2, ?3, ?4)",
            params![new_id, title, artist, now],
        )?;

        Ok(new_id)
    }

    pub fn get_albums(&self) -> Result<Vec<AlbumRecord>, rusqlite::Error> {
        let conn = self.conn.lock().unwrap();
        let mut stmt = conn.prepare("SELECT Id, Title, Artist, CoverArtPath, ReleaseYear, DateAdded FROM Albums ORDER BY Title ASC")?;
        let rows = stmt.query_map([], |row| {
            Ok(AlbumRecord {
                id: row.get(0)?,
                title: row.get(1)?,
                artist: row.get(2)?,
                cover_art_path: row.get(3)?,
                release_year: row.get(4)?,
                date_added: row.get(5)?,
            })
        })?;

        let mut albums = Vec::new();
        for r in rows {
            albums.push(r?);
        }
        Ok(albums)
    }

    // --- Playlists ---

    pub fn get_playlists(&self) -> Result<Vec<PlaylistRecord>, rusqlite::Error> {
        let conn = self.conn.lock().unwrap();
        let mut stmt = conn.prepare(
            "SELECT p.Id, p.Title, p.Subtitle, p.Icon, p.Gradient, p.BannerColor, p.GameId, p.DateCreated,
                    COUNT(ps.SongId) as SongCount
             FROM Playlists p
             LEFT JOIN PlaylistSongs ps ON p.Id = ps.PlaylistId
             GROUP BY p.Id
             ORDER BY p.DateCreated ASC",
        )?;

        let rows = stmt.query_map([], |row| {
            Ok(PlaylistRecord {
                id: row.get(0)?,
                title: row.get(1)?,
                subtitle: row.get(2)?,
                icon: row.get(3)?,
                gradient: row.get(4)?,
                banner_color: row.get(5)?,
                game_id: row.get(6)?,
                date_created: row.get(7)?,
                song_count: row.get::<_, i64>(8)? as usize,
            })
        })?;

        let mut playlists = Vec::new();
        for r in rows {
            playlists.push(r?);
        }
        Ok(playlists)
    }

    pub fn create_playlist(&self, title: &str, icon: Option<&str>, banner_color: Option<&str>) -> Result<PlaylistRecord, rusqlite::Error> {
        let conn = self.conn.lock().unwrap();
        let id = Uuid::new_v4().to_string();
        let now = Utc::now().to_rfc3339();
        let icon_str = icon.unwrap_or("MusicNote2");
        let gradient_str = "linear-gradient(135deg, #1DB954 0%, #0d9488 100%)";
        let banner_str = banner_color.unwrap_or("rgba(29, 185, 84, 0.45)");

        conn.execute(
            "INSERT INTO Playlists (Id, Title, Subtitle, Icon, Gradient, BannerColor, DateCreated)
             VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7)",
            params![id, title, "Custom playlist", icon_str, gradient_str, banner_str, now],
        )?;

        Ok(PlaylistRecord {
            id,
            title: title.to_string(),
            subtitle: Some("Custom playlist".to_string()),
            icon: Some(icon_str.to_string()),
            gradient: Some(gradient_str.to_string()),
            banner_color: Some(banner_str.to_string()),
            game_id: None,
            date_created: now,
            song_count: 0,
        })
    }

    pub fn delete_playlist(&self, id: &str) -> Result<(), rusqlite::Error> {
        let conn = self.conn.lock().unwrap();
        conn.execute("DELETE FROM Playlists WHERE Id = ?1", [id])?;
        Ok(())
    }

    pub fn add_to_playlist(&self, playlist_id: &str, song_id: &str) -> Result<(), rusqlite::Error> {
        let conn = self.conn.lock().unwrap();
        let max_order: i32 = conn
            .query_row(
                "SELECT COALESCE(MAX(SortOrder), 0) FROM PlaylistSongs WHERE PlaylistId = ?1",
                [playlist_id],
                |r| r.get(0),
            )?;
        let next_order = max_order + 1;

        conn.execute(
            "INSERT OR IGNORE INTO PlaylistSongs (PlaylistId, SongId, SortOrder) VALUES (?1, ?2, ?3)",
            params![playlist_id, song_id, next_order],
        )?;
        Ok(())
    }

    pub fn remove_from_playlist(&self, playlist_id: &str, song_id: &str) -> Result<(), rusqlite::Error> {
        let conn = self.conn.lock().unwrap();
        conn.execute(
            "DELETE FROM PlaylistSongs WHERE PlaylistId = ?1 AND SongId = ?2",
            params![playlist_id, song_id],
        )?;
        Ok(())
    }

    pub fn get_playlist_songs(&self, playlist_id: &str) -> Result<Vec<SongRecord>, rusqlite::Error> {
        let conn = self.conn.lock().unwrap();
        let mut stmt = conn.prepare(
            "SELECT s.Id, s.BaseKey, s.Key, s.Path, s.FileHash, s.Title, s.Artist, s.Album, s.AlbumId, s.DateAdded,
                    s.Transpose, s.Speed, s.Bpm, s.DisabledTracks, s.MergeNotes, s.MergeMilliseconds, s.HoldNotes,
                    s.ImagePath, s.CachedDurationMs, s.CachedNativeBpm, s.AutoChord, s.ChordDetectionMilliseconds,
                    s.KeyChanges, s.EnableKeyChangeMarkers, s.TapThresholdMs
             FROM Songs s
             INNER JOIN PlaylistSongs ps ON s.Id = ps.SongId
             WHERE ps.PlaylistId = ?1
             ORDER BY ps.SortOrder ASC",
        )?;

        let rows = stmt.query_map([playlist_id], |row| {
            let path_str: String = row.get(3)?;
            let is_missing = !Path::new(&path_str).exists();

            Ok(SongRecord {
                id: row.get(0)?,
                base_key: row.get(1)?,
                key: row.get(2)?,
                path: path_str,
                file_hash: row.get(4)?,
                title: row.get(5)?,
                artist: row.get(6)?,
                album: row.get(7)?,
                album_id: row.get(8)?,
                date_added: row.get(9)?,
                transpose: row.get::<_, Option<i32>>(10)?.unwrap_or(0),
                speed: row.get(11)?,
                bpm: row.get(12)?,
                disabled_tracks: row.get(13)?,
                merge_notes: row.get::<_, Option<i32>>(14)?.map(|v| v != 0),
                merge_milliseconds: row.get(15)?,
                hold_notes: row.get::<_, Option<i32>>(16)?.map(|v| v != 0),
                tap_threshold_ms: row.get(24)?,
                image_path: row.get(17)?,
                cached_duration_ms: row.get(18)?,
                cached_native_bpm: row.get(19)?,
                auto_chord: row.get::<_, Option<i32>>(20)?.map(|v| v != 0),
                chord_detection_milliseconds: row.get(21)?,
                key_changes: row.get(22)?,
                enable_key_change_markers: row.get::<_, Option<i32>>(23)?.map(|v| v != 0),
                is_missing,
            })
        })?;

        let mut songs = Vec::new();
        for r in rows {
            songs.push(r?);
        }
        Ok(songs)
    }

    // --- Queue ---

    pub fn get_queue_songs(&self) -> Result<Vec<SongRecord>, rusqlite::Error> {
        let conn = self.conn.lock().unwrap();
        let mut stmt = conn.prepare(
            "SELECT s.Id, s.BaseKey, s.Key, s.Path, s.FileHash, s.Title, s.Artist, s.Album, s.AlbumId, s.DateAdded,
                    s.Transpose, s.Speed, s.Bpm, s.DisabledTracks, s.MergeNotes, s.MergeMilliseconds, s.HoldNotes,
                    s.ImagePath, s.CachedDurationMs, s.CachedNativeBpm, s.AutoChord, s.ChordDetectionMilliseconds,
                    s.KeyChanges, s.EnableKeyChangeMarkers, s.TapThresholdMs
             FROM Songs s
             INNER JOIN Queue q ON s.Id = q.SongId
             ORDER BY q.SortOrder ASC",
        )?;

        let rows = stmt.query_map([], |row| {
            let path_str: String = row.get(3)?;
            let is_missing = !Path::new(&path_str).exists();

            Ok(SongRecord {
                id: row.get(0)?,
                base_key: row.get(1)?,
                key: row.get(2)?,
                path: path_str,
                file_hash: row.get(4)?,
                title: row.get(5)?,
                artist: row.get(6)?,
                album: row.get(7)?,
                album_id: row.get(8)?,
                date_added: row.get(9)?,
                transpose: row.get::<_, Option<i32>>(10)?.unwrap_or(0),
                speed: row.get(11)?,
                bpm: row.get(12)?,
                disabled_tracks: row.get(13)?,
                merge_notes: row.get::<_, Option<i32>>(14)?.map(|v| v != 0),
                merge_milliseconds: row.get(15)?,
                hold_notes: row.get::<_, Option<i32>>(16)?.map(|v| v != 0),
                tap_threshold_ms: row.get(24)?,
                image_path: row.get(17)?,
                cached_duration_ms: row.get(18)?,
                cached_native_bpm: row.get(19)?,
                auto_chord: row.get::<_, Option<i32>>(20)?.map(|v| v != 0),
                chord_detection_milliseconds: row.get(21)?,
                key_changes: row.get(22)?,
                enable_key_change_markers: row.get::<_, Option<i32>>(23)?.map(|v| v != 0),
                is_missing,
            })
        })?;

        let mut songs = Vec::new();
        for r in rows {
            songs.push(r?);
        }
        Ok(songs)
    }

    pub fn set_queue(&self, song_ids: &[String]) -> Result<(), rusqlite::Error> {
        let mut conn = self.conn.lock().unwrap();
        let tx = conn.transaction()?;
        tx.execute("DELETE FROM Queue", [])?;
        {
            let mut stmt = tx.prepare("INSERT OR REPLACE INTO Queue (SongId, SortOrder) VALUES (?1, ?2)")?;
            for (idx, song_id) in song_ids.iter().enumerate() {
                stmt.execute(params![song_id, idx as i32])?;
            }
        }
        tx.commit()?;
        Ok(())
    }

    pub fn add_to_queue(&self, song_ids: &[String]) -> Result<(), rusqlite::Error> {
        let mut conn = self.conn.lock().unwrap();
        let max_order: i32 = conn
            .query_row("SELECT COALESCE(MAX(SortOrder), 0) FROM Queue", [], |r| r.get(0))?;

        let tx = conn.transaction()?;
        {
            let mut stmt = tx.prepare("INSERT OR REPLACE INTO Queue (SongId, SortOrder) VALUES (?1, ?2)")?;
            for (idx, song_id) in song_ids.iter().enumerate() {
                stmt.execute(params![song_id, max_order + 1 + (idx as i32)])?;
            }
        }
        tx.commit()?;
        Ok(())
    }

    pub fn remove_from_queue(&self, song_ids: &[String]) -> Result<(), rusqlite::Error> {
        let mut conn = self.conn.lock().unwrap();
        let tx = conn.transaction()?;
        {
            let mut stmt = tx.prepare("DELETE FROM Queue WHERE SongId = ?1")?;
            for song_id in song_ids {
                stmt.execute([song_id])?;
            }
        }
        tx.commit()?;
        Ok(())
    }

    pub fn clear_queue(&self) -> Result<(), rusqlite::Error> {
        let conn = self.conn.lock().unwrap();
        conn.execute("DELETE FROM Queue", [])?;
        Ok(())
    }

    pub fn reorder_queue(&self, song_ids: &[String]) -> Result<(), rusqlite::Error> {
        self.set_queue(song_ids)
    }
}
