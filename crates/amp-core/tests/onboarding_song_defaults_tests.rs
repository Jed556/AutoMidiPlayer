use amp_core::LibraryDatabase;
use amp_core::library::models::{SongDefaults, SongSettingsContext};
use rusqlite::{params, Connection};
use serde_json::{json, Value};

struct Fixture {
    directory: std::path::PathBuf,
    db: LibraryDatabase,
}

impl Fixture {
    fn new() -> Self {
        let directory = std::env::temp_dir().join(format!("amp_onboarding_defaults_{}", uuid::Uuid::new_v4()));
        let db = LibraryDatabase::new(Some(directory.join("library.db"))).unwrap();
        let conn = Connection::open(directory.join("library.db")).unwrap();
        conn.execute_batch("ALTER TABLE Songs ADD COLUMN Description TEXT;
            INSERT INTO Songs (Id, Path, Title, Artist, Album, DateAdded, BaseKey, Key, Transpose,
                Speed, Bpm, DisabledTracks, MergeNotes, MergeMilliseconds, HoldNotes, TapThresholdMs,
                AutoChord, ChordDetectionMilliseconds, KeyChanges, EnableKeyChangeMarkers,
                ImagePath, CachedDurationMs, CachedNativeBpm, FileHash, Description)
            VALUES ('song', 'missing.mid', 'My title', 'My artist', 'My album', '2026-01-01', 5, -2, 3,
                1.75, 143, '1,3', 1, 87, 0, 300, 1, 45,
                '[{\"TimeMs\":1200,\"KeyOffset\":3,\"Label\":\"Chorus\"}]', 0,
                'cover.png', 90000, 144, 'hash', 'My description');").unwrap();
        // Include the instrument copies made during legacy migration, plus a
        // game override and another game that is not currently selected.
        let contexts = [context("a", "lyre"), context("b", "piano")];
        db.migrate_legacy_song_settings(&contexts, || {}).unwrap();
        conn.execute("INSERT INTO SongSettings VALUES ('song', 'a', '', ?1)", [json!({
            "baseKey": 7, "key": 4, "transpose": 2, "speed": 0.5, "holdNotes": false,
            "tapThresholdMs": 200, "mergeNotes": true, "mergeMilliseconds": 75,
            "keyChanges": null, "disabledTracks": "2", "bpm": 160,
        }).to_string()]).unwrap();
        Self { directory, db }
    }

    fn conn(&self) -> Connection { Connection::open(self.directory.join("library.db")).unwrap() }
    fn profiles(&self) -> Vec<(String, String, Value)> {
        self.conn().prepare("SELECT GameId, InstrumentId, Settings FROM SongSettings ORDER BY GameId, InstrumentId").unwrap()
            .query_map([], |row| Ok((row.get(0)?, row.get(1)?, serde_json::from_str::<Value>(&row.get::<_, String>(2)?).unwrap())))
            .unwrap().map(Result::unwrap).collect()
    }
    fn song(&self) -> Value { serde_json::to_value(self.db.get_song_by_id("song").unwrap().unwrap()).unwrap() }
}

impl Drop for Fixture {
    fn drop(&mut self) { let _ = std::fs::remove_dir_all(&self.directory); }
}

fn context(game: &str, instrument: &str) -> SongSettingsContext {
    SongSettingsContext { game_id: game.into(), instrument_id: instrument.into() }
}

fn defaults() -> SongDefaults {
    SongDefaults {
        auto_detect_base_key: false, default_base_key_offset: -3, default_key_offset: 2,
        default_transpose: "Smart".into(), default_speed: 0.75,
        default_hold_notes: true, default_tap_threshold_ms: 50,
        default_merge_notes: false, default_merge_tolerance_ms: 25,
        // Options absent from these onboarding pages must remain untouched.
        default_auto_chord: false, default_enable_key_change_markers: true,
        ..SongDefaults::default()
    }
}

#[test]
fn onboarding_choices_apply_only_the_selected_fields_across_all_scopes() {
    for song_settings in [false, true] {
        for note_settings in [false, true] {
            let f = Fixture::new();
            let before = f.song();
            let profiles = f.profiles();
            let mut expected = before;
            let mut patch = json!({});
            if song_settings {
                patch = json!({"baseKey": -3, "key": 2, "transpose": 1, "speed": 0.75});
            }
            if note_settings {
                for (key, value) in json!({"holdNotes": true, "tapThresholdMs": 50, "mergeNotes": false, "mergeMilliseconds": 25}).as_object().unwrap() {
                    patch[key] = value.clone();
                }
            }
            for (key, value) in patch.as_object().unwrap() { expected[key] = value.clone(); }
            f.db.apply_onboarding_song_defaults(&defaults(), song_settings, note_settings).unwrap();
            assert_eq!(f.song(), expected, "Only selected defaults may change");
            let expected_profiles = profiles.into_iter().map(|(game, instrument, mut settings)| {
                for key in patch.as_object().unwrap().keys() { settings.as_object_mut().unwrap().remove(key); }
                (game, instrument, settings)
            }).collect::<Vec<_>>();
            assert_eq!(f.profiles(), expected_profiles, "Preserve scoped markers, tracks, BPM and other settings");
            for c in [context("a", "lyre"), context("a", "new"), context("b", "piano"), context("new", "new")] {
                let resolved = f.db.resolve_song_settings(vec![f.db.get_song_by_id("song").unwrap().unwrap()], Some(&c)).unwrap();
                let resolved = serde_json::to_value(&resolved[0]).unwrap();
                for (key, value) in patch.as_object().unwrap() { assert_eq!(&resolved[key], value); }
            }
            let description: String = f.conn().query_row("SELECT Description FROM Songs", [], |row| row.get(0)).unwrap();
            assert_eq!(description, "My description");
            let reopened = LibraryDatabase::new(Some(f.directory.join("library.db"))).unwrap();
            assert_eq!(serde_json::to_value(reopened.get_song_by_id("song").unwrap().unwrap()).unwrap(), expected);
        }
    }
}

#[test]
fn auto_detect_uses_the_midi_key_without_replacing_saved_markers() {
    use midly::{Format, Header, MetaMessage, Smf, Timing, TrackEvent, TrackEventKind};
    let f = Fixture::new();
    let midi_path = f.directory.join("key.mid");
    let smf = Smf { header: Header::new(Format::SingleTrack, Timing::Metrical(480.into())), tracks: vec![vec![
        TrackEvent { delta: 0.into(), kind: TrackEventKind::Meta(MetaMessage::KeySignature(2, false)) },
        TrackEvent { delta: 480.into(), kind: TrackEventKind::Meta(MetaMessage::EndOfTrack) },
    ]] };
    smf.save(&midi_path).unwrap();
    f.conn().execute("UPDATE Songs SET Path = ?1", [midi_path.to_string_lossy().as_ref()]).unwrap();
    let before = f.song();
    let mut defaults = defaults();
    defaults.auto_detect_base_key = true;
    f.db.apply_onboarding_song_defaults(&defaults, true, false).unwrap();
    let song = f.song();
    assert_eq!(song["baseKey"], -2, "D major transposes down two semitones to C");
    assert_eq!(song["key"], 0);
    assert_eq!(song["keyChanges"], before["keyChanges"]);
    assert_eq!(song["holdNotes"], before["holdNotes"]);
}

#[test]
fn failed_override_updates_roll_back_the_entire_application() {
    let f = Fixture::new();
    let before = f.song();
    f.conn().execute("UPDATE SongSettings SET Settings = ?1 WHERE GameId = ?2", params!["invalid json", "b"]).unwrap();
    assert!(f.db.apply_onboarding_song_defaults(&defaults(), true, true).is_err());
    assert_eq!(f.song(), before);
}
