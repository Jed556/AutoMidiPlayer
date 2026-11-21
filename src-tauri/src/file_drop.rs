//! Accept Explorer file drops while the app runs elevated as Administrator.
use std::ffi::OsString;
use std::os::windows::ffi::OsStringExt;
use std::ptr;

use tauri::{AppHandle, Emitter, Manager};
use webview2_com::Microsoft::Web::WebView2::Win32::ICoreWebView2Controller4;
use windows::core::Interface;
use windows_sys::Win32::Foundation::{CloseHandle, HWND, LPARAM, LRESULT, POINT, WPARAM};
use windows_sys::Win32::Security::{
    GetTokenInformation, TOKEN_ELEVATION, TOKEN_QUERY, TokenElevation,
};
use windows_sys::Win32::System::Ole::RevokeDragDrop;
use windows_sys::Win32::System::Threading::{GetCurrentProcess, OpenProcessToken};
use windows_sys::Win32::UI::Shell::{
    DefSubclassProc, DragAcceptFiles, DragFinish, DragQueryFileW, DragQueryPoint, HDROP,
    RemoveWindowSubclass, SetWindowSubclass,
};
use windows_sys::Win32::UI::WindowsAndMessaging::{
    ChangeWindowMessageFilterEx, EnumChildWindows, MSGFLT_ALLOW, WM_COPYDATA, WM_DROPFILES,
    WM_NCDESTROY, WM_SHOWWINDOW,
};

const SUBCLASS_ID: usize = 0x414D5044;
const WM_COPYGLOBALDATA: u32 = 0x0049;

struct FileDropState {
    app: AppHandle,
}

#[derive(Clone, serde::Serialize)]
struct DropPosition {
    x: i32,
    y: i32,
}

#[derive(Clone, serde::Serialize)]
struct FileDropPayload {
    paths: Vec<String>,
    position: DropPosition,
}

fn allow_file_drop_messages(hwnd: HWND) -> std::io::Result<()> {
    for message in [WM_DROPFILES, WM_COPYDATA, WM_COPYGLOBALDATA] {
        // SAFETY: The caller owns this live window. Allow only shell file-drop
        // messages across UIPI when the user drags from medium-integrity Explorer.
        if unsafe { ChangeWindowMessageFilterEx(hwnd, message, MSGFLT_ALLOW, ptr::null_mut()) } == 0
        {
            return Err(std::io::Error::last_os_error());
        }
    }
    Ok(())
}

unsafe fn revoke_drop_targets(root: HWND) {
    let _ = RevokeDragDrop(root);
    unsafe extern "system" fn visit(hwnd: HWND, _lparam: LPARAM) -> i32 {
        let _ = RevokeDragDrop(hwnd);
        1
    }
    EnumChildWindows(root, Some(visit), 0);
}

unsafe fn extract_dropped_paths(hdrop: HDROP) -> Vec<String> {
    let count = DragQueryFileW(hdrop, u32::MAX, ptr::null_mut(), 0);
    let mut paths = Vec::with_capacity(count as usize);
    for index in 0..count {
        let len = DragQueryFileW(hdrop, index, ptr::null_mut(), 0);
        if len == 0 {
            continue;
        }
        let mut buf = vec![0u16; len as usize + 1];
        let copied = DragQueryFileW(hdrop, index, buf.as_mut_ptr(), len + 1) as usize;
        if copied > 0 {
            paths.push(OsString::from_wide(&buf[..copied]).to_string_lossy().into_owned());
        }
    }
    paths
}

unsafe extern "system" fn subclass(
    hwnd: HWND,
    message: u32,
    wparam: WPARAM,
    lparam: LPARAM,
    id: usize,
    data: usize,
) -> LRESULT {
    let state = &*(data as *const FileDropState);
    if message == WM_SHOWWINDOW && wparam != 0 {
        revoke_drop_targets(hwnd);
    } else if message == WM_DROPFILES {
        let hdrop = wparam as HDROP;
        let mut pt = POINT { x: 0, y: 0 };
        DragQueryPoint(hdrop, &mut pt);
        let paths = extract_dropped_paths(hdrop);
        DragFinish(hdrop);
        if !paths.is_empty() {
            let _ = state.app.emit(
                "window:file-drop",
                FileDropPayload {
                    paths,
                    position: DropPosition { x: pt.x, y: pt.y },
                },
            );
        }
        return 0;
    } else if message == WM_NCDESTROY {
        RemoveWindowSubclass(hwnd, Some(subclass), id);
        drop(Box::from_raw(data as *mut FileDropState));
        return DefSubclassProc(hwnd, message, wparam, lparam);
    }
    DefSubclassProc(hwnd, message, wparam, lparam)
}

fn is_elevated() -> bool {
    let mut token = ptr::null_mut();
    if unsafe { OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &mut token) } == 0 {
        return false;
    }
    let mut elevation = TOKEN_ELEVATION { TokenIsElevated: 0 };
    let mut returned_len = 0u32;
    let ok = unsafe {
        GetTokenInformation(
            token,
            TokenElevation,
            (&mut elevation as *mut TOKEN_ELEVATION).cast(),
            std::mem::size_of::<TOKEN_ELEVATION>() as u32,
            &mut returned_len,
        )
    };
    unsafe {
        CloseHandle(token);
    }
    ok != 0 && elevation.TokenIsElevated != 0
}

pub fn install(app: &AppHandle) -> Result<(), String> {
    if !is_elevated() {
        return Ok(());
    }

    let window = app
        .get_webview_window("main")
        .ok_or("Main window unavailable")?;
    let main_hwnd = window.hwnd().map_err(|e| e.to_string())?.0;

    allow_file_drop_messages(main_hwnd).map_err(|e| e.to_string())?;
    unsafe {
        DragAcceptFiles(main_hwnd, 1);
    }

    let state = Box::into_raw(Box::new(FileDropState { app: app.clone() }));
    if unsafe { SetWindowSubclass(main_hwnd, Some(subclass), SUBCLASS_ID, state as usize) } == 0 {
        unsafe {
            drop(Box::from_raw(state));
        }
        return Err(std::io::Error::last_os_error().to_string());
    }

    let main_hwnd_raw = main_hwnd as usize;
    window
        .with_webview(move |webview| unsafe {
            let main_hwnd = main_hwnd_raw as HWND;
            let controller = webview.controller();
            if let Ok(controller4) = controller.cast::<ICoreWebView2Controller4>() {
                let _ = controller4.SetAllowExternalDrop(false);
            }
            revoke_drop_targets(main_hwnd);
            let mut parent_hwnd = windows::Win32::Foundation::HWND::default();
            if controller.ParentWindow(&mut parent_hwnd).is_ok() {
                let parent = parent_hwnd.0 as HWND;
                if !parent.is_null() && parent != main_hwnd {
                    revoke_drop_targets(parent);
                }
            }
        })
        .map_err(|e| e.to_string())?;

    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::os::windows::ffi::OsStrExt;
    use windows_sys::w;
    use windows_sys::Win32::Foundation::GlobalFree;
    use windows_sys::Win32::System::Memory::{
        GMEM_MOVEABLE, GMEM_ZEROINIT, GlobalAlloc, GlobalLock, GlobalUnlock,
    };
    use windows_sys::Win32::UI::Shell::DROPFILES;
    use windows_sys::Win32::UI::WindowsAndMessaging::{
        CHANGEFILTERSTRUCT, CreateWindowExW, DestroyWindow, GWL_EXSTYLE, GetWindowLongPtrW,
        MSGFLTINFO_ALREADYALLOWED_FORWND, WS_EX_ACCEPTFILES,
    };

    struct TestWindow(HWND);

    impl TestWindow {
        fn new() -> Self {
            let hwnd = unsafe {
                CreateWindowExW(
                    0,
                    w!("STATIC"),
                    ptr::null(),
                    0,
                    0,
                    0,
                    0,
                    0,
                    ptr::null_mut(),
                    ptr::null_mut(),
                    ptr::null_mut(),
                    ptr::null(),
                )
            };
            assert!(!hwnd.is_null(), "{}", std::io::Error::last_os_error());
            Self(hwnd)
        }
    }

    impl Drop for TestWindow {
        fn drop(&mut self) {
            unsafe { DestroyWindow(self.0) };
        }
    }

    fn allow_status(hwnd: HWND, message: u32) -> u32 {
        let mut status = CHANGEFILTERSTRUCT {
            cbSize: std::mem::size_of::<CHANGEFILTERSTRUCT>() as u32,
            ExtStatus: 0,
        };
        assert_ne!(
            unsafe { ChangeWindowMessageFilterEx(hwnd, message, MSGFLT_ALLOW, &mut status) },
            0,
            "{}",
            std::io::Error::last_os_error()
        );
        status.ExtStatus
    }

    #[test]
    fn accept_files_style_is_set_on_window() {
        let win = TestWindow::new();
        unsafe {
            DragAcceptFiles(win.0, 1);
        }

        let ex_style = unsafe { GetWindowLongPtrW(win.0, GWL_EXSTYLE) } as u32;
        assert_ne!(ex_style & WS_EX_ACCEPTFILES, 0);
    }

    #[test]
    fn extract_dropped_paths_reads_unicode_hdrop_entries() {
        let expected = ["C:\\MIDIs\\Song One.mid", "D:\\音乐\\曲目.midi"];
        let mut wide = Vec::<u16>::new();
        for path in expected {
            wide.extend(std::ffi::OsStr::new(path).encode_wide());
            wide.push(0);
        }
        wide.push(0);

        let header_size = std::mem::size_of::<DROPFILES>();
        let total_bytes = header_size + wide.len() * std::mem::size_of::<u16>();
        let hmem = unsafe { GlobalAlloc(GMEM_MOVEABLE | GMEM_ZEROINIT, total_bytes) };
        assert!(!hmem.is_null(), "{}", std::io::Error::last_os_error());

        let locked = unsafe { GlobalLock(hmem) } as *mut u8;
        assert!(!locked.is_null(), "{}", std::io::Error::last_os_error());

        unsafe {
            ptr::write(
                locked.cast::<DROPFILES>(),
                DROPFILES {
                    pFiles: header_size as u32,
                    pt: POINT { x: 12, y: 34 },
                    fNC: 0,
                    fWide: 1,
                },
            );
            ptr::copy_nonoverlapping(
                wide.as_ptr(),
                locked.add(header_size).cast::<u16>(),
                wide.len(),
            );
            GlobalUnlock(hmem);
        }

        let extracted = unsafe { extract_dropped_paths(hmem as HDROP) };
        unsafe {
            GlobalFree(hmem);
        }
        assert_eq!(extracted, expected);
    }

    #[test]
    fn file_drop_messages_are_allowed_when_token_permits_filter_changes() {
        let win = TestWindow::new();
        match allow_file_drop_messages(win.0) {
            Ok(()) => {
                for msg in [WM_DROPFILES, WM_COPYDATA, WM_COPYGLOBALDATA] {
                    assert_eq!(allow_status(win.0, msg), MSGFLTINFO_ALREADYALLOWED_FORWND);
                }
            }
            Err(err) if err.kind() == std::io::ErrorKind::PermissionDenied => {}
            Err(err) => panic!("Unexpected ChangeWindowMessageFilterEx failure: {err}"),
        }
    }

    #[test]
    fn is_elevated_executes_without_error() {
        let _ = is_elevated();
    }
}
