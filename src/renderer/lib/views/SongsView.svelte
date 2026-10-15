<script lang="ts">
  import { onMount } from 'svelte';
  import { playbackStore } from '../state/playbackStore.svelte';
  import { libraryStore } from '../state/libraryStore.svelte';
  import { uiStore } from '../state/uiStore.svelte';
  import { animationStore } from '../state/animationStore.svelte';
  import { notificationCenter } from '../ui/notificationCenter.svelte';
  import { settingsStore } from '../state/settingsStore.svelte';
  import { t } from '../i18n';
  import type { MidiFile } from '$shared/types/midi';
  import FluentIcon from '../components/Common/FluentIcon.svelte';
  import ExpandableSearchBox from '../components/Common/ExpandableSearchBox.svelte';
  import PlayPauseIcon from '../components/Common/PlayPauseIcon.svelte';
  import SongListTable from '../components/Common/SongListTable.svelte';
  import CustomScrollViewer from '../components/Common/CustomScrollViewer.svelte';

  let isInitialMount = $state(true);

  onMount(() => {
    // Keep transition duration 0s for initial frames so the view renders immediately with target color
    let frameId2: number;
    const frameId1 = requestAnimationFrame(() => {
      frameId2 = requestAnimationFrame(() => {
        isInitialMount = false;
      });
    });
    return () => {
      cancelAnimationFrame(frameId1);
      if (frameId2) cancelAnimationFrame(frameId2);
    };
  });

  // Calculate transition duration tied directly to user animation settings (e.g. 0.20s -> 0.60s)
  let gradientTransitionDuration = $derived.by(() => {
    if (isInitialMount || animationStore.isOff) return 0;
    return Math.round(animationStore.timing * 3.0 * 100) / 100;
  });

  let activeBannerColor = $derived(
    currentPlaylist?.bannerColor || 'rgba(29, 185, 84, 0.45)'
  );

  let searchQuery = $state('');
  let selectedSongId = $state<string | null>(null);
  let selectedSongIds = $state<string[]>([]);

  // Filter / Sort flyout state
  let sortMenuOpen = $state(false);

  let sortOptions = $derived([
    { value: 'title', label: t('songs.sortTitle') },
    { value: 'artist', label: t('songs.sortArtist') },
    { value: 'duration', label: t('songs.sortDuration') },
    { value: 'dateAdded', label: t('songs.sortDateAdded') }
  ]);

  let currentSortLabel = $derived(
    sortOptions.find(o => o.value === uiStore.songsSortMode)?.label ?? t('songs.sortTitle')
  );

  let currentPlaylist = $derived(
    libraryStore.playlists.find(p => p.id === libraryStore.selectedPlaylistId) || libraryStore.playlists[0]
  );

  let playlistSongs = $derived.by(() => {
    if (!currentPlaylist || currentPlaylist.id === 'songs' || currentPlaylist.id === 'all-songs') {
      return libraryStore.songs;
    }
    return libraryStore.songsByPlaylist[currentPlaylist.id] ?? [];
  });

  let scrollContainerEl = $state<HTMLDivElement>();

  const timestampCache = new WeakMap<object, number>();
  function getTimestamp(song: MidiFile): number {
    if (song && typeof song === 'object') {
      const cached = timestampCache.get(song);
      if (cached !== undefined) return cached;
      const t = new Date(song.dateAdded || 0).getTime();
      timestampCache.set(song, t);
      return t;
    }
    return new Date(song?.dateAdded || 0).getTime();
  }

  let filteredSongs = $derived(
    playlistSongs
      .filter(song =>
        (song.title || '').toLowerCase().includes(searchQuery.toLowerCase()) ||
        (song.artist || '').toLowerCase().includes(searchQuery.toLowerCase()) ||
        ((song as any).album && (song as any).album.toLowerCase().includes(searchQuery.toLowerCase()))
      )
      .sort((a, b) => {
        if (uiStore.songsSortMode === 'title') return a.title.localeCompare(b.title);
        if (uiStore.songsSortMode === 'artist') return a.artist.localeCompare(b.artist);
        if (uiStore.songsSortMode === 'duration') return b.durationSeconds - a.durationSeconds;
        if (uiStore.songsSortMode === 'dateAdded') {
          return getTimestamp(b) - getTimestamp(a);
        }
        return 0;
      })
  );

  // Formatted duration for hero metadata (e.g. "31 hr 49 min" or "48 min")
  let totalDurationFormatted = $derived.by(() => {
    const totalSec = filteredSongs.reduce((acc, s) => {
      return acc + (s.durationSeconds ?? (s.durationMs ? s.durationMs / 1000 : 0));
    }, 0);
    const hours = Math.floor(totalSec / 3600);
    const minutes = Math.floor((totalSec % 3600) / 60);
    if (hours > 0) {
      return `${hours} hr ${minutes} min`;
    }
    return `${minutes} min`;
  });

  async function importMidi() {
    try {
      const imported = await libraryStore.importFiles();
      if (imported && imported.length > 0) {
        notificationCenter.success(
          t('songs.importMidiTitle'),
          t('songs.importSuccessMsg', { count: imported.length })
        );
      }
    } catch (e) {
      notificationCenter.error(t('common.error'), String(e));
    }
  }

  let isScanning = $state(false);

  async function scanFolder() {
    if (isScanning) return;
    isScanning = true;
    notificationCenter.info(t('songs.scanningTitle'), t('songs.scanningMsg'));
    try {
      await libraryStore.scanFolder(settingsStore.settings.midiFolder);
      notificationCenter.success(
        t('songs.scanCompletedTitle'),
        t('songs.scanCompletedMsg', { count: libraryStore.songs.length })
      );
    } catch (e) {
      notificationCenter.error(t('common.error'), String(e));
    } finally {
      isScanning = false;
    }
  }

  async function handleFileDrop(e: DragEvent) {
    e.preventDefault();
    e.stopPropagation();
    if (!e.dataTransfer?.files || e.dataTransfer.files.length === 0) return;

    const paths: string[] = [];
    for (let i = 0; i < e.dataTransfer.files.length; i++) {
      const file = e.dataTransfer.files[i] as any;
      if (file.path) {
        paths.push(file.path);
      }
    }

    if (paths.length > 0) {
      try {
        const imported = await libraryStore.importFiles(paths);
        if (imported && imported.length > 0) {
          notificationCenter.success(
            t('songs.importMidiTitle'),
            t('songs.importSuccessMsg', { count: imported.length })
          );
        }
      } catch (err) {
        notificationCenter.error(t('common.error'), String(err));
      }
    }
  }

  function handlePlay(song: MidiFile) {
    if (playbackStore.activeSong?.id === song.id) {
      playbackStore.togglePlay();
    } else {
      playbackStore.play(song);
    }
  }

  async function handleDelete(ids: string[]) {
    try {
      await playbackStore.prepareSongRemoval(ids);
      await libraryStore.deleteSongs(ids);
      selectedSongIds = selectedSongIds.filter(id => !ids.includes(id));
      if (selectedSongId && ids.includes(selectedSongId)) selectedSongId = null;
    } catch (error) {
      notificationCenter.error(t('common.error'), String(error));
    }
  }

  function handlePlayAll() {
    if (currentPlaylist) void playbackStore.playPlaylist(filteredSongs, currentPlaylist.id);
  }

  let isPlaylistPlaying = $derived(
    playbackStore.isPlaying && playbackStore.queuePlaylistId === currentPlaylist?.id
      && playlistSongs.some(song => song.id === playbackStore.activeSong?.id)
  );

  function handleViewInStudio(song: MidiFile) {
    playbackStore.loadSong(song);
    uiStore.activeTab = 'studio';
  }

  function handleEdit(song: MidiFile) {
    uiStore.openDialog('edit', song);
  }

  // Track scroll position to transition sticky table header from transparent to solid
  let isScrolled = $state(false);

  function handleViewScroll(e: Event) {
    const el = e.currentTarget as HTMLElement;
    isScrolled = el.scrollTop > 200;
  }
</script>

<svelte:window onclick={() => { sortMenuOpen = false; }} />

<CustomScrollViewer
  class="songs-view {isScrolled ? 'is-scrolled' : ''}"
  contentClass="songs-view-content"
  bind:scrollContainer={scrollContainerEl}
  onscroll={handleViewScroll}
  fadeTop={false}
  fadeBottom={true}
  fadeHeight={36}
  fadeRampDistance={32}
  ondragover={(e) => e.preventDefault()}
  ondrop={handleFileDrop}
>
  <!-- Extended Continuous Header Gradient Backdrop (flows through Hero Banner, Action Bar, and into Table) -->
  <div
    class="header-gradient-backdrop"
    style="--header-gradient-color: {activeBannerColor}; --header-gradient-duration: {gradientTransitionDuration}s;"
    aria-hidden="true"
  ></div>

  <!-- Spotify Hero Banner: Big Text & Artwork, no username -->
  <div class="hero-banner">
    <div
      class="hero-artwork"
      style="background: {currentPlaylist?.gradient || 'linear-gradient(135deg, #1DB954 0%, #0d9488 100%)'};"
    >
      <FluentIcon name={currentPlaylist?.icon || 'Cd'} filled={true} size={56} />
    </div>
    <div class="hero-info">
      <span class="hero-badge">Playlist</span>
      <h1 class="hero-title">{currentPlaylist?.title || t('nav.songs')}</h1>
      <div class="hero-meta">
        <span class="meta-item bold">{filteredSongs.length} {filteredSongs.length === 1 ? 'song' : 'songs'}</span>
        {#if totalDurationFormatted}
          <span class="meta-dot">•</span>
          <span class="meta-item">{totalDurationFormatted}</span>
        {/if}
      </div>
    </div>
  </div>

  <!-- Action Bar: icon-only playback, search & filter buttons -->
  <div class="action-bar" class:sort-menu-open={sortMenuOpen}>
    <div class="action-left">
      <button
        type="button"
        class="play-all-btn"
        onclick={handlePlayAll}
        title={isPlaylistPlaying ? t('player.pause') : 'Play All'}
        aria-label={isPlaylistPlaying ? t('player.pause') : 'Play All'}
      >
        <span class="play-all-disc">
          <PlayPauseIcon playing={isPlaylistPlaying} size={16} />
        </span>
      </button>
    </div>

    <!-- Right: Ghost Import, Ghost Refresh, Ghost Search, Ghost Filter -->
    <div class="action-right" class:expand-right={animationStore.searchExpansionDirection === 'right'}>
      <!-- Import MIDI ghost button -->
      <button class="ghost-icon-btn" onclick={importMidi} title={t('songs.importMidiTooltip')}>
        <FluentIcon name="Add" size={16} />
      </button>

      <!-- Refresh / Scan folder ghost button -->
      <button class="ghost-icon-btn" onclick={scanFolder} title={t('songs.scanFolderTooltip')}>
        <FluentIcon name="ArrowClockwise" loading={isScanning} size={15} />
      </button>

      <ExpandableSearchBox bind:value={searchQuery} placeholder={t('songs.searchPlaceholder')} />

      <!-- Filter / Sort ghost button -->
      <div class="filter-wrap">
        <button
          class="ghost-filter-btn"
          class:is-active={sortMenuOpen}
          onclick={(e) => { e.stopPropagation(); sortMenuOpen = !sortMenuOpen; }}
          title={t('songs.sort')}
        >
          <span class="filter-label">{currentSortLabel}</span>
          <FluentIcon name="List" size={16} />
        </button>

        {#if sortMenuOpen}
          <div class="sort-menu" role="menu">
            <div class="sort-menu-header">{t('songs.sort')}</div>
            {#each sortOptions as opt}
              <button
                class="sort-menu-option"
                class:selected={uiStore.songsSortMode === opt.value}
                onclick={() => { uiStore.songsSortMode = opt.value; sortMenuOpen = false; }}
              >
                <span>{opt.label}</span>
                {#if uiStore.songsSortMode === opt.value}
                  <FluentIcon name="Checkmark" size={14} class="check-icon" />
                {/if}
              </button>
            {/each}
          </div>
        {/if}
      </div>
    </div>
  </div>

  <!-- Shared SongListTable with Spotify-style resizable columns and sticky header -->
  <SongListTable
    songs={filteredSongs}
    embedInScroll={true}
    scrollContainer={scrollContainerEl}
    bind:selectedId={selectedSongId}
    bind:selectedIds={selectedSongIds}
    onplay={handlePlay}
    onviewinstudio={handleViewInStudio}
    onedit={handleEdit}
    ondelete={!currentPlaylist || currentPlaylist.id === 'songs' || currentPlaylist.id === 'all-songs' ? handleDelete : undefined}
  />
</CustomScrollViewer>

<style>
  :global(.songs-view) {
    height: 100%;
    width: 100%;
    position: relative;
    background: var(--surface-color, #121212);
  }

  :global(.songs-view-content) {
    position: relative;
    min-height: 100%;
    display: flex;
    flex-direction: column;
  }

  /* Extended Continuous Header Gradient */
  .header-gradient-backdrop {
    position: absolute;
    top: 0;
    left: 0;
    right: 0;
    height: 285px;
    pointer-events: none;
    z-index: 0;
    background: linear-gradient(180deg, var(--header-gradient-color, rgba(29, 185, 84, 0.45)) 0%, transparent 100%);
    transition: --header-gradient-color var(--header-gradient-duration, 0s) cubic-bezier(0.16, 1, 0.3, 1);
  }

  /* Spotify-style hero banner */
  .hero-banner {
    display: flex;
    align-items: flex-end;
    gap: 24px;
    padding: 28px 24px 20px 24px;
    background: transparent;
    flex-shrink: 0;
    position: relative;
    z-index: 1;
  }

  .hero-artwork {
    width: 140px;
    height: 140px;
    min-width: 140px;
    min-height: 140px;
    border-radius: 6px;
    background: linear-gradient(135deg, #1DB954 0%, #0d9488 100%);
    display: flex;
    align-items: center;
    justify-content: center;
    box-shadow: var(--hero-artwork-shadow, 0 4px 60px rgba(0, 0, 0, 0.5));
    color: #ffffff;
    flex-shrink: 0;
    transition: background var(--transition-normal);
  }

  .hero-info {
    display: flex;
    flex-direction: column;
    justify-content: flex-end;
    min-width: 0;
  }

  .hero-badge {
    font-size: 12px;
    font-weight: 700;
    text-transform: uppercase;
    letter-spacing: 0.08em;
    color: #ffffff;
    opacity: 0.9;
    margin-bottom: 4px;
  }

  .hero-title {
    font-size: clamp(32px, 5.5vw, 68px);
    font-weight: 800;
    letter-spacing: -0.04em;
    line-height: 1.05;
    color: #ffffff;
    margin: 0 0 10px 0;
    user-select: text;
    word-break: break-word;
  }

  .hero-meta {
    display: flex;
    align-items: center;
    gap: 6px;
    font-size: 13.5px;
    color: rgba(255, 255, 255, 0.7);
    font-weight: 500;
  }

  .meta-item.bold {
    font-weight: 600;
    color: #ffffff;
  }

  .meta-dot {
    opacity: 0.6;
  }

  /* Action Bar matching Spotify layout */
  .action-bar {
    container-type: inline-size;
    display: flex;
    align-items: center;
    justify-content: space-between;
    padding: 12px 20px;
    background: transparent;
    flex-shrink: 0;
    gap: 16px;
    position: relative;
    z-index: 1;
  }

  .action-left {
    display: flex;
    align-items: center;
    gap: 12px;
  }

  /* Raise the containing stacking context above the sticky song header too. */
  .action-bar.sort-menu-open {
    z-index: 2147483646; /* TooltipHost uses 2147483647. */
  }

  .action-right {
    display: flex;
    align-items: center;
    gap: 8px;
    position: relative;
  }

  /* Anchor the toolbar at its starting edge so either direction has room to grow. */
  .action-right.expand-right {
    margin-right: auto;
  }

  /* Keep the original 40px toolbar footprint as the icon grows. */
  .play-all-btn {
    display: inline-flex;
    align-items: center;
    justify-content: center;
    width: 40px;
    height: 40px;
    flex-shrink: 0;
    background: transparent;
    border: none;
    color: var(--bg-app);
    cursor: pointer;
    padding: 0;
    border-radius: 50%;
    transition: transform 0.1s ease;
    user-select: none;
  }

  .play-all-disc {
    display: flex;
    align-items: center;
    justify-content: center;
    width: 32px;
    height: 32px;
    border-radius: 50%;
    background: var(--accent-color);
    transition: background var(--transition-fast);
  }

  .play-all-btn:hover .play-all-disc {
    background: var(--text-primary);
  }

  .play-all-btn:focus-visible {
    outline: 2px solid var(--accent-color);
    outline-offset: 3px;
  }

  .play-all-btn:active {
    transform: scale(0.97);
  }

  :global(.songs-view:not(.is-scrolled) .song-list-container.embedded .table-header) {
    background: transparent !important;
  }

  :global(.songs-view.is-scrolled .song-list-container.embedded .table-header) {
    background: var(--surface-color, #121212) !important;
    box-shadow: 0 4px 12px rgba(0, 0, 0, 0.25);
  }

  /* Ghost icon buttons (Search, Filter, Import, Refresh) */
  .ghost-icon-btn {
    width: 32px;
    height: 32px;
    border-radius: 4px;
    background: transparent;
    border: none;
    color: var(--text-primary);
    opacity: 0.65;
    display: flex;
    align-items: center;
    justify-content: center;
    cursor: pointer;
    padding: 0;
    transition: opacity 0.18s ease, color 0.18s ease;
  }

  .ghost-icon-btn:hover {
    opacity: 1;
    color: var(--text-primary);
  }

  .ghost-icon-btn:active {
    opacity: 0.75;
  }

  /* Ghost Filter / Sort button */
  .filter-wrap {
    position: relative;
  }

  .ghost-filter-btn {
    display: flex;
    align-items: center;
    gap: 8px;
    height: 32px;
    padding: 0 8px;
    background: transparent;
    border: none;
    color: var(--text-primary);
    opacity: 0.6;
    cursor: pointer;
    border-radius: 4px;
    font-size: 12.5px;
    font-weight: 500;
    font-family: inherit;
    transition: opacity 0.18s ease, color 0.18s ease;
    user-select: none;
  }

  .ghost-filter-btn:hover {
    opacity: 1;
    color: var(--text-primary);
  }

  .ghost-filter-btn.is-active {
    opacity: 1;
    color: var(--accent-color, var(--accent, #1DB954));
  }

  .filter-label {
    white-space: nowrap;
  }

  /* Sort dropdown flyout menu */
  .sort-menu {
    position: absolute;
    top: calc(100% + 4px);
    right: 0;
    background: var(--bg-dialog, var(--card-bg, #27272A));
    border: none;
    border-radius: var(--radius-md, 8px);
    box-shadow: var(--popup-shadow);
    z-index: 2147483646;
    min-width: 170px;
    padding: 6px;
    display: flex;
    flex-direction: column;
    gap: 2px;
  }

  .sort-menu-header {
    font-size: 11px;
    font-weight: 700;
    text-transform: uppercase;
    color: var(--text-tertiary);
    padding: 6px 10px 4px 10px;
    letter-spacing: 0.05em;
  }

  .sort-menu-option {
    display: flex;
    align-items: center;
    justify-content: space-between;
    padding: 8px 10px;
    border-radius: 4px;
    background: transparent;
    border: none;
    color: var(--text-primary);
    font-size: 12.5px;
    cursor: pointer;
    text-align: left;
    width: 100%;
    font-family: inherit;
    transition: background 0.12s ease, color 0.12s ease;
  }

  .sort-menu-option:hover {
    background: var(--card-highlight, #3F3F3F);
    color: var(--text-primary);
  }

  .sort-menu-option.selected {
    color: var(--accent-color, #1db954);
    font-weight: 600;
  }

  .sort-menu-option :global(.check-icon) {
    color: var(--accent-color, #1db954);
  }
</style>
