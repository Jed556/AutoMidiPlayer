use std::{
    fs, io,
    path::{Path, PathBuf},
};

/// Single root for application-owned data, shared by the desktop and core.
pub fn app_data_dir() -> PathBuf {
    data_base_dir().join("Jed556").join("AutoMIDIPlayer")
}

fn data_base_dir() -> PathBuf {
    #[cfg(windows)]
    {
        let base = std::env::var_os("LOCALAPPDATA")
            .or_else(|| std::env::var_os("APPDATA"))
            .expect("The operating system must provide an AppData directory");
        PathBuf::from(base)
    }
    #[cfg(target_os = "macos")]
    {
        PathBuf::from(std::env::var_os("HOME").expect("The operating system must provide HOME"))
            .join("Library/Application Support")
    }
    #[cfg(not(any(windows, target_os = "macos")))]
    {
        let base = std::env::var_os("XDG_DATA_HOME")
            .map(PathBuf::from)
            .filter(|path| path.is_absolute())
            .unwrap_or_else(|| {
                PathBuf::from(
                    std::env::var_os("HOME").expect("The operating system must provide HOME"),
                )
                .join(".local/share")
            });
        base
    }
}

/// Move legacy data before opening logs, SQLite, or any other application files.
/// Retrying a partial migration moves the remaining entries without overwriting data.
pub fn migrate_app_data() -> io::Result<()> {
    migrate_directory(&data_base_dir().join("AutoMidiPlayer"), &app_data_dir())
}

fn migrate_directory(source: &Path, destination: &Path) -> io::Result<()> {
    // Check the entire tree first, particularly before moving SQLite sidecar files.
    check_destination(source, destination)?;
    move_directory(source, destination)
}

fn check_destination(source: &Path, destination: &Path) -> io::Result<()> {
    let source_meta = match fs::symlink_metadata(source) {
        Ok(meta) => meta,
        Err(error) if error.kind() == io::ErrorKind::NotFound => return Ok(()),
        Err(error) => return Err(error),
    };
    let destination_meta = match fs::symlink_metadata(destination) {
        Ok(meta) => meta,
        Err(error) if error.kind() == io::ErrorKind::NotFound => return Ok(()),
        Err(error) => return Err(error),
    };
    if source_meta.file_type().is_symlink() || destination_meta.file_type().is_symlink() {
        return Err(io::Error::new(
            io::ErrorKind::InvalidInput,
            format!(
                "Cannot migrate symlinked path: {} or {}",
                source.display(),
                destination.display()
            ),
        ));
    }
    if source_meta.is_dir() != destination_meta.is_dir() {
        return Err(io::Error::new(
            io::ErrorKind::AlreadyExists,
            format!(
                "App data migration type mismatch: {} vs {}",
                destination.display(),
                source.display(),
            ),
        ));
    }
    if source_meta.is_dir() && destination_meta.is_dir() {
        for entry in fs::read_dir(source)? {
            let entry = entry?;
            check_destination(&entry.path(), &destination.join(entry.file_name()))?;
        }
    }
    Ok(())
}

fn backup_destination_path(destination_dir: &Path, file_name: &std::ffi::OsStr) -> PathBuf {
    let name = file_name.to_string_lossy();
    let candidate = destination_dir.join(format!("{name}.legacy.bak"));
    if !candidate.exists() {
        return candidate;
    }
    for i in 1..=100 {
        let candidate = destination_dir.join(format!("{name}.legacy.{i}.bak"));
        if !candidate.exists() {
            return candidate;
        }
    }
    destination_dir.join(format!("{name}.legacy.{}.bak", uuid::Uuid::new_v4()))
}

fn move_directory(source: &Path, destination: &Path) -> io::Result<()> {
    let source_meta = match fs::symlink_metadata(source) {
        Ok(meta) => meta,
        Err(error) if error.kind() == io::ErrorKind::NotFound => return Ok(()),
        Err(error) => return Err(error),
    };
    if !source_meta.is_dir() || source_meta.file_type().is_symlink() {
        return Err(io::Error::other(format!(
            "Cannot migrate data directory {}",
            source.display()
        )));
    }
    match fs::symlink_metadata(destination) {
        Err(error) if error.kind() == io::ErrorKind::NotFound => {
            if let Some(parent) = destination.parent() {
                fs::create_dir_all(parent)?;
            }
            return fs::rename(source, destination);
        }
        Err(error) => return Err(error),
        Ok(meta) if !meta.is_dir() || meta.file_type().is_symlink() => {
            return Err(io::Error::other(format!(
                "Cannot migrate into {}",
                destination.display()
            )));
        }
        Ok(_) => {}
    }

    for entry in fs::read_dir(source)? {
        let entry = entry?;
        let from = entry.path();
        let file_name = entry.file_name();
        let to = destination.join(&file_name);
        if entry.file_type()?.is_dir() {
            move_directory(&from, &to)?;
        } else {
            match fs::symlink_metadata(&to) {
                Err(error) if error.kind() == io::ErrorKind::NotFound => fs::rename(&from, &to)?,
                Err(error) => return Err(error),
                Ok(_) => {
                    // Target already exists: preserve the legacy file as a backup rather than aborting.
                    let backup_to = backup_destination_path(destination, &file_name);
                    fs::rename(&from, &backup_to)?;
                }
            }
        }
    }
    // Never recursively delete legacy data: remove only directories emptied by the moves.
    fs::remove_dir(source)
}

#[cfg(test)]
mod tests {
    use super::*;

    struct Fixture(PathBuf);
    impl Fixture {
        fn new() -> Self {
            Self(std::env::temp_dir().join(format!("amp-paths-{}", uuid::Uuid::new_v4())))
        }
        fn old(&self) -> PathBuf {
            self.0.join("AutoMidiPlayer")
        }
        fn new_path(&self) -> PathBuf {
            self.0.join("Jed556/AutoMIDIPlayer")
        }
    }
    impl Drop for Fixture {
        fn drop(&mut self) {
            let _ = fs::remove_dir_all(&self.0);
        }
    }

    #[test]
    fn root_uses_author_and_application_name() {
        assert_eq!(
            app_data_dir(),
            data_base_dir().join("Jed556").join("AutoMIDIPlayer")
        );
    }

    #[test]
    fn moves_entire_legacy_tree_and_can_run_again() {
        let fixture = Fixture::new();
        fs::create_dir_all(fixture.old().join("Games/custom")).unwrap();
        fs::write(fixture.old().join("Games/custom/config.json"), b"custom").unwrap();
        for file in [
            "AutoMidiPlayer.db",
            "AutoMidiPlayer.db-wal",
            "AutoMidiPlayer.db-shm",
            "settings.json",
        ] {
            fs::write(fixture.old().join(file), file).unwrap();
        }
        migrate_directory(&fixture.old(), &fixture.new_path()).unwrap();
        assert!(!fixture.old().exists());
        assert_eq!(
            fs::read(fixture.new_path().join("Games/custom/config.json")).unwrap(),
            b"custom"
        );
        for file in [
            "AutoMidiPlayer.db",
            "AutoMidiPlayer.db-wal",
            "AutoMidiPlayer.db-shm",
            "settings.json",
        ] {
            assert_eq!(
                fs::read_to_string(fixture.new_path().join(file)).unwrap(),
                file
            );
        }
        migrate_directory(&fixture.old(), &fixture.new_path()).unwrap();
    }

    #[test]
    fn merges_existing_directories_and_removes_only_empty_sources() {
        let fixture = Fixture::new();
        fs::create_dir_all(fixture.old().join("Games/empty")).unwrap();
        fs::create_dir_all(fixture.new_path().join("Games")).unwrap();
        fs::write(fixture.old().join("Games/old.json"), b"old").unwrap();
        fs::write(fixture.new_path().join("Games/new.json"), b"new").unwrap();
        migrate_directory(&fixture.old(), &fixture.new_path()).unwrap();
        assert!(!fixture.old().exists());
        assert_eq!(
            fs::read(fixture.new_path().join("Games/old.json")).unwrap(),
            b"old"
        );
        assert_eq!(
            fs::read(fixture.new_path().join("Games/new.json")).unwrap(),
            b"new"
        );
    }

    #[test]
    fn conflicts_preserve_both_files_and_can_be_retried() {
        let fixture = Fixture::new();
        fs::create_dir_all(fixture.old()).unwrap();
        fs::create_dir_all(fixture.new_path()).unwrap();
        fs::write(fixture.old().join("AutoMidiPlayer.db"), b"old").unwrap();
        fs::write(fixture.old().join("AutoMidiPlayer.db-shm"), b"sidecar").unwrap();
        fs::write(fixture.new_path().join("AutoMidiPlayer.db"), b"new").unwrap();
        migrate_directory(&fixture.old(), &fixture.new_path()).unwrap();
        assert!(!fixture.old().exists());
        assert_eq!(
            fs::read(fixture.new_path().join("AutoMidiPlayer.db")).unwrap(),
            b"new"
        );
        assert_eq!(
            fs::read(fixture.new_path().join("AutoMidiPlayer.db.legacy.bak")).unwrap(),
            b"old"
        );
        assert_eq!(
            fs::read(fixture.new_path().join("AutoMidiPlayer.db-shm")).unwrap(),
            b"sidecar"
        );
        // Subsequent migration runs safely without errors
        migrate_directory(&fixture.old(), &fixture.new_path()).unwrap();
    }

    #[test]
    fn fresh_install_does_not_create_legacy_or_destination_directories() {
        let fixture = Fixture::new();
        migrate_directory(&fixture.old(), &fixture.new_path()).unwrap();
        assert!(!fixture.0.exists());
    }
}
