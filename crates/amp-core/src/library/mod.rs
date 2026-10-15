pub mod models;
pub mod metadata;
pub mod db;
pub mod scanner;
pub mod watcher;
pub mod issues;

pub use models::{SongRecord, AlbumRecord, PlaylistRecord, SongSettingsUpdate, SongMetadataUpdate, SongDefaults};
pub use metadata::{extract_metadata, compute_file_hash, sanitize_tag, ExtractedMetadata};
pub use db::LibraryDatabase;
pub use scanner::{scan_folder, import_files, ScanSummary};
pub use watcher::DirectoryWatcher;
