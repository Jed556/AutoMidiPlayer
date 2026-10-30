import { DEFAULT_UI_PREFERENCES } from '../../../shared/defaults/settings.ts';
import type { PlaybackPreferences, StudioPreferences } from '../../../shared/types/settings.js';

export function boundedNumber(value: unknown, fallback: number, min: number, max: number): number {
  return typeof value === 'number' && Number.isFinite(value) ? Math.max(min, Math.min(max, value)) : fallback;
}

export function normalizePage(value: unknown): string {
  const page = typeof value === 'string' ? value.toLowerCase() : '';
  return ['tracks', 'studio', 'sheet', 'instrument', 'queue', 'songs', 'discover', 'settings', 'about'].includes(page)
    ? page : DEFAULT_UI_PREFERENCES.lastViewedPage;
}

export function normalizeStudio(value: Partial<StudioPreferences> = {}): StudioPreferences {
  return {
    zoomX: boundedNumber(value.zoomX, DEFAULT_UI_PREFERENCES.studio.zoomX, 20, 400),
    zoomY: boundedNumber(value.zoomY, DEFAULT_UI_PREFERENCES.studio.zoomY, 2, 40),
    isLockedToPlayhead: typeof value.isLockedToPlayhead === 'boolean' ? value.isLockedToPlayhead : DEFAULT_UI_PREFERENCES.studio.isLockedToPlayhead,
    displayMode: value.displayMode === 'Auto' || value.displayMode === 'Transpose' || value.displayMode === 'NoteModifiers' || value.displayMode === 'SongDefault'
      ? value.displayMode : DEFAULT_UI_PREFERENCES.studio.displayMode,
    sidebarTab: value.sidebarTab === 'tracks' || value.sidebarTab === 'settings' || value.sidebarTab === 'markers' ? value.sidebarTab : DEFAULT_UI_PREFERENCES.studio.sidebarTab,
  };
}

export function normalizePlayback(value: Partial<PlaybackPreferences> = {}): PlaybackPreferences {
  return {
    activeSongId: typeof value.activeSongId === 'string' ? value.activeSongId : DEFAULT_UI_PREFERENCES.playback.activeSongId,
    positionSeconds: boundedNumber(value.positionSeconds, DEFAULT_UI_PREFERENCES.playback.positionSeconds, 0, Number.MAX_SAFE_INTEGER),
    queuePlaylistId: typeof value.queuePlaylistId === 'string' ? value.queuePlaylistId : DEFAULT_UI_PREFERENCES.playback.queuePlaylistId,
    shuffledSongIds: Array.isArray(value.shuffledSongIds)
      ? [...new Set(value.shuffledSongIds.filter(id => typeof id === 'string'))] : [...DEFAULT_UI_PREFERENCES.playback.shuffledSongIds],
    isShuffled: typeof value.isShuffled === 'boolean' ? value.isShuffled : DEFAULT_UI_PREFERENCES.playback.isShuffled,
    loopMode: value.loopMode === 'off' || value.loopMode === 'all' || value.loopMode === 'single' ? value.loopMode : DEFAULT_UI_PREFERENCES.playback.loopMode,
    isListenMode: typeof value.isListenMode === 'boolean' ? value.isListenMode : DEFAULT_UI_PREFERENCES.playback.isListenMode,
    synthMode: value.synthMode === 'raw' || value.synthMode === 'adapted' ? value.synthMode : DEFAULT_UI_PREFERENCES.playback.synthMode,
    volume: boundedNumber(value.volume, DEFAULT_UI_PREFERENCES.playback.volume, 0, 2),
  };
}
