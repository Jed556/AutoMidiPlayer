# Renderer module boundaries

- `backend/` contains the renderer's native Tauri transport boundary. `api` is
  the only public backend entry point. Run the desktop app for data access.
- `state/` contains reactive Svelte rune stores. A `*Store.svelte.ts` file
  owns one application domain and exports a matching `*Store` singleton.
- `ui/` contains reactive UI coordinators that are not application state, such
  as the notification center and tooltip manager.
- `infrastructure/` contains non-reactive cross-cutting effects, such as
  logging and telemetry.

Components and views should consume the smallest relevant dependency directly:
`playbackStore`, `notificationCenter`, `logger`, or `api`. Stores must not
re-export other stores or backend methods as convenience aliases.

The SQLite-backed domains are the library, playlists, queue, per-song
settings, and app-wide settings (including UI preferences). All app-owned
persistence uses `amp_core::paths::app_data_dir()`, resolving to
`%LOCALAPPDATA%/AutoMidiPlayer` on Windows. The database, Games,
KeyboardLayouts, logs, and telemetry state share that root. Imported MIDI
files remain at their user-selected locations; their references live in SQLite.
There is no browser storage or demo backend. Online MIDI still requires a provider.

`LibraryFileIssues` persists scan conflicts and library exclusions. Missing files
keep their `Songs` records and scoped settings. Removing a song from All Songs
excludes its path from scans and leaves the physical MIDI untouched. Available
library, playlist, and queue queries filter these unavailable paths. The conflicts
dialog can restore an exclusion, select a duplicate copy, forget a missing record,
or explicitly delete an excluded file after confirmation. Folder scans emit
`library:scanned` with availability changes; quiet scans do not produce toasts.
