//! Read-only diagnostics. Never prepare an output backend or open an input session here.
use amp_core::diagnostics::{ServiceState, ServiceStatus};
use serde::Serialize;
use std::sync::OnceLock;
use tauri::{AppHandle, Manager};

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize)]
#[serde(rename_all = "camelCase")]
pub enum DesktopSession {
    Windows,
    Macos,
    X11,
    Wayland,
    Unknown,
}

fn desktop_session(os: &str, session: Option<&str>, wayland: bool, x11: bool) -> DesktopSession {
    match os {
        "windows" => DesktopSession::Windows,
        "macos" => DesktopSession::Macos,
        "linux" if wayland || session == Some("wayland") => DesktopSession::Wayland,
        "linux" if x11 || session == Some("x11") => DesktopSession::X11,
        _ => DesktopSession::Unknown,
    }
}

#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct Environment {
    os_name: Option<String>,
    os_version: Option<String>,
    system_architecture: String,
    app_architecture: &'static str,
    desktop_session: DesktopSession,
}

fn environment() -> &'static Environment {
    static ENVIRONMENT: OnceLock<Environment> = OnceLock::new();
    ENVIRONMENT.get_or_init(|| {
        let nonempty = |key| std::env::var_os(key).is_some_and(|value| !value.is_empty());
        Environment {
            os_name: sysinfo::System::name().ok(),
            os_version: sysinfo::System::long_os_version().ok(),
            system_architecture: sysinfo::System::cpu_arch(),
            app_architecture: std::env::consts::ARCH,
            desktop_session: desktop_session(
                std::env::consts::OS,
                std::env::var("XDG_SESSION_TYPE").ok().as_deref(),
                nonempty("WAYLAND_DISPLAY"),
                nonempty("DISPLAY"),
            ),
        }
    })
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct KeyboardStatus {
    backend: &'static str,
    #[serde(flatten)]
    status: ServiceStatus,
}

fn keyboard_backend(session: DesktopSession) -> &'static str {
    match session {
        DesktopSession::Windows => "enigoWindows",
        DesktopSession::Macos => "enigoMacos",
        DesktopSession::X11 => "enigoX11",
        DesktopSession::Wayland => "remoteDesktopPortal",
        DesktopSession::Unknown => "unknown",
    }
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct SystemSnapshot {
    environment: Environment,
    keyboard: KeyboardStatus,
    audio: ServiceStatus,
    tray: ServiceStatus,
    media_controls: ServiceStatus,
}

#[tauri::command]
pub fn get_system_snapshot(app: AppHandle) -> SystemSnapshot {
    let environment = environment().clone();
    let state = app.try_state::<crate::AppState>();
    let keyboard = state
        .as_ref()
        .map(|state| state.engine.keyboard_status())
        .unwrap_or_default();
    let audio = state
        .as_ref()
        .map(|state| {
            state
                .audio_synth
                .lock()
                .unwrap_or_else(|e| e.into_inner())
                .as_ref()
                .map(|synth| synth.output_status())
                .unwrap_or_else(|| ServiceStatus {
                    state: ServiceState::Unavailable,
                    detail: state.audio_initialization_error.clone(),
                })
        })
        .unwrap_or_default();
    let backend = keyboard_backend(environment.desktop_session);
    SystemSnapshot {
        environment,
        keyboard: KeyboardStatus {
            backend,
            status: keyboard,
        },
        audio,
        tray: ServiceStatus::new(if crate::tray::is_available(&app) {
            ServiceState::Ready
        } else {
            ServiceState::Unavailable
        }),
        media_controls: crate::media_controls::status(),
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn session_and_backend_mapping_does_not_assume_linux_is_x11() {
        for (os, session, wayland, x11, expected, backend) in [
            (
                "windows",
                None,
                false,
                false,
                DesktopSession::Windows,
                "enigoWindows",
            ),
            (
                "macos",
                None,
                false,
                false,
                DesktopSession::Macos,
                "enigoMacos",
            ),
            (
                "linux",
                Some("x11"),
                false,
                true,
                DesktopSession::X11,
                "enigoX11",
            ),
            (
                "linux",
                Some("wayland"),
                true,
                true,
                DesktopSession::Wayland,
                "remoteDesktopPortal",
            ),
            (
                "linux",
                None,
                true,
                true,
                DesktopSession::Wayland,
                "remoteDesktopPortal",
            ),
            (
                "linux",
                None,
                false,
                false,
                DesktopSession::Unknown,
                "unknown",
            ),
            (
                "other",
                None,
                false,
                false,
                DesktopSession::Unknown,
                "unknown",
            ),
        ] {
            assert_eq!(desktop_session(os, session, wayland, x11), expected);
            assert_eq!(keyboard_backend(expected), backend);
        }
    }
    #[test]
    fn static_environment_is_cached() {
        assert!(std::ptr::eq(environment(), environment()));
    }
}
