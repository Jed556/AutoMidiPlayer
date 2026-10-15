<script lang="ts">
  import { uiStore } from '../../state/uiStore.svelte';
  import { gameStore } from '../../state/gameStore.svelte';
  import { notificationCenter } from '../../ui/notificationCenter.svelte';
  import { dialogBackdrop, popupScaleIn, popupScaleOut } from '../../utils/transitions';
  import { t } from '../../i18n';
  import FluentIcon from '../Common/FluentIcon.svelte';
  import ExpandableSearchBox from '../Common/ExpandableSearchBox.svelte';
  import CustomScrollViewer from '../Common/CustomScrollViewer.svelte';
  import { createFrameResizeObserver } from '../../utils/frameResizeObserver';

  let searchQuery = $state('');
  let failedImages = $state<Record<string, boolean>>({});

  function positionPopup(node: HTMLDivElement, anchor: HTMLElement | null) {
    function measure() {
      if (!anchor) return;
      const rect = anchor.getBoundingClientRect();
      const left = Math.max(12, Math.min(rect.left + 20, window.innerWidth - 332));
      const top = Math.min(rect.bottom + 12, window.innerHeight - 80);
      node.style.setProperty('--popup-left', `${left}px`);
      node.style.setProperty('--popup-top', `${top}px`);
      node.style.setProperty('--popup-height', `${Math.max(0, window.innerHeight - top - 12)}px`);
    }
    const observer = createFrameResizeObserver(measure);
    if (anchor) observer.observe(anchor);
    window.addEventListener('resize', measure);
    window.addEventListener('scroll', measure, true);
    measure();
    return {
      update(next: HTMLElement | null) {
        observer.disconnect();
        anchor = next;
        if (anchor) observer.observe(anchor);
        measure();
      },
      destroy() {
        observer.disconnect();
        window.removeEventListener('resize', measure);
        window.removeEventListener('scroll', measure, true);
      },
    };
  }

  function handleImageError(gameId: string, e: Event, fallbackFileName?: string) {
    const target = e.currentTarget as HTMLImageElement;
    const fallbackSrc = `images/games/${fallbackFileName || 'Genshin_Impact.png'}`;
    if (target.src !== fallbackSrc && !target.src.endsWith(fallbackSrc)) {
      target.src = fallbackSrc;
    } else {
      failedImages[gameId] = true;
    }
  }

  let filteredGames = $derived(
    gameStore.games.filter(g =>
      (g.displayName || g.name || '').toLowerCase().includes(searchQuery.toLowerCase()) ||
      (g.shortName || '').toLowerCase().includes(searchQuery.toLowerCase())
    )
  );

  function selectGame(gameId: string) {
    gameStore.setActiveGame(gameId);
    uiStore.gameSelectorOpen = false;
    const game = gameStore.games.find(g => g.id === gameId);
    if (game) {
      notificationCenter.info(
        t('gameSelector.gameSetToastTitle', { name: game.displayName || game.name }),
        t('gameSelector.layoutToastMsg', { layout: gameStore.getGameLayout(game)?.name || 'No Layouts' })
      );
    }
  }

  function handleBackdrop(e: MouseEvent) {
    if ((e.target as HTMLElement).classList.contains('popup-backdrop')) {
      uiStore.gameSelectorOpen = false;
    }
  }

  function handleKeydown(e: KeyboardEvent) {
    if (e.key === 'Escape' && uiStore.gameSelectorOpen) {
      uiStore.gameSelectorOpen = false;
    }
  }

</script>

<svelte:window onkeydown={handleKeydown} />

{#if uiStore.gameSelectorOpen}
  <div
    class="popup-backdrop"
    class:anchored={!!uiStore.gameSelectorAnchor}
    use:positionPopup={uiStore.gameSelectorAnchor}
    transition:dialogBackdrop
    onclick={handleBackdrop}
    role="presentation"
  >
    <div
      class="game-popup"
      in:popupScaleIn
      out:popupScaleOut
      role="dialog"
      aria-label={t('gameSelector.selectTargetGame')}
    >
      <div class="popup-header">
        <FluentIcon name="Games" size={16} class="header-icon" />
        <div class="header-content">
          <span class="header-title">{t('gameSelector.selectTargetGame')}</span>
          <div class="header-search">
            <ExpandableSearchBox
              placeholder={t('gameSelector.searchPlaceholder')}
              bind:value={searchQuery}
              expandedWidth="100cqi"
            />
          </div>
        </div>
      </div>

      <CustomScrollViewer padding="4px 8px 8px 8px" gap="2px" fadeHeight={36}>
        {#each filteredGames as game (game.id)}
          {@const isSelected = gameStore.activeGame?.id === game.id}
          {@const instrument = gameStore.getGameInstrument(game)}
          {@const layout = gameStore.getGameLayout(game, instrument)}
          {@const mappingLabel = `${instrument?.name || 'No Instruments'} · ${layout?.name || 'No Layouts'}`}
          <div
            class="game-card"
            class:selected={isSelected}
            onclick={() => selectGame(game.id)}
            onkeydown={(e) => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); selectGame(game.id); } }}
            role="button"
            tabindex="0"
          >
            <div class="game-thumbnail-wrap">
              {#if !failedImages[game.id]}
                <img
                  src={game.iconUrl || `images/games/${game.imageFileName || 'Genshin_Impact.png'}`}
                  alt={game.displayName || game.name}
                  class="game-thumbnail"
                  class:running={game.isRunning}
                  onerror={(e) => handleImageError(game.id, e, game.imageFileName)}
                />
              {:else}
                <div class="game-thumbnail-placeholder" title={game.displayName || game.name}>
                  <FluentIcon name="Games" size={18} />
                </div>
              {/if}
              {#if game.isRunning}
                <div class="running-indicator" title={t('gameSelector.gameProcessRunning')}></div>
              {/if}
            </div>

            <div class="game-info">
              <div class="game-name-row">
                <span class="game-name">{game.displayName || game.name}</span>
                {#if isSelected}
                  <FluentIcon name="Checkmark" size={16} class="selected-check" />
                {/if}
              </div>
              <div class="game-meta">
                <span class="meta-tag">{mappingLabel}</span>
                {#if game.isRunning}
                  <span class="status-tag running">
                    <FluentIcon name="Pulse" size={10} /> {t('gameSelector.active')}
                  </span>
                {/if}
              </div>
            </div>
          </div>
        {/each}
      </CustomScrollViewer>

    </div>
  </div>
{/if}

<style>
  .popup-backdrop {
    position: fixed;
    inset: 0;
    z-index: 900;
    background: rgba(0, 0, 0, 0.4);
    display: flex;
    align-items: flex-end;
    padding-left: calc(var(--sidebar-width, 76px) + 16px);
    padding-bottom: 78px;
    transition: padding-left var(--transition-fast, 0.15s ease);
  }

  .game-popup {
    width: 320px;
    background: var(--bg-dialog, var(--card-bg, #27272A));
    border: none;
    border-radius: var(--radius-lg);
    box-shadow: 0 16px 36px rgba(0, 0, 0, 0.55), 0 4px 12px rgba(0, 0, 0, 0.35);
    overflow: hidden;
    transform-origin: bottom left;
    display: flex;
    flex-direction: column;
    max-height: 480px;
  }

  .popup-backdrop.anchored {
    display: block;
    padding: 0;
  }

  .anchored .game-popup {
    position: absolute;
    left: var(--popup-left);
    top: var(--popup-top);
    width: min(320px, calc(100vw - 24px));
    max-height: min(480px, var(--popup-height));
    transform-origin: top left;
  }

  .popup-header {
    display: flex;
    align-items: center;
    gap: 8px;
    padding: 14px 16px 8px 16px;
    flex-shrink: 0;
  }

  .header-content {
    position: relative;
    flex: 1;
    min-width: 0;
    height: 32px;
    display: flex;
    align-items: center;
    container-type: inline-size;
  }

  .header-title {
    padding-right: 32px;
    font-size: 13px;
    font-weight: 600;
    color: var(--text-primary);
    transition: opacity var(--transition-fast);
  }

  .popup-header:has(:global(.expandable-search.expanded)) .header-title {
    opacity: 0;
  }

  .popup-header :global(.header-icon) {
    color: var(--text-tertiary);
    flex-shrink: 0;
  }

  .header-search {
    position: absolute;
    inset: 0 0 0 auto;
  }

  .game-card {
    display: flex;
    align-items: center;
    gap: 12px;
    padding: 8px 10px;
    border-radius: var(--radius-md);
    cursor: pointer;
    background: transparent;
    border: none;
    transition: background var(--transition-fast, 0.15s ease);
    outline: none;
  }

  .game-card:hover {
    background: var(--bg-hover);
  }

  .game-card.selected {
    background: var(--accent-subtle);
  }

  .game-card.selected:hover {
    background: rgba(29, 185, 84, 0.2);
  }

  .game-card.selected .game-name {
    font-weight: 600;
  }

  .game-thumbnail-wrap {
    position: relative;
    width: 36px;
    height: 36px;
    flex-shrink: 0;
  }

  .game-thumbnail {
    width: 100%;
    height: 100%;
    border-radius: var(--radius-sm);
    object-fit: cover;
    border: none;
    filter: grayscale(1);
  }

  .game-thumbnail.running {
    filter: none;
  }

  .game-thumbnail-placeholder {
    width: 100%;
    height: 100%;
    border-radius: var(--radius-sm);
    background: var(--bg-hover, rgba(255, 255, 255, 0.08));
    display: flex;
    align-items: center;
    justify-content: center;
    color: var(--text-tertiary);
    border: 1px solid var(--border-subtle, rgba(255, 255, 255, 0.08));
    box-sizing: border-box;
  }

  .running-indicator {
    position: absolute;
    bottom: -2px;
    right: -2px;
    width: 9px;
    height: 9px;
    border-radius: 50%;
    background: var(--status-success);
    box-shadow: 0 0 0 2px var(--bg-dialog, #27272A);
  }

  .game-info {
    flex: 1;
    min-width: 0;
  }

  .game-name-row {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 6px;
  }

  .game-name {
    font-size: 12.5px;
    font-weight: 500;
    color: var(--text-primary);
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
  }

  :global(.selected-check) {
    color: var(--accent);
    flex-shrink: 0;
  }

  .game-meta {
    display: flex;
    align-items: center;
    gap: 6px;
    margin-top: 2px;
  }

  .meta-tag {
    font-size: 10.5px;
    color: var(--text-tertiary);
    min-width: 0;
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
  }

  .status-tag {
    font-size: 10px;
    padding: 1px 6px;
    border-radius: 4px;
    display: inline-flex;
    align-items: center;
    gap: 3px;
    flex-shrink: 0;
  }

  .status-tag.running {
    background: rgba(16, 185, 129, 0.15);
    color: var(--status-success);
    font-weight: 500;
  }

</style>
