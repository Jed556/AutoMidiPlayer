<script lang="ts">
  import { libraryStore } from '../state/libraryStore.svelte';
  import { api } from '../backend/index.js';
  import { notificationCenter } from '../ui/notificationCenter.svelte';
  import { t } from '../i18n';
  import type { OnlineMidiItem } from '$shared/types/onlineMidi';
  import FluentIcon from '../components/Common/FluentIcon.svelte';
  import Button from '../components/Common/Button.svelte';
  import EqualizerControl from '../components/Common/EqualizerControl.svelte';
  import SearchBox from '../components/Common/SearchBox.svelte';
  import Dropdown, { type DropdownOption } from '../components/Common/Dropdown.svelte';
  import CustomScrollViewer from '../components/Common/CustomScrollViewer.svelte';

  let searchQuery = $state('');
  let sortType = $state<'default' | 'popularity' | 'downloads' | 'title' | 'duration' | 'bpm'>('default');
  let previewingId: string | null = $state(null);
  let downloadingIds = $state(new Set<string>());
  let downloadedIds = $state(new Set<string>());
  let onlineItems = $state<OnlineMidiItem[]>([]);
  let isLoading = $state(false);

  const sortOptions = $derived<DropdownOption[]>([
    { value: 'default', label: t('discover.sortDefault'), icon: 'ArrowSort' },
    { value: 'popularity', label: t('discover.sortPopularity'), icon: 'Eye' },
    { value: 'downloads', label: t('discover.sortDownloads'), icon: 'ArrowDownload' },
    { value: 'title', label: t('discover.sortTitle'), icon: 'MusicNote2' },
    { value: 'duration', label: t('discover.sortDuration'), icon: 'Clock' },
    { value: 'bpm', label: t('discover.sortBpm'), icon: 'ArrowSort' }
  ]);

  let filteredItems = $derived.by(() => {
    const q = searchQuery.toLowerCase().trim();
    const items = onlineItems.filter(item => {
      if (!q) return true;
      return (
        (item.title?.toLowerCase().includes(q) ?? false) ||
        (item.artist?.toLowerCase().includes(q) ?? false) ||
        (item.uploader?.toLowerCase().includes(q) ?? false) ||
        (item.category?.toLowerCase().includes(q) ?? false)
      );
    });

    return items.slice().sort((a, b) => {
      switch (sortType) {
        case 'popularity':
          return (b.viewCount ?? 0) - (a.viewCount ?? 0);
        case 'downloads':
          return (b.downloadCount ?? 0) - (a.downloadCount ?? 0);
        case 'title':
          return (a.title ?? '').localeCompare(b.title ?? '');
        case 'duration':
          return (b.durationSeconds ?? 0) - (a.durationSeconds ?? 0);
        case 'bpm':
          return (b.bpm ?? 0) - (a.bpm ?? 0);
        case 'default':
        default:
          return 0;
      }
    });
  });

  $effect(() => {
    const query = searchQuery;
    const sort = sortType;
    const timer = setTimeout(async () => {
      isLoading = true;
      try {
        const result = await api.onlineMidi.search(query, 'MidiShow', undefined, sort);
        onlineItems = result.items;
      } catch (error) {
        console.error('[OnlineMidiView] Search failed:', error);
        onlineItems = [];
      } finally {
        isLoading = false;
      }
    }, query ? 250 : 0);

    return () => clearTimeout(timer);
  });

  function togglePreview(item: OnlineMidiItem) {
    if (previewingId === item.id) {
      previewingId = null;
    } else {
      previewingId = item.id;
      notificationCenter.info(t('discover.previewTitle'), t('discover.previewMsg', { title: item.title }));
    }
  }

  async function downloadMidi(item: OnlineMidiItem) {
    if (downloadedIds.has(item.id)) {
      notificationCenter.info(t('discover.alreadyDownloadedTitle'), t('discover.alreadyDownloadedMsg', { title: item.title }));
      return;
    }

    downloadingIds.add(item.id);
    downloadingIds = new Set(downloadingIds);

    try {
      await api.onlineMidi.downloadSong(item);
      downloadingIds.delete(item.id);
      downloadingIds = new Set(downloadingIds);

      downloadedIds.add(item.id);
      downloadedIds = new Set(downloadedIds);
      await libraryStore.reload();
      notificationCenter.success(t('discover.downloadCompleteTitle'), t('discover.downloadCompleteMsg', { title: item.title }));
    } catch (error) {
      downloadingIds.delete(item.id);
      downloadingIds = new Set(downloadingIds);
      notificationCenter.error('Download unavailable', error instanceof Error ? error.message : 'Could not download this MIDI file.');
    }
  }

  function formatTime(sec: number): string {
    const sVal = Math.max(0, Math.floor(sec || 0));
    const mins = Math.floor(sVal / 60);
    const s = sVal % 60;
    return `${mins}:${s < 10 ? '0' : ''}${s}`;
  }
</script>

<div class="online-midi-view">
  <!-- Search & Filter Header matching WPF OnlineMidi.xaml toolbar -->
  <div class="online-toolbar">
    <div class="toolbar-left">
      <SearchBox
        placeholder={t('discover.searchPlaceholder')}
        bind:value={searchQuery}
        class="online-search-box"
      />

      <Dropdown
        bind:value={sortType}
        options={sortOptions}
        width="170px"
        minWidth="150px"
      />
    </div>

    <!-- MidiShow User Account Status -->
    <div class="user-status-pill" title={t('discover.connected')}>
      <div class="user-avatar">
        <FluentIcon name="Person" size={13} />
      </div>
      <span class="user-name">{t('discover.guestUser')}</span>
    </div>
  </div>

  <!-- Results List matching WPF DiscoveryCard ItemsControl -->
  <CustomScrollViewer
    class="cards-scroll-viewer"
    contentClass="cards-viewport"
    padding="20px 24px 32px 24px"
    fadeHeight={36}
  >
    <div class="discovery-list">
      {#each filteredItems as item (item.id)}
        {@const isPreviewing = previewingId === item.id}
        {@const isDownloading = downloadingIds.has(item.id)}
        {@const isDownloaded = downloadedIds.has(item.id)}

        <div class="discovery-card" class:previewing={isPreviewing}>
          <!-- Left: 50x50 Circular Avatar matching WPF DiscoveryCard -->
          <div class="card-avatar">
            <FluentIcon name="MusicNote2" size={20} class="avatar-icon" />
          </div>

          <!-- Middle: Content & Metadata -->
          <div class="card-details">
            <div class="title-row">
              <span class="card-title" title={item.title}>{item.title}</span>
              {#if item.artist}
                <span class="card-artist">{item.artist}</span>
              {/if}
            </div>

            <!-- Meta Row matching WPF DiscoveryCard MetaContent -->
            <div class="meta-row">
              {#if item.uploader}
                <span class="meta-item" title="Uploader">
                  <FluentIcon name="Person" size={12} class="meta-icon" />
                  <span>{item.uploader}</span>
                </span>
              {/if}

              <span class="meta-item" title="Downloads">
                <FluentIcon name="ArrowDownload" size={12} class="meta-icon" />
                <span>{(item.downloadCount ?? 0).toLocaleString()}</span>
              </span>

              <span class="meta-item" title="Views">
                <FluentIcon name="Eye" size={12} class="meta-icon" />
                <span>{(item.viewCount ?? 0).toLocaleString()}</span>
              </span>

              <span class="meta-item" title="Duration">
                <FluentIcon name="Clock" size={12} class="meta-icon" />
                <span>{formatTime(item.durationSeconds ?? 0)}</span>
              </span>

              {#if item.bpm}
                <span class="meta-item" title="BPM">
                  <span>{item.bpm} BPM</span>
                </span>
              {/if}
            </div>

            <!-- Category & Tag Row matching WPF DiscoveryCard -->
            <div class="tags-row">
              {#if item.category}
                <span class="category-pill">{item.category}</span>
              {/if}
              {#if item.trackCount}
                <span class="subtle-chip">{item.trackCount} Tracks</span>
              {/if}
            </div>
          </div>

          <!-- Right: Actions matching WPF DiscoveryCard buttons -->
          <div class="card-actions">
            <!-- Preview Button (Matches WPF DiscoveryCard.xaml line 471 Variant=Ghost) -->
            <Button
              variant="ghost"
              size="icon"
              active={isPreviewing}
              onclick={() => togglePreview(item)}
              title={isPreviewing ? t('discover.stopPreview') : t('discover.playPreview')}
            >
              {#if isPreviewing}
                <EqualizerControl isPlaying={true} size={15} />
              {:else}
                <FluentIcon name="Play" size={16} />
              {/if}
            </Button>

            <!-- Download Button (Matches WPF DiscoveryCard.xaml line 484 Variant=Ghost ColorMode=Accent) -->
            <Button
              variant="ghost"
              colorMode="accent"
              size="icon"
              onclick={() => downloadMidi(item)}
              disabled={isDownloading}
              title={isDownloaded ? t('discover.added') : t('discover.download')}
            >
              {#if isDownloading}
                <div class="spinner"></div>
              {:else if isDownloaded}
                <FluentIcon name="Checkmark" size={17} class="check-icon" />
              {:else}
                <FluentIcon name="ArrowDownload" size={18} />
              {/if}
            </Button>
          </div>
        </div>
      {/each}

      {#if filteredItems.length === 0}
        <div class="empty-state">
          <FluentIcon name="MusicNote2" size={40} class="empty-icon" />
          <p class="empty-msg">{isLoading ? 'Searching MIDI files…' : 'No MIDI files found matching your search.'}</p>
        </div>
      {/if}
    </div>
  </CustomScrollViewer>
</div>

<style>
  .online-midi-view {
    display: flex;
    flex-direction: column;
    height: 100%;
    width: 100%;
    overflow: hidden;
    background: transparent;
  }

  .online-toolbar {
    display: flex;
    align-items: center;
    justify-content: space-between;
    padding: 14px 24px;
    background: transparent;
    border-bottom: 1px solid var(--border-subtle);
    gap: 16px;
    flex-shrink: 0;
  }

  .toolbar-left {
    display: flex;
    align-items: center;
    gap: 12px;
    flex: 1;
    min-width: 0;
  }

  :global(.online-search-box) {
    max-width: 320px;
    flex: 1;
  }

  .user-status-pill {
    display: flex;
    align-items: center;
    gap: 8px;
    padding: 4px 10px;
    border-radius: 20px;
    background: var(--bg-tertiary);
    border: none;
    font-size: 12px;
    color: var(--text-secondary);
    flex-shrink: 0;
  }

  .user-avatar {
    width: 20px;
    height: 20px;
    border-radius: 50%;
    background: var(--bg-card);
    display: flex;
    align-items: center;
    justify-content: center;
    color: var(--accent);
  }

  :global(.cards-scroll-viewer) {
    flex: 1;
    min-height: 0;
    width: 100%;
  }

  .discovery-list {
    display: flex;
    flex-direction: column;
    gap: 10px;
    max-width: 1040px;
    margin: 0 auto;
  }

  /* WPF DiscoveryCard Base & Hover */
  .discovery-card {
    background: var(--bg-card);
    border: none;
    border-radius: 8px;
    padding: 12px 18px;
    display: flex;
    align-items: center;
    gap: 16px;
    transition: all var(--transition-fast);
    box-shadow: var(--shadow-sm);
  }

  .discovery-card:hover {
    background: var(--bg-hover);
    transform: translateY(-1px);
    box-shadow: var(--shadow-md);
  }

  .discovery-card.previewing {
    background: var(--bg-hover);
    box-shadow: 0 0 12px var(--accent-subtle);
  }

  /* Left 50x50 round avatar */
  .card-avatar {
    width: 48px;
    height: 48px;
    min-width: 48px;
    border-radius: 50%;
    background: var(--bg-tertiary);
    display: flex;
    align-items: center;
    justify-content: center;
    flex-shrink: 0;
  }

  :global(.avatar-icon) {
    color: var(--accent);
    opacity: 0.85;
  }

  /* Middle details */
  .card-details {
    flex: 1;
    min-width: 0;
    display: flex;
    flex-direction: column;
    gap: 4px;
  }

  .title-row {
    display: flex;
    align-items: baseline;
    gap: 8px;
    overflow: hidden;
  }

  .card-title {
    font-size: 14.5px;
    font-weight: 600;
    color: var(--text-primary);
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
  }

  .card-artist {
    font-size: 12px;
    color: var(--text-secondary);
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
  }

  /* Meta row */
  .meta-row {
    display: flex;
    align-items: center;
    gap: 16px;
    flex-wrap: wrap;
    font-size: 11.5px;
    color: var(--text-tertiary);
  }

  .meta-item {
    display: inline-flex;
    align-items: center;
    gap: 4px;
  }

  :global(.meta-icon) {
    opacity: 0.7;
    color: var(--text-secondary);
  }

  /* Tags & Category */
  .tags-row {
    display: flex;
    align-items: center;
    gap: 6px;
    margin-top: 2px;
  }

  .category-pill {
    padding: 2px 9px;
    border-radius: 9px;
    background: var(--accent);
    color: #ffffff;
    font-size: 10.5px;
    font-weight: 600;
    line-height: 1.3;
  }

  .subtle-chip {
    padding: 2px 8px;
    border-radius: 9px;
    background: var(--bg-tertiary);
    color: var(--text-secondary);
    font-size: 10.5px;
    font-weight: 500;
  }

  /* Right Action Buttons matching WPF DiscoveryCard */
  .card-actions {
    display: flex;
    align-items: center;
    gap: 6px;
    flex-shrink: 0;
  }

  :global(.check-icon) {
    color: var(--accent-color);
  }

  .spinner {
    width: 12px;
    height: 12px;
    border: 2px solid rgba(255, 255, 255, 0.3);
    border-top-color: #ffffff;
    border-radius: 50%;
    animation: spin 0.8s linear infinite;
  }

  @keyframes spin {
    to { transform: rotate(360deg); }
  }

  .empty-state {
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    padding: 60px 0;
    gap: 12px;
    color: var(--text-tertiary);
  }

  :global(.empty-icon) {
    opacity: 0.4;
  }

  .empty-msg {
    font-size: 13px;
    margin: 0;
  }
</style>
