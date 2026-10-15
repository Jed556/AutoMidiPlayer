use amp_core::library::{
    LibraryDatabase, SongRecord, SongSettingsUpdate, SongMetadataUpdate,
    scan_folder, import_files
};

#[test]
fn test_library_schema_and_crud() {
    let temp_dir = std::env::temp_dir().join(format!("amp_test_{}", uuid::Uuid::new_v4()));
    let _ = std::fs::create_dir_all(&temp_dir);
    let db_path = temp_dir.join("test.db");

    let db = LibraryDatabase::new(Some(db_path)).expect("Failed to initialize DB");

    // 1. Initial songs count should be 0
    let songs = db.get_all_songs().expect("Failed to get songs");
    assert_eq!(songs.len(), 0);

    // 2. Insert test song
    let test_song = SongRecord {
        id: "test-song-1".to_string(),
        base_key: Some(0),
        key: 2,
        path: "C:\\Music\\test.mid".to_string(),
        file_hash: Some("abcdef123456".to_string()),
        title: Some("Test Song".to_string()),
        artist: Some("Test Artist".to_string()),
        album: Some("Test Album".to_string()),
        album_id: None,
        date_added: Some("2026-10-19T00:00:00Z".to_string()),
        transpose: 1,
        speed: Some(1.0),
        bpm: Some(120.0),
        disabled_tracks: None,
        merge_notes: None,
        merge_milliseconds: None,
        hold_notes: None,
        tap_threshold_ms: None,
        image_path: None,
        cached_duration_ms: Some(60000),
        cached_native_bpm: Some(120.0),
        auto_chord: None,
        chord_detection_milliseconds: None,
        key_changes: None,
        detect_chord_pads: None,
        enable_key_change_markers: Some(true),
        is_missing: true, // File does not actually exist on disk
    };

    db.upsert_song(&test_song).expect("Failed to upsert song");

    // 3. Fetch song by ID & Path
    let fetched = db.get_song_by_id("test-song-1").expect("Query failed").expect("Song not found");
    assert_eq!(fetched.title.as_deref(), Some("Test Song"));
    assert_eq!(fetched.key, 2);
    assert_eq!(fetched.is_missing, true);

    let by_path = db.get_song_by_path("C:\\Music\\test.mid").expect("Query failed").expect("Song not found");
    assert_eq!(by_path.id, "test-song-1");

    // 4. Update song settings (key, transpose, disabled_tracks, key_changes)
    let settings = SongSettingsUpdate {
        key: 5,
        base_key: Some(2),
        transpose: 2,
        disabled_tracks: Some("0,3".to_string()),
        key_changes: Some(r#"[{"TimeMs":1000,"KeyOffset":3,"Transpose":null,"Label":"Chorus"}]"#.to_string()),
        speed: Some(1.25),
        hold_notes: Some(true),
        tap_threshold_ms: Some(100),
        auto_chord: Some(true),
        enable_key_change_markers: Some(true),
    };
    db.update_song_settings("test-song-1", &settings).expect("Failed to update settings");

    let updated = db.get_song_by_id("test-song-1").unwrap().unwrap();
    assert_eq!(updated.key, 5);
    assert_eq!(updated.base_key, Some(2));
    assert_eq!(updated.transpose, 2);
    assert_eq!(updated.disabled_tracks.as_deref(), Some("0,3"));
    assert!(updated.key_changes.unwrap().contains("Chorus"));

    // 5. Update song metadata (title, artist, album, album_id, bpm)
    let album_id = db.get_or_create_album("Soundtrack Vol. 1", Some("Test Artist")).unwrap();
    let meta = SongMetadataUpdate {
        title: Some("Renamed Song".to_string()),
        artist: Some("New Artist".to_string()),
        album: Some("Soundtrack Vol. 1".to_string()),
        album_id: Some(album_id.clone()),
        bpm: Some(128.0),
    };
    db.update_song_metadata("test-song-1", &meta).expect("Failed to update metadata");

    let meta_updated = db.get_song_by_id("test-song-1").unwrap().unwrap();
    assert_eq!(meta_updated.title.as_deref(), Some("Renamed Song"));
    assert_eq!(meta_updated.artist.as_deref(), Some("New Artist"));
    assert_eq!(meta_updated.album_id.as_deref(), Some(album_id.as_str()));
    assert_eq!(meta_updated.bpm, Some(128.0));

    // The editor saves its complete draft, clears nullable overrides, and preserves
    // tracks/markers that are owned by the Studio controls.
    let details = amp_core::library::models::SongDetailsUpdate {
        title: "Edited details".into(), artist: "".into(), album: "".into(),
        base_key: None, key: -3, transpose: 3, bpm: None, speed: 1.5,
        merge_notes: true, merge_milliseconds: 175, hold_notes: true, tap_threshold_ms: 0,
        date_added: "2026-10-22T02:00:00Z".into(),
    };
    db.update_song_details("test-song-1", &details, None).unwrap();
    let edited = db.get_song_by_id("test-song-1").unwrap().unwrap();
    assert_eq!(edited.title.as_deref(), Some("Edited details"));
    assert_eq!(edited.artist, None);
    assert_eq!(edited.album, None);
    assert_eq!(edited.album_id, None);
    assert_eq!(edited.base_key, None);
    assert_eq!(edited.key, -3);
    assert_eq!(edited.transpose, 3);
    assert_eq!(edited.bpm, None);
    assert_eq!(edited.speed, Some(1.5));
    assert_eq!(edited.merge_notes, Some(true));
    assert_eq!(edited.merge_milliseconds, Some(175));
    assert_eq!(edited.hold_notes, Some(true));
    assert_eq!(edited.date_added.as_deref(), Some("2026-10-22T02:00:00Z"));
    assert_eq!(edited.disabled_tracks.as_deref(), Some("0,3"));
    assert!(edited.key_changes.unwrap().contains("Chorus"));
    assert!(db.update_song_details("nonexistent", &details, None).is_err());

    // 6. Playlists test
    let pl = db.create_playlist("Favorites", Some("Heart"), None).expect("Create playlist failed");
    assert_eq!(pl.title, "Favorites");

    db.add_to_playlist(&pl.id, "test-song-1").expect("Add to playlist failed");
    let pl_songs = db.get_playlist_songs(&pl.id).expect("Get playlist songs failed");
    assert_eq!(pl_songs.len(), 1);
    assert_eq!(pl_songs[0].id, "test-song-1");

    let playlists = db.get_playlists().expect("Get playlists failed");
    let found_pl = playlists.iter().find(|p| p.id == pl.id).expect("Playlist not found in list");
    assert_eq!(found_pl.song_count, 1);

    db.remove_from_playlist(&pl.id, "test-song-1").expect("Remove failed");
    let empty_pl_songs = db.get_playlist_songs(&pl.id).unwrap();
    assert_eq!(empty_pl_songs.len(), 0);

    // 7. Delete song
    db.delete_song("test-song-1").expect("Delete song failed");
    assert!(db.get_song_by_id("test-song-1").unwrap().is_none());

    // Clean up
    let _ = std::fs::remove_dir_all(&temp_dir);
}

#[test]
fn test_legacy_database_migration() {
    let temp_dir = std::env::temp_dir().join(format!("amp_migration_{}", uuid::Uuid::new_v4()));
    let _ = std::fs::create_dir_all(&temp_dir);
    let db_path = temp_dir.join("legacy.db");

    // Manually create legacy table without AlbumId column (matching original C# AutoMidiPlayer)
    {
        let conn = rusqlite::Connection::open(&db_path).unwrap();
        conn.execute(
            "CREATE TABLE Songs (
                Id TEXT PRIMARY KEY,
                BaseKey INTEGER,
                Key INTEGER NOT NULL,
                Path TEXT NOT NULL,
                FileHash TEXT,
                Title TEXT,
                Artist TEXT,
                Album TEXT,
                DateAdded TEXT,
                Transpose INTEGER,
                Speed REAL,
                Bpm REAL,
                DisabledTracks TEXT,
                MergeNotes INTEGER,
                MergeMilliseconds INTEGER,
                HoldNotes INTEGER,
                ImagePath TEXT,
                CachedDurationMs INTEGER,
                CachedNativeBpm REAL,
                AutoChord INTEGER,
                ChordDetectionMilliseconds INTEGER,
                KeyChanges TEXT,
                DetectChordPads INTEGER,
                EnableKeyChangeMarkers INTEGER
            );",
            [],
        ).unwrap();

        // Insert legacy row
        conn.execute(
            "INSERT INTO Songs (Id, Key, Path, Title) VALUES ('legacy-1', 0, 'C:\\legacy.mid', 'Legacy Track')",
            [],
        ).unwrap();
    }

    // Now open via LibraryDatabase (which should run migration and add AlbumId + Albums table)
    let db = LibraryDatabase::new(Some(db_path)).expect("Migration failed");
    let songs = db.get_all_songs().expect("Failed to get songs after migration");
    assert_eq!(songs.len(), 1);
    assert_eq!(songs[0].id, "legacy-1");
    assert_eq!(songs[0].title.as_deref(), Some("Legacy Track"));
    assert_eq!(songs[0].album_id, None);

    // Verify Albums table exists and can be linked
    let album_id = db.get_or_create_album("Legacy Album", None).unwrap();
    let meta = SongMetadataUpdate {
        title: None,
        artist: None,
        album: Some("Legacy Album".to_string()),
        album_id: Some(album_id.clone()),
        bpm: None,
    };
    db.update_song_metadata("legacy-1", &meta).unwrap();
    let updated = db.get_song_by_id("legacy-1").unwrap().unwrap();
    assert_eq!(updated.album_id.as_deref(), Some(album_id.as_str()));

    let _ = std::fs::remove_dir_all(&temp_dir);
}

#[test]
fn test_scanner_and_importer() {
    let temp_dir = std::env::temp_dir().join(format!("amp_scanner_{}", uuid::Uuid::new_v4()));
    let _ = std::fs::create_dir_all(&temp_dir);

    // Create a real mini MIDI file
    use midly::{Format, Header, MetaMessage, MidiMessage, Smf, Timing, TrackEvent, TrackEventKind};
    let mut smf = Smf::new(Header::new(Format::SingleTrack, Timing::Metrical(480.into())));
    smf.tracks.push(vec![
        TrackEvent {
            delta: 0.into(),
            kind: TrackEventKind::Meta(MetaMessage::TrackName(b"Special Track")),
        },
        TrackEvent {
            delta: 0.into(),
            kind: TrackEventKind::Midi {
                channel: 0.into(),
                message: MidiMessage::NoteOn {
                    key: 60.into(),
                    vel: 80.into(),
                },
            },
        },
        TrackEvent {
            delta: 480.into(),
            kind: TrackEventKind::Midi {
                channel: 0.into(),
                message: MidiMessage::NoteOff {
                    key: 60.into(),
                    vel: 0.into(),
                },
            },
        },
        TrackEvent {
            delta: 0.into(),
            kind: TrackEventKind::Meta(MetaMessage::EndOfTrack),
        },
    ]);
    let mut midi_bytes = Vec::new();
    smf.write(&mut midi_bytes).unwrap();

    let midi_file_path = temp_dir.join("Artist Name - Track Name [HQ Audio].mid");
    std::fs::write(&midi_file_path, &midi_bytes).unwrap();

    let db_path = temp_dir.join("scanner.db");
    let db = LibraryDatabase::new(Some(db_path)).unwrap();

    // Scan folder
    let summary = scan_folder(&db, &temp_dir, None).expect("Scan failed");
    assert_eq!(summary.total_files, 1);
    assert_eq!(summary.added, 1);

    let songs = db.get_all_songs().unwrap();
    assert_eq!(songs.len(), 1);
    assert_eq!(songs[0].title.as_deref(), Some("Artist Name"));
    assert_eq!(songs[0].artist.as_deref(), Some("Track Name"));
    assert_eq!(songs[0].is_missing, false); // File exists on disk

    // Import the file again - should recognize existing
    let imported = import_files(&db, &[midi_file_path.clone()], None).expect("Import failed");
    assert_eq!(imported.len(), 1);
    assert_eq!(imported[0].id, songs[0].id);

    let _ = std::fs::remove_dir_all(&temp_dir);
}

#[test]
fn test_queue_persistence_and_reorder() {
    let temp_dir = std::env::temp_dir().join(format!("amp_queue_{}", uuid::Uuid::new_v4()));
    let _ = std::fs::create_dir_all(&temp_dir);
    let db_path = temp_dir.join("queue.db");

    let db = LibraryDatabase::new(Some(db_path)).expect("Failed to initialize DB");

    // Insert 3 test songs
    for i in 1..=3 {
        let s = SongRecord {
            id: format!("song-{}", i),
            base_key: None,
            key: 0,
            path: format!("C:\\Music\\song{}.mid", i),
            file_hash: Some(format!("hash{}", i)),
            title: Some(format!("Song {}", i)),
            artist: Some("Artist".to_string()),
            album: None,
            album_id: None,
            date_added: None,
            transpose: 1,
            speed: Some(1.0),
            bpm: Some(120.0),
            disabled_tracks: None,
            merge_notes: None,
            merge_milliseconds: None,
            hold_notes: None,
        tap_threshold_ms: None,
            image_path: None,
            cached_duration_ms: Some(60000),
            cached_native_bpm: Some(120.0),
            auto_chord: None,
            chord_detection_milliseconds: None,
            key_changes: None,
            detect_chord_pads: None,
            enable_key_change_markers: Some(true),
            is_missing: true,
        };
        db.upsert_song(&s).unwrap();
    }

    // 1. Add songs 1, 2, 3 to queue
    let ids = vec!["song-1".to_string(), "song-2".to_string(), "song-3".to_string()];
    db.add_to_queue(&ids).expect("add_to_queue failed");

    let q_songs = db.get_queue_songs().expect("get_queue_songs failed");
    assert_eq!(q_songs.len(), 3);
    assert_eq!(q_songs[0].id, "song-1");
    assert_eq!(q_songs[1].id, "song-2");
    assert_eq!(q_songs[2].id, "song-3");

    // 2. Reorder queue to [song-3, song-1, song-2]
    let reordered = vec!["song-3".to_string(), "song-1".to_string(), "song-2".to_string()];
    db.reorder_queue(&reordered).expect("reorder_queue failed");

    let q_songs2 = db.get_queue_songs().unwrap();
    assert_eq!(q_songs2[0].id, "song-3");
    assert_eq!(q_songs2[1].id, "song-1");
    assert_eq!(q_songs2[2].id, "song-2");

    // 3. Remove song-1 from queue
    db.remove_from_queue(&["song-1".to_string()]).expect("remove_from_queue failed");
    let q_songs3 = db.get_queue_songs().unwrap();
    assert_eq!(q_songs3.len(), 2);
    assert_eq!(q_songs3[0].id, "song-3");
    assert_eq!(q_songs3[1].id, "song-2");

    // 4. Cascade delete: deleting song-3 from Songs table automatically removes it from Queue
    db.delete_song("song-3").expect("delete_song failed");
    let q_songs4 = db.get_queue_songs().unwrap();
    assert_eq!(q_songs4.len(), 1);
    assert_eq!(q_songs4[0].id, "song-2");

    // 5. Clear queue
    db.clear_queue().expect("clear_queue failed");
    assert_eq!(db.get_queue_songs().unwrap().len(), 0);

    let _ = std::fs::remove_dir_all(&temp_dir);
}

#[test]
fn test_song_defaults_applied_on_add() {
    use amp_core::library::SongDefaults;
    use midly::{Format, Header, MetaMessage, MidiMessage, Smf, Timing, TrackEvent, TrackEventKind};

    let temp_dir = std::env::temp_dir().join(format!("amp_defaults_{}", uuid::Uuid::new_v4()));
    let _ = std::fs::create_dir_all(&temp_dir);

    // 1. Create a MIDI file with key signature D Major (sf = 2, key_offset = -2)
    let mut smf = Smf::new(Header::new(Format::SingleTrack, Timing::Metrical(480.into())));
    smf.tracks.push(vec![
        TrackEvent {
            delta: 0.into(),
            kind: TrackEventKind::Meta(MetaMessage::KeySignature(2, false)),
        },
        TrackEvent {
            delta: 0.into(),
            kind: TrackEventKind::Midi {
                channel: 0.into(),
                message: MidiMessage::NoteOn {
                    key: 62.into(),
                    vel: 80.into(),
                },
            },
        },
        TrackEvent {
            delta: 480.into(),
            kind: TrackEventKind::Midi {
                channel: 0.into(),
                message: MidiMessage::NoteOff {
                    key: 62.into(),
                    vel: 0.into(),
                },
            },
        },
        TrackEvent {
            delta: 0.into(),
            kind: TrackEventKind::Meta(MetaMessage::EndOfTrack),
        },
    ]);
    let mut midi_bytes = Vec::new();
    smf.write(&mut midi_bytes).unwrap();

    let song1_path = temp_dir.join("Song One.mid");
    std::fs::write(&song1_path, &midi_bytes).unwrap();

    let db_path = temp_dir.join("defaults.db");
    let db = LibraryDatabase::new(Some(db_path)).unwrap();

    // 2. Test importing with auto_detect_base_key = true
    let defaults_auto = SongDefaults {
        default_base_key_offset: 0,
        default_key_offset: 1,
        auto_detect_base_key: true,
        default_transpose: "Down".to_string(), // Down = 3
        default_speed: 1.25,
        default_merge_notes: false,
        default_merge_tolerance_ms: 50,
        default_hold_notes: true,
        default_tap_threshold_ms: 120,
    };

    let imported = import_files(&db, &[song1_path.clone()], Some(&defaults_auto)).expect("Import failed");
    assert_eq!(imported.len(), 1);
    let s1 = &imported[0];

    // D Major detected offset is -2
    assert_eq!(s1.base_key, Some(-2));
    assert_eq!(s1.key, 0); // Relative key offset 0 when base key auto-detected
    assert_eq!(s1.transpose, 3); // Down
    assert_eq!(s1.speed, Some(1.25));
    assert_eq!(s1.merge_notes, Some(false));
    assert_eq!(s1.merge_milliseconds, Some(50));
    assert_eq!(s1.tap_threshold_ms, Some(120));
    assert_eq!(s1.hold_notes, Some(true));
    assert!(s1.key_changes.is_some());

    // 3. Test scanning a second song with auto_detect_base_key = false
    // Modify note in smf so file has unique content and hash
    smf.tracks[0][1] = TrackEvent {
        delta: 0.into(),
        kind: TrackEventKind::Midi {
            channel: 0.into(),
            message: MidiMessage::NoteOn {
                key: 65.into(),
                vel: 90.into(),
            },
        },
    };
    let mut midi_bytes_2 = Vec::new();
    smf.write(&mut midi_bytes_2).unwrap();

    let song2_path = temp_dir.join("Song Two.mid");
    std::fs::write(&song2_path, &midi_bytes_2).unwrap();

    let defaults_manual = SongDefaults {
        default_base_key_offset: 4,
        default_key_offset: -2,
        auto_detect_base_key: false,
        default_transpose: "Ignore".to_string(), // Ignore = 0
        default_speed: 0.75,
        default_merge_notes: true,
        default_merge_tolerance_ms: 15,
        default_hold_notes: false,
        default_tap_threshold_ms: 80,
    };

    let summary = scan_folder(&db, &temp_dir, Some(&defaults_manual)).expect("Scan failed");
    assert_eq!(summary.added, 1);

    let all_songs = db.get_all_songs().unwrap();
    let s2 = all_songs.iter().find(|s| s.path == song2_path.to_string_lossy()).expect("Song 2 not found");

    // Since auto_detect_base_key = false, it uses default_base_key_offset (4) and default_key_offset (-2)
    assert_eq!(s2.base_key, Some(4));
    assert_eq!(s2.key, -2);
    assert_eq!(s2.transpose, 0); // Ignore
    assert_eq!(s2.speed, Some(0.75));
    assert_eq!(s2.tap_threshold_ms, Some(80));
    assert_eq!(s2.merge_notes, Some(true));
    assert_eq!(s2.merge_milliseconds, Some(15));
    assert_eq!(s2.hold_notes, Some(false));

    let _ = std::fs::remove_dir_all(&temp_dir);
}



