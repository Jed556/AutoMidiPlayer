<script lang="ts">
  import { onMount, onDestroy, tick, untrack } from 'svelte';
  import { portal } from '../../actions/portal';
  import { tooltipManager } from '../../ui/tooltipManager.svelte';
  import { animationStore } from '../../state/animationStore.svelte';
  import { createFrameResizeObserver } from '../../utils/frameResizeObserver';
  import { animateTextSweep } from '../../utils/textSweep';

  let wrapper = $state<HTMLDivElement>();
  let surface = $state<HTMLDivElement>();
  let content = $state<HTMLDivElement>();
  let label = $state<HTMLSpanElement>();
  let animations: Animation[] = [];
  let outgoing: HTMLElement | null = null;

  function cancelMorph() {
    animations.forEach(animation => animation.cancel());
    animations = [];
    outgoing?.remove();
    outgoing = null;
  }

  onDestroy(cancelMorph);

  $effect.pre(() => {
    const revision = tooltipManager.contentRevision;
    untrack(() => {
      const before = surface?.getBoundingClientRect();
      const oldLabel = label?.cloneNode(true) as HTMLSpanElement | undefined;
      const oldWidth = label?.offsetWidth;
      const morph = tooltipManager.morphContent && tooltipManager.isVisible;

      void (async () => {
        await tick();
        if (revision !== tooltipManager.contentRevision || !wrapper) return;
        tooltipManager.updatePosition();
        await tick();
        if (revision !== tooltipManager.contentRevision || !wrapper || !surface || !content || !label) return;
        cancelMorph();
        const duration = animationStore.durationMs;
        if (!morph || !before || !duration ||
          document.documentElement.classList.contains('no-animations') ||
          window.matchMedia('(prefers-reduced-motion: reduce)').matches) return;

        const after = surface.getBoundingClientRect();
        if (!after.width || !after.height) return;
        const timing = { duration, easing: 'cubic-bezier(0.16, 1, 0.3, 1)' };
        // Move the shared shell and resize only its surface, keeping text crisp.
        animations.push(wrapper.animate([
          { translate: `${before.left - after.left}px ${before.top - after.top}px` },
          { translate: '0px 0px' }
        ], timing));
        animations.push(surface.animate([
          { transform: `scale(${before.width / after.width}, ${before.height / after.height})` },
          { transform: 'scale(1, 1)' }
        ], timing));
        // Clip both labels to the animated surface, including during a shrink.
        // Negative insets let the outgoing label use the previous box's size.
        animations.push(content.animate([
          { clipPath: `inset(0px ${after.width - before.width}px ${after.height - before.height}px 0px)` },
          { clipPath: 'inset(0px 0px 0px 0px)' }
        ], timing));

        if (oldLabel && oldLabel.textContent !== label.textContent) {
          outgoing = oldLabel;
          animations.push(...animateTextSweep(label, oldLabel, content, duration, oldWidth));
          const finished = animations[animations.length - 1];
          void finished.finished.then(() => {
            oldLabel.remove();
            if (outgoing === oldLabel) outgoing = null;
          }).catch(() => {});
        }
      })();
    });
  });

  onMount(() => {
    tooltipManager.initGlobal();
  });

  function registerBox(node: HTMLElement) {
    tooltipManager.registerTooltipElement(node);
    const observer = createFrameResizeObserver(() => tooltipManager.updatePosition());
    observer.observe(node);
    return {
      destroy() {
        observer.disconnect();
        tooltipManager.registerTooltipElement(null);
      }
    };
  }
</script>

<div use:portal class="amp-tooltip-portal-root">
  {#if tooltipManager.isRendered}
    <div
      bind:this={wrapper}
      class="amp-tooltip-wrapper pos-{tooltipManager.coords.actualPosition}"
      style="top: {tooltipManager.coords.top}px; left: {tooltipManager.coords.left}px; z-index: 2147483647;"
      role="tooltip"
      aria-hidden={!tooltipManager.isVisible}
      class:interactive={tooltipManager.options.interactive}
    >
      <div
        class="amp-tooltip-anim"
        class:is-visible={tooltipManager.isVisible}
        class:pos-top={tooltipManager.coords.actualPosition === 'top'}
        class:pos-bottom={tooltipManager.coords.actualPosition === 'bottom'}
        class:pos-left={tooltipManager.coords.actualPosition === 'left'}
        class:pos-right={tooltipManager.coords.actualPosition === 'right'}
      >
        <div
          use:registerBox
          class="amp-tooltip-box {tooltipManager.options.variant || 'dark'} {tooltipManager.options.className || ''}"
          style={tooltipManager.options.maxWidth ? `--tooltip-max-width: ${tooltipManager.options.maxWidth};` : ''}
        >
          <div bind:this={surface} class="amp-tooltip-surface"></div>
          <div bind:this={content} class="amp-tooltip-content">
            <span bind:this={label} class="amp-tooltip-label">{tooltipManager.options.text}</span>
          </div>

          {#if tooltipManager.options.arrow}
            <div class="amp-tooltip-arrow arrow-{tooltipManager.coords.actualPosition}"></div>
          {/if}
        </div>
      </div>
    </div>
  {/if}
</div>

<style>
  .amp-tooltip-portal-root {
    position: fixed;
    top: 0;
    left: 0;
    width: 0;
    height: 0;
    margin: 0;
    padding: 0;
    border: none;
    overflow: visible;
    pointer-events: none;
    z-index: 2147483647;
  }

  .amp-tooltip-wrapper {
    position: fixed;
    width: max-content;
    pointer-events: none;
    user-select: none;
    print-color-adjust: exact;
    z-index: 2147483647;
  }

  .amp-tooltip-wrapper.interactive {
    pointer-events: auto;
  }

  /* Alignment transforms based on active orientation */
  .amp-tooltip-wrapper.pos-top {
    transform: translate(-50%, -100%);
  }

  .amp-tooltip-wrapper.pos-bottom {
    transform: translate(-50%, 0);
  }

  .amp-tooltip-wrapper.pos-left {
    transform: translate(-100%, -50%);
  }

  .amp-tooltip-wrapper.pos-right {
    transform: translate(0, -50%);
  }

  /* Inner animation layer - matching Contract Builder & Herald-MaGT, connected to Settings animation timing */
  .amp-tooltip-anim {
    transition: opacity var(--anim-duration, 0.2s) cubic-bezier(0.16, 1, 0.3, 1),
                transform var(--anim-duration, 0.2s) cubic-bezier(0.16, 1, 0.3, 1);
    transform-origin: center center;
  }

  .amp-tooltip-anim.is-visible {
    opacity: 1;
    transform: scale(1) translate(0, 0);
  }

  .amp-tooltip-anim:not(.is-visible).pos-top {
    opacity: 0;
    transform: scale(0.95) translateY(4px);
  }

  .amp-tooltip-anim:not(.is-visible).pos-bottom {
    opacity: 0;
    transform: scale(0.95) translateY(-4px);
  }

  .amp-tooltip-anim:not(.is-visible).pos-left {
    opacity: 0;
    transform: scale(0.95) translateX(4px);
  }

  .amp-tooltip-anim:not(.is-visible).pos-right {
    opacity: 0;
    transform: scale(0.95) translateX(-4px);
  }

  /* Tooltip box styling */
  .amp-tooltip-box {
    position: relative;
    display: inline-flex;
    box-sizing: border-box;
    max-width: min(var(--tooltip-max-width, 100vw), max(0px, calc(100vw - 16px)));
    max-height: max(0px, calc(100vh - 16px));
    align-items: center;
    gap: 6px;
    padding: 4px 10px;
    border-radius: 6px;
    font-size: 11px;
    font-weight: 500;
    line-height: 1.35;
    letter-spacing: -0.01em;
    white-space: normal;
    overflow-wrap: anywhere;
  }

  .amp-tooltip-content {
    position: relative;
    min-width: 0;
    max-height: max(0px, calc(100vh - 24px));
    clip-path: inset(0);
  }

  .amp-tooltip-label {
    display: block;
    max-height: max(0px, calc(100vh - 24px));
    overflow: hidden;
  }

  .amp-tooltip-surface {
    position: absolute;
    inset: 0;
    border-radius: inherit;
    transform-origin: top left;
    background: rgba(30, 30, 30, 0.95);
    border: none;
    color: #f3f4f6;
    box-shadow: 0 10px 25px -5px rgba(0, 0, 0, 0.5), 0 8px 10px -6px rgba(0, 0, 0, 0.4);
    backdrop-filter: blur(12px);
    -webkit-backdrop-filter: blur(12px);
  }

  /* Danger variant */
  .amp-tooltip-box.danger .amp-tooltip-surface {
    background: rgba(220, 38, 38, 0.95);
    border: none;
  }

  .amp-tooltip-box { color: #f3f4f6; }
  .amp-tooltip-box.danger { color: #ffffff; }

  /* Light theme support */
  :global(html.light) .amp-tooltip-box:not(.danger) .amp-tooltip-surface {
    background: rgba(255, 255, 255, 0.96);
    border: none;
    box-shadow: 0 10px 25px -5px rgba(0, 0, 0, 0.15), 0 8px 10px -6px rgba(0, 0, 0, 0.08);
  }

  :global(html.light) .amp-tooltip-box:not(.danger) { color: #18181b; }

  @media (prefers-reduced-motion: reduce) {
    .amp-tooltip-anim { transition: none; }
  }

  /* Arrow indicators */
  .amp-tooltip-arrow {
    position: absolute;
    width: 0;
    height: 0;
    pointer-events: none;
    border: 4px solid transparent;
  }

  .amp-tooltip-arrow.arrow-top {
    bottom: -4px;
    left: 50%;
    transform: translateX(-50%);
    border-top-color: rgba(30, 30, 30, 0.95);
  }

  :global(html.light) .amp-tooltip-arrow.arrow-top {
    border-top-color: rgba(255, 255, 255, 0.96);
  }

  .amp-tooltip-arrow.arrow-bottom {
    top: -4px;
    left: 50%;
    transform: translateX(-50%);
    border-bottom-color: rgba(30, 30, 30, 0.95);
  }

  :global(html.light) .amp-tooltip-arrow.arrow-bottom {
    border-bottom-color: rgba(255, 255, 255, 0.96);
  }

  .amp-tooltip-arrow.arrow-left {
    right: -4px;
    top: 50%;
    transform: translateY(-50%);
    border-left-color: rgba(30, 30, 30, 0.95);
  }

  :global(html.light) .amp-tooltip-arrow.arrow-left {
    border-left-color: rgba(255, 255, 255, 0.96);
  }

  .amp-tooltip-arrow.arrow-right {
    left: -4px;
    top: 50%;
    transform: translateY(-50%);
    border-right-color: rgba(30, 30, 30, 0.95);
  }

  :global(html.light) .amp-tooltip-arrow.arrow-right {
    border-right-color: rgba(255, 255, 255, 0.96);
  }
</style>
