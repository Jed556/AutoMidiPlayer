use amp_core::library::LibraryDatabase;
use serde_json::json;
use amp_core::settings::SettingsStore;
use std::collections::HashMap;

#[test]
fn launch_settings_round_trip_without_exposing_or_overwriting_internal_metadata() {
    let directory = std::env::temp_dir().join(format!("amp_launch_settings_{}", uuid::Uuid::new_v4()));
    std::fs::create_dir_all(&directory).unwrap();
    let path = directory.join("settings.json");
    let document = json!({
        "onboardingCompleted": false,
        "theme": "Dark",
        "ui": { "locale": "zh-CN" },
        "_migrations": { "userConfig": true, "userConfigSelections": true },
        "_legacyUserConfig": { "Theme": "Dark" },
        "_futureMetadata": { "version": 1 }
    });
    let bytes = serde_json::to_vec_pretty(&document).unwrap();
    std::fs::write(&path, &bytes).unwrap();

    let mut launch = SettingsStore::read_for_launch(&path).unwrap();
    assert!(launch
        .as_object()
        .unwrap()
        .keys()
        .all(|key| !key.starts_with('_')));
    assert_eq!(launch["theme"], "Dark");
    assert_eq!(launch["ui"]["locale"], "zh-CN");
    assert_eq!(
        launch["ui"]["studio"],
        SettingsStore::defaults()["ui"]["studio"]
    );
    assert_eq!(
        std::fs::read(&path).unwrap(), bytes,
        "Setup reads must not rewrite settings"
    );
    assert_eq!(
        std::fs::read_dir(&directory).unwrap().count(), 1,
        "Setup reads must not create a library"
    );

    let library = LibraryDatabase::new(Some(directory.join("library.db"))).unwrap();
    let store = SettingsStore::open(path.clone(), &library, &[], &HashMap::new()).unwrap();
    assert_eq!(launch, store.get().unwrap());
    // Finish echoes the preferences read before the native runtime started.
    launch["onboardingCompleted"] = json!(true);
    store.update(&launch).unwrap();
    for key in ["_migrations", "_legacyUserConfig", "_futureMetadata"] {
        assert!(store.update(&json!({ key: {} })).is_err());
    }
    let saved: serde_json::Value = serde_json::from_slice(&std::fs::read(&path).unwrap()).unwrap();
    assert_eq!(saved["onboardingCompleted"], true);
    for key in ["_legacyUserConfig", "_futureMetadata"] {
        assert_eq!(saved[key], document[key]);
    }
    assert_eq!(saved["_migrations"]["userConfig"], true);
    assert_eq!(saved["_migrations"]["userConfigSelections"], true);

    drop(store);
    drop(library);
    std::fs::remove_dir_all(directory).unwrap();
}

#[test]
fn launch_settings_use_defaults_without_creating_files() {
    let directory = std::env::temp_dir().join(format!("amp_launch_defaults_{}", uuid::Uuid::new_v4()));
    assert_eq!(
        SettingsStore::read_for_launch(&directory.join("settings.json")).unwrap(),
        SettingsStore::defaults()
    );
    assert!(!directory.exists());
}

#[test]
fn launch_settings_reject_malformed_or_non_object_documents() {
    let directory = std::env::temp_dir().join(format!("amp_launch_invalid_{}", uuid::Uuid::new_v4()));
    std::fs::create_dir_all(&directory).unwrap();
    let path = directory.join("settings.json");
    for contents in ["{", "null", "[]", "true"] {
        std::fs::write(&path, contents).unwrap();
        assert!(SettingsStore::read_for_launch(&path).is_err());
        assert_eq!(std::fs::read_to_string(&path).unwrap(), contents);
    }
    std::fs::remove_dir_all(directory).unwrap();
}

#[test]
fn settings_merge_persist_and_reset() {
    let directory = std::env::temp_dir().join(format!("amp_settings_{}", uuid::Uuid::new_v4()));
    let path = directory.join("settings.json");
    let library = LibraryDatabase::new(Some(directory.join("library.db"))).unwrap();
    {
        let db = SettingsStore::open(path.clone(), &library, &[], &HashMap::new()).unwrap();
        assert_eq!(db.get().unwrap(), SettingsStore::defaults());
        db.update(&json!({
            "theme": "Dark",
            "overlapBehavior": "retrigger",
            "gamePreviewFollowNoteTiming": true,
            "activeGameId": "game",
            "ui": { "locale": "zh-CN", "songColumnWidths": null }
        })).unwrap();
        db.update(&json!({
            "ui": { "libraryWidth": 340, "songColumnWidths": { "album": 180 } }
        })).unwrap();
        db.update(&json!({
            "ui": { "songColumnWidths": { "bpm": 90 } }
        })).unwrap();
    }
    {
        let db = SettingsStore::open(path.clone(), &library, &[], &HashMap::new()).unwrap();
        let mut expected = SettingsStore::defaults();
        expected["theme"] = json!("Dark");
        expected["overlapBehavior"] = json!("retrigger");
        expected["gamePreviewFollowNoteTiming"] = json!(true);
        expected["activeGameId"] = json!("game");
        expected["ui"]["locale"] = json!("zh-CN");
        expected["ui"]["libraryWidth"] = json!(340);
        expected["ui"]["songColumnWidths"] = json!({"album": 180, "bpm": 90});
        assert_eq!(db.get().unwrap(), expected);
        db.reset().unwrap();
        assert_eq!(db.get().unwrap(), SettingsStore::defaults());
    }
    {
        let db = SettingsStore::open(path.clone(), &library, &[], &HashMap::new()).unwrap();
        assert_eq!(db.get().unwrap(), SettingsStore::defaults());
    }
    drop(library);
    std::fs::remove_dir_all(directory).unwrap();
}
