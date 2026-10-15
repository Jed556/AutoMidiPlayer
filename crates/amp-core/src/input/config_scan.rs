//! Validated, deterministic scans shared by startup and live config reloads.
use super::config::{resolve_game_icon, GameConfig, InstrumentConfigJson, KeyboardLayoutJson, LoadedGameBundle};
use serde::{de::DeserializeOwned, Serialize};
use std::{collections::HashMap, fs, path::{Path, PathBuf}};
use walkdir::WalkDir;

#[derive(Debug, Clone, Serialize, PartialEq, Eq)]
#[serde(rename_all = "camelCase")]
pub struct ConfigIssue {
    pub path: String,
    pub message: String,
}

fn issue(issues: &mut Vec<ConfigIssue>, path: &Path, message: impl ToString) {
    issues.push(ConfigIssue { path: path.to_string_lossy().into_owned(), message: message.to_string() });
}

fn read<T: DeserializeOwned>(path: &Path, issues: &mut Vec<ConfigIssue>) -> Option<T> {
    match fs::read_to_string(path) {
        Ok(content) => match serde_json::from_str(&content) {
            Ok(value) => Some(value),
            Err(error) => { issue(issues, path, format!("Malformed JSON: {error}")); None }
        },
        Err(error) => { issue(issues, path, format!("Cannot read file: {error}")); None }
    }
}

fn unique(value: &str, kind: &str, path: &Path, seen: &mut HashMap<String, PathBuf>, issues: &mut Vec<ConfigIssue>) -> bool {
    let key = value.trim().to_lowercase();
    if key.is_empty() {
        issue(issues, path, format!("{kind} must not be empty"));
        return false;
    }
    if let Some(first) = seen.get(&key) {
        issue(issues, path, format!("Duplicate {kind} '{value}' (also in {})", first.display()));
        return false;
    }
    seen.insert(key, path.to_path_buf());
    true
}

fn entries(path: &Path, issues: &mut Vec<ConfigIssue>) -> Vec<PathBuf> {
    if !path.exists() { return Vec::new(); }
    let mut paths = Vec::new();
    match fs::read_dir(path) {
        Ok(entries) => for entry in entries {
            match entry {
                Ok(entry) => paths.push(entry.path()),
                Err(error) => issue(issues, path, error),
            }
        },
        Err(error) => issue(issues, path, error),
    }
    paths.sort();
    paths
}

fn is_json(path: &Path) -> bool {
    path.extension().and_then(|s| s.to_str()).is_some_and(|s| s.eq_ignore_ascii_case("json"))
}

pub fn scan_layouts_checked(dir: &Path) -> (HashMap<String, KeyboardLayoutJson>, Vec<ConfigIssue>) {
    let mut issues = Vec::new();
    let mut layouts = HashMap::new();
    let mut ids = HashMap::new();
    let mut aliases: HashMap<String, PathBuf> = HashMap::new();
    for path in entries(dir, &mut issues).into_iter().filter(|p| p.is_file() && is_json(p)) {
        let Some(mut layout) = read::<KeyboardLayoutJson>(&path, &mut issues) else { continue; };
        layout.file_path = Some(path.to_string_lossy().into_owned());
        let stem = path.file_stem().and_then(|s| s.to_str()).unwrap_or("").to_lowercase();
        if layout.id.is_empty() { layout.id = stem.clone(); }
        if !unique(&layout.id, "layout ID", &path, &mut ids, &mut issues) { continue; }
        if layout.name.trim().is_empty() || layout.keys.iter().any(|key| key.trim().is_empty()) {
            issue(&mut issues, &path, "Layout name and key bindings must not be empty");
            continue;
        }
        let id = layout.id.to_lowercase();
        for alias in [&id, &stem] {
            if let Some(first) = aliases.get(alias) {
                if first != &path {
                    issue(&mut issues, &path, format!("Duplicate layout alias '{alias}' (also in {})", first.display()));
                }
            } else { aliases.insert(alias.clone(), path.clone()); }
        }
        layouts.insert(id, layout.clone());
        layouts.entry(stem).or_insert(layout);
    }
    (layouts, issues)
}

pub fn scan_games_checked(dir: &Path) -> (Vec<LoadedGameBundle>, Vec<ConfigIssue>) {
    let mut issues = Vec::new();
    let mut bundles = Vec::new();
    let mut ids = HashMap::new();
    let mut names = HashMap::new();
    for folder in entries(dir, &mut issues).into_iter().filter(|p| p.is_dir()) {
        let primary = folder.join("game.json");
        let path = if primary.exists() { primary } else {
            let Some(path) = entries(&folder, &mut issues).into_iter().find(|p| p.is_file() && is_json(p)) else { continue; };
            path
        };
        let Some(config) = read::<GameConfig>(&path, &mut issues) else { continue; };
        if !unique(&config.id, "game ID", &path, &mut ids, &mut issues)
            || !unique(&config.display_name, "game name", &path, &mut names, &mut issues) { continue; }
        let instruments_dir = folder.join("Instruments");
        let mut instruments = Vec::new();
        let mut instrument_ids = HashMap::new();
        let mut instrument_names = HashMap::new();
        if instruments_dir.exists() {
            for entry in WalkDir::new(&instruments_dir).sort_by_file_name() {
                let entry = match entry {
                    Ok(entry) => entry,
                    Err(error) => { issue(&mut issues, error.path().unwrap_or(&instruments_dir), &error); continue; }
                };
                let path = entry.path();
                if !entry.file_type().is_file() || !is_json(path) { continue; }
                let Some(mut inst) = read::<InstrumentConfigJson>(path, &mut issues) else { continue; };
                if inst.keys.as_ref().is_some_and(|rows| rows.is_empty() || rows.iter().any(|row| row.is_empty()) || inst.notes.is_empty()) {
                    issue(&mut issues, path, "Instrument keys must contain non-empty rows and at least one MIDI note");
                    continue;
                }
                if !unique(&inst.id, "instrument ID", path, &mut instrument_ids, &mut issues)
                    || !unique(&inst.name, "instrument name", path, &mut instrument_names, &mut issues) { continue; }
                if inst.notes.iter().any(|pitch| *pitch > 127) {
                    issue(&mut issues, path, "MIDI notes must be between 0 and 127");
                    continue;
                }
                let mut pitches = std::collections::HashSet::new();
                if inst.notes.iter().any(|pitch| !pitches.insert(*pitch)) {
                    issue(&mut issues, path, "Duplicate MIDI note entry");
                    continue;
                }
                let subfolder = path.strip_prefix(&instruments_dir).ok().and_then(Path::parent)
                    .filter(|p| !p.as_os_str().is_empty()).map(|p| p.to_string_lossy().into_owned());
                if inst.subfolder.is_none() { inst.subfolder = subfolder.clone(); }
                if inst.category.is_none() { inst.category = subfolder; }
                inst.file_path = Some(path.to_string_lossy().into_owned());
                instruments.push(inst);
            }
        }
        instruments.sort_by(|a, b| a.category.cmp(&b.category).then_with(|| a.name.cmp(&b.name)));
        bundles.push(LoadedGameBundle {
            icon_path: resolve_game_icon(&folder, config.icon.as_deref()),
            config, instruments, folder_path: folder.to_string_lossy().into_owned(),
            config_path: Some(path.to_string_lossy().into_owned()),
        });
    }
    bundles.sort_by(|a, b| a.config.display_name.cmp(&b.config.display_name));
    (bundles, issues)
}
