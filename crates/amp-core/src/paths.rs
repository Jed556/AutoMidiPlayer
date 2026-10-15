use std::path::PathBuf;

/// Single root for application-owned data, shared by the desktop and core.
pub fn app_data_dir() -> PathBuf {
    #[cfg(windows)]
    {
        let base = std::env::var_os("LOCALAPPDATA")
            .or_else(|| std::env::var_os("APPDATA"))
            .expect("The operating system must provide an AppData directory");
        PathBuf::from(base).join("AutoMidiPlayer")
    }
    #[cfg(target_os = "macos")]
    {
        PathBuf::from(std::env::var_os("HOME").expect("The operating system must provide HOME"))
            .join("Library/Application Support/AutoMidiPlayer")
    }
    #[cfg(not(any(windows, target_os = "macos")))]
    {
        let base = std::env::var_os("XDG_DATA_HOME")
            .map(PathBuf::from)
            .filter(|path| path.is_absolute())
            .unwrap_or_else(|| PathBuf::from(std::env::var_os("HOME")
                .expect("The operating system must provide HOME")).join(".local/share"));
        base.join("AutoMidiPlayer")
    }
}
