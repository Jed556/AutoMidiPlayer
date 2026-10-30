use amp_core::LibraryDatabase;
use rusqlite::Connection;
use std::cell::Cell;

#[test]
fn only_existing_outdated_databases_report_migration_and_keep_settings() {
    let directory = std::env::temp_dir().join(format!("amp_migration_{}", uuid::Uuid::new_v4()));
    let path = directory.join("library.db");
    let reported = Cell::new(0);
    {
        let _db = LibraryDatabase::new_with_migration_callback(Some(path.clone()), || reported.set(reported.get() + 1)).unwrap();
        assert_eq!(reported.get(), 0, "A fresh installation is not a migration");
        let conn = Connection::open(&path).unwrap();
        conn.execute_batch("CREATE TABLE AppSettings (Key TEXT PRIMARY KEY, Value TEXT NOT NULL, UpdatedAt TEXT NOT NULL);
            INSERT INTO AppSettings VALUES ('renderer', '{\"volume\":42}', '2026-10-30');").unwrap();
    }
    // Recreate a small legacy Songs table without AlbumId and keep its records.
    {
        let conn = Connection::open(&path).unwrap();
        conn.execute_batch("DROP TABLE Songs;
            CREATE TABLE Songs (Id TEXT PRIMARY KEY, Title TEXT, Path TEXT NOT NULL UNIQUE);
            INSERT INTO Songs VALUES ('legacy', 'Keep this song', 'legacy.mid');").unwrap();
    }
    {
        let db = LibraryDatabase::new_with_migration_callback(Some(path.clone()), || reported.set(reported.get() + 1)).unwrap();
        assert_eq!(reported.get(), 1);
        assert_eq!(db.legacy_app_settings().unwrap().unwrap()["volume"], 42);
        let conn = Connection::open(&path).unwrap();
        let title: String = conn.query_row("SELECT Title FROM Songs WHERE Id = 'legacy'", [], |row| row.get(0)).unwrap();
        assert_eq!(title, "Keep this song");
    }
    {
        let _db = LibraryDatabase::new_with_migration_callback(Some(path.clone()), || reported.set(reported.get() + 1)).unwrap();
        assert_eq!(reported.get(), 1, "Reopening the migrated library must skip the migration phase");
    }
    std::fs::remove_dir_all(directory).unwrap();
}

#[test]
fn existing_libraries_gain_a_zero_tap_threshold_without_changing_hold_notes() {
    let directory = std::env::temp_dir().join(format!("amp_tap_migration_{}", uuid::Uuid::new_v4()));
    let path = directory.join("library.db");
    drop(LibraryDatabase::new(Some(path.clone())).unwrap());
    {
        let conn = Connection::open(&path).unwrap();
        conn.execute_batch("ALTER TABLE Songs DROP COLUMN TapThresholdMs;
            INSERT INTO Songs (Id, Path, HoldNotes) VALUES ('song', 'song.mid', 1);").unwrap();
    }
    let reported = Cell::new(0);
    for _ in 0..2 {
        let db = LibraryDatabase::new_with_migration_callback(Some(path.clone()), || reported.set(reported.get() + 1)).unwrap();
        let song = db.get_song_by_id("song").unwrap().unwrap();
        assert_eq!(song.hold_notes, Some(true));
        assert_eq!(song.tap_threshold_ms, Some(0));
        assert_eq!(reported.get(), 1);
    }
    std::fs::remove_dir_all(directory).unwrap();
}
