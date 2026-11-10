use amp_core::{LibraryDatabase, settings::SettingsStore};
use rusqlite::Connection;
use serde_json::json;
use std::collections::HashMap;

#[test]
fn clear_data_empties_library_and_preferences_without_deleting_source_files_or_reimporting_legacy_settings() {
    let root = std::env::temp_dir().join(format!("amp_clear_data_{}", uuid::Uuid::new_v4()));
    let db = LibraryDatabase::new(Some(root.join("library.db"))).unwrap();
    let original = root.join("original.mid");
    std::fs::write(&original, b"original MIDI").unwrap();
    let conn = Connection::open(db.path()).unwrap();
    conn.execute_batch("INSERT INTO Albums (Id, Title, DateAdded) VALUES ('album', 'Album', 'now');
        INSERT INTO Songs (Id, Path, AlbumId) VALUES ('song', 'original.mid', 'album');
        INSERT INTO SongSettings VALUES ('song', 'game', 'instrument', '{\"key\":5}');
        INSERT INTO Queue VALUES ('song', 0);
        INSERT INTO Playlists (Id, Title, DateCreated) VALUES ('custom', 'Custom', 'now');
        INSERT INTO PlaylistSongs VALUES ('custom', 'song', 0);
        INSERT INTO PlaylistSongs VALUES ('liked-songs', 'song', 0);
        INSERT INTO LibraryFileIssues VALUES ('missing.mid', '{}');").unwrap();
    let settings_path = root.join("settings.json");
    // Simulate the hidden raw preferences kept by older migration versions.
    std::fs::write(&settings_path, br#"{"onboardingCompleted":true,"_legacyUserConfig":{"Theme":"Dark"},"_migrations":{"userConfig":true}}"#).unwrap();
    let settings = SettingsStore::open(settings_path.clone(), &db, &[], &HashMap::new()).unwrap();
    settings.update(&json!({ "midiFolder": "outside", "ui": { "lastViewedPage": "settings" } })).unwrap();
    db.clear_data().unwrap();
    settings.clear_data().unwrap();
    for table in ["Songs", "Albums", "Queue", "PlaylistSongs", "SongSettings", "LibraryFileIssues"] {
        assert_eq!(conn.query_row(&format!("SELECT COUNT(*) FROM {table}"), [], |row| row.get::<_, i64>(0)).unwrap(), 0);
    }
    assert_eq!(db.get_playlists().unwrap().len(), 1);
    assert_eq!(db.get_playlists().unwrap()[0].id, "liked-songs");
    assert_eq!(settings.get().unwrap(), SettingsStore::defaults());
    assert_eq!(settings.get().unwrap()["onboardingCompleted"], false);
    let persisted: serde_json::Value = serde_json::from_slice(&std::fs::read(&settings_path).unwrap()).unwrap();
    assert!(persisted.get("_legacyUserConfig").is_none());
    assert_eq!(persisted["_migrations"]["userConfig"], true);
    drop(settings);
    let reopened = SettingsStore::open(settings_path, &db, &[], &HashMap::new()).unwrap();
    assert_eq!(reopened.get().unwrap(), SettingsStore::defaults());
    assert_eq!(std::fs::read(original).unwrap(), b"original MIDI");
    // Resetting again is safe, and the retained connection remains usable.
    db.clear_data().unwrap();
    db.create_playlist("After reset", None, None).unwrap();
    drop(reopened);
    drop(conn);
    drop(db);
    std::fs::remove_dir_all(root).unwrap();
}
