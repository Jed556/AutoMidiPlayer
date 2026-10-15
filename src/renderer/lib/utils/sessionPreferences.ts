import type { PlaybackPreferences, StudioPreferences } from '../../../shared/types/settings.js';

export function boundedNumber(value: unknown, fallback: number, min: number, max: number): number {
  return typeof value === 'number' && Number.isFinite(value) ? Math.max(min, Math.min(max, value)) : fallback;
}

export function normalizePage(value: unknown): string {
  const page = typeof value === 'string' ? value.toLowerCase() : '';
  return ['tracks', 'studio', 'sheet', 'instrument', 'queue', 'songs', 'discover', 'settings', 'about'].includes(page)
    ? page : 'songs';
}

export function normalizeStudio(value: Partial<StudioPreferences> = {}): StudioPreferences {
  return {
    zoomX: boundedNumber(value.zoomX, 80, 20, 400),
    zoomY: boundedNumber(value.zoomY, 16, 2, 40),
    isLockedToPlayhead: typeof value.isLockedToPlayhead === 'boolean' ? value.isLockedToPlayhead : true,
    displayMode: value.displayMode === 'Transpose' || value.displayMode === 'NoteModifiers' || value.displayMode === 'SongDefault'
      ? value.displayMode : 'Auto',
    sidebarTab: value.sidebarTab === 'settings' || value.sidebarTab === 'markers' ? value.sidebarTab : 'tracks',
  };
}

export function normalizePlayback(value: Partial<PlaybackPreferences> = {}): PlaybackPreferences {
  return {
    activeSongId: typeof value.activeSongId === 'string' ? value.activeSongId : null,
    positionSeconds: boundedNumber(value.positionSeconds, 0, 0, Number.MAX_SAFE_INTEGER),
    queuePlaylistId: typeof value.queuePlaylistId === 'string' ? value.queuePlaylistId : null,
    shuffledSongIds: Array.isArray(value.shuffledSongIds)
      ? [...new Set(value.shuffledSongIds.filter(id => typeof id === 'string'))] : [],
    isShuffled: value.isShuffled === true,
    loopMode: value.loopMode === 'all' || value.loopMode === 'single' ? value.loopMode : 'off',
    isListenMode: value.isListenMode === true,
    synthMode: value.synthMode === 'raw' ? 'raw' : 'adapted',
    volume: boundedNumber(value.volume, 0.85, 0, 2),
  };
}
