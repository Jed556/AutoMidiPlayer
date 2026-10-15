import type { MidiFile } from './midi.js';
import type { PlaybackStatus } from './playback.js';
import type { GameInfo, GameStatus, GameConfigState, KeyboardLayoutConfig } from './games.js';
import type { AppSettings } from './settings.js';
import type { OnlineMidiItem } from './onlineMidi.js';

/** Native Tauri backend API exposed to the renderer. */
export interface WindowControlApi {
  minimize: () => Promise<void>;
  maximize: () => Promise<boolean>;
  close: () => Promise<void>;
  isMaximized: () => Promise<boolean>;
  onResized: (callback: () => void) => () => void;
  onNavigateBack?: (callback: () => void) => () => void;
  onNavigateForward?: (callback: () => void) => () => void;
}

export interface PedalState {
  sustain: boolean;
  sostenuto: boolean;
  unaCorda: boolean;
}

export interface PlaybackApi {
  play: (songId?: string) => Promise<boolean>;
  pause: () => Promise<boolean>;
  stop: () => Promise<boolean>;
  seek: (positionMs: number, seekId: number) => Promise<boolean>;
  setSpeed: (speed: number) => Promise<boolean>;
  setVolume: (volume: number) => Promise<boolean>;
  setKeyOffset: (offset: number) => Promise<boolean>;
  setTransposeMode: (mode: string) => Promise<boolean>;
  setTrackMuted: (trackIndex: number, muted: boolean) => Promise<boolean>;
  setTrackVolume: (trackIndex: number, volume: number) => Promise<boolean>;
  setListenMode: (enabled: boolean) => Promise<boolean>;
  setNoteTiming: (holdNotes: boolean, tapThresholdMs: number) => Promise<boolean>;
  setKeyMarkers: (markers: any[]) => Promise<boolean>;
  setKeyMarkersEnabled: (enabled: boolean) => Promise<boolean>;
  detectKeySignatures: (filePath: string) => Promise<any[]>;
  onStateChanged: (callback: (status: PlaybackStatus) => void) => () => void;
  onTimeUpdate: (callback: (position: { timeMs: number; seekId: number }) => void) => () => void;
  onPedalState: (callback: (state: PedalState) => void) => () => void;
  onNoteEvent: (callback: (activeNotes: number[]) => void) => () => void;
  onTrackNoteEvent: (callback: (trackNotes: { pitch: number; trackIndex: number }[]) => void) => () => void;
  loadMidiFile: (path: string) => Promise<MidiFile>;
  setUiFps: (fps: number) => Promise<boolean>;
  setSynthMode: (mode: 'adapted' | 'raw') => Promise<boolean>;
  loadCustomSoundFont: (path: string) => Promise<boolean>;
  resetSoundFont: () => Promise<boolean>;
  panicStop: () => Promise<boolean>;
}

export interface SongDetailsUpdate {
  title: string;
  artist: string;
  album: string;
  baseKey: number | null;
  key: number;
  transpose: number;
  bpm: number | null;
  speed: number;
  mergeNotes: boolean;
  mergeMilliseconds: number;
  holdNotes: boolean;
  tapThresholdMs: number;
  dateAdded: string;
}

export interface SongSettingsContext {
  gameId: string;
  instrumentId: string;
}

export type SongSettingsScope =
  | ({ kind: 'instrument' } & SongSettingsContext)
  | { kind: 'game'; gameId: string }
  | { kind: 'global' };

export interface SongFileAnalysis {
  nativeBpm: number;
  detectedBaseKey: number | null;
  fileDate: string;
}

export interface SongsApi {
  setSettingsContext: (context: SongSettingsContext | null) => void;
  getSongs: () => Promise<MidiFile[]>;
  getSongById: (id: string) => Promise<MidiFile | null>;
  readMidiFile: (path: string) => Promise<MidiFile>;
  saveSong: (song: MidiFile, scope: SongSettingsContext & { kind: 'instrument' }) => Promise<boolean>;
  saveSongSettings: (id: string, settings: { key: number; baseKey?: number | null; transpose: number; disabledTracks?: string | null; keyChanges?: string | null; speed?: number; holdNotes?: boolean; tapThresholdMs?: number; autoChord?: boolean; enableKeyChangeMarkers?: boolean }, scope: SongSettingsContext & { kind: 'instrument' }) => Promise<boolean>;
  saveSongMetadata: (id: string, metadata: { title?: string; artist?: string; album?: string; albumId?: string; bpm?: number }) => Promise<boolean>;
  saveSongDetails: (id: string, details: SongDetailsUpdate, scope: SongSettingsScope) => Promise<boolean>;
  analyzeSongFile: (path: string) => Promise<SongFileAnalysis>;
  revealSongFile: (path: string) => Promise<boolean>;
  deleteSong: (id: string) => Promise<boolean>;
  excludeSongs: (ids: string[]) => Promise<boolean>;
  getFileIssues: () => Promise<LibraryFileIssue[]>;
  resolveFileIssue: (path: string, action: FileIssueAction) => Promise<boolean>;
  importFiles: (filePaths?: string[]) => Promise<MidiFile[]>;
  scanFolder: (folderPath?: string) => Promise<MidiFile[]>;
  watchFolder: (folderPath: string | null) => Promise<boolean>;
}

export type FileIssueKind = 'missing' | 'invalid' | 'duplicate' | 'excluded';
export type FileIssueAction = 'restore' | 'forget' | 'useCopy' | 'deleteFile';
export interface LibraryFileIssue {
  path: string;
  title: string;
  kind: FileIssueKind;
  songId: string | null;
  relatedPath: string | null;
  details: string | null;
}

export interface LibraryScanSummary {
  added: number;
  removed: number;
  addedFiles: string[];
  removedFiles: string[];
  newConflicts?: number;
}

export interface PlaylistsApi {
  getPlaylists: () => Promise<any[]>;
  createPlaylist: (title: string, icon?: string, bannerColor?: string) => Promise<any>;
  deletePlaylist: (id: string) => Promise<boolean>;
  addToPlaylist: (playlistId: string, songId: string) => Promise<boolean>;
  removeFromPlaylist: (playlistId: string, songId: string) => Promise<boolean>;
  getPlaylistSongs: (playlistId: string) => Promise<MidiFile[]>;
}

export interface QueueApi {
  getQueue: () => Promise<MidiFile[]>;
  addToQueue: (songIds: string[]) => Promise<boolean>;
  removeFromQueue: (songIds: string[]) => Promise<boolean>;
  clearQueue: () => Promise<boolean>;
  reorderQueue: (newOrderIds: string[]) => Promise<boolean>;
  setQueue: (songIds: string[]) => Promise<boolean>;
}

export interface GamesApi {
  getConfigs: () => Promise<GameConfigState>;
  onConfigsChanged: (callback: () => void) => Promise<() => void>;
  getGames: () => Promise<GameInfo[]>;
  getActiveGame: () => Promise<GameInfo>;
  setActiveGame: (gameId: string) => Promise<boolean>;
  setGameLocation: (gameId: string, path: string) => Promise<boolean>;
  getGameStatuses: () => Promise<GameStatus[]>;
  focusGame: (pid: number) => Promise<boolean>;
  tapNotePreview: (pitch: number, isListenMode: boolean) => Promise<boolean>;
  setGameInstrumentMapping: (layout: string, keyPairs: [number, string][], configPaths?: string[]) => Promise<boolean>;
  onGameStatusChanged: (callback: (statuses: GameStatus[]) => void) => Promise<() => void>;
  getKeyboardLayouts: () => Promise<Record<string, KeyboardLayoutConfig>>;
  reloadConfigs: () => Promise<GameConfigState>;
  resetConfigs: () => Promise<GameConfigState>;
  openGamesFolder: () => Promise<boolean>;
  openLayoutsFolder: () => Promise<boolean>;
}

export interface StorageUsage {
  /** App data excluding the two separately counted categories. */
  appDataBytes: number;
  onlineMidiBytes: number;
  discoverCacheBytes: number;
  totalBytes: number;
}

export interface SettingsApi {
  getStorageUsage: () => Promise<StorageUsage>;
  clearDiscoverCache: () => Promise<number>;
  getDataDirectory: () => Promise<string>;
  getSettings: () => Promise<Partial<AppSettings>>;
  updateSettings: (partial: Partial<AppSettings>) => Promise<boolean>;
  resetSettings: () => Promise<boolean>;
  browseFolder: (title?: string) => Promise<string | null>;
  openFolderInExplorer: (path?: string) => Promise<boolean>;
  openUrl: (url: string) => Promise<boolean>;
  getAppVersion: () => Promise<string>;
  updateGlobalHotkeys: (enabled: boolean, bindings: any[]) => Promise<boolean>;
  updateSongDefaults: (defaults: any) => Promise<boolean>;
  browseGameExecutable: () => Promise<string | null>;
  browseSoundFont: () => Promise<string | null>;
}

export interface OnlineMidiApi {
  search: (query: string, provider: string, category?: string, sort?: string, page?: number) => Promise<{
    items: OnlineMidiItem[];
    total: number;
    page: number;
  }>;
  downloadSong: (item: OnlineMidiItem) => Promise<MidiFile>;
}

export interface LoggingApi {
  logMessage: (level: string, message: string, channel?: string, caller?: string, file?: string, line?: number) => Promise<void>;
  logStep: (step: string, details?: string) => Promise<void>;
  logPageVisit: (pageName: string, source?: string) => Promise<void>;
  clearLogs: () => Promise<boolean>;
  openLogsFolder: () => Promise<boolean>;
  readLogTail: (channel?: string, maxLines?: number) => Promise<string>;
  setVerbosity: (verbosity: number) => Promise<boolean>;
  setDebugMode: (enabled: boolean) => Promise<boolean>;
}

export interface TelemetryApi {
  setEnabled: (enabled: boolean) => Promise<boolean>;
  sendTest: () => Promise<{ success: boolean; message: string }>;
  captureException: (errorType: string, message: string, stackTrace?: string) => Promise<boolean>;
  captureMessage: (message: string, level?: string) => Promise<boolean>;
}

// Full typed API exposed to renderer window.api
export interface AppWindowApi {
  updates: import('./update').UpdatesApi;
  window: WindowControlApi;
  playback: PlaybackApi;
  songs: SongsApi;
  queue: QueueApi;
  games: GamesApi;
  settings: SettingsApi;
  onlineMidi: OnlineMidiApi;
  playlists: PlaylistsApi;
  logging: LoggingApi;
  telemetry: TelemetryApi;
}

declare global {
  interface Window {
    api?: AppWindowApi;
  }
}
