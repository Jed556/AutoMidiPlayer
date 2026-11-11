<script lang="ts">
  import type { HTMLButtonAttributes } from 'svelte/elements';
  import { uiStore } from '../../state/uiStore.svelte';
  import { gameStore } from '../../state/gameStore.svelte';
  import { t } from '../../i18n';
  import AdaptiveText from '../Common/AdaptiveText.svelte';
  import GameIcon from '../Common/GameIcon.svelte';

  interface Props extends HTMLButtonAttributes {
    variant?: 'collapsed' | 'expanded';
    GrayscaleInactiveGame?: boolean;
    grayscaleInactiveGame?: boolean;
  }

  let {
    variant = 'collapsed',
    class: className = '',
    onclick,
    title,
    GrayscaleInactiveGame = true,
    grayscaleInactiveGame = undefined,
    ...restProps
  }: Props = $props();

  let shouldGrayscale = $derived(
    grayscaleInactiveGame !== undefined
      ? grayscaleInactiveGame
      : (GrayscaleInactiveGame ?? true)
  );

  function handleClick(e: MouseEvent) {
    if (onclick) {
      (onclick as (e: MouseEvent) => void)(e);
    } else {
      uiStore.gameSelectorAnchor = null;
      uiStore.gameSelectorOpen = !uiStore.gameSelectorOpen;
    }
  }

  let defaultTitle = $derived(
    gameStore.gameDeletionTooltip ||
    (variant === 'expanded'
      ? `Active Game: ${gameStore.activeGame?.displayName || gameStore.activeGame?.name || 'Game'}`
      : t('nav.activeGameTooltip', { game: gameStore.activeGame?.displayName || gameStore.activeGame?.name || 'Game' }))
  );

  let currentDisplayName = $derived(
    gameStore.activeGame?.displayName || gameStore.activeGame?.name || 'Game'
  );
</script>

{#if variant === 'collapsed'}
  <button
    type="button"
    class="game-drawer-item {className}"
    class:active={uiStore.gameSelectorOpen}
    onclick={handleClick}
    title={title ?? defaultTitle}
    data-tooltip-position="right"
    data-tooltip-follow="false"
    {...restProps}
  >
    <div class="game-image-host" class:running={gameStore.activeGame?.isRunning}>
      <GameIcon size="100%" borderRadius="8px" placeholderSize={18} GrayscaleInactiveGame={shouldGrayscale} />
    </div>
  </button>
{:else}
  <button
    type="button"
    class="expanded-bottom-item {className}"
    class:active={uiStore.gameSelectorOpen}
    onclick={handleClick}
    title={title ?? defaultTitle}
    data-tooltip-disabled={gameStore.gameDeletionTooltip ? undefined : 'true'}
    data-tooltip-follow="false"
    {...restProps}
  >
    <div class="game-bottom-icon-host">
      <GameIcon size="36px" borderRadius="6px" placeholderSize={18} GrayscaleInactiveGame={shouldGrayscale} />
      {#if gameStore.activeGame?.isRunning}
        <div class="running-indicator-dot"></div>
      {/if}
    </div>

    <div class="bottom-item-info">
      <div class="bottom-item-title">
        <AdaptiveText
          text={currentDisplayName}
          compactText={gameStore.activeGame?.shortName}
        />
      </div>
      <div class="bottom-item-sub">
        {gameStore.activeGame?.isMissing ? 'Entry Deleted' : gameStore.activeGame?.isRunning ? 'Running' : 'Target Game'}
      </div>
    </div>
  </button>
{/if}

<style>
  /* COLLAPSED BUTTON */
  .game-drawer-item {
    width: 60px;
    height: 60px;
    min-width: 60px;
    min-height: 60px;
    flex-shrink: 0;
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
    position: relative;
    width: 100%;
    height: 100%;
    border-radius: 8px;
    overflow: hidden;
    transition: transform var(--transition-fast);
  }

  .game-drawer-item:hover .game-image-host {
    transform: scale(1.08);
  }

  /* EXPANDED BUTTON */
  .expanded-bottom-item {
    display: flex;
    align-items: center;
    gap: 12px;
    padding: 6px 8px;
    border-radius: 8px;
    background: transparent;
    border: none;
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

  .running-indicator-dot {
    position: absolute;
    bottom: 2px;
    right: 2px;
    width: 8px;
    height: 8px;
    border-radius: 50%;
    background: var(--status-success);
    border: 1.5px solid var(--bg-app, #000000);
    z-index: 4;
  }

  .bottom-item-info {
    display: flex;
    flex-direction: column;
    min-width: 0;
    flex: 1;
    overflow: hidden;
  }

  .bottom-item-title {
    font-size: 13px;
    font-weight: 500;
    color: var(--text-primary);
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
    line-height: 1.3;
  }

  .bottom-item-sub {
    font-size: 11px;
    color: var(--text-tertiary);
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
    line-height: 1.2;
  }
</style>
