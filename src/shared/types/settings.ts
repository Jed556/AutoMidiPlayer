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
  soundFontGapFilling: boolean;
  soundFontOrder: string[];
  soundFontId: string;
  volume: number;
}

export interface UiPreferences {
  lastViewedPage?: string;
  selectedPlaylistId?: string;
  songsSortMode?: 'custom' | 'title' | 'artist' | 'dateAdded' | 'duration';
  songsSortDirection?: 'asc' | 'desc';
  librarySongOrder?: string[];
  scrollPositions?: Record<string, { x: number; y: number }>;
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
  instrumentStage?: 'game' | 'piano';
  instrumentGameSelected?: boolean;
  instrumentPianoSelected?: boolean;
}

export interface KeyBinding {
  key: string;
  ctrl: boolean;
  alt: boolean;
  shift: boolean;
}

export interface HotkeyBinding extends KeyBinding {
  action: 'PlayPause' | 'Next' | 'Previous' | 'SpeedUp' | 'SpeedDown' | 'Panic';
  label: string;
}

export interface PedalBindings {
  sustainKey: string | null;
  sostenutoKey: string | null;
  unaCordaKey: string | null;
}

export interface AppSettings {
  /** Owned by the native MIDI service; change through MidiInputApi.selectPort. */
  midiInputPortId: string | null;
  /** Set only after first-run setup has saved preferences and imported the library. */
  onboardingCompleted: boolean;
  selectedInstrumentIds?: Record<string, string>;
  selectedLayoutIds?: Record<string, string>;
  /** Selected game profile, persisted alongside the rest of the application settings. */
  activeGameId?: string;
  ui?: UiPreferences;
  theme: ThemeMode;
  accentColor: string; // hex
  smoothScrolling: boolean;
  minimizeToTray: boolean;
  closeToTray: boolean;
  autoEnableListenMode: boolean;
  keypressMethod?: KeypressMethod;
  enableKeyUp: boolean;
  keyPressDelayMs: number;
  gamePreviewFollowNoteTiming: boolean;
  overlapBehavior: OverlapBehavior;
  repressGapMs: number;
  autoCorrectThreshold: number;
  autoCorrectDefault: { belowKeyCount: number; preferredKeyCount: number | null };
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
  defaultAutoChord: boolean;
  defaultChordDetectionMilliseconds: number;
  defaultEnableKeyChangeMarkers: boolean;
  autoCheckUpdates: boolean;
  updateCheckFrequency: '10min' | 'hourly' | 'daily' | 'weekly' | 'launch';
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
