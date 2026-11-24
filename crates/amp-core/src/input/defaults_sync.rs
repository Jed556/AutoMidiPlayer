//! Install new bundled files and review replacements without overwriting local JSON.
use super::config_scan::ConfigIssue;
use serde::{Deserialize, Serialize};
use serde_json::Value;
use sha2::{Digest, Sha256};
use std::collections::{BTreeMap, HashSet};
use std::fs::{self, OpenOptions};
use std::io::Write;
use std::path::{Component, Path, PathBuf};
use walkdir::WalkDir;

const MANIFEST: &str = "configuration-state.json";
const LEGACY_MANIFEST: &str = ".bundled-defaults.json";

pub struct BundledDefault<'a> {
    pub path: &'a Path,
    pub contents: &'a [u8],
}

#[derive(Clone, Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct DefaultConfigConflict {
    pub id: String,
    pub path: String,
    pub kind: String,
    pub name: String,
    pub current_json: String,
    pub bundled_json: String,
    /// Binds consent to the exact file contents shown in the review.
    pub token: String,
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct DefaultConfigUpdates {
    pub added_files: Vec<String>,
    pub conflicts: Vec<DefaultConfigConflict>,
    pub duplicates: Vec<DuplicateConfig>,
    pub issues: Vec<ConfigIssue>,
    pub backup_directory: Option<String>,
}

impl DefaultConfigUpdates {
    pub fn needs_review(&self) -> bool {
        !self.conflicts.is_empty() || !self.duplicates.is_empty() || !self.issues.is_empty()
    }
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct DuplicateConfigFile {
    pub path: String,
    pub json: String,
    pub modified_ms: u64,
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct DuplicateConfig {
    pub id: String,
    pub config_id: String,
    pub kind: String,
    pub keep: DuplicateConfigFile,
    pub older_files: Vec<DuplicateConfigFile>,
    pub token: String,
}

#[derive(Clone, Copy, Debug, Deserialize, Serialize, PartialEq, Eq)]
#[serde(rename_all = "lowercase")]
pub enum DefaultConfigAction {
    Keep,
    Replace,
}

#[derive(Debug, Deserialize, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct DefaultConfigChoice {
    pub id: String,
    pub token: String,
    pub action: DefaultConfigAction,
}

// Consent only applies to the local contents and bundled revision actually reviewed.
#[derive(Serialize, Deserialize)]
#[serde(untagged)]
enum ReviewedDefault {
    Current { bundled: String, local: String },
    // Older history did not bind decisions to local contents; re-review differences.
    Legacy(String),
}
type Manifest = BTreeMap<String, ReviewedDefault>;

fn reviewed(bundled: String, local: String) -> ReviewedDefault {
    ReviewedDefault::Current { bundled, local }
}

fn local_revision(bytes: &[u8]) -> String {
    json_revision(bytes).unwrap_or_else(|_| digest(bytes))
}

fn digest(bytes: &[u8]) -> String {
    Sha256::digest(bytes)
        .iter()
        .map(|byte| format!("{byte:02x}"))
        .collect()
}

fn json_revision(bytes: &[u8]) -> Result<String, String> {
    let value: Value = serde_json::from_slice(bytes).map_err(|e| e.to_string())?;
    Ok(digest(
        &serde_json::to_vec(&value).map_err(|e| e.to_string())?,
    ))
}

fn relative(path: &Path) -> String {
    path.to_string_lossy().replace('\\', "/")
}

/// Even registered destinations may have been replaced with symlinks on disk.
fn contained_path(root: &Path, relative: &Path) -> Result<PathBuf, String> {
    if relative.as_os_str().is_empty()
        || relative
            .components()
            .any(|c| !matches!(c, Component::Normal(_)))
    {
        return Err("Invalid default configuration path".into());
    }
    let base = root.canonicalize().map_err(|e| e.to_string())?;
    let mut path = root.to_path_buf();
    for part in relative.components() {
        path.push(part);
        match fs::symlink_metadata(&path) {
            Ok(meta) => {
                if meta.file_type().is_symlink()
                    || !path
                        .canonicalize()
                        .map_err(|e| e.to_string())?
                        .starts_with(&base)
                {
                    return Err(format!(
                        "Configuration path leaves app data or uses a symlink: {}",
                        path.display()
                    ));
                }
            }
            Err(e) if e.kind() == std::io::ErrorKind::NotFound => {}
            Err(e) => return Err(e.to_string()),
        }
    }
    Ok(path)
}

fn stamp() -> Result<String, String> {
    static STAMP_SEQ: std::sync::atomic::AtomicU64 = std::sync::atomic::AtomicU64::new(0);
    Ok(format!(
        "{}-{}-{}",
        std::time::SystemTime::now()
            .duration_since(std::time::UNIX_EPOCH)
            .map_err(|e| e.to_string())?
            .as_nanos(),
        std::process::id(),
        STAMP_SEQ.fetch_add(1, std::sync::atomic::Ordering::Relaxed),
    ))
}

fn write_new(path: &Path, contents: &[u8]) -> Result<(), String> {
    if let Some(parent) = path.parent() {
        fs::create_dir_all(parent).map_err(|e| e.to_string())?;
    }
    let mut file = OpenOptions::new()
        .write(true)
        .create_new(true)
        .open(path)
        .map_err(|e| e.to_string())?;
    if let Err(e) = file.write_all(contents).and_then(|_| file.sync_all()) {
        drop(file);
        let _ = fs::remove_file(path);
        return Err(e.to_string());
    }
    Ok(())
}

fn write_atomic(path: &Path, contents: &[u8]) -> Result<(), String> {
    let temporary = path.with_file_name(format!(".default-sync-{}.tmp", stamp()?));
    write_new(&temporary, contents)?;
    let result = fs::rename(&temporary, path).map_err(|e| e.to_string());
    if result.is_err() {
        let _ = fs::remove_file(temporary);
    }
    result
}

fn read_manifest(root: &Path) -> Result<Manifest, String> {
    let path = contained_path(root, Path::new(MANIFEST))?;
    let bytes = match fs::read(path) {
        Err(error) if error.kind() == std::io::ErrorKind::NotFound => {
            fs::read(contained_path(root, Path::new(LEGACY_MANIFEST))?)
        }
        result => result,
    };
    match bytes {
        Ok(bytes) => serde_json::from_slice(&bytes)
            .map_err(|e| format!("Cannot read default configuration history: {e}")),
        Err(e) if e.kind() == std::io::ErrorKind::NotFound => Ok(Manifest::new()),
        Err(e) => Err(e.to_string()),
    }
}

fn save_manifest(root: &Path, manifest: &Manifest) -> Result<(), String> {
    let bytes = serde_json::to_vec_pretty(manifest).map_err(|e| e.to_string())?;
    let path = contained_path(root, Path::new(MANIFEST))?;
    if fs::read(&path).ok().as_deref() != Some(bytes.as_slice()) {
        write_atomic(&path, &bytes)?;
    }
    // Remove the former filename only after the new state has been saved safely.
    let legacy = contained_path(root, Path::new(LEGACY_MANIFEST))?;
    match fs::remove_file(legacy) {
        Ok(()) => {}
        Err(error) if error.kind() == std::io::ErrorKind::NotFound => {}
        Err(error) => return Err(error.to_string()),
    }
    Ok(())
}

/// Only used after an explicit full configuration reset, including to recover corrupt history.
pub fn reset_default_history(root: &Path) -> Result<(), String> {
    save_manifest(root, &Manifest::new())
}

/// An explicit in-app edit can advance an already-reviewed local baseline.
/// Do not acknowledge unrelated external edits or a pending bundled update.
pub fn record_reviewed_edit(
    root: &Path,
    bundled_path: &Path,
    before: &[u8],
    after: &[u8],
) -> Result<(), String> {
    let mut manifest = read_manifest(root)?;
    if let Some(ReviewedDefault::Current { local, .. }) = manifest.get_mut(&relative(bundled_path))
    {
        if *local == local_revision(before) {
            *local = local_revision(after);
            save_manifest(root, &manifest)?;
        }
    }
    Ok(())
}

fn kind(path: &Path) -> Option<(&'static str, String)> {
    let parts: Vec<_> = path.iter().filter_map(|s| s.to_str()).collect();
    if !path
        .extension()
        .is_some_and(|s| s.eq_ignore_ascii_case("json"))
    {
        return None;
    }
    match parts.as_slice() {
        ["KeyboardLayouts", _] => Some(("layout", "layouts".into())),
        ["Games", _, _] => Some(("game", "games".into())),
        ["Games", game, "Instruments", ..] => {
            Some(("instrument", format!("instruments/{}", game.to_lowercase())))
        }
        _ => None,
    }
}

struct Existing {
    path: PathBuf,
    document: Option<Value>,
    contents: Vec<u8>,
    modified_ms: u64,
}

fn modified_ms(path: &Path) -> u64 {
    fs::metadata(path)
        .and_then(|meta| meta.modified())
        .ok()
        .and_then(|time| time.duration_since(std::time::UNIX_EPOCH).ok())
        .map(|duration| duration.as_millis().min(u64::MAX as u128) as u64)
        .unwrap_or(0)
}

fn duplicate_configs(files: &[Existing]) -> Vec<DuplicateConfig> {
    let mut groups: BTreeMap<String, Vec<&Existing>> = BTreeMap::new();
    for file in files {
        let Some((category, scope)) = kind(&file.path) else {
            continue;
        };
        let Some(document) = &file.document else {
            continue;
        };
        let id = field(document, "id").or_else(|| {
            (category == "layout")
                .then(|| {
                    file.path
                        .file_stem()
                        .map(|stem| stem.to_string_lossy().to_lowercase())
                })
                .flatten()
        });
        if let Some(id) = id {
            groups
                .entry(format!("{category}/{scope}/{id}"))
                .or_default()
                .push(file);
        }
    }
    groups
        .into_iter()
        .filter_map(|(id, mut files)| {
            if files.len() < 2 {
                return None;
            }
            // Millisecond ties use path order deterministically. The review always names the survivor.
            files.sort_by(|a, b| {
                b.modified_ms
                    .cmp(&a.modified_ms)
                    .then_with(|| a.path.cmp(&b.path))
            });
            let config_id = files[0]
                .document
                .as_ref()
                .and_then(|doc| doc.get("id"))
                .and_then(Value::as_str)
                .map(str::to_string)
                .unwrap_or_else(|| {
                    files[0]
                        .path
                        .file_stem()
                        .unwrap()
                        .to_string_lossy()
                        .into_owned()
                });
            let token = digest(
                &serde_json::to_vec(
                    &files
                        .iter()
                        .map(|file| {
                            (
                                relative(&file.path),
                                digest(&file.contents),
                                file.modified_ms,
                            )
                        })
                        .collect::<Vec<_>>(),
                )
                .unwrap(),
            );
            let convert = |file: &Existing| DuplicateConfigFile {
                path: relative(&file.path),
                json: String::from_utf8_lossy(&file.contents).into_owned(),
                modified_ms: file.modified_ms,
            };
            Some(DuplicateConfig {
                id: format!("duplicate/{id}"),
                config_id,
                kind: kind(&files[0].path).unwrap().0.into(),
                keep: convert(files[0]),
                older_files: files[1..].iter().map(|file| convert(file)).collect(),
                token,
            })
        })
        .collect()
}

fn existing_json(root: &Path) -> Result<Vec<Existing>, String> {
    let mut files = Vec::new();
    for folder in ["Games", "KeyboardLayouts"] {
        let directory = contained_path(root, Path::new(folder))?;
        if !directory.exists() {
            continue;
        }
        for entry in WalkDir::new(directory).sort_by_file_name() {
            let entry = entry.map_err(|e| e.to_string())?;
            let path = entry.path().strip_prefix(root).map_err(|e| e.to_string())?;
            if kind(path).is_none() || entry.file_type().is_dir() {
                continue;
            }
            let full = contained_path(root, path)?;
            let bytes =
                fs::read(&full).map_err(|e| format!("Cannot read {}: {e}", full.display()))?;
            files.push(Existing {
                path: path.to_path_buf(),
                document: serde_json::from_slice(&bytes).ok(),
                contents: bytes,
                modified_ms: modified_ms(&full),
            });
        }
    }
    Ok(files)
}

fn field(value: &Value, key: &str) -> Option<String> {
    value
        .get(key)
        .and_then(Value::as_str)
        .filter(|s| !s.is_empty())
        .map(str::to_lowercase)
}

fn aliases(path: &Path, document: &Value) -> HashSet<String> {
    [
        field(document, "id"),
        path.file_stem()
            .and_then(|s| s.to_str())
            .map(str::to_lowercase),
    ]
    .into_iter()
    .flatten()
    .collect()
}

fn destination(
    file: &BundledDefault<'_>,
    document: &Value,
    files: &[Existing],
) -> Result<PathBuf, String> {
    let scope = kind(file.path).ok_or("Unsupported bundled configuration path")?;
    let mut candidates: Vec<_> = files
        .iter()
        .filter(|other| {
            if other.path == file.path {
                return true;
            }
            if kind(&other.path).as_ref() != Some(&scope) {
                return false;
            }
            let Some(value) = &other.document else {
                return false;
            };
            if scope.0 == "layout" {
                return !aliases(file.path, document).is_disjoint(&aliases(&other.path, value));
            }
            field(document, "id").is_some_and(|id| field(value, "id") == Some(id))
                || field(document, "name")
                    .or_else(|| field(document, "displayName"))
                    .is_some_and(|name| {
                        field(value, "name").or_else(|| field(value, "displayName")) == Some(name)
                    })
        })
        .collect();
    candidates.sort_by(|a, b| {
        b.modified_ms
            .cmp(&a.modified_ms)
            .then_with(|| a.path.cmp(&b.path))
    });
    match candidates.as_slice() {
        [] => Ok(file.path.to_path_buf()),
        [existing] => Ok(existing.path.clone()),
        [newest, ..] if field(document, "id").is_some_and(|id| candidates.iter()
            .all(|file| file.document.as_ref().and_then(|doc| field(doc, "id")) == Some(id.clone()))) => Ok(newest.path.clone()),
        _ => Err(format!("Multiple existing files conflict with {}. Resolve the duplicate IDs or names before updating defaults.", file.path.display())),
    }
}

fn consent_token(id: &str, path: &str, current: &[u8], bundled: &[u8]) -> String {
    digest(
        &serde_json::to_vec(&(id, path, digest(current), digest(bundled)))
            .expect("string tuple serialization"),
    )
}

/// New files are installed on every launch. Existing JSON is only proposed for review.
pub fn sync_defaults(
    root: &Path,
    bundled: &[BundledDefault<'_>],
) -> Result<DefaultConfigUpdates, String> {
    fs::create_dir_all(root).map_err(|e| e.to_string())?;
    let mut manifest = read_manifest(root)?;
    let mut existing = existing_json(root)?;
    // If a game folder was renamed, its new instruments and assets must still
    // go beside the existing game JSON, rather than into an orphaned folder.
    let game_folders: Vec<_> = bundled
        .iter()
        .filter(|file| kind(file.path).is_some_and(|(kind, _)| kind == "game"))
        .filter_map(|file| {
            let document = serde_json::from_slice::<Value>(file.contents).ok()?;
            let target = destination(file, &document, &existing).ok()?;
            let source_parent = file.path.parent()?;
            let target_parent = target.parent()?;
            (source_parent != target_parent)
                .then(|| (source_parent.to_path_buf(), target_parent.to_path_buf()))
        })
        .collect();
    let mut report = DefaultConfigUpdates {
        added_files: Vec::new(),
        conflicts: Vec::new(),
        duplicates: Vec::new(),
        issues: Vec::new(),
        backup_directory: None,
    };
    for file in bundled {
        let id = relative(file.path);
        let mapped_path = game_folders
            .iter()
            .find_map(|(source, target)| {
                file.path
                    .strip_prefix(source)
                    .ok()
                    .map(|suffix| target.join(suffix))
            })
            .unwrap_or_else(|| file.path.to_path_buf());
        let file = &BundledDefault {
            path: &mapped_path,
            contents: file.contents,
        };
        let Some((category, _)) = kind(file.path) else {
            // Icons and other bundled assets are copied only when absent.
            let path = contained_path(root, file.path)?;
            if !path.exists() {
                write_new(&path, file.contents)?;
            }
            continue;
        };
        let document: Value = serde_json::from_slice(file.contents)
            .map_err(|e| format!("Invalid bundled {id}: {e}"))?;
        let revision = json_revision(file.contents)?;
        let destination = match destination(file, &document, &existing) {
            Ok(path) => path,
            Err(message) => {
                report.issues.push(ConfigIssue { path: id, message });
                continue;
            }
        };
        let path = contained_path(root, &destination)?;
        if !path.exists() {
            write_new(&path, file.contents).map_err(|e| format!("Cannot add {id}: {e}"))?;
            existing.push(Existing {
                path: destination,
                document: Some(document),
                contents: file.contents.to_vec(),
                modified_ms: modified_ms(&path),
            });
            report.added_files.push(id.clone());
            manifest.insert(id, reviewed(revision.clone(), revision));
            continue;
        }
        let current = fs::read(&path).map_err(|e| format!("Cannot read {id}: {e}"))?;
        let local = local_revision(&current);
        if local == revision {
            manifest.insert(id, reviewed(revision.clone(), revision));
        } else if !matches!(manifest.get(&id), Some(ReviewedDefault::Current { bundled, local: previous })
            if bundled == &revision && previous == &local)
        {
            let target = relative(&destination);
            report.conflicts.push(DefaultConfigConflict {
                token: consent_token(&id, &target, &current, file.contents),
                id,
                path: target,
                kind: category.into(),
                name: document
                    .get("name")
                    .or_else(|| document.get("displayName"))
                    .and_then(Value::as_str)
                    .unwrap_or("Configuration")
                    .into(),
                current_json: String::from_utf8_lossy(&current).into_owned(),
                bundled_json: String::from_utf8_lossy(file.contents).into_owned(),
            });
        }
    }
    report.duplicates = duplicate_configs(&existing);
    save_manifest(root, &manifest)?;
    Ok(report)
}

pub fn resolve_default_updates(
    root: &Path,
    bundled: &[BundledDefault<'_>],
    choices: &[DefaultConfigChoice],
    duplicate_choices: &[DefaultConfigChoice],
) -> Result<DefaultConfigUpdates, String> {
    let report = sync_defaults(root, bundled)?;
    let mut selected = HashSet::new();
    // Validate the entire batch before replacing anything.
    for choice in choices {
        if !selected.insert(&choice.id) {
            return Err("Duplicate default configuration choice".into());
        }
        if !report
            .conflicts
            .iter()
            .any(|c| c.id == choice.id && c.token == choice.token)
        {
            return Err("Configurations changed since this review. Review the latest files before applying your choices.".into());
        }
    }
    for choice in duplicate_choices {
        if !selected.insert(&choice.id) {
            return Err("Duplicate default configuration choice".into());
        }
        if !report
            .duplicates
            .iter()
            .any(|duplicate| duplicate.id == choice.id && duplicate.token == choice.token)
        {
            return Err("Duplicate files changed since this review. Review the latest files before deleting older copies.".into());
        }
    }
    let mut manifest = read_manifest(root)?;
    let mut backup_directory: Option<String> = None;
    for choice in duplicate_choices
        .iter()
        .filter(|choice| choice.action == DefaultConfigAction::Replace)
    {
        let duplicate = report
            .duplicates
            .iter()
            .find(|duplicate| duplicate.id == choice.id)
            .unwrap();
        // Recheck every copy before deleting any member of this group.
        for file in std::iter::once(&duplicate.keep).chain(&duplicate.older_files) {
            let path = contained_path(root, Path::new(&file.path))?;
            if fs::read(&path).map_err(|e| e.to_string())? != file.json.as_bytes() {
                return Err(format!(
                    "{} changed during review. Check the duplicate files again.",
                    file.path
                ));
            }
        }
        for file in &duplicate.older_files {
            let directory = backup_directory.get_or_insert(format!("ConfigBackups/{}", stamp()?));
            let backup = contained_path(root, &Path::new(directory).join(&file.path))?;
            write_new(&backup, file.json.as_bytes())
                .map_err(|e| format!("Cannot back up {}: {e}", file.path))?;
            let path = contained_path(root, Path::new(&file.path))?;
            fs::remove_file(path)
                .map_err(|e| format!("Cannot delete older duplicate {}: {e}", file.path))?;
        }
    }
    for choice in choices {
        let conflict = report
            .conflicts
            .iter()
            .find(|c| c.id == choice.id)
            .expect("validated conflict");
        let file = bundled
            .iter()
            .find(|f| relative(f.path) == choice.id)
            .expect("registered default");
        let path = contained_path(root, Path::new(&conflict.path))?;
        let current = fs::read(&path).map_err(|e| e.to_string())?;
        if consent_token(&conflict.id, &conflict.path, &current, file.contents) != choice.token {
            return Err(format!(
                "{} changed during review. Reload the comparison before replacing it.",
                conflict.path
            ));
        }
        if choice.action == DefaultConfigAction::Replace {
            let directory = match &backup_directory {
                Some(directory) => directory.clone(),
                None => format!("ConfigBackups/{}", stamp()?),
            };
            let backup = contained_path(root, &Path::new(&directory).join(&conflict.path))?;
            write_new(&backup, &current)
                .map_err(|e| format!("Cannot back up {}: {e}", conflict.path))?;
            backup_directory = Some(directory);
            write_atomic(&path, file.contents)
                .map_err(|e| format!("Cannot replace {}: {e}", conflict.path))?;
        }
        manifest.insert(
            choice.id.clone(),
            reviewed(
                json_revision(file.contents)?,
                local_revision(if choice.action == DefaultConfigAction::Replace {
                    file.contents
                } else {
                    &current
                }),
            ),
        );
        // Persist each successful choice so a later failure never repeats an approved replacement.
        save_manifest(root, &manifest)?;
    }
    let mut remaining = sync_defaults(root, bundled)?;
    remaining.added_files = report.added_files;
    remaining.backup_directory = backup_directory;
    Ok(remaining)
}
