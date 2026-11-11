use std::path::{Path, PathBuf};
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::mpsc::{channel, RecvTimeoutError};
use std::sync::Arc;
use std::time::{Duration, Instant};
use notify::event::{CreateKind, ModifyKind, RemoveKind};
use notify::{Config, Event, EventKind, RecommendedWatcher, RecursiveMode, Watcher};

pub enum WatcherAction {
    ScanNeeded,
    Error(String),
}

pub struct DirectoryWatcher {
    _watcher: RecommendedWatcher,
    watch_path: PathBuf,
    is_active: Arc<AtomicBool>,
}

fn needs_scan(event: &Event) -> bool {
    if event.need_rescan() {
        return true;
    }
    let has_midi_path = event.paths.iter().any(|path| {
        path.extension().and_then(|ext| ext.to_str())
            .is_some_and(|ext| ext.eq_ignore_ascii_case("mid") || ext.eq_ignore_ascii_case("midi"))
    });
    match event.kind {
        EventKind::Create(CreateKind::File) | EventKind::Remove(RemoveKind::File)
        | EventKind::Modify(ModifyKind::Data(_) | ModifyKind::Metadata(_)) => has_midi_path,
        // Folder removals and renames can contain songs without a MIDI extension themselves.
        EventKind::Create(_) | EventKind::Remove(_)
        | EventKind::Modify(ModifyKind::Name(_)) | EventKind::Any => true,
        EventKind::Modify(_) => has_midi_path || event.paths.iter().any(|path| path.is_dir()),
        _ => false,
    }
}

impl DirectoryWatcher {
    /// Watch only the source file's directory, without scanning its descendants.
    pub fn watch_soundfont_file<F>(file: &Path, on_change: F) -> Result<Self, String>
    where F: Fn() + Send + 'static,
    {
        let file = file.to_path_buf();
        let parent = file.parent().ok_or("SoundFont path has no parent")?.to_path_buf();
        Self::watch_filtered_mode(&parent, RecursiveMode::NonRecursive, move |event| {
            if matches!(event.kind, EventKind::Access(_) | EventKind::Modify(ModifyKind::Metadata(_))) { return false; }
            event.need_rescan() || event.paths.iter().any(|path| path == &file)
        }, on_change)
    }

    /// Watch through the parent so removing/recreating the SoundFonts folder is detected too.
    pub fn watch_soundfonts<F>(folder: &Path, on_change: F) -> Result<Self, String>
    where F: Fn() + Send + 'static,
    {
        let root = folder.to_path_buf();
        let parent = folder.parent().ok_or("SoundFonts folder has no parent")?;
        Self::watch_filtered(parent, move |event| {
            if event.need_rescan() { return true; }
            // Reads can update access metadata; reloading in response would create a loop.
            if matches!(event.kind, EventKind::Access(_) | EventKind::Modify(ModifyKind::Metadata(_))) { return false; }
            event.paths.iter().any(|path| {
                (path == &root && matches!(event.kind, EventKind::Create(_) | EventKind::Remove(_)
                    | EventKind::Modify(ModifyKind::Name(_)) | EventKind::Any)) || (path.starts_with(&root)
                    && path != &root
                    && (path.extension().and_then(|ext| ext.to_str())
                        .is_some_and(|ext| ext.eq_ignore_ascii_case("sf2"))
                        || matches!(event.kind, EventKind::Remove(_) | EventKind::Modify(ModifyKind::Name(_)))))
            })
        }, on_change)
    }

    /// Creates and starts watching a folder for changes to .mid and .midi files with debouncing.
    pub fn watch<F>(folder: &Path, on_change: F) -> Result<Self, String>
    where
        F: Fn() + Send + 'static,
    {
        Self::watch_filtered(folder, needs_scan, on_change)
    }

    /// Watch the config roots through their parent, so deleting/recreating a root is detected.
    pub fn watch_configs<F>(folder: &Path, on_change: F) -> Result<Self, String>
    where F: Fn() + Send + 'static,
    {
        Self::watch_configs_with_removals(folder, || {}, on_change)
    }

    /// Notify removals immediately, while retaining debounce for parsing saves and other edits.
    pub fn watch_configs_with_removals<F, R>(folder: &Path, on_removal: R, on_change: F) -> Result<Self, String>
    where F: Fn() + Send + 'static, R: Fn() + Send + 'static,
    {
        let root = folder.to_path_buf();
        Self::watch_filtered(folder, move |event| {
            if matches!(event.kind, EventKind::Access(_)) { return false; }
            let relevant = event.need_rescan() || event.paths.iter().any(|path| {
                path == &root || path.starts_with(root.join("Games"))
                    || path.starts_with(root.join("KeyboardLayouts"))
            });
            if relevant && (event.need_rescan() || matches!(event.kind,
                EventKind::Remove(_) | EventKind::Modify(ModifyKind::Name(_)))) {
                on_removal();
            }
            relevant
        }, on_change)
    }

    fn watch_filtered<F, P>(folder: &Path, needs_scan: P, on_change: F) -> Result<Self, String>
    where F: Fn() + Send + 'static, P: Fn(&Event) -> bool + Send + 'static,
    {
        Self::watch_filtered_mode(folder, RecursiveMode::Recursive, needs_scan, on_change)
    }

    fn watch_filtered_mode<F, P>(folder: &Path, mode: RecursiveMode, needs_scan: P, on_change: F) -> Result<Self, String>
    where F: Fn() + Send + 'static, P: Fn(&Event) -> bool + Send + 'static,
    {
        if !folder.exists() || !folder.is_dir() {
            return Err(format!("Cannot watch non-existent directory: {}", folder.display()));
        }

        let (tx, rx) = channel::<notify::Result<Event>>();
        let mut watcher = RecommendedWatcher::new(tx, Config::default())
            .map_err(|e| format!("Failed to create filesystem watcher: {}", e))?;

        watcher
            .watch(folder, mode)
            .map_err(|e| format!("Failed to watch folder {}: {}", folder.display(), e))?;

        let is_active = Arc::new(AtomicBool::new(true));
        let active_clone = is_active.clone();

        // Background thread with debouncing
        std::thread::spawn(move || {
            let debounce_duration = Duration::from_millis(500);
            let mut deadline: Option<Instant> = None;

            while active_clone.load(Ordering::Acquire) {
                let result = if let Some(deadline) = deadline {
                    rx.recv_timeout(deadline.saturating_duration_since(Instant::now()))
                } else {
                    rx.recv().map_err(|_| RecvTimeoutError::Disconnected)
                };
                match result {
                    Ok(Ok(event)) => {
                        if needs_scan(&event) {
                            deadline = Some(Instant::now() + debounce_duration);
                        }
                    }
                    Ok(Err(e)) => {
                        log::warn!("Watch error: {}", e);
                        deadline = Some(Instant::now() + debounce_duration);
                    }
                    Err(RecvTimeoutError::Timeout) => {
                        deadline = None;
                        if active_clone.load(Ordering::Acquire) {
                            on_change();
                        }
                    }
                    Err(RecvTimeoutError::Disconnected) => break,
                }
            }
        });

        Ok(Self {
            _watcher: watcher,
            watch_path: folder.to_path_buf(),
            is_active,
        })
    }

    pub fn path(&self) -> &Path {
        &self.watch_path
    }

    pub fn stop(&self) {
        self.is_active.store(false, Ordering::Release);
    }
}

impl Drop for DirectoryWatcher {
    fn drop(&mut self) {
        self.stop();
    }
}
