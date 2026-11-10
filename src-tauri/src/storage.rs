use std::{
    fs,
    path::{Path, PathBuf},
    sync::Mutex,
    time::{SystemTime, UNIX_EPOCH},
};

// Serialize scans and cleanup so the UI never measures a half-finished cleanup.
static STORAGE_LOCK: Mutex<()> = Mutex::new(());

#[derive(serde::Serialize, Debug, PartialEq, Eq)]
#[serde(rename_all = "camelCase")]
pub struct StorageUsage {
    pub app_data_bytes: u64,
    pub web_view_cache_bytes: u64,
    pub online_midi_bytes: u64,
    pub discover_cache_bytes: u64,
    pub total_bytes: u64,
}

struct StoredFile {
    path: PathBuf,
    bytes: u64,
    accessed: SystemTime,
}

fn is_link(meta: &fs::Metadata) -> bool {
    #[cfg(windows)]
    {
        use std::os::windows::fs::MetadataExt;
        if meta.file_attributes() & 0x400 != 0 {
            return true;
        }
    }
    meta.file_type().is_symlink()
}

fn files(root: &Path) -> Result<Vec<StoredFile>, String> {
    let mut result = Vec::new();
    let mut pending = vec![root.to_path_buf()];
    while let Some(path) = pending.pop() {
        let meta = match fs::symlink_metadata(&path) {
            Ok(meta) => meta,
            Err(error) if error.kind() == std::io::ErrorKind::NotFound => continue,
            Err(error) => return Err(format!("Unable to read storage: {error}")),
        };
        // Never follow junctions or symlinks outside application-owned storage.
        if is_link(&meta) {
            continue;
        }
        if meta.is_dir() {
            for entry in fs::read_dir(path).map_err(|e| e.to_string())? {
                pending.push(entry.map_err(|e| e.to_string())?.path());
            }
        } else if meta.is_file() {
            result.push(StoredFile {
                path,
                bytes: meta.len(),
                accessed: meta
                    .accessed()
                    .or_else(|_| meta.modified())
                    .unwrap_or(UNIX_EPOCH),
            });
        }
    }
    Ok(result)
}

fn is_webview_cache(path: &Path, root: &Path) -> bool {
    let ebwebview = root.join("EBWebView");
    path.starts_with(&ebwebview)
}

fn delete_legacy_ebwebview_backups(root: &Path) {
    if let Ok(entries) = fs::read_dir(root) {
        for entry in entries.flatten() {
            let name = entry.file_name();
            let name_str = name.to_string_lossy();
            if name_str.starts_with("EBWebView.legacy") && name_str.ends_with(".bak") {
                let path = entry.path();
                if let Ok(meta) = fs::symlink_metadata(&path) {
                    if !is_link(&meta) && meta.is_dir() {
                        let _ = fs::remove_dir_all(path);
                    }
                }
            }
        }
    }
}

fn measure(root: &Path) -> Result<StorageUsage, String> {
    let online = root.join("OnlineMidi");
    let discover = root.join("cache").join("discover");
    let mut usage = StorageUsage {
        app_data_bytes: 0,
        web_view_cache_bytes: 0,
        online_midi_bytes: 0,
        discover_cache_bytes: 0,
        total_bytes: 0,
    };
    for file in files(root)? {
        if file.path.starts_with(&online) {
            usage.online_midi_bytes += file.bytes;
        } else if file.path.starts_with(&discover) {
            usage.discover_cache_bytes += file.bytes;
        } else if is_webview_cache(&file.path, root) {
            usage.web_view_cache_bytes += file.bytes;
        } else {
            usage.app_data_bytes += file.bytes;
        }
        usage.total_bytes += file.bytes;
    }
    Ok(usage)
}

pub fn usage() -> Result<StorageUsage, String> {
    let _lock = STORAGE_LOCK.lock().map_err(|e| e.to_string())?;
    measure(&amp_core::paths::app_data_dir())
}

fn cleanup_candidates(mut cached: Vec<StoredFile>, clear: bool, max_bytes: u64) -> Vec<PathBuf> {
    cached.sort_by_key(|file| file.accessed);
    let mut size: u64 = cached.iter().map(|file| file.bytes).sum();
    cached
        .into_iter()
        .filter_map(|file| {
            if !clear && (max_bytes == 0 || size <= max_bytes) {
                return None;
            }
            size = size.saturating_sub(file.bytes);
            Some(file.path)
        })
        .collect()
}

fn clean_with(
    root: &Path,
    settings: &serde_json::Value,
    force: bool,
    recycle: impl FnOnce(&[PathBuf]) -> Result<(), String>,
) -> Result<u64, String> {
    // Check ancestors too: a cache junction must never redirect cleanup into user files.
    for directory in [
        root.to_path_buf(),
        root.join("cache"),
        root.join("cache/discover"),
    ] {
        match fs::symlink_metadata(&directory) {
            Ok(meta) if is_link(&meta) || !meta.is_dir() => {
                return Err("Storage cleanup cannot follow linked directories".into())
            }
            Ok(_) => {}
            Err(error) if error.kind() == std::io::ErrorKind::NotFound => {}
            Err(error) => return Err(error.to_string()),
        }
    }
    let cache = root.join("cache").join("discover");
    let marker = root.join("cache").join(".discover-last-clean");
    if fs::symlink_metadata(&marker).is_ok_and(|meta| is_link(&meta) || !meta.is_file()) {
        return Err("Invalid cache cleanup marker".into());
    }
    let interval_days = match settings
        .get("cacheAutoClean")
        .and_then(|v| v.as_str())
        .unwrap_or_else(|| amp_core::settings::defaults::document()["cacheAutoClean"].as_str().expect("Invalid cache default"))
    {
        "daily" => Some(1),
        "weekly" => Some(7),
        "monthly" => Some(30),
        _ => None,
    };
    let now = SystemTime::now();
    // No marker means start the interval now, rather than immediately clearing a legacy cache.
    let previous = fs::read_to_string(&marker)
        .ok()
        .and_then(|v| v.parse::<u64>().ok());
    let due = interval_days.zip(previous).is_some_and(|(days, stamp)| {
        now.duration_since(UNIX_EPOCH)
            .unwrap_or_default()
            .as_secs()
            .saturating_sub(stamp)
            >= days * 86400
    });
    let max_bytes = settings
        .get("cacheMaxSizeMb")
        .and_then(|v| v.as_u64())
        .unwrap_or_else(|| amp_core::settings::defaults::value("/cacheMaxSizeMb"))
        .saturating_mul(1024 * 1024);
    let cached = files(&cache)?;
    let before: u64 = cached.iter().map(|file| file.bytes).sum();
    let candidates = cleanup_candidates(cached, force || due, max_bytes);
    // Paths come only from a non-following walk of the fixed Discover cache root.
    // Keep saved OnlineMidi files, database, configurations, logs and updater data intact.
    if !candidates.is_empty() {
        recycle(&candidates)?;
    }
    if force || due || previous.is_none() {
        fs::create_dir_all(root.join("cache")).map_err(|e| e.to_string())?;
        fs::write(
            marker,
            now.duration_since(UNIX_EPOCH)
                .unwrap_or_default()
                .as_secs()
                .to_string(),
        )
        .map_err(|e| e.to_string())?;
    }
    let after: u64 = files(&cache)?.iter().map(|file| file.bytes).sum();
    Ok(before.saturating_sub(after))
}

fn clean(root: &Path, settings: &serde_json::Value, force: bool) -> Result<u64, String> {
    delete_legacy_ebwebview_backups(root);
    clean_with(root, settings, force, |paths| {
        trash::delete_all(paths).map_err(|e| format!("Unable to recycle cached files: {e}"))
    })
}

pub fn clear_cache() -> Result<u64, String> {
    let _lock = STORAGE_LOCK.lock().map_err(|e| e.to_string())?;
    clean(
        &amp_core::paths::app_data_dir(),
        &serde_json::Value::Null,
        true,
    )
}

/// Only these application-owned payloads are cleared. Preserve live WebView
/// files, logs, installed SoundFonts, updater files, and legacy archives.
pub fn clear_data_files() -> Result<(), String> {
    let _lock = STORAGE_LOCK.lock().map_err(|e| e.to_string())?;
    clear_data_files_with(&amp_core::paths::app_data_dir(), |paths| {
        trash::delete_all(paths).map_err(|e| format!("Unable to recycle app data: {e}"))
    })
}

fn clear_data_files_with(root: &Path, recycle: impl FnOnce(&[PathBuf]) -> Result<(), String>) -> Result<(), String> {
    for path in [root.to_path_buf(), root.join("cache"), root.join("OnlineMidi"), root.join("cache/discover")] {
        match fs::symlink_metadata(&path) {
            Ok(meta) if is_link(&meta) => return Err(format!("Cannot clear linked app storage: {}", path.display())),
            Ok(_) => {},
            Err(error) if error.kind() == std::io::ErrorKind::NotFound => {},
            Err(error) => return Err(error.to_string()),
        }
    }
    let paths = ["OnlineMidi", "cache/discover", "cache/.discover-last-clean"].map(|name| root.join(name));
    let paths: Vec<_> = paths.into_iter().filter(|path| path.exists()).collect();
    // Recycle only fixed application-owned paths, never targets from settings.
    if !paths.is_empty() { recycle(&paths)?; }
    Ok(())
}

pub fn maintain(settings: &serde_json::Value) -> Result<(), String> {
    let _lock = STORAGE_LOCK.lock().map_err(|e| e.to_string())?;
    clean(&amp_core::paths::app_data_dir(), settings, false).map(|_| ())
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::time::Duration;

    #[test]
    fn clear_data_only_recycles_downloads_and_discover_cache() {
        let root = std::env::temp_dir().join(format!("amp-clear-{}-{}", std::process::id(),
            SystemTime::now().duration_since(UNIX_EPOCH).unwrap().as_nanos()));
        for path in ["OnlineMidi/song.mid", "cache/discover/preview.mid", "cache/.discover-last-clean",
            "cache/update/installer.exe", "SoundFonts/custom.sf2", "EBWebView/Default/data",
            "logs/app.log", "Legacy/old.db", "AutoMidiPlayer.db", "settings.json"] {
            let file = root.join(path);
            fs::create_dir_all(file.parent().unwrap()).unwrap();
            fs::write(file, b"kept").unwrap();
        }
        clear_data_files_with(&root, |paths| {
            assert_eq!(paths, [root.join("OnlineMidi"), root.join("cache/discover"), root.join("cache/.discover-last-clean")]);
            for path in paths {
                if path.is_dir() { fs::remove_dir_all(path).unwrap(); }
                else { fs::remove_file(path).unwrap(); }
            }
            Ok(())
        }).unwrap();
        assert!(!root.join("OnlineMidi").exists());
        assert!(!root.join("cache/discover").exists());
        for path in ["cache/update/installer.exe", "SoundFonts/custom.sf2", "EBWebView/Default/data",
            "logs/app.log", "Legacy/old.db", "AutoMidiPlayer.db", "settings.json"] {
            assert_eq!(fs::read(root.join(path)).unwrap(), b"kept");
        }
        fs::remove_dir_all(root).unwrap();
    }

    #[test]
    fn categories_are_disjoint_and_missing_root_is_empty() {
        let root = std::env::temp_dir().join(format!(
            "amp-storage-{}-{}",
            std::process::id(),
            SystemTime::now()
                .duration_since(UNIX_EPOCH)
                .unwrap()
                .as_nanos()
        ));
        assert_eq!(measure(&root).unwrap().total_bytes, 0);
        fs::create_dir_all(root.join("OnlineMidi")).unwrap();
        fs::create_dir_all(root.join("cache/discover/MidiShow")).unwrap();
        fs::create_dir_all(root.join("cache/update")).unwrap();
        fs::create_dir_all(root.join("EBWebView/Default")).unwrap();
        fs::write(root.join("AutoMidiPlayer.db"), [0; 11]).unwrap();
        fs::write(root.join("OnlineMidi/saved.mid"), [0; 23]).unwrap();
        fs::write(root.join("cache/discover/MidiShow/preview.mid"), [0; 37]).unwrap();
        fs::write(root.join("cache/update/installer.exe"), [0; 5]).unwrap();
        fs::write(root.join("EBWebView/Default/data"), [0; 42]).unwrap();
        let usage = measure(&root).unwrap();
        assert_eq!(
            (
                usage.app_data_bytes,
                usage.web_view_cache_bytes,
                usage.online_midi_bytes,
                usage.discover_cache_bytes,
                usage.total_bytes
            ),
            (16, 42, 23, 37, 118)
        );
        fs::remove_dir_all(root).unwrap();
    }

    #[test]
    fn removes_legacy_ebwebview_backups() {
        let root = std::env::temp_dir().join(format!(
            "amp-legacy-ebwebview-{}-{}",
            std::process::id(),
            SystemTime::now()
                .duration_since(UNIX_EPOCH)
                .unwrap()
                .as_nanos()
        ));
        fs::create_dir_all(root.join("EBWebView.legacy.bak")).unwrap();
        fs::create_dir_all(root.join("EBWebView.legacy.1.bak")).unwrap();
        fs::create_dir_all(root.join("EBWebView")).unwrap();
        fs::write(root.join("EBWebView.legacy.bak/file"), [0; 10]).unwrap();
        delete_legacy_ebwebview_backups(&root);
        assert!(!root.join("EBWebView.legacy.bak").exists());
        assert!(!root.join("EBWebView.legacy.1.bak").exists());
        assert!(root.join("EBWebView").exists());
        fs::remove_dir_all(root).unwrap();
    }

    #[test]
    fn limit_evicts_oldest_first_and_never_disables_size_limit() {
        let make_files = || {
            vec![
                StoredFile {
                    path: "new".into(),
                    bytes: 40,
                    accessed: UNIX_EPOCH + Duration::from_secs(2),
                },
                StoredFile {
                    path: "old".into(),
                    bytes: 30,
                    accessed: UNIX_EPOCH,
                },
            ]
        };
        assert_eq!(
            cleanup_candidates(make_files(), false, 50),
            vec![PathBuf::from("old")]
        );
        assert!(cleanup_candidates(make_files(), false, 70).is_empty());
        assert!(cleanup_candidates(make_files(), false, 0).is_empty());
        assert_eq!(cleanup_candidates(make_files(), true, 0).len(), 2);
    }
    #[test]
    fn cleanup_policy_preserves_saved_files_and_only_advances_marker_on_success() {
        let root = std::env::temp_dir().join(format!(
            "amp-clean-{}-{}",
            std::process::id(),
            SystemTime::now()
                .duration_since(UNIX_EPOCH)
                .unwrap()
                .as_nanos()
        ));
        fs::create_dir_all(root.join("cache/discover/MidiShow")).unwrap();
        fs::create_dir_all(root.join("cache/update")).unwrap();
        fs::create_dir_all(root.join("OnlineMidi")).unwrap();
        let cached = root.join("cache/discover/MidiShow/preview.mid");
        let marker = root.join("cache/.discover-last-clean");
        fs::write(&cached, [0; 37]).unwrap();
        fs::write(root.join("AutoMidiPlayer.db"), [0; 11]).unwrap();
        fs::write(root.join("OnlineMidi/saved.mid"), [0; 23]).unwrap();
        fs::write(root.join("cache/update/installer.exe"), [0; 5]).unwrap();
        let settings = serde_json::json!({ "cacheAutoClean": "daily", "cacheMaxSizeMb": 500 });
        clean_with(&root, &settings, false, |_| {
            panic!("First run starts the interval")
        })
        .unwrap();
        fs::write(&marker, "0").unwrap();
        let never = serde_json::json!({ "cacheAutoClean": "never", "cacheMaxSizeMb": 500 });
        clean_with(&root, &never, false, |_| {
            panic!("Never must disable scheduled cleanup")
        })
        .unwrap();
        assert!(clean_with(&root, &settings, false, |_| Err("Locked cache".into())).is_err());
        assert_eq!(fs::read_to_string(&marker).unwrap(), "0");
        let removed = clean_with(&root, &settings, false, |paths| {
            assert_eq!(paths, &[cached.clone()]);
            for path in paths {
                fs::remove_file(path).unwrap();
            }
            Ok(())
        })
        .unwrap();
        assert_eq!(removed, 37);
        assert!(!cached.exists());
        assert_eq!(measure(&root).unwrap().online_midi_bytes, 23);
        assert!(root.join("AutoMidiPlayer.db").exists());
        assert!(root.join("cache/update/installer.exe").exists());
        assert_ne!(fs::read_to_string(marker).unwrap(), "0");
        fs::remove_dir_all(root).unwrap();
    }
}
