use windows_sys::Win32::System::LibraryLoader::{
    SetDefaultDllDirectories, LOAD_LIBRARY_SEARCH_SYSTEM32,
};

/// Exclude the executable directory, working directory, and PATH from implicit
/// runtime DLL searches before initializing Tauri, WebView2, or audio devices.
/// Our native dependencies are statically linked or provided by Windows;
/// components such as WebView2 can still load DLLs by their explicit paths.
pub fn restrict_to_system_directory() -> std::io::Result<()> {
    // SAFETY: This process-wide policy takes only a constant flag and is set
    // before application initialization starts any worker threads.
    if unsafe { SetDefaultDllDirectories(LOAD_LIBRARY_SEARCH_SYSTEM32) } == 0 {
        return Err(std::io::Error::last_os_error());
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::{ffi::OsString, os::windows::ffi::OsStringExt, process::Command};
    use windows_sys::Win32::Foundation::FreeLibrary;
    use windows_sys::Win32::System::LibraryLoader::{GetModuleFileNameW, LoadLibraryW};

    #[test]
    fn ignores_version_dll_beside_executable_and_in_working_directory() {
        const CHILD: &str = "AMP_DLL_SEARCH_TEST_CHILD";
        if std::env::var_os(CHILD).is_some() {
            restrict_to_system_directory().unwrap();
            let name: Vec<u16> = "VERSION.dll\0".encode_utf16().collect();
            // SAFETY: The name is terminated and the output buffer is writable.
            let module = unsafe { LoadLibraryW(name.as_ptr()) };
            assert!(!module.is_null(), "{}", std::io::Error::last_os_error());
            let mut path = [0u16; 32768];
            let length = unsafe { GetModuleFileNameW(module, path.as_mut_ptr(), path.len() as u32) }
                as usize;
            unsafe { FreeLibrary(module) };
            assert!(length > 0 && length < path.len());
            let loaded = std::path::PathBuf::from(OsString::from_wide(&path[..length]));
            let local = std::env::current_exe()
                .unwrap()
                .with_file_name("VERSION.dll");
            assert_ne!(
                loaded.canonicalize().unwrap(),
                local.canonicalize().unwrap()
            );
            assert_eq!(
                loaded
                    .parent()
                    .unwrap()
                    .file_name()
                    .unwrap()
                    .to_string_lossy()
                    .to_lowercase(),
                "system32"
            );
            return;
        }

        // Run in an isolated process: DLL policy and loaded-module caching are
        // process-wide. A deliberately invalid DLL makes an unsafe load fail.
        let directory = std::env::temp_dir().join(format!("amp-dll-search-{}", std::process::id()));
        std::fs::create_dir(&directory).unwrap();
        let result = (|| {
            let executable = directory.join("dll-search-test.exe");
            std::fs::copy(std::env::current_exe().unwrap(), &executable).unwrap();
            std::fs::write(directory.join("VERSION.dll"), b"not a Windows DLL").unwrap();
            Command::new(executable)
                .args(["--exact", "windows_dll_search::tests::ignores_version_dll_beside_executable_and_in_working_directory", "--nocapture"])
                .env(CHILD, "1")
                .current_dir(&directory)
                .output()
                .unwrap()
        })();
        std::fs::remove_dir_all(&directory).unwrap();
        assert!(
            result.status.success(),
            "{}\n{}",
            String::from_utf8_lossy(&result.stdout),
            String::from_utf8_lossy(&result.stderr)
        );
    }
}
