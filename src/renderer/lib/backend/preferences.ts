import type { UiPreferences } from '$shared/types/settings.js';
import { api } from './index.js';

export async function loadUiPreferences(): Promise<UiPreferences> {
  const settings = await api.settings.getSettings();
  return settings.ui ?? {};
}

let pending: UiPreferences | null = null;
let timer: ReturnType<typeof setTimeout> | null = null;
let writes: Promise<void> = Promise.resolve();

function merge(older: UiPreferences, newer: UiPreferences): UiPreferences {
  const result = { ...older, ...newer };
  for (const key of ['studio', 'playback', 'songColumnWidths'] as const) {
    if (older[key] && newer[key]) Object.assign(result, { [key]: { ...older[key], ...newer[key] } });
  }
  return result;
}

/** Coalesce slider/zoom changes and keep independent preference owners intact. */
export function saveUiPreferences(preferences: UiPreferences): void {
  const snapshot = JSON.parse(JSON.stringify(preferences)) as UiPreferences;
  pending = merge(pending ?? {}, snapshot);
  if (!timer) timer = setTimeout(() => {
    void flushUiPreferences().catch(error => console.warn('[Preferences] Failed to persist UI preferences:', error));
  }, 150);
}

/** Await this before closing or reloading so the last interaction reaches SQLite. */
export async function flushUiPreferences(): Promise<void> {
  if (timer) clearTimeout(timer);
  timer = null;
  const write = async () => {
    if (!pending) return;
    const snapshot = pending;
    pending = null;
    try {
      if (!await api.settings.updateSettings({ ui: snapshot })) throw new Error('Preferences could not be saved.');
    } catch (error) {
      pending = merge(snapshot, pending ?? {});
      throw error;
    }
  };
  writes = writes.then(write, write);
  await writes;
}
