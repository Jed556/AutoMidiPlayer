//! Explicit onboarding ownership of legacy files. Startup must never move them.
use std::{fs, io, path::Path};

const COMPLETED: &str = "legacy-migration-completed";

/// Inspect only current app data to decide whether automatic imports are complete.
pub fn is_complete() -> bool {
    is_complete_at(&crate::paths::app_data_dir())
}

/// Clearing current data must not automatically reimport an old installation.
pub fn prevent_reimport() -> io::Result<()> {
    let root = crate::paths::app_data_dir();
    fs::create_dir_all(&root)?;
    fs::write(root.join(COMPLETED), b"complete")
}

fn is_complete_at(root: &Path) -> bool {
    root.join(COMPLETED).is_file()
        || fs::read(root.join("settings.json"))
            .ok()
            .and_then(|bytes| serde_json::from_slice::<serde_json::Value>(&bytes).ok())
            .and_then(|settings| {
                settings
                    .get("onboardingCompleted")
                    .and_then(serde_json::Value::as_bool)
            })
            == Some(true)
}

/// Archive only by explicit opt-in, then suppress automatic legacy imports.
pub fn finish(remove_legacy: bool) -> io::Result<()> {
    finish_at(
        &crate::paths::legacy_data_dir(),
        &crate::paths::app_data_dir(),
        remove_legacy,
    )
}

fn finish_at(source: &Path, current: &Path, remove_legacy: bool) -> io::Result<()> {
    fs::create_dir_all(current)?;
    if remove_legacy {
        match fs::symlink_metadata(source) {
            Err(error) if error.kind() == io::ErrorKind::NotFound => {}
            Err(error) => return Err(error),
            Ok(meta) => {
                if !meta.is_dir() || meta.file_type().is_symlink() {
                    return Err(io::Error::other("Legacy data must be a regular directory"));
                }
                let mut archive = current.join("Legacy");
                if let Ok(meta) = fs::symlink_metadata(&archive) {
                    if !meta.is_dir() || meta.file_type().is_symlink() {
                        return Err(io::Error::other(
                            "Legacy archive must be a regular directory",
                        ));
                    }
                    archive = archive.join(format!("archive-{}", uuid::Uuid::new_v4()));
                }
                // Atomic move preserves all bytes and sidecars before removing the old
                // location. Failure leaves the original intact; no recursive deletion.
                fs::rename(source, archive)?;
            }
        }
    }
    fs::write(current.join(COMPLETED), b"complete")
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn completed_onboarding_still_allows_explicit_archiving() {
        let root = std::env::temp_dir().join(format!("amp-legacy-{}", uuid::Uuid::new_v4()));
        let old = root.join("old");
        let current = root.join("current");
        fs::create_dir_all(&old).unwrap();
        fs::create_dir_all(&current).unwrap();
        fs::write(
            current.join("settings.json"),
            br#"{"onboardingCompleted":true}"#,
        )
        .unwrap();
        fs::write(old.join("user.config"), b"original").unwrap();
        finish_at(&old, &current, true).unwrap();
        assert!(!old.exists());
        assert_eq!(fs::read(current.join("Legacy/user.config")).unwrap(), b"original");
        assert!(is_complete_at(&current));
        fs::remove_dir_all(root).unwrap();
    }

    #[test]
    fn opt_out_leaves_original_and_records_completion() {
        let root = std::env::temp_dir().join(format!("amp-legacy-{}", uuid::Uuid::new_v4()));
        let old = root.join("old");
        let current = root.join("current");
        fs::create_dir_all(&old).unwrap();
        fs::write(old.join("user.config"), b"original").unwrap();
        finish_at(&old, &current, false).unwrap();
        assert_eq!(fs::read(old.join("user.config")).unwrap(), b"original");
        assert!(!current.join("Legacy").exists());
        assert!(current.join(COMPLETED).is_file());
        fs::remove_dir_all(root).unwrap();
    }

    #[test]
    fn repeated_explicit_opt_in_archives_without_overwriting_active_or_archived_data() {
        let root = std::env::temp_dir().join(format!("amp-legacy-{}", uuid::Uuid::new_v4()));
        let old = root.join("old");
        let current = root.join("current");
        fs::create_dir_all(&old).unwrap();
        fs::create_dir_all(&current).unwrap();
        fs::write(old.join("AutoMidiPlayer.db"), b"legacy").unwrap();
        fs::write(current.join("AutoMidiPlayer.db"), b"active").unwrap();
        finish_at(&old, &current, true).unwrap();
        assert!(!old.exists());
        assert_eq!(
            fs::read(current.join("Legacy/AutoMidiPlayer.db")).unwrap(),
            b"legacy"
        );
        assert_eq!(
            fs::read(current.join("AutoMidiPlayer.db")).unwrap(),
            b"active"
        );
        fs::create_dir_all(&old).unwrap();
        fs::write(old.join("user.config"), b"another").unwrap();
        // Starting fresh without cleanup still leaves a recreated legacy folder intact.
        finish_at(&old, &current, false).unwrap();
        assert!(old.join("user.config").is_file());
        finish_at(&old, &current, true).unwrap();
        assert!(!old.exists());
        let archive = fs::read_dir(current.join("Legacy")).unwrap()
            .map(|entry| entry.unwrap().path()).find(|path| path.is_dir()).unwrap();
        assert_eq!(fs::read(archive.join("user.config")).unwrap(), b"another");
        assert_eq!(fs::read(current.join("Legacy/AutoMidiPlayer.db")).unwrap(), b"legacy");
        assert_eq!(fs::read(current.join("AutoMidiPlayer.db")).unwrap(), b"active");
        finish_at(&old, &current, true).unwrap();
        assert_eq!(fs::read_dir(current.join("Legacy")).unwrap().count(), 2);
        fs::remove_dir_all(root).unwrap();
    }
}
