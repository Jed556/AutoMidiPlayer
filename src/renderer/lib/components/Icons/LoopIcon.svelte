<script module lang="ts">
  import type { IconMetadata } from './iconData';

  export const metadata: IconMetadata = {
    name: 'Loop',
    description: 'Track follow-through & line breathing along rounded rectangle with fancy 1-song loop entrance',
    params: [
      {
        type: 'option',
        id: 'loopMode',
        label: 'Loop State',
        defaultValue: 'off',
        options: [
          { label: 'Disabled', value: 'off', props: { mode: 'off', active: false } },
          { label: 'Playlist (All)', value: 'all', props: { mode: 'all', active: true } },
          { label: 'Track (One)', value: 'single', props: { mode: 'single', active: true } }
        ]
      }
    ]
  };
</script>

<script lang="ts">
  import { onDestroy, untrack } from 'svelte';

  interface Props {
    mode?: 'off' | 'all' | 'single';
    active?: boolean;
    loading?: boolean;
    size?: number;
    class?: string;
    style?: string;
    title?: string;
    duration?: number;
    trigger?: number | boolean;
  }

  let {
    mode = 'off',
    active,
    loading = false,
    size = 14,
    class: customClass = '',
    style = '',
    title,
    duration = 0.45,
    trigger
  }: Props = $props();

  // Track geometry constants
  // Track has perimeter P = 27 + 6*PI ≈ 45.8496
  // Idle state: tail = 0 (at (5.0, 12.75)), head = 42.8496 (at apex (8.0, 12.75))
  // Gap between tail (x=5.0) and apex (x=8.0) is 3.0px.
  const R = 3.0;
  const P = 27 + 6 * Math.PI;
  const IDLE_TAIL = 0;
  const IDLE_HEAD = 24.0 + 6 * Math.PI; // 42.8495559...
  const FULL_SPAN = IDLE_HEAD;
  const MIN_SPAN = 20.0;
  const DELTA_SPAN = FULL_SPAN - MIN_SPAN;
  const IDLE_CENTER = FULL_SPAN / 2; // 21.4247779...

  // Unique mask ID for multiple instances on page
  const maskId = `loop-mask-${Math.random().toString(36).slice(2, 9)}`;

  let trackEl: SVGPathElement | null = $state(null);
  let arrowGroupEl: SVGGElement | null = $state(null);
  let maskCutoutEl: SVGRectElement | null = $state(null);
  let digitGroupEl: SVGGElement | null = $state(null);

  // Effective mode & active state
  let effectiveMode = $derived.by(() => {
    if (mode === 'single') return 'single';
    if (mode === 'all') return 'all';
    if (mode === 'off') return active ? 'all' : 'off';
    return active ? 'all' : 'off';
  });

  let isEffectivelyActive = $derived(effectiveMode !== 'off');

  // Animation State Model for Rounded Rectangle Follow-Through
  // States: 'IDLE' | 'STARTING' | 'SPINNING' | 'STOPPING' | 'EXTENDING'
  let animState = 'IDLE';
  let tail = IDLE_TAIL;
  let head = IDLE_HEAD;

  let rafId: number | null = null;
  let lastTime = 0;
  let stateStartTime = 0;

  let stopDistRem = 0;
  let stopTargetTail = 0;
  let inTailBrake = false;
  let tailBrakeStartTime = 0;
  let tailBrakeStartPos = 0;
  let extStartHead = 0;
  let extDuration = 0;
  let D = 0;
  let spinStartTime = 0;

  let startInitialSpan = IDLE_HEAD;

  // Single Loop ("1") transition state & spring animation
  let singleProgress = $state(untrack(() => (mode === 'single' ? 1.0 : 0.0)));
  let singleRafId: number | null = null;
  let bounceRafId: number | null = null;

  // Track arc-length parametrization: returns (x, y) coordinates and tangent angle in SVG screen coordinates
  // (0° = right, 90° = down, 180° = left, -90° = up)
  function getPointOnTrack(s: number) {
    let d = ((s % P) + P) % P;

    const L1 = 1.25;
    const L2 = L1 + 1.5 * Math.PI;
    const L3 = L2 + 5.0;
    const L4 = L3 + 1.5 * Math.PI;
    const L5 = L4 + 8.5;
    const L6 = L5 + 1.5 * Math.PI;
    const L7 = L6 + 5.0;
    const L8 = L7 + 1.5 * Math.PI;
    const L9 = L8 + 4.25; // Apex at x=8.0, y=12.75

    if (d <= L1) {
      // 1. Bottom-Left Straight from (5.0, 12.75) to (3.75, 12.75): moving LEFT (180°)
      const t = d / 1.25;
      return { x: 5.0 - 1.25 * t, y: 12.75, angle: 180 };
    } else if (d <= L2) {
      // 2. Bottom-Left Corner Arc from (3.75, 12.75) to (0.75, 9.75): turning from LEFT (180°) to UP (-90°)
      const u = (d - L1) / (1.5 * Math.PI);
      const t = u * (Math.PI / 2);
      const dx = -Math.cos(t);
      const dy = -Math.sin(t);
      const angle = Math.atan2(dy, dx) * 180 / Math.PI;
      return { x: 3.75 - R * Math.sin(t), y: 9.75 + R * Math.cos(t), angle };
    } else if (d <= L3) {
      // 3. Left Straight from (0.75, 9.75) to (0.75, 4.75): moving UP (-90°)
      const t = (d - L2) / 5.0;
      return { x: 0.75, y: 9.75 - 5.0 * t, angle: -90 };
    } else if (d <= L4) {
      // 4. Top-Left Corner Arc from (0.75, 4.75) to (3.75, 1.75): turning from UP (-90°) to RIGHT (0°)
      const u = (d - L3) / (1.5 * Math.PI);
      const t = u * (Math.PI / 2);
      const dx = Math.sin(t);
      const dy = -Math.cos(t);
      const angle = Math.atan2(dy, dx) * 180 / Math.PI;
      return { x: 3.75 - R * Math.cos(t), y: 4.75 - R * Math.sin(t), angle };
    } else if (d <= L5) {
      // 5. Top Straight from (3.75, 1.75) to (12.25, 1.75): moving RIGHT (0°)
      const t = (d - L4) / 8.5;
      return { x: 3.75 + 8.5 * t, y: 1.75, angle: 0 };
    } else if (d <= L6) {
      // 6. Top-Right Corner Arc from (12.25, 1.75) to (15.25, 4.75): turning from RIGHT (0°) to DOWN (90°)
      const u = (d - L5) / (1.5 * Math.PI);
      const t = u * (Math.PI / 2);
      const dx = Math.cos(t);
      const dy = Math.sin(t);
      const angle = Math.atan2(dy, dx) * 180 / Math.PI;
      return { x: 12.25 + R * Math.sin(t), y: 4.75 - R * Math.cos(t), angle };
    } else if (d <= L7) {
      // 7. Right Straight from (15.25, 4.75) to (15.25, 9.75): moving DOWN (90°)
      const t = (d - L6) / 5.0;
      return { x: 15.25, y: 4.75 + 5.0 * t, angle: 90 };
    } else if (d <= L8) {
      // 8. Bottom-Right Corner Arc from (15.25, 9.75) to (12.25, 12.75): turning from DOWN (90°) to LEFT (180°)
      const u = (d - L7) / (1.5 * Math.PI);
      const t = u * (Math.PI / 2);
      const dx = -Math.sin(t);
      const dy = Math.cos(t);
      const angle = Math.atan2(dy, dx) * 180 / Math.PI;
      return { x: 12.25 + R * Math.cos(t), y: 9.75 + R * Math.sin(t), angle };
    } else if (d <= L9) {
      // 9. Bottom Straight to Apex from (12.25, 12.75) to (8.0, 12.75): moving LEFT (180°)
      const t = (d - L8) / 4.25;
      return { x: 12.25 - 4.25 * t, y: 12.75, angle: 180 };
    } else {
      // 10. Gap to Tail from (8.0, 12.75) to (5.0, 12.75): moving LEFT (180°)
      const t = (d - L9) / 3.0;
      return { x: 8.0 - 3.0 * t, y: 12.75, angle: 180 };
    }
  }

  function renderTrack() {
    if (!trackEl || !arrowGroupEl) return;

    const tailMod = ((tail % P) + P) % P;
    const span = Math.max(0.5, head - tail);

    // Apply seamless 360° dasharray without seam jumps
    if (tailMod + span <= P) {
      trackEl.style.strokeDasharray = `0px ${tailMod.toFixed(2)}px ${span.toFixed(2)}px 100px`;
    } else {
      const A = (tailMod + span) - P;
      const B = P - span;
      const C = P - tailMod;
      trackEl.style.strokeDasharray = `${A.toFixed(2)}px ${B.toFixed(2)}px ${C.toFixed(2)}px 100px`;
    }
    trackEl.style.strokeDashoffset = '0px';

    // Position arrowhead at head coordinates along track with tangent rotation
    const ptHead = getPointOnTrack(head);
    const rot = ptHead.angle - 180;
    arrowGroupEl.setAttribute('transform', `translate(${ptHead.x.toFixed(2)}, ${ptHead.y.toFixed(2)}) rotate(${rot.toFixed(1)})`);
  }

  function renderSingle() {
    if (!maskCutoutEl || !digitGroupEl) return;

    const p = singleProgress;

    // 1. Smooth stroke cut: expands gap from 0px to 7.0px centered at x=8.0
    const easeCut = 1 - Math.pow(1 - p, 3);
    const gapWidth = easeCut * 7.0;
    const maskX = 8.0 - gapWidth / 2;
    maskCutoutEl.setAttribute('width', gapWidth.toFixed(2));
    maskCutoutEl.setAttribute('x', maskX.toFixed(2));

    // 2. Fancy "1" Digit entrance: drop-in with playful spring bounce
    if (p <= 0.001) {
      digitGroupEl.style.opacity = '0';
      digitGroupEl.style.transform = 'translateY(-3px) scale(0)';
    } else {
      const pDigit = Math.min(1, p / 0.85);
      const spring = Math.sin(pDigit * Math.PI * 1.25) * 0.28 * (1 - pDigit);
      const scale = pDigit + spring;
      const translateY = -2.5 * (1 - pDigit);
      digitGroupEl.style.opacity = Math.min(1, p / 0.35).toFixed(2);
      digitGroupEl.style.transform = `translateY(${translateY.toFixed(2)}px) scale(${scale.toFixed(3)})`;
    }
  }

  function animateSingleTo(target: number) {
    if (singleRafId) {
      cancelAnimationFrame(singleRafId);
      singleRafId = null;
    }

    const startVal = singleProgress;
    const startTime = performance.now();
    const currentDuration = duration > 0 ? duration : 0.45;
    const animDuration = Math.round((target > startVal ? 220 : 180) * (currentDuration / 0.45));

    function step(now: number) {
      const elapsed = now - startTime;
      const progress = Math.min(1, elapsed / animDuration);
      singleProgress = startVal + (target - startVal) * progress;
      renderSingle();

      if (progress < 1) {
        singleRafId = requestAnimationFrame(step);
      } else {
        singleProgress = target;
        renderSingle();
        singleRafId = null;
      }
    }

    singleRafId = requestAnimationFrame(step);
  }

  // Playful spring bounce when already in single mode and clicked/triggered
  function fancyBounce() {
    if (bounceRafId) {
      cancelAnimationFrame(bounceRafId);
      bounceRafId = null;
    }
    if (!digitGroupEl) return;

    const startTime = performance.now();
    const currentDuration = duration > 0 ? duration : 0.45;
    const bounceDuration = Math.round(220 * (currentDuration / 0.45));

    function step(now: number) {
      const elapsed = now - startTime;
      const p = Math.min(1, elapsed / bounceDuration);
      // Spring pulse from 1.0 -> 1.3 -> 0.94 -> 1.0
      const pulse = Math.sin(p * Math.PI * 1.5) * 0.28 * (1 - p);
      const scale = 1.0 + pulse;
      if (digitGroupEl) {
        digitGroupEl.style.transform = `translateY(0px) scale(${scale.toFixed(3)})`;
      }

      if (p < 1) {
        bounceRafId = requestAnimationFrame(step);
      } else {
        if (digitGroupEl) {
          digitGroupEl.style.transform = 'translateY(0px) scale(1)';
        }
        bounceRafId = null;
      }
    }

    bounceRafId = requestAnimationFrame(step);
  }

  export function spinOnce() {
    if (loading) return;

    if (animState === 'SPINNING' || animState === 'STARTING' || animState === 'STOPPING') {
      // Re-triggering / rapid clicking: ensure target is next upcoming lap
      const curTailMod = ((tail % P) + P) % P;
      let rem = P - curTailMod;
      if (rem < 6.0) rem += P;
      stopDistRem = rem;
      stopTargetTail = tail + rem;
      inTailBrake = false;
      animState = 'STOPPING';
      stateStartTime = performance.now();
      return;
    }

    startInitialSpan = Math.max(1, head - tail);
    animState = 'STARTING';
    stateStartTime = performance.now();
    lastTime = performance.now();
    stopTargetTail = P;

    if (!rafId) {
      rafId = requestAnimationFrame(tick);
    }
  }

  function start() {
    if (animState === 'SPINNING' || animState === 'STARTING') return;
    startInitialSpan = Math.max(1, head - tail);
    animState = 'STARTING';
    stateStartTime = performance.now();
    lastTime = performance.now();
    if (!rafId) {
      rafId = requestAnimationFrame(tick);
    }
  }

  function stop() {
    if (animState === 'IDLE' || animState === 'EXTENDING') return;
    animState = 'STOPPING';
    stateStartTime = performance.now();

    const curTailMod = ((tail % P) + P) % P;
    let rem = P - curTailMod;
    if (rem <= 0) rem += P;
    if (rem < 6.0) rem += P; // Smooth deceleration runway
    stopDistRem = rem;
    stopTargetTail = tail + rem;
    inTailBrake = false;
  }

  export function triggerAnimation() {
    if (effectiveMode === 'single') {
      fancyBounce();
    } else {
      spinOnce();
    }
  }

  function tick(now: number) {
    const dt = Math.min((now - lastTime) / 1000, 0.1);
    lastTime = now;

    const baseDuration = duration > 0 ? duration : 0.45;
    const omega = P / baseDuration; // px/sec along track

    if (animState === 'STARTING') {
      const elapsed = now - stateStartTime;
      const startDuration = Math.min(80, baseDuration * 180); // ~75ms
      const p = Math.min(1, elapsed / startDuration);

      head += omega * dt;

      // Smoothstep line contraction from startInitialSpan to 18px
      const ease = p * p * (3 - 2 * p);
      const span = startInitialSpan - (startInitialSpan - 18) * ease;
      tail = head - span;

      if (p >= 1) {
        if (loading) {
          animState = 'SPINNING';
          spinStartTime = now;
        } else {
          // In 1-shot mode, transition directly to single-lap stop target
          animState = 'STOPPING';
          stateStartTime = now;
          stopDistRem = stopTargetTail - tail;
          inTailBrake = false;
        }
      }
    } else if (animState === 'SPINNING') {
      const cycleTime = 1000;
      const u = (((now - spinStartTime) % cycleTime) / cycleTime) * Math.PI * 2;
      const span = 22 - 4 * Math.cos(u);

      const center = ((head + tail) / 2) + omega * dt;
      head = center + span / 2;
      tail = center - span / 2;
    } else if (animState === 'STOPPING') {
      const S_TAIL = 6.0; // 6px smooth deceleration zone for tail
      const T_tail = (2000 * S_TAIL) / omega; // ms duration for tail braking

      if (!inTailBrake) {
        const distToBrake = stopDistRem - S_TAIL;
        const step = Math.min(omega * dt, distToBrake);
        tail += step;
        head += step;
        stopDistRem -= step;

        if (stopDistRem <= S_TAIL + 0.001) {
          inTailBrake = true;
          tailBrakeStartTime = now;
          tailBrakeStartPos = tail;
        }
      } else {
        const elapsed = now - tailBrakeStartTime;
        const u = Math.min(1, elapsed / T_tail);
        tail = tailBrakeStartPos + S_TAIL * (1 - Math.pow(1 - u, 2));
        head += omega * dt;

        if (u >= 1) {
          tail = stopTargetTail;
          extStartHead = head;

          const curHeadMod = ((head % P) + P) % P;
          D = IDLE_HEAD - curHeadMod;
          if (D <= 0) D += P;

          const p0 = 0.3;
          extDuration = Math.max(80, (2000 * D) / (omega * (1 + p0)));

          animState = 'EXTENDING';
          stateStartTime = now;
        }
      }
    } else if (animState === 'EXTENDING') {
      tail = stopTargetTail;

      const elapsed = now - stateStartTime;
      const p = Math.min(1, elapsed / extDuration);

      const p0 = 0.3;
      let dist: number;
      if (p <= p0) {
        dist = ((2 * D) / (1 + p0)) * p;
      } else {
        const u = (p - p0) / (1 - p0);
        dist = ((2 * D) / (1 + p0)) * (p0 + (1 - p0) * (u / 2 + Math.sin(Math.PI * u) / (2 * Math.PI)));
      }
      head = extStartHead + dist;

      if (p >= 1) {
        tail = IDLE_TAIL;
        head = IDLE_HEAD;
        animState = 'IDLE';
        renderTrack();
        rafId = null;
        return;
      }
    }

    renderTrack();
    rafId = requestAnimationFrame(tick);
  }

  // Initial render when elements become bound in DOM
  $effect(() => {
    if (trackEl && arrowGroupEl && animState === 'IDLE') {
      renderTrack();
    }
    if (maskCutoutEl && digitGroupEl) {
      renderSingle();
    }
  });

  // React strictly to external loading prop changes (continuous spinning)
  $effect(() => {
    const activeLoading = loading;
    untrack(() => {
      if (activeLoading) {
        start();
      } else {
        stop();
      }
    });
  });

  // React to mode / active state changes
  let previousMode: string | undefined = undefined;
  $effect(() => {
    const curMode = effectiveMode;
    untrack(() => {
      if (previousMode === undefined) {
        previousMode = curMode;
        if (curMode === 'single') {
          singleProgress = 1.0;
          renderSingle();
        }
        return;
      }
      if (curMode !== previousMode) {
        const wasSingle = previousMode === 'single';
        const isSingle = curMode === 'single';
        previousMode = curMode;

        if (isSingle) {
          // Transition into single mode: cut stroke smoothly & pop in "1"
          animateSingleTo(1.0);
        } else if (wasSingle) {
          // Transition away from single mode: close stroke gap, fade out "1" & run line follow-through
          animateSingleTo(0.0);
          spinOnce();
        } else {
          // Transition between 'off' and 'all': follow-through animation
          spinOnce();
        }
      }
    });
  });

  // React to trigger prop changes
  let previousTrigger: number | boolean | undefined = undefined;
  $effect(() => {
    const cur = trigger;
    untrack(() => {
      if (cur !== undefined && cur !== previousTrigger) {
        previousTrigger = cur;
        triggerAnimation();
      }
    });
  });

  onDestroy(() => {
    if (rafId) {
      cancelAnimationFrame(rafId);
      rafId = null;
    }
    if (singleRafId) {
      cancelAnimationFrame(singleRafId);
      singleRafId = null;
    }
    if (bounceRafId) {
      cancelAnimationFrame(bounceRafId);
      bounceRafId = null;
    }
  });
</script>

<span
  class="loop-icon {customClass}"
  class:active={isEffectivelyActive}
  style="--icon-size: {size}px; {style}"
  role="img"
  aria-hidden={title ? undefined : 'true'}
  {title}
>
  <svg
    viewBox="0 0 16 16"
    width={size}
    height={size}
    aria-hidden="true"
    focusable="false"
  >
    <defs>
      <!-- Mask for RepeatOne top gap: cuts [4.5, 11.5] on the top bar dynamically with smooth transition -->
      <mask id={maskId}>
        <rect width="16" height="16" fill="white" />
        <rect
          bind:this={maskCutoutEl}
          x="8.0"
          y="0.5"
          width="0"
          height="2.5"
          fill="black"
        />
      </mask>
    </defs>

    <!-- Rounded rectangle track: full closed loop from tail (5.0, 12.75) with stroke-width 1.5 -->
    <path
      bind:this={trackEl}
      class="track-stroke"
      mask="url(#{maskId})"
      d="M 5 12.75 L 3.75 12.75 A 3 3 0 0 1 0.75 9.75 L 0.75 4.75 A 3 3 0 0 1 3.75 1.75 L 12.25 1.75 A 3 3 0 0 1 15.25 4.75 L 15.25 9.75 A 3 3 0 0 1 12.25 12.75 L 5 12.75 Z"
    />

    <!-- Chevron wings meeting at apex tip, translates & rotates around track trajectory -->
    <g
      bind:this={arrowGroupEl}
      class="arrow-group"
      transform="translate(8.0, 12.75) rotate(0)"
    >
      <path
        class="chevron-wings"
        d="M 2.8 -2.8 L 0 0 L 2.8 2.8"
      />
    </g>

    <!-- Fancy "1" Digit for RepeatOne with spring pop-in & entrance drop -->
    <g
      bind:this={digitGroupEl}
      class="digit-group"
      style="transform-origin: 8px 4.5px; opacity: 0; transform: translateY(-3px) scale(0);"
    >
      <path
        class="digit-one"
        d="m8 1.85.77.694H6.095V1.488q1.046-.077 1.507-.385.474-.308.583-.913h1.32V8H8z M8.77 2.544 8 1.85v.693z"
      />
    </g>
  </svg>
</span>

<style>
  .loop-icon {
    display: inline-flex;
    align-items: center;
    justify-content: center;
    width: var(--icon-size, 16px);
    height: var(--icon-size, 16px);
    position: relative;
    color: currentColor;
    flex-shrink: 0;
    vertical-align: middle;
    user-select: none;
    overflow: visible;
  }

  .loop-icon.active {
    color: var(--accent-color, #1db954);
  }

  .loop-icon svg {
    width: 100%;
    height: 100%;
    display: block;
    overflow: visible;
  }

  .track-stroke {
    fill: none;
    stroke: currentColor;
    stroke-width: 1.5;
    stroke-linecap: butt;
    stroke-dasharray: 42.85px 100px;
    stroke-dashoffset: 0px;
  }

  .chevron-wings {
    fill: none;
    stroke: currentColor;
    stroke-width: 1.5;
    stroke-linecap: round;
    stroke-linejoin: miter;
    stroke-miterlimit: 2;
  }

  .digit-group {
    transform-origin: 8px 4.5px;
    will-change: transform, opacity;
  }

  .digit-one {
    fill: currentColor;
  }
</style>
