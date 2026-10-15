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
