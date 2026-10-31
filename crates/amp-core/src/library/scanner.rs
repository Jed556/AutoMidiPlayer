use std::path::{Path, PathBuf};
use std::collections::{HashMap, HashSet};
use chrono::Utc;
use rayon::prelude::*;
use walkdir::WalkDir;
use uuid::Uuid;
use serde::{Deserialize, Serialize};

use crate::library::db::LibraryDatabase;
use crate::library::metadata::extract_metadata;
use crate::library::models::{SongDefaults, SongRecord};
use crate::library::issues::{FileIssueKind, LibraryFileIssue};

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ScanSummary {
    pub total_files: usize,
    pub added: usize,
    pub updated: usize,
    pub removed: usize,
    pub skipped: usize,
    pub added_files: Vec<String>,
    pub removed_files: Vec<String>,
    pub new_conflicts: usize,
}

fn create_new_song_record(
    path_str: String,
    meta: &crate::library::metadata::ExtractedMetadata,
    defaults: &SongDefaults,
) -> SongRecord {
    let (base_key, key) = if defaults.auto_detect_base_key {
        if let Some(detected) = meta.detected_key_offset {
            (Some(detected as i32), 0)
        } else {
            (Some(defaults.default_base_key_offset), defaults.default_key_offset)
        }
    } else {
        (Some(defaults.default_base_key_offset), defaults.default_key_offset)
    };

    SongRecord {
        id: Uuid::new_v4().to_string(),
        base_key,
        key,
        path: path_str,
        file_hash: Some(meta.file_hash.clone()),
        title: Some(meta.title.clone()),
        artist: meta.artist.clone(),
        album: None,
        album_id: None,
        date_added: Some(Utc::now().to_rfc3339()),
        transpose: defaults.transpose_mode_int(),
        speed: Some(defaults.default_speed),
        bpm: Some(meta.native_bpm),
        disabled_tracks: None,
        merge_notes: Some(defaults.default_merge_notes),
        merge_milliseconds: Some(defaults.default_merge_tolerance_ms),
        hold_notes: Some(defaults.default_hold_notes),
        tap_threshold_ms: Some(defaults.default_tap_threshold_ms),
        image_path: None,
        cached_duration_ms: Some(meta.duration_ms),
        cached_native_bpm: Some(meta.native_bpm),
        auto_chord: Some(defaults.default_auto_chord),
        chord_detection_milliseconds: Some(defaults.default_chord_detection_milliseconds),
        // Timeline markers are created explicitly in Studio, independently of base-key detection.
        key_changes: None,
        enable_key_change_markers: Some(defaults.default_enable_key_change_markers),
        is_missing: false,
    }
}

/// Recursively scans a folder for MIDI files (.mid and .midi) and updates the library database.
pub fn scan_folder(
    db: &LibraryDatabase,
    folder: &Path,
    defaults: Option<&SongDefaults>,
) -> Result<ScanSummary, String> {
    if !folder.exists() || !folder.is_dir() {
        return Err(format!("Directory does not exist or is not a folder: {}", folder.display()));
    }

    // 1. Gather all .mid and .midi file paths
    // A failed traversal must not be treated as an empty folder during reconciliation.
    let walked = WalkDir::new(folder)
        .follow_links(true)
        .into_iter()
        .collect::<Result<Vec<_>, _>>()
        .map_err(|error| format!("Failed to scan {}: {}", folder.display(), error))?;
    let entries: Vec<PathBuf> = walked.into_iter()
        .filter(|e| {
            if !e.file_type().is_file() {
                return false;
            }
            if let Some(ext) = e.path().extension().and_then(|s| s.to_str()) {
                ext.eq_ignore_ascii_case("mid") || ext.eq_ignore_ascii_case("midi")
            } else {
                false
            }
        })
        .map(|e| e.into_path())
        .collect();

    let total_files = entries.len();
    let previous = db.file_states()?;
    let blocked: HashSet<_> = previous.iter().map(|issue| issue.path.clone()).collect();
    let excluded: HashSet<_> = previous.iter().filter(|issue| issue.kind == FileIssueKind::Excluded)
        .map(|issue| issue.path.clone()).collect();
    let originals = db.get_all_songs().map_err(|e| e.to_string())?;
    // Use the last recorded availability, rather than today's existence check,
    // to announce each disappearance only once.
    let before: HashMap<_, _> = originals.iter().filter(|song| !blocked.contains(&song.path))
        .map(|song| (song.id.clone(), song.clone())).collect();
    let known_paths: HashSet<_> = originals.iter().map(|song| song.path.clone()).collect();
    let mut entries = entries;
    entries.sort_by_key(|path| (!known_paths.contains(path.to_string_lossy().as_ref()), path.clone()));
    let mut canonical: HashMap<String, SongRecord> = HashMap::new();
    for song in &originals {
        if excluded.contains(&song.path) || previous.iter().any(|issue| issue.path == song.path
            && matches!(issue.kind, FileIssueKind::Duplicate | FileIssueKind::Invalid)) { continue; }
        if let Some(hash) = &song.file_hash {
            let entry = canonical.entry(hash.clone()).or_insert_with(|| song.clone());
            if entry.is_missing && !song.is_missing { *entry = song.clone(); }
        }
    }

    // 2. Extract metadata in parallel using rayon
    let extracted: Vec<(PathBuf, Result<crate::library::metadata::ExtractedMetadata, String>)> = entries
        .into_par_iter()
        .map(|path| {
            let meta = extract_metadata(&path);
            (path, meta)
        })
        .collect();

    let mut updated = 0;
    let mut skipped = 0;
    let mut issues = HashMap::new();
    let default_settings = defaults.cloned().unwrap_or_default();

    // 3. Sequential DB updates
    for (path, meta_result) in extracted {
        let path_str = path.to_string_lossy().to_string();
        if excluded.contains(&path_str) { skipped += 1; continue; }
        let meta = match meta_result {
            Ok(m) => m,
            Err(e) => {
                log::warn!("Skipping unparseable MIDI {}: {}", path.display(), e);
                canonical.retain(|_, song| song.path != path_str);
                let mut issue = LibraryFileIssue::for_path(&path, FileIssueKind::Invalid);
                issue.song_id = db.get_song_by_path(&path_str).map_err(|e| e.to_string())?.map(|song| song.id);
                issue.details = Some(e);
                issues.insert(path_str, issue);
                skipped += 1;
                continue;
            }
        };

        if let Some(existing) = canonical.get(&meta.file_hash).filter(|song| song.path != path_str
            && Path::new(&song.path).is_file()) {
            let mut issue = LibraryFileIssue::for_path(&path, FileIssueKind::Duplicate);
            issue.song_id = Some(existing.id.clone());
            issue.related_path = Some(existing.path.clone());
            issues.insert(path_str, issue);
            skipped += 1;
            continue;
        }

        // Check if path already exists in database
        if let Some(existing_by_path) = db.get_song_by_path(&path_str).map_err(|e| e.to_string())? {
            // Check if file was modified (hash mismatch)
            if existing_by_path.file_hash.as_deref() != Some(&meta.file_hash) {
                canonical.retain(|_, song| song.id != existing_by_path.id);
                let mut updated_song = existing_by_path;
                updated_song.file_hash = Some(meta.file_hash.clone());
                updated_song.cached_duration_ms = Some(meta.duration_ms);
                updated_song.cached_native_bpm = Some(meta.native_bpm);
                db.upsert_song(&updated_song).map_err(|e| e.to_string())?;
                canonical.insert(meta.file_hash, updated_song);
                updated += 1;
            } else {
                canonical.insert(meta.file_hash, existing_by_path);
                skipped += 1;
            }
            continue;
        }

        // Path was not found. Check if hash matches an existing song (file moved or renamed)
        if let Some(mut existing_by_hash) = canonical.get(&meta.file_hash).cloned()
            .filter(|song| matches!(Path::new(&song.path).try_exists(), Ok(false)))
        {
            db.clear_file_issue(&existing_by_hash.path)?;
            db.update_song_path(&existing_by_hash.id, &path_str).map_err(|e| e.to_string())?;
            existing_by_hash.path = path_str.clone();
            existing_by_hash.cached_duration_ms = Some(meta.duration_ms);
            existing_by_hash.cached_native_bpm = Some(meta.native_bpm);
            db.upsert_song(&existing_by_hash).map_err(|e| e.to_string())?;
            canonical.insert(existing_by_hash.file_hash.clone().unwrap(), existing_by_hash);
            updated += 1;
            continue;
        }

        // Completely new song
        let new_song = create_new_song_record(path_str, &meta, &default_settings);

        db.upsert_song(&new_song).map_err(|e| e.to_string())?;
        canonical.insert(meta.file_hash.clone(), new_song);
    }

    // Missing files leave the playable list but keep their records and settings
    // until the user explicitly removes them from the conflicts dialog.
    if !folder.is_dir() {
        return Err(format!("Directory disappeared during scan: {}", folder.display()));
    }
    for song in db.get_all_songs().map_err(|e| e.to_string())? {
        let path = Path::new(&song.path);
        if path.starts_with(folder) && !excluded.contains(&song.path) && matches!(path.try_exists(), Ok(false)) {
            issues.insert(song.path.clone(), LibraryFileIssue::for_song(&song, FileIssueKind::Missing));
        }
    }
    let issues: Vec<_> = issues.into_values().collect();
    let new_conflicts = issues.iter().filter(|issue| matches!(issue.kind, FileIssueKind::Invalid | FileIssueKind::Duplicate)
        && !previous.iter().any(|old| old.path == issue.path && old.kind == issue.kind)).count();
    db.replace_scan_file_issues(folder, &issues)?;
    let after = db.available_songs(db.get_all_songs().map_err(|e| e.to_string())?)?;
    let after_ids: HashSet<_> = after.iter().map(|song| song.id.as_str()).collect();
    let added_files: Vec<_> = after.iter().filter(|song| Path::new(&song.path).starts_with(folder)
        && !before.contains_key(&song.id)).map(|song| song.title.clone().unwrap_or_else(|| song.path.clone())).collect();
    let removed_files: Vec<_> = before.values().filter(|song| Path::new(&song.path).starts_with(folder)
        && !after_ids.contains(song.id.as_str())).map(|song| song.title.clone().unwrap_or_else(|| song.path.clone())).collect();

    Ok(ScanSummary {
        total_files,
        added: added_files.len(),
        updated,
        removed: removed_files.len(),
        skipped,
        added_files,
        removed_files,
        new_conflicts,
    })
}

/// Imports a specific list of file paths into the library.
pub fn import_files(
    db: &LibraryDatabase,
    paths: &[PathBuf],
    defaults: Option<&SongDefaults>,
) -> Result<Vec<SongRecord>, String> {
    let mut imported = Vec::new();
    let default_settings = defaults.cloned().unwrap_or_default();

    for path in paths {
        if !path.exists() || !path.is_file() {
            continue;
        }

        let meta = match extract_metadata(path) {
            Ok(m) => m,
            Err(e) => {
                log::warn!("Failed to extract metadata for {}: {}", path.display(), e);
                let mut issue = LibraryFileIssue::for_path(path, FileIssueKind::Invalid);
                issue.details = Some(e);
                db.set_file_issue(&issue)?;
                continue;
            }
        };

        let path_str = path.to_string_lossy().to_string();
        if let Some(existing) = db.available_songs(db.get_all_songs().map_err(|e| e.to_string())?)?
            .into_iter().find(|song| song.file_hash.as_deref() == Some(&meta.file_hash) && song.path != path_str) {
            let mut issue = LibraryFileIssue::for_path(path, FileIssueKind::Duplicate);
            issue.song_id = Some(existing.id);
            issue.related_path = Some(existing.path);
            db.set_file_issue(&issue)?;
            continue;
        }
        let excluded: HashSet<_> = db.file_states()?.into_iter().filter(|issue| issue.kind == FileIssueKind::Excluded)
            .map(|issue| issue.path).collect();

        let song = if let Some(mut existing) = db.get_song_by_path(&path_str).map_err(|e| e.to_string())? {
            existing.file_hash = Some(meta.file_hash.clone());
            existing.cached_duration_ms = Some(meta.duration_ms);
            existing.cached_native_bpm = Some(meta.native_bpm);
            existing.is_missing = false;
            db.upsert_song(&existing).map_err(|e| e.to_string())?;
            existing
        } else if let Some(mut existing_by_hash) = db.get_song_by_hash(&meta.file_hash)
            .map_err(|e| e.to_string())?
            .filter(|song| !excluded.contains(&song.path))
            .filter(|song| matches!(Path::new(&song.path).try_exists(), Ok(false)))
        {
            db.clear_file_issue(&existing_by_hash.path)?;
            db.update_song_path(&existing_by_hash.id, &path_str).map_err(|e| e.to_string())?;
            existing_by_hash.path = path_str.clone();
            existing_by_hash.is_missing = false;
            existing_by_hash
        } else {
            let new_song = create_new_song_record(path_str.clone(), &meta, &default_settings);
            db.upsert_song(&new_song).map_err(|e| e.to_string())?;
            new_song
        };

        db.clear_file_issue(&path_str)?;
        imported.push(song);
    }

    Ok(imported)
}
