//! Native media keys / Now Playing: Windows SMTC, macOS MediaPlayer, Linux MPRIS.
//! Keep platform objects on Tauri's main thread (required by macOS), independent
//! of configurable global shortcuts and without a WebView audio element.
use std::{cell::RefCell, time::Duration, sync::{Mutex, OnceLock}};
use amp_core::diagnostics::{ServiceState, ServiceStatus};
use serde::{Deserialize, Serialize};
use souvlaki::{
    MediaControlEvent, MediaControls, MediaMetadata, MediaPlayback, MediaPosition, PlatformConfig,
    SeekDirection,
};
use tauri::{AppHandle, Emitter, Manager};

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
enum Action {
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

struct Session {
    controls: MediaControls,
    metadata: Option<(String, String, String, Duration)>,
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

fn sync(app: &AppHandle, snapshot: Snapshot) -> Result<(), String> {
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
        let metadata = (snapshot.title, snapshot.artist, snapshot.album, length);
        if session.metadata.as_ref() != Some(&metadata) {
            session
                .controls
                .set_metadata(MediaMetadata {
                    title: Some(&metadata.0),
                    artist: Some(&metadata.1),
                    album: Some(&metadata.2),
                    duration: Some(length),
                    cover_url: None,
                })
                .map_err(|error| error.to_string())?;
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
                ..MediaMetadata::default()
            })
            .expect("Publish native metadata");
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
