use super::{GameWindow, WindowSnapshot};
use windows_sys::Win32::{Foundation::HWND, UI::WindowsAndMessaging::*};

pub(super) fn snapshot() -> WindowSnapshot {
    unsafe extern "system" fn visit(hwnd: HWND, data: isize) -> i32 {
        if IsWindowVisible(hwnd) == 0 {
            return 1;
        }
        let snapshot = &mut *(data as *mut WindowSnapshot);
        let mut pid = 0;
        GetWindowThreadProcessId(hwnd, &mut pid);
        // GetWindowText[Length] sends window messages for this process's own
        // windows. A background scan holds the monitor lock, so a UI command
        // waiting for that lock would deadlock with those messages. Our own
        // windows cannot be game targets and must never be queried here.
        if pid == 0 || pid == std::process::id() {
            return 1;
        }
        let mut title = vec![0u16; (GetWindowTextLengthW(hwnd).max(0) + 1) as usize];
        let length = GetWindowTextW(hwnd, title.as_mut_ptr(), title.len() as i32);
        if pid != 0 && length > 0 {
            snapshot.windows.push(GameWindow {
                pid,
                title: String::from_utf16_lossy(&title[..length as usize]),
            });
        }
        1
    }
    let mut snapshot = WindowSnapshot::default();
    unsafe {
        let hwnd = GetForegroundWindow();
        let mut pid = 0;
        if !hwnd.is_null() {
            GetWindowThreadProcessId(hwnd, &mut pid);
        }
        snapshot.foreground_pid = (pid != 0).then_some(pid);
        EnumWindows(Some(visit), &mut snapshot as *mut WindowSnapshot as isize);
    }
    snapshot
}

pub fn focus_window_by_pid(target_pid: u32) -> bool {
    struct Search {
        pid: u32,
        hwnd: HWND,
    }
    unsafe extern "system" fn visit(hwnd: HWND, data: isize) -> i32 {
        let search = &mut *(data as *mut Search);
        let mut pid = 0;
        GetWindowThreadProcessId(hwnd, &mut pid);
        if pid == search.pid && IsWindowVisible(hwnd) != 0 && GetWindow(hwnd, GW_OWNER).is_null() {
            search.hwnd = hwnd;
            return 0;
        }
        1
    }
    if target_pid == 0 {
        return false;
    }
    unsafe {
        let mut search = Search {
            pid: target_pid,
            hwnd: std::ptr::null_mut(),
        };
        EnumWindows(Some(visit), &mut search as *mut Search as isize);
        if search.hwnd.is_null() {
            return false;
        }
        if IsIconic(search.hwnd) != 0 {
            ShowWindow(search.hwnd, SW_RESTORE);
        }
        SetForegroundWindow(search.hwnd) != 0
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::sync::atomic::{AtomicUsize, Ordering};

    static TITLE_REQUESTS: AtomicUsize = AtomicUsize::new(0);

    #[test]
    fn snapshot_never_sends_title_messages_to_own_windows() {
        unsafe extern "system" fn window_proc(hwnd: HWND, message: u32, wparam: usize, lparam: isize) -> isize {
            if message == WM_GETTEXT || message == WM_GETTEXTLENGTH {
                TITLE_REQUESTS.fetch_add(1, Ordering::SeqCst);
            }
            DefWindowProcW(hwnd, message, wparam, lparam)
        }

        unsafe {
            let class: Vec<u16> = "STATIC\0".encode_utf16().collect();
            let title: Vec<u16> = "Monitor regression test\0".encode_utf16().collect();
            let hwnd = CreateWindowExW(
                WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW, class.as_ptr(), title.as_ptr(),
                WS_POPUP | WS_VISIBLE, -32000, -32000, 1, 1,
                std::ptr::null_mut(), std::ptr::null_mut(), std::ptr::null_mut(), std::ptr::null(),
            );
            assert!(!hwnd.is_null());
            SetWindowLongPtrW(hwnd, GWLP_WNDPROC, window_proc as *const () as isize);
            TITLE_REQUESTS.store(0, Ordering::SeqCst);

            let windows = snapshot();
            let requests = TITLE_REQUESTS.load(Ordering::SeqCst);
            // Verify that the test window really intercepts the hazardous call.
            GetWindowTextLengthW(hwnd);
            let intercepted = TITLE_REQUESTS.load(Ordering::SeqCst);
            DestroyWindow(hwnd);

            assert_eq!(requests, 0, "snapshot must not synchronously message its UI thread");
            assert!(intercepted > requests);
            assert!(windows.windows.iter().all(|window| window.pid != std::process::id()));
        }
    }
}
