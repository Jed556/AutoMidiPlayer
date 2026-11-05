//! Some Windows keyboards deliver WM_APPCOMMAND to the foreground window
//! instead of going through SMTC. Consume it here so Windows doesn't also
//! forward the same press to the shell's active media session.
use super::Action;
use tauri::{AppHandle, Emitter, Manager};
use webview2_com::{
    AcceleratorKeyPressedEventHandler,
    Microsoft::Web::WebView2::Win32::{
        COREWEBVIEW2_KEY_EVENT_KIND, COREWEBVIEW2_KEY_EVENT_KIND_KEY_DOWN,
        COREWEBVIEW2_KEY_EVENT_KIND_SYSTEM_KEY_DOWN, COREWEBVIEW2_PHYSICAL_KEY_STATUS,
    },
};
use windows_sys::Win32::{
    Foundation::{HWND, LPARAM, LRESULT, WPARAM},
    System::SystemServices::{
        APPCOMMAND_MEDIA_NEXTTRACK, APPCOMMAND_MEDIA_PAUSE, APPCOMMAND_MEDIA_PLAY,
        APPCOMMAND_MEDIA_PLAY_PAUSE, APPCOMMAND_MEDIA_PREVIOUSTRACK, APPCOMMAND_MEDIA_STOP,
    },
    UI::{
        Input::KeyboardAndMouse::{
            VK_MEDIA_NEXT_TRACK, VK_MEDIA_PLAY_PAUSE, VK_MEDIA_PREV_TRACK, VK_MEDIA_STOP,
        },
        Shell::{DefSubclassProc, RemoveWindowSubclass, SetWindowSubclass},
        WindowsAndMessaging::{WM_APPCOMMAND, WM_NCDESTROY},
    },
};

const SUBCLASS_ID: usize = 0x414D504D;
struct Handler(Box<dyn Fn(Action)>);

fn action_for_key(key: u32) -> Option<Action> {
    Some(match key as u16 {
        VK_MEDIA_PLAY_PAUSE => Action::Toggle,
        VK_MEDIA_NEXT_TRACK => Action::Next,
        VK_MEDIA_PREV_TRACK => Action::Previous,
        VK_MEDIA_STOP => Action::Stop,
        _ => return None,
    })
}

fn is_initial_press(kind: COREWEBVIEW2_KEY_EVENT_KIND, was_key_down: bool) -> bool {
    !was_key_down
        && (kind == COREWEBVIEW2_KEY_EVENT_KIND_KEY_DOWN
            || kind == COREWEBVIEW2_KEY_EVENT_KIND_SYSTEM_KEY_DOWN)
}

fn dispatch(app: &AppHandle, action: Action) {
    log::debug!("Windows media command: {action:?}");
    if let Err(error) = app.emit("playback:media_action", action) {
        log::warn!("Unable to dispatch Windows media command: {error}");
    }
}

fn install_webview_keys(app: &AppHandle) -> Result<(), String> {
    let window = app
        .get_webview_window("main")
        .ok_or("Main window unavailable")?;
    let app = app.clone();
    window
        .with_webview(move |webview| {
            let handler = AcceleratorKeyPressedEventHandler::create(Box::new(move |_, args| {
                let Some(args) = args else {
                    return Ok(());
                };
                unsafe {
                    let mut key = 0;
                    args.VirtualKey(&mut key)?;
                    let Some(action) = action_for_key(key) else {
                        return Ok(());
                    };
                    // Consume releases/repeats too: Chromium must not forward the same
                    // physical press to Windows' media session and toggle it twice.
                    args.SetHandled(true)?;
                    let mut kind = COREWEBVIEW2_KEY_EVENT_KIND::default();
                    let mut physical = COREWEBVIEW2_PHYSICAL_KEY_STATUS::default();
                    args.KeyEventKind(&mut kind)?;
                    args.PhysicalKeyStatus(&mut physical)?;
                    if is_initial_press(kind, physical.WasKeyDown.as_bool()) {
                        dispatch(&app, action);
                    }
                }
                Ok(())
            }));
            let mut token = 0;
            // The controller owns the subscription until the WebView is destroyed.
            if let Err(error) = unsafe {
                webview
                    .controller()
                    .add_AcceleratorKeyPressed(&handler, &mut token)
            } {
                log::warn!("Unable to attach focused WebView media keys: {error}");
            }
        })
        .map_err(|error| error.to_string())
}

fn action_for_command(lparam: LPARAM) -> Option<Action> {
    // GET_APPCOMMAND_LPARAM: mask off FAPPCOMMAND_* device flags.
    Some(match ((lparam >> 16) & 0x0fff) as u32 {
        APPCOMMAND_MEDIA_PLAY_PAUSE => Action::Toggle,
        APPCOMMAND_MEDIA_PLAY => Action::Play,
        APPCOMMAND_MEDIA_PAUSE => Action::Pause,
        APPCOMMAND_MEDIA_STOP => Action::Stop,
        APPCOMMAND_MEDIA_NEXTTRACK => Action::Next,
        APPCOMMAND_MEDIA_PREVIOUSTRACK => Action::Previous,
        _ => return None,
    })
}

unsafe extern "system" fn subclass(
    hwnd: HWND,
    message: u32,
    wparam: WPARAM,
    lparam: LPARAM,
    id: usize,
    data: usize,
) -> LRESULT {
    if message == WM_APPCOMMAND {
        if let Some(action) = action_for_command(lparam) {
            let handler = &*(data as *const Handler);
            (handler.0)(action);
            return 1;
        }
    } else if message == WM_NCDESTROY {
        RemoveWindowSubclass(hwnd, Some(subclass), id);
        drop(Box::from_raw(data as *mut Handler));
    }
    DefSubclassProc(hwnd, message, wparam, lparam)
}

// Must run on the window's owning thread. WM_NCDESTROY frees the callback.
fn attach(hwnd: HWND, callback: impl Fn(Action) + 'static) -> Result<(), String> {
    let handler = Box::into_raw(Box::new(Handler(Box::new(callback))));
    if unsafe { SetWindowSubclass(hwnd, Some(subclass), SUBCLASS_ID, handler as usize) } == 0 {
        unsafe {
            drop(Box::from_raw(handler));
        }
        return Err(std::io::Error::last_os_error().to_string());
    }
    Ok(())
}

pub(super) fn install(app: &AppHandle) -> Result<(), String> {
    // WebView2 owns keyboard focus inside the app; its accelerator event runs
    // before Chromium can consume media keys or generate WM_APPCOMMAND.
    install_webview_keys(app)?;
    let hwnd = app
        .get_webview_window("main")
        .ok_or("Main window unavailable")?
        .hwnd()
        .map_err(|error| error.to_string())?
        .0;
    let app = app.clone();
    attach(hwnd, move |action| dispatch(&app, action))
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::{cell::RefCell, rc::Rc};
    use windows_sys::Win32::UI::{
        Input::KeyboardAndMouse::{VK_NEXT, VK_PRIOR},
        WindowsAndMessaging::{CreateWindowExW, DestroyWindow, SendMessageW, WM_KEYDOWN},
    };

    #[test]
    fn webview_media_keys_dispatch_only_initial_keydown() {
        use webview2_com::Microsoft::Web::WebView2::Win32::{
            COREWEBVIEW2_KEY_EVENT_KIND_KEY_UP, COREWEBVIEW2_KEY_EVENT_KIND_SYSTEM_KEY_UP,
        };
        for (key, expected) in [
            (VK_MEDIA_PLAY_PAUSE, Action::Toggle),
            (VK_MEDIA_NEXT_TRACK, Action::Next),
            (VK_MEDIA_PREV_TRACK, Action::Previous),
            (VK_MEDIA_STOP, Action::Stop),
        ] {
            assert_eq!(action_for_key(key as u32), Some(expected));
            assert!(is_initial_press(
                COREWEBVIEW2_KEY_EVENT_KIND_KEY_DOWN,
                false
            ));
            assert!(is_initial_press(
                COREWEBVIEW2_KEY_EVENT_KIND_SYSTEM_KEY_DOWN,
                false
            ));
            assert!(!is_initial_press(
                COREWEBVIEW2_KEY_EVENT_KIND_KEY_DOWN,
                true
            ));
            assert!(!is_initial_press(COREWEBVIEW2_KEY_EVENT_KIND_KEY_UP, true));
            assert!(!is_initial_press(
                COREWEBVIEW2_KEY_EVENT_KIND_SYSTEM_KEY_UP,
                false
            ));
        }
        assert_eq!(action_for_key(VK_PRIOR as u32), None);
        assert_eq!(action_for_key(VK_NEXT as u32), None);
        assert_eq!(action_for_key(0x23), None); // End remains navigation.
    }

    #[test]
    fn windows_messages_deliver_media_commands_once_and_leave_page_keys_alone() {
        let class: Vec<u16> = "STATIC\0".encode_utf16().collect();
        let hwnd = unsafe {
            CreateWindowExW(
                0,
                class.as_ptr(),
                class.as_ptr(),
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
        assert!(!hwnd.is_null());
        let actions = Rc::new(RefCell::new(Vec::new()));
        let received = actions.clone();
        attach(hwnd, move |action| received.borrow_mut().push(action)).unwrap();
        unsafe {
            for command in [
                APPCOMMAND_MEDIA_PLAY_PAUSE,
                APPCOMMAND_MEDIA_PREVIOUSTRACK,
                APPCOMMAND_MEDIA_NEXTTRACK,
                APPCOMMAND_MEDIA_PLAY,
                APPCOMMAND_MEDIA_PAUSE,
                APPCOMMAND_MEDIA_STOP,
            ] {
                assert_eq!(
                    SendMessageW(
                        hwnd,
                        WM_APPCOMMAND,
                        hwnd as usize,
                        ((command | 0x8000) << 16) as isize
                    ),
                    1
                );
            }
            SendMessageW(hwnd, WM_KEYDOWN, VK_PRIOR as usize, 0);
            SendMessageW(hwnd, WM_KEYDOWN, VK_NEXT as usize, 0);
            DestroyWindow(hwnd);
        }
        assert_eq!(
            *actions.borrow(),
            vec![
                Action::Toggle,
                Action::Previous,
                Action::Next,
                Action::Play,
                Action::Pause,
                Action::Stop
            ]
        );
        assert_eq!(
            Rc::strong_count(&actions),
            1,
            "window destruction must release the callback"
        );
    }
}
