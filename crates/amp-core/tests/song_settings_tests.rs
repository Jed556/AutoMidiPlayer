use amp_core::{LibraryDatabase, SongRecord, SongSettingsUpdate};
use amp_core::library::models::{SongDetailsUpdate, SongSettingsContext, SongSettingsScope};

struct Fixture { directory: std::path::PathBuf, db: LibraryDatabase }

impl Fixture {
    fn new() -> Self {
        let directory = std::env::temp_dir().join(format!("amp_scoped_{}", uuid::Uuid::new_v4()));
        let db = LibraryDatabase::new(Some(directory.join("library.db"))).unwrap();
        let song: SongRecord = serde_json::from_value(serde_json::json!({
            "id": "song", "key": 1, "baseKey": 2, "transpose": 1,
            "path": "song.mid", "title": "Original", "speed": 1.0,
            "bpm": 130.0, "mergeNotes": false, "mergeMilliseconds": 25,
            "holdNotes": false, "isMissing": true,
        })).unwrap();
        db.upsert_song(&song).unwrap();
        Self { directory, db }
    }

    fn resolved(&self, game: &str, instrument: &str) -> SongRecord {
        let song = self.db.get_song_by_id("song").unwrap().unwrap();
        self.db.resolve_song_settings(vec![song], Some(&SongSettingsContext {
            game_id: game.into(), instrument_id: instrument.into(),
        })).unwrap().remove(0)
    }
}

impl Drop for Fixture {
    fn drop(&mut self) { let _ = std::fs::remove_dir_all(&self.directory); }
}

fn instrument(game: &str, instrument: &str) -> SongSettingsScope {
    SongSettingsScope::Instrument { game_id: game.into(), instrument_id: instrument.into() }
}

fn settings(key: i32) -> SongSettingsUpdate {
    SongSettingsUpdate {
        key, base_key: Some(2), transpose: 1, disabled_tracks: Some("0,2".into()),
        key_changes: Some("[]".into()), speed: Some(1.5), hold_notes: Some(true), tap_threshold_ms: Some(100),
        auto_chord: Some(true), enable_key_change_markers: Some(false),
    }
}

fn details(key: i32) -> SongDetailsUpdate {
    SongDetailsUpdate {
        title: "Edited".into(), artist: "Artist".into(), album: "".into(),
        base_key: None, key, transpose: 3, bpm: None, speed: 0.75,
        merge_notes: true, merge_milliseconds: 50, hold_notes: false, tap_threshold_ms: 0,
        date_added: "2026-10-23T00:00:00Z".into(),
    }
}

#[test]
fn instrument_settings_are_isolated_by_both_game_and_instrument() {
    let f = Fixture::new();
    f.db.update_song_settings_scoped("song", &settings(5), &instrument("game-a", "lyre")).unwrap();
    let saved = f.resolved("game-a", "lyre");
    assert_eq!(saved.key, 5);
    assert_eq!(saved.speed, Some(1.5));
    assert_eq!(saved.disabled_tracks.as_deref(), Some("0,2"));
    assert_eq!(saved.auto_chord, Some(true));
    assert_eq!(saved.enable_key_change_markers, Some(false));
    assert_eq!(f.resolved("game-a", "piano").key, 1);
    assert_eq!(f.resolved("game-b", "lyre").key, 1);
    assert_eq!(f.db.get_song_by_id("song").unwrap().unwrap().key, 1);
}

#[test]
fn game_save_applies_to_its_instruments_and_preserves_studio_only_fields() {
    let f = Fixture::new();
    for (game, inst) in [("a", "lyre"), ("a", "piano"), ("b", "lyre")] {
        f.db.update_song_settings_scoped("song", &settings(5), &instrument(game, inst)).unwrap();
    }
    f.db.update_song_details_scoped("song", &details(-3), None, &SongSettingsScope::Game { game_id: "a".into() }).unwrap();
    for inst in ["lyre", "piano", "new-instrument"] {
        let song = f.resolved("a", inst);
        assert_eq!(song.key, -3);
        assert_eq!(song.base_key, None);
        assert_eq!(song.bpm, None);
        assert_eq!(song.merge_notes, Some(true));
        assert_eq!(song.merge_milliseconds, Some(50));
        assert_eq!(song.speed, Some(0.75));
    }
    let lyre = f.resolved("a", "lyre");
    assert_eq!(lyre.disabled_tracks.as_deref(), Some("0,2"));
    assert_eq!(lyre.key_changes.as_deref(), Some("[]"));
    assert_eq!(lyre.auto_chord, Some(true));
    assert_eq!(f.resolved("b", "lyre").key, 5);
    let global = f.db.get_song_by_id("song").unwrap().unwrap();
    assert_eq!(global.key, 1);
    assert_eq!(global.title.as_deref(), Some("Edited"));
}

#[test]
fn global_save_applies_across_games_and_future_instruments() {
    let f = Fixture::new();
    f.db.update_song_details_scoped("song", &details(3), None, &SongSettingsScope::Game { game_id: "a".into() }).unwrap();
    f.db.update_song_settings_scoped("song", &settings(5), &instrument("a", "lyre")).unwrap();
    f.db.update_song_settings_scoped("song", &settings(7), &instrument("b", "lyre")).unwrap();
    f.db.update_song_details_scoped("song", &details(-2), None, &SongSettingsScope::Global).unwrap();
    for (game, inst) in [("a", "lyre"), ("a", "piano"), ("b", "lyre"), ("new", "new")] {
        assert_eq!(f.resolved(game, inst).key, -2);
        assert_eq!(f.resolved(game, inst).speed, Some(0.75));
    }
    assert_eq!(f.resolved("b", "lyre").disabled_tracks.as_deref(), Some("0,2"));
    let global = f.db.get_song_by_id("song").unwrap().unwrap();
    assert_eq!(global.key, -2);
    assert_eq!(global.base_key, None);
    assert_eq!(global.bpm, None);
}

#[test]
fn studio_updates_inherit_game_fields_and_can_clear_tracks_and_markers() {
    let f = Fixture::new();
    f.db.update_song_details_scoped("song", &details(3), None, &SongSettingsScope::Game { game_id: "a".into() }).unwrap();
    f.db.update_song_settings_scoped("song", &settings(5), &instrument("a", "lyre")).unwrap();
    let mut patch = settings(6);
    patch.disabled_tracks = None;
    patch.key_changes = None;
    patch.speed = None; // Preserve a value that is absent from an older Studio payload.
    f.db.update_song_settings_scoped("song", &patch, &instrument("a", "lyre")).unwrap();
    let song = f.resolved("a", "lyre");
    assert_eq!(song.disabled_tracks, None);
    assert_eq!(song.key_changes, None);
    assert_eq!(song.speed, Some(1.5));
    assert_eq!(song.merge_notes, Some(true));
    assert_eq!(song.merge_milliseconds, Some(50));
    assert_eq!(song.bpm, None);
    assert_eq!(f.resolved("a", "piano").key, 3);
}

#[test]
fn settings_survive_reopening_and_are_deleted_with_the_song() {
    let f = Fixture::new();
    f.db.update_song_settings_scoped("song", &settings(5), &instrument("a", "lyre")).unwrap();
    let reopened = LibraryDatabase::new(Some(f.directory.join("library.db"))).unwrap();
    let songs = reopened.resolve_song_settings(reopened.get_all_songs().unwrap(), Some(&SongSettingsContext {
        game_id: "a".into(), instrument_id: "lyre".into(),
    })).unwrap();
    assert_eq!(songs[0].key, 5);
    reopened.delete_song("song").unwrap();
    let conn = rusqlite::Connection::open(f.directory.join("library.db")).unwrap();
    assert_eq!(conn.query_row("SELECT COUNT(*) FROM SongSettings", [], |row| row.get::<_, i64>(0)).unwrap(), 0);
}

#[test]
fn invalid_scope_rolls_back_shared_details_and_missing_songs_cannot_get_profiles() {
    let f = Fixture::new();
    assert!(f.db.update_song_details_scoped("song", &details(3), None, &instrument("", "lyre")).is_err());
    assert_eq!(f.resolved("a", "lyre").title.as_deref(), Some("Original"));
    assert!(f.db.update_song_settings_scoped("missing", &settings(3), &instrument("a", "lyre")).is_err());
}

#[test]
fn adding_scoped_settings_reports_migration_without_changing_global_values() {
    let f = Fixture::new();
    let conn = rusqlite::Connection::open(f.directory.join("library.db")).unwrap();
    conn.execute("DROP TABLE SongSettings", []).unwrap();
    let migrated = std::cell::Cell::new(false);
    let db = LibraryDatabase::new_with_migration_callback(Some(f.directory.join("library.db")), || migrated.set(true)).unwrap();
    assert!(migrated.get());
    assert_eq!(db.get_all_songs().unwrap()[0].key, 1);
}

#[test]
fn tap_threshold_follows_scope_precedence_and_survives_reopening() {
    let f = Fixture::new();
    assert_eq!(f.resolved("a", "lyre").tap_threshold_ms, None);
    let mut global = details(0);
    global.tap_threshold_ms = 50;
    f.db.update_song_details_scoped("song", &global, None, &SongSettingsScope::Global).unwrap();
    let mut game = details(0);
    game.tap_threshold_ms = 100;
    f.db.update_song_details_scoped("song", &game, None, &SongSettingsScope::Game { game_id: "a".into() }).unwrap();
    let mut studio = settings(0);
    studio.tap_threshold_ms = Some(150);
    f.db.update_song_settings_scoped("song", &studio, &instrument("a", "lyre")).unwrap();
    assert_eq!(f.resolved("a", "lyre").tap_threshold_ms, Some(150));
    assert_eq!(f.resolved("a", "piano").tap_threshold_ms, Some(100));
    assert_eq!(f.resolved("b", "lyre").tap_threshold_ms, Some(50));
    let reopened = LibraryDatabase::new(Some(f.directory.join("library.db"))).unwrap();
    let songs = reopened.resolve_song_settings(reopened.get_all_songs().unwrap(), Some(&SongSettingsContext {
        game_id: "a".into(), instrument_id: "lyre".into(),
    })).unwrap();
    assert_eq!(songs[0].tap_threshold_ms, Some(150));
    f.db.update_song_details_scoped("song", &game, None, &SongSettingsScope::Game { game_id: "a".into() }).unwrap();
    assert_eq!(f.resolved("a", "lyre").tap_threshold_ms, Some(100));
    f.db.update_song_details_scoped("song", &global, None, &SongSettingsScope::Global).unwrap();
    assert_eq!(f.resolved("a", "lyre").tap_threshold_ms, Some(50));
}
