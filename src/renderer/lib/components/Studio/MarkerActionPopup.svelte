<script lang="ts">
  import { onMount, onDestroy, tick, untrack } from 'svelte';
  import { cubicOut, cubicIn } from 'svelte/easing';
  import type { TransitionConfig } from 'svelte/transition';
  import { animationStore } from '../../state/animationStore.svelte';
  import { animateTextSweep } from '../../utils/textSweep';
  import type { KeyChangeMarker } from '$shared/types/midi';
  import Button from '../Common/Button.svelte';
  import { t } from '../../i18n';

  interface Props {
    marker: KeyChangeMarker;
    x: number;
    y: number;
    placement?: 'above' | 'below';
    isSelected?: boolean;
    onDelete: (markerId: string) => void;
    onEdit?: (marker: KeyChangeMarker) => void;
    onClose?: () => void;
    onmouseenter?: () => void;
    onmouseleave?: () => void;
  }

  let {
    marker,
    x,
    y,
    placement = 'below',
    isSelected = false,
    onDelete,
    onEdit,
    onClose,
    onmouseenter,
    onmouseleave
  }: Props = $props();

  let wrapper = $state<HTMLDivElement>();
  let surface = $state<HTMLDivElement>();
  let content = $state<HTMLDivElement>();
  let label = $state<HTMLDivElement>();
  let animations: Animation[] = [];
  let outgoing: HTMLElement | null = null;
  let isMounted = false;
  let lastMarkerId = $state<string | null>(null);
  let lastX = $state<number | null>(null);

  function cancelMorph() {
    animations.forEach(animation => animation.cancel());
    animations = [];
    outgoing?.remove();
    outgoing = null;
  }

  onMount(() => {
    isMounted = true;
  });

  onDestroy(cancelMorph);

  // Resize + Sweep + Move animation matching TooltipHost when moving between close markers
  $effect.pre(() => {
    const nextMarkerId = marker.id;
    const nextX = x;

    if (!isMounted || !wrapper || !surface || !content || !label || lastMarkerId === null || lastX === null) {
      lastMarkerId = nextMarkerId;
      lastX = nextX;
      return;
    }

    if (nextMarkerId !== lastMarkerId || Math.abs(nextX - lastX) > 0.5) {
      const beforeWrapper = wrapper.getBoundingClientRect();
      const beforeSurface = surface.getBoundingClientRect();
      const oldLabel = label.cloneNode(true) as HTMLElement | undefined;
      const oldWidth = label.offsetWidth;
      const isSwitchingMarker = nextMarkerId !== lastMarkerId;

      lastMarkerId = nextMarkerId;
      lastX = nextX;

      untrack(() => {
        void (async () => {
          await tick();
          if (!wrapper || !surface || !content || !label) return;
          cancelMorph();

          const duration = animationStore.durationMs;
          if (
            !duration ||
            document.documentElement.classList.contains('no-animations') ||
            window.matchMedia('(prefers-reduced-motion: reduce)').matches
          ) {
            return;
          }

          const afterWrapper = wrapper.getBoundingClientRect();
          const afterSurface = surface.getBoundingClientRect();
          if (!afterWrapper.width || !afterSurface.width) return;

          const timing: KeyframeAnimationOptions = { duration, easing: 'cubic-bezier(0.16, 1, 0.3, 1)' };

          // 1. MOVE: Move the shared wrapper shell from before to after
          const dx = beforeWrapper.left - afterWrapper.left;
          const dy = beforeWrapper.top - afterWrapper.top;
          if (dx !== 0 || dy !== 0) {
            animations.push(
              wrapper.animate(
                [
                  { translate: `${dx}px ${dy}px` },
                  { translate: '0px 0px' }
                ],
                timing
              )
            );
          }

          // 2. RESIZE: Resize only the surface background, keeping text crisp
          if (
            Math.abs(beforeSurface.width - afterSurface.width) > 1 ||
            Math.abs(beforeSurface.height - afterSurface.height) > 1
          ) {
            animations.push(
              surface.animate(
                [
                  { transform: `scale(${beforeSurface.width / afterSurface.width}, ${beforeSurface.height / afterSurface.height})` },
                  { transform: 'scale(1, 1)' }
                ],
                timing
              )
            );

            // Clip content to the animated surface bounds
            animations.push(
              content.animate(
                [
                  { clipPath: `inset(0px ${afterSurface.width - beforeSurface.width}px ${afterSurface.height - beforeSurface.height}px 0px)` },
                  { clipPath: 'inset(0px 0px 0px 0px)' }
                ],
                timing
              )
            );
          }

          // 3. SWEEP: Diagonal text sweep transition between the old label and new label
          if (isSwitchingMarker && oldLabel) {
            outgoing = oldLabel;
            animations.push(...animateTextSweep(label, oldLabel, content, duration, oldWidth));
            const finished = animations[animations.length - 1];
            void finished.finished
              .then(() => {
                oldLabel.remove();
                if (outgoing === oldLabel) outgoing = null;
              })
              .catch(() => {});
          }
        })();
      });
    }
  });

  function formatTime(ms: number): string {
    const totalSeconds = ms / 1000;
    const minutes = Math.floor(totalSeconds / 60);
    const seconds = totalSeconds % 60;
    const padM = String(minutes).padStart(2, '0');
    const padS = seconds.toFixed(2).padStart(5, '0');
    return `${padM}:${padS}`;
  }

  function popupEntrance(_node: Element): TransitionConfig {
    if (animationStore.isOff) return { duration: 0 };
    const duration = Math.max(140, Math.round(animationStore.durationMs * 1.1));
    const translateYStart = placement === 'above' ? 8 : -8;
    return {
      duration,
      easing: cubicOut,
      css: (t, u) => `
        opacity: ${t};
        transform: translateY(${u * translateYStart}px) scale(${0.85 + 0.15 * t});
        transform-origin: 50% ${placement === 'above' ? '100%' : '0%'};
      `
    };
  }

  function popupExit(_node: Element): TransitionConfig {
    if (animationStore.isOff) return { duration: 0 };
    const duration = Math.max(100, Math.round(animationStore.durationMs * 0.75));
    const translateYStart = placement === 'above' ? 8 : -8;
    return {
      duration,
      easing: cubicIn,
      css: (t, u) => `
        opacity: ${t};
        transform: translateY(${u * translateYStart}px) scale(${0.85 + 0.15 * t});
        transform-origin: 50% ${placement === 'above' ? '100%' : '0%'};
      `
    };
  }
</script>

<!-- svelte-ignore a11y_no_static_element_interactions -->
<div
  bind:this={wrapper}
  class="marker-action-popup"
  class:is-above={placement === 'above'}
  style="left: {x}px; {placement === 'above' ? `bottom: calc(100% + ${Math.abs(y)}px);` : `top: ${y}px;`}"
  onpointerdown={(e) => e.stopPropagation()}
  onmousedown={(e) => e.stopPropagation()}
  {onmouseenter}
  {onmouseleave}
>
  <div
    class="popup-inner"
    in:popupEntrance
    out:popupExit
  >
    <div bind:this={surface} class="popup-surface"></div>

    <div bind:this={content} class="marker-info-content">
      <div bind:this={label} class="marker-info-label">
        <div class="marker-title" title="{marker.noteDisplay || marker.key} ({marker.offsetDisplay})">
          {marker.noteDisplay || marker.key} ({marker.offsetDisplay})
        </div>
        <div class="marker-time">
          {formatTime(marker.timeMs ?? Math.round((marker.time ?? 0) * 1000))}
        </div>
      </div>
    </div>

    <div class="popup-actions">
      <Button
        variant="ghost"
        colorMode="danger"
        icon="Delete"
        iconFilled={true}
        class="action-btn"
        title={t('studio.deleteMarkerTooltip') || 'Delete marker'}
        onclick={() => onDelete(marker.id)}
      />

      <Button
        variant={isSelected ? 'accent' : 'ghost'}
        colorMode={isSelected ? 'accent' : 'default'}
        active={isSelected}
        icon="Edit"
        iconFilled={true}
        class="action-btn"
        title={isSelected ? (t('studio.deselectMarkerTooltip') || 'Deselect marker') : (t('studio.editMarkerTooltip') || 'Edit marker key offset')}
        onclick={() => onEdit?.(marker)}
      />
    </div>
  </div>
</div>

<style>
  .marker-action-popup {
    position: absolute;
    transform: translate(-50%, 0);
    z-index: 100;
    pointer-events: auto;
    padding: 2px 0;
  }

  .popup-inner {
    position: relative;
    width: 90px;
    border-radius: 8px;
    padding: 7px 8px;
    display: flex;
    flex-direction: column;
    align-items: center;
    box-sizing: border-box;
  }

  .popup-surface {
    position: absolute;
    inset: 0;
    border-radius: inherit;
    transform-origin: top left;
    background: var(--bg-surface-elevated, #242428);
    border: none !important;
    outline: none !important;
    box-shadow: 0 8px 24px rgba(0, 0, 0, 0.55), 0 2px 6px rgba(0, 0, 0, 0.35);
    backdrop-filter: blur(12px);
    -webkit-backdrop-filter: blur(12px);
    pointer-events: none;
    z-index: 0;
  }

  .marker-info-content {
    position: relative;
    width: 100%;
    z-index: 1;
    clip-path: inset(0);
    display: flex;
    flex-direction: column;
    align-items: center;
  }

  .marker-info-label {
    display: flex;
    flex-direction: column;
    align-items: center;
    width: 100%;
  }

  .marker-title {
    font-size: 11.5px;
    font-weight: 600;
    color: var(--text-primary, #ffffff);
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
    max-width: 78px;
    text-align: center;
    line-height: 1.2;
  }

  .marker-time {
    font-size: 10.5px;
    color: var(--text-secondary, rgba(255, 255, 255, 0.65));
    margin-top: 2px;
    margin-bottom: 6px;
    font-variant-numeric: tabular-nums;
    text-align: center;
  }

  .popup-actions {
    position: relative;
    z-index: 1;
    display: flex;
    align-items: center;
    gap: 4px;
  }

  .popup-actions :global(.action-btn) {
    width: 26px !important;
    height: 26px !important;
    padding: 0 !important;
    display: flex !important;
    align-items: center !important;
    justify-content: center !important;
    border-radius: 5px !important;
    border: none !important;
    outline: none !important;
  }

  .popup-actions :global(.action-btn:focus),
  .popup-actions :global(.action-btn:focus-visible) {
    outline: none !important;
    box-shadow: none !important;
  }
</style>
