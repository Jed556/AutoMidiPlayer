export type ThemeMode = 'Light' | 'Dark' | 'System';

export type KeypressMethod = 'SendInput' | 'PostMessage' | 'WindowsHook';

export type OverlapBehavior = 'hold' | 'retrigger';

export type MouseStopClickOption = 'Disabled' | 'LeftClick' | 'RightClick' | 'AnyClick';

export type SearchExpansionDirection = 'left' | 'right';

export interface StudioPreferences {
  zoomX: number;
  zoomY: number;
  isLockedToPlayhead: boolean;
  displayMode: 'Auto' | 'Transpose' | 'NoteModifiers' | 'SongDefault';
  sidebarTab: 'tracks' | 'settings' | 'markers';
}

export interface PlaybackPreferences {
  activeSongId: string | null;
  positionSeconds: number;
  queuePlaylistId: string | null;
  shuffledSongIds: string[];
  isShuffled: boolean;
  loopMode: 'off' | 'all' | 'single';
  isListenMode: boolean;
  synthMode: 'adapted' | 'raw';
  volume: number;
}

export interface UiPreferences {
  lastViewedPage?: string;
  selectedPlaylistId?: string;
  songsSortMode?: 'title' | 'artist' | 'dateAdded' | 'duration';
  studioSidebarCollapsed?: boolean;
  studio?: Partial<StudioPreferences>;
  playback?: Partial<PlaybackPreferences>;
  locale?: 'en-US' | 'zh-CN';
  animationTiming?: number;
  pedalsEnabled?: boolean;
  libraryWidth?: number;
  libraryExpandedWidth?: number;
  songColumnWidths?: Partial<{
    album: number;
    key: number;
    bpm: number;
    transpose: number;
    date: number;
  }>;
}

export interface HotkeyBinding {
  action: 'PlayPause' | 'Next' | 'Previous' | 'SpeedUp' | 'SpeedDown' | 'Panic';
  label: string;
  key: string;
  ctrl: boolean;
  alt: boolean;
  shift: boolean;
}

export interface AppSettings {
  selectedInstrumentIds?: Record<string, string>;
  selectedLayoutIds?: Record<string, string>;
  /** Selected game profile, persisted alongside the rest of the application settings. */
  activeGameId?: string;
  ui?: UiPreferences;
  theme: ThemeMode;
  accentColor: string; // hex
  smoothScrolling: boolean;
  autoEnableListenMode: boolean;
  keypressMethod?: KeypressMethod;
  enableKeyUp: boolean;
  keyPressDelayMs: number;
  overlapBehavior: OverlapBehavior;
  autoCorrectThreshold: number;
  hotkeysEnabled: boolean;
  mouseStopClick: MouseStopClickOption;
  hotkeys: HotkeyBinding[];
  midiFolder: string;
  autoScanMidiFolder: boolean;
  gameLocations: Record<string, string>;
  defaultBaseKeyOffset: number;
  defaultKeyOffset: number;
  autoDetectBaseKey: boolean;
  defaultTranspose: 'Ignore' | 'Up' | 'Down' | 'Smart';
  defaultSpeed: number;
  defaultHoldNotes: boolean;
  defaultTapThresholdMs: number;
  defaultMergeNotes: boolean;
  defaultMergeToleranceMs: number;
  autoCheckUpdates: boolean;
  updateCheckFrequency: '10min' | 'hourly' | 'daily';
  autoDownloadUpdates: boolean;
  includeBetaUpdates: boolean;
  telemetryOptIn: boolean;
  notifyAccountRisk: boolean;
  debugModeEnabled: boolean;
  crashLogVerbosity: number;
  logPlayedNotes: boolean;
  pageCaching: boolean;
  cacheAutoClean: 'daily' | 'weekly' | 'monthly' | 'never';
  cacheMaxSizeMb: number;
}
