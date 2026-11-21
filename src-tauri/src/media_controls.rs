//! Native media keys / Now Playing: Windows SMTC, macOS MediaPlayer, Linux MPRIS.
//! Keep platform objects on Tauri's main thread (required by macOS), independent
//! of configurable global shortcuts and without a WebView audio element.
use std::{
    cell::RefCell,
    fs, io,
    path::{Path, PathBuf},
    sync::{Mutex, OnceLock},
    time::Duration,
};
use amp_core::diagnostics::{ServiceState, ServiceStatus};
use reqwest::Url;
use serde::{Deserialize, Serialize};
use souvlaki::{
    MediaControlEvent, MediaControls, MediaMetadata, MediaPlayback, MediaPosition, PlatformConfig,
    SeekDirection,
};
use tauri::{AppHandle, Emitter, Manager};

const APP_ICON_PNG: &[u8] = include_bytes!("../icons/icon.png");
const FALLBACK_ICON_FILE_NAME: &str = "media-controls-icon.png";

fn observed_status() -> &'static Mutex<ServiceStatus> {
    static STATUS: OnceLock<Mutex<ServiceStatus>> = OnceLock::new();
    STATUS.get_or_init(Mutex::default)
}
pub fn status() -> ServiceStatus { observed_status().lock().unwrap_or_else(|e| e.into_inner()).clone() }
fn record_status(status: ServiceStatus) { *observed_status().lock().unwrap_or_else(|e| e.into_inner()) = status; }

#[cfg(target_os = "windows")]
mod windows;

#[derive(Clone, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Snapshot {
    title: String,
    artist: String,
    album: String,
    #[serde(default)]
    cover_url: Option<String>,
    duration_seconds: f64,
    position_seconds: f64,
    status: Status,
    volume: f64,
}

#[derive(Clone, Copy, Deserialize)]
enum Status {
    Playing,
    Paused,
    Stopped,
}

#[derive(Clone, Debug, PartialEq, Serialize)]
#[serde(tag = "action", rename_all = "camelCase")]
pub(crate) enum Action {
    Play,
    Pause,
    Toggle,
    Stop,
    Next,
    Previous,
    Seek { seconds: f64 },
    SeekBy { seconds: f64 },
    Volume { volume: f64 },
}

fn action_for(event: MediaControlEvent) -> Option<Action> {
    let offset = |direction, seconds: f64| match direction {
        SeekDirection::Forward => seconds,
        SeekDirection::Backward => -seconds,
    };
    Some(match event {
        MediaControlEvent::Play => Action::Play,
        MediaControlEvent::Pause => Action::Pause,
        MediaControlEvent::Toggle => Action::Toggle,
        MediaControlEvent::Stop => Action::Stop,
        MediaControlEvent::Next => Action::Next,
        MediaControlEvent::Previous => Action::Previous,
        MediaControlEvent::SetPosition(position) => Action::Seek {
            seconds: position.0.as_secs_f64(),
        },
        MediaControlEvent::Seek(direction) => Action::SeekBy {
            seconds: offset(direction, 5.0),
        },
        MediaControlEvent::SeekBy(direction, amount) => Action::SeekBy {
            seconds: offset(direction, amount.as_secs_f64()),
        },
        MediaControlEvent::SetVolume(volume) if volume.is_finite() => Action::Volume {
            volume: volume.clamp(0.0, 2.0),
        },
        _ => return None,
    })
}

#[derive(Clone, Debug, PartialEq, Eq)]
struct PublishedMetadata {
    title: String,
    artist: String,
    album: String,
    cover_url: Option<String>,
    duration: Duration,
}

struct Session {
    controls: MediaControls,
    metadata: Option<PublishedMetadata>,
}

thread_local! {
    static SESSION: RefCell<Option<Session>> = const { RefCell::new(None) };
    static INITIALIZED: RefCell<bool> = const { RefCell::new(false) };
}

fn create_session(app: &AppHandle) -> Result<Session, String> {
    #[cfg(target_os = "windows")]
    let hwnd = Some(
        app.get_webview_window("main")
            .ok_or("Main window is unavailable")?
            .hwnd()
            .map_err(|error| error.to_string())?
            .0 as *mut std::ffi::c_void,
    );
    #[cfg(not(target_os = "windows"))]
    let hwnd = None;

    let mut controls = MediaControls::new(PlatformConfig {
        dbus_name: "AutoMidiPlayer",
        display_name: "Auto MIDI Player",
        hwnd,
    })
    .map_err(|error| error.to_string())?;
    let app = app.clone();
    controls
        .attach(move |event| {
            if event == MediaControlEvent::Raise {
                if let Some(window) = app.get_webview_window("main") {
                    let _ = window.show();
                    let _ = window.unminimize();
                    let _ = window.set_focus();
                }
            } else if let Some(action) = action_for(event) {
                log::debug!("System media control: {action:?}");
                if let Err(error) = app.emit("playback:media_action", action) {
                    log::warn!("Unable to dispatch media control: {error}");
                }
            }
        })
        .map_err(|error| error.to_string())?;
    Ok(Session {
        controls,
        metadata: None,
    })
}

fn duration(seconds: f64) -> Duration {
    Duration::try_from_secs_f64(seconds).unwrap_or(Duration::ZERO)
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

fn ensure_regular_dir(path: &Path) -> io::Result<()> {
    match fs::symlink_metadata(path) {
        Ok(meta) if is_link(&meta) || !meta.is_dir() => Err(io::Error::new(
            io::ErrorKind::InvalidInput,
            format!("Expected regular directory at {}", path.display()),
        )),
        Ok(_) => Ok(()),
        Err(error) if error.kind() == io::ErrorKind::NotFound => Ok(()),
        Err(error) => Err(error),
    }
}

/// Materializes the bundled application icon under `<root>/cache` so OS media
/// sessions that require a file path/URI can display it when a song lacks cover art.
fn ensure_fallback_icon_at(root: &Path) -> io::Result<PathBuf> {
    let cache_dir = root.join("cache");
    let icon_path = cache_dir.join(FALLBACK_ICON_FILE_NAME);
    ensure_regular_dir(root)?;
    ensure_regular_dir(&cache_dir)?;

    match fs::symlink_metadata(&icon_path) {
        Ok(meta) if is_link(&meta) || !meta.is_file() => {
            return Err(io::Error::new(
                io::ErrorKind::InvalidInput,
                format!("Expected regular file at {}", icon_path.display()),
            ));
        }
        Ok(meta) if meta.len() == APP_ICON_PNG.len() as u64 => {
            if let Ok(existing) = fs::read(&icon_path) {
                if existing == APP_ICON_PNG {
                    return Ok(icon_path);
                }
            }
        }
        Ok(_) => {}
        Err(error) if error.kind() == io::ErrorKind::NotFound => {}
        Err(error) => return Err(error),
    }

    fs::create_dir_all(&cache_dir)?;
    ensure_regular_dir(&cache_dir)?;
    fs::write(&icon_path, APP_ICON_PNG)?;
    Ok(icon_path)
}

#[cfg(any(windows, test))]
fn normalize_windows_path(path: &str) -> String {
    let normalized = path.replace('/', "\\");
    if let Some(unc) = normalized.strip_prefix(r"\\?\UNC\") {
        format!(r"\\{unc}")
    } else if let Some(stripped) = normalized.strip_prefix(r"\\?\") {
        stripped.to_string()
    } else {
        normalized
    }
}

fn file_cover_url(path: &Path) -> Option<String> {
    let absolute = if path.is_absolute() {
        path.to_path_buf()
    } else {
        std::path::absolute(path).ok()?
    };
    #[cfg(windows)]
    {
        // souvlaki 0.9.3 strips `file://` verbatim and passes the remainder to
        // `StorageFile::GetFileFromPathAsync`, which rejects leading slashes,
        // percent-encoded spaces, and `\\?\` prefixes.
        let raw = absolute.to_str()?;
        let normalized = normalize_windows_path(raw);
        (!normalized.is_empty()).then(|| format!("file://{normalized}"))
    }
    #[cfg(not(windows))]
    {
        Url::from_file_path(&absolute).ok().map(String::from)
    }
}

fn default_cover_url() -> Option<String> {
    match ensure_fallback_icon_at(&amp_core::paths::app_data_dir()) {
        Ok(path) => file_cover_url(&path),
        Err(error) => {
            log::warn!("Unable to prepare default media controls icon: {error}");
            None
        }
    }
}

fn percent_decode_utf8(input: &str) -> Option<String> {
    let bytes = input.as_bytes();
    let mut decoded = Vec::with_capacity(bytes.len());
    let mut index = 0;
    while index < bytes.len() {
        if bytes[index] == b'%' {
            let hex = std::str::from_utf8(bytes.get(index + 1..index + 3)?).ok()?;
            let byte = u8::from_str_radix(hex, 16).ok()?;
            decoded.push(byte);
            index += 3;
        } else {
            decoded.push(bytes[index]);
            index += 1;
        }
    }
    let text = String::from_utf8(decoded).ok()?;
    (!text.contains('\0')).then_some(text)
}

fn resolve_asset_url_path(url: &Url) -> Option<PathBuf> {
    let trimmed = url.path().strip_prefix('/').unwrap_or(url.path());
    if let Some(decoded) = percent_decode_utf8(trimmed) {
        let candidate = PathBuf::from(decoded);
        if candidate.is_absolute() && candidate.is_file() {
            return Some(candidate);
        }
    }
    #[cfg(not(windows))]
    if let Some(decoded) = percent_decode_utf8(url.path()) {
        let candidate = PathBuf::from(decoded);
        if candidate.is_absolute() && candidate.is_file() {
            return Some(candidate);
        }
    }
    None
}

fn resolve_custom_cover_url(raw: Option<&str>) -> Option<String> {
    let trimmed = raw?.trim();
    if trimmed.is_empty() {
        return None;
    }

    if let Ok(url) = Url::parse(trimmed) {
        let scheme = url.scheme();
        if scheme.eq_ignore_ascii_case("http") || scheme.eq_ignore_ascii_case("https") {
            let host = url.host_str()?;
            if host.eq_ignore_ascii_case("asset.localhost") {
                return resolve_asset_url_path(&url).and_then(|path| file_cover_url(&path));
            }
            return Some(url.to_string());
        }
        if scheme.eq_ignore_ascii_case("asset") {
            let host = url.host_str()?;
            if host.eq_ignore_ascii_case("localhost") {
                return resolve_asset_url_path(&url).and_then(|path| file_cover_url(&path));
            }
            return None;
        }
        if scheme.eq_ignore_ascii_case("file") {
            let path = url.to_file_path().ok()?;
            return path.is_file().then(|| file_cover_url(&path)).flatten();
        }
        // Single-letter schemes are Windows drive letters (e.g. `C:\cover.png`),
        // not URL schemes. Reject other schemes like `data:` or `blob:`.
        if scheme.len() > 1 {
            return None;
        }
    }

    let path = Path::new(trimmed);
    (path.is_absolute() && path.is_file())
        .then(|| file_cover_url(path))
        .flatten()
}

fn publish_cover_with_fallback<F, G>(
    custom_cover_url: Option<&str>,
    fallback_cover_url: F,
    mut apply: G,
) -> Result<(), String>
where
    F: FnOnce() -> Option<String>,
    G: FnMut(Option<&str>) -> Result<(), String>,
{
    if let Some(custom) = custom_cover_url {
        match apply(Some(custom)) {
            Ok(()) => return Ok(()),
            Err(error) => {
                log::warn!("Unable to apply custom media controls artwork: {error}");
            }
        }
    }
    if let Some(fallback) = fallback_cover_url() {
        match apply(Some(&fallback)) {
            Ok(()) => return Ok(()),
            Err(error) => {
                log::warn!("Unable to apply default media controls icon: {error}");
            }
        }
    }
    apply(None)
}

fn sync(app: &AppHandle, snapshot: Snapshot) -> Result<(), String> {
    #[cfg(windows)]
    crate::taskbar::sync(app, matches!(snapshot.status, Status::Playing),
        snapshot.duration_seconds > 0.0 || !snapshot.title.is_empty());
    // A missing desktop D-Bus session must not prevent the player from starting.
    // Try once per app launch so an unavailable service doesn't flood the log.
    if !INITIALIZED.with(|initialized| initialized.replace(true)) {
        #[cfg(target_os = "windows")]
        if let Err(error) = windows::install(app) {
            log::warn!("Unable to attach Windows media commands: {error}");
        }
        let controls = create_session(app)?;
        SESSION.with(|session| *session.borrow_mut() = Some(controls));
        record_status(ServiceStatus::new(ServiceState::Ready));
        log::info!("System media controls initialized");
    }
    SESSION.with(|session| {
        let mut session = session.borrow_mut();
        let Some(session) = session.as_mut() else {
            return Ok(());
        };
        let length = duration(snapshot.duration_seconds);
        let metadata = PublishedMetadata {
            title: snapshot.title,
            artist: snapshot.artist,
            album: snapshot.album,
            cover_url: snapshot
                .cover_url
                .map(|url| url.trim().to_string())
                .filter(|url| !url.is_empty()),
            duration: length,
        };
        if session.metadata.as_ref() != Some(&metadata) {
            let custom_cover = resolve_custom_cover_url(metadata.cover_url.as_deref());
            let title = metadata.title.as_str();
            let artist = metadata.artist.as_str();
            let album = metadata.album.as_str();
            publish_cover_with_fallback(custom_cover.as_deref(), default_cover_url, |cover_url| {
                session
                    .controls
                    .set_metadata(MediaMetadata {
                        title: Some(title),
                        artist: Some(artist),
                        album: Some(album),
                        duration: Some(length),
                        cover_url,
                    })
                    .map_err(|error| error.to_string())
            })?;
            session.metadata = Some(metadata);
        }
        let progress = Some(MediaPosition(
            duration(snapshot.position_seconds).min(length),
        ));
        let playback = match snapshot.status {
            Status::Playing => MediaPlayback::Playing { progress },
            Status::Paused => MediaPlayback::Paused { progress },
            Status::Stopped => MediaPlayback::Stopped,
        };
        session
            .controls
            .set_playback(playback)
            .map_err(|error| error.to_string())?;
        #[cfg(target_os = "linux")]
        session
            .controls
            .set_volume(snapshot.volume.clamp(0.0, 2.0))
            .map_err(|error| error.to_string())?;
        #[cfg(not(target_os = "linux"))]
        let _ = snapshot.volume;
        Ok(())
    })
}

#[tauri::command]
pub fn sync_media_controls(app: AppHandle, snapshot: Snapshot) -> Result<(), String> {
    let handle = app.clone();
    app.run_on_main_thread(move || {
        if let Err(error) = sync(&handle, snapshot) {
            shutdown();
            record_status(ServiceStatus::with_detail(ServiceState::Unavailable, &error));
            log::warn!("Unable to update system media controls: {error}");
        }
    })
    .map_err(|error| {
        record_status(ServiceStatus::with_detail(ServiceState::Unavailable, &error));
        error.to_string()
    })
}

pub fn shutdown() {
    if status().state == ServiceState::Ready { record_status(ServiceStatus::new(ServiceState::Inactive)); }
    // Dropping MediaControls detaches its OS callbacks on the main thread.
    SESSION.with(|session| {
        session.borrow_mut().take();
    });
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::sync::atomic::{AtomicU64, Ordering};
    use std::time::{SystemTime, UNIX_EPOCH};

    struct TempDir(PathBuf);

    impl TempDir {
        fn new(label: &str) -> Self {
            static COUNTER: AtomicU64 = AtomicU64::new(0);
            let nanos = SystemTime::now()
                .duration_since(UNIX_EPOCH)
                .unwrap_or_default()
                .as_nanos();
            let id = COUNTER.fetch_add(1, Ordering::Relaxed);
            let path = std::env::temp_dir().join(format!(
                "amp-media-controls-{label}-{}-{nanos}-{id}",
                std::process::id()
            ));
            fs::create_dir_all(&path).unwrap();
            Self(path)
        }

        fn path(&self) -> &Path {
            &self.0
        }
    }

    impl Drop for TempDir {
        fn drop(&mut self) {
            let _ = fs::remove_dir_all(&self.0);
        }
    }

    fn encode_uri_component(input: &str) -> String {
        let mut out = String::with_capacity(input.len());
        for byte in input.as_bytes() {
            if byte.is_ascii_alphanumeric() || matches!(byte, b'-' | b'_' | b'.' | b'~') {
                out.push(*byte as char);
            } else {
                out.push('%');
                out.push_str(&format!("{byte:02X}"));
            }
        }
        out
    }

    #[test]
    fn passive_media_status_retains_initialization_failure() {
        assert_eq!(status().state, ServiceState::NotChecked);
        assert!(!INITIALIZED.with(|value| *value.borrow()));
        assert!(SESSION.with(|value| value.borrow().is_none()));
        record_status(ServiceStatus::with_detail(ServiceState::Unavailable, "Media service unavailable"));
        shutdown();
        assert_eq!(status().state, ServiceState::Unavailable);
        assert_eq!(status().detail.as_deref(), Some("Media service unavailable"));
        assert!(!INITIALIZED.with(|value| *value.borrow()));
        record_status(ServiceStatus::default());
    }

    #[test]
    fn ensure_fallback_icon_materializes_refreshes_and_rejects_directories() {
        let temp = TempDir::new("icon");
        let icon_path = ensure_fallback_icon_at(temp.path()).unwrap();
        assert_eq!(icon_path, temp.path().join("cache").join(FALLBACK_ICON_FILE_NAME));
        let bytes = fs::read(&icon_path).unwrap();
        assert!(bytes.starts_with(b"\x89PNG\r\n\x1a\n"));
        assert_eq!(bytes, APP_ICON_PNG);

        // Idempotent when contents already match.
        assert_eq!(ensure_fallback_icon_at(temp.path()).unwrap(), icon_path);

        // Refreshes both mismatched-length and same-length corrupted files.
        fs::write(&icon_path, b"stale").unwrap();
        assert_eq!(ensure_fallback_icon_at(temp.path()).unwrap(), icon_path);
        assert_eq!(fs::read(&icon_path).unwrap(), APP_ICON_PNG);

        let mut same_len_corrupt = APP_ICON_PNG.to_vec();
        same_len_corrupt[0] ^= 0xFF;
        fs::write(&icon_path, &same_len_corrupt).unwrap();
        assert_eq!(ensure_fallback_icon_at(temp.path()).unwrap(), icon_path);
        assert_eq!(fs::read(&icon_path).unwrap(), APP_ICON_PNG);

        // Rejects a directory colliding with the target icon path.
        fs::remove_file(&icon_path).unwrap();
        fs::create_dir(&icon_path).unwrap();
        assert!(ensure_fallback_icon_at(temp.path()).is_err());
    }

    #[test]
    fn normalize_windows_path_handles_slashes_unc_and_spaces() {
        assert_eq!(
            normalize_windows_path("C:/Users/Jed 556/cache/media-controls-icon.png"),
            r"C:\Users\Jed 556\cache\media-controls-icon.png"
        );
        assert_eq!(
            normalize_windows_path(r"\\?\C:\Users\Jed 556\icon.png"),
            r"C:\Users\Jed 556\icon.png"
        );
        assert_eq!(
            normalize_windows_path(r"\\?\UNC\server\share\icon.png"),
            r"\\server\share\icon.png"
        );
        #[cfg(windows)]
        assert_eq!(
            file_cover_url(Path::new(r"C:\Users\Jed 556\my icon.png")).as_deref(),
            Some(r"file://C:\Users\Jed 556\my icon.png")
        );
    }

    #[test]
    fn resolve_custom_cover_url_handles_missing_remote_and_local_sources() {
        let temp = TempDir::new("cover");
        let missing = temp.path().join("missing cover.png");
        let missing_str = missing.to_str().unwrap();

        for rejected in [
            None,
            Some(""),
            Some("   "),
            Some("data:image/png;base64,AAAA"),
            Some("blob:https://example.test/123"),
            Some("relative/cover.png"),
            Some("http://asset.localhost/nonexistent/cover.png"),
            Some("asset://localhost/nonexistent/cover.png"),
            Some(missing_str),
        ] {
            assert_eq!(resolve_custom_cover_url(rejected), None, "{rejected:?}");
        }

        assert_eq!(
            resolve_custom_cover_url(Some(" https://example.com/album/cover.png ")).as_deref(),
            Some("https://example.com/album/cover.png")
        );

        let local_cover = temp.path().join("cover art.png");
        fs::write(&local_cover, APP_ICON_PNG).unwrap();
        let expected = file_cover_url(&local_cover).unwrap();
        let local_path_str = local_cover.to_str().unwrap();
        let encoded_path = encode_uri_component(local_path_str);
        let file_uri = Url::from_file_path(&local_cover).unwrap().to_string();

        assert_eq!(resolve_custom_cover_url(Some(local_path_str)).as_deref(), Some(expected.as_str()));
        assert_eq!(resolve_custom_cover_url(Some(&file_uri)).as_deref(), Some(expected.as_str()));
        assert_eq!(
            resolve_custom_cover_url(Some(&format!("http://asset.localhost/{encoded_path}"))).as_deref(),
            Some(expected.as_str())
        );
        assert_eq!(
            resolve_custom_cover_url(Some(&format!("asset://localhost/{encoded_path}"))).as_deref(),
            Some(expected.as_str())
        );
    }

    #[test]
    fn publish_cover_with_fallback_orders_custom_icon_fallback_and_none() {
        let mut attempts = Vec::new();
        publish_cover_with_fallback(
            None,
            || Some("file://app-icon.png".into()),
            |cover| {
                attempts.push(cover.map(str::to_string));
                Ok(())
            },
        )
        .unwrap();
        assert_eq!(attempts, vec![Some("file://app-icon.png".into())]);

        let mut attempts = Vec::new();
        publish_cover_with_fallback(
            Some("https://example.test/cover.png"),
            || panic!("fallback should not be materialized when custom cover succeeds"),
            |cover| {
                attempts.push(cover.map(str::to_string));
                Ok(())
            },
        )
        .unwrap();
        assert_eq!(attempts, vec![Some("https://example.test/cover.png".into())]);

        let mut attempts = Vec::new();
        publish_cover_with_fallback(
            Some("https://example.test/broken.png"),
            || Some("file://app-icon.png".into()),
            |cover| {
                attempts.push(cover.map(str::to_string));
                if cover == Some("https://example.test/broken.png") {
                    Err("custom failed".into())
                } else {
                    Ok(())
                }
            },
        )
        .unwrap();
        assert_eq!(
            attempts,
            vec![
                Some("https://example.test/broken.png".into()),
                Some("file://app-icon.png".into()),
            ]
        );

        let mut attempts = Vec::new();
        publish_cover_with_fallback(
            Some("https://example.test/broken.png"),
            || Some("file://app-icon.png".into()),
            |cover| {
                attempts.push(cover.map(str::to_string));
                if cover.is_some() {
                    Err("thumbnail failed".into())
                } else {
                    Ok(())
                }
            },
        )
        .unwrap();
        assert_eq!(
            attempts,
            vec![
                Some("https://example.test/broken.png".into()),
                Some("file://app-icon.png".into()),
                None,
            ]
        );

        let err = publish_cover_with_fallback(None, || None, |_| Err("metadata failed".into()))
            .unwrap_err();
        assert_eq!(err, "metadata failed");
    }

    #[cfg(target_os = "windows")]
    #[test]
    #[ignore = "requires an interactive Windows desktop with SMTC available"]
    fn windows_native_session_smoke() {
        use windows_sys::Win32::UI::WindowsAndMessaging::{CreateWindowExW, DestroyWindow};
        let class: Vec<u16> = "STATIC\0".encode_utf16().collect();
        let title: Vec<u16> = "AMP media controls test\0".encode_utf16().collect();
        let hwnd = unsafe {
            CreateWindowExW(
                0,
                class.as_ptr(),
                title.as_ptr(),
                0,
                0,
                0,
                1,
                1,
                std::ptr::null_mut(),
                std::ptr::null_mut(),
                std::ptr::null_mut(),
                std::ptr::null(),
            )
        };
        assert!(!hwnd.is_null(), "Could not create an invisible test window");
        struct WindowGuard(windows_sys::Win32::Foundation::HWND);
        impl Drop for WindowGuard {
            fn drop(&mut self) {
                unsafe {
                    DestroyWindow(self.0);
                }
            }
        }
        let window = WindowGuard(hwnd);
        let temp = TempDir::new("smtc-smoke");
        let icon_path = ensure_fallback_icon_at(temp.path()).expect("Materialize fallback icon");
        let cover_url = file_cover_url(&icon_path).expect("Format fallback icon URL");
        let mut controls = MediaControls::new(PlatformConfig {
            dbus_name: "AutoMidiPlayerTest",
            display_name: "AMP media controls test",
            hwnd: Some(window.0),
        })
        .expect("Create Windows SMTC session");
        controls
            .attach(|_| {})
            .expect("Attach native media buttons");
        controls
            .set_metadata(MediaMetadata {
                title: Some("Native media controls smoke test"),
                duration: Some(Duration::from_secs(60)),
                cover_url: Some(&cover_url),
                ..MediaMetadata::default()
            })
            .expect("Publish native metadata with app icon");
        controls
            .set_playback(MediaPlayback::Paused {
                progress: Some(MediaPosition(Duration::from_secs(10))),
            })
            .expect("Publish native playback state");
        controls.detach().expect("Detach native media buttons");
    }

    #[test]
    fn native_commands_preserve_explicit_play_and_pause() {
        for (event, action) in [
            (MediaControlEvent::Play, Action::Play),
            (MediaControlEvent::Pause, Action::Pause),
            (MediaControlEvent::Toggle, Action::Toggle),
            (MediaControlEvent::Stop, Action::Stop),
            (MediaControlEvent::Next, Action::Next),
            (MediaControlEvent::Previous, Action::Previous),
        ] {
            assert_eq!(action_for(event), Some(action));
        }
    }

    #[test]
    fn native_seek_units_and_payload_match_the_renderer() {
        let action = action_for(MediaControlEvent::SeekBy(
            SeekDirection::Backward,
            Duration::from_millis(1250),
        ))
        .unwrap();
        assert_eq!(
            serde_json::to_value(action).unwrap(),
            serde_json::json!({ "action": "seekBy", "seconds": -1.25 })
        );
        assert_eq!(
            action_for(MediaControlEvent::SetPosition(MediaPosition(
                Duration::from_secs(42)
            ))),
            Some(Action::Seek { seconds: 42.0 })
        );
        assert_eq!(action_for(MediaControlEvent::SetVolume(f64::NAN)), None);
        assert_eq!(
            action_for(MediaControlEvent::OpenUri("file:///untrusted.mid".into())),
            None
        );
    }
}
