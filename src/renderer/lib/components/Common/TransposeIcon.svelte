<script module lang="ts">
  import type { IconMetadata } from './iconData';

  export const metadata: IconMetadata = {
    name: 'Transpose',
    description: 'Dynamic 5-bar equalizer height transitions based on mode',
    animationParamId: 'mode',
    params: [
      {
        type: 'option',
        id: 'mode',
        label: 'Transpose Mode',
        defaultValue: 'TransposeSmart',
        options: [
          { label: 'Smart', value: 'TransposeSmart', props: { iconName: 'TransposeSmart' } },
          { label: 'Up', value: 'TransposeUp', props: { iconName: 'TransposeUp' } },
          { label: 'Down', value: 'TransposeDown', props: { iconName: 'TransposeDown' } },
          { label: 'Ignore', value: 'TransposeIgnore', props: { iconName: 'TransposeIgnore' } }
        ]
      }
    ]
  };
</script>

<script lang="ts">
  import { onDestroy, onMount, untrack } from 'svelte';
  import { animationStore } from '../../state/animationStore.svelte';
  import type { TransposeMode } from '$shared/types/midi';

  interface Props {
    mode?: TransposeMode | string;
    size?: number;
    class?: string;
    style?: string;
    title?: string;
    duration?: number;
  }

  let {
    mode = 'Smart',
    size = 20,
    class: customClass = '',
    style = '',
    title,
    duration
  }: Props = $props();

  // The 5 equalizer bar heights (viewBox 0 0 800 800)
  // All bars are vertically centered at Y = 400 (y = 400 - height / 2)
  const MODE_HEIGHTS: Record<TransposeMode, [number, number, number, number, number]> = {
    Ignore: [100, 200, 300, 200, 100],
    Up:     [100, 140, 180, 240, 300],
    Down:   [300, 240, 180, 140, 100],
    Smart:  [160, 300, 240, 260, 80]
  };

  // Fixed X positions for the 5 bars (center at 200, 300, 400, 500, 600, width = 50)
  const BAR_X = [175, 275, 375, 475, 575];

  function resolveMode(m: string | undefined): TransposeMode {
    if (!m) return 'Smart';
    const clean = m.replace(/^Transpose/, '') as TransposeMode;
    if (clean === 'Ignore' || clean === 'Up' || clean === 'Down' || clean === 'Smart') {
      return clean;
    }
    return 'Smart';
  }

  let currentMode = $derived(resolveMode(mode));

  let barElements: SVGRectElement[] = [];
  let isMounted = false;

  // Svelte 5 state for reactive initial heights
  let currentHeights = $state<number[]>([...MODE_HEIGHTS['Smart']]);
  let startHeights: number[] = [...MODE_HEIGHTS['Smart']];
  let targetHeights: number[] = [...MODE_HEIGHTS['Smart']];

  let animStartTime = 0;
  let animDurationMs = 220;
  let animFrame: number | null = null;

  // Snappy quartic ease-out: immediate motion on trigger, silky deceleration to rest
  function easeOutQuart(t: number): number {
    return 1 - Math.pow(1 - t, 4);
  }

  function applyHeights(heights: number[]) {
    for (let i = 0; i < 5; i++) {
      const el = barElements[i];
      if (el) {
        const h = heights[i];
        const y = 400 - h / 2;
        el.setAttribute('y', y.toFixed(2));
        el.setAttribute('height', h.toFixed(2));
      }
    }
  }

  function animateTo(target: [number, number, number, number, number]) {
    if (animFrame !== null) {
      cancelAnimationFrame(animFrame);
      animFrame = null;
    }

    const isOff = animationStore.isOff ||
      (typeof document !== 'undefined' && document.documentElement.classList.contains('no-animations'));

    if (isOff || !isMounted) {
      currentHeights = [...target];
      applyHeights(currentHeights);
      return;
    }

    // Mid-flight capture: start from whatever the current visual heights are (100% spam-proof)
    startHeights = [...currentHeights];
    targetHeights = [...target];
    animStartTime = performance.now();
    animDurationMs = duration !== undefined ? duration * 1000 : (animationStore.durationMs || 220);

    function step(now: number) {
      const elapsed = now - animStartTime;
      const progress = Math.min(1, elapsed / animDurationMs);
      const ease = easeOutQuart(progress);

      for (let i = 0; i < 5; i++) {
        currentHeights[i] = startHeights[i] + (targetHeights[i] - startHeights[i]) * ease;
      }

      applyHeights(currentHeights);

      if (progress < 1) {
        animFrame = requestAnimationFrame(step);
      } else {
        currentHeights = [...targetHeights];
        applyHeights(currentHeights);
        animFrame = null;
      }
    }

    animFrame = requestAnimationFrame(step);
  }

  $effect(() => {
    const target = MODE_HEIGHTS[currentMode];
    untrack(() => {
      if (isMounted) {
        animateTo(target);
      } else {
        currentHeights = [...target];
      }
    });
  });

  onMount(() => {
    isMounted = true;
    currentHeights = [...MODE_HEIGHTS[currentMode]];
    applyHeights(currentHeights);
  });

  onDestroy(() => {
    if (animFrame !== null) {
      cancelAnimationFrame(animFrame);
      animFrame = null;
    }
  });
</script>

<svg
  xmlns="http://www.w3.org/2000/svg"
  viewBox="0 0 800 800"
  width={size}
  height={size}
  class="transpose-icon {customClass}"
  {style}
  fill="currentColor"
  role="img"
  aria-hidden={title ? undefined : 'true'}
>
  {#if title}
    <title>{title}</title>
  {/if}

  <!-- Static Outer Frame Outline -->
  <path
    class="transpose-frame"
    fill="currentColor"
    d="M750,225c0-19.9-7.9-39-22-53c-14.1-14.1-33.1-22-53-22H125c-19.9,0-39,7.9-53,22c-14.1,14.1-22,33.1-22,53c0,86.6,0,263.4,0,350c0,19.9,7.9,39,22,53c14.1,14.1,33.1,22,53,22h550c19.9,0,39-7.9,53-22c14.1-14.1,22-33.1,22-53V225zM700,225v350c0,6.6-2.6,13-7.3,17.7c-4.7,4.7-11,7.3-17.7,7.3H125c-6.6,0-13-2.6-17.7-7.3c-4.7-4.7-7.3-11-7.3-17.7V225c0-6.6,2.6-13,7.3-17.7c4.7-4.7,11-7.3,17.7-7.3h550c6.6,0,13,2.6,17.7,7.3C697.4,212,700,218.4,700,225z"
  />

  <!-- 5 Morphing Equalizer Bars (rx=25, ry=25 preserves perfect semicircular caps) -->
  {#each BAR_X as x, i}
    <rect
      bind:this={barElements[i]}
      class="transpose-bar transpose-bar-{i + 1}"
      {x}
      y={400 - currentHeights[i] / 2}
      width={50}
      height={currentHeights[i]}
      rx={25}
      ry={25}
      fill="currentColor"
    />
  {/each}
</svg>

<style>
  .transpose-icon {
    display: inline-block;
    vertical-align: middle;
    flex-shrink: 0;
    user-select: none;
    overflow: visible;
  }

  .transpose-frame,
  .transpose-bar {
    fill: currentColor;
  }
</style>
