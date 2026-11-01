<script module lang="ts">
  import type { IconMetadata } from './iconData';

  export const metadata: IconMetadata = {
    name: 'Shuffle',
    description: 'Crossing arrows trim path with animated arrowhead collapse and unfold',
    params: [
      {
        type: 'toggle',
        id: 'activeState',
        label: 'Active State',
        defaultValue: false,
        onProps: { active: true },
        offProps: { active: false }
      }
    ]
  };
</script>

<script lang="ts">
  import { onDestroy, onMount, untrack } from 'svelte';
  import { animationStore } from '../../state/animationStore.svelte';

  interface Props {
    active?: boolean;
    size?: number;
    class?: string;
    style?: string;
    title?: string;
    duration?: number;
    trigger?: number | boolean;
  }

  let {
    active = false,
    size = 14,
    class: customClass = '',
    style = '',
    title,
    duration,
    trigger
  }: Props = $props();

  let trackAEl: SVGPathElement | null = $state(null);
  let trackB1El: SVGPathElement | null = $state(null);
  let trackB2El: SVGPathElement | null = $state(null);
  let arr1Group: SVGGElement | null = $state(null);
  let arr2Group: SVGGElement | null = $state(null);
  let arr1w1El: SVGLineElement | null = $state(null);
  let arr1w2El: SVGLineElement | null = $state(null);
  let arr2w1El: SVGLineElement | null = $state(null);
  let arr2w2El: SVGLineElement | null = $state(null);

  let isMounted = false;
  let previousActive: boolean | undefined = undefined;
  let previousTrigger: number | boolean | undefined = undefined;

  let lenA = 0;
  let lenB1 = 0;
  let lenB2 = 0;
  const wingLen = 3.253; // Length from apex (14.92, 3.75) to wing tip (12.62, 1.45)
  const GAP_B = 4.2;

  let curStartFrac = 0;
  let curEndFrac = 1;
  let curWingTrim = 0; // 0 = fully extended wings, 1 = fully retracted into apex

  let animFrame: number | null = null;
  let animStartTime = 0;

  function setWingTrim(trimFrac: number) {
    curWingTrim = trimFrac;
    const clamped = Math.max(0, Math.min(1, trimFrac));
    const visibleLen = Math.max(0, (1 - clamped) * wingLen);
    const dashStr = `${visibleLen.toFixed(3)}px 50px`;

    if (arr1w1El) {
      arr1w1El.style.strokeDasharray = dashStr;
      arr1w1El.style.opacity = visibleLen > 0.05 ? '1' : '0';
    }
    if (arr1w2El) {
      arr1w2El.style.strokeDasharray = dashStr;
      arr1w2El.style.opacity = visibleLen > 0.05 ? '1' : '0';
    }
    if (arr2w1El) {
      arr2w1El.style.strokeDasharray = dashStr;
      arr2w1El.style.opacity = visibleLen > 0.05 ? '1' : '0';
    }
    if (arr2w2El) {
      arr2w2El.style.strokeDasharray = dashStr;
      arr2w2El.style.opacity = visibleLen > 0.05 ? '1' : '0';
    }
  }

  function renderFrame(startFrac: number, endFrac: number, arrowDist: number | null, wingTrim: number) {
    curStartFrac = startFrac;
    curEndFrac = endFrac;

    // 1. Track A
    if (trackAEl && lenA > 0) {
      const sA = Math.max(0, Math.min(1, startFrac)) * lenA;
      const eA = Math.max(0, Math.min(1, endFrac)) * lenA;
      const spanA = Math.max(0, eA - sA);
      trackAEl.style.strokeDasharray = `${spanA.toFixed(2)}px ${lenA * 2}px`;
      trackAEl.style.strokeDashoffset = `${(-sA).toFixed(2)}px`;
    }

    // 2. Track B (with authentic gap)
    if (trackB1El && trackB2El && lenB1 > 0 && lenB2 > 0) {
      const totalB = lenB1 + GAP_B + lenB2;
      const sB_abs = Math.max(0, Math.min(1, startFrac)) * totalB;
      const eB_abs = Math.max(0, Math.min(1, endFrac)) * totalB;

      const sB1 = Math.max(0, Math.min(lenB1, sB_abs));
      const eB1 = Math.max(0, Math.min(lenB1, eB_abs));
      const spanB1 = Math.max(0, eB1 - sB1);
      trackB1El.style.strokeDasharray = `${spanB1.toFixed(2)}px ${lenB1 * 2}px`;
      trackB1El.style.strokeDashoffset = `${(-sB1).toFixed(2)}px`;

      const b2_start_abs = lenB1 + GAP_B;
      const sB2 = Math.max(0, Math.min(lenB2, sB_abs - b2_start_abs));
      const eB2 = Math.max(0, Math.min(lenB2, eB_abs - b2_start_abs));
      const spanB2 = Math.max(0, eB2 - sB2);
      trackB2El.style.strokeDasharray = `${spanB2.toFixed(2)}px ${lenB2 * 2}px`;
      trackB2El.style.strokeDashoffset = `${(-sB2).toFixed(2)}px`;
    }

    // 3. Arrowhead Wings (Pure trim path from tips into apex)
    setWingTrim(wingTrim);

    // 4. Arrowhead Position:
    if (arrowDist !== null) {
      if (arr1Group) arr1Group.style.display = '';
      if (arr2Group) arr2Group.style.display = '';

      if (arrowDist >= 0.99) {
        if (arr1Group) arr1Group.setAttribute('transform', '');
        if (arr2Group) arr2Group.setAttribute('transform', '');
      } else if (trackAEl && trackB1El && trackB2El && lenA > 0 && lenB1 > 0) {
        const ptA = trackAEl.getPointAtLength(Math.max(0, Math.min(lenA, arrowDist * lenA)));
        const dxA = ptA.x - 14.92;
        const dyA = ptA.y - 3.75;
        if (arr1Group) arr1Group.setAttribute('transform', `translate(${dxA.toFixed(2)}, ${dyA.toFixed(2)})`);

        const totalB = lenB1 + GAP_B + lenB2;
        const distB = Math.max(0, Math.min(totalB, arrowDist * totalB));
        const b2_start_abs = lenB1 + GAP_B;
        if (distB <= lenB1) {
          const ptB = trackB1El.getPointAtLength(distB);
          const dxB = ptB.x - 14.92;
          const dyB = ptB.y - 12.25;
          if (arr2Group) arr2Group.setAttribute('transform', `translate(${dxB.toFixed(2)}, ${dyB.toFixed(2)})`);
        } else if (distB >= b2_start_abs) {
          const ptB = trackB2El.getPointAtLength(Math.min(lenB2, distB - b2_start_abs));
          const dxB = ptB.x - 14.92;
          const dyB = ptB.y - 12.25;
          if (arr2Group) arr2Group.setAttribute('transform', `translate(${dxB.toFixed(2)}, ${dyB.toFixed(2)})`);
        } else {
          const pGap = (distB - lenB1) / GAP_B;
          const p1 = trackB1El.getPointAtLength(lenB1);
          const p2 = trackB2El.getPointAtLength(0);
          const gx = p1.x + (p2.x - p1.x) * pGap;
          const gy = p1.y + (p2.y - p1.y) * pGap;
          const dxB = gx - 14.92;
          const dyB = gy - 12.25;
          if (arr2Group) arr2Group.setAttribute('transform', `translate(${dxB.toFixed(2)}, ${dyB.toFixed(2)})`);
        }
      }
    } else {
      if (arr1Group) arr1Group.style.display = 'none';
      if (arr2Group) arr2Group.style.display = 'none';
    }
  }

  function renderIdle() {
    curStartFrac = 0;
    curEndFrac = 1;
    curWingTrim = 0;

    if (trackAEl && lenA > 0) {
      trackAEl.style.strokeDasharray = `${lenA}px ${lenA * 2}px`;
      trackAEl.style.strokeDashoffset = '0px';
    }
    if (trackB1El && lenB1 > 0) {
      trackB1El.style.strokeDasharray = `${lenB1}px ${lenB1 * 2}px`;
      trackB1El.style.strokeDashoffset = '0px';
    }
    if (trackB2El && lenB2 > 0) {
      trackB2El.style.strokeDasharray = `${lenB2}px ${lenB2 * 2}px`;
      trackB2El.style.strokeDashoffset = '0px';
    }

    if (arr1Group) {
      arr1Group.style.display = '';
      arr1Group.setAttribute('transform', '');
    }
    if (arr2Group) {
      arr2Group.style.display = '';
      arr2Group.setAttribute('transform', '');
    }

    setWingTrim(0);
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
      renderIdle();
      return;
    }

    // Mid-flight capture for 100% spam-proofing:
    const initStartFrac = curStartFrac;
    const initEndFrac = curEndFrac;
    const initWingTrim = curWingTrim;

    animStartTime = performance.now();

    const baseDuration = duration !== undefined ? duration * 1000 : 660;
    const ratio = baseDuration / 660;

    const T_COLLAPSE = 110 * ratio;
    const T_OUTRO_TRIM = 180 * ratio;
    const T_PAUSE = 30 * ratio;
    const T_INTRO = 300 * ratio;
    const T_SETTLE = 40 * ratio;

    const T1 = T_COLLAPSE;
    const T2 = T1 + T_OUTRO_TRIM;
    const T3 = T2 + T_PAUSE;
    const T4 = T3 + T_INTRO;
    const T_TOTAL = T4 + T_SETTLE;

    const easeInQuad = (t: number) => t * t;
    const easeOutCubic = (t: number) => 1 - Math.pow(1 - t, 3);

    function step(now: number) {
      const elapsed = now - animStartTime;

      if (elapsed <= T1) {
        // Outro Phase 1: Pure Trim Path on Arrow Wings from tips into apex
        const p = elapsed / T_COLLAPSE;
        const wingTrim = initWingTrim + (1.0 - initWingTrim) * easeInQuad(p);
        renderFrame(initStartFrac, initEndFrac, initEndFrac, wingTrim);
        animFrame = requestAnimationFrame(step);
      } else if (elapsed <= T2) {
        // Outro Phase 2: Trim path lines to right until no longer visible
        const p = (elapsed - T1) / T_OUTRO_TRIM;
        const startFrac = initStartFrac + (1.0 - initStartFrac) * easeInQuad(p);
        const arrowVis = startFrac < 0.96;
        renderFrame(startFrac, 1.0, arrowVis ? 1.0 : null, 1.0);
        animFrame = requestAnimationFrame(step);
      } else if (elapsed <= T3) {
        // Brief pause breath
        renderFrame(1.0, 1.0, null, 1.0);
        animFrame = requestAnimationFrame(step);
      } else if (elapsed <= T4) {
        // Intro Phase: Trimpath in from left, move arrow along and trim open wings
        const p = (elapsed - T3) / T_INTRO;
        const headFrac = easeOutCubic(p);

        const wingProg = Math.max(0, Math.min(1, (p - 0.2) / 0.7));
        const wingTrim = 1.0 - easeOutCubic(wingProg);

        renderFrame(0, headFrac, headFrac, wingTrim);
        animFrame = requestAnimationFrame(step);
      } else if (elapsed <= T_TOTAL) {
        renderIdle();
        animFrame = requestAnimationFrame(step);
      } else {
        renderIdle();
        animFrame = null;
      }
    }

    animFrame = requestAnimationFrame(step);
  }

  // React to active prop toggle
  $effect(() => {
    const currentActive = active;
    untrack(() => {
      if (previousActive === undefined) {
        previousActive = currentActive;
        return;
      }
      if (currentActive !== previousActive) {
        previousActive = currentActive;
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
    if (trackAEl) lenA = trackAEl.getTotalLength();
    if (trackB1El) lenB1 = trackB1El.getTotalLength();
    if (trackB2El) lenB2 = trackB2El.getTotalLength();
    renderIdle();
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
  viewBox="0 0 16 16"
  width={size}
  height={size}
  class="fluent-system-icon shuffle-icon {customClass}"
  class:active
  {style}
  fill="none"
  stroke="currentColor"
  role="img"
  aria-hidden={title ? undefined : 'true'}
>
  {#if title}
    <title>{title}</title>
  {/if}

  <!-- Track A: Continuous from x=0 to apex (14.92, 3.75) with zero cuts -->
  <path
    bind:this={trackAEl}
    stroke-width="1.5"
    stroke-linecap="butt"
    stroke-linejoin="round"
    d="M 0 13.25 L 0.391 13.25 A 3.0 3.0 0 0 0 2.689 12.179 L 8.862 4.821 A 3.0 3.0 0 0 1 11.16 3.75 L 14.92 3.75"
  />

  <!-- Arrowhead 1: Wings meeting at apex (14.92, 3.75) -->
  <g bind:this={arr1Group}>
    <line
      bind:this={arr1w1El}
      x1="14.92"
      y1="3.75"
      x2="12.62"
      y2="1.45"
      stroke-width="1.5"
      stroke-linecap="round"
    />
    <line
      bind:this={arr1w2El}
      x1="14.92"
      y1="3.75"
      x2="12.62"
      y2="6.05"
      stroke-width="1.5"
      stroke-linecap="round"
    />
  </g>

  <!-- Track B1 (Before crossing gap): from x=0 to (4.40, 5.86) -->
  <path
    bind:this={trackB1El}
    stroke-width="1.5"
    stroke-linecap="butt"
    stroke-linejoin="round"
    d="M 0 2.75 L 0.391 2.75 A 3.0 3.0 0 0 1 2.689 3.821 L 4.40 5.86"
  />

  <!-- Track B2 (After crossing gap): from (7.99, 10.14) to apex (14.92, 12.25) with zero cuts -->
  <path
    bind:this={trackB2El}
    stroke-width="1.5"
    stroke-linecap="butt"
    stroke-linejoin="round"
    d="M 7.99 10.14 L 8.862 11.179 A 3.0 3.0 0 0 0 11.16 12.25 L 14.92 12.25"
  />

  <!-- Arrowhead 2: Wings meeting at apex (14.92, 12.25) -->
  <g bind:this={arr2Group}>
    <line
      bind:this={arr2w1El}
      x1="14.92"
      y1="12.25"
      x2="12.62"
      y2="9.95"
      stroke-width="1.5"
      stroke-linecap="round"
    />
    <line
      bind:this={arr2w2El}
      x1="14.92"
      y1="12.25"
      x2="12.62"
      y2="14.55"
      stroke-width="1.5"
      stroke-linecap="round"
    />
  </g>
</svg>

<style>
  .shuffle-icon {
    display: inline-block;
    vertical-align: middle;
    flex-shrink: 0;
    overflow: visible;
  }

  .shuffle-icon.active {
    color: var(--accent-color, #1db954);
  }
</style>
