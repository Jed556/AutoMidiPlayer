use amp_core::LibraryDatabase;
use amp_core::library::models::SongSettingsContext;
use rusqlite::{params, Connection};
use serde_json::{json, Value};
use std::cell::Cell;
use std::path::PathBuf;

struct Fixture { directory: PathBuf, path: PathBuf }

impl Fixture {
    fn legacy(columns: &str) -> Self {
        let directory = std::env::temp_dir().join(format!("amp_legacy_profiles_{}", uuid::Uuid::new_v4()));
        std::fs::create_dir_all(&directory).unwrap();
        let path = directory.join("AutoMidiPlayer.db");
        let conn = Connection::open(&path).unwrap();
        // EF only constrained the ID, not the path. Missing optional columns
        // exercise the complete additive upgrade instead of a fresh schema.
        conn.execute_batch(&format!("CREATE TABLE Songs (Id TEXT PRIMARY KEY, Path TEXT NOT NULL, {columns});
            INSERT INTO Songs (Id, Path) VALUES ('song', 'song.mid');")).unwrap();
        Self { directory, path }
    }

    fn conn(&self) -> Connection { Connection::open(&self.path).unwrap() }
    fn open(&self) -> LibraryDatabase { LibraryDatabase::new(Some(self.path.clone())).unwrap() }
    fn profile(&self, context: &SongSettingsContext) -> Value {
        let raw: String = self.conn().query_row("SELECT Settings FROM SongSettings WHERE SongId = 'song' AND GameId = ?1 AND InstrumentId = ?2",
            params![context.game_id, context.instrument_id], |row| row.get(0)).unwrap();
        serde_json::from_str(&raw).unwrap()
    }
}

impl Drop for Fixture {
    fn drop(&mut self) { let _ = std::fs::remove_dir_all(&self.directory); }
}

fn context(game: &str, instrument: &str) -> SongSettingsContext {
    SongSettingsContext { game_id: game.into(), instrument_id: instrument.into() }
}

#[test]
fn canonical_auto_chord_libraries_need_no_detect_chord_pads_column() {
    let f = Fixture::legacy("AutoChord INTEGER, Transpose INTEGER");
    f.conn().execute("UPDATE Songs SET AutoChord = 1", []).unwrap();
    let reported = Cell::new(0);
    for _ in 0..2 {
        let db = LibraryDatabase::new_with_migration_callback(Some(f.path.clone()), || reported.set(reported.get() + 1)).unwrap();
        let song = db.get_song_by_id("song").unwrap().unwrap();
        assert_eq!(song.auto_chord, Some(true));
        assert_eq!(song.transpose, 0, "Legacy null transpose means Ignore");
        assert_eq!(song.tap_threshold_ms, Some(0));
        assert_eq!(db.get_all_songs().unwrap().len(), 1);
        assert_eq!(db.get_song_by_path("song.mid").unwrap().unwrap().id, "song");
        db.set_queue(&["song".to_string()]).unwrap();
        assert_eq!(db.get_queue_songs().unwrap()[0].auto_chord, Some(true));
        assert_eq!(reported.get(), 1);
    }
    let columns: Vec<String> = f.conn().prepare("PRAGMA table_info(Songs)").unwrap()
        .query_map([], |row| row.get(1)).unwrap().map(Result::unwrap).collect();
    assert!(!columns.iter().any(|c| c == "DetectChordPads"));
}

#[test]
fn old_column_aliases_are_preserved_under_the_current_names() {
    let f = Fixture::legacy("Author TEXT, DefaultKey INTEGER, DetectChordPads INTEGER");
    f.conn().execute("UPDATE Songs SET Author = 'Artist', DefaultKey = -3, DetectChordPads = 1", []).unwrap();
    let db = f.open();
    let song = db.get_song_by_id("song").unwrap().unwrap();
    assert_eq!(song.artist.as_deref(), Some("Artist"));
    assert_eq!(song.base_key, Some(-3));
    assert_eq!(song.auto_chord, Some(true));
}

#[test]
fn partial_alias_upgrades_fill_only_missing_values_and_do_not_restore_cleared_values() {
    let f = Fixture::legacy("AutoChord INTEGER, DetectChordPads INTEGER, Artist TEXT, Author TEXT, BaseKey INTEGER, DefaultKey INTEGER");
    f.conn().execute("UPDATE Songs SET AutoChord = 0, DetectChordPads = 1, Author = 'Old', DefaultKey = 3", []).unwrap();
    drop(f.open());
    let song = f.open().get_song_by_id("song").unwrap().unwrap();
    assert_eq!(song.auto_chord, Some(false));
    assert_eq!(song.artist.as_deref(), Some("Old"));
    assert_eq!(song.base_key, Some(3));
    f.conn().execute("UPDATE Songs SET BaseKey = NULL, Artist = NULL", []).unwrap();
    let song = f.open().get_song_by_id("song").unwrap().unwrap();
    assert_eq!(song.base_key, None);
    assert_eq!(song.artist, None);
}

#[test]
fn every_existing_instrument_gets_technical_settings_with_newer_overrides_preserved() {
    let f = Fixture::legacy("AutoChord INTEGER");
    let db = f.open();
    let markers = r#"[{"TimeMs":1200,"KeyOffset":3,"Transpose":2,"Label":"Chorus"}]"#;
    f.conn().execute("UPDATE Songs SET Title = 'Shared title', Artist = 'Shared artist', Album = 'Shared album',
        BaseKey = 2, Key = -1, Transpose = 3, Speed = 1.25, Bpm = 110, DisabledTracks = '0,2',
        MergeNotes = 1, MergeMilliseconds = 40, HoldNotes = 1, TapThresholdMs = 60, AutoChord = 1,
        ChordDetectionMilliseconds = 45, KeyChanges = ?1, EnableKeyChangeMarkers = 0", [markers]).unwrap();
    f.conn().execute("INSERT INTO SongSettings VALUES ('song', 'a', '', ?1)", [json!({"speed": 1.5, "bpm": null}).to_string()]).unwrap();
    f.conn().execute("INSERT INTO SongSettings VALUES ('song', 'a', 'piano', ?1)",
        [json!({"key": 7, "keyChanges": null, "disabledTracks": null, "autoChord": false}).to_string()]).unwrap();
    let contexts = vec![context("a", "lyre"), context("a", "piano"), context("b", "piano")];
    assert_eq!(db.migrate_legacy_song_settings(&contexts, || {}).unwrap(), 3);
    let baseline = json!({
        "baseKey": 2, "key": -1, "transpose": 3, "speed": 1.25, "bpm": 110.0,
        "disabledTracks": "0,2", "mergeNotes": true, "mergeMilliseconds": 40, "holdNotes": true,
        "tapThresholdMs": 60, "autoChord": true, "chordDetectionMilliseconds": 45,
        "keyChanges": markers, "enableKeyChangeMarkers": false,
    });
    assert_eq!(f.profile(&contexts[2]), baseline);
    let mut inherited = baseline.clone();
    inherited["speed"] = json!(1.5);
    inherited["bpm"] = Value::Null;
    assert_eq!(f.profile(&contexts[0]), inherited);
    inherited["key"] = json!(7);
    inherited["keyChanges"] = Value::Null;
    inherited["disabledTracks"] = Value::Null;
    inherited["autoChord"] = json!(false);
    assert_eq!(f.profile(&contexts[1]), inherited);
    let resolved = db.resolve_song_settings(db.get_all_songs().unwrap(), Some(&contexts[0])).unwrap();
    assert_eq!(resolved[0].title.as_deref(), Some("Shared title"));
    assert_eq!(resolved[0].key_changes.as_deref(), Some(markers));
    assert_eq!(resolved[0].speed, Some(1.5));
    // Reopening must neither overwrite edits nor resurrect profiles deleted by
    // a later broad save, nor copy settings to newly added instruments.
    f.conn().execute("DELETE FROM SongSettings WHERE InstrumentId = 'lyre'", []).unwrap();
    f.conn().execute("UPDATE Songs SET Speed = 0.5, KeyChanges = NULL", []).unwrap();
    let mut later_contexts = contexts;
    later_contexts.push(context("a", "new-instrument"));
    assert_eq!(f.open().migrate_legacy_song_settings(&later_contexts, || panic!("already applied")).unwrap(), 0);
    assert_eq!(f.conn().query_row("SELECT COUNT(*) FROM SongSettings WHERE InstrumentId IN ('lyre', 'new-instrument')", [], |r| r.get::<_, i64>(0)).unwrap(), 0);
}

#[test]
fn unavailable_targets_and_failed_copies_remain_retryable_without_partial_writes() {
    let f = Fixture::legacy("AutoChord INTEGER");
    let db = f.open();
    assert_eq!(db.migrate_legacy_song_settings(&[], || panic!("no targets")).unwrap(), 0);
    let contexts = vec![context("a", "lyre"), context("a", "piano")];
    f.conn().execute("INSERT INTO SongSettings VALUES ('song', 'a', 'piano', 'invalid json')", []).unwrap();
    assert!(db.migrate_legacy_song_settings(&contexts, || {}).is_err());
    assert_eq!(f.conn().query_row("SELECT COUNT(*) FROM SongSettings", [], |r| r.get::<_, i64>(0)).unwrap(), 1);
    f.conn().execute("DELETE FROM SongSettings", []).unwrap();
    assert_eq!(db.migrate_legacy_song_settings(&contexts, || {}).unwrap(), 2);
    assert_eq!(db.migrate_legacy_song_settings(&contexts, || panic!("already applied")).unwrap(), 0);
}

#[test]
fn legacy_marker_labels_become_text_while_offsets_and_other_marker_fields_are_preserved() {
    let f = Fixture::legacy("KeyChanges TEXT");
    let original = json!([
        {"Label": "A♯0 (-26)", "KeyOffset": -26, "TimeMs": 183013, "Transpose": 2, "custom": {"keep": 7}},
        {"Label": "D4 (+14)", "KeyOffset": 14, "TimeMs": 10748, "Transpose": null},
        {"Label": "C3 (+0)", "KeyOffset": 0, "TimeMs": 0},
        {"Label": "A♯0 (-26) (-26)", "KeyOffset": -26, "TimeMs": 2500},
        {"Label": "Verse 2 (3)", "KeyOffset": -26, "TimeMs": 5000},
        {"Label": "Chorus", "KeyOffset": 3, "TimeMs": 6000},
        {"Label": "(-26)", "KeyOffset": -26, "TimeMs": 7000},
        {"Label": "Unknown (+2)", "TimeMs": 8000},
    ]);
    f.conn().execute("UPDATE Songs SET KeyChanges = ?1", [original.to_string()]).unwrap();
    let db = f.open();
    let mut expected = original;
    for (index, label) in [(0, "A♯0"), (1, "D4"), (2, "C3"), (3, "A♯0")] {
        expected[index]["Label"] = json!(label);
    }
    let song = db.get_song_by_id("song").unwrap().unwrap();
    assert_eq!(serde_json::from_str::<Value>(song.key_changes.as_ref().unwrap()).unwrap(), expected);
    let ctx = context("game", "instrument");
    db.migrate_legacy_song_settings(&[ctx.clone()], || {}).unwrap();
    assert_eq!(serde_json::from_str::<Value>(f.profile(&ctx)["keyChanges"].as_str().unwrap()).unwrap(), expected);
}

#[test]
fn already_migrated_profiles_get_label_cleanup_without_resetting_instrument_edits() {
    let f = Fixture::legacy("KeyChanges TEXT");
    let db = f.open();
    let global = json!([{"Label": "A♯0 (-26)", "KeyOffset": -26, "TimeMs": 183013, "Transpose": null}]);
    f.conn().execute("UPDATE Songs SET KeyChanges = ?1", [global.to_string()]).unwrap();
    let ctx = context("a", "lyre");
    db.migrate_legacy_song_settings(&[ctx.clone()], || {}).unwrap();
    let scoped = json!({
        "speed": 1.75, "disabledTracks": "0,2", "unknown": {"keep": true},
        "keyChanges": json!([{"Label": "D4 (+14)", "KeyOffset": 14, "TimeMs": 10748, "Transpose": 3}]).to_string(),
    });
    f.conn().execute("UPDATE SongSettings SET Settings = ?1", [scoped.to_string()]).unwrap();
    f.conn().execute("INSERT INTO SongSettings VALUES ('song', 'a', '', ?1)", [scoped.to_string()]).unwrap();
    f.conn().execute("INSERT INTO SongSettings VALUES ('song', 'a', 'cleared', ?1)", [json!({"keyChanges": null}).to_string()]).unwrap();
    // Simulate a database that already completed the earlier profile migration.
    f.conn().execute("DELETE FROM LibraryMigrations WHERE Id = 'marker-label-offsets-v1'", []).unwrap();
    let reported = Cell::new(0);
    let reopened = LibraryDatabase::new_with_migration_callback(Some(f.path.clone()), || reported.set(reported.get() + 1)).unwrap();
    assert_eq!(reported.get(), 1);
    let mut expected = scoped;
    let mut markers: Value = serde_json::from_str(expected["keyChanges"].as_str().unwrap()).unwrap();
    markers[0]["Label"] = json!("D4");
    expected["keyChanges"] = json!(markers.to_string());
    assert_eq!(f.profile(&ctx), expected);
    assert_eq!(f.profile(&context("a", "")), expected);
    assert_eq!(f.profile(&context("a", "cleared")), json!({"keyChanges": null}));
    assert_eq!(reopened.migrate_legacy_song_settings(&[ctx.clone()], || panic!("do not re-copy profiles")).unwrap(), 0);
    // Completion is recorded once, and reopening does not rewrite new edits.
    let before: String = f.conn().query_row("SELECT AppliedAt FROM LibraryMigrations WHERE Id = 'marker-label-offsets-v1'", [], |row| row.get(0)).unwrap();
    drop(LibraryDatabase::new_with_migration_callback(Some(f.path.clone()), || panic!("already migrated")).unwrap());
    let after: String = f.conn().query_row("SELECT AppliedAt FROM LibraryMigrations WHERE Id = 'marker-label-offsets-v1'", [], |row| row.get(0)).unwrap();
    assert_eq!(before, after);
}

#[test]
fn malformed_marker_payloads_are_left_untouched() {
    let f = Fixture::legacy("KeyChanges TEXT");
    f.conn().execute("UPDATE Songs SET KeyChanges = 'invalid json'", []).unwrap();
    let db = f.open();
    f.conn().execute("INSERT INTO SongSettings VALUES ('song', 'a', 'lyre', ?1)", [json!({"keyChanges": "invalid json", "speed": 1.5}).to_string()]).unwrap();
    f.conn().execute("INSERT INTO SongSettings VALUES ('song', 'a', 'piano', 'invalid json')", []).unwrap();
    f.conn().execute("DELETE FROM LibraryMigrations WHERE Id = 'marker-label-offsets-v1'", []).unwrap();
    drop(db);
    let db = f.open();
    assert_eq!(db.get_song_by_id("song").unwrap().unwrap().key_changes.as_deref(), Some("invalid json"));
    assert_eq!(f.profile(&context("a", "lyre")), json!({"keyChanges": "invalid json", "speed": 1.5}));
    let raw: String = f.conn().query_row("SELECT Settings FROM SongSettings WHERE InstrumentId = 'piano'", [], |row| row.get(0)).unwrap();
    assert_eq!(raw, "invalid json");
}
