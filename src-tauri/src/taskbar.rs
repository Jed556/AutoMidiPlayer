//! Windows thumbnail toolbar. All COM objects and icons stay on the window thread.
use tauri::{AppHandle, Emitter, Manager};
use windows::{
    core::w,
    Win32::{
        Foundation::HWND as ComHwnd,
        System::Com::{CoCreateInstance, CLSCTX_INPROC_SERVER},
        UI::{
            Shell::{
                ITaskbarList3, TaskbarList, THBF_DISABLED, THBF_ENABLED, THBN_CLICKED, THB_FLAGS,
                THB_ICON, THB_TOOLTIP, THUMBBUTTON,
            },
            WindowsAndMessaging::{CreateIcon, DestroyIcon, HICON},
        },
    },
};
use windows_sys::Win32::{
    Foundation::{HWND, LPARAM, LRESULT, WPARAM},
    UI::{
        Shell::{DefSubclassProc, RemoveWindowSubclass, SetWindowSubclass},
        WindowsAndMessaging::{
            RegisterWindowMessageW, SendMessageW, WM_APP, WM_COMMAND, WM_NCDESTROY,
        },
    },
};

const SUBCLASS_ID: usize = 0x414D5054;
const SYNC: u32 = WM_APP + 0x415;
const PREVIOUS: u32 = 0x4150;
const TOGGLE: u32 = PREVIOUS + 1;
const NEXT: u32 = PREVIOUS + 2;

fn action_for_click(wparam: WPARAM) -> Option<crate::media_controls::Action> {
    use crate::media_controls::Action;
    if ((wparam >> 16) & 0xffff) as u32 != THBN_CLICKED {
        return None;
    }
    Some(match (wparam & 0xffff) as u32 {
        PREVIOUS => Action::Previous,
        TOGGLE => Action::Toggle,
        NEXT => Action::Next,
        _ => return None,
    })
}

struct Icon(HICON);
impl Drop for Icon {
    fn drop(&mut self) {
        unsafe {
            let _ = DestroyIcon(self.0);
        }
    }
}

// Small monochrome transport glyphs with transparent backgrounds.
fn icon(kind: usize) -> windows::core::Result<Icon> {
    let mut pixels = [0u8; 16 * 16 * 4];
    let mut mask = [255u8; 16 * 2];
    for y in 2usize..14 {
        for x in 2usize..14 {
            let play = |x: usize| (4..=12).contains(&x) && y.abs_diff(8) * 2 <= 12 - x;
            let visible = match kind {
                0 => (3..=4).contains(&x) || play(16 - x),
                1 => play(x),
                2 => (4..=6).contains(&x) || (10..=12).contains(&x),
                _ => (12..=13).contains(&x) || play(x),
            };
            if visible {
                pixels[(y * 16 + x) * 4..(y * 16 + x + 1) * 4].fill(255);
                mask[y * 2 + x / 8] &= !(0x80 >> (x % 8));
            }
        }
    }
    unsafe { CreateIcon(None, 16, 16, 1, 32, mask.as_ptr(), pixels.as_ptr()).map(Icon) }
}

struct Toolbar {
    app: AppHandle,
    created_message: u32,
    taskbar: Option<ITaskbarList3>,
    attempted: bool,
    icons: Vec<Icon>,
    playing: bool,
    available: bool,
}

impl Toolbar {
    fn buttons(&self) -> [THUMBBUTTON; 3] {
        let center = if self.playing { 2 } else { 1 };
        [
            (PREVIOUS, 0, "Previous"),
            (TOGGLE, center, if self.playing { "Pause" } else { "Play" }),
            (NEXT, 3, "Next"),
        ]
        .map(|(id, glyph, label)| {
            let mut button = THUMBBUTTON::default();
            button.dwMask = THB_FLAGS | THB_ICON | THB_TOOLTIP;
            button.iId = id;
            button.hIcon = self.icons[glyph].0;
            button.dwFlags = if self.available {
                THBF_ENABLED
            } else {
                THBF_DISABLED
            };
            for (target, code) in button.szTip.iter_mut().zip(label.encode_utf16()) {
                *target = code;
            }
            button
        })
    }

    unsafe fn create(&mut self, hwnd: HWND) -> windows::core::Result<()> {
        // Explorer sends this again after restarting; create a fresh toolbar then.
        self.taskbar = None;
        self.attempted = true;
        if self.icons.is_empty() {
            self.icons = (0..4).map(icon).collect::<windows::core::Result<_>>()?;
        }
        let taskbar: ITaskbarList3 = CoCreateInstance(&TaskbarList, None, CLSCTX_INPROC_SERVER)?;
        taskbar.HrInit()?;
        taskbar.ThumbBarAddButtons(ComHwnd(hwnd), &self.buttons())?;
        self.taskbar = Some(taskbar);
        Ok(())
    }

    unsafe fn update(&self, hwnd: HWND) {
        if let Some(taskbar) = &self.taskbar {
            if let Err(error) = taskbar.ThumbBarUpdateButtons(ComHwnd(hwnd), &self.buttons()) {
                log::debug!("Unable to update taskbar controls: {error}");
            }
        }
    }
}

unsafe extern "system" fn subclass(
    hwnd: HWND,
    message: u32,
    wparam: WPARAM,
    lparam: LPARAM,
    id: usize,
    data: usize,
) -> LRESULT {
    let toolbar = &mut *(data as *mut Toolbar);
    if message == toolbar.created_message {
        if let Err(error) = toolbar.create(hwnd) {
            log::warn!("Taskbar thumbnail controls unavailable: {error}");
        }
    } else if message == SYNC {
        let playing = wparam & 1 != 0;
        let available = wparam & 2 != 0;
        let changed = toolbar.playing != playing || toolbar.available != available;
        toolbar.playing = playing;
        toolbar.available = available;
        // WebView creation can pump messages before setup installs this subclass.
        // The first frontend snapshot arrives after the window has been shown.
        if !toolbar.attempted {
            if let Err(error) = toolbar.create(hwnd) {
                log::warn!("Taskbar thumbnail controls unavailable: {error}");
            }
        } else if changed {
            toolbar.update(hwnd);
        }
        return 0;
    } else if message == WM_COMMAND {
        if let Some(action) = action_for_click(wparam) {
            if toolbar.available {
                let _ = toolbar.app.emit("playback:media_action", action);
            }
            return 0;
        }
    } else if message == WM_NCDESTROY {
        RemoveWindowSubclass(hwnd, Some(subclass), id);
        drop(Box::from_raw(data as *mut Toolbar));
    }
    DefSubclassProc(hwnd, message, wparam, lparam)
}

pub fn install(app: &AppHandle) -> Result<(), String> {
    let hwnd = app
        .get_webview_window("main")
        .ok_or("Main window unavailable")?
        .hwnd()
        .map_err(|e| e.to_string())?
        .0;
    let message = unsafe { RegisterWindowMessageW(w!("TaskbarButtonCreated").as_ptr()) };
    if message == 0 {
        return Err(std::io::Error::last_os_error().to_string());
    }
    let toolbar = Box::into_raw(Box::new(Toolbar {
        app: app.clone(),
        created_message: message,
        taskbar: None,
        attempted: false,
        icons: Vec::new(),
        playing: false,
        available: false,
    }));
    if unsafe { SetWindowSubclass(hwnd, Some(subclass), SUBCLASS_ID, toolbar as usize) } == 0 {
        unsafe {
            drop(Box::from_raw(toolbar));
        }
        return Err(std::io::Error::last_os_error().to_string());
    }
    Ok(())
}

pub fn sync(app: &AppHandle, playing: bool, available: bool) {
    if let Some(window) = app.get_webview_window("main") {
        if let Ok(hwnd) = window.hwnd() {
            unsafe {
                SendMessageW(
                    hwnd.0,
                    SYNC,
                    usize::from(playing) | (usize::from(available) << 1),
                    0,
                );
            }
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn thumbnail_commands_only_dispatch_known_button_clicks() {
        use crate::media_controls::Action;
        for (id, action) in [
            (PREVIOUS, Action::Previous),
            (TOGGLE, Action::Toggle),
            (NEXT, Action::Next),
        ] {
            assert_eq!(
                action_for_click(((THBN_CLICKED as usize) << 16) | id as usize),
                Some(action)
            );
            assert_eq!(action_for_click(id as usize), None);
        }
        assert_eq!(action_for_click(((THBN_CLICKED as usize) << 16) | 12), None);
    }

    #[test]
    fn transport_icons_are_valid_native_resources() {
        for kind in 0..4 {
            let glyph = icon(kind).expect("Create transport icon");
            assert!(!glyph.0.is_invalid());
        }
    }
}
