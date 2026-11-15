//! Receive Explorer's tray notifications while the app runs as Administrator.
use windows_sys::Win32::{
    Foundation::HWND,
    UI::WindowsAndMessaging::{ChangeWindowMessageFilterEx, MSGFLT_ALLOW},
};

// tray-icon 0.25.1 uses this message for mouse events on its hidden window.
// Keep this in sync when upgrading tray-icon: it is not a public constant.
const WM_USER_TRAYICON: u32 = 6002;

fn allow_notifications(hwnd: HWND) -> std::io::Result<()> {
    // SAFETY: The caller owns this live window. Allow only the tray callback,
    // which carries mouse notifications, on this window. Explorer normally runs
    // at medium integrity; our requireAdministrator manifest elevates the app.
    if unsafe {
        ChangeWindowMessageFilterEx(hwnd, WM_USER_TRAYICON, MSGFLT_ALLOW, std::ptr::null_mut())
    } == 0
    {
        return Err(std::io::Error::last_os_error());
    }
    Ok(())
}

pub(super) fn allow_shell_callbacks(tray: &tauri::tray::TrayIcon) -> tauri::Result<()> {
    // Tauri runs this closure on the window's owning thread. The tray crate
    // already allows TaskbarCreated, but does not allow its mouse callback.
    tray.with_inner_tray_icon(|inner| allow_notifications(inner.window_handle()))??;
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;
    use windows_sys::{
        w,
        Win32::UI::WindowsAndMessaging::{
            CreateWindowExW, DestroyWindow, CHANGEFILTERSTRUCT, MSGFLTINFO_ALREADYALLOWED_FORWND,
            MSGFLTINFO_NONE,
        },
    };

    struct TestWindow(HWND);

    impl TestWindow {
        fn new() -> Self {
            // SAFETY: STATIC is a system window class; no pointers are retained.
            let hwnd = unsafe {
                CreateWindowExW(
                    0,
                    w!("STATIC"),
                    std::ptr::null(),
                    0,
                    0,
                    0,
                    0,
                    0,
                    std::ptr::null_mut(),
                    std::ptr::null_mut(),
                    std::ptr::null_mut(),
                    std::ptr::null(),
                )
            };
            assert!(!hwnd.is_null(), "{}", std::io::Error::last_os_error());
            Self(hwnd)
        }
    }

    impl Drop for TestWindow {
        fn drop(&mut self) {
            // SAFETY: This test created the window on the current thread.
            unsafe { DestroyWindow(self.0) };
        }
    }

    fn allow_status(hwnd: HWND, message: u32) -> u32 {
        let mut status = CHANGEFILTERSTRUCT {
            cbSize: std::mem::size_of::<CHANGEFILTERSTRUCT>() as u32,
            ExtStatus: 0,
        };
        // SAFETY: The test owns the window and status is writable.
        assert_ne!(
            unsafe { ChangeWindowMessageFilterEx(hwnd, message, MSGFLT_ALLOW, &mut status) },
            0,
            "{}",
            std::io::Error::last_os_error()
        );
        status.ExtStatus
    }

    #[test]
    fn shell_callback_is_allowed_only_on_the_tray_window() {
        let tray = TestWindow::new();
        let other = TestWindow::new();
        allow_notifications(tray.0).unwrap();
        allow_notifications(tray.0).unwrap(); // Installing twice is safe.

        assert_eq!(
            allow_status(tray.0, WM_USER_TRAYICON),
            MSGFLTINFO_ALREADYALLOWED_FORWND
        );
        assert_eq!(allow_status(other.0, WM_USER_TRAYICON), MSGFLTINFO_NONE);
        assert_eq!(allow_status(tray.0, WM_USER_TRAYICON + 1), MSGFLTINFO_NONE);
    }
}
