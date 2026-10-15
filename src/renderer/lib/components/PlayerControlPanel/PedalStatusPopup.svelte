<script lang="ts">
  import { cubicOut, cubicIn } from 'svelte/easing';
  import type { TransitionConfig } from 'svelte/transition';
  import { playbackStore } from '../../state/playbackStore.svelte';
  import { animationStore } from '../../state/animationStore.svelte';
  import { t } from '../../i18n';
  import PedalIcon from '../Common/PedalIcon.svelte';

  interface Props {
    open: boolean;
    onclose?: () => void;
  }

  let { open = false }: Props = $props();

  function pedalPopupEntrance(_node: Element): TransitionConfig {
    if (animationStore.isOff) return { duration: 0 };
    return {
      duration: 240,
      easing: cubicOut,
      css: (t, u) => `
        opacity: ${t};
        transform: translateY(${u * 8}px) scale(${0.85 + 0.15 * t});
        transform-origin: 50% 100%;
      `
    };
  }

  function pedalPopupExit(_node: Element): TransitionConfig {
    if (animationStore.isOff) return { duration: 0 };
    return {
      duration: 170,
      easing: cubicIn,
      css: (t, u) => `
        opacity: ${t};
        transform: translateY(${u * 8}px) scale(${0.85 + 0.15 * t});
        transform-origin: 50% 100%;
      `
    };
  }
</script>

{#if open}
  <div
    class="pedal-popup-wrapper"
    role="tooltip"
    aria-label={t('player.pedals') || 'Pedals'}
  >
    <div
      class="pedal-card"
      in:pedalPopupEntrance
      out:pedalPopupExit
    >
      <PedalIcon
        size={60}
        isSoftActive={playbackStore.pedals.enabled && playbackStore.pedals.unaCorda}
        isSostenutoActive={playbackStore.pedals.enabled && playbackStore.pedals.sostenuto}
        isSustainActive={playbackStore.pedals.enabled && playbackStore.pedals.sustain}
        isActive={playbackStore.pedals.enabled}
      />
      <div class="status-indicator">
        <span class="status-dot" class:active={playbackStore.pedals.enabled}></span>
        <span class="status-text">{t('player.pedals') || 'Pedals'}</span>
      </div>
    </div>
  </div>
{/if}

<style>
  .pedal-popup-wrapper {
    position: absolute;
    bottom: calc(100% + 4px);
    left: 50%;
    transform: translateX(-50%);
    transform-origin: bottom center;
    padding-bottom: 6px;
    z-index: 100;
    pointer-events: none;
  }

  .pedal-card {
    background: var(--bg-card, #202020);
    color: var(--text-primary);
    border-radius: 8px;
    box-shadow: 0 8px 24px rgba(0, 0, 0, 0.55), 0 2px 6px rgba(0, 0, 0, 0.35);
    padding: 16px 18px 12px 18px;
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    border: none;
    user-select: none;
    min-width: 96px;
    box-sizing: border-box;
    transform-origin: 50% 100%;
    will-change: transform, opacity;
  }

  .status-indicator {
    display: flex;
    align-items: center;
    justify-content: center;
    gap: 6px;
    margin-top: 10px;
  }

  .status-dot {
    width: 8px;
    height: 8px;
    border-radius: 50%;
    background: var(--text-tertiary, rgba(255, 255, 255, 0.35));
    transition: background var(--transition-fast, 0.15s ease), box-shadow var(--transition-fast, 0.15s ease);
  }

  .status-dot.active {
    background: var(--accent, #1DB954);
    box-shadow: 0 0 8px var(--accent-glow, rgba(29, 185, 84, 0.6));
  }

  .status-text {
    font-size: 12px;
    color: var(--text-primary);
    opacity: 0.85;
    font-weight: 500;
    line-height: 1;
    letter-spacing: -0.01em;
  }
</style>
