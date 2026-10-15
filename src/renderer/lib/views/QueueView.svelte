<script lang="ts">
  import { playbackStore } from '../state/playbackStore.svelte';
  import { uiStore } from '../state/uiStore.svelte';
  import { notificationCenter } from '../ui/notificationCenter.svelte';
  import { t } from '../i18n';
  import type { MidiFile } from '$shared/types/midi';
  import FluentIcon from '../components/Common/FluentIcon.svelte';
  import SongListTable from '../components/Common/SongListTable.svelte';
  import SearchBox from '../components/Common/SearchBox.svelte';

  let filterText = $state('');
  let selectedSongId = $state<string | null>(null);
  let selectedSongIds = $state<string[]>([]);
  let isFiltered = $derived(filterText.trim().length > 0);

  let filteredQueue = $derived(
    playbackStore.queue.filter(song =>
      (song.title || '').toLowerCase().includes(filterText.toLowerCase()) ||
      (song.artist || '').toLowerCase().includes(filterText.toLowerCase()) ||
      ((song as any).album && (song as any).album.toLowerCase().includes(filterText.toLowerCase()))
    )
  );

  function playQueueItem(song: MidiFile, index: number) {
    if (playbackStore.activeSong?.id === song.id) {
      playbackStore.togglePlay();
      return;
    }
    playbackStore.play(song);
  }

  function removeMultipleFromQueue(ids: string[]) {
    playbackStore.removeMultipleFromQueue(ids);
    selectedSongId = null;
    selectedSongIds = [];
    notificationCenter.info(
      t('queue.removedFromQueueTitle'),
      ids.length === 1
        ? t('queue.removedFromQueueMsg')
        : `${ids.length} tracks removed from queue.`
    );
  }

  function removeSelected() {
    const ids = selectedSongIds.length > 0
      ? [...selectedSongIds]
      : (selectedSongId ? [selectedSongId] : []);
    if (ids.length === 0) return;
    removeMultipleFromQueue(ids);
  }

  function removeFromQueue(index: number) {
    const song = filteredQueue[index];
    if (song) playbackStore.removeMultipleFromQueue([song.id]);
    notificationCenter.info(t('queue.removedFromQueueTitle'), t('queue.removedFromQueueMsg'));
  }

  function clearQueue() {
    playbackStore.clearQueue();
    selectedSongId = null;
    selectedSongIds = [];
    notificationCenter.info(t('queue.queueClearedTitle'), t('queue.queueClearedMsg'));
  }

  function handleViewInStudio(song: MidiFile) {
    playbackStore.loadSong(song);
    uiStore.activeTab = 'studio';
  }

  function handleEdit(song: MidiFile) {
    uiStore.openDialog('edit', song);
  }
</script>

<div class="queue-view">
  <!-- Top Bar: Filter and Actions matching WPF Queue.xaml -->
  <div class="queue-top-bar">
    <div class="filter-group">
      <SearchBox
        placeholder={t('queue.filterPlaceholder')}
        bind:value={filterText}
        width="250px"
      />

      <span class="queue-count-label">
        {t('queue.inQueue', { count: playbackStore.queue.length })}
      </span>
    </div>

    <div class="actions-group">
      <!-- Remove selected items -->
      <button
        class="action-btn"
        onclick={removeSelected}
        disabled={!selectedSongId && selectedSongIds.length === 0}
        title={t('queue.removeSelectedTooltip')}
      >
        <FluentIcon name="DismissCircle" size={16} />
      </button>

      <!-- Clear queue button -->
      <button
        class="action-btn danger-btn"
        onclick={clearQueue}
        disabled={playbackStore.queue.length === 0}
        title={t('queue.clearQueueTooltip')}
      >
        <FluentIcon name="Delete" size={16} />
      </button>
    </div>
  </div>

  <!-- Shared SongListTable in Queue mode -->
  <SongListTable
    songs={filteredQueue}
    isQueue={true}
    canReorder={!isFiltered}
    bind:selectedId={selectedSongId}
    bind:selectedIds={selectedSongIds}
    onplay={playQueueItem}
    onremove={removeFromQueue}
    onremoveselected={removeMultipleFromQueue}
    onviewinstudio={handleViewInStudio}
    onedit={handleEdit}
    onreorder={(newOrder) => {
      if (isFiltered) return;
      playbackStore.reorderQueue(newOrder);
    }}
  />
</div>

<style>
  .queue-view {
    display: flex;
    flex-direction: column;
    height: 100%;
    width: 100%;
    overflow: hidden;
    padding-top: 8px;
  }

  /* Top Bar matching WPF Queue.xaml */
  .queue-top-bar {
    display: flex;
    align-items: center;
    justify-content: space-between;
    padding: 0 16px 8px 16px;
    gap: 12px;
    flex-shrink: 0;
  }

  .filter-group {
    display: flex;
    align-items: center;
    gap: 6px;
  }

  .queue-count-label {
    font-size: 12px;
    color: var(--text-primary);
    opacity: 0.7;
    margin-left: 6px;
    white-space: nowrap;
  }

  .actions-group {
    display: flex;
    align-items: center;
    gap: 4px;
  }

  .action-btn {
    width: 32px;
    height: 32px;
    border-radius: 4px;
    background: transparent;
    border: none;
    color: var(--text-primary);
    opacity: 0.4;
    display: flex;
    align-items: center;
    justify-content: center;
    cursor: pointer;
    padding: 0;
    transition: opacity var(--transition-normal, 0.22s cubic-bezier(0, 0, 0.2, 1));
  }

  .action-btn:hover:not(:disabled) {
    background: transparent;
    opacity: 1;
  }

  .action-btn:active:not(:disabled) {
    opacity: 0.75;
  }

  .action-btn:disabled {
    opacity: 0.2;
    cursor: not-allowed;
  }

  .danger-btn {
    color: #C42B1C;
    opacity: 0.8;
  }

  .danger-btn:hover:not(:disabled) {
    background: transparent;
    opacity: 1;
    color: #C42B1C;
  }
</style>
