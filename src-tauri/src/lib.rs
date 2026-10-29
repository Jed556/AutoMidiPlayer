mod config_manager;
mod process_monitor;
mod updates;
mod storage;
pub mod logging;
pub mod telemetry;
use config_manager::{ConfigManager, ConfigSnapshot};
use process_monitor::{
    focus_window_by_pid, GameProcessTarget, GameStatusPayload,
    ProcessMonitor,
};
use std::path::PathBuf;
use std::sync::{Arc, Mutex, RwLock};
use std::str::FromStr;

use tauri::{AppHandle, Emitter, Manager, State};
use tauri_plugin_global_shortcut::{GlobalShortcutExt, Shortcut, ShortcutState};

use amp_core::{
    parse_midi_file, KeyboardLayout, NoteKeyMapping, ParsedMidi, PlaybackEngine,
    PlaybackStatus, PlayerEvent, TransposeMode, AudioDevice, AudioSynth, SynthMode, SoundFont, OverlapBehavior,
    LibraryDatabase, SongRecord, AlbumRecord, PlaylistRecord, SongSettingsUpdate, SongMetadataUpdate,
    SongDefaults, DirectoryWatcher, ScanSummary, scan_folder, import_files,
    LoadedGameBundle, KeyboardLayoutJson,
    KeyChangeMarker, detect_key_changes,
};
use amp_core::library::models::{SongDetailsUpdate, SongSettingsContext, SongSettingsScope};
use amp_core::library::issues::{LibraryFileIssue, FileIssueAction, resolve_file_issue};

#[derive(serde::Deserialize, serde::Serialize, Clone, Debug)]
#[serde(rename_all = "camelCase")]
pub struct HotkeyBindingPayload {
    pub action: String,
    pub label: Option<String>,
    pub key: String,
    pub ctrl: bool,
    pub alt: bool,
    pub shift: bool,
}

pub fn format_shortcut_string(binding: &HotkeyBindingPayload) -> Option<String> {
    let raw_key = binding.key.trim();
    if raw_key.is_empty() {
        return None;
    }

    let lower = raw_key.to_lowercase();
    let normalized_key = match lower.as_str() {
        "space" => "space",
        "up" | "arrowup" => "up",
        "down" | "arrowdown" => "down",
        "left" | "arrowleft" => "left",
        "right" | "arrowright" => "right",
        "escape" | "esc" => "escape",
        "enter" | "return" => "enter",
        "tab" => "tab",
        "backspace" => "backspace",
        "delete" | "del" => "delete",
        "insert" | "ins" => "insert",
        "home" => "home",
        "end" => "end",
        "pageup" | "pgup" => "pageup",
        "pagedown" | "pgdn" => "pagedown",
        k => k,
    };

    let mut parts = Vec::new();
    if binding.ctrl {
        parts.push("ctrl");
    }
    if binding.alt {
        parts.push("alt");
    }
    if binding.shift {
        parts.push("shift");
    }
    parts.push(normalized_key);

    Some(parts.join("+"))
}

pub fn default_hotkey_bindings() -> Vec<HotkeyBindingPayload> {
    vec![
        HotkeyBindingPayload {
            action: "PlayPause".to_string(),
            label: Some("Play / Pause".to_string()),
            key: "Space".to_string(),
            ctrl: true,
            alt: true,
            shift: false,
        },
        HotkeyBindingPayload {
            action: "Next".to_string(),
            label: Some("Next Song".to_string()),
            key: "Right".to_string(),
            ctrl: true,
            alt: true,
            shift: false,
        },
        HotkeyBindingPayload {
            action: "Previous".to_string(),
            label: Some("Previous Song".to_string()),
            key: "Left".to_string(),
            ctrl: true,
            alt: true,
            shift: false,
        },
        HotkeyBindingPayload {
            action: "SpeedUp".to_string(),
            label: Some("Increase Speed".to_string()),
            key: "Up".to_string(),
            ctrl: true,
            alt: true,
            shift: false,
        },
        HotkeyBindingPayload {
            action: "SpeedDown".to_string(),
            label: Some("Decrease Speed".to_string()),
            key: "Down".to_string(),
            ctrl: true,
            alt: true,
            shift: false,
        },
        HotkeyBindingPayload {
            action: "Panic".to_string(),
            label: Some("Panic / Stop All".to_string()),
            key: "Escape".to_string(),
            ctrl: true,
            alt: true,
            shift: false,
        },
    ]
}

pub fn apply_hotkeys_config(
    app: &AppHandle,
    engine: &Arc<PlaybackEngine>,
    enabled: bool,
    bindings: &[HotkeyBindingPayload],
) -> Result<(), String> {
    let global_shortcut = app.global_shortcut();
    let _ = global_shortcut.unregister_all();

    if !enabled {
        return Ok(());
    }

    for binding in bindings {
        let Some(shortcut_str) = format_shortcut_string(binding) else {
            continue;
        };

        let shortcut = match shortcut_str.parse::<Shortcut>() {
            Ok(sc) => sc,
            Err(e) => {
                eprintln!("[Hotkeys] Failed to parse shortcut '{}': {}", shortcut_str, e);
                continue;
            }
        };

        let action = binding.action.clone();
        let app_handle = app.clone();
        let engine_handle = Arc::clone(engine);

        let res = global_shortcut.on_shortcut(shortcut, move |_app, _sc, event| {
            if event.state() == ShortcutState::Pressed {
                match action.as_str() {
                    "PlayPause" => {
                        let _ = app_handle.emit("playback:toggle_play", ());
                    }
                    "Next" => {
                        let _ = app_handle.emit("playback:next", ());
                    }
                    "Previous" => {
                        let _ = app_handle.emit("playback:prev", ());
                    }
                    "SpeedUp" => {
                        let _ = app_handle.emit("playback:speed_up", ());
                    }
                    "SpeedDown" => {
                        let _ = app_handle.emit("playback:speed_down", ());
                    }
                    "Panic" => {
                        engine_handle.panic();
                        let _ = app_handle.emit("playback:panic", ());
                        let _ = app_handle.emit("playback:state_changed", "Stopped");
                    }
                    _ => {}
                }
            }
        });

        if let Err(e) = res {
            eprintln!("[Hotkeys] Failed to register shortcut '{}' for action '{}': {}", shortcut_str, binding.action, e);
        }
    }

    // Hardware media keys
    let app_m_pp = app.clone();
    for s in ["mediaplaypause", "media_play_pause"] {
        if let Ok(sc) = s.parse::<Shortcut>() {
            let app_c = app_m_pp.clone();
            let _ = global_shortcut.on_shortcut(sc, move |_app, _sc, event| {
                if event.state() == ShortcutState::Pressed {
                    let _ = app_c.emit("playback:toggle_play", ());
                }
            });
        }
    }

    let app_m_next = app.clone();
    for s in ["mediatracknext", "media_track_next"] {
        if let Ok(sc) = s.parse::<Shortcut>() {
            let app_c = app_m_next.clone();
            let _ = global_shortcut.on_shortcut(sc, move |_app, _sc, event| {
                if event.state() == ShortcutState::Pressed {
                    let _ = app_c.emit("playback:next", ());
                }
            });
        }
    }

    let app_m_prev = app.clone();
    for s in ["mediatrackprevious", "media_track_prev", "media_track_previous"] {
        if let Ok(sc) = s.parse::<Shortcut>() {
            let app_c = app_m_prev.clone();
            let _ = global_shortcut.on_shortcut(sc, move |_app, _sc, event| {
                if event.state() == ShortcutState::Pressed {
                    let _ = app_c.emit("playback:prev", ());
                }
            });
        }
    }

    Ok(())
}

#[derive(serde::Serialize, Clone, Debug)]
#[serde(rename_all = "camelCase")]
pub struct RollNotePayload {
    pub id: String,
    pub note_number: u8,
    pub pitch: u8,
    pub start_ms: f64,
    pub start_time: f64,
    pub length_ms: f64,
    pub duration: f64,
    pub track_index: usize,
    pub velocity: u8,
}

#[derive(serde::Serialize, Clone, Debug)]
#[serde(rename_all = "camelCase")]
pub struct TrackPayload {
    pub id: usize,
    pub index: usize,
    pub name: String,
    pub instrument_name: String,
    pub program_id: Option<u8>,
    pub channel: u8,
    pub note_count: usize,
    pub is_muted: bool,
    pub is_active: bool,
    pub min_pitch: Option<u8>,
    pub max_pitch: Option<u8>,
    pub avg_pitch: Option<u8>,
    pub start_ms: f64,
    pub duration_ms: f64,
    pub black_key_ratio: f64,
    pub frequent_notes_ratio: f64,
    pub average_duration_ms: f64,
}

#[derive(serde::Serialize, Clone, Debug)]
#[serde(rename_all = "camelCase")]
pub struct MidiFilePayload {
    pub id: String,
    pub title: String,
    pub artist: String,
    pub album: String,
    pub duration_ms: u64,
    pub duration_seconds: f64,
    pub bpm: f64,
    pub tempo: f64,
    pub file_path: String,
    pub tracks: Vec<TrackPayload>,
    pub notes: Vec<RollNotePayload>,
}

pub struct AppState {
    pub engine: Arc<PlaybackEngine>,
    pub current_song: Arc<Mutex<Option<ParsedMidi>>>,
    pub audio_synth: Arc<Mutex<Option<AudioSynth>>>,
    pub db: Arc<LibraryDatabase>,
    pub watcher: Arc<Mutex<Option<DirectoryWatcher>>>,
    pub library_updates: Arc<Mutex<()>>,
    pub process_monitor: Arc<Mutex<ProcessMonitor>>,
    pub config_manager: Arc<Mutex<ConfigManager>>,
    pub config_watcher: Mutex<Option<DirectoryWatcher>>,
    pub config_sources: Arc<Mutex<Vec<PathBuf>>>,
    pub song_defaults: Arc<RwLock<SongDefaults>>,
}

// Window commands
#[tauri::command]
fn minimize_window(window: tauri::Window) {
    let _ = window.minimize();
}

#[tauri::command]
fn maximize_window(window: tauri::Window) -> bool {
    if window.is_maximized().unwrap_or(false) {
        let _ = window.unmaximize();
        false
    } else {
        let _ = window.maximize();
        true
    }
}

#[tauri::command]
fn close_window(window: tauri::Window) {
    let _ = window.close();
}

#[tauri::command]
fn is_maximized(window: tauri::Window) -> bool {
    window.is_maximized().unwrap_or(false)
}

// Playback commands
#[tauri::command]
fn load_midi_file(
    path: String,
    state: State<AppState>,
    app: AppHandle,
) -> Result<MidiFilePayload, String> {
    let payload = read_midi_payload(path, &state, true)?;
    let _ = app.emit("playback:song_loaded", &payload);
    Ok(payload)
}

#[tauri::command]
fn read_midi_file(path: String, state: State<AppState>) -> Result<MidiFilePayload, String> {
    read_midi_payload(path, &state, false)
}

fn read_midi_payload(
    path: String,
    state: &AppState,
    load_for_playback: bool,
) -> Result<MidiFilePayload, String> {
    let path_buf = PathBuf::from(&path);
    if !path_buf.exists() {
        return Err(format!("File does not exist: {}", path));
    }

    let parsed = parse_midi_file(&path_buf).map_err(|e| format!("Failed to parse MIDI: {}", e))?;

    // Reading Studio notes must leave the playback engine and current song alone.
    if load_for_playback {
        state.engine.load_song(parsed.clone());
        *state.current_song.lock().unwrap() = Some(parsed.clone());
    }

    // Resolve song ID, metadata, and disabled tracks from SQLite database
    let (db_id, title, artist, album, disabled_tracks_str) = match state.db.get_song_by_path(&path) {
        Ok(Some(record)) => (
            record.id,
            record.title.unwrap_or_else(|| parsed.title.clone()),
            record.artist.unwrap_or_else(|| "Unknown Artist".to_string()),
            record.album.unwrap_or_default(),
            record.disabled_tracks,
        ),
        _ => {
            if load_for_playback {
                // Auto-import only when loading for playback.
                let def = state.song_defaults.read().unwrap().clone();
                let _ = import_files(&state.db, std::slice::from_ref(&path_buf), Some(&def));
            }
            match state.db.get_song_by_path(&path) {
                Ok(Some(r)) => (
                    r.id,
                    r.title.unwrap_or_else(|| parsed.title.clone()),
                    r.artist.unwrap_or_else(|| "Unknown Artist".to_string()),
                    r.album.unwrap_or_default(),
                    r.disabled_tracks,
                ),
                _ => (
                    format!("{:x}", md5_hash(&path)),
                    parsed.title.clone(),
                    "Unknown Artist".to_string(),
                    "".to_string(),
                    None,
                ),
            }
        }
    };

    let disabled_indices: std::collections::HashSet<usize> = disabled_tracks_str
        .as_deref()
        .unwrap_or("")
        .split(',')
        .filter_map(|s| s.trim().parse::<usize>().ok())
        .collect();

    let duration_ms = parsed.duration_us / 1000;
    let duration_seconds = (parsed.duration_us as f64) / 1_000_000.0;
    let bpm = parsed.bpm;

    let tracks = parsed
        .tracks
        .iter()
        .map(|t| {
            let is_muted = disabled_indices.contains(&t.index);
            if load_for_playback && is_muted {
                state.engine.set_track_muted(t.index, true);
            }
            TrackPayload {
                id: t.index,
                index: t.index,
                name: t.name.clone(),
                instrument_name: t.instrument_name.clone(),
                program_id: t.program_id,
                channel: t.channel,
                note_count: t.note_count,
                is_muted,
                is_active: t.note_count > 0,
                min_pitch: t.min_pitch,
                max_pitch: t.max_pitch,
                avg_pitch: t.avg_pitch,
                start_ms: t.start_ms,
                duration_ms: t.duration_ms,
                black_key_ratio: t.black_key_ratio,
                frequent_notes_ratio: t.frequent_notes_ratio,
                average_duration_ms: t.average_duration_ms,
            }
        })
        .collect::<Vec<_>>();

    let mut notes = Vec::new();
    let mut note_id_counter = 0usize;

    let note_durations = amp_core::note_durations(&parsed.events);
    for (i, ev) in parsed.events.iter().enumerate() {
        if ev.is_note_on {
            let start_ms = (ev.time_us as f64) / 1000.0;
            let length_ms = note_durations[i].unwrap_or(200_000) as f64 / 1000.0;

            notes.push(RollNotePayload {
                id: format!("n_{}", note_id_counter),
                note_number: ev.note,
                pitch: ev.note,
                start_ms,
                start_time: start_ms / 1000.0,
                length_ms,
                duration: length_ms / 1000.0,
                track_index: ev.track_index,
                velocity: ev.velocity,
            });
            note_id_counter += 1;
        }
    }

    let payload = MidiFilePayload {
        id: db_id,
        title,
        artist,
        album,
        duration_ms,
        duration_seconds,
        bpm,
        tempo: bpm,
        file_path: path.clone(),
        tracks,
        notes,
    };

    Ok(payload)
}

#[tauri::command]
fn play_song(state: State<AppState>) -> Result<bool, String> {
    state.engine.play();
    Ok(true)
}

#[tauri::command]
fn pause_song(state: State<AppState>) -> Result<bool, String> {
    state.engine.pause();
    Ok(true)
}

#[tauri::command]
fn stop_song(state: State<AppState>) -> Result<bool, String> {
    state.engine.stop();
    Ok(true)
}

#[tauri::command]
fn seek_song(ms: u64, seek_id: u64, state: State<AppState>) -> Result<bool, String> {
    state.engine.seek(ms * 1000, seek_id);
    Ok(true)
}

#[tauri::command]
fn set_playback_speed(speed: f64, state: State<AppState>) -> Result<bool, String> {
    state.engine.set_speed(speed);
    Ok(true)
}

#[tauri::command]
fn set_key_offset(offset: i8, state: State<AppState>) -> Result<bool, String> {
    state.engine.set_key_offset(offset);
    Ok(true)
}

#[tauri::command]
fn set_transpose_mode(mode: String, state: State<AppState>) -> Result<bool, String> {
    let tm = TransposeMode::from_str(&mode).unwrap_or(TransposeMode::Smart);
    state.engine.set_transpose_mode(tm);
    Ok(true)
}

#[tauri::command]
fn set_track_muted(track_index: usize, muted: bool, state: State<AppState>) -> Result<bool, String> {
    state.engine.set_track_muted(track_index, muted);
    Ok(true)
}

#[tauri::command]
fn set_ui_fps(fps: u32, state: State<AppState>) -> Result<bool, String> {
    state.engine.set_fps(fps);
    Ok(true)
}

#[tauri::command]
fn set_hold_delay(ms: u64, state: State<AppState>) -> Result<bool, String> {
    state.engine.set_hold_delay_ms(ms);
    Ok(true)
}

#[tauri::command]
fn set_note_timing(hold_notes: bool, tap_threshold_ms: u64, state: State<AppState>) -> Result<bool, String> {
    if tap_threshold_ms > 10_000 { return Err("Tap threshold must be between 0 and 10000 ms".into()); }
    state.engine.set_note_timing(hold_notes, tap_threshold_ms);
    Ok(true)
}

#[tauri::command]
fn panic_stop(state: State<AppState>) -> Result<bool, String> {
    state.engine.panic();
    Ok(true)
}

#[tauri::command]
fn update_global_hotkeys(
    enabled: bool,
    bindings: Vec<HotkeyBindingPayload>,
    app: AppHandle,
    state: State<AppState>,
) -> Result<bool, String> {
    apply_hotkeys_config(&app, &state.engine, enabled, &bindings)?;
    Ok(true)
}

// Phase 10: Key Change Modulation & Detection commands
#[tauri::command]
fn set_key_markers(markers: Vec<KeyChangeMarker>, state: State<AppState>) -> Result<bool, String> {
    state.engine.set_key_markers(markers);
    Ok(true)
}

#[tauri::command]
fn set_key_markers_enabled(enabled: bool, state: State<AppState>) -> Result<bool, String> {
    state.engine.set_key_markers_enabled(enabled);
    Ok(true)
}

#[tauri::command]
fn detect_key_signatures(file_path: String, state: State<AppState>) -> Result<Vec<KeyChangeMarker>, String> {
    if !file_path.is_empty() {
        let path = std::path::Path::new(&file_path);
        if path.exists() {
            if let Ok(parsed) = parse_midi_file(path) {
                return Ok(detect_key_changes(&parsed));
            }
        }
    }
    let current_opt = state.current_song.lock().unwrap();
    if let Some(ref song) = *current_opt {
        Ok(detect_key_changes(song))
    } else {
        Ok(Vec::new())
    }
}

// Phase 4: Audio Synthesizer & Listen Mode commands
#[tauri::command]
fn set_listen_mode(enabled: bool, state: State<AppState>) -> Result<bool, String> {
    state.engine.set_listen_mode(enabled);
    Ok(true)
}

#[tauri::command]
fn set_master_volume(volume: f32, state: State<AppState>) -> Result<bool, String> {
    state.engine.set_master_volume(volume);
    Ok(true)
}

#[tauri::command]
fn set_track_volume(track_index: usize, volume: f32, state: State<AppState>) -> Result<bool, String> {
    state.engine.set_track_volume(track_index, volume);
    Ok(true)
}

#[tauri::command]
fn set_synth_mode(mode: String, state: State<AppState>) -> Result<bool, String> {
    let sm = match mode.to_lowercase().as_str() {
        "raw" => SynthMode::Raw,
        _ => SynthMode::Adapted,
    };
    state.engine.set_synth_mode(sm);
    Ok(true)
}

#[tauri::command]
fn load_custom_soundfont(path: String, state: State<AppState>) -> Result<bool, String> {
    let path_buf = PathBuf::from(&path);
    if !path_buf.exists() {
        return Err(format!("SoundFont file not found: {}", path));
    }

    let mut file = std::fs::File::open(&path_buf)
        .map_err(|e| format!("Failed to open SoundFont file: {}", e))?;

    let sf = SoundFont::new(&mut file)
        .map_err(|e| format!("Failed to parse SoundFont: {:?}", e))?;

    let sf_arc = Arc::new(sf);
    if let Some(ref synth) = *state.audio_synth.lock().unwrap() {
        synth.load_soundfont(sf_arc);
        Ok(true)
    } else {
        Err("Audio synthesizer is not initialized".to_string())
    }
}

#[tauri::command]
fn reset_soundfont(state: State<AppState>, app: AppHandle) -> Result<bool, String> {
    if let Some(default_path) = find_default_soundfont(&app) {
        let mut file = std::fs::File::open(&default_path)
            .map_err(|e| format!("Failed to open default SoundFont: {}", e))?;
        let sf = SoundFont::new(&mut file)
            .map_err(|e| format!("Failed to parse default SoundFont: {:?}", e))?;
        if let Some(ref synth) = *state.audio_synth.lock().unwrap() {
            synth.load_soundfont(Arc::new(sf));
            return Ok(true);
        }
    }
    Err("Failed to find or reload default SoundFont".to_string())
}

fn find_default_soundfont(app: &AppHandle) -> Option<PathBuf> {
    if let Ok(path) = app.path().resolve("resources/default.sf2", tauri::path::BaseDirectory::Resource) {
        if path.exists() {
            return Some(path);
        }
    }
    let candidates = [
        PathBuf::from("resources/default.sf2"),
        PathBuf::from("src-tauri/resources/default.sf2"),
        PathBuf::from("../src-tauri/resources/default.sf2"),
    ];
    for c in candidates {
        if c.exists() {
            return Some(c);
        }
    }
    None
}

fn md5_hash(input: &str) -> u64 {
    use std::hash::{Hash, Hasher};
    let mut hasher = std::collections::hash_map::DefaultHasher::new();
    input.hash(&mut hasher);
    hasher.finish()
}

// Library & Persistence commands
#[tauri::command]
fn get_library_songs(context: Option<SongSettingsContext>, state: State<AppState>) -> Result<Vec<SongRecord>, String> {
    let songs = state.db.get_all_songs().map_err(|e| e.to_string())?;
    let songs = state.db.available_songs(songs)?;
    state.db.resolve_song_settings(songs, context.as_ref()).map_err(|e| e.to_string())
}

#[tauri::command]
fn get_song_settings(id: String, context: Option<SongSettingsContext>, state: State<AppState>) -> Result<Option<SongRecord>, String> {
    let songs = state.db.get_song_by_id(&id).map_err(|e| e.to_string())?.into_iter().collect();
    Ok(state.db.resolve_song_settings(songs, context.as_ref()).map_err(|e| e.to_string())?.into_iter().next())
}

#[tauri::command]
fn update_song_defaults(
    defaults: SongDefaults,
    state: State<AppState>,
) -> Result<bool, String> {
    *state.song_defaults.write().unwrap() = defaults;
    Ok(true)
}

#[tauri::command]
fn get_app_settings(state: State<AppState>) -> Result<serde_json::Value, String> {
    state.db.get_app_settings().map_err(|e| e.to_string())
}

#[tauri::command]
fn get_data_directory() -> String {
    amp_core::paths::app_data_dir().to_string_lossy().into_owned()
}

#[tauri::command]
async fn get_storage_usage(state: State<'_, AppState>) -> Result<storage::StorageUsage, String> {
    let settings = state.db.get_app_settings().map_err(|e| e.to_string())?;
    tauri::async_runtime::spawn_blocking(move || {
        if let Err(error) = storage::maintain(&settings) {
            log::warn!("Storage maintenance failed: {error}");
        }
        storage::usage()
    }).await.map_err(|e| e.to_string())?
}

#[tauri::command]
async fn clear_discover_cache() -> Result<u64, String> {
    tauri::async_runtime::spawn_blocking(storage::clear_cache).await.map_err(|e| e.to_string())?
}

#[tauri::command]
async fn update_app_settings(
    settings: serde_json::Value,
    state: State<'_, AppState>,
) -> Result<bool, String> {
    if !settings.is_object() {
        return Err("Application settings must be a JSON object".to_string());
    }
    let overlap_behavior = settings.get("overlapBehavior").map(|value|
        serde_json::from_value::<OverlapBehavior>(value.clone())
            .map_err(|e| format!("Invalid overlap behavior: {e}"))
    ).transpose()?;
    let repress_gap_ms = settings.get("repressGapMs").map(|value|
        value.as_u64().filter(|ms| *ms <= 1000)
            .ok_or_else(|| "Re-press gap must be a whole number between 0 and 1000 ms".to_string())
    ).transpose()?;
    let db = Arc::clone(&state.db);
    let engine = Arc::clone(&state.engine);
    let process_monitor = Arc::clone(&state.process_monitor);
    // SQLite writes and process-monitor locks must not block the window's event
    // loop when settings are changed repeatedly.
    tauri::async_runtime::spawn_blocking(move || {
        db.update_app_settings(&settings).map_err(|e| e.to_string())?;
        if let Some(behavior) = overlap_behavior {
            engine.set_overlap_behavior(behavior);
        }
        if let Some(ms) = repress_gap_ms {
            engine.set_repress_gap_ms(ms);
        }
        if let Some(ms) = settings.get("keyPressDelayMs").and_then(serde_json::Value::as_u64) {
            engine.set_hold_delay_ms(ms.min(1000));
        }
        if let Some(enabled) = settings.get("ui").and_then(|ui| ui.get("pedalsEnabled")).and_then(serde_json::Value::as_bool) {
            engine.set_pedals_enabled(enabled);
        }
        if settings.get("gameLocations").is_some() {
            let settings = db.get_app_settings().map_err(|e| e.to_string())?;
            process_monitor.lock().map_err(|e| e.to_string())?.set_locations(&settings);
        }
        Ok(true)
    }).await.map_err(|e| e.to_string())?
}

#[tauri::command]
fn reset_app_settings(state: State<AppState>) -> Result<bool, String> {
    state.db.reset_app_settings().map_err(|e| e.to_string())?;
    state.engine.set_hold_delay_ms(20);
    state.engine.set_overlap_behavior(OverlapBehavior::default());
    state.engine.set_repress_gap_ms(20);
    state.engine.set_pedals_enabled(true);
    state.process_monitor.lock().map_err(|e| e.to_string())?.set_locations(&serde_json::json!({}));
    Ok(true)
}

#[tauri::command]
fn scan_library_folder(
    folder_path: String,
    state: State<AppState>,
    app: AppHandle,
) -> Result<ScanSummary, String> {
    let _update = state.library_updates.lock().unwrap();
    let folder = PathBuf::from(&folder_path);
    let defaults = state.song_defaults.read().unwrap().clone();
    let summary = scan_folder(&state.db, &folder, Some(&defaults))?;
    let _ = app.emit("library:updated", ());
    let _ = app.emit("queue:updated", ());
    let _ = app.emit("library:scanned", &summary);
    Ok(summary)
}

#[tauri::command]
fn import_midi_files(
    paths: Vec<String>,
    state: State<AppState>,
    app: AppHandle,
) -> Result<Vec<SongRecord>, String> {
    let _update = state.library_updates.lock().unwrap();
    let path_bufs: Vec<PathBuf> = paths.into_iter().map(PathBuf::from).collect();
    let defaults = state.song_defaults.read().unwrap().clone();
    let imported = import_files(&state.db, &path_bufs, Some(&defaults))?;
    let _ = app.emit("library:updated", ());
    Ok(imported)
}

#[tauri::command]
fn save_song_settings(
    id: String,
    settings: SongSettingsUpdate,
    scope: SongSettingsScope,
    state: State<AppState>,
) -> Result<bool, String> {
    scope.validate()?;
    if !matches!(scope, SongSettingsScope::Instrument { .. }) {
        return Err("Studio settings must be saved for a game instrument".into());
    }
    state.db.update_song_settings_scoped(&id, &settings, &scope).map_err(|e| e.to_string())?;
    Ok(true)
}

#[tauri::command]
fn save_song_metadata(
    id: String,
    mut metadata: SongMetadataUpdate,
    state: State<AppState>,
    app: AppHandle,
) -> Result<bool, String> {
    if let Some(ref album_title) = metadata.album {
        if metadata.album_id.is_none() && !album_title.trim().is_empty() {
            if let Ok(album_id) = state.db.get_or_create_album(album_title.trim(), metadata.artist.as_deref()) {
                metadata.album_id = Some(album_id);
            }
        }
    }
    state.db.update_song_metadata(&id, &metadata).map_err(|e| e.to_string())?;
    let _ = app.emit("library:updated", ());
    Ok(true)
}

#[tauri::command]
fn save_song_details(
    id: String,
    details: SongDetailsUpdate,
    scope: SongSettingsScope,
    state: State<AppState>,
    app: AppHandle,
) -> Result<bool, String> {
    scope.validate()?;
    let base_key = details.base_key.unwrap_or(0);
    if details.title.trim().is_empty()
        || !(-27..=27).contains(&base_key)
        || !(-27..=27).contains(&(base_key + details.key))
        || !(0..=3).contains(&details.transpose)
        || !details.speed.is_finite() || !(0.1..=4.0).contains(&details.speed)
        || !(1..=1000).contains(&details.merge_milliseconds)
        || details.bpm.is_some_and(|bpm| !bpm.is_finite() || bpm <= 0.0 || bpm > 999.0)
    {
        return Err("Invalid song details".to_string());
    }
    let album_id = if details.album.trim().is_empty() {
        None
    } else {
        Some(state.db.get_or_create_album(details.album.trim(), Some(&details.artist)).map_err(|e| e.to_string())?)
    };
    state.db.update_song_details_scoped(&id, &details, album_id.as_deref(), &scope).map_err(|e| e.to_string())?;
    let _ = app.emit("library:updated", ());
    Ok(true)
}

#[derive(serde::Serialize)]
#[serde(rename_all = "camelCase")]
struct SongFileAnalysis {
    native_bpm: f64,
    detected_base_key: Option<i8>,
    file_date: String,
}

#[tauri::command]
fn analyze_song_file(path: String) -> Result<SongFileAnalysis, String> {
    // Analysis never loads the playback engine or changes the currently playing song.
    let path = PathBuf::from(path);
    let metadata = amp_core::extract_metadata(&path)?;
    let modified = std::fs::metadata(&path).and_then(|meta| meta.modified()).map_err(|e| e.to_string())?;
    Ok(SongFileAnalysis {
        native_bpm: metadata.native_bpm,
        detected_base_key: metadata.detected_key_offset,
        file_date: chrono::DateTime::<chrono::Utc>::from(modified).to_rfc3339(),
    })
}

#[tauri::command]
fn reveal_song_file(path: String) -> Result<bool, String> {
    #[cfg(windows)]
    {
        use std::os::windows::process::CommandExt;

        let path = path.replace('/', "\\");
        if !std::path::Path::new(&path).is_file() {
            return Err(format!("File no longer exists: {}", path));
        }
        // Explorer parses /select itself. Command::arg would escape these
        // embedded quotes, preventing paths with spaces from being selected.
        std::process::Command::new("explorer.exe")
            .raw_arg(format!("/select,\"{}\"", path))
            .spawn()
            .map_err(|e| e.to_string())?;
    }
    #[cfg(target_os = "macos")]
    std::process::Command::new("open").arg("-R").arg(&path).spawn().map_err(|e| e.to_string())?;
    #[cfg(all(not(windows), not(target_os = "macos")))]
    std::process::Command::new("xdg-open").arg(PathBuf::from(path).parent().ok_or("File has no parent directory")?).spawn().map_err(|e| e.to_string())?;
    Ok(true)
}

#[tauri::command]
fn delete_song(
    id: String,
    state: State<AppState>,
    app: AppHandle,
) -> Result<bool, String> {
    let _update = state.library_updates.lock().unwrap();
    state.db.exclude_songs(&[id])?;
    let _ = app.emit("library:updated", ());
    let _ = app.emit("queue:updated", ());
    Ok(true)
}

#[tauri::command]
fn exclude_library_songs(ids: Vec<String>, state: State<AppState>, app: AppHandle) -> Result<bool, String> {
    let _update = state.library_updates.lock().unwrap();
    state.db.exclude_songs(&ids)?;
    let _ = app.emit("library:updated", ());
    let _ = app.emit("queue:updated", ());
    Ok(true)
}

#[tauri::command]
fn get_library_file_issues(state: State<AppState>) -> Result<Vec<LibraryFileIssue>, String> {
    state.db.get_file_issues()
}

#[tauri::command]
fn resolve_library_file_issue(path: String, action: FileIssueAction, state: State<AppState>, app: AppHandle) -> Result<bool, String> {
    let _update = state.library_updates.lock().unwrap();
    let defaults = state.song_defaults.read().unwrap().clone();
    let result = resolve_file_issue(&state.db, &path, action, Some(&defaults));
    let _ = app.emit("library:updated", ());
    let _ = app.emit("queue:updated", ());
    result.map(|()| true)
}

#[tauri::command]
fn get_playlists(state: State<AppState>) -> Result<Vec<PlaylistRecord>, String> {
    let mut playlists = state.db.get_playlists().map_err(|e| e.to_string())?;
    for playlist in &mut playlists {
        let songs = state.db.get_playlist_songs(&playlist.id).map_err(|e| e.to_string())?;
        playlist.song_count = state.db.available_songs(songs)?.len();
    }
    Ok(playlists)
}

#[tauri::command]
fn create_playlist(
    title: String,
    icon: Option<String>,
    banner_color: Option<String>,
    state: State<AppState>,
    app: AppHandle,
) -> Result<PlaylistRecord, String> {
    let pl = state.db.create_playlist(&title, icon.as_deref(), banner_color.as_deref()).map_err(|e| e.to_string())?;
    let _ = app.emit("library:updated", ());
    Ok(pl)
}

#[tauri::command]
fn delete_playlist(
    id: String,
    state: State<AppState>,
    app: AppHandle,
) -> Result<bool, String> {
    state.db.delete_playlist(&id).map_err(|e| e.to_string())?;
    let _ = app.emit("library:updated", ());
    Ok(true)
}

#[tauri::command]
fn add_to_playlist(
    playlist_id: String,
    song_id: String,
    state: State<AppState>,
    app: AppHandle,
) -> Result<bool, String> {
    state.db.add_to_playlist(&playlist_id, &song_id).map_err(|e| e.to_string())?;
    let _ = app.emit("library:updated", ());
    Ok(true)
}

#[tauri::command]
fn remove_from_playlist(
    playlist_id: String,
    song_id: String,
    state: State<AppState>,
    app: AppHandle,
) -> Result<bool, String> {
    state.db.remove_from_playlist(&playlist_id, &song_id).map_err(|e| e.to_string())?;
    let _ = app.emit("library:updated", ());
    Ok(true)
}

#[tauri::command]
fn get_playlist_songs(
    playlist_id: String,
    context: Option<SongSettingsContext>,
    state: State<AppState>,
) -> Result<Vec<SongRecord>, String> {
    let songs = state.db.get_playlist_songs(&playlist_id).map_err(|e| e.to_string())?;
    let songs = state.db.available_songs(songs)?;
    state.db.resolve_song_settings(songs, context.as_ref()).map_err(|e| e.to_string())
}

#[tauri::command]
fn get_albums(state: State<AppState>) -> Result<Vec<AlbumRecord>, String> {
    state.db.get_albums().map_err(|e| e.to_string())
}

#[tauri::command]
fn watch_library_folder(
    folder_path: Option<String>,
    state: State<AppState>,
    app: AppHandle,
) -> Result<bool, String> {
    // Drop the previous watcher when disabled, cleared, or switched to another folder.
    let mut active_watcher = state.watcher.lock().unwrap();
    *active_watcher = None;
    let Some(folder_path) = folder_path.filter(|path| !path.trim().is_empty()) else {
        return Ok(true);
    };
    let folder = PathBuf::from(&folder_path);
    let db_clone = Arc::clone(&state.db);
    let app_clone = app.clone();
    let folder_clone = folder.clone();
    let defaults_clone = Arc::clone(&state.song_defaults);
    let updates = Arc::clone(&state.library_updates);

    let watcher = DirectoryWatcher::watch(&folder, move || {
        let _update = updates.lock().unwrap();
        log::info!("Folder change detected in {}, re-scanning...", folder_clone.display());
        let def = defaults_clone.read().unwrap().clone();
        match scan_folder(&db_clone, &folder_clone, Some(&def)) {
            Ok(summary) => {
                log::info!("Folder auto-scan complete: {:?}", summary);
                let _ = app_clone.emit("library:updated", ());
                let _ = app_clone.emit("queue:updated", ());
                let _ = app_clone.emit("library:scanned", &summary);
            }
            Err(error) => log::warn!("Folder auto-scan failed: {}", error),
        }
    })?;

    *active_watcher = Some(watcher);
    Ok(true)
}

// Queue commands
#[tauri::command]
fn get_queue_songs(context: Option<SongSettingsContext>, state: State<AppState>) -> Result<Vec<SongRecord>, String> {
    let songs = state.db.get_queue_songs().map_err(|e| e.to_string())?;
    let songs = state.db.available_songs(songs)?;
    state.db.resolve_song_settings(songs, context.as_ref()).map_err(|e| e.to_string())
}

#[tauri::command]
fn set_queue(
    song_ids: Vec<String>,
    state: State<AppState>,
    app: AppHandle,
) -> Result<bool, String> {
    state.db.set_queue(&song_ids).map_err(|e| e.to_string())?;
    let _ = app.emit("queue:updated", ());
    Ok(true)
}

#[tauri::command]
fn add_to_queue(
    song_ids: Vec<String>,
    state: State<AppState>,
    app: AppHandle,
) -> Result<bool, String> {
    state.db.add_to_queue(&song_ids).map_err(|e| e.to_string())?;
    let _ = app.emit("queue:updated", ());
    Ok(true)
}

#[tauri::command]
fn remove_from_queue(
    song_ids: Vec<String>,
    state: State<AppState>,
    app: AppHandle,
) -> Result<bool, String> {
    state.db.remove_from_queue(&song_ids).map_err(|e| e.to_string())?;
    let _ = app.emit("queue:updated", ());
    Ok(true)
}

#[tauri::command]
fn clear_queue(state: State<AppState>, app: AppHandle) -> Result<bool, String> {
    state.db.clear_queue().map_err(|e| e.to_string())?;
    let _ = app.emit("queue:updated", ());
    Ok(true)
}

#[tauri::command]
fn reorder_queue(
    song_ids: Vec<String>,
    state: State<AppState>,
    app: AppHandle,
) -> Result<bool, String> {
    state.db.reorder_queue(&song_ids).map_err(|e| e.to_string())?;
    let _ = app.emit("queue:updated", ());
    Ok(true)
}

// Phase 8: Game Process & Instrument Mapping commands
#[tauri::command]
fn get_game_statuses(state: State<AppState>) -> Result<Vec<GameStatusPayload>, String> {
    let mut pm = state.process_monitor.lock().map_err(|e| e.to_string())?;
    Ok(pm.check_statuses())
}

#[tauri::command]
fn set_game_targets(targets: Vec<GameProcessTarget>, state: State<AppState>) -> Result<bool, String> {
    let mut pm = state.process_monitor.lock().map_err(|e| e.to_string())?;
    pm.set_targets(targets);
    Ok(true)
}

#[tauri::command]
fn focus_game(pid: u32) -> Result<bool, String> {
    Ok(focus_window_by_pid(pid))
}

#[tauri::command]
fn tap_note_preview(pitch: u8, is_listen_mode: bool, state: State<AppState>) -> Result<bool, String> {
    state.engine.tap_note(pitch, is_listen_mode);
    Ok(true)
}

#[tauri::command]
fn set_game_instrument_mapping(
    layout: String,
    key_pairs: Vec<(u8, String)>,
    config_paths: Option<Vec<String>>,
    state: State<AppState>,
) -> Result<bool, String> {
    let paths: Vec<PathBuf> = config_paths.unwrap_or_default().into_iter().map(PathBuf::from).collect();
    let mut sources = state.config_sources.lock().map_err(|e| e.to_string())?;
    let missing = paths.iter().any(|path| !path.is_file());
    *sources = paths;
    // An IPC mapping queued before deletion must not re-enable its old keys afterward.
    if missing {
        state.engine.set_config_available(false);
        state.engine.set_mapping(NoteKeyMapping::from_pairs(KeyboardLayout::Diatonic21, &[]));
        return Ok(false);
    }
    let layout_lower = layout.to_lowercase();
    let cm = state.config_manager.lock().map_err(|e| e.to_string())?;

    let mapping = if layout.is_empty() {
        NoteKeyMapping::from_pairs(KeyboardLayout::Diatonic21, &[])
    } else if !key_pairs.is_empty() {
        let kb_layout = match layout_lower.as_str() {
            "diatonic15" | "sky" => KeyboardLayout::Diatonic15,
            "drum8" | "drums" | "drum" => KeyboardLayout::Drum8,
            "chromatic36" | "chromatic" => KeyboardLayout::Chromatic36,
            _ => KeyboardLayout::Diatonic21,
        };
        NoteKeyMapping::from_key_pairs(kb_layout, &key_pairs)
    } else if let Some(layout_cfg) = cm.layouts.get(&layout_lower) {
        let diatonic = [
            72, 74, 76, 77, 79, 81, 83,
            60, 62, 64, 65, 67, 69, 71,
            48, 50, 52, 53, 55, 57, 59
        ];
        let pairs: Vec<(u8, String)> = layout_cfg
            .keys
            .iter()
            .enumerate()
            .filter_map(|(idx, s)| {
                if idx < diatonic.len() {
                    Some((diatonic[idx], s.clone()))
                } else {
                    None
                }
            })
            .collect();
        NoteKeyMapping::from_key_pairs(KeyboardLayout::Diatonic21, &pairs)
    } else {
        let kb_layout = match layout_lower.as_str() {
            "diatonic15" | "sky" => KeyboardLayout::Diatonic15,
            "drum8" | "drums" | "drum" => KeyboardLayout::Drum8,
            "chromatic36" | "chromatic" => KeyboardLayout::Chromatic36,
            _ => KeyboardLayout::Diatonic21,
        };
        NoteKeyMapping::new(kb_layout)
    };

    let mapping = if let Some(layout_cfg) = cm.layouts.get(&layout_lower) {
        mapping.with_pedal_bindings(layout_cfg)
    } else {
        mapping
    };

    // Stop while the old mapping is still installed so its held keys are released.
    if layout.is_empty() { state.engine.set_config_available(false); }
    state.engine.set_mapping(mapping);
    if !layout.is_empty() { state.engine.set_config_available(true); }
    Ok(true)
}

#[tauri::command]
fn get_games(state: State<AppState>) -> Result<Vec<LoadedGameBundle>, String> {
    let cm = state.config_manager.lock().map_err(|e| e.to_string())?;
    Ok(cm.games.clone())
}

#[tauri::command]
fn get_keyboard_layouts(
    state: State<AppState>,
) -> Result<std::collections::HashMap<String, KeyboardLayoutJson>, String> {
    let cm = state.config_manager.lock().map_err(|e| e.to_string())?;
    Ok(cm.layouts.clone())
}

#[tauri::command]
fn get_game_configs(state: State<AppState>) -> Result<ConfigSnapshot, String> {
    Ok(state.config_manager.lock().map_err(|e| e.to_string())?.snapshot())
}

fn reload_configs(
    app: &AppHandle,
    config_manager: &Mutex<ConfigManager>,
    process_monitor: &Mutex<ProcessMonitor>,
) -> Result<ConfigSnapshot, String> {
    let mut cm = config_manager.lock().map_err(|e| e.to_string())?;
    cm.reload();

    let targets = cm.games.iter().map(GameProcessTarget::from).collect();

    if let Ok(mut pm) = process_monitor.lock() {
        pm.set_targets(targets);
    }

    let snapshot = cm.snapshot();
    drop(cm);
    let _ = app.emit("configs:changed", ());
    Ok(snapshot)
}

#[tauri::command]
fn reload_game_configs(app: AppHandle, state: State<AppState>) -> Result<ConfigSnapshot, String> {
    stop_for_deleted_config(&state.engine, &state.config_sources);
    reload_configs(&app, &state.config_manager, &state.process_monitor)
}

#[tauri::command]
fn reset_game_configs(app: AppHandle, state: State<AppState>) -> Result<ConfigSnapshot, String> {
    state.engine.stop();
    state.config_manager.lock().map_err(|e| e.to_string())?.reset()?;
    reload_configs(&app, &state.config_manager, &state.process_monitor)
}

fn stop_for_deleted_config(engine: &PlaybackEngine, sources: &Mutex<Vec<PathBuf>>) -> bool {
    let Ok(paths) = sources.lock() else { return false; };
    let missing = paths.iter().any(|path| !path.is_file());
    if missing {
        engine.set_config_available(false);
        engine.set_mapping(NoteKeyMapping::from_pairs(KeyboardLayout::Diatonic21, &[]));
    }
    missing
}

#[tauri::command]
fn open_games_folder(state: State<AppState>) -> Result<(), String> {
    let cm = state.config_manager.lock().map_err(|e| e.to_string())?;
    cm.open_games_folder()
}

#[tauri::command]
fn open_layouts_folder(state: State<AppState>) -> Result<(), String> {
    let cm = state.config_manager.lock().map_err(|e| e.to_string())?;
    cm.open_layouts_folder()
}

#[tauri::command]
fn get_app_version() -> String {
    env!("CARGO_PKG_VERSION").to_string()
}

#[tauri::command]
fn open_url(url: String) -> Result<(), String> {
    #[cfg(windows)]
    {
        std::process::Command::new("rundll32")
            .args(["url.dll,FileProtocolHandler", &url])
            .spawn()
            .map_err(|e| format!("Failed to open URL: {}", e))?;
        Ok(())
    }
    #[cfg(not(windows))]
    {
        std::process::Command::new("xdg-open")
            .arg(&url)
            .spawn()
            .map_err(|e| format!("Failed to open URL: {}", e))?;
        Ok(())
    }
}

#[tauri::command]
fn open_folder_in_explorer(path: Option<String>) -> Result<(), String> {
    let target = path
        .map(std::path::PathBuf::from)
        .unwrap_or_else(config_manager::get_appdata_dir);

    #[cfg(windows)]
    {
        let _ = std::fs::create_dir_all(&target);
        std::process::Command::new("explorer")
            .arg(target.as_os_str())
            .spawn()
            .map_err(|e| format!("Failed to open explorer: {}", e))?;
        Ok(())
    }
    #[cfg(not(windows))]
    {
        let _ = std::fs::create_dir_all(&target);
        std::process::Command::new("xdg-open")
            .arg(target.as_os_str())
            .spawn()
            .map_err(|e| format!("Failed to open folder: {}", e))?;
        Ok(())
    }
}

#[tauri::command]
async fn fetch_github_releases(user_agent: Option<String>) -> Result<String, String> {
    let ua = user_agent.unwrap_or_else(|| format!("AutoMidiPlayer/{}", env!("CARGO_PKG_VERSION")));
    // Async networking keeps the Tauri UI thread responsive and launches no child process.
    let client = reqwest::Client::builder()
        .user_agent(ua)
        .connect_timeout(std::time::Duration::from_secs(5))
        .timeout(std::time::Duration::from_secs(15))
        .build()
        .map_err(|e| format!("Could not initialize update request: {}", e))?;
    client
        .get("https://api.github.com/repos/Jed556/AutoMidiPlayer/releases")
        .header(reqwest::header::ACCEPT, "application/vnd.github.v3+json")
        .send()
        .await
        .and_then(reqwest::Response::error_for_status)
        .map_err(|e| format!("GitHub update request failed: {}", e))?
        .text()
        .await
        .map_err(|e| format!("Could not read GitHub releases: {}", e))
}

// =========================================================================
// Logging & Diagnostics Commands
// =========================================================================

#[tauri::command]
fn log_step(step: String, details: Option<String>) {
    logging::log_step(&step, details.as_deref(), Some("renderer"), None, None);
}

#[tauri::command]
fn log_page_visit(page_name: String, source: Option<String>) {
    logging::log_page_visit(&page_name, source.as_deref(), Some("renderer"), None, None);
}

#[tauri::command]
fn log_message(
    level: String,
    message: String,
    channel: Option<String>,
    caller: Option<String>,
    file: Option<String>,
    line: Option<u32>,
) {
    let caller_ref = caller.as_deref().or(Some("renderer"));
    if level.eq_ignore_ascii_case("error") {
        logging::log_error(&message, caller_ref, file.as_deref(), line);
    } else if let Some(ch) = channel {
        let channel_enum = logging::LogChannel::from_name(&ch);
        logging::write_log_line(channel_enum, &message, caller_ref, file.as_deref(), line);
    } else {
        logging::log(&message, caller_ref, file.as_deref(), line);
    }
}

#[tauri::command]
fn clear_app_logs() -> Result<(), String> {
    logging::clear_logs();
    Ok(())
}

#[tauri::command]
fn open_logs_folder() -> Result<(), String> {
    logging::open_logs_folder()
}

#[tauri::command]
fn read_log_tail(channel: Option<String>, max_lines: Option<usize>) -> Result<String, String> {
    let channel_enum = channel
        .as_deref()
        .map(logging::LogChannel::from_name)
        .unwrap_or(logging::LogChannel::App);
    let log_path = logging::get_logs_dir().join(channel_enum.file_name());
    Ok(logging::read_log_tail(&log_path, max_lines.unwrap_or(100)))
}

#[tauri::command]
fn set_crash_log_verbosity(verbosity: u8) -> Result<(), String> {
    logging::set_verbosity(verbosity);
    Ok(())
}

#[tauri::command]
fn set_debug_mode(enabled: bool) -> Result<(), String> {
    logging::set_debug_mode(enabled);
    Ok(())
}

// =========================================================================
// Telemetry & Sentry Commands
// =========================================================================

#[tauri::command]
fn set_telemetry_enabled(enabled: bool) -> Result<(), String> {
    telemetry::set_telemetry_enabled(enabled);
    Ok(())
}

#[tauri::command]
async fn send_sentry_test() -> Result<String, String> {
    tauri::async_runtime::spawn_blocking(telemetry::send_test_message)
        .await
        .map_err(|error| format!("Sentry test task failed: {error}"))?
}

#[tauri::command]
fn capture_telemetry_exception(
    error_type: String,
    message: String,
    stack_trace: Option<String>,
) -> Result<(), String> {
    telemetry::capture_error(&error_type, &message, stack_trace.as_deref());
    Ok(())
}

#[tauri::command]
fn capture_telemetry_message(message: String, level: Option<String>) -> Result<(), String> {
    let lvl = match level.as_deref().unwrap_or("info").to_lowercase().as_str() {
        "warning" | "warn" => sentry::Level::Warning,
        "error" => sentry::Level::Error,
        "fatal" => sentry::Level::Fatal,
        "debug" => sentry::Level::Debug,
        _ => sentry::Level::Info,
    };
    telemetry::capture_message(&message, lvl);
    Ok(())
}

#[derive(Clone, serde::Serialize)]
#[serde(rename_all = "camelCase")]
struct StartupStatus {
    stage: String,
    ready: bool,
    error: Option<String>,
    update_resume: Option<updates::UpdateResume>,
}

type StartupState = Arc<Mutex<StartupStatus>>;

#[tauri::command]
fn get_startup_status(state: State<StartupState>) -> Result<StartupStatus, String> {
    state.lock().map(|status| status.clone()).map_err(|error| error.to_string())
}

fn set_startup_stage(app: &AppHandle, stage: &str) {
    if let Ok(mut status) = app.state::<StartupState>().lock() {
        status.stage = stage.to_owned();
    }
}

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    tauri::Builder::default()
        .setup(|app| {
            // Move legacy data before any logger, database, or settings file is opened.
            let migration_result = amp_core::paths::migrate_app_data();

            // Initialize logging system, panic hook, and sentry telemetry
            let _ = logging::init_logger(logging::ALL_STEPS_VERBOSITY, cfg!(debug_assertions));
            telemetry::register_panic_hook();
            logging::log_startup(
                "Auto MIDI Player",
                env!("CARGO_PKG_VERSION"),
                Some("run"),
                Some(file!()),
                Some(line!()),
            );

            if let Err(ref error) = migration_result {
                log::warn!("Legacy app data migration encountered a non-fatal error: {error}");
            }

            // Register dialog plugin
            app.handle().plugin(tauri_plugin_dialog::init())?;

            // Register global shortcut plugin
            app.handle().plugin(tauri_plugin_global_shortcut::Builder::new().build())?;
            app.handle().plugin(tauri_plugin_updater::Builder::new().build())?;
            app.manage(updates::UpdateState::default());

            // Return from setup immediately so the webview can display the splash.
            // App commands are gated by get_startup_status until AppState is managed.
            let startup = Arc::new(Mutex::new(StartupStatus {
                stage: "Initializing application\u{2026}".into(),
                ready: false,
                error: None,
                update_resume: updates::load_resume(env!("CARGO_PKG_VERSION")),
            }));
            app.manage(startup.clone());
            let app = app.handle().clone();
            tauri::async_runtime::spawn_blocking(move || {
                let result = std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| {
                    // Initialize Audio Device and load default SoundFont
                    set_startup_stage(&app, "Initializing audio\u{2026}");
                    let default_sf_path = find_default_soundfont(&app);
                    let default_sf = default_sf_path.and_then(|p| {
                        std::fs::File::open(p).ok().and_then(|mut f| SoundFont::new(&mut f).ok().map(Arc::new))
                    });

                    let (synth_tx, synth_rx) = std::sync::mpsc::channel();
                    std::thread::Builder::new()
                        .name("audio-device-thread".into())
                        .spawn(move || {
                            let device = AudioDevice::new(default_sf);
                            if let Ok(dev) = device {
                                let synth = dev.synth();
                                let _ = synth_tx.send(Some(synth));
                                loop {
                                    std::thread::sleep(std::time::Duration::from_secs(3600));
                                }
                            } else {
                                let _ = synth_tx.send(None);
                            }
                        })
                        .ok();

                    let audio_synth = synth_rx.recv().ok().flatten();

                    set_startup_stage(&app, "Initializing player\u{2026}");
                    // Initialize amp-core playback engine
                    let app_handle = app.clone();
                    let engine = Arc::new(PlaybackEngine::new(
                        None,
                        NoteKeyMapping::new(KeyboardLayout::Diatonic21),
                        true, // Key simulation enabled
                        audio_synth.clone(),
                        move |event| match event {
                            PlayerEvent::TimeUpdate { time_us, seek_id } => {
                                let _ = app_handle.emit("playback:time", serde_json::json!({
                                    "timeMs": time_us as f64 / 1000.0,
                                    "seekId": seek_id
                                }));
                            }
                            PlayerEvent::PedalsChanged(state) => {
                                let _ = app_handle.emit("playback:pedals", state);
                            }
                            PlayerEvent::ActiveNotes(notes) => {
                                let _ = app_handle.emit("playback:notes", notes);
                            }
                            PlayerEvent::ActiveTrackNotes(notes) => {
                                let _ = app_handle.emit("playback:track_notes", notes);
                            }
                            PlayerEvent::StateChanged(status) => {
                                let status_str = match status {
                                    PlaybackStatus::Playing => "Playing",
                                    PlaybackStatus::Paused => "Paused",
                                    PlaybackStatus::Stopped => "Stopped",
                                };
                                let _ = app_handle.emit("playback:state_changed", status_str);
                            }
                            PlayerEvent::Finished => {
                                let _ = app_handle.emit("playback:finished", ());
                            }
                            _ => {}
                        },
                    ));

                    // Register default global hotkeys matching the original WPF app (Ctrl+Alt bindings):
                    let default_bindings = default_hotkey_bindings();
                    let _ = apply_hotkeys_config(&app.clone(), &engine, true, &default_bindings);

                    set_startup_stage(&app, "Loading library database\u{2026}");
                    // Initialize SQLite library database at %LocalAppData%\Jed556\AutoMIDIPlayer\AutoMidiPlayer.db
                    let db = Arc::new(LibraryDatabase::new_with_migration_callback(None, || {
                        set_startup_stage(&app, "Migrating library database\u{2026}");
                    }).expect("Failed to initialize library database"));
                    let settings = db.get_app_settings().unwrap_or_default();
                    // First-run setup must not send diagnostics or opt-out refresh pings.
                    if settings.get("onboardingCompleted").and_then(serde_json::Value::as_bool).unwrap_or(false) {
                        telemetry::init(settings.get("telemetryOptIn")
                            .and_then(serde_json::Value::as_bool).unwrap_or(false));
                    }
                    engine.set_hold_delay_ms(settings.get("keyPressDelayMs")
                        .and_then(serde_json::Value::as_u64).unwrap_or(20).min(1000));
                    engine.set_overlap_behavior(settings.get("overlapBehavior")
                        .and_then(|value| serde_json::from_value(value.clone()).ok()).unwrap_or_default());
                    engine.set_repress_gap_ms(settings.get("repressGapMs")
                        .and_then(serde_json::Value::as_u64).unwrap_or(20));
                    engine.set_pedals_enabled(settings.get("ui")
                        .and_then(|ui| ui.get("pedalsEnabled"))
                        .and_then(serde_json::Value::as_bool).unwrap_or(true));

                    set_startup_stage(&app, "Loading game configurations\u{2026}");
                    // Initialize ConfigManager (extracts defaults to %LocalAppData%\Jed556\AutoMIDIPlayer if missing)
                    let config_manager = Arc::new(Mutex::new(ConfigManager::new()));

                    // Build dynamic GameProcessTarget list from config_manager
                    let initial_targets = {
                        let cm = config_manager.lock().unwrap();
                        cm.games.iter().map(GameProcessTarget::from).collect()
                    };

                    set_startup_stage(&app, "Initializing game monitor\u{2026}");
                    // Initialize ProcessMonitor and start background polling thread
                    let process_monitor = Arc::new(Mutex::new(ProcessMonitor::new(initial_targets)));
                    process_monitor.lock().unwrap().set_locations(&settings);
                    let pm_clone = Arc::clone(&process_monitor);
                    let app_pm = app.clone();

                    std::thread::Builder::new()
                        .name("process-monitor-thread".into())
                        .spawn(move || {
                            let mut previous = None;
                            loop {
                                let statuses = {
                                    if let Ok(mut pm) = pm_clone.lock() {
                                        pm.check_statuses()
                                    } else {
                                        Vec::new()
                                    }
                                };
                                if previous.as_ref() != Some(&statuses) {
                                    let _ = app_pm.emit("games:status_updated", &statuses);
                                    previous = Some(statuses);
                                }
                                std::thread::sleep(std::time::Duration::from_millis(1000));
                            }
                        })
                        .ok();

                    let config_sources = Arc::new(Mutex::new(Vec::new()));
                    let config_watcher = {
                        let cm = Arc::clone(&config_manager);
                        let pm = Arc::clone(&process_monitor);
                        let handle = app.clone();
                        let removal_cm = Arc::clone(&config_manager);
                        let removal_pm = Arc::clone(&process_monitor);
                        let removal_app = app.clone();
                        let removal_engine = Arc::clone(&engine);
                        let removal_sources = Arc::clone(&config_sources);
                        let change_engine = Arc::clone(&engine);
                        let change_sources = Arc::clone(&config_sources);
                        let root = cm.lock().unwrap().watch_root().to_path_buf();
                        match DirectoryWatcher::watch_configs_with_removals(&root, move || {
                            if stop_for_deleted_config(&removal_engine, &removal_sources) {
                                if let Err(error) = reload_configs(&removal_app, &removal_cm, &removal_pm) {
                                    log::warn!("Config deletion reload failed: {error}");
                                }
                            }
                        }, move || {
                            stop_for_deleted_config(&change_engine, &change_sources);
                            if let Err(error) = reload_configs(&handle, &cm, &pm) {
                                log::warn!("Config reload failed: {error}");
                            }
                        }) {
                            Ok(watcher) => Some(watcher),
                            Err(error) => {
                                log::warn!("Config watcher failed: {error}");
                                config_manager.lock().unwrap().issues.push(amp_core::input::config_scan::ConfigIssue {
                                    path: root.to_string_lossy().into_owned(),
                                    message: format!("Live config updates unavailable: {error}"),
                                });
                                None
                            }
                        }
                    };

                    // Cover changes between the initial scan and watcher registration.
                    if config_watcher.is_some() {
                        let _ = reload_configs(&app, &config_manager, &process_monitor);
                    }

                    // Maintenance runs independently of the Settings view and reads current policy.
                    let storage_db = Arc::clone(&db);
                    std::thread::Builder::new().name("storage-maintenance".into()).spawn(move || loop {
                        match storage_db.get_app_settings().map_err(|e| e.to_string())
                            .and_then(|settings| storage::maintain(&settings)) {
                            Ok(()) => {},
                            Err(error) => log::warn!("Storage maintenance failed: {error}"),
                        }
                        std::thread::sleep(std::time::Duration::from_secs(60));
                    }).ok();

                    // Manage AppState in Tauri
                    app.manage(AppState {
                        engine,
                        current_song: Arc::new(Mutex::new(None)),
                        audio_synth: Arc::new(Mutex::new(audio_synth)),
                        db,
                        watcher: Arc::new(Mutex::new(None)),
                        library_updates: Arc::new(Mutex::new(())),
                        process_monitor,
                        config_manager,
                        config_watcher: Mutex::new(config_watcher),
                        config_sources,
                        song_defaults: Arc::new(RwLock::new(SongDefaults::default())),
                    });
                }));
                if let Ok(mut status) = startup.lock() {
                    match result {
                        Ok(()) => {
                            status.ready = true;
                            status.stage = "Loading interface\u{2026}".into();
                        }
                        Err(_) => {
                            status.error = Some("Unable to initialize the application. Please restart.".into());
                        }
                    }
                }
            });

            Ok(())
        })
        .invoke_handler(tauri::generate_handler![
            get_startup_status,
            updates::prepare_update,
            updates::download_update,
            updates::install_update,
            updates::complete_update,
            minimize_window,
            maximize_window,
            close_window,
            is_maximized,
            load_midi_file,
            read_midi_file,
            play_song,
            pause_song,
            stop_song,
            seek_song,
            set_playback_speed,
            set_key_offset,
            set_transpose_mode,
            set_track_muted,
            set_ui_fps,
            set_hold_delay,
            set_note_timing,
            panic_stop,
            update_global_hotkeys,
            // Phase 10 key modulation & detection commands
            set_key_markers,
            set_key_markers_enabled,
            detect_key_signatures,
            // Phase 4 audio commands
            set_listen_mode,
            set_master_volume,
            set_track_volume,
            set_synth_mode,
            load_custom_soundfont,
            reset_soundfont,
            // Phase 5 library commands
            get_library_songs,
            get_song_settings,
            scan_library_folder,
            import_midi_files,
            update_song_defaults,
            get_app_settings,
            get_data_directory,
            get_storage_usage,
            clear_discover_cache,
            update_app_settings,
            reset_app_settings,
            save_song_settings,
            save_song_metadata,
            save_song_details,
            analyze_song_file,
            reveal_song_file,
            delete_song,
            exclude_library_songs,
            get_library_file_issues,
            resolve_library_file_issue,
            get_playlists,
            create_playlist,
            delete_playlist,
            add_to_playlist,
            remove_from_playlist,
            get_playlist_songs,
            get_albums,
            watch_library_folder,
            // Phase 6 queue commands
            get_queue_songs,
            set_queue,
            add_to_queue,
            remove_from_queue,
            clear_queue,
            reorder_queue,
            // Phase 8 game commands
            get_game_statuses,
            set_game_targets,
            focus_game,
            tap_note_preview,
            set_game_instrument_mapping,
            // Phase 9 modular config commands
            get_games,
            get_game_configs,
            get_keyboard_layouts,
            reload_game_configs,
            reset_game_configs,
            open_games_folder,
            open_layouts_folder,
            // App utility & update commands
            get_app_version,
            open_url,
            open_folder_in_explorer,
            fetch_github_releases,
            // Logging & diagnostics commands
            log_step,
            log_page_visit,
            log_message,
            clear_app_logs,
            open_logs_folder,
            read_log_tail,
            set_crash_log_verbosity,
            set_debug_mode,
            // Telemetry & sentry commands
            set_telemetry_enabled,
            send_sentry_test,
            capture_telemetry_exception,
            capture_telemetry_message,
        ])
        .run(tauri::generate_context!())
        .expect("error while building tauri application");
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_shortcut_parsing() {
        assert!("ctrl+alt+space".parse::<Shortcut>().is_ok());
        assert!("ctrl+alt+up".parse::<Shortcut>().is_ok());
        assert!("ctrl+alt+down".parse::<Shortcut>().is_ok());
        assert!("ctrl+alt+left".parse::<Shortcut>().is_ok());
        assert!("ctrl+alt+right".parse::<Shortcut>().is_ok());
        assert!("ctrl+alt+escape".parse::<Shortcut>().is_ok());
        assert!("shift+escape".parse::<Shortcut>().is_ok());
        assert!("ctrl+space".parse::<Shortcut>().is_ok());
        assert!("ctrl+right".parse::<Shortcut>().is_ok());
        assert!("ctrl+left".parse::<Shortcut>().is_ok());
        assert!("ctrl+up".parse::<Shortcut>().is_ok());
        assert!("ctrl+down".parse::<Shortcut>().is_ok());
        assert!("ctrl+alt+f1".parse::<Shortcut>().is_ok());
        assert!("ctrl+alt+a".parse::<Shortcut>().is_ok());
        assert!("ctrl+alt+1".parse::<Shortcut>().is_ok());
        assert!("ctrl+alt+esc".parse::<Shortcut>().is_ok());

        let p1 = HotkeyBindingPayload {
            action: "PlayPause".to_string(),
            label: None,
            key: "Space".to_string(),
            ctrl: true,
            alt: true,
            shift: false,
        };
        let s1 = format_shortcut_string(&p1).unwrap();
        assert_eq!(s1, "ctrl+alt+space");
        assert!(s1.parse::<Shortcut>().is_ok());

        let p2 = HotkeyBindingPayload {
            action: "Next".to_string(),
            label: None,
            key: "ArrowRight".to_string(),
            ctrl: true,
            alt: true,
            shift: false,
        };
        let s2 = format_shortcut_string(&p2).unwrap();
        assert_eq!(s2, "ctrl+alt+right");
        assert!(s2.parse::<Shortcut>().is_ok());

        let p3 = HotkeyBindingPayload {
            action: "Panic".to_string(),
            label: None,
            key: "Esc".to_string(),
            ctrl: true,
            alt: true,
            shift: false,
        };
        let s3 = format_shortcut_string(&p3).unwrap();
        assert_eq!(s3, "ctrl+alt+escape");
        assert!(s3.parse::<Shortcut>().is_ok());

        let p_empty = HotkeyBindingPayload {
            action: "PlayPause".to_string(),
            label: None,
            key: "".to_string(),
            ctrl: false,
            alt: false,
            shift: false,
        };
        assert!(format_shortcut_string(&p_empty).is_none());
    }
}
