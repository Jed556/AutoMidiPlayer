//! Keep the webview and playback runtime alive while the main window is hidden.
use std::sync::atomic::{AtomicBool, Ordering};

use amp_core::settings::defaults;
use serde_json::Value;
use tauri::{
    menu::{Menu, MenuItem, PredefinedMenuItem},
    tray::{MouseButton, MouseButtonState, TrayIconBuilder, TrayIconEvent},
    AppHandle, Emitter, Manager, Window, WindowEvent,
};

const ID: &str = "main-tray";

struct TrayState {
    exit_requested: AtomicBool,
    chinese: AtomicBool,
    items: [MenuItem<tauri::Wry>; 4],
}

fn labels(chinese: bool) -> [&'static str; 4] {
    if chinese {
        ["播放/暂停", "下一首", "显示窗口", "退出"]
    } else {
        ["Play/Pause", "Next", "Show Window", "Exit"]
    }
}

fn preferences(app: &AppHandle) -> Value {
    let settings = if let Some(state) = app.try_state::<crate::AppState>() {
        state.settings.get()
    } else {
        crate::read_launch_settings()
    };
    settings.unwrap_or_else(|error| {
        log::warn!("Unable to read tray preferences: {error}");
        defaults::document().clone()
    })
}

// A failed tray installation must always leave the window accessible.
fn should_hide(settings: &Value, key: &str, available: bool, exiting: bool) -> bool {
    available
        && !exiting
        && settings
            .get(key)
            .and_then(Value::as_bool)
            .unwrap_or_else(|| defaults::value(&format!("/{key}")))
}

fn exiting(app: &AppHandle) -> bool {
    app.try_state::<TrayState>()
        .is_some_and(|state| state.exit_requested.load(Ordering::SeqCst))
}

fn hide_for(window: &Window, key: &str) -> bool {
    let app = window.app_handle();
    should_hide(
        &preferences(app),
        key,
        app.tray_by_id(ID).is_some(),
        exiting(app),
    )
}

pub fn show(app: &AppHandle) -> tauri::Result<()> {
    if let Some(window) = app.get_webview_window("main") {
        window.show()?;
        window.unminimize()?;
        window.set_focus()?;
    }
    Ok(())
}

pub fn install(app: &AppHandle) -> tauri::Result<()> {
    let settings = preferences(app);
    let chinese = settings.pointer("/ui/locale").and_then(Value::as_str) == Some("zh-CN");
    let text = labels(chinese);
    let play = MenuItem::with_id(app, "tray-play-pause", text[0], false, None::<&str>)?;
    let next = MenuItem::with_id(app, "tray-next", text[1], false, None::<&str>)?;
    let show = MenuItem::with_id(app, "tray-show", text[2], true, None::<&str>)?;
    let exit = MenuItem::with_id(app, "tray-exit", text[3], true, None::<&str>)?;
    let separator = PredefinedMenuItem::separator(app)?;
    let menu = Menu::with_items(app, &[&play, &next, &separator, &show, &exit])?;
    let icon = app
        .default_window_icon()
        .ok_or(tauri::Error::AssetNotFound("window icon".into()))?;
    TrayIconBuilder::with_id(ID)
        .icon(icon.clone())
        .tooltip("Auto MIDI Player")
        .menu(&menu)
        .show_menu_on_left_click(false)
        .on_tray_icon_event(|tray, event| {
            if matches!(
                event,
                TrayIconEvent::Click {
                    button: MouseButton::Left,
                    button_state: MouseButtonState::Up,
                    ..
                } | TrayIconEvent::DoubleClick {
                    button: MouseButton::Left,
                    ..
                }
            ) {
                if let Err(error) = self::show(tray.app_handle()) {
                    log::warn!("Unable to restore window from tray: {error}");
                }
            }
        })
        .on_menu_event(|app, event| {
            let result = match event.id.as_ref() {
                "tray-show" => self::show(app),
                "tray-exit" => {
                    app.state::<TrayState>()
                        .exit_requested
                        .store(true, Ordering::SeqCst);
                    // Use the regular close handshake so the renderer saves the session.
                    if let Some(window) = app.get_webview_window("main") {
                        window.close()
                    } else {
                        app.exit(0);
                        Ok(())
                    }
                }
                "tray-play-pause" | "tray-next" => {
                    let action = if event.id.as_ref() == "tray-next" {
                        "next"
                    } else {
                        "toggle"
                    };
                    // Share the serialized media-command path with native media keys.
                    app.emit(
                        "playback:media_action",
                        serde_json::json!({ "action": action }),
                    )
                }
                _ => Ok(()),
            };
            if let Err(error) = result {
                app.state::<TrayState>()
                    .exit_requested
                    .store(false, Ordering::SeqCst);
                log::warn!("Unable to handle tray action: {error}");
            }
        })
        .build(app)?;
    app.manage(TrayState {
        exit_requested: AtomicBool::new(false),
        chinese: AtomicBool::new(chinese),
        items: [play, next, show, exit],
    });
    Ok(())
}

pub fn refresh(app: &AppHandle, settings: &Value) {
    let Some(state) = app.try_state::<TrayState>() else {
        return;
    };
    let enabled = app.try_state::<crate::AppState>().is_some()
        && settings.get("onboardingCompleted").and_then(Value::as_bool) == Some(true);
    for item in &state.items[..2] {
        if let Err(error) = item.set_enabled(enabled) {
            log::warn!("Unable to update tray controls: {error}");
        }
    }
    let chinese = settings.pointer("/ui/locale").and_then(Value::as_str) == Some("zh-CN");
    if state.chinese.swap(chinese, Ordering::SeqCst) != chinese {
        for (item, text) in state.items.iter().zip(labels(chinese)) {
            if let Err(error) = item.set_text(text) {
                log::warn!("Unable to translate tray menu: {error}");
            }
        }
    }
}

pub fn minimize(window: &Window) -> tauri::Result<()> {
    if hide_for(window, "minimizeToTray") {
        window.hide()
    } else {
        window.minimize()
    }
}

pub fn on_window_event(window: &Window, event: &WindowEvent) {
    if window.label() != "main" {
        return;
    }
    let result = match event {
        WindowEvent::CloseRequested { api, .. } if hide_for(window, "closeToTray") => {
            // Also covers Alt+F4 and closing before the renderer has subscribed.
            match window.hide() {
                Ok(()) => {
                    api.prevent_close();
                    Ok(())
                }
                Err(error) => Err(error),
            }
        }
        // Taskbar/system-menu minimization does not call our title-bar command.
        WindowEvent::Resized(_)
            if window.is_minimized().unwrap_or(false) && hide_for(window, "minimizeToTray") =>
        {
            window.hide()
        }
        _ => Ok(()),
    };
    if let Err(error) = result {
        log::warn!("Unable to hide window to tray: {error}");
    }
}

#[tauri::command]
pub fn complete_window_close(window: Window, cancel: bool) -> Result<(), String> {
    let app = window.app_handle();
    let exiting = app
        .try_state::<TrayState>()
        .is_some_and(|state| state.exit_requested.swap(false, Ordering::SeqCst));
    if cancel {
        return show(app).map_err(|error| error.to_string());
    }
    if should_hide(
        &preferences(app),
        "closeToTray",
        app.tray_by_id(ID).is_some(),
        exiting,
    ) {
        window.hide()
    } else {
        // Destroy only after the renderer has flushed; close() would repeat the handshake.
        window.destroy()
    }
    .map_err(|error| error.to_string())
}

#[cfg(test)]
mod tests {
    use super::*;
    use serde_json::json;

    #[test]
    fn independent_preferences_keep_default_close_as_exit() {
        assert!(should_hide(&json!({}), "minimizeToTray", true, false));
        assert!(!should_hide(&json!({}), "closeToTray", true, false));
        let settings = json!({ "minimizeToTray": false, "closeToTray": true });
        assert!(!should_hide(&settings, "minimizeToTray", true, false));
        assert!(should_hide(&settings, "closeToTray", true, false));
    }

    #[test]
    fn explicit_exit_and_unavailable_tray_never_hide_the_window() {
        let settings = json!({ "minimizeToTray": true, "closeToTray": true });
        for key in ["minimizeToTray", "closeToTray"] {
            assert!(!should_hide(&settings, key, false, false));
            assert!(!should_hide(&settings, key, true, true));
        }
    }
}
