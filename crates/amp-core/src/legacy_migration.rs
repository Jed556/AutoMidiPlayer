//! One-time onboarding ownership of legacy files. Startup must never move them.
use std::{fs, io, path::Path};

const COMPLETED: &str = "legacy-migration-completed";

/// Inspect only current app data; never probe the legacy directory after setup.
pub fn is_complete() -> bool {
    is_complete_at(&crate::paths::app_data_dir())
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

/// Archive only by explicit opt-in, then permanently stop legacy discovery.
pub fn finish(remove_legacy: bool) -> io::Result<()> {
    finish_at(
        &crate::paths::legacy_data_dir(),
        &crate::paths::app_data_dir(),
        remove_legacy,
    )
}

fn finish_at(source: &Path, current: &Path, remove_legacy: bool) -> io::Result<()> {
    if is_complete_at(current) {
        return Ok(());
    }
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
    fn completed_onboarding_prevents_legacy_cleanup_without_a_marker() {
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
        assert!(old.join("user.config").is_file());
        assert!(!current.join("Legacy").exists());
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
    fn opt_in_archives_without_overwriting_active_data_and_never_repeats() {
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
        finish_at(&old, &current, true).unwrap();
        assert!(
            old.join("user.config").is_file(),
            "completed migration must not touch a recreated legacy folder"
        );
        assert_eq!(fs::read_dir(current.join("Legacy")).unwrap().count(), 1);
        fs::remove_dir_all(root).unwrap();
    }
}
