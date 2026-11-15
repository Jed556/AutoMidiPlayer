use super::{GameWindow, WindowSnapshot};
use core_foundation::{
    array::{CFArray, CFArrayRef},
    base::{CFType, TCFType},
    dictionary::CFDictionary,
    number::CFNumber,
    string::CFString,
    bundle::CFBundle,
    url::CFURL,
};
use objc2::rc::autoreleasepool;
use objc2_app_kit::{NSApplicationActivationOptions, NSRunningApplication, NSWorkspace};

/// Core Foundation handles both XML and binary Info.plist files, including
/// bundles whose displayed directory name differs from CFBundleExecutable.
pub(super) fn bundle_executable(path: &std::path::Path) -> Option<std::path::PathBuf> {
    let bundle = CFBundle::new(CFURL::from_path(path, true)?)?;
    bundle.executable_url()?.to_path().filter(|path| path.is_file())
}
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

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn application_location_resolves_the_manifest_executable_after_a_bundle_is_renamed() {
        use std::os::unix::fs::PermissionsExt;
        let root = std::env::temp_dir().join(format!("amp-bundle-{}-{}", std::process::id(),
            std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH).unwrap().as_nanos()));
        let bundle = root.join("Renamed Game.app");
        let contents = bundle.join("Contents");
        let executable = contents.join("MacOS/ActualGameLauncher");
        std::fs::create_dir_all(executable.parent().unwrap()).unwrap();
        std::fs::write(&executable, b"#!/bin/sh\nexit 0\n").unwrap();
        std::fs::set_permissions(&executable, std::fs::Permissions::from_mode(0o755)).unwrap();
        std::fs::write(contents.join("Info.plist"), br#"<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
<key>CFBundleExecutable</key><string>ActualGameLauncher</string>
<key>CFBundlePackageType</key><string>APPL</string>
<key>CFBundleIdentifier</key><string>com.jed556.amp-bundle-fixture</string>
</dict></plist>"#).unwrap();
        assert_eq!(bundle_executable(&bundle), Some(executable));
        assert_eq!(super::super::normalize_game_location(&bundle.to_string_lossy()), "actualgamelauncher");
        std::fs::remove_dir_all(root).unwrap();
    }
}
