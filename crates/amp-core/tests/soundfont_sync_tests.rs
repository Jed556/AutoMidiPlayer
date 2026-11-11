use amp_core::DirectoryWatcher;
use std::{fs, path::PathBuf, sync::mpsc, time::Duration};

struct TestDirectory(PathBuf);
impl TestDirectory {
    fn new() -> Self {
        let path = std::env::temp_dir().join(format!("amp_soundfont_sync_{}", uuid::Uuid::new_v4()));
        fs::create_dir_all(&path).unwrap();
        Self(path)
    }
}
impl Drop for TestDirectory {
    fn drop(&mut self) {
        assert_eq!(self.0.parent(), Some(std::env::temp_dir().as_path()));
        let _ = fs::remove_dir_all(&self.0);
    }
}

#[test]
fn soundfont_watcher_detects_deletions_renames_and_directory_recreation() {
    let temp = TestDirectory::new();
    let root = temp.0.canonicalize().unwrap().join("SoundFonts");
    fs::create_dir(&root).unwrap();
    let font = root.join("Stock.sf2");
    fs::write(&font, b"test font").unwrap();
    let (tx, rx) = mpsc::channel();
    let _watcher = DirectoryWatcher::watch_soundfonts(&root, move || { let _ = tx.send(()); }).unwrap();
    // Reading a font and changing unrelated files must not trigger a reload loop.
    fs::read(&font).unwrap();
    fs::write(root.join("LICENSE.txt"), b"license").unwrap();
    fs::write(temp.0.join("Other.sf2"), b"unrelated").unwrap();
    assert!(rx.recv_timeout(Duration::from_millis(800)).is_err());
    fs::remove_file(&font).unwrap();
    rx.recv_timeout(Duration::from_secs(5)).expect("file deletion");
    fs::write(&font, b"restored font").unwrap();
    rx.recv_timeout(Duration::from_secs(5)).expect("file restoration");
    fs::rename(&font, root.join("Renamed.SF2")).unwrap();
    rx.recv_timeout(Duration::from_secs(5)).expect("file rename");
    fs::rename(&root, temp.0.join("RemovedSoundFonts")).unwrap();
    rx.recv_timeout(Duration::from_secs(5)).expect("directory removal");
    fs::create_dir(&root).unwrap();
    fs::write(root.join("New.sf2"), b"new font").unwrap();
    rx.recv_timeout(Duration::from_secs(5)).expect("directory recreation");
}

#[test]
fn soundfont_watcher_can_start_with_a_missing_directory() {
    let temp = TestDirectory::new();
    let root = temp.0.join("SoundFonts");
    let (tx, rx) = mpsc::channel();
    let _watcher = DirectoryWatcher::watch_soundfonts(&root, move || { let _ = tx.send(()); }).unwrap();
    fs::create_dir(&root).unwrap();
    fs::write(root.join("Custom.sf2"), b"new font").unwrap();
    rx.recv_timeout(Duration::from_secs(5)).expect("missing directory created");
}

#[test]
fn source_file_watcher_ignores_other_fonts_and_detects_removal_and_restoration() {
    let temp = TestDirectory::new();
    let root = temp.0.canonicalize().unwrap();
    let font = root.join("Source.sf2");
    fs::write(&font, b"source").unwrap();
    let (tx, rx) = mpsc::channel();
    let _watcher = DirectoryWatcher::watch_soundfont_file(&font, move || { let _ = tx.send(()); }).unwrap();
    fs::read(&font).unwrap();
    fs::write(root.join("Unrelated.sf2"), b"other").unwrap();
    assert!(rx.recv_timeout(Duration::from_millis(800)).is_err());
    fs::remove_file(&font).unwrap();
    rx.recv_timeout(Duration::from_secs(5)).expect("source removed");
    fs::write(&font, b"restored").unwrap();
    rx.recv_timeout(Duration::from_secs(5)).expect("source restored");
}

#[test]
fn registered_source_paths_persist_without_copying_and_removal_preserves_source() {
    use amp_core::{settings::{SettingsStore, soundfonts}, LibraryDatabase};
    use std::collections::HashMap;
    let temp = TestDirectory::new();
    let library = LibraryDatabase::new(Some(temp.0.join("library.db"))).unwrap();
    let settings_path = temp.0.join("settings.json");
    let settings = SettingsStore::open(settings_path.clone(), &library, &[], &HashMap::new()).unwrap();
    let source = PathBuf::from(concat!(env!("CARGO_MANIFEST_DIR"), "/../../src-tauri/resources/soundfonts/Roland GM.sf2"));
    let registered = soundfonts::add(&settings, &source).unwrap();
    assert_eq!(PathBuf::from(&registered), source.canonicalize().unwrap());
    assert_eq!(soundfonts::add(&settings, &source).unwrap(), registered);
    assert_eq!(soundfonts::paths(&settings).unwrap(), vec![registered.clone()]);
    assert!(!temp.0.join("SoundFonts").exists());
    drop(settings);
    let settings = SettingsStore::open(settings_path, &library, &[], &HashMap::new()).unwrap();
    assert_eq!(soundfonts::paths(&settings).unwrap(), vec![registered.clone()]);
    soundfonts::remove(&settings, &registered).unwrap();
    assert!(source.is_file());
    assert!(soundfonts::paths(&settings).unwrap().is_empty());
    assert!(soundfonts::add(&settings, &temp.0.join("Missing.sf2")).is_err());
    assert!(soundfonts::paths(&settings).unwrap().is_empty());
}
