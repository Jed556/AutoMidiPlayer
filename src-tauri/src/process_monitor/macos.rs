use super::{GameWindow, WindowSnapshot};
use core_foundation::{
    array::{CFArray, CFArrayRef},
    base::{CFType, TCFType},
    dictionary::CFDictionary,
    number::CFNumber,
    string::CFString,
};
use objc2::rc::autoreleasepool;
use objc2_app_kit::{NSApplicationActivationOptions, NSRunningApplication, NSWorkspace};

#[link(name = "CoreGraphics", kind = "framework")]
extern "C" {
    fn CGWindowListCopyWindowInfo(options: u32, relative_to_window: u32) -> CFArrayRef;
}

pub(super) fn snapshot() -> WindowSnapshot {
    autoreleasepool(|_| {
        let mut snapshot = WindowSnapshot {
            foreground_pid: NSWorkspace::sharedWorkspace()
                .frontmostApplication()
                .and_then(|app| u32::try_from(app.processIdentifier()).ok())
                .filter(|pid| *pid != 0),
            ..Default::default()
        };
        // All windows, including minimized ones, excluding desktop elements.
        // macOS can withhold titles without Screen Recording permission. Missing
        // titles never bypass a profile's required windowNames constraint.
        let raw = unsafe { CGWindowListCopyWindowInfo(1 << 4, 0) };
        if raw.is_null() {
            return snapshot;
        }
        let windows: CFArray<CFDictionary<CFString, CFType>> =
            unsafe { TCFType::wrap_under_create_rule(raw) };
        let pid_key = CFString::new("kCGWindowOwnerPID");
        let title_key = CFString::new("kCGWindowName");
        let layer_key = CFString::new("kCGWindowLayer");
        for window in windows.iter() {
            let layer = window
                .find(&layer_key)
                .and_then(|v| v.downcast::<CFNumber>())
                .and_then(|v| v.to_i64());
            if layer != Some(0) {
                continue;
            }
            let pid = window
                .find(&pid_key)
                .and_then(|v| v.downcast::<CFNumber>())
                .and_then(|v| v.to_i64())
                .and_then(|pid| u32::try_from(pid).ok())
                .filter(|pid| *pid != 0);
            let title = window
                .find(&title_key)
                .and_then(|v| v.downcast::<CFString>())
                .map(|v| v.to_string());
            if let (Some(pid), Some(title)) = (pid, title) {
                snapshot.windows.push(GameWindow { pid, title });
            }
        }
        snapshot
    })
}

pub fn focus_window_by_pid(pid: u32) -> bool {
    let Ok(pid) = i32::try_from(pid) else {
        return false;
    };
    if pid <= 0 {
        return false;
    }
    autoreleasepool(|_| {
        let Some(app) = NSRunningApplication::runningApplicationWithProcessIdentifier(pid) else {
            return false;
        };
        #[allow(deprecated)]
        app.activateWithOptions(NSApplicationActivationOptions::ActivateIgnoringOtherApps)
    })
}
