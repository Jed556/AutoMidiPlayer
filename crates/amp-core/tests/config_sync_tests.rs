use amp_core::input::config_scan::{scan_games_checked, scan_layouts_checked};
use amp_core::DirectoryWatcher;
use std::{fs, path::{Path, PathBuf}, sync::mpsc, time::Duration};

struct TestDirectory(PathBuf);
impl TestDirectory {
    fn new() -> Self {
        let root = std::env::temp_dir().join(format!("amp_config_sync_{}", uuid::Uuid::new_v4()));
        fs::create_dir_all(&root).unwrap();
        Self(root)
    }
    fn write(&self, relative: &str, content: &str) -> PathBuf {
        let path = self.0.join(relative);
        fs::create_dir_all(path.parent().unwrap()).unwrap();
        fs::write(&path, content).unwrap();
        path
    }
}
impl Drop for TestDirectory {
    fn drop(&mut self) {
        assert_eq!(self.0.parent(), Some(std::env::temp_dir().as_path()));
        let _ = fs::remove_dir_all(&self.0);
    }
}

const GAME: &str = r#"{"id":"NTE","displayName":"Neverness to Everness"}"#;
const INSTRUMENT: &str = r#"{"id":"guitar","name":"Guitar","notes":[60,61],"supportedLayouts":["qwerty"]}"#;
const LAYOUT: &str = r#"{"id":"qwerty","name":"QWERTY","keys":["a","Enter"]}"#;

#[test]
fn scans_additions_renames_and_removals_without_inventing_instruments_or_layouts() {
    let temp = TestDirectory::new();
    temp.write("Games/NTE/game.json", GAME);
    let (games, issues) = scan_games_checked(&temp.0.join("Games"));
    assert!(issues.is_empty());
    assert!(games[0].instruments.is_empty());
    let instrument = temp.write("Games/NTE/Instruments/Strings/Guitar.json", INSTRUMENT);
    let (games, issues) = scan_games_checked(&temp.0.join("Games"));
    assert!(issues.is_empty());
    assert_eq!(games[0].instruments[0].notes, [60, 61]);
    assert_eq!(games[0].instruments[0].category.as_deref(), Some("Strings"));
    fs::rename(&instrument, instrument.with_file_name("Renamed.json")).unwrap();
    assert_eq!(scan_games_checked(&temp.0.join("Games")).0[0].instruments.len(), 1);
    fs::remove_file(instrument.with_file_name("Renamed.json")).unwrap();
    assert!(scan_games_checked(&temp.0.join("Games")).0[0].instruments.is_empty());
    let layout = temp.write("KeyboardLayouts/qwerty.json", LAYOUT);
    assert_eq!(scan_layouts_checked(&temp.0.join("KeyboardLayouts")).0.len(), 1);
    fs::remove_file(layout).unwrap();
    assert!(scan_layouts_checked(&temp.0.join("KeyboardLayouts")).0.is_empty());
    fs::remove_file(temp.0.join("Games/NTE/game.json")).unwrap();
    assert!(scan_games_checked(&temp.0.join("Games")).0.is_empty());
}

#[test]
fn reports_malformed_json_and_duplicate_games_instruments_and_layouts_with_paths() {
    let temp = TestDirectory::new();
    temp.write("Games/NTE/game.json", GAME);
    let malformed = temp.write("Games/NTE/Instruments/Broken.json", "{bad");
    temp.write("Games/NTE/Instruments/Guitar.json", INSTRUMENT);
    let copy = temp.write("Games/NTE/Instruments/GuitarCopy.json", INSTRUMENT);
    temp.write("Games/ZZ NTE Copy/game.json", GAME);
    let (_, issues) = scan_games_checked(&temp.0.join("Games"));
    assert!(issues.iter().any(|i| Path::new(&i.path) == malformed && i.message.contains("line 1")), "{issues:#?}");
    assert!(issues.iter().any(|i| Path::new(&i.path) == copy && i.message.contains("Duplicate instrument ID")), "{issues:#?}");
    assert!(issues.iter().any(|i| i.message.contains("Duplicate game ID")));
    temp.write("KeyboardLayouts/qwerty.json", LAYOUT);
    temp.write("KeyboardLayouts/copy.json", LAYOUT);
    temp.write("KeyboardLayouts/broken.json", "{");
    let (_, issues) = scan_layouts_checked(&temp.0.join("KeyboardLayouts"));
    assert!(issues.iter().any(|i| i.message.contains("Duplicate layout ID")));
    assert!(issues.iter().any(|i| i.message.contains("Malformed JSON")));
}

#[test]
fn detects_layout_alias_collisions_and_duplicate_json_fields() {
    let temp = TestDirectory::new();
    temp.write("KeyboardLayouts/first.json", LAYOUT);
    temp.write("KeyboardLayouts/qwerty.json", r#"{"id":"other","name":"Other","keys":[]}"#);
    temp.write("Games/NTE/game.json", r#"{"id":"NTE","id":"other","displayName":"NTE"}"#);
    assert!(scan_layouts_checked(&temp.0.join("KeyboardLayouts")).1.iter().any(|i| i.message.contains("Duplicate layout alias")));
    assert!(scan_games_checked(&temp.0.join("Games")).1[0].message.contains("duplicate field"));
}

#[test]
fn all_bundled_defaults_pass_validation() {
    let defaults = Path::new(env!("CARGO_MANIFEST_DIR")).join("../../src-tauri/resources/defaults");
    let (games, game_issues) = scan_games_checked(&defaults.join("Games"));
    let (layouts, layout_issues) = scan_layouts_checked(&defaults.join("KeyboardLayouts"));
    assert!(game_issues.is_empty(), "{game_issues:#?}");
    assert!(layout_issues.is_empty(), "{layout_issues:#?}");
    assert!(!games.is_empty() && !layouts.is_empty());
}

#[test]
fn watches_config_bursts_and_root_recreation_but_ignores_other_appdata() {
    let temp = TestDirectory::new();
    let (tx, rx) = mpsc::channel();
    let _watcher = DirectoryWatcher::watch_configs(&temp.0, move || { let _ = tx.send(()); }).unwrap();
    temp.write("settings.json", "{}");
    assert!(rx.recv_timeout(Duration::from_millis(750)).is_err());
    for _ in 0..5 {
        temp.write("Games/NTE/game.json", GAME);
        std::thread::sleep(Duration::from_millis(80));
    }
    rx.recv_timeout(Duration::from_secs(5)).unwrap();
    assert!(rx.recv_timeout(Duration::from_millis(750)).is_err(), "Burst should trigger one reload");
    // Remove only the verified test root's Games child; the watcher stays attached to its parent.
    let games = temp.0.join("Games");
    assert_eq!(games.parent(), Some(temp.0.as_path()));
    fs::remove_dir_all(&games).unwrap();
    rx.recv_timeout(Duration::from_secs(5)).unwrap();
    temp.write("Games/NTE/game.json", GAME);
    rx.recv_timeout(Duration::from_secs(5)).unwrap();
    temp.write("KeyboardLayouts/qwerty.json", LAYOUT);
    rx.recv_timeout(Duration::from_secs(5)).unwrap();
}

#[test]
fn config_removal_is_reported_before_the_debounced_reload() {
    let temp = TestDirectory::new();
    let path = temp.write("Games/NTE/Instruments/Guitar.json", INSTRUMENT);
    let (tx, rx) = mpsc::channel();
    let removed = tx.clone();
    let _watcher = DirectoryWatcher::watch_configs_with_removals(&temp.0,
        move || { let _ = removed.send("removed"); },
        move || { let _ = tx.send("reloaded"); }).unwrap();
    fs::remove_file(path).unwrap();
    assert_eq!(rx.recv_timeout(Duration::from_secs(3)).unwrap(), "removed");
    loop {
        if rx.recv_timeout(Duration::from_secs(3)).unwrap() == "reloaded" { break; }
    }
}

#[test]
fn unavailable_config_stops_playback_and_blocks_native_restarts_until_replaced() {
    use amp_core::{KeyboardLayout, NoteKeyMapping, ParsedMidi, PlaybackEngine, PlaybackStatus, PlayerEvent};
    use amp_core::midi::{TempoMap, TimedNoteEvent};
    let song = ParsedMidi {
        title: "Deleted config guard".into(), duration_us: 20_000_000, bpm: 120.0, ppqn: 480,
        tracks: vec![], events: vec![TimedNoteEvent {
            time_us: 19_000_000, track_index: 0, channel: 0, note: 60, velocity: 80, is_note_on: true,
        }], control_events: vec![], tempo_map: TempoMap::new(480, &[]), key_signatures: vec![],
    };
    let (tx, rx) = mpsc::channel();
    let engine = PlaybackEngine::new(Some(song),
        NoteKeyMapping::from_pairs(KeyboardLayout::Diatonic21, &[]), false, None,
        move |event| { if let PlayerEvent::StateChanged(status) = event { let _ = tx.send(status); } });
    engine.play();
    assert_eq!(rx.recv_timeout(Duration::from_secs(2)).unwrap(), PlaybackStatus::Playing);
    engine.set_config_available(false);
    assert_eq!(rx.recv_timeout(Duration::from_secs(2)).unwrap(), PlaybackStatus::Stopped);
    engine.play();
    assert!(rx.recv_timeout(Duration::from_millis(150)).is_err());
    engine.set_config_available(true);
    assert!(rx.recv_timeout(Duration::from_millis(150)).is_err(), "Restoring a file must not resume automatically");
    engine.play();
    assert_eq!(rx.recv_timeout(Duration::from_secs(2)).unwrap(), PlaybackStatus::Playing);
}
