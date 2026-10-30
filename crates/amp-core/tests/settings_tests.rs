use amp_core::library::LibraryDatabase;
use serde_json::json;
use amp_core::settings::SettingsStore;
use std::collections::HashMap;

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
