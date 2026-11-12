use amp_core::library::{LibraryDatabase, SongRecord};

fn song(id: &str) -> SongRecord {
    serde_json::from_value(serde_json::json!({
        "id": id, "key": 0, "path": format!("{id}.mid"), "transpose": 1, "isMissing": true,
    })).unwrap()
}

fn ids(db: &LibraryDatabase, playlist: &str) -> Vec<String> {
    db.get_playlist_songs(playlist).unwrap().into_iter().map(|song| song.id).collect()
}

#[test]
fn playlist_reorder_persists_without_changing_membership_or_queue() {
    let directory = std::env::temp_dir().join(format!("amp-playlist-order-{}", uuid::Uuid::new_v4()));
    let path = directory.join("library.db");
    {
        let db = LibraryDatabase::new(Some(path.clone())).unwrap();
        for id in ["a", "missing", "b", "c"] {
            db.upsert_song(&song(id)).unwrap();
            db.add_to_playlist("liked-songs", id).unwrap();
        }
        db.set_queue(&["a".into(), "c".into()]).unwrap();
        db.reorder_playlist("liked-songs", &["c".into(), "b".into(), "a".into()]).unwrap();
        assert_eq!(ids(&db, "liked-songs"), ["c", "missing", "b", "a"]);
        assert_eq!(db.get_queue_songs().unwrap().iter().map(|song| song.id.as_str()).collect::<Vec<_>>(), ["a", "c"]);
        db.reorder_playlist("liked-songs", &[]).unwrap();
        assert_eq!(ids(&db, "liked-songs"), ["c", "missing", "b", "a"]);
    }
    let db = LibraryDatabase::new(Some(path)).unwrap();
    assert_eq!(ids(&db, "liked-songs"), ["c", "missing", "b", "a"]);
    db.add_to_playlist("liked-songs", "new").unwrap_err();
    drop(db);
    std::fs::remove_dir_all(directory).unwrap();
}

#[test]
fn invalid_reorder_rolls_back_and_does_not_import_foreign_songs() {
    let directory = std::env::temp_dir().join(format!("amp-invalid-order-{}", uuid::Uuid::new_v4()));
    let db = LibraryDatabase::new(Some(directory.join("library.db"))).unwrap();
    for id in ["a", "b", "foreign"] { db.upsert_song(&song(id)).unwrap(); }
    for id in ["a", "b"] { db.add_to_playlist("liked-songs", id).unwrap(); }
    for invalid in [vec!["a".into(), "a".into()], vec!["b".into(), "foreign".into()]] {
        assert!(db.reorder_playlist("liked-songs", &invalid).is_err());
        assert_eq!(ids(&db, "liked-songs"), ["a", "b"]);
    }
    assert!(db.reorder_playlist("absent", &["a".into()]).is_err());
    drop(db);
    std::fs::remove_dir_all(directory).unwrap();
}
