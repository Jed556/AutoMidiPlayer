<script module lang="ts">
  import type { IconMetadata } from './iconData';

  export const metadata: IconMetadata = {
    name: 'Headphones',
    description: 'Elastic ear cup outward stretch & headband kinematic flex',
    params: [
      {
        type: 'toggle',
        id: 'activeFilled',
        label: 'Active & Filled',
        defaultValue: false,
        onProps: { filled: true, active: true },
        offProps: { filled: false, active: false }
      }
    ]
  };
</script>

<script lang="ts">
  import { onDestroy, onMount, untrack } from 'svelte';
  import { animationStore } from '../../state/animationStore.svelte';

  interface Props {
    filled?: boolean;
    active?: boolean;
    size?: number;
    class?: string;
    style?: string;
    title?: string;
    duration?: number;
    trigger?: number | boolean;
  }

  let {
    filled = false,
    active = false,
    size = 20,
    class: customClass = '',
    style = '',
    title,
    duration,
    trigger
  }: Props = $props();

  // Fluent UI 24px Headphones Geometry Decomposed:
  // Headband: R = 9.25, stroke-width = 1.5, endpoints at (2.75, 12) and (21.25, 12)
  const LEFT_CUP_FILLED = 'M2 12v7a3 3 0 0 0 3 3h3a1 1 0 0 0 1-1v-6a1 1 0 0 0-1-1H3.5v-2H2z';
  const RIGHT_CUP_FILLED = 'M22 12v7a3 3 0 0 1-3 3h-3a1 1 0 0 1-1-1v-6a1 1 0 0 1 1-1h4.5v-2H22z';

  const LEFT_CUP_REGULAR =
    'M2 12v7a3 3 0 0 0 3 3h3a1 1 0 0 0 1-1v-6a1 1 0 0 0-1-1H3.5v-2H2zm5.5 3.5h-4V19c0 .83.67 1.5 1.5 1.5h2.5z';
  const RIGHT_CUP_REGULAR =
    'M22 12v7a3 3 0 0 1-3 3h-3a1 1 0 0 1-1-1v-6a1 1 0 0 1 1-1h4.5v-2H22zm-1.5 3.5h-4v5H19c.78 0 1.42-.6 1.5-1.36z';

  // Kinematic parameters at peak stretch (lambda = 1.0)
  const MAX_DX = 2.2; // Outward cup translation (px)
  const MAX_DY = 1.0; // Upward cup translation (px)
  const MAX_ROT = 5.5; // Outward tilt rotation (degrees)
  const MAX_DTOP = 0.4; // Headband top apex dip (px)

  let hbPathEl: SVGPathElement | null = $state(null);
  let leftCupG: SVGGElement | null = $state(null);
  let rightCupG: SVGGElement | null = $state(null);

  let isMounted = false;
  let previousFilled: boolean | undefined = undefined;
  let previousTrigger: number | boolean | undefined = undefined;

  let currentLambda = 0; // Current stretch factor: 0 = idle, 1 = peak stretch
  let animFrame: number | null = null;
  let animStartTime = 0;
  let stretchDuration = 160;
  let releaseDuration = 340;
  let startLambda = 0;

  function render(lambda: number) {
    currentLambda = lambda;
    const dx = lambda * MAX_DX;
    const dy = lambda * MAX_DY;
    const rot = lambda * MAX_ROT;
    const dtop = lambda * MAX_DTOP;

    if (leftCupG) {
      leftCupG.setAttribute(
        'transform',
        `translate(${-dx.toFixed(3)}, ${-dy.toFixed(3)}) rotate(${(-rot).toFixed(2)}, 5.5, 18)`
      );
    }

    if (rightCupG) {
      rightCupG.setAttribute(
        'transform',
        `translate(${dx.toFixed(3)}, ${-dy.toFixed(3)}) rotate(${rot.toFixed(2)}, 18.5, 18)`
      );
    }

    if (hbPathEl) {
      const rad = (rot * Math.PI) / 180;

      // Left attachment point (shelf surface 2.75, 12 rotated around pivot 5.5, 18, translated by -dx, -dy)
      const rxL = -2.75;
      const ryL = -6.0;
      const rotRxL = rxL * Math.cos(-rad) - ryL * Math.sin(-rad);
      const rotRyL = rxL * Math.sin(-rad) + ryL * Math.cos(-rad);
      const p0x = 5.5 + rotRxL - dx;
      const p0y = 18.0 + rotRyL - dy;

      // Right attachment point (shelf surface 21.25, 12 rotated around pivot 18.5, 18, translated by +dx, -dy)
      const rxR = 2.75;
      const ryR = -6.0;
      const rotRxR = rxR * Math.cos(rad) - ryR * Math.sin(rad);
      const rotRyR = rxR * Math.sin(rad) + ryR * Math.cos(rad);
      const p3x = 18.5 + rotRxR + dx;
      const p3y = 18.0 + rotRyR - dy;

      // Tangent projection: control points project upwards along the outward-tilted ear cup angle
      const cpDist = 5.11 - dy * 0.4;
      const cp1x = p0x - cpDist * Math.sin(rad);
      const cp1y = p0y - cpDist * Math.cos(rad);

      const cp4x = p3x + cpDist * Math.sin(rad);
      const cp4y = p3y - cpDist * Math.cos(rad);

      const cp2x = 6.89 - dx * 0.3;
      const cp2y = 2.75 + dtop;

      const cp3x = 17.11 + dx * 0.3;
      const cp3y = 2.75 + dtop;

      const d = `M ${p0x.toFixed(3)} ${p0y.toFixed(3)} C ${cp1x.toFixed(3)} ${cp1y.toFixed(3)}, ${cp2x.toFixed(3)} ${cp2y.toFixed(3)}, 12 ${(2.75 + dtop).toFixed(3)} C ${cp3x.toFixed(3)} ${cp3y.toFixed(3)}, ${cp4x.toFixed(3)} ${cp4y.toFixed(3)}, ${p3x.toFixed(3)} ${p3y.toFixed(3)}`;
      hbPathEl.setAttribute('d', d);
    }
  }

  export function triggerAnimation() {
    if (animFrame !== null) {
      cancelAnimationFrame(animFrame);
      animFrame = null;
    }

    const isOff =
      animationStore.isOff ||
      (typeof document !== 'undefined' &&
        document.documentElement.classList.contains('no-animations'));

    if (isOff || !isMounted) {
      render(0);
      return;
    }

    // Mid-flight recapture for 100% spam-proofing:
    // Seamlessly begin new stretch from whatever the current lambda is
    startLambda = currentLambda;
    animStartTime = performance.now();

    const baseDuration = duration !== undefined ? duration * 1000 : 500;
    const ratio = baseDuration / 500;

    // Adaptive stretch time: shorter if already partially stretched
    const stretchPortion = Math.max(0.35, 1.0 - Math.max(0, startLambda) * 0.6);
    stretchDuration = 160 * ratio * stretchPortion;
    releaseDuration = 340 * ratio;
    const totalDuration = stretchDuration + releaseDuration;

    function step(now: number) {
      const elapsed = now - animStartTime;

      if (elapsed <= stretchDuration) {
        // Phase 1: Outward stretch ("putting on")
        const p = Math.min(1, elapsed / stretchDuration);
        const ease = 1 - Math.pow(1 - p, 3);
        const lambda = startLambda + (1.0 - startLambda) * ease;
        render(lambda);
        animFrame = requestAnimationFrame(step);
      } else if (elapsed <= totalDuration) {
        // Phase 2: Damped elastic snap back ("let go")
        const tau = (elapsed - stretchDuration) / releaseDuration;
        const omega = 12.0;
        const decay = 7.5;
        const spring = Math.exp(-decay * tau) * Math.cos(omega * tau);
        render(spring);
        animFrame = requestAnimationFrame(step);
      } else {
        render(0);
        animFrame = null;
      }
    }

    animFrame = requestAnimationFrame(step);
  }

  // React to filled prop toggle
  $effect(() => {
    const currentFilled = filled;
    untrack(() => {
      if (previousFilled === undefined) {
        previousFilled = currentFilled;
        return;
      }
      if (currentFilled !== previousFilled) {
        previousFilled = currentFilled;
        triggerAnimation();
      }
    });
  });

  // React to explicit trigger prop
  $effect(() => {
    const currentTrig = trigger;
    untrack(() => {
      if (previousTrigger === undefined) {
        previousTrigger = currentTrig;
        return;
      }
      if (currentTrig !== undefined && currentTrig !== previousTrigger) {
        previousTrigger = currentTrig;
        triggerAnimation();
      }
    });
  });

  onMount(() => {
    isMounted = true;
    render(0);
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
  class="fluent-system-icon headphones-icon {customClass}"
  class:active
  {style}
  role="img"
  aria-hidden={title ? undefined : 'true'}
>
  {#if title}
    <title>{title}</title>
  {/if}

  <!-- Headband Arc -->
  <path
    bind:this={hbPathEl}
    fill="none"
    stroke="currentColor"
    stroke-width="1.5"
    stroke-linecap="butt"
    stroke-linejoin="round"
    d="M 2.75 12 C 2.75 6.89, 6.89 2.75, 12 2.75 C 17.11 2.75, 21.25 6.89, 21.25 12"
  />

  <!-- Left Ear Cup -->
  <g bind:this={leftCupG}>
    <path
      fill="currentColor"
      d={filled ? LEFT_CUP_FILLED : LEFT_CUP_REGULAR}
      fill-rule={filled ? undefined : 'evenodd'}
    />
  </g>

  <!-- Right Ear Cup -->
  <g bind:this={rightCupG}>
    <path
      fill="currentColor"
      d={filled ? RIGHT_CUP_FILLED : RIGHT_CUP_REGULAR}
      fill-rule={filled ? undefined : 'evenodd'}
    />
  </g>
</svg>

<style>
  .headphones-icon {
    display: inline-block;
    vertical-align: middle;
    flex-shrink: 0;
    overflow: visible;
  }

  .headphones-icon.active {
    color: var(--accent-color, #1db954);
  }
</style>
