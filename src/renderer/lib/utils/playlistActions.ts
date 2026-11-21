import { isBuiltInPlaylist, libraryStore, type PlaylistInfo } from '../state/libraryStore.svelte';
import { playbackStore } from '../state/playbackStore.svelte';
import { uiStore } from '../state/uiStore.svelte';
import { notificationCenter } from '../ui/notificationCenter.svelte';
import { t } from '../i18n';
import { normalizeTargetPlaylistId } from './midiImport';

export async function importMidiFilesWithFeedback(
  paths?: string[],
  playlistId?: string | null,
): Promise<number> {
  const targetPlaylistId = normalizeTargetPlaylistId(playlistId);
  const imported = await libraryStore.importFiles(paths, targetPlaylistId);
  if (!imported || imported.length === 0) return 0;

  const targetPlaylist = targetPlaylistId
    ? libraryStore.playlists.find((playlist) => playlist.id === targetPlaylistId) ?? null
    : null;

  notificationCenter.success(
    t('songs.importMidiTitle'),
    targetPlaylist
      ? t('songs.importSuccessPlaylistMsg', { count: imported.length, playlist: targetPlaylist.title })
      : t('songs.importSuccessMsg', { count: imported.length })
  );
  return imported.length;
}

export async function confirmAndDeletePlaylist(playlist: PlaylistInfo): Promise<boolean> {
  if (isBuiltInPlaylist(playlist.id)) return false;

  const outcome = await uiStore.showActionDialog({
    title: t('playlists.deleteConfirmTitle', { title: playlist.title }),
    body: t('playlists.deleteConfirmBody'),
    icon: 'Delete',
    iconColor: '#ff5252',
    showCloseButton: false,
    cancelButton: { text: t('dialogs.cancel'), variant: 'secondary' },
    confirmButton: { text: t('playlists.deletePlaylist'), variant: 'danger' },
  });

  if (outcome !== 'Confirmed') return false;

  try {
    await libraryStore.deletePlaylist(playlist.id);
    if (playbackStore.queuePlaylistId === playlist.id) {
      playbackStore.queuePlaylistId = null;
    }
    notificationCenter.info(
      t('playlists.deletedTitle'),
      t('playlists.deletedMsg', { title: playlist.title })
    );
    return true;
  } catch (error) {
    notificationCenter.error(t('common.error'), String(error));
    return false;
  }
}
