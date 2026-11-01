<script module lang="ts">
  import type { IconMetadata } from './iconData';

  export const metadata: IconMetadata = {
    name: 'Speed / TopSpeed',
    description: 'Analog speedometer dial needle & tick proximity shrink',
    animationParamId: 'speed',
    params: [
      {
        type: 'slider',
        id: 'speed',
        label: 'Gauge Speed Target',
        min: 0.25,
        max: 3.0,
        step: 0.05,
        defaultValue: 1.0,
        unit: 'x',
        presets: [
          { label: '0.25x', value: 0.25 },
          { label: '0.5x', value: 0.5 },
          { label: '0.75x', value: 0.75 },
          { label: '1.0x', value: 1.0 },
          { label: '1.5x', value: 1.5 },
          { label: '2.0x', value: 2.0 },
          { label: '3.0x', value: 3.0 }
        ]
      }
    ]
  };
</script>

<script lang="ts">
  import { onDestroy, onMount, untrack } from 'svelte';
  import { animationStore } from '../../state/animationStore.svelte';

  interface Props {
    speed?: number;
    size?: number;
    class?: string;
    style?: string;
    title?: string;
    duration?: number;
  }

  let {
    speed = 1.0,
    size = 16,
    class: customClass = '',
    style = '',
    title,
    duration
  }: Props = $props();

  // 5 Symmetric gauge ticks positioned every 45° around the dial:
  // -90° (Left horizontal), -45° (Upper-left diagonal), 0° (Top center vertical),
  // +45° (Upper-right diagonal), +90° (Right horizontal)
  const TICK_ANGLES = [-90, -45, 0, 45, 90];
  const PROXIMITY_WIDTH = 26.0; // Angular width (degrees) where tick starts shrinking

  // Maps playback speed to pointer angle (degrees from 12 o'clock center tick)
  // 0.25x -> -90° (Left tick)
  // 0.5x  -> -45° (Upper-left diagonal tick)
  // 1.0x  ->   0° (Top center tick)
  // 2.0x  -> +45° (Upper-right diagonal tick)
  // 3.0x  -> +90° (Right tick)
  function speedToAngle(s: number): number {
    const val = Number.isFinite(s) ? s : 1.0;
    if (val <= 1.0) {
      if (val >= 0.5) {
        // [0.5, 1.0] -> [-45°, 0°]
        return ((val - 1.0) / 0.5) * 45;
      } else {
        // [0.25, 0.5) -> [-90°, -45°]
        return Math.max(-90, -45 + ((val - 0.5) / 0.25) * 45);
      }
    } else {
      if (val <= 2.0) {
        // [1.0, 2.0] -> [0°, +45°]
        return ((val - 1.0) / 1.0) * 45;
      } else {
        // (2.0, 3.0] -> [+45°, +90°]
        return Math.min(90, 45 + ((val - 2.0) / 1.0) * 45);
      }
    }
  }

  // Calculates radial length for each tick.
  // Full extended length is y2 = 5.25.
  // As the needle enters proximity (|pointerAngle - tickAngle| < PROXIMITY_WIDTH),
  // y2 smoothly shrinks to 2.75 so the rounded cap retracts flush inside the circular stroke.
  function calculateTickY2(pointerAngle: number, tickAngle: number): number {
    const diff = Math.abs(pointerAngle - tickAngle);
    if (diff >= PROXIMITY_WIDTH) {
      return 5.25;
    }
    const u = diff / PROXIMITY_WIDTH;
    const ease = u * u * (3 - 2 * u);
    return 2.75 + 2.50 * ease;
  }

  let needleGroup: SVGGElement | null = $state(null);
  let tickElements: SVGLineElement[] = [];
  let isMounted = false;

  let needleAngle = $derived(speedToAngle(speed));
  let currentAngle = $state(0);
  let startAngle = 0;
  let targetAngle = 0;

  let animStartTime = 0;
  let animDurationMs = 220;
  let animFrame: number | null = null;

  // Snappy quartic ease-out: immediate response on trigger, silky deceleration to rest
  function easeOutQuart(t: number): number {
    return 1 - Math.pow(1 - t, 4);
  }

  function applyAngle(angle: number) {
    if (needleGroup) {
      const rot = angle - 39.0;
      needleGroup.setAttribute('transform', `rotate(${rot.toFixed(2)}, 12, 12)`);
    }
    for (let i = 0; i < tickElements.length; i++) {
      const el = tickElements[i];
      if (el) {
        const y2 = calculateTickY2(angle, TICK_ANGLES[i]);
        el.setAttribute('y2', y2.toFixed(2));
      }
    }
  }

  function animateTo(target: number) {
    if (animFrame !== null) {
      cancelAnimationFrame(animFrame);
      animFrame = null;
    }

    const isOff = animationStore.isOff ||
      (typeof document !== 'undefined' && document.documentElement.classList.contains('no-animations'));

    if (isOff || !isMounted) {
      currentAngle = target;
      applyAngle(currentAngle);
      return;
    }

    // Mid-flight capture: start from whatever the current visual angle is (100% spam-proof)
    startAngle = currentAngle;
    targetAngle = target;
    animStartTime = performance.now();
    animDurationMs = duration !== undefined ? duration * 1000 : (animationStore.durationMs || 220);

    function step(now: number) {
      const elapsed = now - animStartTime;
      const progress = Math.min(1, elapsed / animDurationMs);
      const ease = easeOutQuart(progress);

      currentAngle = startAngle + (targetAngle - startAngle) * ease;
      applyAngle(currentAngle);

      if (progress < 1) {
        animFrame = requestAnimationFrame(step);
      } else {
        currentAngle = targetAngle;
        applyAngle(currentAngle);
        animFrame = null;
      }
    }

    animFrame = requestAnimationFrame(step);
  }

  $effect(() => {
    const target = needleAngle;
    untrack(() => {
      if (isMounted) {
        animateTo(target);
      } else {
        currentAngle = target;
      }
    });
  });

  onMount(() => {
    isMounted = true;
    currentAngle = needleAngle;
    applyAngle(currentAngle);
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
  viewBox="0 0 24 24"
  width={size}
  height={size}
  class="speed-icon {customClass}"
  {style}
  role="img"
  aria-hidden={title ? undefined : 'true'}
>
  {#if title}
    <title>{title}</title>
  {/if}

  <!-- Gauge Dial Outer Rim (Clean 283° circular track with rounded endpoints) -->
  <path
    class="gauge-dial"
    fill="currentColor"
    d="M 5.77 19.83 a 0.75 0.75 0 0 0 0.96 -1.16 A 8.5 8.5 0 1 1 17.27 18.67 a 0.75 0.75 0 0 0 0.96 1.16 A 10 10 0 1 0 5.77 19.83 Z"
  />

  <!-- 5 Symmetric Gauge Ticks (-90°, -45°, 0°, +45°, +90°) that dynamically shrink when pointer approaches -->
  <g class="gauge-ticks">
    {#each TICK_ANGLES as tickAngle, i}
      <g transform="rotate({tickAngle}, 12, 12)">
        <line
          bind:this={tickElements[i]}
          class="gauge-tick-line"
          x1="12"
          y1="2.75"
          x2="12"
          y2={calculateTickY2(currentAngle, tickAngle).toFixed(2)}
          stroke="currentColor"
          stroke-width="1.5"
          stroke-linecap="round"
        />
      </g>
    {/each}
  </g>

  <!-- Needle Group rotating around center (12, 12) -->
  <g
    bind:this={needleGroup}
    class="gauge-needle-group"
    transform="rotate({(currentAngle - 39.0).toFixed(2)}, 12, 12)"
  >
    <path
      class="gauge-needle"
      fill="currentColor"
      d="M16.85 6.02a.65.65 0 0 0-.8.02l-.23.18-4.21 3.6-1.1 1.05c-.66.8-.52 1.98.3 2.62s2.03.5 2.68-.3q.14-.19.32-.5l.46-.8a153 153 0 0 0 2.62-4.85l.13-.24a.6.6 0 0 0-.17-.78z"
    />
  </g>
</svg>

<style>
  .speed-icon {
    display: inline-block;
    vertical-align: middle;
    flex-shrink: 0;
    user-select: none;
    overflow: visible;
  }

  .gauge-dial,
  .gauge-needle {
    fill: currentColor;
  }

  .gauge-tick-line {
    stroke: currentColor;
    stroke-width: 1.5;
    stroke-linecap: round;
  }
</style>
