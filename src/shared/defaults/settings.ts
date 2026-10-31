import type { AppSettings, UiPreferences, PlaybackPreferences, StudioPreferences } from '../types/settings.js';
import defaults from '../../../crates/amp-core/src/settings/defaults.json' with { type: 'json' };

/** Shared with native settings migration so newly introduced options use app defaults. */
export const DEFAULT_APP_SETTINGS: AppSettings = defaults as AppSettings;
export const DEFAULT_UI_PREFERENCES = defaults.ui as Required<Omit<UiPreferences, 'songColumnWidths'>>
  & { studio: StudioPreferences; playback: PlaybackPreferences };

/** Each reset/store gets its own nested objects instead of mutating the defaults. */
export function createDefaultSettings(): AppSettings {
  return structuredClone(DEFAULT_APP_SETTINGS);
}
