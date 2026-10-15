import type { AppWindowApi, PedalState, SongSettingsContext, StorageUsage } from '$shared/types/ipc.js';
import type { GameStatus, GameConfigState, ConfigIssue, KeyboardLayoutConfig } from '$shared/types/games.js';
import type { MidiFile } from '$shared/types/midi.js';
import type { OnlineMidiItem } from '$shared/types/onlineMidi.js';
import type { PlaybackStatus } from '$shared/types/playback.js';
import type { AppSettings } from '$shared/types/settings.js';
import type { UpdateProgress, PreparedUpdate } from '$shared/types/update.js';
import { logger } from '../infrastructure/logger.js';
import { telemetry } from '../infrastructure/telemetry.js';
import {
  loadedBundleToGameInfo,
  loadedMidiToMidiFile,
  songRecordToMidiFile,
  type RawLoadedBundle,
  type RawLoadedMidiFile,
  type RawSongRecord,
} from './mappers.js';
import { invokeTauri, isTauriEnvironment, listenTauri, runTauriCommand } from './runtime.js';

function serializeSongSettings(song: MidiFile) {
  const transpose = song.transposeMode === 'Ignore'
    ? 0
    : song.transposeMode === 'Up'
      ? 2
      : song.transposeMode === 'Down'
        ? 3
        : 1;
  const keyChanges = song.keyMarkers.length
    ? JSON.stringify(song.keyMarkers.map((marker) => ({
      TimeMs: marker.timeMs ?? Math.round((marker.time ?? 0) * 1000),
      KeyOffset: marker.keyOffset,
      Transpose: marker.transpose ?? null,
      Label: marker.label || marker.noteDisplay || marker.key,
    })))
    : null;

  return {
    key: song.keyOffset,
    baseKey: song.baseKey === null ? null : song.baseKeyOffset,
    transpose,
    disabledTracks: song.tracks.filter((track) => track.isMuted).map((track) => track.index).join(',') || null,
    keyChanges,
    speed: song.speed,
    holdNotes: song.holdNotes,
    tapThresholdMs: song.tapThresholdMs ?? 0,
    autoChord: song.autoChord ?? false,
    enableKeyChangeMarkers: song.enableKeyChangeMarkers ?? true,
  };
}

/** The renderer's sole, native Tauri backend boundary. */
export function createAppApi(): AppWindowApi {
  let songSettingsContext: SongSettingsContext | null = null;
  const updateCommand = async (command: string, onProgress: (progress: UpdateProgress) => void) => {
    const { Channel } = await import('@tauri-apps/api/core');
    const channel = new Channel<UpdateProgress>();
    channel.onmessage = onProgress;
    await invokeTauri<void>('Updates', command, { onProgress: channel });
  };
  const getSettings = () => invokeTauri<Partial<AppSettings>>('Settings', 'get_app_settings', undefined);
  let settingsWrites: Promise<unknown> = Promise.resolve();
  const enqueueSettingsWrite = (write: () => Promise<boolean>) => {
    const result = settingsWrites.then(write);
    settingsWrites = result.catch(() => {});
    return result;
  };
  const updateSettings = (settings: Partial<AppSettings>) => {
    const snapshot = JSON.parse(JSON.stringify(settings)) as Partial<AppSettings>;
    return enqueueSettingsWrite(() =>
      invokeTauri<boolean>('Settings', 'update_app_settings', { settings: snapshot }));
  };
  const getSongs = () => invokeTauri<RawSongRecord[]>('Songs', 'get_library_songs', { context: songSettingsContext })
    .then((songs) => songs.map(songRecordToMidiFile));
  const getConfigs = async (reload: boolean | 'reset' = false): Promise<GameConfigState> => {
    const { convertFileSrc } = await import('@tauri-apps/api/core');
    const [snapshot, settings] = await Promise.all([
      invokeTauri<{ games: RawLoadedBundle[]; layouts: Record<string, KeyboardLayoutConfig>; issues: ConfigIssue[] }>(
        'Games', reload === 'reset' ? 'reset_game_configs' : reload ? 'reload_game_configs' : 'get_game_configs', undefined),
      getSettings(),
    ]);
    return {
      issues: snapshot.issues,
      games: snapshot.games.map((bundle, index) => ({
        ...loadedBundleToGameInfo(bundle, snapshot.layouts, convertFileSrc),
        isSelected: settings.activeGameId
          ? bundle.config.id === settings.activeGameId
          : index === 0,
      })),
    };
  };
  const getGames = async () => (await getConfigs()).games;

  return {
    updates: {
      prepare: (tagName) => invokeTauri<PreparedUpdate>('Updates', 'prepare_update', { tagName }),
      download: (onProgress) => updateCommand('download_update', onProgress),
      install: async (onProgress) => {
        // Finish queued preferences writes before the installer exits the process.
        await settingsWrites;
        await updateCommand('install_update', onProgress);
      },
      complete: () => invokeTauri<void>('Updates', 'complete_update', undefined),
    },
    window: {
      minimize: () => invokeTauri<void>('Window', 'minimize_window', undefined),
      maximize: () => invokeTauri<boolean>('Window', 'maximize_window', undefined),
      close: () => invokeTauri<void>('Window', 'close_window', undefined),
      isMaximized: () => invokeTauri<boolean>('Window', 'is_maximized', undefined),
      onResized: (callback) => listenTauri('tauri://resize', callback),
    },

    playback: {
      play: () => invokeTauri<boolean>('Playback', 'play_song', undefined),
      pause: () => invokeTauri<boolean>('Playback', 'pause_song', undefined),
      stop: () => invokeTauri<boolean>('Playback', 'stop_song', undefined),
      seek: (positionMs, seekId) => invokeTauri<boolean>('Playback', 'seek_song', { ms: Math.round(positionMs), seekId }),
      setSpeed: (speed) => invokeTauri<boolean>('Playback', 'set_playback_speed', { speed }),
      setVolume: (volume) => invokeTauri<boolean>('Playback', 'set_master_volume', { volume: volume / 100 }),
      setKeyOffset: (offset) => invokeTauri<boolean>('Playback', 'set_key_offset', { offset }),
      setTransposeMode: (mode) => invokeTauri<boolean>('Playback', 'set_transpose_mode', { mode }),
      setTrackMuted: (trackIndex, muted) => invokeTauri<boolean>('Playback', 'set_track_muted', { trackIndex, muted }),
      setTrackVolume: (trackIndex, volume) => invokeTauri<boolean>('Playback', 'set_track_volume', { trackIndex, volume }),
      setListenMode: (enabled) => invokeTauri<boolean>('Playback', 'set_listen_mode', { enabled }),
      setNoteTiming: (holdNotes, tapThresholdMs) => invokeTauri<boolean>('Playback', 'set_note_timing', { holdNotes, tapThresholdMs }),
      setKeyMarkers: (markers) => invokeTauri<boolean>('Playback', 'set_key_markers', {
        markers: markers.map((marker) => ({
          timeMs: marker.timeMs ?? Math.round((marker.time ?? 0) * 1000),
          keyOffset: marker.keyOffset ?? 0,
          transpose: marker.transpose ?? null,
          label: marker.label || marker.noteDisplay || marker.key || '',
        })),
      }),
      setKeyMarkersEnabled: (enabled) =>
        invokeTauri<boolean>('Playback', 'set_key_markers_enabled', { enabled }),
      detectKeySignatures: (filePath) =>
        invokeTauri<unknown[]>('Playback', 'detect_key_signatures', { filePath }),
      onStateChanged: (callback) => listenTauri<PlaybackStatus>(
        'playback:state_changed',
        callback,
      ),
      onTimeUpdate: (callback) => listenTauri<{ timeMs: number; seekId: number }>('playback:time', callback),
      onPedalState: (callback) => listenTauri<PedalState>('playback:pedals', callback),
      onNoteEvent: (callback) => listenTauri<number[]>('playback:notes', callback),
      onTrackNoteEvent: (callback) => listenTauri<{ pitch: number; track_index?: number; trackIndex?: number }[]>(
        'playback:track_notes',
        (notes) => callback(notes.map((note) => ({
          pitch: note.pitch,
          trackIndex: note.track_index ?? note.trackIndex ?? 0,
        }))),
      ),
      loadMidiFile: async (path) => {
        const context = songSettingsContext;
        const parsed = loadedMidiToMidiFile(await invokeTauri<RawLoadedMidiFile>('Playback', 'load_midi_file', { path }));
        const record = await invokeTauri<RawSongRecord | null>('Songs', 'get_song_settings', { id: parsed.id, context });
        if (!record) return parsed;
        const saved = songRecordToMidiFile(record);
        const disabled = new Set((saved.disabledTracks ?? '').split(',').filter(Boolean).map(Number));
        return {
          ...saved, durationMs: parsed.durationMs, durationSeconds: parsed.durationSeconds,
          nativeBpm: parsed.nativeBpm, bpm: saved.bpmOverride ?? parsed.bpm,
          notes: parsed.notes,
          tracks: parsed.tracks.map(track => ({ ...track, isMuted: disabled.has(track.index), muted: disabled.has(track.index) })),
        };
      },
      setUiFps: (fps) => invokeTauri<boolean>('Playback', 'set_ui_fps', { fps }),
      setSynthMode: (mode) => invokeTauri<boolean>('Playback', 'set_synth_mode', { mode }),
      loadCustomSoundFont: (path) => invokeTauri<boolean>('Playback', 'load_custom_soundfont', { path }),
      resetSoundFont: () => invokeTauri<boolean>('Playback', 'reset_soundfont', undefined),
      panicStop: () => invokeTauri<boolean>('Playback', 'panic_stop', undefined),
    },

    songs: {
      setSettingsContext: (context) => { songSettingsContext = context ? { ...context } : null; },
      getSongs,
      readMidiFile: async (path) => loadedMidiToMidiFile(
        await invokeTauri<RawLoadedMidiFile>('Songs', 'read_midi_file', { path })),
      getSongById: async (id) => {
        const record = await invokeTauri<RawSongRecord | null>('Songs', 'get_song_settings', { id, context: songSettingsContext });
        return record ? songRecordToMidiFile(record) : null;
      },
      saveSong: (song, scope) => invokeTauri<boolean>('Songs', 'save_song_settings', {
        id: song.id,
        settings: serializeSongSettings(song),
        scope,
      }),
      saveSongSettings: (id, settings, scope) =>
        invokeTauri<boolean>('Songs', 'save_song_settings', { id, settings, scope }),
      saveSongMetadata: (id, metadata) =>
        invokeTauri<boolean>('Songs', 'save_song_metadata', { id, metadata }),
      saveSongDetails: (id, details, scope) =>
        invokeTauri<boolean>('Songs', 'save_song_details', { id, details, scope }),
      analyzeSongFile: (path) => invokeTauri('Songs', 'analyze_song_file', { path }),
      revealSongFile: (path) => invokeTauri('Songs', 'reveal_song_file', { path }),
      deleteSong: (id) => invokeTauri<boolean>('Songs', 'delete_song', { id }),
      excludeSongs: (ids) => invokeTauri<boolean>('Songs', 'exclude_library_songs', { ids }),
      getFileIssues: () => invokeTauri('Songs', 'get_library_file_issues', undefined),
      resolveFileIssue: (path, action) => invokeTauri('Songs', 'resolve_library_file_issue', { path, action }),
      importFiles: async (paths) => {
        let selectedPaths = paths;
        if (!selectedPaths?.length) {
          if (!isTauriEnvironment) return [];
          const { open } = await import('@tauri-apps/plugin-dialog');
          const selection = await open({
            multiple: true,
            filters: [{ name: 'MIDI Files', extensions: ['mid', 'midi'] }],
          });
          if (!selection) return [];
          selectedPaths = Array.isArray(selection) ? selection : [selection];
        }
        const records = await invokeTauri<RawSongRecord[]>('Songs', 'import_midi_files', { paths: selectedPaths });
        return records.map(songRecordToMidiFile);
      },
      scanFolder: async (folderPath) => {
        if (!folderPath?.trim()) {
          throw new Error('Choose a MIDI library folder in Settings before scanning.');
        }
        await invokeTauri('Songs', 'scan_library_folder', { folderPath });
        return getSongs();
      },
      watchFolder: (folderPath) => invokeTauri<boolean>('Songs', 'watch_library_folder', { folderPath }),
    },

    playlists: {
      getPlaylists: () => invokeTauri('Playlists', 'get_playlists', undefined),
      createPlaylist: (title, icon, bannerColor) =>
        invokeTauri('Playlists', 'create_playlist', { title, icon, bannerColor }),
      deletePlaylist: (id) => invokeTauri<boolean>('Playlists', 'delete_playlist', { id }),
      addToPlaylist: (playlistId, songId) =>
        invokeTauri<boolean>('Playlists', 'add_to_playlist', { playlistId, songId }),
      removeFromPlaylist: (playlistId, songId) =>
        invokeTauri<boolean>('Playlists', 'remove_from_playlist', { playlistId, songId }),
      getPlaylistSongs: (playlistId) => invokeTauri<RawSongRecord[]>(
        'Playlists',
        'get_playlist_songs',
        { playlistId, context: songSettingsContext },
      ).then((songs) => songs.map(songRecordToMidiFile)),
    },

    queue: {
      getQueue: () => invokeTauri<RawSongRecord[]>('Queue', 'get_queue_songs', { context: songSettingsContext })
        .then((songs) => songs.map(songRecordToMidiFile)),
      addToQueue: (songIds) => invokeTauri<boolean>('Queue', 'add_to_queue', { songIds }),
      removeFromQueue: (songIds) => invokeTauri<boolean>('Queue', 'remove_from_queue', { songIds }),
      clearQueue: () => invokeTauri<boolean>('Queue', 'clear_queue', undefined),
      reorderQueue: (songIds) => invokeTauri<boolean>('Queue', 'reorder_queue', { songIds }),
      setQueue: (songIds) => invokeTauri<boolean>('Queue', 'set_queue', { songIds }),
    },

    games: {
      getConfigs,
      onConfigsChanged: async (callback) => {
        if (!isTauriEnvironment) return () => {};
        const { listen } = await import('@tauri-apps/api/event');
        return listen('configs:changed', callback);
      },
      getGames,
      getActiveGame: async () => {
        const games = await getGames();
        const activeGame = games.find((game) => game.isSelected) || games[0];
        if (!activeGame) throw new Error('No game profiles are configured.');
        return activeGame;
      },
      setActiveGame: (gameId) => updateSettings({ activeGameId: gameId }),
      setGameLocation: (gameId, path) => updateSettings({ gameLocations: { [gameId]: path } }),
      getGameStatuses: () => invokeTauri<GameStatus[]>('Games', 'get_game_statuses', undefined),
      focusGame: (pid) => invokeTauri<boolean>('Games', 'focus_game', { pid }),
      tapNotePreview: (pitch, isListenMode) =>
        invokeTauri<boolean>('Games', 'tap_note_preview', { pitch, isListenMode }),
      setGameInstrumentMapping: (layout, keyPairs, configPaths) =>
        invokeTauri<boolean>('Games', 'set_game_instrument_mapping', { layout, keyPairs, configPaths }),
      getKeyboardLayouts: () =>
        invokeTauri<Record<string, KeyboardLayoutConfig>>('Games', 'get_keyboard_layouts', undefined),
      reloadConfigs: () => getConfigs(true),
      resetConfigs: () => getConfigs('reset'),
      openGamesFolder: () => runTauriCommand('Games', 'open_games_folder'),
      openLayoutsFolder: () => runTauriCommand('Games', 'open_layouts_folder'),
      onGameStatusChanged: async (callback) => {
        if (!isTauriEnvironment) return () => {};
        const { listen } = await import('@tauri-apps/api/event');
        return listen<GameStatus[]>('games:status_updated', event => callback(event.payload));
      },
    },

    settings: {
      getStorageUsage: async () => {
        await settingsWrites;
        return invokeTauri<StorageUsage>('Storage', 'get_storage_usage', undefined);
      },
      clearDiscoverCache: () => invokeTauri<number>('Storage', 'clear_discover_cache', undefined),
      getDataDirectory: () => invokeTauri<string>('Settings', 'get_data_directory', undefined),
      getSettings,
      updateSettings,
      resetSettings: () => enqueueSettingsWrite(() => invokeTauri<boolean>('Settings', 'reset_app_settings', undefined)),
      browseFolder: async (title) => {
        if (!isTauriEnvironment) return null;
        const { open } = await import('@tauri-apps/plugin-dialog');
        const selection = await open({ directory: true, multiple: false, title });
        return selection ? (Array.isArray(selection) ? selection[0] ?? null : selection) : null;
      },
      browseGameExecutable: async () => {
        const { open } = await import('@tauri-apps/plugin-dialog');
        const selection = await open({
          multiple: false,
          filters: [{ name: 'Game Executable', extensions: ['exe'] }],
        });
        return Array.isArray(selection) ? selection[0] ?? null : selection;
      },
      browseSoundFont: async () => {
        if (!isTauriEnvironment) return null;
        const { open } = await import('@tauri-apps/plugin-dialog');
        const selection = await open({
          multiple: false,
          filters: [{ name: 'SoundFont 2 Files', extensions: ['sf2'] }],
        });
        return selection ? (Array.isArray(selection) ? selection[0] ?? null : selection) : null;
      },
      openFolderInExplorer: (path) =>
        invokeTauri('Explorer', 'open_folder_in_explorer', { path }).then(() => true),
      openUrl: (url) => invokeTauri('OpenUrl', 'open_url', { url }).then(() => true),
      getAppVersion: () => invokeTauri<string>('Version', 'get_app_version', undefined),
      updateGlobalHotkeys: (enabled, bindings) =>
        invokeTauri<boolean>('Hotkeys', 'update_global_hotkeys', { enabled, bindings }),
      updateSongDefaults: (defaults) =>
        invokeTauri<boolean>('Settings', 'update_song_defaults', { defaults }),
    },

    onlineMidi: {
      search: (_query, _provider, _category, _sort, page) =>
        Promise.resolve({ items: [], total: 0, page: page ?? 1 } as { items: OnlineMidiItem[]; total: number; page: number }),
      downloadSong: () => Promise.reject(new Error('Online MIDI downloads are not configured yet.')),
    },

    logging: {
      logMessage: async (level, message, _channel, caller, file, line) => {
        switch (level.toLowerCase()) {
          case 'error': await logger.error(message, undefined, caller, file, line); break;
          case 'warn':
          case 'warning': await logger.warn(message, caller, file, line); break;
          default: await logger.info(message, caller, file, line);
        }
      },
      logStep: (step, details) => logger.step(step, details),
      logPageVisit: (pageName, source) => logger.pageVisit(pageName, source),
      clearLogs: () => logger.clearLogs(),
      openLogsFolder: () => logger.openLogsFolder(),
      readLogTail: (channel, maxLines) => logger.readTail(channel, maxLines),
      setVerbosity: (verbosity) => logger.setVerbosity(verbosity),
      setDebugMode: (enabled) => logger.setDebugMode(enabled),
    },

    telemetry: {
      setEnabled: (enabled) => telemetry.setEnabled(enabled),
      sendTest: () => telemetry.sendTest(),
      captureException: async (errorType, message, stackTrace) => {
        await telemetry.captureException(new Error(`${errorType}: ${message}\n${stackTrace || ''}`));
        return true;
      },
      captureMessage: async (message, level) => {
        const safeLevel = level === 'warning' || level === 'error' || level === 'fatal' ? level : 'info';
        await telemetry.captureMessage(message, safeLevel);
        return true;
      },
    },
  };
}
