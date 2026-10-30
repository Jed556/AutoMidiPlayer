use amp_core::settings::SettingsStore;
use amp_core::{KeyboardLayoutJson, LibraryDatabase, LoadedGameBundle};
use rusqlite::Connection;
use serde_json::{json, Value};
use std::{collections::HashMap, fs, path::PathBuf, sync::Arc};

struct Fixture {
    root: PathBuf,
}
impl Fixture {
    fn new() -> Self {
        let root = std::env::temp_dir().join(format!("amp_preferences_{}", uuid::Uuid::new_v4()));
        fs::create_dir_all(&root).unwrap();
        Self { root }
    }
    fn db(&self) -> LibraryDatabase {
        LibraryDatabase::new(Some(self.root.join("library.db"))).unwrap()
    }
    fn open(&self, db: &LibraryDatabase) -> Result<SettingsStore, String> {
        let game: LoadedGameBundle = serde_json::from_value(json!({
            "config": {"id": "Genshin Impact", "displayName": "Genshin Impact"},
            "folderPath": "Games/Genshin Impact", "instruments": [{"id": "djem-djem-drum", "name": "Djem Djem Drum", "supportedLayouts": ["Drums"]}],
        })).unwrap();
        let layout: KeyboardLayoutJson =
            serde_json::from_value(json!({"id": "drums", "name": "QWERTY (Drums)", "keys": []}))
                .unwrap();
        SettingsStore::open(
            self.root.join("settings.json"),
            db,
            &[game],
            &HashMap::from([("drums".into(), layout)]),
        )
    }
    fn config(&self, values: &[(&str, &str)]) -> String {
        let entries = values
            .iter()
            .map(|(name, value)| {
                format!("<{name} serializeAs=\"String\"><value>{value}</value></{name}>")
            })
            .collect::<String>();
        let xml = format!("<?xml version=\"1.0\"?><configuration><userSettings>{entries}</userSettings></configuration>");
        fs::write(self.root.join("user.config"), &xml).unwrap();
        xml
    }
    fn old_preferences(&self, value: Value) {
        let conn = Connection::open(self.root.join("library.db")).unwrap();
        conn.execute_batch("CREATE TABLE AppSettings (Key TEXT PRIMARY KEY, Value TEXT NOT NULL, UpdatedAt TEXT NOT NULL)").unwrap();
        conn.execute(
            "INSERT INTO AppSettings VALUES ('renderer', ?1, '2026-10-30')",
            [value.to_string()],
        )
        .unwrap();
    }
}
impl Drop for Fixture {
    fn drop(&mut self) {
        let _ = fs::remove_dir_all(&self.root);
    }
}

#[test]
fn portable_config_imports_preferences_selections_and_session_without_changing_library_records() {
    let f = Fixture::new();
    let db = f.db();
    let conn = Connection::open(f.root.join("library.db")).unwrap();
    conn.execute(
        "INSERT INTO Songs (Id, Path, Title) VALUES ('SONG-A', 'song.mid', 'Keep song')",
        [],
    )
    .unwrap();
    let original = f.config(&[
        ("MidiFolder", r"C:\MIDI &amp; Tracks"),
        ("AppTheme", "-1"),
        ("KeyboardPressDelayMs", "50"),
        ("DefaultSongTranspose", "0"),
        ("DefaultSongMergeMilliseconds", "100"),
        ("DefaultSongHoldNotes", "False"),
        ("DefaultSongSpeed", "1.5"),
        ("IncludeBetaUpdates", "True"),
        ("CacheMaxSizeMB", "0"),
        ("CacheAutoCleanInterval", "2"),
        ("UpdateCheckFrequency", "4"),
        ("HasShownFirstLaunch", "True"),
        ("ActiveGenshin", "True"),
        ("GenshinLocation", "GenshinImpact.exe"),
        (
            "SelectedInstrumentByGame",
            r#"{"Genshin Impact":"Genshin Impact:Djem Djem Drum"}"#,
        ),
        ("SelectedLayoutName", "QWERTY (Drums)"),
        ("LastViewedPage", "Studio"),
        ("QueueSongIds", "song-a,missing,song-a"),
        ("CurrentSongId", "song-a"),
        ("CurrentSongPosition", "8.5104884"),
        ("QueueLoopMode", "2"),
        ("QueueShuffle", "True"),
        ("UseSpeakers", "True"),
        ("HotkeyPlayPause", "3|18"),
        ("HotkeyNext", "3|25"),
        ("HotkeyPrevious", "3|23"),
        ("HotkeySpeedUp", "3|24"),
        ("HotkeySpeedDown", "3|26"),
        ("HotkeyPanic", "3|13"),
        ("FutureLegacyOption", "Keep me"),
    ]);
    let store = f.open(&db).unwrap();
    let settings = store.get().unwrap();
    for (key, value) in [
        ("midiFolder", json!(r"C:\MIDI & Tracks")),
        ("theme", json!("System")),
        ("keyPressDelayMs", json!(50)),
        ("defaultTranspose", json!("Ignore")),
        ("defaultMergeToleranceMs", json!(100)),
        ("defaultHoldNotes", json!(false)),
        ("defaultSpeed", json!(1.5)),
        ("includeBetaUpdates", json!(true)),
        ("cacheMaxSizeMb", json!(0)),
        ("cacheAutoClean", json!("monthly")),
        ("updateCheckFrequency", json!("launch")),
        ("onboardingCompleted", json!(true)),
    ] {
        assert_eq!(settings[key], value, "{key}");
    }
    assert_eq!(settings["activeGameId"], "Genshin Impact");
    assert_eq!(
        settings["selectedInstrumentIds"]["Genshin Impact"],
        "djem-djem-drum"
    );
    assert_eq!(
        settings["selectedLayoutIds"]["Genshin Impact/djem-djem-drum"],
        "drums"
    );
    assert_eq!(
        settings["gameLocations"]["Genshin Impact"],
        "GenshinImpact.exe"
    );
    assert_eq!(settings["ui"]["lastViewedPage"], "studio");
    assert_eq!(settings["ui"]["playback"]["activeSongId"], "SONG-A");
    assert_eq!(settings["ui"]["playback"]["positionSeconds"], 8.5104884);
    assert_eq!(settings["ui"]["playback"]["loopMode"], "single");
    assert_eq!(
        settings["hotkeys"][0],
        json!({"action": "PlayPause", "label": "Play / Pause", "key": "Space", "ctrl": true, "alt": true, "shift": false})
    );
    assert_eq!(db.get_queue_songs().unwrap()[0].id, "SONG-A");
    assert_eq!(db.get_queue_songs().unwrap().len(), 1);
    assert_eq!(
        db.get_all_songs().unwrap()[0].title.as_deref(),
        Some("Keep song")
    );
    assert_eq!(
        fs::read_to_string(f.root.join("user.config")).unwrap(),
        original
    );
    let saved: Value =
        serde_json::from_slice(&fs::read(f.root.join("settings.json")).unwrap()).unwrap();
    assert_eq!(saved["_legacyUserConfig"]["FutureLegacyOption"], "Keep me");
    assert!(settings.get("_legacyUserConfig").is_none());
}

#[test]
fn database_preferences_move_to_json_and_existing_json_wins_over_both_sources() {
    let f = Fixture::new();
    let db = f.db();
    f.old_preferences(json!({"theme": "Dark", "repressGapMs": 45, "ui": {"studio": {"zoomX": 300}, "locale": "zh-CN"}}));
    f.config(&[("AppTheme", "0"), ("KeyboardPressDelayMs", "50")]);
    fs::write(
        f.root.join("settings.json"),
        json!({"keyPressDelayMs": 70, "ui": {"locale": "en-US"}}).to_string(),
    )
    .unwrap();
    let store = f.open(&db).unwrap();
    let mut expected = SettingsStore::defaults();
    expected["theme"] = json!("Light");
    expected["keyPressDelayMs"] = json!(70);
    expected["repressGapMs"] = json!(45);
    expected["ui"]["studio"]["zoomX"] = json!(300);
    expected["ui"]["locale"] = json!("en-US");
    assert_eq!(store.get().unwrap(), expected);
    assert_eq!(db.legacy_app_settings().unwrap(), None);
    // Neither restarting nor resetting should reimport a stale user.config.
    store.update(&json!({"theme": "System"})).unwrap();
    assert_eq!(f.open(&db).unwrap().get().unwrap()["theme"], "System");
    store.reset().unwrap();
    assert_eq!(
        f.open(&db).unwrap().get().unwrap(),
        SettingsStore::defaults()
    );
}

#[test]
fn invalid_sources_do_not_destroy_the_database_or_an_existing_settings_file() {
    let f = Fixture::new();
    let db = f.db();
    f.old_preferences(json!({"volume": 42}));
    fs::write(
        f.root.join("user.config"),
        "<configuration><userSettings><MidiFolder>",
    )
    .unwrap();
    assert!(f.open(&db).is_err());
    assert_eq!(db.legacy_app_settings().unwrap().unwrap()["volume"], 42);
    assert!(!f.root.join("settings.json").exists());
    fs::remove_file(f.root.join("user.config")).unwrap();
    fs::write(f.root.join("settings.json"), "bad json").unwrap();
    assert!(f.open(&db).is_err());
    assert_eq!(
        fs::read_to_string(f.root.join("settings.json")).unwrap(),
        "bad json"
    );
    assert!(db.legacy_app_settings().unwrap().is_some());
}

#[test]
fn standard_dotnet_xml_cdata_and_invalid_typed_values_are_handled_without_guessing() {
    let f = Fixture::new();
    let db = f.db();
    fs::write(f.root.join("user.config"), r#"<configuration><userSettings><AutoMidiPlayer.Properties.Settings>
        <setting name="MidiFolder" serializeAs="String"><value><![CDATA[C:\MIDI & Songs]]></value></setting>
        <setting name="DefaultSongSpeed" serializeAs="String"><value>NaN</value></setting>
        <setting name="KeyboardPressDelayMs" serializeAs="String"><value>-1</value></setting>
        <setting name="AutoCheckUpdates" serializeAs="String"><value>maybe</value></setting>
        </AutoMidiPlayer.Properties.Settings></userSettings></configuration>"#).unwrap();
    let mut expected = SettingsStore::defaults();
    expected["midiFolder"] = json!(r"C:\MIDI & Songs");
    assert_eq!(f.open(&db).unwrap().get().unwrap(), expected);
}

#[test]
fn concurrent_patches_keep_all_changes_in_the_json_file() {
    let f = Fixture::new();
    let db = f.db();
    let store = Arc::new(f.open(&db).unwrap());
    let handles = (0..8)
        .map(|i| {
            let store = store.clone();
            std::thread::spawn(move || {
                store
                    .update(&json!({"ui": {format!("test{i}"): i}}))
                    .unwrap();
            })
        })
        .collect::<Vec<_>>();
    for thread in handles {
        thread.join().unwrap();
    }
    let saved = f.open(&db).unwrap().get().unwrap();
    for i in 0..8 {
        assert_eq!(saved["ui"][format!("test{i}")], i);
    }
    assert!(store.update(&json!({"_migrations": null})).is_err());
}

#[test]
fn archived_game_and_layout_selections_can_resolve_after_preferences_were_already_imported() {
    let f = Fixture::new();
    let db = f.db();
    f.config(&[
        ("ActiveGenshin", "True"),
        (
            "SelectedInstrumentByGame",
            r#"{"Genshin Impact":"Genshin Impact:Djem Djem Drum"}"#,
        ),
        ("SelectedLayoutName", "QWERTY (Drums)"),
    ]);
    let first =
        SettingsStore::open(f.root.join("settings.json"), &db, &[], &HashMap::new()).unwrap();
    assert!(first.get().unwrap().get("activeGameId").is_none());
    let resolved = f.open(&db).unwrap().get().unwrap();
    assert_eq!(resolved["activeGameId"], "Genshin Impact");
    assert_eq!(
        resolved["selectedInstrumentIds"]["Genshin Impact"],
        "djem-djem-drum"
    );
    assert_eq!(
        resolved["selectedLayoutIds"]["Genshin Impact/djem-djem-drum"],
        "drums"
    );
}

#[test]
fn missing_legacy_settings_use_app_defaults_and_are_written_to_json() {
    let f = Fixture::new();
    let db = f.db();
    f.config(&[
        ("DefaultSongHoldNotes", "False"),
        ("KeyboardPressDelayMs", "50"),
    ]);
    let store = f.open(&db).unwrap();
    let settings = store.get().unwrap();
    let defaults = SettingsStore::defaults();
    assert_eq!(settings["overlapBehavior"], "retrigger");
    assert_eq!(settings["repressGapMs"], 20);
    assert_eq!(settings["defaultTapThresholdMs"], 0);
    assert_eq!(
        settings["ui"]["animationTiming"],
        defaults["ui"]["animationTiming"]
    );
    // Actual legacy values still override defaults, including false and zero.
    assert_eq!(settings["defaultHoldNotes"], false);
    assert_eq!(settings["keyPressDelayMs"], 50);
    let saved: Value =
        serde_json::from_slice(&fs::read(f.root.join("settings.json")).unwrap()).unwrap();
    for key in defaults.as_object().unwrap().keys() {
        assert!(saved.get(key).is_some(), "Missing persisted default: {key}");
    }
    assert_eq!(saved["repressGapMs"], 20);
    store.update(&json!({"repressGapMs": 75})).unwrap();
    assert_eq!(f.open(&db).unwrap().get().unwrap()["repressGapMs"], 75);
}
