//! Signed native updates. Packages remain in memory until installation, so a
//! background download can be reused without trusting an on-disk executable.
use std::sync::{atomic::{AtomicBool, Ordering}, Arc, Mutex};
use std::time::Duration;
use tauri::{ipc::Channel, AppHandle, Manager, State};
use tauri_plugin_updater::{Update, UpdaterExt};

const RESUME_PROGRESS: u8 = 75;

#[derive(Clone, serde::Serialize)]
#[serde(rename_all = "camelCase")]
pub struct UpdateResume {
    version: String,
    progress: u8,
}

#[derive(serde::Serialize, serde::Deserialize)]
#[serde(rename_all = "camelCase")]
struct ResumeMarker {
    version: String,
    created_at: i64,
}

#[derive(Clone, serde::Serialize)]
#[serde(rename_all = "camelCase")]
pub struct UpdateProgress {
    phase: &'static str,
    progress: f64,
    downloaded_bytes: u64,
    total_bytes: Option<u64>,
}

fn report(channel: &Channel<UpdateProgress>, phase: &'static str, progress: f64) {
    let _ = channel.send(UpdateProgress { phase, progress, downloaded_bytes: 0, total_bytes: None });
}

#[derive(serde::Serialize)]
#[serde(rename_all = "camelCase")]
pub struct PreparedUpdate {
    version: String,
    downloaded: bool,
}

struct PendingUpdate {
    update: Update,
    bytes: Option<Arc<Vec<u8>>>,
}

#[derive(Default)]
pub struct UpdateState {
    pending: Mutex<Option<PendingUpdate>>,
    busy: AtomicBool,
    install_progress: Mutex<Option<Channel<UpdateProgress>>>,
}

// Never hold the pending-package mutex across a network request or installer.
struct Operation<'a>(&'a AtomicBool);
impl Drop for Operation<'_> {
    fn drop(&mut self) { self.0.store(false, Ordering::Release); }
}
impl UpdateState {
    fn begin(&self) -> Result<Operation<'_>, String> {
        self.busy.compare_exchange(false, true, Ordering::AcqRel, Ordering::Acquire)
            .map_err(|_| "Another update operation is already running.".to_owned())?;
        Ok(Operation(&self.busy))
    }
}

fn marker_path() -> std::path::PathBuf {
    amp_core::paths::app_data_dir().join("update-resume.json")
}

pub fn load_resume(version: &str) -> Option<UpdateResume> {
    let path = marker_path();
    let marker = std::fs::read(&path).ok()
        .and_then(|bytes| serde_json::from_slice::<ResumeMarker>(&bytes).ok());
    if let Some(marker) = marker {
        if marker_matches(&marker, version, chrono::Utc::now().timestamp()) {
            return Some(UpdateResume { version: marker.version, progress: RESUME_PROGRESS });
        }
    }
    // Failed/cancelled installs and stale markers must not affect a normal launch.
    let _ = std::fs::remove_file(path);
    None
}

fn marker_matches(marker: &ResumeMarker, version: &str, now: i64) -> bool {
    marker.version == version && (0..86_400).contains(&(now - marker.created_at))
}

fn clear_resume_marker() -> Result<(), String> {
    match std::fs::remove_file(marker_path()) {
        Ok(()) => Ok(()),
        Err(error) if error.kind() == std::io::ErrorKind::NotFound => Ok(()),
        Err(error) => Err(error.to_string()),
    }
}

#[tauri::command]
pub fn complete_update(app: AppHandle) -> Result<(), String> {
    clear_resume_marker()?;
    if let Some(state) = app.try_state::<crate::StartupState>() {
        state.lock().map_err(|error| error.to_string())?.update_resume = None;
    }
    Ok(())
}

fn release_endpoint(tag_name: &str) -> Result<String, String> {
    let version = tag_name.trim_start_matches(['v', 'V']);
    if !version.as_bytes().first().is_some_and(u8::is_ascii_digit)
        || !tag_name.bytes().all(|b| b.is_ascii_alphanumeric() || b".-_+".contains(&b)) {
        return Err("Invalid update release tag.".into());
    }
    Ok(format!("https://github.com/Jed556/AutoMidiPlayer/releases/download/{tag_name}/latest.json"))
}

#[tauri::command]
pub async fn prepare_update(app: AppHandle, state: State<'_, UpdateState>, tag_name: String) -> Result<PreparedUpdate, String> {
    #[cfg(windows)]
    if std::env::current_exe().ok().and_then(|exe| exe.parent().map(|dir| dir.join("portable.txt")))
        .is_some_and(|marker| marker.is_file()) {
        return Err("Update this portable copy using the ZIP on the GitHub release page.".into());
    }
    let _operation = state.begin()?;
    let endpoint = release_endpoint(&tag_name)?;
    let version = tag_name.trim_start_matches(['v', 'V']);
    {
        let pending = state.pending.lock().map_err(|e| e.to_string())?;
        if let Some(pending) = pending.as_ref().filter(|p| p.update.version == version) {
            return Ok(PreparedUpdate { version: pending.update.version.clone(), downloaded: pending.bytes.is_some() });
        }
    }

    let configured_key = option_env!("AMP_UPDATER_PUBLIC_KEY").unwrap_or("").trim();
    let config_key = app.config().plugins.0.get("updater")
        .and_then(|config| config.get("pubkey")).and_then(|key| key.as_str()).unwrap_or("");
    if configured_key.is_empty() && config_key.trim().is_empty() {
        return Err("Signed in-app updates are not configured for this build. Use the release downloads instead.".into());
    }
    let mut builder = app.updater_builder()
        .endpoints(vec![endpoint.parse().map_err(|e| format!("Invalid update endpoint: {e}"))?])
        .map_err(|e| e.to_string())?
        .timeout(Duration::from_secs(20));
    if !configured_key.is_empty() { builder = builder.pubkey(configured_key); }

    let exit_app = app.clone();
    // Preserve Tauri's normal cleanup when overriding its before-exit callback.
    builder = builder.on_before_exit(move || {
        if let Ok(channel) = exit_app.state::<UpdateState>().install_progress.lock() {
            if let Some(channel) = channel.as_ref() { report(channel, "installing", 70.0); }
        }
        exit_app.cleanup_before_exit();
    });
    let mut update = builder.build().map_err(|e| e.to_string())?.check().await
        .map_err(|e| e.to_string())?.ok_or("No newer native update is available for this installation.")?;
    if update.version != version {
        return Err("The signed update manifest does not match the selected release.".into());
    }
    update.timeout = Some(Duration::from_secs(300));
    let result = PreparedUpdate { version: update.version.clone(), downloaded: false };
    *state.pending.lock().map_err(|e| e.to_string())? = Some(PendingUpdate { update, bytes: None });
    Ok(result)
}

#[tauri::command]
pub async fn download_update(state: State<'_, UpdateState>, on_progress: Channel<UpdateProgress>) -> Result<(), String> {
    let _operation = state.begin()?;
    let update = {
        let pending = state.pending.lock().map_err(|e| e.to_string())?;
        let pending = pending.as_ref().ok_or("Prepare an update before downloading it.")?;
        if pending.bytes.is_some() { return Ok(()); }
        pending.update.clone()
    };
    report(&on_progress, "downloading", 8.0);
    let mut downloaded = 0u64;
    let mut last_report = std::time::Instant::now();
    let bytes = update.download(|chunk, total| {
        downloaded = downloaded.saturating_add(chunk as u64);
        // Downloads may produce thousands of chunks; avoid flooding the webview.
        if last_report.elapsed() >= Duration::from_millis(50) || total == Some(downloaded) {
            let ratio = total.filter(|total| *total > 0).map(|total| (downloaded as f64 / total as f64).min(1.0)).unwrap_or(0.0);
            let _ = on_progress.send(UpdateProgress {
                phase: "downloading", progress: 8.0 + ratio * 47.0,
                downloaded_bytes: downloaded, total_bytes: total,
            });
            last_report = std::time::Instant::now();
        }
    }, || report(&on_progress, "verifying", 58.0)).await.map_err(|e| e.to_string())?;
    // download() verifies the signature before returning. Cache only verified bytes.
    let mut pending = state.pending.lock().map_err(|e| e.to_string())?;
    pending.as_mut().ok_or("The pending update was lost.")?.bytes = Some(Arc::new(bytes));
    Ok(())
}

#[tauri::command]
pub async fn install_update(app: AppHandle, state: State<'_, UpdateState>, on_progress: Channel<UpdateProgress>) -> Result<(), String> {
    let _operation = state.begin()?;
    if cfg!(debug_assertions) {
        return Err("Install updates from a packaged release build, not a development session.".into());
    }
    let (update, bytes) = {
        let pending = state.pending.lock().map_err(|e| e.to_string())?;
        let pending = pending.as_ref().ok_or("No update has been prepared.")?;
        (pending.update.clone(), pending.bytes.clone().ok_or("Download the update before installing it.")?)
    };
    let marker = ResumeMarker { version: update.version.clone(), created_at: chrono::Utc::now().timestamp() };
    let path = marker_path();
    std::fs::create_dir_all(path.parent().ok_or("Invalid update data directory.")?).map_err(|e| e.to_string())?;
    std::fs::write(&path, serde_json::to_vec(&marker).map_err(|e| e.to_string())?).map_err(|e| e.to_string())?;

    // ZIP and tar.gz artifacts are extracted by the native updater. Raw installers
    // skip this phase. "Installing" covers replacement of the application files.
    let archived = bytes.starts_with(b"PK\x03\x04") || bytes.starts_with(&[0x1f, 0x8b]);
    report(&on_progress, if archived { "extracting" } else { "installing" }, if archived { 63.0 } else { 70.0 });
    let install_app = app.clone();
    *state.install_progress.lock().map_err(|e| e.to_string())? = Some(on_progress.clone());
    let result = tauri::async_runtime::spawn_blocking(move || {
        // Stop native output and shortcuts before Windows exits directly.
        if let Some(state) = install_app.try_state::<crate::AppState>() { state.engine.stop(); }
        update.install(bytes.as_slice()).map_err(|e| e.to_string())
    }).await.map_err(|e| e.to_string()).and_then(|result| result);
    if let Err(error) = result {
        *state.install_progress.lock().map_err(|e| e.to_string())? = None;
        let _ = clear_resume_marker();
        return Err(error);
    }
    // Windows exits inside install() and the installer relaunches the app.
    report(&on_progress, "restarting", RESUME_PROGRESS as f64);
    app.restart();
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn resume_requires_the_installed_version_and_a_recent_marker() {
        let marker = ResumeMarker { version: "8.1.0".into(), created_at: 100 };
        assert!(marker_matches(&marker, "8.1.0", 101));
        assert!(!marker_matches(&marker, "8.0.0", 101));
        assert!(!marker_matches(&marker, "8.1.0", 86_500));
        assert!(!marker_matches(&marker, "8.1.0", 99));
    }

    #[test]
    fn releases_cannot_redirect_the_manifest_to_another_host_or_path() {
        assert!(release_endpoint("v8.1.0-beta.1").unwrap().ends_with("/v8.1.0-beta.1/latest.json"));
        for tag in ["", ".", "..", "../other", "v8.1.0?x=y", "https://example.com", "v8.1.0#x"] {
            assert!(release_endpoint(tag).is_err());
        }
    }

    #[test]
    fn operations_are_exclusive_and_failures_release_the_guard() {
        let state = UpdateState::default();
        let operation = state.begin().unwrap();
        assert!(state.begin().is_err());
        drop(operation);
        assert!(state.begin().is_ok());
    }
}
