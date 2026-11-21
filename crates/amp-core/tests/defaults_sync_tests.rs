use amp_core::input::defaults_sync::*;
use std::fs;
use std::path::{Path, PathBuf};

struct Profile(PathBuf);
impl Profile {
    fn new() -> Self {
        let path = std::env::temp_dir().join(format!(
            "amp-defaults-{}-{}",
            std::process::id(),
            uuid::Uuid::new_v4()
        ));
        fs::create_dir_all(&path).unwrap();
        Self(path)
    }
    fn write(&self, path: &str, value: &[u8]) {
        let path = self.0.join(path);
        fs::create_dir_all(path.parent().unwrap()).unwrap();
        fs::write(path, value).unwrap();
    }
    fn read(&self, path: &str) -> Vec<u8> {
        fs::read(self.0.join(path)).unwrap()
    }
}
impl Drop for Profile {
    fn drop(&mut self) {
        let _ = fs::remove_dir_all(&self.0);
    }
}

const LAYOUT_PATH: &str = "KeyboardLayouts/keys.json";
const OLD: &[u8] = br#"{"id":"keys","name":"Keys","keys":["q"]}"#;
const NEW: &[u8] = br#"{"id":"keys","name":"Keys","keys":["w"]}"#;
const LOCAL: &[u8] = br#"{"id":"keys","name":"My keys","keys":["a"],"custom":true}"#;
const INSTRUMENT_PATH: &str = "Games/Test/Instruments/Strings/UkuleleChords.json";
const INSTRUMENT: &[u8] = br#"{"id":"ukulele-chords","name":"Ukulele (Chords)","chordLayouts":["keys"],"chords":[{"name":"C","pitchClasses":[0,4,7]}]}"#;

fn bundled<'a>(path: &'a str, contents: &'a [u8]) -> BundledDefault<'a> {
    BundledDefault {
        path: Path::new(path),
        contents,
    }
}
fn choice(conflict: &DefaultConfigConflict, action: DefaultConfigAction) -> DefaultConfigChoice {
    DefaultConfigChoice {
        id: conflict.id.clone(),
        token: conflict.token.clone(),
        action,
    }
}

#[test]
fn first_install_and_new_release_install_missing_defaults_without_touching_local_files() {
    let profile = Profile::new();
    let old = [bundled(LAYOUT_PATH, OLD)];
    let first = sync_defaults(&profile.0, &old).unwrap();
    assert_eq!(first.added_files, [LAYOUT_PATH]);
    assert!(first.conflicts.is_empty());
    profile.write(LAYOUT_PATH, LOCAL);
    profile.write("settings.json", b"keep settings");
    let updated = [
        bundled(LAYOUT_PATH, OLD),
        bundled(INSTRUMENT_PATH, INSTRUMENT),
    ];
    let report = sync_defaults(&profile.0, &updated).unwrap();
    assert_eq!(report.added_files, [INSTRUMENT_PATH]);
    assert_eq!(
        report.conflicts.len(),
        1,
        "changed local content needs fresh review"
    );
    assert_eq!(profile.read(LAYOUT_PATH), LOCAL);
    assert_eq!(profile.read(INSTRUMENT_PATH), INSTRUMENT);
    assert_eq!(profile.read("settings.json"), b"keep settings");
    fs::remove_file(profile.0.join(INSTRUMENT_PATH)).unwrap();
    assert_eq!(
        sync_defaults(&profile.0, &updated).unwrap().added_files,
        [INSTRUMENT_PATH]
    );
}

#[test]
fn legacy_profiles_review_existing_differences_and_ignore_json_formatting() {
    let profile = Profile::new();
    profile.write(
        LAYOUT_PATH,
        b"{\n \"keys\": [\"q\"], \"name\": \"Keys\", \"id\": \"keys\"\n}",
    );
    assert!(sync_defaults(&profile.0, &[bundled(LAYOUT_PATH, OLD)])
        .unwrap()
        .conflicts
        .is_empty());
    // A pre-history installation cannot establish an old baseline from local content.
    fs::remove_file(profile.0.join("configuration-state.json")).unwrap();
    profile.write(LAYOUT_PATH, LOCAL);
    let report = sync_defaults(&profile.0, &[bundled(LAYOUT_PATH, NEW)]).unwrap();
    assert_eq!(report.conflicts.len(), 1);
    assert_eq!(report.conflicts[0].current_json.as_bytes(), LOCAL);
    assert_eq!(report.conflicts[0].bundled_json.as_bytes(), NEW);
    assert_eq!(profile.read(LAYOUT_PATH), LOCAL);
    assert_eq!(
        sync_defaults(&profile.0, &[bundled(LAYOUT_PATH, NEW)])
            .unwrap()
            .conflicts
            .len(),
        1,
        "deferring the dialog does not accept a revision"
    );
}

#[test]
fn older_app_clear_data_cannot_reuse_the_new_apps_review_decision() {
    let profile = Profile::new();
    profile.write(LAYOUT_PATH, OLD);
    let new = [bundled(LAYOUT_PATH, NEW)];
    let pending = sync_defaults(&profile.0, &new).unwrap();
    resolve_default_updates(
        &profile.0,
        &new,
        &[choice(&pending.conflicts[0], DefaultConfigAction::Replace)],
        &[],
    )
    .unwrap();
    assert!(sync_defaults(&profile.0, &new)
        .unwrap()
        .conflicts
        .is_empty());
    // An old build resets its JSON but does not know about the new history file.
    profile.write(LAYOUT_PATH, OLD);
    assert_eq!(sync_defaults(&profile.0, &new).unwrap().conflicts.len(), 1);
    assert_eq!(
        profile.read(LAYOUT_PATH),
        OLD,
        "review never overwrites without consent"
    );
}

#[test]
fn keep_is_bound_to_local_content_and_legacy_history_cannot_suppress_differences() {
    let profile = Profile::new();
    profile.write(LAYOUT_PATH, LOCAL);
    let defaults = [bundled(LAYOUT_PATH, NEW)];
    let pending = sync_defaults(&profile.0, &defaults).unwrap();
    resolve_default_updates(
        &profile.0,
        &defaults,
        &[choice(&pending.conflicts[0], DefaultConfigAction::Keep)],
        &[],
    )
    .unwrap();
    assert!(sync_defaults(&profile.0, &defaults)
        .unwrap()
        .conflicts
        .is_empty());
    profile.write(LAYOUT_PATH, OLD);
    assert_eq!(
        sync_defaults(&profile.0, &defaults)
            .unwrap()
            .conflicts
            .len(),
        1
    );
    let mut history: serde_json::Value =
        serde_json::from_slice(&profile.read("configuration-state.json")).unwrap();
    history[LAYOUT_PATH] = history[LAYOUT_PATH]["bundled"].clone();
    profile.write(
        "configuration-state.json",
        &serde_json::to_vec(&history).unwrap(),
    );
    assert_eq!(
        sync_defaults(&profile.0, &defaults)
            .unwrap()
            .conflicts
            .len(),
        1
    );
}

#[test]
fn explicit_in_app_edits_advance_only_the_local_content_already_reviewed() {
    let profile = Profile::new();
    let defaults = [bundled(LAYOUT_PATH, OLD)];
    sync_defaults(&profile.0, &defaults).unwrap();
    profile.write(LAYOUT_PATH, LOCAL);
    record_reviewed_edit(&profile.0, Path::new(LAYOUT_PATH), OLD, LOCAL).unwrap();
    assert!(sync_defaults(&profile.0, &defaults)
        .unwrap()
        .conflicts
        .is_empty());
    profile.write(LAYOUT_PATH, NEW);
    record_reviewed_edit(&profile.0, Path::new(LAYOUT_PATH), NEW, LOCAL).unwrap();
    assert_eq!(
        sync_defaults(&profile.0, &defaults)
            .unwrap()
            .conflicts
            .len(),
        1,
        "an unrelated external edit cannot inherit earlier consent"
    );
}

#[test]
fn review_state_filename_change_preserves_decisions_and_prefers_the_new_file() {
    let profile = Profile::new();
    profile.write(LAYOUT_PATH, LOCAL);
    let defaults = [bundled(LAYOUT_PATH, NEW)];
    let pending = sync_defaults(&profile.0, &defaults).unwrap();
    resolve_default_updates(
        &profile.0,
        &defaults,
        &[choice(&pending.conflicts[0], DefaultConfigAction::Keep)],
        &[],
    )
    .unwrap();
    let saved = profile.read("configuration-state.json");
    fs::rename(
        profile.0.join("configuration-state.json"),
        profile.0.join(".bundled-defaults.json"),
    )
    .unwrap();
    assert!(!sync_defaults(&profile.0, &defaults).unwrap().needs_review());
    assert_eq!(profile.read("configuration-state.json"), saved);
    assert!(!profile.0.join(".bundled-defaults.json").exists());
    profile.write(".bundled-defaults.json", b"{}");
    assert!(!sync_defaults(&profile.0, &defaults).unwrap().needs_review());
    assert_eq!(profile.read("configuration-state.json"), saved);
    assert!(!profile.0.join(".bundled-defaults.json").exists());
}

#[test]
fn keep_remembers_only_this_revision_and_replace_backs_up_the_original_bytes() {
    let profile = Profile::new();
    let old = [bundled(LAYOUT_PATH, OLD)];
    sync_defaults(&profile.0, &old).unwrap();
    profile.write(LAYOUT_PATH, LOCAL);
    let new = [bundled(LAYOUT_PATH, NEW)];
    let pending = sync_defaults(&profile.0, &new).unwrap();
    let kept = resolve_default_updates(
        &profile.0,
        &new,
        &[choice(&pending.conflicts[0], DefaultConfigAction::Keep)],
        &[],
    )
    .unwrap();
    assert!(kept.conflicts.is_empty());
    assert!(kept.backup_directory.is_none());
    assert_eq!(profile.read(LAYOUT_PATH), LOCAL);
    assert!(sync_defaults(&profile.0, &new)
        .unwrap()
        .conflicts
        .is_empty());
    let later = [bundled(LAYOUT_PATH, OLD)];
    let pending = sync_defaults(&profile.0, &later).unwrap();
    let replaced = resolve_default_updates(
        &profile.0,
        &later,
        &[choice(&pending.conflicts[0], DefaultConfigAction::Replace)],
        &[],
    )
    .unwrap();
    assert!(replaced.conflicts.is_empty());
    let backup = profile
        .0
        .join(replaced.backup_directory.unwrap())
        .join(LAYOUT_PATH);
    assert_eq!(fs::read(backup).unwrap(), LOCAL);
    assert_eq!(profile.read(LAYOUT_PATH), OLD);
}

#[test]
fn changed_files_invalidate_the_entire_consent_batch_before_any_replacement() {
    let profile = Profile::new();
    profile.write(LAYOUT_PATH, OLD);
    profile.write(INSTRUMENT_PATH, b"{");
    let defaults = [
        bundled(LAYOUT_PATH, NEW),
        bundled(INSTRUMENT_PATH, INSTRUMENT),
    ];
    let report = sync_defaults(&profile.0, &defaults).unwrap();
    let choices: Vec<_> = report
        .conflicts
        .iter()
        .map(|c| choice(c, DefaultConfigAction::Replace))
        .collect();
    profile.write(INSTRUMENT_PATH, b"edited during review");
    assert!(
        resolve_default_updates(&profile.0, &defaults, &choices, &[])
            .unwrap_err()
            .contains("changed")
    );
    assert_eq!(profile.read(LAYOUT_PATH), OLD);
    assert_eq!(profile.read(INSTRUMENT_PATH), b"edited during review");
    assert!(!profile.0.join("ConfigBackups").exists());
}

#[test]
fn renamed_files_and_layout_aliases_are_reviewed_in_place_without_duplicate_ids() {
    let profile = Profile::new();
    let renamed = "KeyboardLayouts/custom-name.json";
    profile.write(renamed, LOCAL);
    let defaults = [bundled(LAYOUT_PATH, NEW)];
    let pending = sync_defaults(&profile.0, &defaults).unwrap();
    assert_eq!(pending.conflicts[0].path, renamed);
    assert!(!profile.0.join(LAYOUT_PATH).exists());
    resolve_default_updates(
        &profile.0,
        &defaults,
        &[choice(&pending.conflicts[0], DefaultConfigAction::Replace)],
        &[],
    )
    .unwrap();
    assert_eq!(profile.read(renamed), NEW);
    assert!(sync_defaults(&profile.0, &defaults)
        .unwrap()
        .conflicts
        .is_empty());
    assert!(!profile.0.join(LAYOUT_PATH).exists());
    profile.write(
        "KeyboardLayouts/keys.json",
        br#"{"id":"other","name":"Other","keys":["a"]}"#,
    );
    assert!(sync_defaults(&profile.0, &defaults).unwrap().issues[0]
        .message
        .contains("Multiple existing files"));
}

#[test]
fn relocated_instruments_are_not_duplicated_and_other_games_do_not_conflict() {
    let profile = Profile::new();
    let moved = "Games/Test/Instruments/Custom/Ukulele.json";
    profile.write(moved, INSTRUMENT);
    profile.write("Games/Other/Instruments/Ukulele.json", INSTRUMENT);
    let report = sync_defaults(&profile.0, &[bundled(INSTRUMENT_PATH, INSTRUMENT)]).unwrap();
    assert!(report.added_files.is_empty());
    assert!(report.conflicts.is_empty());
    assert!(!profile.0.join(INSTRUMENT_PATH).exists());
}

#[test]
fn renamed_game_folders_receive_new_instruments_and_assets_beside_their_game_json() {
    let profile = Profile::new();
    let game = br#"{"id":"test","displayName":"Test"}"#;
    profile.write("Games/Renamed/game.json", game);
    let report = sync_defaults(
        &profile.0,
        &[
            bundled("Games/Test/game.json", game),
            bundled(INSTRUMENT_PATH, INSTRUMENT),
            bundled("Games/Test/icon.png", b"icon"),
        ],
    )
    .unwrap();
    assert!(report.conflicts.is_empty());
    assert_eq!(
        profile.read("Games/Renamed/Instruments/Strings/UkuleleChords.json"),
        INSTRUMENT
    );
    assert_eq!(profile.read("Games/Renamed/icon.png"), b"icon");
    assert!(!profile.0.join("Games/Test").exists());
}

#[test]
fn malformed_json_is_a_reviewable_conflict_and_unknown_decisions_are_rejected() {
    let profile = Profile::new();
    profile.write(LAYOUT_PATH, b"{ invalid json");
    let defaults = [bundled(LAYOUT_PATH, OLD)];
    let pending = sync_defaults(&profile.0, &defaults).unwrap();
    assert_eq!(pending.conflicts.len(), 1);
    let invalid = DefaultConfigChoice {
        id: "../outside.json".into(),
        token: pending.conflicts[0].token.clone(),
        action: DefaultConfigAction::Replace,
    };
    assert!(resolve_default_updates(&profile.0, &defaults, &[invalid], &[]).is_err());
    assert_eq!(profile.read(LAYOUT_PATH), b"{ invalid json");
    let duplicate = [
        choice(&pending.conflicts[0], DefaultConfigAction::Keep),
        choice(&pending.conflicts[0], DefaultConfigAction::Replace),
    ];
    assert!(
        resolve_default_updates(&profile.0, &defaults, &duplicate, &[])
            .unwrap_err()
            .contains("Duplicate")
    );
    assert_eq!(profile.read(LAYOUT_PATH), b"{ invalid json");
}

#[test]
fn bundled_paths_cannot_escape_appdata_and_assets_never_replace_local_files() {
    let profile = Profile::new();
    let bad = [bundled("../outside.json", OLD)];
    assert!(sync_defaults(&profile.0, &bad).is_err());
    profile.write("Games/Test/icon.png", b"custom icon");
    let defaults = [bundled("Games/Test/icon.png", b"bundled icon")];
    sync_defaults(&profile.0, &defaults).unwrap();
    assert_eq!(profile.read("Games/Test/icon.png"), b"custom icon");
}

fn set_time(profile: &Profile, path: &str, seconds: u64) {
    fs::File::options()
        .write(true)
        .open(profile.0.join(path))
        .unwrap()
        .set_times(
            fs::FileTimes::new()
                .set_modified(std::time::UNIX_EPOCH + std::time::Duration::from_secs(seconds)),
        )
        .unwrap();
}

#[test]
fn duplicate_id_replacement_deletes_only_older_files_and_does_not_recreate_them() {
    let profile = Profile::new();
    let newer = "KeyboardLayouts/renamed-keys.json";
    profile.write(LAYOUT_PATH, OLD);
    profile.write(newer, LOCAL);
    profile.write(
        "KeyboardLayouts/unrelated.json",
        br#"{"id":"unrelated","name":"Other","keys":["z"]}"#,
    );
    set_time(&profile, LAYOUT_PATH, 1_600_000_000);
    set_time(&profile, newer, 1_700_000_000);
    let defaults = [bundled(LAYOUT_PATH, NEW)];
    let report = sync_defaults(&profile.0, &defaults).unwrap();
    assert_eq!(report.duplicates.len(), 1);
    assert_eq!(report.duplicates[0].keep.path, newer);
    assert_eq!(report.duplicates[0].older_files[0].path, LAYOUT_PATH);
    assert!(report.needs_review());
    let duplicate = DefaultConfigChoice {
        id: report.duplicates[0].id.clone(),
        token: report.duplicates[0].token.clone(),
        action: DefaultConfigAction::Replace,
    };
    let kept = resolve_default_updates(
        &profile.0,
        &defaults,
        &[choice(&report.conflicts[0], DefaultConfigAction::Keep)],
        &[duplicate],
    )
    .unwrap();
    assert!(kept.duplicates.is_empty());
    assert!(!profile.0.join(LAYOUT_PATH).exists());
    assert_eq!(profile.read(newer), LOCAL);
    assert!(profile.0.join("KeyboardLayouts/unrelated.json").exists());
    assert_eq!(
        profile.read(&format!("{}/{LAYOUT_PATH}", kept.backup_directory.unwrap())),
        OLD
    );
    let next_launch = sync_defaults(&profile.0, &defaults).unwrap();
    assert!(!next_launch.needs_review());
    assert!(next_launch.added_files.is_empty());
    assert!(!profile.0.join(LAYOUT_PATH).exists());
}

#[test]
fn bundle_replacement_and_duplicate_cleanup_can_be_approved_together() {
    let profile = Profile::new();
    let newer = "KeyboardLayouts/newer.json";
    profile.write(LAYOUT_PATH, OLD);
    profile.write(newer, LOCAL);
    set_time(&profile, LAYOUT_PATH, 1_600_000_000);
    set_time(&profile, newer, 1_700_000_000);
    let defaults = [bundled(LAYOUT_PATH, NEW)];
    let report = sync_defaults(&profile.0, &defaults).unwrap();
    let duplicate = DefaultConfigChoice {
        id: report.duplicates[0].id.clone(),
        token: report.duplicates[0].token.clone(),
        action: DefaultConfigAction::Replace,
    };
    let updated = resolve_default_updates(
        &profile.0,
        &defaults,
        &[choice(&report.conflicts[0], DefaultConfigAction::Replace)],
        &[duplicate],
    )
    .unwrap();
    assert!(!updated.needs_review());
    assert!(!profile.0.join(LAYOUT_PATH).exists());
    assert_eq!(profile.read(newer), NEW);
    let backups = updated.backup_directory.unwrap();
    assert_eq!(profile.read(&format!("{backups}/{LAYOUT_PATH}")), OLD);
    assert_eq!(profile.read(&format!("{backups}/{newer}")), LOCAL);
}

#[test]
fn duplicate_ids_are_case_insensitive_scoped_by_game_and_preserved_without_consent() {
    let profile = Profile::new();
    profile.write(
        "Games/A/Instruments/one.json",
        br#"{"id":"STRINGS","name":"First","notes":[60]}"#,
    );
    profile.write(
        "Games/A/Instruments/two.json",
        br#"{"id":"strings","name":"Second","notes":[62]}"#,
    );
    profile.write(
        "Games/B/Instruments/one.json",
        br#"{"id":"strings","name":"Other game","notes":[64]}"#,
    );
    let report = sync_defaults(&profile.0, &[]).unwrap();
    assert_eq!(report.duplicates.len(), 1);
    let duplicate = &report.duplicates[0];
    let kept = resolve_default_updates(
        &profile.0,
        &[],
        &[],
        &[DefaultConfigChoice {
            id: duplicate.id.clone(),
            token: duplicate.token.clone(),
            action: DefaultConfigAction::Keep,
        }],
    )
    .unwrap();
    assert_eq!(
        kept.duplicates.len(),
        1,
        "unresolved duplicates remain reviewable on the next launch"
    );
    assert!(profile.0.join("Games/A/Instruments/one.json").exists());
    assert!(profile.0.join("Games/A/Instruments/two.json").exists());
    assert!(profile.0.join("Games/B/Instruments/one.json").exists());
}

#[test]
fn changing_a_duplicate_during_review_or_failing_backup_prevents_its_deletion() {
    let profile = Profile::new();
    profile.write(LAYOUT_PATH, OLD);
    profile.write("KeyboardLayouts/newer.json", LOCAL);
    set_time(&profile, LAYOUT_PATH, 1_600_000_000);
    set_time(&profile, "KeyboardLayouts/newer.json", 1_700_000_000);
    let report = sync_defaults(&profile.0, &[]).unwrap();
    let duplicate = &report.duplicates[0];
    let choices = [DefaultConfigChoice {
        id: duplicate.id.clone(),
        token: duplicate.token.clone(),
        action: DefaultConfigAction::Replace,
    }];
    profile.write(LAYOUT_PATH, NEW);
    assert!(resolve_default_updates(&profile.0, &[], &[], &choices)
        .unwrap_err()
        .contains("changed"));
    assert_eq!(profile.read(LAYOUT_PATH), NEW);
    let report = sync_defaults(&profile.0, &[]).unwrap();
    let duplicate = &report.duplicates[0];
    let choices = [DefaultConfigChoice {
        id: duplicate.id.clone(),
        token: duplicate.token.clone(),
        action: DefaultConfigAction::Replace,
    }];
    profile.write("ConfigBackups", b"a file blocks the backup directory");
    assert!(resolve_default_updates(&profile.0, &[], &[], &choices).is_err());
    assert!(profile.0.join(LAYOUT_PATH).exists());
    assert!(profile.0.join("KeyboardLayouts/newer.json").exists());
}

#[cfg(unix)]
#[test]
fn symlinked_configuration_directories_are_never_written_through() {
    let profile = Profile::new();
    let outside = Profile::new();
    std::os::unix::fs::symlink(&outside.0, profile.0.join("KeyboardLayouts")).unwrap();
    assert!(sync_defaults(&profile.0, &[bundled(LAYOUT_PATH, OLD)]).is_err());
    assert!(!outside.0.join("keys.json").exists());
}
