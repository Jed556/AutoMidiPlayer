import type { MidiFile } from '../../../shared/types/midi.js';
import { DEFAULT_UI_PREFERENCES } from '../../../shared/defaults/settings.js';
import { api, isTauri } from '../backend/index.js';
import type { LibraryFileIssue, LibraryScanSummary, FileIssueAction } from '../../../shared/types/ipc.js';
import { notificationCenter } from '../ui/notificationCenter.svelte';
import { t } from '../i18n';

export const ALL_SONGS_PLAYLIST_ID = 'songs';
export const LIKED_SONGS_PLAYLIST_ID = 'liked-songs';

export function isBuiltInPlaylist(id: string | null | undefined): boolean {
  return !id || id === ALL_SONGS_PLAYLIST_ID || id === 'all-songs' || id === LIKED_SONGS_PLAYLIST_ID;
}

export interface PlaylistInfo {
  id: string;
  title: string;
  subtitle: string;
  description?: string;
  icon: string;
  gradient: string;
  bannerColor?: string;
  songCount: number;
  gameId?: string;
  isBuiltIn?: boolean;
}

class LibraryStore {
  songs = $state<MidiFile[]>([]);
  songsByPlaylist = $state<Record<string, MidiFile[]>>({});
  likedSongIds = $derived(new Set((this.songsByPlaylist[LIKED_SONGS_PLAYLIST_ID] ?? []).map((song) => song.id)));
  selectedPlaylistId = $state<string>(DEFAULT_UI_PREFERENCES.selectedPlaylistId);
  isScanning = $state<boolean>(false);
  playlists = $state<PlaylistInfo[]>([]);
  customPlaylists = $derived(this.playlists.filter((playlist) => !playlist.isBuiltIn));
  fileIssues = $state<LibraryFileIssue[]>([]);
  hasFileIssues = $derived(this.fileIssues.length > 0);
  onSongsUnavailable?: (ids: string[]) => Promise<void>;

  isSongLiked(songId: string): boolean {
    return this.likedSongIds.has(songId);
  }

  isSongInPlaylist(playlistId: string, songId: string): boolean {
    return (this.songsByPlaylist[playlistId] ?? []).some((song) => song.id === songId);
  }

  private _isInitialized = false;
  private reloadId = 0;

  async init() {
    if (this._isInitialized) return;
    this._isInitialized = true;

    // Listen for backend library update events
    if (isTauri && typeof window !== 'undefined') {
      const { listen } = await import('@tauri-apps/api/event');
      await listen('library:updated', async () => {
        await this.reload();
      });
      await listen<LibraryScanSummary>('library:scanned', ({ payload }) => {
        this.notifyChanges(payload);
      });
    }
    await this.reload();
  }

  async reload() {
    const reloadId = ++this.reloadId;
    try {
      const [fetchedSongs, fetchedPlaylists, fileIssues] = await Promise.all([
        api.songs.getSongs(), api.playlists.getPlaylists(), api.songs.getFileIssues(),
      ]);
      if (reloadId !== this.reloadId) return;

      const mapped: PlaylistInfo[] = fetchedPlaylists.map((playlist) => {
        const count = playlist.songCount ?? 0;
        const description = typeof playlist.subtitle === 'string' && playlist.subtitle.trim()
          ? playlist.subtitle.trim()
          : undefined;
        return {
          id: playlist.id,
          title: playlist.title,
          description,
          subtitle: description || `Playlist • ${count} ${count === 1 ? 'song' : 'songs'}`,
          icon: playlist.icon || 'MusicNote2',
          gradient: playlist.gradient || 'linear-gradient(135deg, #1DB954 0%, #0d9488 100%)',
          bannerColor: playlist.bannerColor || 'rgba(29, 185, 84, 0.45)',
          songCount: count,
          gameId: playlist.gameId,
          isBuiltIn: isBuiltInPlaylist(playlist.id),
        };
      });
      const playlistSongs = await Promise.all(
        mapped.map(async (playlist) => [playlist.id, await api.playlists.getPlaylistSongs(playlist.id)] as const),
      );
      if (reloadId !== this.reloadId) return;
      const availableIds = new Set(fetchedSongs.map(song => song.id));
      const unavailable = this.songs.filter(song => !availableIds.has(song.id)).map(song => song.id);
      this.songs = fetchedSongs;
      this.fileIssues = fileIssues;
      this.songsByPlaylist = Object.fromEntries(playlistSongs);
      this.playlists = [
        {
          id: ALL_SONGS_PLAYLIST_ID,
          title: 'All Songs',
          subtitle: `All library songs`,
          icon: 'Cd',
          gradient: 'linear-gradient(135deg, #1DB954 0%, #0d9488 100%)',
          bannerColor: 'rgba(29, 185, 84, 0.45)',
          songCount: this.songs.length,
          isBuiltIn: true,
        },
        ...mapped,
      ];

      if (!this.playlists.some((playlist) => playlist.id === this.selectedPlaylistId)) {
        this.selectedPlaylistId = ALL_SONGS_PLAYLIST_ID;
      }
      if (unavailable.length) await this.onSongsUnavailable?.(unavailable);
    } catch (e) {
      console.warn('[LibraryState] Failed to reload library:', e);
    }
  }

  async scanFolder(folderPath?: string) {
    this.isScanning = true;
    try {
      await api.songs.scanFolder(folderPath);
      await this.reload();
    } catch (e) {
      console.error('[LibraryState] scanFolder error:', e);
      throw e;
    } finally {
      this.isScanning = false;
    }
  }

  async importFiles(paths?: string[], playlistId?: string | null) {
    try {
      const imported = await api.songs.importFiles(paths);
      if (imported && imported.length > 0) {
        const targetPlaylistId = playlistId
          && playlistId !== ALL_SONGS_PLAYLIST_ID
          && playlistId !== 'all-songs'
          ? playlistId
          : null;
        try {
          if (targetPlaylistId) {
            const songIds = [...new Set(imported.map((song) => song.id))];
            for (const songId of songIds) {
              const ok = await api.playlists.addToPlaylist(targetPlaylistId, songId);
              if (!ok) throw new Error('The song could not be added to the playlist.');
            }
          }
        } finally {
          await this.reload();
        }
      }
      return imported;
    } catch (e) {
      console.error('[LibraryState] importFiles error:', e);
      throw e;
    }
  }

  async deleteSong(id: string) {
    return this.deleteSongs([id]);
  }

  async deleteSongs(ids: string[]) {
    const selected = [...new Set(ids)];
    if (!selected.length) return false;
    const success = await api.songs.excludeSongs(selected);
    if (success) {
      await this.reload();
      notificationCenter.warning(t('songs.removedTitle'), t('songs.removedMessage', { count: selected.length }));
    }
    return success;
  }

  notifyChanges(summary: LibraryScanSummary) {
    if (summary.newConflicts && !summary.removedFiles?.length) {
      notificationCenter.warning(t('fileIssues.title'), t('fileIssues.newConflicts', { count: summary.newConflicts }));
    }
    if (summary.addedFiles?.length) {
      notificationCenter.success(t('songs.filesAddedTitle'), summary.addedFiles.length === 1
        ? summary.addedFiles[0] : t('songs.filesAddedMessage', { count: summary.addedFiles.length }));
    }
    if (summary.removedFiles?.length) {
      notificationCenter.warning(t('songs.filesRemovedTitle'), summary.removedFiles.length === 1
        ? t('songs.fileRemovedMessage', { title: summary.removedFiles[0] })
        : t('songs.filesRemovedMessage', { count: summary.removedFiles.length }));
    }
  }

  async resolveFileIssue(path: string, action: FileIssueAction) {
    try {
      await api.songs.resolveFileIssue(path, action);
    } finally {
      await this.reload();
    }
  }

  async createPlaylist(
    title: string,
    icon?: string,
    bannerColor?: string,
    subtitle?: string,
    gradient?: string,
  ) {
    const pl = await api.playlists.createPlaylist(title, icon, bannerColor, subtitle, gradient);
    await this.reload();
    return pl;
  }

  async updatePlaylist(
    id: string,
    details: { title: string; subtitle?: string; icon?: string; gradient?: string; bannerColor?: string },
  ) {
    if (isBuiltInPlaylist(id)) {
      throw new Error('Built-in playlists cannot be edited.');
    }
    const pl = await api.playlists.updatePlaylist(id, details);
    await this.reload();
    return pl;
  }

  async deletePlaylist(id: string) {
    if (isBuiltInPlaylist(id)) {
      throw new Error('Built-in playlists cannot be deleted.');
    }
    const res = await api.playlists.deletePlaylist(id);
    if (this.selectedPlaylistId === id) {
      this.selectedPlaylistId = ALL_SONGS_PLAYLIST_ID;
    }
    await this.reload();
    return res;
  }

  async addToPlaylist(playlistId: string, songId: string) {
    const res = await api.playlists.addToPlaylist(playlistId, songId);
    await this.reload();
    return res;
  }

  async addSongsToPlaylist(playlistId: string, songIds: string[]): Promise<number> {
    const existingIds = new Set((this.songsByPlaylist[playlistId] ?? []).map((song) => song.id));
    const uniqueIds = [...new Set(songIds)];
    const toAdd = uniqueIds.filter((id) => !existingIds.has(id));
    if (!toAdd.length) return 0;

    for (const songId of toAdd) {
      const ok = await api.playlists.addToPlaylist(playlistId, songId);
      if (!ok) throw new Error('The song could not be added to the playlist.');
    }
    await this.reload();
    return toAdd.length;
  }

  async removeFromPlaylist(playlistId: string, songId: string) {
    const res = await api.playlists.removeFromPlaylist(playlistId, songId);
    await this.reload();
    return res;
  }

  async removeSongsFromPlaylist(playlistId: string, songIds: string[]): Promise<number> {
    const uniqueIds = [...new Set(songIds)];
    const existing = this.songsByPlaylist[playlistId];
    const toRemove = existing
      ? uniqueIds.filter((id) => existing.some((song) => song.id === id))
      : uniqueIds;
    if (!toRemove.length) return 0;

    for (const songId of toRemove) {
      const ok = await api.playlists.removeFromPlaylist(playlistId, songId);
      if (!ok) throw new Error('The song could not be removed from the playlist.');
    }
    await this.reload();
    return toRemove.length;
  }

  async reorderPlaylist(playlistId: string, songIds: string[]) {
    const success = await api.playlists.reorderPlaylist(playlistId, songIds);
    if (!success) throw new Error('The playlist order could not be saved.');
    await this.reload();
    return success;
  }
}

export const libraryStore = new LibraryStore();

if (typeof window !== 'undefined') {
  (window as any).libraryStore = libraryStore;
}
