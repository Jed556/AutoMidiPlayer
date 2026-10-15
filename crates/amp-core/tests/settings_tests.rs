use amp_core::library::LibraryDatabase;
use serde_json::json;

#[test]
fn settings_merge_persist_and_reset() {
    let path = std::env::temp_dir().join(format!("amp_settings_{}.db", uuid::Uuid::new_v4()));
    {
        let db = LibraryDatabase::new(Some(path.clone())).unwrap();
        assert_eq!(db.get_app_settings().unwrap(), json!({}));
        db.update_app_settings(&json!({
            "theme": "Dark",
            "overlapBehavior": "retrigger",
            "activeGameId": "game",
            "ui": { "locale": "zh-CN", "songColumnWidths": null }
        })).unwrap();
        db.update_app_settings(&json!({
            "ui": { "libraryWidth": 340, "songColumnWidths": { "album": 180 } }
        })).unwrap();
        db.update_app_settings(&json!({
            "ui": { "songColumnWidths": { "bpm": 90 } }
        })).unwrap();
    }
    {
        let db = LibraryDatabase::new(Some(path.clone())).unwrap();
        assert_eq!(db.get_app_settings().unwrap(), json!({
            "theme": "Dark",
            "overlapBehavior": "retrigger",
            "activeGameId": "game",
            "ui": {
                "locale": "zh-CN",
                "libraryWidth": 340,
                "songColumnWidths": { "album": 180, "bpm": 90 }
            }
        }));
        db.reset_app_settings().unwrap();
        assert_eq!(db.get_app_settings().unwrap(), json!({}));
    }
    {
        let db = LibraryDatabase::new(Some(path.clone())).unwrap();
        assert_eq!(db.get_app_settings().unwrap(), json!({}));
    }
    std::fs::remove_file(path).unwrap();
}
