<script module lang="ts">
  import type { IconMetadata } from './iconData';

  export const metadata: IconMetadata = {
    name: 'Refresh (Clockwise)',
    description: 'Smooth tail brake, continuous spin & arrowhead unfold',
    params: []
  };
</script>

<script lang="ts">
  import { onDestroy, untrack } from 'svelte';

  interface Props {
    loading?: boolean;
    size?: number;
    class?: string;
    style?: string;
    title?: string;
    duration?: number;
    trigger?: number | boolean;
  }

  let {
    loading = false,
    size = 16,
    class: customClass = '',
    style = '',
    title,
    duration = 0.95,
    trigger
  }: Props = $props();

  let circleEl: SVGCircleElement | null = $state(null);
  let wingsGroupEl: SVGGElement | null = $state(null);
  let wing1El: SVGLineElement | null = $state(null);
  let wing2El: SVGLineElement | null = $state(null);

  // Motion Model: continuous world angles (degrees clockwise from 3 o'clock)
  // Idle: tail = 36.87° (4 o'clock), head = 323.13° (2 o'clock)
  let animState = 'IDLE'; // 'IDLE' | 'STARTING' | 'SPINNING' | 'STOPPING' | 'EXTENDING'
  let tail = 36.87;
  let head = 323.13;
  let wingTrim = 0; // 0 = fully extended (4px), 4 = retracted into apex

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

  let startInitialSpan = 286.26;
  let startInitialWingTrim = 0;
  let spinOnceTimer: ReturnType<typeof setTimeout> | null = null;
  let stopRequested = false;

  function render() {
    if (!circleEl || !wingsGroupEl) return;

    // Normalize tail angle to [0, 360)
    const tailMod = ((tail % 360) + 360) % 360;
    // Cap span at 286.26° so the stroke never overruns into a full closed circle
    const span = Math.min(286.26, Math.max(1, head - tail));
    // Circumference = 2 * PI * 8 ≈ 50.2655px
    const arcPx = (span / 360) * 50.2655;

    // Rotate circle to tail position; stroke begins at tail and extends clockwise
    circleEl.setAttribute('transform', `rotate(${tailMod.toFixed(2)}, 12, 12)`);
    circleEl.style.strokeDasharray = `${arcPx.toFixed(2)}px 100px`;
    circleEl.style.strokeDashoffset = '0px';

    // The chevron wings follow the head position (wings drawn at 323.13° apex)
    const headRot = (((head - 323.13) % 360) + 360) % 360;
    wingsGroupEl.setAttribute('transform', `rotate(${headRot.toFixed(2)}, 12, 12)`);

    const trimStr = `${wingTrim.toFixed(2)}px`;
    if (wing1El) wing1El.style.strokeDashoffset = trimStr;
    if (wing2El) wing2El.style.strokeDashoffset = trimStr;
  }

  function initiateStop() {
    animState = 'STOPPING';
    stateStartTime = performance.now();

    // Current tail position normalized to [0, 360)
    const curTailMod = ((tail % 360) + 360) % 360;
    // Calculate exact forward distance to 4 o'clock (36.87°)
    let rem = 36.87 - curTailMod;
    if (rem <= 0) rem += 360;
    if (rem < 90) rem += 360; // Generous runway so it completes revolution smoothly
    stopDistRem = rem;
    stopTargetTail = tail + rem;
    inTailBrake = false;
  }

  function start() {
    stopRequested = false;
    if (animState === 'SPINNING' || animState === 'STARTING') return;

    // Seamless capture: preserve current span and wingTrim so re-triggering mid-animation NEVER snaps
    startInitialSpan = Math.max(1, head - tail);
    startInitialWingTrim = wingTrim;

    animState = 'STARTING';
    stateStartTime = performance.now();
    lastTime = performance.now();
    if (!rafId) {
      rafId = requestAnimationFrame(tick);
    }
  }

  function stop() {
    if (animState === 'IDLE' || animState === 'EXTENDING') return;
    if (animState === 'STARTING') {
      // Allow STARTING (300ms) to finish contracting to 65° before initiating stop
      stopRequested = true;
      return;
    }
    initiateStop();
  }

  // Imperative 1-shot spin for instantaneous clicks (e.g. reset settings)
  // Fully spam-proof: debounces and extends continuous spinning smoothly on rapid clicks
  export function spinOnce() {
    if (spinOnceTimer) {
      clearTimeout(spinOnceTimer);
      spinOnceTimer = null;
    }

    if (animState === 'SPINNING' || animState === 'STARTING') {
      // Already spinning: extend spin smoothly past this rapid click
      spinOnceTimer = setTimeout(() => {
        stop();
        spinOnceTimer = null;
      }, 300);
      return;
    }

    // Seamless start from IDLE, STOPPING, or EXTENDING without any snapping
    start();
    spinOnceTimer = setTimeout(() => {
      stop();
      spinOnceTimer = null;
    }, 350);
  }

  function tick(now: number) {
    const dt = Math.min((now - lastTime) / 1000, 0.1);
    lastTime = now;

    const baseDuration = duration > 0 ? duration : 0.95;
    const omega = 360 / baseDuration;

    if (animState === 'STARTING') {
      const elapsed = now - stateStartTime;
      const startDuration = 300; // ms
      const p = Math.min(1, elapsed / startDuration);

      // Head moves forward continuously at natural spinning speed omega
      head += omega * dt;

      // Chevron wings trim into apex tip (startInitialWingTrim -> 4px) over 150ms
      const pWing = Math.min(1, elapsed / 150);
      wingTrim = startInitialWingTrim + (4 - startInitialWingTrim) * pWing;

      // Smoothstep: zero jerk at both start and finish, contracts smoothly from startInitialSpan down to 65°
      const ease = p * p * (3 - 2 * p);
      const span = startInitialSpan - (startInitialSpan - 65) * ease;
      tail = head - span;

      if (p >= 1) {
        animState = 'SPINNING';
        spinStartTime = now;
        if (stopRequested) {
          stopRequested = false;
          initiateStop();
        }
      }
    } else if (animState === 'SPINNING') {
      // 1.5s cycle period allows deep shortening (65°) while preserving high min velocity (>= 242°/s) with no pause
      const cycleTime = 1500;
      const u = (((now - spinStartTime) % cycleTime) / cycleTime) * Math.PI * 2;

      // Dynamic breathing: shortens crisply to 65° and expands to 195°
      const span = 130 - 65 * Math.cos(u);

      // Arc center advances continuously forward at omega
      const center = ((head + tail) / 2) + omega * dt;
      head = center + span / 2;
      tail = center - span / 2;
      wingTrim = 4;
    } else if (animState === 'STOPPING') {
      const S_TAIL = 60; // 60° smooth deceleration zone for tail
      const T_tail = (2000 * S_TAIL) / omega; // ms duration for tail braking

      if (!inTailBrake) {
        // Cruise forward at full speed omega until tail is 60° from locking
        const distToBrake = stopDistRem - S_TAIL;
        const step = Math.min(omega * dt, distToBrake);
        tail += step;
        head += step;
        stopDistRem -= step;
        wingTrim = 4;

        if (stopDistRem <= S_TAIL + 0.001) {
          inTailBrake = true;
          tailBrakeStartTime = now;
          tailBrakeStartPos = tail;
        }
      } else {
        // Smoothly ease in the locking of the tail to 4 o'clock (v: omega -> 0, quadratic ease-out)
        const elapsed = now - tailBrakeStartTime;
        const u = Math.min(1, elapsed / T_tail);
        tail = tailBrakeStartPos + S_TAIL * (1 - Math.pow(1 - u, 2));

        // Head maintains natural forward spinning momentum omega
        head += omega * dt;

        // Chevron wings begin emerging as the tail eases into its lock
        wingTrim = 4 - 2 * u;

        if (u >= 1) {
          // Tail lands gently and precisely at 4 o'clock with 0 velocity
          tail = stopTargetTail;
          extStartHead = head;

          // Target head angle is always 286.26° ahead of the parked tail (2 o'clock)
          const targetHead = stopTargetTail + 286.26;
          D = Math.max(0, targetHead - extStartHead);

          if (D <= 0.5) {
            tail = 36.87;
            head = 323.13;
            wingTrim = 0;
            animState = 'IDLE';
            render();
            rafId = null;
            return;
          }

          // Delayed deceleration for head return: coast at omega for first 30%, then ease out
          const p0 = 0.3;
          extDuration = Math.max(300, (2000 * D) / (omega * (1 + p0)));

          animState = 'EXTENDING';
          stateStartTime = now;
        }
      }
    } else if (animState === 'EXTENDING') {
      tail = stopTargetTail; // Firmly locked at 4 o'clock (normalized = 36.87°)

      const elapsed = now - stateStartTime;
      const p = Math.min(1, elapsed / extDuration);

      // Delayed deceleration: coast at full spinning speed omega for first 30%, then feather land to 0
      const p0 = 0.3;
      let dist: number;
      if (p <= p0) {
        dist = ((2 * D) / (1 + p0)) * p;
      } else {
        const u = (p - p0) / (1 - p0);
        dist = ((2 * D) / (1 + p0)) * (p0 + (1 - p0) * (u / 2 + Math.sin(Math.PI * u) / (2 * Math.PI)));
      }
      head = extStartHead + dist;

      // Chevron wings finish unfolding over the first half of extending
      const pWing = Math.min(1, p / 0.5);
      wingTrim = 2 * (1 - pWing);

      if (p >= 1) {
        tail = 36.87;
        head = 323.13;
        wingTrim = 0;
        animState = 'IDLE';
        render();
        rafId = null;
        return; // Completely stop rAF loop (0% CPU)
      }
    }

    render();
    rafId = requestAnimationFrame(tick);
  }

  // Initial render when elements become bound in DOM
  $effect(() => {
    if (circleEl && wingsGroupEl && animState === 'IDLE') {
      render();
    }
  });

  // React strictly to external loading prop changes
  $effect(() => {
    const active = loading;
    untrack(() => {
      if (active) {
        start();
      } else {
        stop();
      }
    });
  });

  let previousTrigger: number | boolean | undefined = undefined;
  $effect(() => {
    const cur = trigger;
    if (cur !== undefined && cur !== previousTrigger) {
      previousTrigger = cur;
      untrack(() => {
        spinOnce();
      });
    }
  });

  onDestroy(() => {
    if (rafId) {
      cancelAnimationFrame(rafId);
      rafId = null;
    }
    if (spinOnceTimer) {
      clearTimeout(spinOnceTimer);
      spinOnceTimer = null;
    }
  });
</script>

<span
  class="refresh-icon {customClass}"
  style="--icon-size: {size}px; {style}"
  role="img"
  aria-hidden={title ? undefined : 'true'}
  {title}
>
  <svg
    viewBox="0 0 24 24"
    width={size}
    height={size}
    aria-hidden="true"
    focusable="false"
  >
    <!-- Full 360° circle: allows seamless, unbroken arc breathing without artificial gaps -->
    <circle
      bind:this={circleEl}
      class="arc-stroke"
      cx="12"
      cy="12"
      r="8"
      transform="rotate(36.87, 12, 12)"
    />
    <!-- Chevron Wings meeting at apex tip, rotated around center (12, 12) to stay attached to head -->
    <g
      bind:this={wingsGroupEl}
      class="wings-group"
      transform="rotate(0, 12, 12)"
    >
      <!-- Vertical Wing (Up) -->
      <line
        bind:this={wing1El}
        class="chevron-wing"
        x1="18.4"
        y1="7.2"
        x2="18.4"
        y2="3.2"
      />
      <!-- Horizontal Wing (Left) -->
      <line
        bind:this={wing2El}
        class="chevron-wing"
        x1="18.4"
        y1="7.2"
        x2="14.4"
        y2="7.2"
      />
    </g>
  </svg>
</span>

<style>
  .refresh-icon {
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

  .refresh-icon svg {
    width: 100%;
    height: 100%;
    display: block;
    overflow: visible;
  }

  /* Concentric circular arc: exact center (12, 12), radius 8.0, length ~40px */
  .arc-stroke {
    fill: none;
    stroke: currentColor;
    stroke-width: 1.85;
    stroke-linecap: round;
    stroke-dasharray: 40px 100px;
    stroke-dashoffset: 0px;
  }

  /* Chevron wings meeting at apex tip (18.4, 7.2) - pure trim path, solid opacity */
  .chevron-wing {
    fill: none;
    stroke: currentColor;
    stroke-width: 1.85;
    stroke-linecap: round;
    stroke-linejoin: round;
    stroke-dasharray: 4px 50px;
    stroke-dashoffset: 0px;
  }

  .wings-group {
  }
</style>
