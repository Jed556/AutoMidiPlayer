use std::path::{Path, PathBuf};
use std::sync::{mpsc, Arc};
use std::time::Duration;

use amp_core::library::models::{SongSettingsContext, SongSettingsScope};
use amp_core::library::{import_files, scan_folder, DirectoryWatcher, LibraryDatabase, SongSettingsUpdate};
use amp_core::library::issues::{FileIssueKind, FileIssueAction, resolve_file_issue};
use midly::{Format, Header, MetaMessage, MidiMessage, Smf, Timing, TrackEvent, TrackEventKind};

struct TestDirectory(PathBuf);

impl TestDirectory {
    fn new() -> Self {
        let root = std::env::temp_dir().join(format!("amp_library_sync_{}", uuid::Uuid::new_v4()));
        std::fs::create_dir_all(&root).unwrap();
        Self(root)
    }

    fn folder(&self, name: &str) -> PathBuf {
        let path = self.0.join(name);
        std::fs::create_dir_all(&path).unwrap();
        path
    }

    fn db(&self) -> LibraryDatabase {
        LibraryDatabase::new(Some(self.0.join("library.db"))).unwrap()
    }
}

impl Drop for TestDirectory {
    fn drop(&mut self) {
        assert_eq!(self.0.parent(), Some(std::env::temp_dir().as_path()));
        let _ = std::fs::remove_dir_all(&self.0);
    }
}

fn write_midi(path: &Path, pitch: u8) {
    let mut smf = Smf::new(Header::new(Format::SingleTrack, Timing::Metrical(480.into())));
    smf.tracks.push(vec![
        TrackEvent { delta: 0.into(), kind: TrackEventKind::Midi {
            channel: 0.into(), message: MidiMessage::NoteOn { key: pitch.into(), vel: 80.into() },
        } },
        TrackEvent { delta: 480.into(), kind: TrackEventKind::Midi {
            channel: 0.into(), message: MidiMessage::NoteOff { key: pitch.into(), vel: 0.into() },
        } },
        TrackEvent { delta: 0.into(), kind: TrackEventKind::Meta(MetaMessage::EndOfTrack) },
    ]);
    let mut bytes = Vec::new();
    smf.write(&mut bytes).unwrap();
    std::fs::write(path, bytes).unwrap();
}

#[test]
fn imports_with_missing_options_use_the_shared_json_defaults() {
    let temp = TestDirectory::new();
    let db = temp.db();
    let path = temp.0.join("default.mid");
    write_midi(&path, 60);
    let defaults = serde_json::from_value::<amp_core::SongDefaults>(serde_json::json!({})).unwrap();
    let song = import_files(&db, &[path], Some(&defaults)).unwrap().remove(0);
    let expected = amp_core::settings::SettingsStore::defaults();
    assert_eq!(song.hold_notes, expected["defaultHoldNotes"].as_bool());
    assert_eq!(song.merge_notes, expected["defaultMergeNotes"].as_bool());
    assert_eq!(song.speed, expected["defaultSpeed"].as_f64());
    assert_eq!(song.merge_milliseconds.map(u64::from), expected["defaultMergeToleranceMs"].as_u64());
    assert_eq!(song.tap_threshold_ms.map(u64::from), expected["defaultTapThresholdMs"].as_u64());
    assert_eq!(song.auto_chord, expected["defaultAutoChord"].as_bool());
    assert_eq!(song.chord_detection_milliseconds.map(u64::from), expected["defaultChordDetectionMilliseconds"].as_u64());
    assert_eq!(song.enable_key_change_markers, expected["defaultEnableKeyChangeMarkers"].as_bool());
}

#[test]
fn imports_and_folder_scans_leave_marker_creation_to_studio() {
    for auto_detect_base_key in [true, false] {
        for via_folder_scan in [true, false] {
            let temp = TestDirectory::new();
            let folder = temp.folder("songs");
            let path = folder.join("song.mid");
            write_midi(&path, 62);
            let bytes = std::fs::read(&path).unwrap();
            let mut midi = Smf::parse(&bytes).unwrap();
            midi.tracks[0].insert(0, TrackEvent {
                delta: 0.into(),
                kind: TrackEventKind::Meta(MetaMessage::KeySignature(2, false)),
            });
            let mut bytes_with_key = Vec::new();
            midi.write(&mut bytes_with_key).unwrap();
            std::fs::write(&path, bytes_with_key).unwrap();
            let db = temp.db();
            let defaults = amp_core::library::SongDefaults {
                auto_detect_base_key,
                ..Default::default()
            };
            let detected = amp_core::detect_key_changes(&amp_core::parse_midi_file(&path).unwrap());
            assert!(!detected.is_empty(), "Fixture must contain detectable key information");

            if via_folder_scan {
                scan_folder(&db, &folder, Some(&defaults)).unwrap();
            } else {
                import_files(&db, std::slice::from_ref(&path), Some(&defaults)).unwrap();
            }
            let song = db.get_all_songs().unwrap().pop().unwrap();
            assert!(song.key_changes.is_none());

            // Existing markers, including markers saved by older versions, survive both paths.
            let saved_markers = serde_json::to_string(&detected).unwrap();
            let mut settings: SongSettingsUpdate = serde_json::from_value(serde_json::json!({
                "key": song.key, "baseKey": song.base_key, "transpose": song.transpose,
                "keyChanges": saved_markers,
            })).unwrap();
            db.update_song_settings(&song.id, &settings).unwrap();
            import_files(&db, std::slice::from_ref(&path), Some(&defaults)).unwrap();
            assert_eq!(db.get_song_by_id(&song.id).unwrap().unwrap().key_changes.as_deref(), Some(saved_markers.as_str()));
            scan_folder(&db, &folder, Some(&defaults)).unwrap();
            assert_eq!(db.get_song_by_id(&song.id).unwrap().unwrap().key_changes.as_deref(), Some(saved_markers.as_str()));

            // Removing all markers must persist even though the MIDI still has a detectable key.
            settings.key_changes = None;
            db.update_song_settings(&song.id, &settings).unwrap();
            import_files(&db, std::slice::from_ref(&path), Some(&defaults)).unwrap();
            assert!(db.get_song_by_id(&song.id).unwrap().unwrap().key_changes.is_none());
            scan_folder(&db, &folder, Some(&defaults)).unwrap();
            assert!(db.get_song_by_id(&song.id).unwrap().unwrap().key_changes.is_none());
        }
    }
}

#[test]
fn scanning_a_legacy_database_without_a_unique_path_preserves_existing_song_references() {
    let temp = TestDirectory::new();
    let folder = temp.folder("songs");
    drop(temp.db());
    // EF created only the Id primary key, unlike the rewrite's fresh schema.
    // Keep every other column identical so this exercises the missing constraint.
    {
        let conn = rusqlite::Connection::open(temp.0.join("library.db")).unwrap();
        let schema: String = conn.query_row("SELECT sql FROM sqlite_master WHERE name = 'Songs'",
            [], |row| row.get(0)).unwrap();
        assert!(schema.contains("Path TEXT NOT NULL UNIQUE"));
        conn.execute("DROP TABLE Songs", []).unwrap();
        conn.execute_batch(&schema.replace("Path TEXT NOT NULL UNIQUE", "Path TEXT NOT NULL")).unwrap();
    }
    let db = temp.db();
    let path = folder.join("legacy.mid");
    write_midi(&path, 60);
    let song = import_files(&db, &[path.clone()], None).unwrap().remove(0);
    let playlist = db.create_playlist("Favorites", None, None).unwrap();
    db.add_to_playlist(&playlist.id, &song.id).unwrap();
    db.add_to_queue(&[song.id.clone()]).unwrap();

    // Repeated imports with another proposed ID must reuse the existing path.
    let mut updated = song.clone();
    updated.id = uuid::Uuid::new_v4().to_string();
    updated.title = Some("Updated metadata".into());
    db.upsert_song(&updated).unwrap();
    assert_eq!(db.get_all_songs().unwrap().len(), 1);
    assert_eq!(db.get_all_songs().unwrap()[0].id, song.id);

    write_midi(&path, 65);
    assert_eq!(scan_folder(&db, &folder, None).unwrap().updated, 1);
    write_midi(&folder.join("new.mid"), 67);
    assert_eq!(scan_folder(&db, &folder, None).unwrap().added, 1);
    let renamed = folder.join("renamed.mid");
    std::fs::rename(&path, &renamed).unwrap();
    assert_eq!(scan_folder(&db, &folder, None).unwrap().updated, 1);
    assert_eq!(db.get_playlist_songs(&playlist.id).unwrap()[0].id, song.id);
    assert_eq!(db.get_queue_songs().unwrap()[0].id, song.id);
    assert_eq!(db.get_song_by_id(&song.id).unwrap().unwrap().title.as_deref(), Some("Updated metadata"));
    assert_eq!(scan_folder(&db, &folder, None).unwrap().added, 0);
    std::fs::remove_file(renamed).unwrap();
    assert_eq!(scan_folder(&db, &folder, None).unwrap().removed, 1);
}

#[test]
fn refresh_tracks_duplicates_and_preserves_missing_records_for_recovery() {
    let temp = TestDirectory::new();
    let folder = temp.folder("songs");
    let outside = temp.folder("songs-other").join("outside.mid");
    let db = temp.db();
    write_midi(&outside, 72);
    let external_song = import_files(&db, &[outside.clone()], None).unwrap().remove(0);
    std::fs::remove_file(outside).unwrap();

    let original = folder.join("original.mid");
    let copy = folder.join("copy.MIDI");
    write_midi(&original, 60);
    assert_eq!(scan_folder(&db, &folder, None).unwrap().added, 1);
    std::fs::copy(&original, &copy).unwrap();
    assert_eq!(scan_folder(&db, &folder, None).unwrap().added, 0);
    assert_eq!(db.get_all_songs().unwrap().len(), 2);
    assert!(db.get_file_issues().unwrap().iter().any(|issue| issue.kind == FileIssueKind::Duplicate));

    std::fs::remove_file(original).unwrap();
    let summary = scan_folder(&db, &folder, None).unwrap();
    assert_eq!(summary.removed, 0, "An identical surviving copy takes over without losing the song");
    assert_eq!(summary.total_files, 1);
    assert!(db.get_song_by_path(&copy.to_string_lossy()).unwrap().is_some());

    std::fs::remove_file(copy).unwrap();
    assert_eq!(scan_folder(&db, &folder, None).unwrap().removed, 1);
    let remaining = db.get_all_songs().unwrap();
    assert_eq!(remaining.len(), 2);
    assert!(remaining.iter().any(|song| song.id == external_song.id));
    assert!(remaining.iter().all(|song| song.is_missing));
    assert!(db.available_songs(remaining).unwrap().is_empty());
    assert_eq!(scan_folder(&db, &folder, None).unwrap().removed, 0);
}

#[test]
fn renames_preserve_song_ids_scoped_settings_playlists_and_queue() {
    let temp = TestDirectory::new();
    let folder = temp.folder("songs");
    let db = temp.db();
    let original = folder.join("original.mid");
    write_midi(&original, 60);
    scan_folder(&db, &folder, None).unwrap();
    let song = db.get_all_songs().unwrap().remove(0);
    let playlist = db.create_playlist("Favorites", None, None).unwrap();
    db.add_to_playlist(&playlist.id, &song.id).unwrap();
    db.add_to_queue(&[song.id.clone()]).unwrap();
    let context = SongSettingsContext { game_id: "game".into(), instrument_id: "lyre".into() };
    let settings: SongSettingsUpdate = serde_json::from_value(serde_json::json!({
        "key": 5, "baseKey": 2, "transpose": 3, "speed": 1.5, "disabledTracks": "0,2",
    })).unwrap();
    db.update_song_settings_scoped(&song.id, &settings, &SongSettingsScope::Instrument {
        game_id: context.game_id.clone(), instrument_id: context.instrument_id.clone(),
    }).unwrap();

    let renamed = folder.join("renamed.MIDI");
    std::fs::rename(original, &renamed).unwrap();
    let summary = scan_folder(&db, &folder, None).unwrap();
    assert_eq!((summary.added, summary.updated, summary.removed), (0, 1, 0));
    let songs = db.resolve_song_settings(db.get_all_songs().unwrap(), Some(&context)).unwrap();
    assert_eq!(songs.len(), 1);
    assert_eq!(songs[0].id, song.id);
    assert_eq!(songs[0].path, renamed.to_string_lossy());
    assert_eq!(songs[0].key, 5);
    assert_eq!(songs[0].speed, Some(1.5));
    assert_eq!(songs[0].disabled_tracks.as_deref(), Some("0,2"));
    assert_eq!(db.get_playlist_songs(&playlist.id).unwrap()[0].id, song.id);
    assert_eq!(db.get_queue_songs().unwrap()[0].id, song.id);

    let imported_path = folder.join("imported.mid");
    std::fs::rename(&renamed, &imported_path).unwrap();
    let imported = import_files(&db, &[imported_path], None).unwrap();
    assert_eq!(imported[0].id, song.id);
    assert!(!imported[0].is_missing);
    assert_eq!(db.get_all_songs().unwrap().len(), 1);
}

#[test]
fn refresh_preserves_existing_records_on_invalid_files_or_unavailable_roots() {
    let temp = TestDirectory::new();
    let folder = temp.folder("songs");
    let db = temp.db();
    let path = folder.join("song.mid");
    write_midi(&path, 60);
    scan_folder(&db, &folder, None).unwrap();
    let song = db.get_all_songs().unwrap().remove(0);
    write_midi(&path, 65);
    assert_eq!(scan_folder(&db, &folder, None).unwrap().updated, 1);
    assert_eq!(db.get_all_songs().unwrap()[0].id, song.id);
    std::fs::write(&path, b"incomplete MIDI copy").unwrap();
    assert_eq!(scan_folder(&db, &folder, None).unwrap().skipped, 1);
    assert_eq!(db.get_all_songs().unwrap()[0].id, song.id);
    std::fs::rename(&folder, temp.0.join("unavailable")).unwrap();
    assert!(scan_folder(&db, &folder, None).is_err());
    assert_eq!(db.get_all_songs().unwrap()[0].id, song.id);
}

#[test]
fn watcher_scans_immediate_changes_bursts_and_folder_renames_and_stops_cleanly() {
    let temp = TestDirectory::new();
    let folder = temp.folder("songs");
    let db = Arc::new(temp.db());
    let (tx, rx) = mpsc::channel();
    let scan_db = Arc::clone(&db);
    let scan_root = folder.clone();
    let watcher = DirectoryWatcher::watch(&folder, move || {
        let result = scan_folder(&scan_db, &scan_root, None);
        tx.send(result).unwrap();
    }).unwrap();

    // The old throttle discarded all events in the first 500 ms.
    write_midi(&folder.join("first.mid"), 60);
    assert_eq!(rx.recv_timeout(Duration::from_secs(5)).unwrap().unwrap().added, 1);
    let first_id = db.get_all_songs().unwrap()[0].id.clone();

    // A burst directly after a scan must still produce a final scan.
    let nested = folder.join("nested");
    std::fs::create_dir(&nested).unwrap();
    write_midi(&nested.join("second.MIDI"), 65);
    write_midi(&nested.join("third.mid"), 67);
    assert_eq!(rx.recv_timeout(Duration::from_secs(5)).unwrap().unwrap().total_files, 3);
    assert_eq!(db.get_all_songs().unwrap().len(), 3);

    let renamed = folder.join("renamed");
    std::fs::rename(&nested, &renamed).unwrap();
    assert_eq!(rx.recv_timeout(Duration::from_secs(5)).unwrap().unwrap().updated, 2);
    assert_eq!(db.get_all_songs().unwrap().len(), 3);

    // Move the directory out of the watched root, leaving only the original song.
    std::fs::rename(&renamed, temp.0.join("moved-out")).unwrap();
    assert_eq!(rx.recv_timeout(Duration::from_secs(5)).unwrap().unwrap().removed, 2);
    assert_eq!(db.available_songs(db.get_all_songs().unwrap()).unwrap()[0].id, first_id);
    std::fs::remove_file(folder.join("first.mid")).unwrap();
    assert_eq!(rx.recv_timeout(Duration::from_secs(5)).unwrap().unwrap().removed, 1);
    assert_eq!(db.get_all_songs().unwrap().len(), 3);
    assert!(db.available_songs(db.get_all_songs().unwrap()).unwrap().is_empty());

    watcher.stop();
    write_midi(&folder.join("after-stop.mid"), 72);
    assert!(rx.recv_timeout(Duration::from_millis(800)).is_err());
    assert!(db.available_songs(db.get_all_songs().unwrap()).unwrap().is_empty());
}

#[test]
fn library_removal_persists_without_deleting_the_file_and_restore_keeps_scoped_settings() {
    let temp = TestDirectory::new();
    let folder = temp.folder("songs");
    let path = folder.join("song.mid");
    write_midi(&path, 60);
    let id;
    let context = SongSettingsContext { game_id: "game".into(), instrument_id: "lyre".into() };
    {
        let db = temp.db();
        scan_folder(&db, &folder, None).unwrap();
        let song = db.get_all_songs().unwrap().remove(0);
        id = song.id.clone();
        let settings: SongSettingsUpdate = serde_json::from_value(serde_json::json!({
            "key": 4, "baseKey": 2, "transpose": 3, "speed": 1.5,
        })).unwrap();
        db.update_song_settings_scoped(&id, &settings, &SongSettingsScope::Instrument {
            game_id: context.game_id.clone(), instrument_id: context.instrument_id.clone(),
        }).unwrap();
        db.add_to_queue(&[id.clone()]).unwrap();
        db.exclude_songs(&[id.clone()]).unwrap();
        assert!(path.is_file());
        assert!(db.get_queue_songs().unwrap().is_empty());
        assert!(db.available_songs(db.get_all_songs().unwrap()).unwrap().is_empty());
        assert_eq!(db.get_file_issues().unwrap()[0].kind, FileIssueKind::Excluded);
        assert_eq!(scan_folder(&db, &folder, None).unwrap().added, 0);
    }
    let db = temp.db();
    assert!(db.available_songs(db.get_all_songs().unwrap()).unwrap().is_empty());
    assert_eq!(db.get_file_issues().unwrap()[0].kind, FileIssueKind::Excluded);
    resolve_file_issue(&db, &path.to_string_lossy(), FileIssueAction::Restore, None).unwrap();
    let restored = db.resolve_song_settings(db.available_songs(db.get_all_songs().unwrap()).unwrap(), Some(&context)).unwrap();
    assert_eq!(restored[0].id, id);
    assert_eq!(restored[0].key, 4);
    assert_eq!(restored[0].speed, Some(1.5));
    assert!(db.get_file_issues().unwrap().is_empty());
}

#[test]
fn disappearance_is_announced_once_and_restoring_the_file_recovers_the_same_song() {
    let temp = TestDirectory::new();
    let folder = temp.folder("songs");
    let path = folder.join("recover.mid");
    let db = temp.db();
    write_midi(&path, 60);
    assert_eq!(scan_folder(&db, &folder, None).unwrap().added_files.len(), 1);
    let id = db.get_all_songs().unwrap()[0].id.clone();
    std::fs::remove_file(&path).unwrap();
    let removed = scan_folder(&db, &folder, None).unwrap();
    assert_eq!(removed.removed_files, vec!["recover"]);
    assert!(db.get_song_by_id(&id).unwrap().is_some());
    assert_eq!(db.get_file_issues().unwrap()[0].kind, FileIssueKind::Missing);
    assert!(scan_folder(&db, &folder, None).unwrap().removed_files.is_empty());
    write_midi(&path, 60);
    assert_eq!(scan_folder(&db, &folder, None).unwrap().added_files, vec!["recover"]);
    assert_eq!(db.available_songs(db.get_all_songs().unwrap()).unwrap()[0].id, id);
    assert!(db.get_file_issues().unwrap().is_empty());
    std::fs::remove_file(&path).unwrap();
    scan_folder(&db, &folder, None).unwrap();
    resolve_file_issue(&db, &path.to_string_lossy(), FileIssueAction::Forget, None).unwrap();
    assert!(db.get_song_by_id(&id).unwrap().is_none());
    assert!(db.get_file_issues().unwrap().is_empty());
}

#[test]
fn duplicate_selection_and_invalid_file_retry_resolve_real_file_conflicts() {
    let temp = TestDirectory::new();
    let folder = temp.folder("songs");
    let original = folder.join("original.mid");
    let copy = folder.join("copy.MIDI");
    let invalid = folder.join("invalid.mid");
    let db = temp.db();
    write_midi(&original, 60);
    scan_folder(&db, &folder, None).unwrap();
    let id = db.get_all_songs().unwrap()[0].id.clone();
    std::fs::copy(&original, &copy).unwrap();
    std::fs::write(&invalid, b"invalid MIDI").unwrap();
    assert_eq!(scan_folder(&db, &folder, None).unwrap().new_conflicts, 2);
    assert_eq!(scan_folder(&db, &folder, None).unwrap().new_conflicts, 0);
    let issues = db.get_file_issues().unwrap();
    assert_eq!(issues.len(), 2);
    assert!(issues.iter().any(|issue| issue.kind == FileIssueKind::Invalid));
    assert!(issues.iter().any(|issue| issue.kind == FileIssueKind::Duplicate));
    assert_eq!(db.available_songs(db.get_all_songs().unwrap()).unwrap().len(), 1);
    resolve_file_issue(&db, &copy.to_string_lossy(), FileIssueAction::UseCopy, None).unwrap();
    assert_eq!(db.get_song_by_id(&id).unwrap().unwrap().path, copy.to_string_lossy());
    scan_folder(&db, &folder, None).unwrap();
    assert_eq!(db.get_song_by_id(&id).unwrap().unwrap().path, copy.to_string_lossy());
    assert!(original.is_file() && copy.is_file());
    write_midi(&invalid, 67);
    resolve_file_issue(&db, &invalid.to_string_lossy(), FileIssueAction::Restore, None).unwrap();
    assert_eq!(db.available_songs(db.get_all_songs().unwrap()).unwrap().len(), 2);
    assert!(db.get_file_issues().unwrap().iter().all(|issue| issue.kind == FileIssueKind::Duplicate));
}

#[test]
fn disk_deletion_is_a_separate_action_and_only_accepts_removed_file_conflicts() {
    let temp = TestDirectory::new();
    let folder = temp.folder("songs");
    let path = folder.join("delete.mid");
    let db = temp.db();
    write_midi(&path, 60);
    scan_folder(&db, &folder, None).unwrap();
    let id = db.get_all_songs().unwrap()[0].id.clone();
    assert!(resolve_file_issue(&db, &path.to_string_lossy(), FileIssueAction::DeleteFile, None).is_err());
    assert!(path.is_file());
    db.exclude_songs(&[id.clone()]).unwrap();
    assert!(path.is_file());
    resolve_file_issue(&db, &path.to_string_lossy(), FileIssueAction::DeleteFile, None).unwrap();
    assert!(!path.exists());
    assert!(db.get_song_by_id(&id).unwrap().is_none());
    assert!(db.get_file_issues().unwrap().is_empty());
}
