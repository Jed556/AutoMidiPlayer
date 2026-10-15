import type { AppSettings } from '../types/settings.js';

/**
 * Defaults for a new installation. These are application defaults, not mock
 * data: the native settings store persists the user's changes separately.
 */
export const DEFAULT_APP_SETTINGS: AppSettings = {
  ui: {
    animationTiming: 0.2,
    pedalsEnabled: true,
    libraryWidth: 80,
    libraryExpandedWidth: 340,
  },
  theme: 'Dark',
  accentColor: '#1DB954',
  smoothScrolling: true,
  autoEnableListenMode: true,
  keypressMethod: 'SendInput',
  enableKeyUp: true,
  keyPressDelayMs: 20,
  overlapBehavior: 'retrigger',
  // Resolved from loaded keyboard layouts by SettingsStore before views open.
  autoCorrectThreshold: 0,
  hotkeysEnabled: true,
  mouseStopClick: 'Disabled',
  hotkeys: [
    { action: 'PlayPause', label: 'Play / Pause', key: 'Space', ctrl: true, alt: true, shift: false },
    { action: 'Next', label: 'Next Song', key: 'Right', ctrl: true, alt: true, shift: false },
    { action: 'Previous', label: 'Previous Song', key: 'Left', ctrl: true, alt: true, shift: false },
    { action: 'SpeedUp', label: 'Increase Speed', key: 'Up', ctrl: true, alt: true, shift: false },
    { action: 'SpeedDown', label: 'Decrease Speed', key: 'Down', ctrl: true, alt: true, shift: false },
    { action: 'Panic', label: 'Panic / Stop All', key: 'Escape', ctrl: true, alt: true, shift: false },
  ],
  midiFolder: '',
  autoScanMidiFolder: true,
  gameLocations: {},
  defaultBaseKeyOffset: 0,
  defaultKeyOffset: 0,
  autoDetectBaseKey: true,
  defaultTranspose: 'Smart',
  defaultSpeed: 1.0,
  defaultHoldNotes: false,
  defaultTapThresholdMs: 0,
  defaultMergeNotes: true,
  defaultMergeToleranceMs: 25,
  autoCheckUpdates: true,
  updateCheckFrequency: 'hourly',
  autoDownloadUpdates: false,
  includeBetaUpdates: false,
  telemetryOptIn: false,
  notifyAccountRisk: true,
  debugModeEnabled: false,
  crashLogVerbosity: 1,
  logPlayedNotes: false,
  pageCaching: true,
  cacheAutoClean: 'weekly',
  cacheMaxSizeMb: 500,
};
