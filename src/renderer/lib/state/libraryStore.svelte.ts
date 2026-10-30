import type { MidiFile } from '../../../shared/types/midi.js';
import { DEFAULT_UI_PREFERENCES } from '../../../shared/defaults/settings.js';
import { api, isTauri } from '../backend/index.js';
import type { LibraryFileIssue, LibraryScanSummary, FileIssueAction } from '../../../shared/types/ipc.js';
import { notificationCenter } from '../ui/notificationCenter.svelte';
import { t } from '../i18n';

export interface PlaylistInfo {
  id: string;
  title: string;
  subtitle: string;
  icon: string;
  gradient: string;
  bannerColor?: string;
  songCount: number;
  gameId?: string;
}

class LibraryStore {
  songs = $state<MidiFile[]>([]);
  songsByPlaylist = $state<Record<string, MidiFile[]>>({});
  selectedPlaylistId = $state<string>(DEFAULT_UI_PREFERENCES.selectedPlaylistId);
  isScanning = $state<boolean>(false);
  playlists = $state<PlaylistInfo[]>([]);
  fileIssues = $state<LibraryFileIssue[]>([]);
  hasFileIssues = $derived(this.fileIssues.length > 0);
  onSongsUnavailable?: (ids: string[]) => Promise<void>;

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

      const mapped: PlaylistInfo[] = fetchedPlaylists.map((playlist) => ({
        id: playlist.id,
        title: playlist.title,
        subtitle: playlist.subtitle || `Playlist • ${playlist.songCount ?? 0} songs`,
        icon: playlist.icon || 'MusicNote2',
        gradient: playlist.gradient || 'linear-gradient(135deg, #1DB954 0%, #0d9488 100%)',
        bannerColor: playlist.bannerColor || 'rgba(29, 185, 84, 0.45)',
        songCount: playlist.songCount ?? 0,
        gameId: playlist.gameId,
      }));
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
          id: 'songs',
          title: 'All Songs',
          subtitle: `All library songs`,
          icon: 'Cd',
          gradient: 'linear-gradient(135deg, #1DB954 0%, #0d9488 100%)',
          bannerColor: 'rgba(29, 185, 84, 0.45)',
          songCount: this.songs.length,
        },
        ...mapped,
      ];

      if (!this.playlists.some((playlist) => playlist.id === this.selectedPlaylistId)) {
        this.selectedPlaylistId = 'songs';
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

  async importFiles(paths?: string[]) {
    try {
      const imported = await api.songs.importFiles(paths);
      if (imported && imported.length > 0) {
        await this.reload();
      }
      return imported;
    } catch (e) {
      console.error('[LibraryState] importFiles error:', e);
      return [];
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

  async createPlaylist(title: string, icon?: string, bannerColor?: string) {
    const pl = await api.playlists.createPlaylist(title, icon, bannerColor);
    await this.reload();
    return pl;
  }

  async deletePlaylist(id: string) {
    const res = await api.playlists.deletePlaylist(id);
    if (this.selectedPlaylistId === id) {
      this.selectedPlaylistId = 'songs';
    }
    await this.reload();
    return res;
  }

  async addToPlaylist(playlistId: string, songId: string) {
    const res = await api.playlists.addToPlaylist(playlistId, songId);
    await this.reload();
    return res;
  }

  async removeFromPlaylist(playlistId: string, songId: string) {
    const res = await api.playlists.removeFromPlaylist(playlistId, songId);
    await this.reload();
    return res;
  }
}

export const libraryStore = new LibraryStore();

if (typeof window !== 'undefined') {
  (window as any).libraryStore = libraryStore;
}
