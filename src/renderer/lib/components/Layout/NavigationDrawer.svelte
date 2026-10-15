<script lang="ts">
  import { searchOverflow } from '../../actions/searchOverflow';
  import { bufferedImage } from '../../actions/bufferedImage';
  import { playbackStore } from '../../state/playbackStore.svelte';
  import { uiStore } from '../../state/uiStore.svelte';
  import { gameStore } from '../../state/gameStore.svelte';
  import { libraryStore, type PlaylistInfo } from '../../state/libraryStore.svelte';
  import { t } from '../../i18n';
  import FluentIcon from '../Common/FluentIcon.svelte';
  import AdaptiveText from '../Common/AdaptiveText.svelte';
  import CustomScrollViewer from '../Common/CustomScrollViewer.svelte';

  interface Props {
    isResizing?: boolean;
  }

  let { isResizing = false }: Props = $props();

  let searchQuery = $state('');
  let activeGameImageError = $state(false);

  $effect(() => {
    if (gameStore.activeGame?.id) {
      activeGameImageError = false;
    }
  });

  // Primary Page Navigation Items (6 tabs: tracks, studio, sheet, instrument, queue, discover)
  let navItems = $derived([
    { id: 'tracks', icon: 'AppsListDetail', labelKey: 'nav.tracks' },
    { id: 'studio', icon: 'CalendarMonth', labelKey: 'nav.studio' },
    { id: 'sheet', icon: 'ReadingList', labelKey: 'nav.sheet' },
    { id: 'instrument', icon: 'Guitar', labelKey: 'nav.instrument' },
    { id: 'queue', icon: 'TextBulletListSquare', labelKey: 'nav.queue', badge: true },
    { id: 'discover', icon: 'GlobeSearch', labelKey: 'nav.discover' },
  ]);

  function selectTab(id: string) {
    uiStore.activeTab = id;
  }

  function toggleGameSelector() {
    uiStore.gameSelectorAnchor = null;
    uiStore.gameSelectorOpen = !uiStore.gameSelectorOpen;
  }

  function selectPlaylist(playlist: PlaylistInfo) {
    libraryStore.selectedPlaylistId = playlist.id;
    uiStore.activeTab = 'songs';
  }

  function playPlaylist(e: MouseEvent, playlist: PlaylistInfo) {
    e.stopPropagation();
    libraryStore.selectedPlaylistId = playlist.id;
    uiStore.activeTab = 'songs';
    const songs = playlist.id === 'songs'
      ? libraryStore.songs
      : libraryStore.songsByPlaylist[playlist.id] ?? [];
    if (songs.length > 0) {
      playbackStore.loadSong(songs[0]);
      playbackStore.play();
    }
  }

  let isSettingsActive = $derived((uiStore.activeTab || '').toLowerCase() === 'settings');

  let filteredPlaylists = $derived(
    libraryStore.playlists.filter(pl => {
      if (!searchQuery.trim()) return true;
      const q = searchQuery.toLowerCase();
      return pl.title.toLowerCase().includes(q) || pl.subtitle.toLowerCase().includes(q);
    })
  );

</script>

{#snippet newPlaylistButton()}
  <button
    type="button"
    class="new-playlist-button"
    class:selected={uiStore.activeTab === 'new-playlist'}
    aria-current={uiStore.activeTab === 'new-playlist' ? 'page' : undefined}
    aria-label={t('playlists.newPlaylist')}
    title={t('playlists.newPlaylist')}
    data-tooltip-position="right"
    data-tooltip-follow="false"
    onclick={() => selectTab('new-playlist')}
  >
    <span class="new-playlist-icon"><FluentIcon name="Add" size={uiStore.libraryMode === 'compressed' ? 24 : 18} /></span>
  </button>
{/snippet}

<aside
  class="nav-drawer"
  class:compressed={uiStore.libraryMode === 'compressed'}
  class:expanded={uiStore.libraryMode !== 'compressed'}
  class:two-column={uiStore.libraryMode === 'two-column'}
  class:three-column={uiStore.libraryMode === 'three-column'}
  class:is-resizing={isResizing}
  style="width: {uiStore.libraryWidth}px;"
>
  {#if uiStore.libraryMode === 'compressed'}
    <!-- COMPRESSED MODE (Original 80px Width, Clean Vertical Layout) -->
    <div class="nav-top">
      <CustomScrollViewer
        showScrollbar={false}
        fadeHeight={16}
        contentStyle="align-items: center; padding: 4px 0 0;"
      >
        {#each navItems as item (item.id)}
          {@const isActive = (uiStore.activeTab || '').toLowerCase() === item.id}
          {@const label = t(item.labelKey)}
          <button
            class="nav-item"
            class:active={isActive}
            onclick={() => selectTab(item.id)}
            title={label}
            data-tooltip-position="right"
            data-tooltip-follow="false"
          >
            <div class="item-inner">
              <div class="icon-wrap" class:active={isActive}>
                <FluentIcon
                  name={item.icon}
                  size={isActive ? 28 : 24}
                  filled={isActive}
                />
                {#if item.badge && playbackStore.queue.length > 0}
                  <span class="info-badge">{playbackStore.queue.length}</span>
                {/if}
              </div>

              <div class="content-grid" class:collapsed={isActive}>
                <span class="nav-label">{label}</span>
              </div>
            </div>
          </button>
        {/each}
      </CustomScrollViewer>
    </div>

    <!-- Middle: Scrollable Playlists List (Spotify-style Collapsed Sidebar) -->
    <div class="compressed-playlists-wrapper">
      <CustomScrollViewer
        contentClass="compressed-playlists-scroll"
        contentStyle="align-items: center; padding: 0 0 16px;"
        showScrollbar={false}
        fadeHeight={16}
      >
        {#each libraryStore.playlists as playlist (playlist.id)}
          {@const isSelected = libraryStore.selectedPlaylistId === playlist.id && uiStore.activeTab === 'songs'}
          <button
            type="button"
            class="compressed-playlist-item"
            class:selected={isSelected}
            onclick={() => selectPlaylist(playlist)}
            ondblclick={(e) => playPlaylist(e, playlist)}
            title="{playlist.title} • {playlist.subtitle}"
            aria-label={playlist.title}
            data-tooltip-position="right"
            data-tooltip-follow="false"
          >
            <div class="compressed-thumb" style="background: {playlist.gradient};">
              <FluentIcon name={playlist.icon} size={22} filled={isSelected} />
            </div>
          </button>
        {/each}
        {@render newPlaylistButton()}
      </CustomScrollViewer>
    </div>

    <!-- Bottom Navigation Items (Pinned at the bottom, Fixed) -->
    <div class="nav-bottom">
      <button
        class="game-drawer-item"
        class:active={uiStore.gameSelectorOpen}
        onclick={toggleGameSelector}
        title={gameStore.gameDeletionTooltip || t('nav.activeGameTooltip', { game: gameStore.activeGame?.displayName || gameStore.activeGame?.name || 'Game' })}
        data-tooltip-position="right"
        data-tooltip-follow="false"
      >
        <div class="game-image-host" class:running={gameStore.activeGame?.isRunning}>
          {#if !activeGameImageError && !gameStore.activeGame?.isMissing}
            <div
              role="img"
              aria-label={gameStore.activeGame?.displayName || gameStore.activeGame?.name || 'Game'}
              class="game-image"
              use:bufferedImage={{ src: gameStore.activeGame?.iconUrl,
                fallbackSrc: `images/games/${gameStore.activeGame?.imageFileName || 'Genshin_Impact.png'}`,
                onUnavailable: () => { activeGameImageError = true; } }}
            ></div>
          {:else}
            <div class="game-image-placeholder">
              <FluentIcon name="Games" size={18} />
            </div>
          {/if}
        </div>
      </button>

      <button
        class="nav-item"
        class:active={isSettingsActive}
        onclick={() => selectTab('settings')}
        title={t('nav.settings')}
        data-tooltip-position="right"
        data-tooltip-follow="false"
      >
        <div class="item-inner">
          <div class="icon-wrap" class:active={isSettingsActive}>
            <FluentIcon
              name="Settings"
              size={isSettingsActive ? 28 : 24}
              filled={isSettingsActive}
            />
          </div>
          <div class="content-grid" class:collapsed={isSettingsActive}>
            <span class="nav-label">{t('nav.settings')}</span>
          </div>
        </div>
      </button>
    </div>
  {:else}
    <!-- EXPANDED MODE (2-COLUMN & 3-COLUMN MODES) -->
    <div class="expanded-header">
      <div class="header-title-wrap">
        <FluentIcon name="ReadingList" size={20} class="library-header-icon" />
        <span class="header-title-text">Your Library</span>
      </div>

      <div class="header-controls">
        <!-- Collapse Arrow Button (NO 2-col/3-col chips as requested) -->
        <button
          class="collapse-icon-btn"
          onclick={() => uiStore.setLibraryMode('compressed')}
          title="Collapse to compact sidebar"
          aria-label="Collapse"
          data-tooltip-position="bottom"
        >
          <FluentIcon name="ChevronLeft" size={16} />
        </button>
      </div>
    </div>

    <!-- Page Navigation Section:
         - 2-Column Mode: 1 long row with icon on left and name on right
         - 3-Column Mode: 2 items per row side-by-side
    -->
    <div class="nav-pages-wrapper">
      <CustomScrollViewer showScrollbar={false} fadeHeight={16}>
        <nav class="nav-pages-section" class:two-column={uiStore.libraryMode === 'two-column'} class:three-column={uiStore.libraryMode === 'three-column'} aria-label="Main Navigation" data-tooltip-disabled="true">
          {#each navItems as item (item.id)}
            {@const isActive = (uiStore.activeTab || '').toLowerCase() === item.id}
            {@const label = t(item.labelKey)}
            <button
              class="nav-page-row"
              class:active={isActive}
              onclick={() => selectTab(item.id)}
              title={label}
            >
              <div class="page-row-icon" class:active={isActive}>
                <FluentIcon
                  name={item.icon}
                  size={uiStore.libraryMode === 'three-column' ? 18 : 20}
                  filled={isActive}
                />
              </div>
              <span class="page-row-label">{label}</span>
              {#if item.badge && playbackStore.queue.length > 0}
                <span class="page-queue-badge">{playbackStore.queue.length}</span>
              {/if}
            </button>
          {/each}
        </nav>
      </CustomScrollViewer>
    </div>

    <!-- Playlists Search Header -->
    <div class="playlists-section-header">
      <div class="playlists-search-bar">
        <FluentIcon name="Search" size={14} class="search-ico" />
        <input
          type="text"
          placeholder="Search playlists..."
          bind:value={searchQuery}
          use:searchOverflow={{ value: searchQuery, placeholder: 'Search playlists...' }}
          class="library-search-input"
        />
        {#if searchQuery}
          <button class="clear-query-btn" onclick={() => searchQuery = ''} aria-label="Clear">
            <FluentIcon name="Dismiss" size={12} />
          </button>
        {/if}
      </div>
      {@render newPlaylistButton()}
    </div>

    <!-- Below all this: Playlists / Grid Section with OG Scroll Edge Fade -->
    <div class="library-scroll-wrapper">
      <CustomScrollViewer
        contentClass="library-grid-scroll"
        fadeHeight={32}
      >
        <div class="cards-grid {uiStore.libraryMode}" data-tooltip-disabled="true">
          {#each filteredPlaylists as playlist (playlist.id)}
            {@const isSelected = libraryStore.selectedPlaylistId === playlist.id && uiStore.activeTab === 'songs'}
            <div
              class="playlist-card"
              class:selected={isSelected}
              role="button"
              tabindex="0"
              onclick={() => selectPlaylist(playlist)}
              onkeydown={(e) => { if (e.key === 'Enter' || e.key === ' ') selectPlaylist(playlist); }}
            >
              <!-- Thumbnail Box -->
              <div class="card-thumb" style="background: {playlist.gradient};">
                <FluentIcon name={playlist.icon} size={uiStore.libraryMode === 'three-column' ? 24 : 28} filled={true} />

                <!-- Spotify-Style Hover Play Button -->
                <button
                  type="button"
                  class="hover-play-btn"
                  onclick={(e) => playPlaylist(e, playlist)}
                  title="Play {playlist.title}"
                  data-tooltip-disabled="false"
                >
                  <FluentIcon name="Play" size={uiStore.libraryMode === 'three-column' ? 14 : 16} filled={true} />
                </button>
              </div>

              <!-- Card Metadata -->
              <div class="card-meta">
                <div class="card-title" class:active-text={isSelected}>{playlist.title}</div>
                <div class="card-sub">{playlist.subtitle}</div>
              </div>
            </div>
          {/each}
        </div>
      </CustomScrollViewer>
    </div>

    <!-- Pinned Bottom Controls (Always at the bottom of the drawer) -->
    <div class="expanded-bottom">
      <!-- Active Game Row -->
      <button
        class="expanded-bottom-item"
        class:active={uiStore.gameSelectorOpen}
        onclick={toggleGameSelector}
        title={gameStore.gameDeletionTooltip || `Active Game: ${gameStore.activeGame?.displayName || gameStore.activeGame?.name || 'Game'}`}
        data-tooltip-disabled={gameStore.gameDeletionTooltip ? undefined : 'true'}
        data-tooltip-follow="false"
      >
        <div class="game-bottom-icon-host">
          {#if !activeGameImageError && !gameStore.activeGame?.isMissing}
            <div
              role="img"
              aria-label={gameStore.activeGame?.displayName || gameStore.activeGame?.name || 'Game'}
              class="game-bottom-icon"
              class:running={gameStore.activeGame?.isRunning}
              use:bufferedImage={{ src: gameStore.activeGame?.iconUrl,
                fallbackSrc: `images/games/${gameStore.activeGame?.imageFileName || 'Genshin_Impact.png'}`,
                onUnavailable: () => { activeGameImageError = true; } }}
            ></div>
          {:else}
            <div class="game-bottom-icon-placeholder" class:running={gameStore.activeGame?.isRunning}>
              <FluentIcon name="Games" size={18} />
            </div>
          {/if}
          {#if gameStore.activeGame?.isRunning}
            <div class="running-indicator-dot"></div>
          {/if}
        </div>
        <div class="bottom-item-info">
          <div class="bottom-item-title"><AdaptiveText text={gameStore.activeGame?.displayName || gameStore.activeGame?.name || 'Game'} compactText={gameStore.activeGame?.shortName} /></div>
          <div class="bottom-item-sub">{gameStore.activeGame?.isMissing ? 'Entry Deleted' : gameStore.activeGame?.isRunning ? 'Running' : 'Target Game'}</div>
        </div>
      </button>

      <!-- Settings Row -->
      <button
        data-tooltip-disabled="true"
        class="expanded-bottom-item"
        class:active={isSettingsActive}
        onclick={() => selectTab('settings')}
        title={t('nav.settings')}
      >
        <div class="settings-bottom-icon">
          <FluentIcon name="Settings" size={20} filled={isSettingsActive} />
        </div>
        <div class="bottom-item-info">
          <div class="bottom-item-title" class:active-text={isSettingsActive}>{t('nav.settings')}</div>
          <div class="bottom-item-sub">Preferences</div>
        </div>
      </button>
    </div>
  {/if}
</aside>

<style>
  .new-playlist-button {
    display: flex;
    align-items: center;
    justify-content: center;
    gap: 8px;
    min-height: 48px;
    margin: 8px 10px;
    width: calc(100% - 20px);
    padding: 10px;
    border: 1px dashed var(--card-bg);
    border-radius: 8px;
    background: transparent;
    color: var(--text-secondary);
    font: inherit;
    cursor: pointer;
    flex-shrink: 0;
    transition: background var(--transition-fast), transform 0.1s ease;
  }

  .compressed .new-playlist-button {
    width: 48px;
    min-height: 48px;
    margin: 8px 6px;
  }

  .new-playlist-button:hover,
  .new-playlist-button.selected {
    background: var(--bg-hover);
    color: var(--accent-color);
  }

  .new-playlist-icon {
    display: flex;
    transition: transform var(--transition-fast);
  }

  .new-playlist-button:hover .new-playlist-icon {
    transform: scale(1.06);
  }

  .new-playlist-button:active {
    transform: scale(0.96);
  }

  .expanded .new-playlist-button {
    width: 30px;
    height: 30px;
    min-height: 30px;
    margin: 0;
    padding: 4px;
    border: none;
  }

  .new-playlist-button:focus-visible {
    outline: 2px solid var(--accent-color);
    outline-offset: 2px;
  }

  .nav-drawer {
    background: var(--surface-color, #121212);
    border-radius: 8px;
    display: flex;
    flex-direction: column;
    user-select: none;
    z-index: 50;
    flex-shrink: 0;
    height: 100%;
    min-height: 0;
    overflow: hidden;
    position: relative;
    transition: width var(--transition-normal, 0.22s cubic-bezier(0, 0, 0.2, 1));
  }

  .nav-drawer.is-resizing {
    transition: none !important;
  }

  /* COMPRESSED MODE (80px width, original comfortable padding) */
  .nav-drawer.compressed {
    justify-content: flex-start;
    padding: 0 10px 8px 10px;
    overflow: hidden;
    display: flex;
    flex-direction: column;
    height: 100%;
    box-sizing: border-box;
  }

  .nav-drawer.compressed::-webkit-scrollbar {
    display: none;
  }

  .nav-top {
    display: flex;
    flex-direction: column;
    align-items: center;
    width: 100%;
    min-height: 0;
    flex: 0 1 400px;
    overflow: hidden;
  }

  .nav-bottom {
    margin-top: auto;
    display: flex;
    flex-direction: column;
    align-items: center;
    width: 100%;
    flex-shrink: 0;
    padding-top: 4px;
  }

  /* Compressed Mode Playlists Scroll Section (Middle Blue Area) */
  .compressed-playlists-wrapper {
    position: relative;
    /* Keep two 66px playlist slots clear of the padding and lower fade. */
    flex: 1 0 148px;
    min-height: 148px;
    display: flex;
    flex-direction: column;
    width: 100%;
    margin: 0 0 4px;
    overflow: hidden;
  }

  .compressed-playlists-scroll {
    flex: 1;
    min-height: 0;
    overflow-y: auto;
    overflow-x: hidden;
    scrollbar-width: none;
    -ms-overflow-style: none;
    display: flex;
    flex-direction: column;
    align-items: center;
    width: 100%;
    padding: 4px 0;
  }

  .compressed-playlists-scroll::-webkit-scrollbar {
    display: none;
  }

  .compressed-playlist-item {
    position: relative;
    width: 60px;
    height: 60px;
    min-width: 60px;
    min-height: 60px;
    aspect-ratio: 1 / 1;
    margin: 2px 0 4px 0;
    padding: 6px;
    border-radius: 8px;
    background: transparent;
    border: none;
    cursor: pointer;
    display: flex;
    align-items: center;
    justify-content: center;
    transition: background var(--transition-fast), transform 0.1s ease;
    outline: none;
    box-sizing: border-box;
    flex-shrink: 0;
  }

  .compressed-playlist-item:hover {
    background: var(--bg-hover);
  }

  .compressed-playlist-item:active {
    transform: scale(0.96);
  }

  .compressed-playlist-item.selected {
    background: var(--card-bg, #1F1F1F);
  }

  .compressed-playlist-item.selected:hover {
    background: var(--card-highlight, #3F3F3F);
  }

  .compressed-thumb {
    width: 46px;
    height: 46px;
    min-width: 46px;
    min-height: 46px;
    aspect-ratio: 1 / 1;
    border-radius: 6px;
    display: flex;
    align-items: center;
    justify-content: center;
    color: #ffffff;
    box-shadow: 0 4px 10px rgba(0, 0, 0, 0.25);
    transition: transform var(--transition-fast);
    overflow: hidden;
  }

  .compressed-playlist-item:hover .compressed-thumb {
    transform: scale(1.06);
  }

  .compressed-playlist-item.selected .compressed-thumb {
    box-shadow: 0 4px 12px rgba(0, 0, 0, 0.4);
  }

  /* SquareLeftFluentNavigationViewItemTemplate */
  .nav-item {
    position: relative;
    width: 60px;
    min-width: 60px;
    min-height: 60px;
    height: 60px;
    flex-shrink: 0;
    aspect-ratio: 1 / 1;
    margin: 2px 0 4px 0;
    padding: 4px;
    border-radius: 8px;
    background: transparent;
    border: none;
    color: var(--text-secondary);
    cursor: pointer;
    display: flex;
    align-items: center;
    justify-content: center;
    transition: background var(--transition-fast), color var(--transition-fast);
    outline: none;
  }

  .nav-item:hover {
    background: var(--bg-hover);
    color: var(--text-primary);
  }

  .nav-item.active {
    background: var(--card-bg, #1F1F1F);
    color: var(--accent);
  }

  .nav-item.active:hover {
    background: var(--card-highlight, #3F3F3F);
    color: var(--accent);
  }

  .item-inner {
    width: 100%;
    height: 100%;
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    position: relative;
  }

  .icon-wrap {
    position: relative;
    display: flex;
    align-items: center;
    justify-content: center;
    color: inherit;
  }

  .content-grid {
    height: 14px;
    opacity: 1;
    overflow: hidden;
    display: flex;
    align-items: flex-start;
    justify-content: center;
    transition: height var(--transition-normal), opacity var(--transition-normal);
  }

  .content-grid.collapsed {
    height: 0;
    opacity: 0;
    pointer-events: none;
  }

  .nav-label {
    font-size: 11px;
    font-weight: 500;
    line-height: 1.1;
    color: inherit;
    margin-top: -2px;
    white-space: nowrap;
  }

  .info-badge {
    position: absolute;
    top: -6px;
    right: -10px;
    background: var(--accent);
    color: white;
    font-size: 9px;
    font-weight: 700;
    border-radius: 10px;
    min-width: 16px;
    height: 16px;
    display: flex;
    align-items: center;
    justify-content: center;
    padding: 0 4px;
    box-shadow: 0 2px 4px rgba(0, 0, 0, 0.3);
  }

  .game-drawer-item {
    width: 60px;
    height: 60px;
    min-width: 60px;
    min-height: 60px;
    aspect-ratio: 1 / 1;
    margin: 3px 0 6px 0;
    padding: 0;
    border-radius: 8px;
    background: transparent;
    border: none;
    cursor: pointer;
    overflow: hidden;
    outline: none;
    display: flex;
    align-items: center;
    justify-content: center;
  }

  .game-image-host {
    width: 100%;
    height: 100%;
    border-radius: 8px;
    overflow: hidden;
    transition: transform var(--transition-fast);
  }

  .game-drawer-item:hover .game-image-host {
    transform: scale(1.08);
  }

  .game-image {
    width: 100%;
    height: 100%;
    object-fit: cover;
    border-radius: 8px;
    filter: grayscale(1);
    transition: filter var(--transition-normal);
  }

  .game-image-host.running .game-image {
    filter: none;
  }

  @media (max-height: 620px) {
    .compressed .nav-item,
    .compressed .game-drawer-item {
      height: 48px;
      min-height: 48px;
      aspect-ratio: auto;
    }

    .compressed .game-image-host {
      width: 48px;
      height: 48px;
    }

    .nav-top {
      flex-basis: 328px;
    }
  }

  /* EXPANDED MODE (2-COLUMN & 3-COLUMN) */
  .expanded-header {
    display: flex;
    align-items: center;
    justify-content: space-between;
    padding: 14px 14px 8px 14px;
    flex-shrink: 0;
  }

  .header-title-wrap {
    display: flex;
    align-items: center;
    gap: 10px;
    color: var(--text-primary);
  }

  :global(.library-header-icon) {
    color: var(--text-primary);
    flex-shrink: 0;
  }

  .header-title-text {
    font-size: 14px;
    font-weight: 700;
    letter-spacing: -0.01em;
    white-space: nowrap;
  }

  .header-controls {
    display: flex;
    align-items: center;
  }

  .collapse-icon-btn {
    width: 28px;
    height: 28px;
    border-radius: 50%;
    background: transparent;
    border: none;
    color: var(--text-secondary);
    display: flex;
    align-items: center;
    justify-content: center;
    cursor: pointer;
    transition: background var(--transition-fast), color var(--transition-fast);
  }

  .collapse-icon-btn:hover {
    background: var(--bg-hover);
    color: var(--text-primary);
  }

  /* Page Navigation Section */
  .nav-pages-wrapper {
    display: flex;
    flex-direction: column;
    flex: 0 1 263px;
    min-height: 150px;
    overflow: hidden;
  }

  .three-column .nav-pages-wrapper {
    flex-basis: 144px;
    min-height: 108px;
  }

  .nav-pages-section {
    padding: 4px 10px 16px;
    flex-shrink: 0;
  }

  /* 2-column mode: 1 long row with icon on left and name on right */
  .nav-pages-section.two-column {
    display: flex;
    flex-direction: column;
    gap: 3px;
  }

  /* 3-column mode: 2 items per row side-by-side */
  .nav-pages-section.three-column {
    display: grid;
    grid-template-columns: repeat(2, 1fr);
    gap: 6px;
  }

  .nav-page-row {
    display: flex;
    align-items: center;
    gap: 12px;
    height: 38px;
    flex-shrink: 0;
    padding: 0 12px;
    border-radius: 6px;
    background: transparent;
    border: none;
    color: var(--text-secondary);
    cursor: pointer;
    text-align: left;
    width: 100%;
    outline: none;
    transition: background var(--transition-fast), color var(--transition-fast);
    min-width: 0;
    box-sizing: border-box;
  }

  .nav-pages-section.three-column .nav-page-row {
    height: 36px;
    padding: 0 10px;
    gap: 8px;
  }

  .nav-page-row:hover {
    background: var(--bg-hover);
    color: var(--text-primary);
  }

  .nav-page-row.active {
    background: var(--card-bg, #1F1F1F);
    color: var(--accent);
    font-weight: 600;
  }

  .nav-page-row.active:hover {
    background: var(--card-highlight, #3F3F3F);
  }

  .page-row-icon {
    display: flex;
    align-items: center;
    justify-content: center;
    color: inherit;
    flex-shrink: 0;
    width: 22px;
  }

  .page-row-label {
    font-size: 13.5px;
    font-weight: 500;
    color: inherit;
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
    flex: 1;
    min-width: 0;
  }

  .nav-pages-section.three-column .page-row-label {
    font-size: 12px;
  }

  .page-queue-badge {
    background: var(--accent);
    color: #ffffff;
    font-size: 10px;
    font-weight: 700;
    border-radius: 10px;
    min-width: 16px;
    height: 16px;
    display: flex;
    align-items: center;
    justify-content: center;
    padding: 0 5px;
    flex-shrink: 0;
    margin-left: auto;
  }

  /* Playlists Section Header & Search */
  .playlists-section-header {
    display: flex;
    align-items: center;
    gap: 6px;
    padding: 4px 10px 8px 10px;
    flex-shrink: 0;
  }

  .playlists-search-bar {
    flex: 1;
    min-width: 0;
    display: flex;
    align-items: center;
    background: rgba(255, 255, 255, 0.07);
    border: none;
    border-radius: 16px;
    height: 30px;
    padding: 0 10px;
    gap: 6px;
    width: 100%;
    box-sizing: border-box;
    transition: background var(--transition-fast);
  }

  .playlists-search-bar:focus-within {
    background: rgba(255, 255, 255, 0.12);
  }

  :global(.light) .playlists-search-bar {
    background: rgba(0, 0, 0, 0.05);
  }

  :global(.light) .playlists-search-bar:focus-within {
    background: rgba(0, 0, 0, 0.08);
  }

  :global(.search-ico) {
    opacity: 0.6;
    flex-shrink: 0;
  }

  .library-search-input {
    min-width: 0;
    padding: 0;
    background: transparent;
    border: none;
    outline: none;
    color: var(--text-primary);
    font-size: 12px;
    font-family: inherit;
    width: 100%;
  }

  .library-search-input::placeholder {
    color: var(--text-tertiary);
  }

  .clear-query-btn {
    flex-shrink: 0;
    background: transparent;
    border: none;
    color: var(--text-secondary);
    cursor: pointer;
    display: flex;
    align-items: center;
    justify-content: center;
    padding: 2px;
  }

  .clear-query-btn:hover {
    color: var(--text-primary);
  }

  /* Playlists Grid Scroll Area with OG-Style Edge Fade */
  .library-scroll-wrapper {
    position: relative;
    flex: 1 1 72px;
    display: flex;
    flex-direction: column;
    min-height: 0;
    overflow: hidden;
  }

  .library-grid-scroll {
    flex: 1;
    overflow-y: auto;
    overflow-x: hidden;
    padding-top: 2px;
    padding-bottom: 12px;
    scrollbar-width: thin;
    scrollbar-color: var(--control-fill-hover) transparent;
  }

  .library-grid-scroll::-webkit-scrollbar {
    width: 6px;
  }

  .library-grid-scroll::-webkit-scrollbar-thumb {
    background: var(--control-fill-hover);
    border-radius: 3px;
  }

  .cards-grid {
    display: grid;
    padding: 0 10px;
  }

  .cards-grid.two-column {
    grid-template-columns: repeat(2, 1fr);
    gap: 10px;
  }

  .cards-grid.three-column {
    grid-template-columns: repeat(3, 1fr);
    gap: 6px;
  }

  .cards-grid.three-column .playlist-card {
    padding: 6px;
  }

  .cards-grid.three-column .hover-play-btn {
    width: 28px;
    height: 28px;
    bottom: 6px;
    right: 6px;
  }

  .cards-grid.three-column .card-title {
    font-size: 12px;
  }

  .cards-grid.three-column .card-sub {
    font-size: 10.5px;
  }

  /* Playlist Card */
  .playlist-card {
    display: flex;
    flex-direction: column;
    padding: 8px;
    border-radius: 8px;
    background: transparent;
    border: none;
    cursor: pointer;
    text-align: left;
    transition: background var(--transition-fast), transform 0.1s ease;
    user-select: none;
    outline: none;
    min-width: 0;
    box-sizing: border-box;
  }

  .playlist-card:hover {
    background: var(--bg-hover);
  }

  .playlist-card:active {
    transform: scale(0.98);
  }

  .playlist-card.selected {
    background: var(--card-bg, #1F1F1F);
  }

  .playlist-card.selected:hover {
    background: var(--card-highlight, #3F3F3F);
  }

  /* Card Thumbnail */
  .card-thumb {
    width: 100%;
    aspect-ratio: 1 / 1;
    border-radius: 6px;
    display: flex;
    align-items: center;
    justify-content: center;
    color: #ffffff;
    position: relative;
    overflow: hidden;
    flex-shrink: 0;
    box-shadow: 0 4px 10px rgba(0, 0, 0, 0.25);
  }

  .card-thumb :global(svg) {
    transition: transform var(--transition-fast);
  }

  .playlist-card:hover .card-thumb :global(svg) {
    transform: scale(1.06);
  }

  /* Spotify-style Floating Green Play Button on Card Hover */
  .hover-play-btn {
    position: absolute;
    bottom: 8px;
    right: 8px;
    width: 34px;
    height: 34px;
    border-radius: 50%;
    background: var(--accent, #1DB954);
    color: #000000;
    border: none;
    display: flex;
    align-items: center;
    justify-content: center;
    cursor: pointer;
    box-shadow: 0 8px 16px rgba(0, 0, 0, 0.4);
    opacity: 0;
    transform: translateY(6px);
    transition: opacity var(--transition-fast), transform var(--transition-fast), background var(--transition-fast);
    padding: 0;
    z-index: 5;
  }

  .hover-play-btn:hover {
    background: #1ed760;
    transform: translateY(0) scale(1.08);
  }

  .playlist-card:hover .hover-play-btn {
    opacity: 1;
    transform: translateY(0);
  }

  /* Card Metadata */
  .card-meta {
    margin-top: 8px;
    min-width: 0;
  }

  .card-title {
    font-size: 13px;
    font-weight: 600;
    color: var(--text-primary);
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
    line-height: 1.25;
  }

  .card-title.active-text {
    color: var(--accent);
  }

  .card-sub {
    font-size: 11px;
    color: var(--text-secondary);
    margin-top: 3px;
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
    line-height: 1.2;
    opacity: 0.8;
  }

  /* PINNED BOTTOM DOCKED CONTROLS IN EXPANDED MODE */
  .expanded-bottom {
    margin-top: auto;
    display: flex;
    flex-direction: column;
    gap: 4px;
    padding: 6px 10px 8px 10px;
    flex-shrink: 0;
    background: var(--surface-color, #121212);
  }

  .expanded-bottom-item {
    display: flex;
    align-items: center;
    gap: 12px;
    padding: 6px 8px;
    border-radius: 8px;
    background: transparent;
    border: none;
    color: var(--text-primary);
    cursor: pointer;
    text-align: left;
    width: 100%;
    transition: background var(--transition-fast);
    outline: none;
  }

  .expanded-bottom-item:hover {
    background: var(--bg-hover);
  }

  .expanded-bottom-item.active {
    background: var(--card-bg, #1F1F1F);
  }

  .game-bottom-icon-host {
    position: relative;
    width: 36px;
    height: 36px;
    min-width: 36px;
    border-radius: 6px;
    overflow: hidden;
    flex-shrink: 0;
  }

  .game-bottom-icon {
    width: 100%;
    height: 100%;
    object-fit: cover;
    filter: grayscale(1);
    transition: filter var(--transition-normal);
  }

  .game-bottom-icon.running {
    filter: none;
  }

  .game-image-placeholder,
  .game-bottom-icon-placeholder {
    width: 100%;
    height: 100%;
    border-radius: inherit;
    background: var(--bg-hover, rgba(255, 255, 255, 0.08));
    display: flex;
    align-items: center;
    justify-content: center;
    color: var(--text-tertiary);
    transition: color var(--transition-normal);
  }

  .game-image-host.running .game-image-placeholder,
  .game-bottom-icon-placeholder.running {
    color: var(--accent);
  }

  .running-indicator-dot {
    position: absolute;
    bottom: 2px;
    right: 2px;
    width: 8px;
    height: 8px;
    border-radius: 50%;
    background: var(--status-success);
    border: 1.5px solid var(--bg-app, #000000);
  }

  .settings-bottom-icon {
    width: 36px;
    height: 36px;
    min-width: 36px;
    border-radius: 6px;
    background: rgba(255, 255, 255, 0.08);
    display: flex;
    align-items: center;
    justify-content: center;
    color: var(--text-secondary);
    flex-shrink: 0;
    transition: color var(--transition-fast), background var(--transition-fast);
  }

  .expanded-bottom-item:hover .settings-bottom-icon {
    color: var(--text-primary);
    background: rgba(255, 255, 255, 0.14);
  }

  .bottom-item-info {
    flex: 1;
    min-width: 0;
  }

  .bottom-item-title {
    font-size: 13px;
    font-weight: 600;
    color: var(--text-primary);
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
  }

  .bottom-item-title.active-text {
    color: var(--accent);
  }

  .bottom-item-sub {
    font-size: 11px;
    color: var(--text-secondary);
    margin-top: 2px;
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
    opacity: 0.8;
  }
</style>
