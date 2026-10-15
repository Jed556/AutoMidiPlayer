<script lang="ts">
  import { cubicIn } from 'svelte/easing';
  import type { TransitionConfig } from 'svelte/transition';
  import { animationStore } from '../../state/animationStore.svelte';
  import { notificationCenter } from '../../ui/notificationCenter.svelte';
  import FluentIcon from './FluentIcon.svelte';
  import { t } from '../../i18n';

  const iconNames: Record<string, string> = {
    info: 'Info',
    success: 'Checkmark',
    warning: 'Warning',
    error: 'Dismiss'
  };

  let isHovered = $state(false);
  let cardHeights = $state<Record<string, number>>({});

  const toasts = $derived(notificationCenter.toasts || []);
  const reversedToasts = $derived([...toasts].reverse().slice(0, 10));
  const offset = $derived(notificationCenter.toastOffset || {});

  function toCssValue(val: number | string | undefined): string | null {
    if (val === undefined || val === null || val === '') return null;
    return typeof val === 'number' ? `${val}px` : `${val}`;
  }

  const containerStyle = $derived.by(() => {
    const parts: string[] = [];
    const b = toCssValue(offset.bottom);
    if (b) parts.push(`--toast-offset-bottom: ${b}`);

    const r = toCssValue(offset.right);
    if (r) parts.push(`--toast-offset-right: ${r}`);

    const x = toCssValue(offset.x);
    if (x) parts.push(`--toast-offset-x: ${x}`);

    const y = toCssValue(offset.y);
    if (y) parts.push(`--toast-offset-y: ${y}`);

    if (offset.top !== undefined && offset.top !== null) {
      const topVal = toCssValue(offset.top);
      parts.push(`top: calc(${topVal} + var(--toast-offset-y, 0px))`);
      parts.push('bottom: auto');
    }

    if (offset.left !== undefined && offset.left !== null) {
      const leftVal = toCssValue(offset.left);
      parts.push(`left: calc(${leftVal} + var(--toast-offset-x, 0px))`);
      parts.push('right: auto');
    }

    return parts.length > 0 ? parts.join('; ') : undefined;
  });

  // Reset hover state when all toasts clear
  $effect(() => {
    if (toasts.length === 0) {
      isHovered = false;
    }
  });

  // Clean up unused height entries
  $effect(() => {
    const currentIds = new Set(toasts.map(t => t.id));
    for (const id of Object.keys(cardHeights)) {
      if (!currentIds.has(id)) {
        delete cardHeights[id];
      }
    }
  });

  function getCardHeight(id: string): number {
    return cardHeights[id] || 60;
  }

  // Cumulative offsets for expanded stacked view (spacing 8px between cards)
  const expandedOffsets = $derived.by(() => {
    const offsets: number[] = [];
    let acc = 0;
    for (let i = 0; i < reversedToasts.length; i++) {
      offsets.push(acc);
      acc += getCardHeight(reversedToasts[i].id) + 8;
    }
    return offsets;
  });

  const totalExpandedHeight = $derived.by(() => {
    if (reversedToasts.length === 0) return 0;
    const lastIdx = reversedToasts.length - 1;
    return expandedOffsets[lastIdx] + getCardHeight(reversedToasts[lastIdx].id);
  });

  const newestCardId = $derived(reversedToasts[0]?.id);
  const frontCardHeight = $derived(newestCardId ? getCardHeight(newestCardId) : 60);
  const visibleLevels = $derived(Math.min(Math.max(toasts.length - 1, 0), 3));
  const stackedHeight = $derived(toasts.length === 0 ? 0 : (frontCardHeight + visibleLevels * 10));

  const deckHeight = $derived(toasts.length === 0 ? 0 : (isHovered ? totalExpandedHeight : stackedHeight));
  const badgeBottomOffset = $derived(stackedHeight - 9);

  /**
   * Toast Exit transition linked directly to animationStore settings timing.
   * Scales down by 15%, drops down 12px, and fades opacity smoothly to 0.
   */
  function toastExit(node: HTMLElement): TransitionConfig {
    if (animationStore.isOff) {
      return { duration: 0 };
    }
    const duration = Math.round(animationStore.durationMs * 0.85);
    if (duration <= 0) {
      return { duration: 0 };
    }

    node.style.pointerEvents = 'none';
    const baseTransform = node.style.transform || 'translateY(0px) scale(1)';
    const initialOpacity = parseFloat(node.style.opacity) || 1;

    return {
      duration,
      easing: cubicIn,
      tick: (t: number, u: number) => {
        node.style.opacity = `${t * initialOpacity}`;
        node.style.transform = `${baseTransform} scale(${1 - u * 0.15}) translateY(${u * 12}px)`;
      }
    };
  }
</script>

<div class="snackbar-container" role="region" aria-live="polite" style={containerStyle}>
  <!-- Count badge when collapsed and multiple toasts exist -->
  {#if toasts.length > 1}
    <div
      class="stack-badge"
      class:hidden={isHovered}
      style:bottom="{badgeBottomOffset}px"
    >
      <span>{toasts.length}</span>
    </div>
  {/if}

  <div
    class="snackbar-deck"
    role="group"
    aria-label="Notifications"
    class:expanded={isHovered}
    style:height="{deckHeight}px"
    style:pointer-events="{toasts.length === 0 ? 'none' : 'auto'}"
    onmouseenter={() => {
      isHovered = true;
      notificationCenter.pauseAll();
    }}
    onmouseleave={() => {
      isHovered = false;
      notificationCenter.resumeAll();
    }}
  >
    {#each reversedToasts as toast, i (toast.id)}
      {@const levelsBehind = i}
      {@const translateY = isHovered
        ? -expandedOffsets[i]
        : -(Math.min(levelsBehind, 3) * 10)}
      {@const scale = isHovered
        ? 1.0
        : Math.max(0.85, 1.0 - (0.05 * levelsBehind))}
      {@const opacity = isHovered
        ? 1.0
        : (levelsBehind > 3 ? 0 : Math.max(0.4, 1.0 - (0.15 * levelsBehind)))}
      {@const pointerEvents = (isHovered || levelsBehind === 0) ? 'auto' : 'none'}
      {@const zIndex = 100 - levelsBehind}

      <div
        class="snackbar-card {toast.type || 'info'}"
        class:top-card={levelsBehind === 0}
        bind:clientHeight={cardHeights[toast.id]}
        out:toastExit
        style:transform="translateY({translateY}px) scale({scale})"
        style:opacity="{opacity}"
        style:z-index="{zIndex}"
        style:pointer-events="{pointerEvents}"
      >
        <div class="card-main">
          <!-- Standalone 22px Icon without circular border ring -->
          <div class="toast-icon {toast.type || 'info'}">
            <FluentIcon name={iconNames[toast.type] || 'Info'} size={22} />
          </div>

          <!-- Text Stack -->
          <div class="text-stack">
            <div class="primary-text">{toast.title}</div>
            {#if toast.message}
              <div class="secondary-text">{toast.message}</div>
            {/if}
            {#if toast.action}
              <button
                class="action-btn"
                onclick={(e) => {
                  e.stopPropagation();
                  toast.action?.onClick();
                }}
              >
                {toast.action.label}
              </button>
            {/if}
          </div>

          <!-- Close Button -->
          <button
            class="close-btn"
            onclick={(e) => {
              e.stopPropagation();
              notificationCenter.dismiss(toast.id);
            }}
            aria-label="Close"
            title={t('dialogs.dismiss')}
          >
            <FluentIcon name="Dismiss" size={12} />
          </button>
        </div>

        <!-- Shrinking Bottom Progress Bar -->
        <div class="progress-bar-track">
          <div
            class="progress-bar-fill {toast.type || 'info'}"
            style:animation-duration="{(toast.durationMs ?? 4500) / 1000}s"
          ></div>
        </div>
      </div>
    {/each}
  </div>
</div>

<style>
  /* Positioned at bottom-right, firmly above the player control panel */
  .snackbar-container {
    position: fixed;
    bottom: calc(var(--player-panel-height, 90px) + 16px + var(--toast-offset-bottom, 0px) - var(--toast-offset-y, 0px));
    right: calc(16px + var(--toast-offset-right, 0px) - var(--toast-offset-x, 0px));
    z-index: 9999;
    width: 360px;
    max-width: calc(100vw - 32px);
    pointer-events: none;
    user-select: none;
    transition:
      bottom var(--anim-normal, 0.20s) cubic-bezier(0.16, 1, 0.3, 1),
      right var(--anim-normal, 0.20s) cubic-bezier(0.16, 1, 0.3, 1),
      top var(--anim-normal, 0.20s) cubic-bezier(0.16, 1, 0.3, 1),
      left var(--anim-normal, 0.20s) cubic-bezier(0.16, 1, 0.3, 1);
  }

  .stack-badge {
    position: absolute;
    right: 12px;
    background: var(--accent);
    color: white;
    font-size: 11px;
    font-weight: 600;
    min-width: 18px;
    height: 18px;
    border-radius: 9px;
    padding: 0 5px;
    display: flex;
    align-items: center;
    justify-content: center;
    box-shadow: 0 2px 6px rgba(0, 0, 0, 0.4);
    z-index: 1000;
    pointer-events: none;
    transition:
      transform var(--anim-fast, 0.15s) cubic-bezier(0.16, 1, 0.3, 1),
      opacity var(--anim-fast, 0.15s) ease,
      bottom var(--anim-normal, 0.20s) cubic-bezier(0.16, 1, 0.3, 1);
    transform-origin: center center;
  }

  .stack-badge.hidden {
    transform: scale(0);
    opacity: 0;
  }

  /* Deck container: fixed baseline at bottom, expands upward smoothly */
  .snackbar-deck {
    position: relative;
    width: 100%;
    pointer-events: auto;
    transition: height var(--anim-slow, 0.25s) cubic-bezier(0.16, 1, 0.3, 1);
  }

  .snackbar-card {
    position: absolute;
    bottom: 0;
    right: 0;
    width: 100%;
    border-radius: 8px;
    background: var(--bg-dialog, #27272A);
    border: 1px solid var(--border-medium);
    box-shadow: 0 8px 24px rgba(0, 0, 0, 0.45), 0 2px 6px rgba(0, 0, 0, 0.25);
    display: flex;
    flex-direction: column;
    overflow: hidden;
    color: var(--text-primary);
    transform-origin: 50% 100%;
    animation: toastEntrance var(--anim-normal, 0.20s) cubic-bezier(0.16, 1, 0.3, 1);
    transition:
      transform var(--anim-slow, 0.25s) cubic-bezier(0.16, 1, 0.3, 1),
      opacity var(--anim-normal, 0.20s) ease,
      box-shadow var(--anim-normal, 0.20s) ease;
    box-sizing: border-box;
  }

  @keyframes toastEntrance {
    from {
      opacity: 0;
      transform: translateY(12px) scale(0.96);
    }
  }

  /* Zero-delay immediate jumps when animations are disabled in Settings */
  :global(.no-animations) .snackbar-container,
  :global(.no-animations) .snackbar-card,
  :global(.no-animations) .snackbar-deck,
  :global(.no-animations) .stack-badge {
    animation: none !important;
    transition: none !important;
  }

  /* Pause progress bar animations while hovered in expanded deck */
  .snackbar-deck.expanded .progress-bar-fill {
    animation-play-state: paused;
  }

  .card-main {
    display: flex;
    align-items: center;
    padding: 10px 14px;
    gap: 12px;
  }

  /* Standalone Icon without enclosing circular ring */
  .toast-icon {
    width: 24px;
    height: 24px;
    min-width: 24px;
    min-height: 24px;
    display: flex;
    align-items: center;
    justify-content: center;
    flex-shrink: 0;
  }

  .toast-icon.info,
  .toast-icon.success {
    color: #1DB954;
  }

  .toast-icon.warning {
    color: #F59E0B;
  }

  .toast-icon.error {
    color: #EF4444;
  }

  /* Text Stack */
  .text-stack {
    flex: 1;
    min-width: 0;
    display: flex;
    flex-direction: column;
  }

  .primary-text {
    font-size: 13px;
    font-weight: 500;
    line-height: 1.25;
    color: var(--text-primary);
  }

  .secondary-text {
    font-size: 12px;
    color: var(--text-secondary);
    line-height: 1.3;
    margin-top: 3px;
    word-break: break-word;
    white-space: pre-line;
  }

  .action-btn {
    margin-top: 4px;
    font-size: 12px;
    font-weight: 500;
    color: var(--accent);
    background: transparent;
    border: none;
    padding: 0;
    cursor: pointer;
    text-align: left;
    text-decoration: underline;
  }

  /* Close Button */
  .close-btn {
    width: 24px;
    height: 24px;
    min-width: 24px;
    min-height: 24px;
    border-radius: 4px;
    background: transparent;
    border: none;
    color: var(--text-secondary);
    cursor: pointer;
    display: flex;
    align-items: center;
    justify-content: center;
    padding: 0;
    align-self: flex-start;
    margin-top: -2px;
    margin-right: -4px;
    transition: background var(--transition-fast, 0.12s ease), color var(--transition-fast, 0.12s ease);
  }

  .close-btn:hover {
    background: var(--bg-hover, rgba(128, 128, 128, 0.15));
    color: var(--text-primary);
  }

  /* 3px Bottom Progress Bar */
  .progress-bar-track {
    width: 100%;
    height: 3px;
    background: rgba(128, 128, 128, 0.15);
    position: relative;
    overflow: hidden;
  }

  .progress-bar-fill {
    height: 100%;
    width: 100%;
    transform-origin: left;
    animation: shrinkProgress linear forwards;
  }

  .progress-bar-fill.info,
  .progress-bar-fill.success {
    background: #1DB954;
  }

  .progress-bar-fill.warning {
    background: #F59E0B;
  }

  .progress-bar-fill.error {
    background: #EF4444;
  }

  @keyframes shrinkProgress {
    from {
      transform: scaleX(1);
    }
    to {
      transform: scaleX(0);
    }
  }
</style>
