<script module lang="ts">
  import type { IconMetadata } from './iconData';

  export const metadata: IconMetadata = {
    name: 'Moon / Sun',
    description: 'Moon-to-sun CCW morph, radial rays trim path & solar pulse',
    animationParamId: 'isDark',
    params: [
      {
        type: 'toggle',
        id: 'isDark',
        label: 'Dark Mode (Moon)',
        defaultValue: true,
        onProps: { isDark: true, iconName: 'Moon' },
        offProps: { isDark: false, iconName: 'Sun' }
      }
    ]
  };
</script>

<script lang="ts">
  import { onDestroy, onMount, untrack } from 'svelte';
  import { animationStore } from '../../state/animationStore.svelte';
  import { getThemeRevealSpeed } from '../../utils/themeTransition';

  interface Props {
    isDark?: boolean;
    active?: boolean;
    filled?: boolean;
    size?: number;
    class?: string;
    style?: string;
    title?: string;
    duration?: number;
    trigger?: number | boolean;
  }

  let {
    isDark = true,
    active = false,
    filled = false,
    size = 18,
    class: customClass = '',
    style = '',
    title,
    duration = 0.56,
    trigger
  }: Props = $props();

  // 8 Short Sun Rays spaced at 45° intervals around center (12, 12)
  // Inner radius R1 = 7.2, Outer radius R2 = 9.6 -> length = 2.4px
  const RAY_ANGLES = [0, 45, 90, 135, 180, 225, 270, 315];
  const R1 = 7.2;
  const R2 = 9.6;
  const RAY_LEN = R2 - R1; // 2.4px

  const RAY_COORDS = RAY_ANGLES.map(deg => {
    const rad = (deg * Math.PI) / 180;
    return {
      x1: +(12 + R1 * Math.cos(rad)).toFixed(3),
      y1: +(12 + R1 * Math.sin(rad)).toFixed(3),
      x2: +(12 + R2 * Math.cos(rad)).toFixed(3),
      y2: +(12 + R2 * Math.sin(rad)).toFixed(3)
    };
  });

  // Base resting rotation angle in Dark mode (counter-clockwise tilt matching user design)
  const BASE_MOON_ROT = -18;

  // Authentic Centered Mirrored Microsoft Fluent WeatherMoon 8-Cubic-Bezier Geometry:
  // Starts at bottom-left of outer circle arc [3.3382, 17.0014]
  const MOON_P0 = [3.3382, 17.0014];
  const MOON_B = [
    [ 6.1001, 21.7851, 12.2169, 23.4241, 17.0006, 20.6622 ], // B1: outer circle bottom
    [ 18.3033, 19.9101, 19.4000, 18.8926, 20.2353, 17.6797 ], // B2: outer circle bottom-right
    [ 20.5145, 17.2741, 20.3339, 16.7141, 19.8703, 16.5482 ], // B3: bottom rounded cap
    [ 16.1029, 15.1997, 14.0851, 13.6372, 12.9142, 11.4021 ], // B4: scoop lower half
    [ 11.6815, 9.0491, 11.3630, 6.4716, 12.2253, 2.9386 ],   // B5: scoop upper half
    [ 12.3445, 2.4501, 11.9587, 1.9849, 11.4565, 2.0119 ],   // B6: top rounded cap
    [ 9.8995, 2.0954, 8.3784, 2.5426, 6.9991, 3.3390 ],       // B7: outer circle top-left
    [ 2.2154, 6.1009, 0.5764, 12.2177, 3.3382, 17.0014 ]      // B8: outer circle left
  ];

  // Circle of radius R=10 at (12, 12) decomposed into the matching 8-cubic-bezier topology:
  const CIRCLE_B = [
    [ 6.1001, 21.7851, 12.2169, 23.4241, 17.0006, 20.6622 ], // B1
    [ 18.3033, 19.9101, 19.4000, 18.8926, 20.2353, 17.6797 ], // B2
    [ 20.2353, 17.6797, 20.2353, 17.6797, 20.2353, 17.6797 ], // B3: cap merges into circle
    [ 22.3975, 14.5377, 22.5883, 10.4396, 20.7240, 7.1121 ],  // B4: circle arc segment
    [ 18.8598, 3.7847, 15.2651, 1.8075, 11.4565, 2.0119 ],   // B5: circle arc segment
    [ 11.4565, 2.0119, 11.4565, 2.0119, 11.4565, 2.0119 ],   // B6: cap merges into circle
    [ 9.8995, 2.0954, 8.3784, 2.5426, 6.9991, 3.3390 ],       // B7
    [ 2.2154, 6.1009, 0.5764, 12.2177, 3.3382, 17.0014 ]      // B8
  ];

  let pathEl: SVGPathElement | null = $state(null);
  let coreGroupEl: SVGGElement | null = $state(null);
  let rayLines: SVGLineElement[] = [];

  let isMounted = false;

  // Target Dark state tracking:
  // Handles isDark prop changes and trigger pulse events cleanly without state cancellation
  let targetDark = $state(true);
  let initialized = false;
  let lastSeenIsDark: boolean | undefined = undefined;
  let lastSeenTrigger: number | boolean | undefined = undefined;
  let previousEffectiveIsDark: boolean | undefined = undefined;

  $effect(() => {
    const curDark = Boolean(isDark);
    const curTrigger = trigger;

    if (!initialized) {
      initialized = true;
      lastSeenIsDark = curDark;
      lastSeenTrigger = curTrigger;
      targetDark = curDark;
      return;
    }

    const darkChanged = curDark !== lastSeenIsDark;
    const triggerChanged = curTrigger !== undefined && curTrigger !== lastSeenTrigger;

    lastSeenIsDark = curDark;
    lastSeenTrigger = curTrigger;

    if (darkChanged) {
      targetDark = curDark;
    } else if (triggerChanged) {
      targetDark = !targetDark;
    }
  });

  // Motion state
  let currentRot = BASE_MOON_ROT;
  let animProgress = 0; // 0 = Dark (Moon), 1 = Light (Sun)
  let startRot = BASE_MOON_ROT;
  let endRot = BASE_MOON_ROT;
  let startProgress = 0;
  let endProgress = 0;
  let animDirection = 1; // 1 = toward light, -1 = toward dark

  let animFrame: number | null = null;
  let animStartTime = 0;
  let effectiveDurationMs = 560;

  function lerp(a: number, b: number, t: number): number {
    return a + (b - a) * t;
  }

  function easeInOutCubic(t: number): number {
    return t < 0.5 ? 4 * t * t * t : 1 - Math.pow(-2 * t + 2, 3) / 2;
  }

  function easeOutQuad(t: number): number {
    return t * (2 - t);
  }

  function easeInQuad(t: number): number {
    return t * t;
  }

  function getBezierPath(p: number, dir: number): string {
    let m: number;
    let s: number;

    if (dir > 0) {
      // Dark -> Light:
      // Morph: scoop expands out to circle during p: 0.0 -> 0.70
      const morphP = Math.min(1, Math.max(0, p / 0.70));
      m = easeInOutCubic(morphP);
      // Shrink: starts early at p=0.08 and finishes at p=0.85
      // So while morphing (0.08 to 0.70), it is ALREADY shrinking concurrently!
      const shrinkP = Math.min(1, Math.max(0, (p - 0.08) / 0.77));
      s = easeInOutCubic(shrinkP);
    } else {
      // Light -> Dark:
      // Expand: circle expands from R=5 to R=10 during p: 0.85 down to 0.08
      const shrinkP = Math.min(1, Math.max(0, (p - 0.08) / 0.77));
      s = easeInOutCubic(shrinkP);
      // Morph: scoop hollows out into moon during p: 0.80 down to 0.0
      // So while growing (0.80 down to 0.08), the moon is ALREADY carving out concurrently!
      const morphP = Math.min(1, Math.max(0, p / 0.80));
      m = easeInOutCubic(morphP);
    }

    // Scale factor: scales from R=10.0 (scale=1.0) down to R=5.0 (scale=0.5) centered on (12, 12)
    const scale = 1.0 - 0.50 * s;
    const cx = 12.0;
    const cy = 12.0;

    function scX(x: number): string {
      return (cx + (x - cx) * scale).toFixed(3);
    }
    function scY(y: number): string {
      return (cy + (y - cy) * scale).toFixed(3);
    }

    const startX = scX(MOON_P0[0]);
    const startY = scY(MOON_P0[1]);

    const segs = [`M ${startX} ${startY}`];

    for (let i = 0; i < 8; i++) {
      const mb = MOON_B[i];
      const cb = CIRCLE_B[i];

      const cp1x = scX(lerp(mb[0], cb[0], m));
      const cp1y = scY(lerp(mb[1], cb[1], m));
      const cp2x = scX(lerp(mb[2], cb[2], m));
      const cp2y = scY(lerp(mb[3], cb[3], m));
      const endX = scX(lerp(mb[4], cb[4], m));
      const endY = scY(lerp(mb[5], cb[5], m));

      segs.push(`C ${cp1x} ${cp1y}, ${cp2x} ${cp2y}, ${endX} ${endY}`);
    }
    segs.push('Z');
    return segs.join(' ');
  }

  function getNextCcwTargetRot(curRot: number, toDark: boolean): number {
    // Rotation is always counter-clockwise in both directions (decreasing rot angle).
    // In Dark mode (p = 0): effective rot % 360 must be BASE_MOON_ROT (-18°).
    // In Light mode (p = 1): effective rot % 360 must be BASE_MOON_ROT - 180° (-198°).
    if (toDark) {
      return Math.floor((curRot - 1 - BASE_MOON_ROT) / 360) * 360 + BASE_MOON_ROT;
    } else {
      return Math.floor((curRot - 1 - (BASE_MOON_ROT - 180)) / 360) * 360 + (BASE_MOON_ROT - 180);
    }
  }

  function renderFrame(p: number, rot: number, dir: number) {
    if (!pathEl || !coreGroupEl) return;

    // Update core morph path
    pathEl.setAttribute('d', getBezierPath(p, dir));

    // Update CCW rotation transform around (12, 12)
    coreGroupEl.setAttribute('transform', `rotate(${rot.toFixed(2)} 12 12)`);

    // Calculate ray fraction
    let rayFrac: number;
    if (dir > 0) {
      // Dark -> Light: rays show radiating outward near end (p 0.60 -> 1.0)
      const rayP = easeOutQuad(Math.min(1, Math.max(0, (p - 0.60) / 0.40)));
      rayFrac = rayP;
    } else {
      // Light -> Dark: rays retract inward first (p 1.0 down to 0.70)
      const rayP = easeInQuad(Math.min(1, Math.max(0, (p - 0.70) / 0.30)));
      rayFrac = rayP;
    }

    // Update sun rays trim path radiating from inner center outward
    const rayOffset = -RAY_LEN * (1 - rayFrac);
    const rayOpacity = rayFrac > 0.02 ? rayFrac.toFixed(3) : '0';

    for (let i = 0; i < rayLines.length; i++) {
      const line = rayLines[i];
      if (line) {
        line.style.strokeDashoffset = `${rayOffset.toFixed(2)}px`;
        line.style.opacity = rayOpacity;
      }
    }
  }

  let animationElapsedMs = 0;
  let animationSpeed = 1;

  function tickAnimation(now: number) {
    // Integrate elapsed time so increasing speed never jumps the morph backwards.
    // Keep the fastest rate until this morph finishes, even as older waves end.
    animationSpeed = Math.min(effectiveDurationMs / 180, Math.max(animationSpeed, getThemeRevealSpeed()));
    // Snapshot work can delay a frame; retain visible motion instead of skipping it.
    animationElapsedMs += Math.min(50, Math.max(0, now - animStartTime)) * animationSpeed;
    animStartTime = now;
    const rawT = Math.min(1, animationElapsedMs / effectiveDurationMs);
    const easedT = easeInOutCubic(rawT);

    animProgress = startProgress + (endProgress - startProgress) * easedT;
    currentRot = startRot + (endRot - startRot) * easedT;

    renderFrame(animProgress, currentRot, animDirection);

    if (rawT < 1) {
      animFrame = requestAnimationFrame(tickAnimation);
    } else {
      animFrame = null;
      animProgress = endProgress;
      currentRot = endRot;

      // Normalize rotation when animation completes
      if (animDirection < 0) {
        currentRot = BASE_MOON_ROT;
      } else {
        currentRot = BASE_MOON_ROT - 180;
      }

      renderFrame(animProgress, currentRot, animDirection);
    }
  }

  function startAnimation(newTargetDark: boolean) {
    const isOff =
      animationStore.isOff ||
      (typeof document !== 'undefined' &&
        document.documentElement.classList.contains('no-animations'));

    if (isOff || !isMounted) {
      if (animFrame) {
        cancelAnimationFrame(animFrame);
        animFrame = null;
      }
      animProgress = newTargetDark ? 0 : 1;
      currentRot = newTargetDark ? BASE_MOON_ROT : BASE_MOON_ROT - 180;
      renderFrame(animProgress, currentRot, newTargetDark ? -1 : 1);
      return;
    }

    // Direction: going to dark = -1, going to light = +1
    animDirection = newTargetDark ? -1 : 1;

    // Seamless capture from current running progress and rotation
    startProgress = animProgress;
    endProgress = newTargetDark ? 0 : 1;

    startRot = currentRot;
    endRot = getNextCcwTargetRot(startRot, newTargetDark);

    animStartTime = performance.now();
    animationElapsedMs = 0;
    animationSpeed = getThemeRevealSpeed();
    effectiveDurationMs = Math.max(100, (duration ?? 0.56) * 1000);

    if (animFrame) {
      cancelAnimationFrame(animFrame);
    }
    animFrame = requestAnimationFrame(tickAnimation);
  }

  // Seed the SVG once. After mounting, renderFrame owns its geometry so a prop
  // change cannot flash the final shape before the animation's first frame.
  const initialDark = untrack(() => Boolean(isDark));
  const initialPath = getBezierPath(initialDark ? 0 : 1, initialDark ? -1 : 1);
  const initialRot = initialDark ? BASE_MOON_ROT : BASE_MOON_ROT - 180;

  onMount(() => {
    isMounted = true;
    animProgress = targetDark ? 0 : 1;
    currentRot = targetDark ? BASE_MOON_ROT : BASE_MOON_ROT - 180;
    renderFrame(animProgress, currentRot, targetDark ? -1 : 1);
  });

  $effect(() => {
    const cur = targetDark;
    untrack(() => {
      if (previousEffectiveIsDark === undefined) {
        previousEffectiveIsDark = cur;
        animProgress = cur ? 0 : 1;
        currentRot = cur ? BASE_MOON_ROT : BASE_MOON_ROT - 180;
        renderFrame(animProgress, currentRot, cur ? -1 : 1);
        return;
      }

      if (cur !== previousEffectiveIsDark) {
        previousEffectiveIsDark = cur;
        startAnimation(cur);
      }
    });
  });

  onDestroy(() => {
    if (animFrame) {
      cancelAnimationFrame(animFrame);
      animFrame = null;
    }
  });
</script>

<span
  class="theme-toggle-icon {customClass}"
  data-theme-reveal-live
  class:is-dark={targetDark}
  style="--icon-size: {size}px; {style}"
  role="img"
  aria-hidden={title ? undefined : 'true'}
  {title}
>
  <svg viewBox="0 0 24 24" aria-hidden="true" focusable="false">
    <!-- Center Core Shape (Mirrored Fluent Moon morphing to Sun circle via pure cubic beziers) -->
    <g
      bind:this={coreGroupEl}
      class="core-rot-group"
      transform="rotate({initialRot} 12 12)"
    >
      <path
        bind:this={pathEl}
        d={initialPath}
        fill="currentColor"
      />
    </g>

    <!-- Sun Rays Group (stationary / radial trim path) -->
    <g
      class="rays-group"
      stroke="currentColor"
      stroke-width="1.8"
      stroke-linecap="round"
    >
      {#each RAY_COORDS as r, i}
        <line
          bind:this={rayLines[i]}
          class="sun-ray"
          x1={r.x1}
          y1={r.y1}
          x2={r.x2}
          y2={r.y2}
        />
      {/each}
    </g>
  </svg>
</span>

<style>
  .theme-toggle-icon {
    display: inline-flex;
    align-items: center;
    justify-content: center;
    width: var(--icon-size, 18px);
    height: var(--icon-size, 18px);
    position: relative;
    color: currentColor;
    flex-shrink: 0;
    vertical-align: middle;
    user-select: none;
    overflow: visible;
  }

  .theme-toggle-icon svg {
    width: 100%;
    height: 100%;
    display: block;
    overflow: visible;
  }

  .core-rot-group {
    will-change: transform;
  }

  .core-rot-group path {
    will-change: d;
  }

  .rays-group {
    stroke: currentColor;
    stroke-width: 1.8px;
    stroke-linecap: round;
    overflow: visible;
  }

  .sun-ray {
    stroke-dasharray: 2.4px 2.4px;
    will-change: stroke-dashoffset, opacity;
  }
</style>
