use std::{fs, io, path::Path};
use tauri::utils::config::{AppDirectoriesOverride, Config};

/// Run before Tauri creates any windows or initializes WebView2.
pub fn prepare(config: &mut Config) -> io::Result<()> {
    let root = amp_core::paths::app_data_dir();
    let local_data = std::env::var_os("LOCALAPPDATA")
        .or_else(|| std::env::var_os("APPDATA"))
        .ok_or_else(|| io::Error::new(io::ErrorKind::NotFound, "Missing AppData directory"))?;
    migrate(&Path::new(&local_data).join(&config.identifier), &root)?;
    // Covers every window and Tauri's app path APIs, rather than just the main webview.
    config.app.app_directories_override = Some(AppDirectoriesOverride::Root(root));
    Ok(())
}

fn directory_exists(path: &Path) -> io::Result<bool> {
    match fs::symlink_metadata(path) {
        Ok(meta) => {
            use std::os::windows::fs::MetadataExt;
            // Junctions and other reparse points must not redirect a profile move.
            if !meta.is_dir() || meta.file_attributes() & 0x400 != 0 {
                return Err(io::Error::new(
                    io::ErrorKind::InvalidInput,
                    format!("Expected an unlinked directory: {}", path.display()),
                ));
            }
            Ok(true)
        }
        Err(error) if error.kind() == io::ErrorKind::NotFound => Ok(false),
        Err(error) => Err(error),
    }
}

fn migrate(legacy_root: &Path, root: &Path) -> io::Result<()> {
    if !directory_exists(legacy_root)? {
        return Ok(());
    }
    let source = legacy_root.join("EBWebView");
    if directory_exists(&source)? {
        directory_exists(root)?;
        if let Some(parent) = root.parent() {
            directory_exists(parent)?;
        }
        let destination = root.join("EBWebView");
        let destination = if directory_exists(&destination)? {
            // Never merge Chromium databases or replace a newer profile. Preserve the
            // entire old profile separately if both locations have already been used.
            let mut index = 0u32;
            loop {
                let name = if index == 0 {
                    "EBWebView.legacy.bak".to_owned()
                } else {
                    format!("EBWebView.legacy.{index}.bak")
                };
                let backup = root.join(name);
                match fs::symlink_metadata(&backup) {
                    Err(error) if error.kind() == io::ErrorKind::NotFound => break backup,
                    Err(error) => return Err(error),
                    Ok(_) => index += 1,
                }
            }
        } else {
            destination
        };
        fs::create_dir_all(root)?;
        // One rename keeps the profile intact and fails safely if another instance
        // still holds it open. Do not start a fresh profile after migration failure.
        fs::rename(&source, destination)?;
    }
    // Leave unrelated contents alone; only remove an empty identifier directory.
    match fs::remove_dir(legacy_root) {
        Ok(()) => Ok(()),
        Err(error)
            if matches!(
                error.kind(),
                io::ErrorKind::NotFound | io::ErrorKind::DirectoryNotEmpty
            ) =>
        {
            Ok(())
        }
        Err(error) => Err(error),
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::path::PathBuf;
    use std::sync::atomic::{AtomicUsize, Ordering};

    static NEXT_FIXTURE: AtomicUsize = AtomicUsize::new(0);

    struct Fixture(PathBuf);
    impl Fixture {
        fn new() -> Self {
            Self(std::env::temp_dir().join(format!(
                    "amp-webview-{}-{}-{}",
                    std::process::id(),
                    std::time::SystemTime::now()
                        .duration_since(std::time::UNIX_EPOCH)
                        .unwrap()
                        .as_nanos(),
                    NEXT_FIXTURE.fetch_add(1, Ordering::Relaxed),
                )))
        }
        fn legacy(&self) -> PathBuf {
            self.0.join("com.jed556.automidiplayer")
        }
        fn root(&self) -> PathBuf {
            self.0.join("Jed556/AutoMIDIPlayer")
        }
        fn profile(&self) -> PathBuf {
            let profile = self.legacy().join("EBWebView/Default");
            fs::create_dir_all(&profile).unwrap();
            fs::write(profile.join("Cookies"), b"old cookies").unwrap();
            fs::write(profile.join("Preferences"), b"old preferences").unwrap();
            profile
        }
    }
    impl Drop for Fixture {
        fn drop(&mut self) {
            let _ = fs::remove_dir_all(&self.0);
        }
    }

    #[test]
    fn fresh_install_does_not_create_identifier_directory() {
        let fixture = Fixture::new();
        migrate(&fixture.legacy(), &fixture.root()).unwrap();
        assert!(!fixture.0.exists());
    }

    #[test]
    fn moves_whole_profile_removes_empty_parent_and_is_repeatable() {
        let fixture = Fixture::new();
        fixture.profile();
        fs::create_dir_all(fixture.root()).unwrap();
        fs::write(fixture.root().join("settings.json"), b"settings").unwrap();
        migrate(&fixture.legacy(), &fixture.root()).unwrap();
        migrate(&fixture.legacy(), &fixture.root()).unwrap();
        assert!(!fixture.legacy().exists());
        assert_eq!(
            fs::read(fixture.root().join("EBWebView/Default/Cookies")).unwrap(),
            b"old cookies"
        );
        assert_eq!(
            fs::read(fixture.root().join("EBWebView/Default/Preferences")).unwrap(),
            b"old preferences"
        );
        assert_eq!(
            fs::read(fixture.root().join("settings.json")).unwrap(),
            b"settings"
        );
    }

    #[test]
    fn existing_profile_is_not_merged_or_overwritten() {
        let fixture = Fixture::new();
        fixture.profile();
        fs::create_dir_all(fixture.root().join("EBWebView/Default")).unwrap();
        fs::create_dir_all(fixture.root().join("EBWebView.legacy.bak")).unwrap();
        fs::write(
            fixture.root().join("EBWebView/Default/Cookies"),
            b"new cookies",
        )
        .unwrap();
        migrate(&fixture.legacy(), &fixture.root()).unwrap();
        assert!(!fixture.legacy().exists());
        assert_eq!(
            fs::read(fixture.root().join("EBWebView/Default/Cookies")).unwrap(),
            b"new cookies"
        );
        assert!(!fixture
            .root()
            .join("EBWebView/Default/Preferences")
            .exists());
        assert_eq!(
            fs::read(
                fixture
                    .root()
                    .join("EBWebView.legacy.1.bak/Default/Cookies")
            )
            .unwrap(),
            b"old cookies"
        );
    }

    #[test]
    fn unrelated_legacy_contents_are_preserved() {
        let fixture = Fixture::new();
        fixture.profile();
        fs::write(fixture.legacy().join("unrelated.txt"), b"keep").unwrap();
        migrate(&fixture.legacy(), &fixture.root()).unwrap();
        assert_eq!(
            fs::read(fixture.legacy().join("unrelated.txt")).unwrap(),
            b"keep"
        );
        assert!(!fixture.legacy().join("EBWebView").exists());
    }

    #[test]
    fn invalid_destination_keeps_source_intact() {
        let fixture = Fixture::new();
        let profile = fixture.profile();
        fs::create_dir_all(fixture.root()).unwrap();
        fs::write(fixture.root().join("EBWebView"), b"not a directory").unwrap();
        assert!(migrate(&fixture.legacy(), &fixture.root()).is_err());
        assert_eq!(fs::read(profile.join("Cookies")).unwrap(), b"old cookies");
    }

    #[test]
    fn locked_profile_is_preserved_and_can_be_retried_after_close() {
        use std::os::windows::fs::OpenOptionsExt;

        let fixture = Fixture::new();
        let profile = fixture.profile();
        let locked = fs::OpenOptions::new()
            .read(true)
            .share_mode(0)
            .open(profile.join("Cookies"))
            .unwrap();
        assert!(migrate(&fixture.legacy(), &fixture.root()).is_err());
        assert!(profile.join("Preferences").exists());
        assert!(!fixture.root().join("EBWebView").exists());
        drop(locked);
        migrate(&fixture.legacy(), &fixture.root()).unwrap();
        assert!(!fixture.legacy().exists());
        assert_eq!(
            fs::read(fixture.root().join("EBWebView/Default/Cookies")).unwrap(),
            b"old cookies"
        );
    }
}
