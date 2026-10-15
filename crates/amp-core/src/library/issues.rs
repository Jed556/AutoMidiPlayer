use std::collections::HashSet;
use std::path::Path;
use rusqlite::params;
use serde::{Deserialize, Serialize};

use super::{LibraryDatabase, SongRecord};

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub enum FileIssueKind { Missing, Invalid, Duplicate, Excluded }

#[derive(Debug, Clone, Copy, Deserialize)]
#[serde(rename_all = "camelCase")]
pub enum FileIssueAction { Restore, Forget, UseCopy, DeleteFile }

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct LibraryFileIssue {
    pub path: String,
    pub title: String,
    pub kind: FileIssueKind,
    pub song_id: Option<String>,
    pub related_path: Option<String>,
    pub details: Option<String>,
}

impl LibraryFileIssue {
    pub fn for_song(song: &SongRecord, kind: FileIssueKind) -> Self {
        Self { path: song.path.clone(), title: song.title.clone().unwrap_or_default(), kind,
            song_id: Some(song.id.clone()), related_path: None, details: None }
    }

    pub fn for_path(path: &Path, kind: FileIssueKind) -> Self {
        Self { path: path.to_string_lossy().into_owned(),
            title: path.file_stem().unwrap_or_default().to_string_lossy().into_owned(),
            kind, song_id: None, related_path: None, details: None }
    }
}

impl LibraryDatabase {
    pub fn file_states(&self) -> Result<Vec<LibraryFileIssue>, String> {
        let conn = self.conn.lock().unwrap();
        let mut stmt = conn.prepare("SELECT Data FROM LibraryFileIssues ORDER BY Path").map_err(|e| e.to_string())?;
        let rows = stmt.query_map([], |row| row.get::<_, String>(0)).map_err(|e| e.to_string())?;
        rows.map(|row| serde_json::from_str(&row.map_err(|e| e.to_string())?).map_err(|e| e.to_string())).collect()
    }

    pub fn get_file_issues(&self) -> Result<Vec<LibraryFileIssue>, String> {
        let states = self.file_states()?;
        let known: HashSet<_> = states.iter().map(|issue| issue.path.clone()).collect();
        let songs = self.get_all_songs().map_err(|e| e.to_string())?;
        let mut issues: Vec<_> = states.into_iter().filter_map(|issue| {
            let exists = Path::new(&issue.path).is_file();
            if exists { return (issue.kind != FileIssueKind::Missing).then_some(issue); }
            if issue.kind == FileIssueKind::Excluded { return None; }
            songs.iter().find(|song| song.path == issue.path)
                .map(|song| LibraryFileIssue::for_song(song, FileIssueKind::Missing))
        }).collect();
        for song in songs {
            if song.is_missing && !known.contains(&song.path) {
                issues.push(LibraryFileIssue::for_song(&song, FileIssueKind::Missing));
            }
        }
        Ok(issues)
    }

    pub fn available_songs(&self, songs: Vec<SongRecord>) -> Result<Vec<SongRecord>, String> {
        let blocked: HashSet<_> = self.file_states()?.into_iter()
            .filter(|issue| issue.kind != FileIssueKind::Missing || !Path::new(&issue.path).is_file())
            .map(|issue| issue.path).collect();
        Ok(songs.into_iter().filter(|song| !song.is_missing && !blocked.contains(&song.path)).collect())
    }

    pub fn set_file_issue(&self, issue: &LibraryFileIssue) -> Result<(), String> {
        let data = serde_json::to_string(issue).map_err(|e| e.to_string())?;
        self.conn.lock().unwrap().execute("INSERT INTO LibraryFileIssues (Path, Data) VALUES (?1, ?2)
            ON CONFLICT(Path) DO UPDATE SET Data = excluded.Data", params![issue.path, data])
            .map_err(|e| e.to_string())?;
        Ok(())
    }

    pub fn clear_file_issue(&self, path: &str) -> Result<(), String> {
        self.conn.lock().unwrap().execute("DELETE FROM LibraryFileIssues WHERE Path = ?1", [path])
            .map_err(|e| e.to_string())?;
        Ok(())
    }

    pub fn replace_scan_file_issues(&self, root: &Path, issues: &[LibraryFileIssue]) -> Result<(), String> {
        let previous = self.file_states()?;
        let mut conn = self.conn.lock().unwrap();
        let tx = conn.transaction().map_err(|e| e.to_string())?;
        for issue in previous {
            if issue.kind != FileIssueKind::Excluded && Path::new(&issue.path).starts_with(root) {
                tx.execute("DELETE FROM LibraryFileIssues WHERE Path = ?1", [issue.path]).map_err(|e| e.to_string())?;
            }
        }
        for issue in issues {
            let data = serde_json::to_string(issue).map_err(|e| e.to_string())?;
            tx.execute("INSERT INTO LibraryFileIssues (Path, Data) VALUES (?1, ?2)
                ON CONFLICT(Path) DO UPDATE SET Data = excluded.Data", params![issue.path, data])
                .map_err(|e| e.to_string())?;
        }
        tx.commit().map_err(|e| e.to_string())
    }

    /// Hide songs without deleting their files, metadata, or scoped playback settings.
    pub fn exclude_songs(&self, ids: &[String]) -> Result<(), String> {
        let songs: Vec<_> = ids.iter().map(|id| self.get_song_by_id(id).map_err(|e| e.to_string()))
            .collect::<Result<Vec<_>, _>>()?.into_iter().flatten().collect();
        let mut conn = self.conn.lock().unwrap();
        let tx = conn.transaction().map_err(|e| e.to_string())?;
        for song in songs {
            let issue = LibraryFileIssue::for_song(&song, FileIssueKind::Excluded);
            let data = serde_json::to_string(&issue).map_err(|e| e.to_string())?;
            tx.execute("INSERT INTO LibraryFileIssues (Path, Data) VALUES (?1, ?2)
                ON CONFLICT(Path) DO UPDATE SET Data = excluded.Data", params![song.path, data])
                .map_err(|e| e.to_string())?;
            tx.execute("DELETE FROM Queue WHERE SongId = ?1", [&song.id]).map_err(|e| e.to_string())?;
        }
        tx.commit().map_err(|e| e.to_string())
    }
}

pub fn resolve_file_issue(db: &LibraryDatabase, path: &str, action: FileIssueAction,
    defaults: Option<&super::SongDefaults>) -> Result<(), String> {
    let issue = db.get_file_issues()?.into_iter().find(|issue| issue.path == path)
        .ok_or("This file conflict has already changed. Refresh the list and try again.")?;
    match (action, issue.kind) {
        (FileIssueAction::Restore, FileIssueKind::Excluded | FileIssueKind::Invalid) => {
            let imported = super::import_files(db, &[path.into()], defaults)?;
            if imported.is_empty() { return Err("This file could not be restored. Check its file conflict details.".into()); }
        }
        (FileIssueAction::Forget, FileIssueKind::Missing) => {
            if let Some(id) = &issue.song_id { db.delete_song(id).map_err(|e| e.to_string())?; }
            db.clear_file_issue(path)?;
        }
        (FileIssueAction::Forget, FileIssueKind::Invalid) => {
            let mut excluded = issue;
            excluded.kind = FileIssueKind::Excluded;
            db.set_file_issue(&excluded)?;
        }
        (FileIssueAction::UseCopy, FileIssueKind::Duplicate) => {
            let metadata = super::extract_metadata(Path::new(path))?;
            let id = issue.song_id.as_deref().ok_or("The original song is no longer available.")?;
            let song = db.get_song_by_id(id).map_err(|e| e.to_string())?.ok_or("The original song is no longer available.")?;
            if song.file_hash.as_deref() != Some(&metadata.file_hash) {
                return Err("This file has changed since the scan. Scan the folder again before choosing a copy.".into());
            }
            if let Some(other) = db.get_song_by_path(path).map_err(|e| e.to_string())? {
                if other.id != id { db.delete_song(&other.id).map_err(|e| e.to_string())?; }
            }
            db.update_song_path(id, path).map_err(|e| e.to_string())?;
            db.clear_file_issue(path)?;
            for mut duplicate in db.file_states()?.into_iter().filter(|entry| entry.kind == FileIssueKind::Duplicate
                && entry.song_id.as_deref() == Some(id)) {
                duplicate.related_path = Some(path.into());
                db.set_file_issue(&duplicate)?;
            }
            if Path::new(&song.path).is_file() && song.path != path {
                let mut alternate = LibraryFileIssue::for_song(&song, FileIssueKind::Duplicate);
                alternate.related_path = Some(path.into());
                db.set_file_issue(&alternate)?;
            }
        }
        (FileIssueAction::DeleteFile, FileIssueKind::Excluded) => {
            // Only a file already listed in the conflicts dialog can be deleted.
            // The UI asks for explicit confirmation separately from library removal.
            std::fs::remove_file(path).map_err(|e| e.to_string())?;
            if let Some(song) = db.get_song_by_path(path).map_err(|e| e.to_string())? {
                db.delete_song(&song.id).map_err(|e| e.to_string())?;
            }
            db.clear_file_issue(path)?;
        }
        _ => return Err("This action is not available for this file conflict.".into()),
    }
    Ok(())
}
