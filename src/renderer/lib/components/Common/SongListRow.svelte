<script lang="ts">
  import type { MidiFile } from '$shared/types/midi';
  import { t } from '../../i18n';
  import FluentIcon from './FluentIcon.svelte';
  import PlayPauseIcon from './PlayPauseIcon.svelte';
  import EqualizerControl from './EqualizerControl.svelte';
  import RollingIndex from './RollingIndex.svelte';
  import OverflowText from './OverflowText.svelte';

  interface Props {
    song: MidiFile;
    index: number;
    visIndex: number;
    displayIndex: number;
    isSelected: boolean;
    isCurrentSong: boolean;
    isPlaying: boolean;
    isDragging: boolean;
    isOrigin: boolean;
    showDropBefore: boolean;
    showDropAfter: boolean;
    isMenuOpen: boolean;
    isQueue?: boolean;
    hasStudioOption?: boolean;
    hasEditOption?: boolean;
    onclick: (e: MouseEvent) => void;
    onpointerdown: (e: PointerEvent) => void;
    oncontextmenu: (e: MouseEvent) => void;
    ondblclick: () => void;
    onplayclick: (e: MouseEvent) => void;
    onmenuclick: (e: MouseEvent) => void;
    onplaynext?: () => void;
    onaddtoqueue?: () => void;
    onviewinstudio?: () => void;
    onedit?: () => void;
    onremove?: () => void;
    ondelete?: () => void;
    onclosemenu?: () => void;
  }

  let {
    song,
    index,
    visIndex,
    displayIndex,
    isSelected,
    isCurrentSong,
    isPlaying,
    isDragging,
    isOrigin,
    showDropBefore,
    showDropAfter,
    isMenuOpen,
    isQueue = false,
    hasStudioOption = true,
    hasEditOption = true,
    onclick,
    onpointerdown,
    oncontextmenu,
    ondblclick,
    onplayclick,
    onmenuclick,
    onplaynext,
    onaddtoqueue,
    onviewinstudio,
    onedit,
    onremove,
    ondelete,
    onclosemenu
  }: Props = $props();

  let isActivelyPlaying = $derived(isCurrentSong && isPlaying);

  function formatDuration(sec?: number): string {
    if (sec === undefined || sec === null || isNaN(sec)) return '0:00';
    const sVal = Math.max(0, sec);
    const mins = Math.floor(sVal / 60);
    const s = Math.floor(sVal % 60);
    return `${mins}:${s < 10 ? '0' : ''}${s}`;
  }

  const dateCache = new WeakMap<object, string>();
  function formatDate(isoOrDate?: string | Date): string {
    if (!isoOrDate) return 'Oct 14, 2024';
    if (song && typeof song === 'object') {
      const cached = dateCache.get(song);
      if (cached) return cached;
    }
    const parsed = new Date(isoOrDate);
    if (isNaN(parsed.getTime())) return 'Oct 14, 2024';
    const res = parsed.toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' });
    if (song && typeof song === 'object') {
      dateCache.set(song, res);
    }
    return res;
  }

  const keyCache = new WeakMap<object, string>();
  const NOTES = ['C', 'C♯', 'D', 'D♯', 'E', 'F', 'F♯', 'G', 'G♯', 'A', 'A♯', 'B'];
  function getKeyName(song: MidiFile): string {
    if (song && typeof song === 'object') {
      const cached = keyCache.get(song);
      if (cached) return cached;
    }
    const baseKey = song.baseKey ?? (song as any).baseKeyOffset ?? 0;
    const idx = ((baseKey % 12) + 12) % 12;
    const octave = Math.floor(baseKey / 12) + 3;
    const res = `${NOTES[idx]}${octave}`;
    if (song && typeof song === 'object') {
      keyCache.set(song, res);
    }
    return res;
  }
</script>

<div
  class="table-row"
  data-vis-idx={visIndex}
  class:selected={isSelected}
  class:current={isCurrentSong}
  class:drop-before={showDropBefore}
  class:drop-after={showDropAfter}
  class:is-drag-origin={isOrigin}
  class:is-playing-row={isActivelyPlaying}
  {onclick}
  {onpointerdown}
  {oncontextmenu}
  {ondblclick}
  role="row"
  tabindex="0"
  onkeydown={(e) => {
    if (e.key === 'Enter') ondblclick();
  }}
>
  <!-- # Column: Number, Equalizer when actively playing, or Play/Pause button on hover (CSS-driven) -->
  <div class="col-num">
    <!-- Play button revealed purely via CSS hover -->
    <button
      type="button"
      class="row-play-btn"
      onclick={onplayclick}
      ondblclick={(e) => { e.stopPropagation(); e.preventDefault(); }}
      title={isActivelyPlaying ? t('player.pause') : t('player.play')}
      aria-label={isActivelyPlaying ? t('player.pause') : t('player.play')}
    >
      <PlayPauseIcon playing={isActivelyPlaying} size={12} />
    </button>

    <!-- Normal index or playing equalizer indicator (hidden when row is hovered via CSS) -->
    <div class="col-num-indicator">
      {#if isActivelyPlaying}
        <span class="eq-wrap" aria-label={t('player.playing') || 'Playing'}>
          <EqualizerControl isPlaying={true} size={14} />
        </span>
      {:else}
        <span class="index-display" class:accent={isCurrentSong}>
          {displayIndex}
        </span>
      {/if}
    </div>
  </div>

  <!-- Title Column: 40x40 thumbnail box + Title + Artist stacked -->
  <div class="col-title">
    <div class="art-box">
      {#if song.coverArtUrl}
        <img src={song.coverArtUrl} alt={song.title} class="art-img" draggable="false" loading="lazy" />
      {:else}
        <FluentIcon name="MusicNote2" size={18} class="art-fallback-icon" />
      {/if}
    </div>
    <div class="title-artist-stack">
      <span class="song-title" class:accent={isCurrentSong} title={song.title} data-tooltip-follow="false">
        <OverflowText text={song.title} hoverTarget=".table-row" showTooltip={false} />
      </span>
      <span class="song-artist" title={song.artist || t('player.unknownArtist')} data-tooltip-follow="false">
        <OverflowText text={song.artist || t('player.unknownArtist')} hoverTarget=".table-row" showTooltip={false} />
      </span>
    </div>
  </div>

  <!-- Album -->
  <div class="col-album">
    <OverflowText text={String((song as any).album || t('songs.singles'))} hoverTarget=".table-row" />
  </div>

  <!-- Key -->
  <div class="col-key">
    <OverflowText text={String(getKeyName(song))} hoverTarget=".table-row" />
  </div>

  <!-- BPM -->
  <div class="col-bpm">
    <OverflowText text={String(Math.round(song.bpm || 120))} hoverTarget=".table-row" />
  </div>

  <!-- Transpose -->
  <div class="col-transpose">
    <OverflowText text={String(song.transposeMode || 'Smart')} hoverTarget=".table-row" />
  </div>

  <!-- Date Added -->
  <div class="col-date">
    <OverflowText text={String(formatDate(song.dateAdded))} hoverTarget=".table-row" />
  </div>

  <!-- Duration -->
  <div class="col-duration">
    {formatDuration(song.durationSeconds ?? (song.durationMs ? song.durationMs / 1000 : 0))}
  </div>

  <!-- 3-dot Menu Button & Inline Flyout -->
  <div class="col-menu">
    <button
      type="button"
      class="menu-btn"
      class:visible={isMenuOpen}
      onclick={onmenuclick}
      ondblclick={(e) => { e.stopPropagation(); e.preventDefault(); }}
      title={t('songs.moreOptions')}
    >
      <FluentIcon name="MoreHorizontal" size={16} />
    </button>

    {#if isMenuOpen}
      <div
        class="row-context-menu"
        role="menu"
        tabindex="-1"
        onclick={(e) => e.stopPropagation()}
        onkeydown={(e) => { if (e.key === 'Escape') onclosemenu?.(); }}
      >
        <button
          class="context-item"
          onclick={() => { ondblclick(); onclosemenu?.(); }}
        >
          <FluentIcon name="Play" size={14} />
          <span>{isActivelyPlaying ? t('player.pause') : t('player.play')}</span>
        </button>

        {#if onplaynext}
          <button class="context-item" onclick={() => { onplaynext?.(); onclosemenu?.(); }}>
            <FluentIcon name="Next" size={14} />
            <span>{t('songs.playNext')}</span>
          </button>
        {/if}
        {#if onaddtoqueue}
          <button class="context-item" onclick={() => { onaddtoqueue?.(); onclosemenu?.(); }}>
            <FluentIcon name="TextBulletListSquare" size={14} />
            <span>{t('songs.addToQueue')}</span>
          </button>
        {/if}
        {#if onviewinstudio && hasStudioOption}
          <button class="context-item" onclick={() => { onviewinstudio?.(); onclosemenu?.(); }}>
            <FluentIcon name="CalendarMonth" size={14} />
            <span>{t('songs.viewInStudio')}</span>
          </button>
        {/if}

        {#if onedit && hasEditOption}
          <button
            class="context-item"
            onclick={() => { onedit?.(); onclosemenu?.(); }}
          >
            <FluentIcon name="Edit" size={14} />
            <span>{t('songs.editDetails')}</span>
          </button>
        {/if}
        {#if ondelete}
          <button class="context-item danger" onclick={() => { ondelete?.(); onclosemenu?.(); }}>
            <FluentIcon name="Delete" size={14} />
            <span>{t('songs.deleteFromLibrary')}</span>
          </button>
        {/if}
        {#if isQueue && onremove}
          <button class="context-item danger" onclick={() => { onremove?.(); onclosemenu?.(); }}>
            <FluentIcon name="Delete" size={14} />
            <span>{t('songs.removeFromQueue')}</span>
          </button>
        {/if}
      </div>
    {/if}
  </div>
</div>

<style>
  .table-row {
    display: flex;
    align-items: center;
    height: 56px;
    min-height: 56px;
    border-radius: 4px;
    background: transparent;
    transition: background var(--transition-fast, 0.12s ease),
                opacity 0.28s ease,
                margin-top 0.28s cubic-bezier(0.16, 1, 0.3, 1),
                margin-bottom 0.28s cubic-bezier(0.16, 1, 0.3, 1);
    cursor: default;
    color: var(--text-primary);
    user-select: none;
    -webkit-user-select: none;
    position: relative;
    box-sizing: border-box;
  }

  .table-row:hover {
    background: var(--bg-hover, rgba(255, 255, 255, 0.07));
  }

  .table-row.selected {
    background: var(--card-highlight, rgba(255, 255, 255, 0.14));
  }

  .table-row.selected:hover {
    background: var(--bg-selected-hover, rgba(255, 255, 255, 0.18));
  }

  .table-row.current .song-title {
    color: var(--accent-color, var(--accent, #1DB954));
    font-weight: 600;
  }

  /* When dragging origin row: immediately gone, no moving up or shrinking animation */
  .table-row.is-drag-origin {
    display: none !important;
  }

  /* When hovering slot: allot 58px space */
  .table-row.drop-before {
    margin-top: 58px !important;
  }

  .table-row.drop-after {
    margin-bottom: 58px !important;
  }

  :global(.song-list-container.is-committing) .table-row {
    transition: none !important;
  }

  :global(.song-list-container.is-dragging) .table-row {
    pointer-events: none;
  }

  :global(.song-list-container.is-dragging) .table-row:hover {
    background: transparent !important;
  }

  /* # Column Styling */
  .col-num {
    width: 44px;
    min-width: 44px;
    max-width: 44px;
    flex-shrink: 0;
    display: flex;
    align-items: center;
    justify-content: center;
    font-size: 12px;
    position: relative;
  }

  .col-num-indicator {
    display: flex;
    align-items: center;
    justify-content: center;
    width: 100%;
    height: 100%;
  }

  .index-display {
    color: var(--text-secondary);
    font-size: 12px;
    font-weight: 500;
    font-variant-numeric: tabular-nums;
  }

  .index-display.accent {
    color: var(--accent-color, var(--accent, #1DB954));
    font-weight: 600;
  }

  /* Play button: hidden by default, shown purely via CSS :hover on .table-row */
  .row-play-btn {
    display: none;
    width: 24px;
    height: 24px;
    border-radius: 50%;
    background: transparent;
    border: none;
    color: var(--text-primary);
    cursor: pointer;
    align-items: center;
    justify-content: center;
    padding: 0;
    transition: transform 0.1s ease, color 0.1s ease;
  }

  .row-play-btn:hover {
    transform: scale(1.15);
    color: var(--accent-color, var(--accent, #1DB954));
  }

  .table-row:hover .row-play-btn {
    display: flex;
  }

  .table-row:hover .col-num-indicator {
    display: none;
  }

  .eq-wrap {
    display: flex;
    align-items: center;
    justify-content: center;
  }

  /* Title Column */
  .col-title {
    flex: 1;
    min-width: 140px;
    display: flex;
    align-items: center;
    gap: 12px;
    overflow: hidden;
    position: relative;
    box-sizing: border-box;
    padding-right: 12px;
  }

  .art-box {
    width: 40px;
    height: 40px;
    min-width: 40px;
    min-height: 40px;
    border-radius: 4px;
    background: var(--card-bg, #282828);
    display: flex;
    align-items: center;
    justify-content: center;
    overflow: hidden;
    flex-shrink: 0;
  }

  .art-img {
    width: 100%;
    height: 100%;
    object-fit: cover;
  }

  :global(.art-fallback-icon) {
    opacity: 0.35;
  }

  .title-artist-stack {
    flex: 1;
    display: flex;
    flex-direction: column;
    justify-content: center;
    min-width: 0;
    overflow: hidden;
    gap: 3px;
  }

  .song-title,
  .song-artist {
    width: fit-content;
    max-width: 100%;
  }

  .song-title {
    font-size: 13.5px;
    font-weight: 500;
    color: var(--text-primary);
    white-space: nowrap;
    overflow: hidden;
  }

  .song-artist {
    font-size: 12px;
    color: var(--text-secondary);
    white-space: nowrap;
    overflow: hidden;
  }

  /* Data Columns */
  .col-album {
    width: var(--col-album-w, 130px);
    min-width: var(--col-album-w, 130px);
    max-width: var(--col-album-w, 130px);
    flex-shrink: 0;
    font-size: 12px;
    white-space: nowrap;
    overflow: hidden;
    padding: 0 12px;
    opacity: 0.7;
    position: relative;
    box-sizing: border-box;
  }

  .col-key {
    width: var(--col-key-w, 65px);
    min-width: var(--col-key-w, 65px);
    max-width: var(--col-key-w, 65px);
    flex-shrink: 0;
    font-size: 12px;
    opacity: 0.7;
    white-space: nowrap;
    overflow: hidden;
    padding: 0 12px;
    position: relative;
    box-sizing: border-box;
  }

  .col-bpm {
    width: var(--col-bpm-w, 60px);
    min-width: var(--col-bpm-w, 60px);
    max-width: var(--col-bpm-w, 60px);
    flex-shrink: 0;
    font-size: 12px;
    opacity: 0.6;
    white-space: nowrap;
    overflow: hidden;
    padding: 0 12px;
    position: relative;
    box-sizing: border-box;
  }

  .col-transpose {
    width: var(--col-transpose-w, 80px);
    min-width: var(--col-transpose-w, 80px);
    max-width: var(--col-transpose-w, 80px);
    flex-shrink: 0;
    font-size: 12px;
    opacity: 0.6;
    white-space: nowrap;
    overflow: hidden;
    padding: 0 12px;
    position: relative;
    box-sizing: border-box;
  }

  .col-date {
    width: var(--col-date-w, 105px);
    min-width: var(--col-date-w, 105px);
    max-width: var(--col-date-w, 105px);
    flex-shrink: 0;
    font-size: 12px;
    opacity: 0.6;
    white-space: nowrap;
    overflow: hidden;
    padding: 0 12px;
    position: relative;
    box-sizing: border-box;
  }

  .col-duration {
    width: 60px;
    min-width: 60px;
    max-width: 60px;
    flex-shrink: 0;
    font-size: 12px;
    opacity: 0.6;
    text-align: right;
    display: flex;
    align-items: center;
    justify-content: flex-end;
    position: relative;
    box-sizing: border-box;
    padding: 0 12px;
  }

  .col-menu {
    width: 44px;
    min-width: 44px;
    max-width: 44px;
    flex-shrink: 0;
    display: flex;
    align-items: center;
    justify-content: center;
    position: relative;
  }

  /* 3-dot Menu Button: CSS hover reveals it */
  .menu-btn {
    width: 28px;
    height: 28px;
    border-radius: 4px;
    background: transparent;
    border: none;
    color: var(--text-secondary);
    cursor: pointer;
    display: flex;
    align-items: center;
    justify-content: center;
    opacity: 0;
    transition: opacity 0.15s ease, color 0.15s ease;
  }

  .menu-btn:hover {
    color: var(--text-primary);
  }

  .table-row:hover .menu-btn,
  .menu-btn.visible {
    opacity: 1;
  }

  /* Context Menu Dropdown Flyout */
  .row-context-menu {
    position: absolute;
    top: calc(100% + 4px);
    right: 8px;
    background: var(--bg-dialog, #282828);
    border: 1px solid var(--border-subtle, rgba(255, 255, 255, 0.1));
    border-radius: 6px;
    box-shadow: 0 8px 24px rgba(0, 0, 0, 0.5);
    z-index: 100;
    min-width: 170px;
    padding: 4px;
    display: flex;
    flex-direction: column;
    gap: 2px;
  }

  .context-item {
    display: flex;
    align-items: center;
    gap: 10px;
    padding: 8px 12px;
    border-radius: 4px;
    background: transparent;
    border: none;
    color: var(--text-primary);
    font-size: 12.5px;
    cursor: pointer;
    text-align: left;
    width: 100%;
    transition: background 0.12s ease;
    user-select: none;
  }

  .context-item:hover {
    background: var(--bg-hover, rgba(255, 255, 255, 0.1));
  }

  .context-item.danger {
    color: #ff5252;
  }

  .context-item.danger:hover {
    background: rgba(255, 82, 82, 0.15);
  }
</style>
