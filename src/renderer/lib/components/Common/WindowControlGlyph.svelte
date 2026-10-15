<script module lang="ts">
  export interface WindowControlIconProps {
    maximized?: boolean;
    hovered?: boolean;
    trigger?: number | boolean;
    size?: number;
    class?: string;
    style?: string;
    title?: string;
    duration?: number;
  }
</script>

<script lang="ts">
  import { onDestroy, onMount, untrack } from 'svelte';
  import { animationStore } from '../../state/animationStore.svelte';
  import CrossStrokes, { crossStrokeProgress } from './CrossStrokes.svelte';

  let {
    kind, maximized = false, hovered = false, trigger, size = 12,
    class: className = '', style = '', title, duration
  }: WindowControlIconProps & { kind: 'close' | 'maximize' | 'minimize' } = $props();
  const id = $props.id();

  function resting(state: boolean) {
    return { extent: state ? 6 : 8, back: state ? 1 : 0, scaleX: 1, scaleY: 1, y: 0, first: 1, second: 1 };
  }

  let motion = $state(resting(untrack(() => maximized)));
  let mounted = false;
  let frame: number | null = null;
  let previous: { kind: string; maximized: boolean; hovered: boolean; trigger: typeof trigger } | undefined;

  function animate(milliseconds: number) {
    if (frame !== null) cancelAnimationFrame(frame);
    frame = null;
    const target = resting(maximized);
    if (!mounted || milliseconds <= 0) {
      motion = target;
      return;
    }

    const start = { ...motion };
    const started = performance.now();
    const currentKind = kind;
    const direction = maximized ? -1 : 1;
    const mix = (a: number, b: number, p: number) => a + (b - a) * p;
    const ease = (p: number) => 1 - Math.pow(1 - p, 3);

    function step(now: number) {
      const p = Math.min(1, (now - started) / milliseconds);
      if (p >= 1) {
        motion = target;
        frame = null;
        return;
      }
      const e = ease(p);
      // Damped spring starts at rest and overshoots before settling.
      const spring = 1 - Math.exp(-7 * p) * (Math.cos(11 * p) + 7 / 11 * Math.sin(11 * p));
      const bounce = Math.sin(3 * Math.PI * p) * Math.exp(-4 * p);
      const lift = Math.sin(Math.PI * p);
      let first = 1;
      let second = 1;
      if (currentKind === 'close') {
        if (p < 0.25) {
          first = start.first * (1 - ease(p / 0.25));
          second = start.second * (1 - ease(p / 0.25));
        } else {
          ({ first, second } = crossStrokeProgress((p - 0.25) / 0.75));
        }
      }
      motion = {
        extent: mix(start.extent, target.extent, spring),
        back: mix(start.back, target.back, e),
        scaleX: mix(start.scaleX, 1, e) + (currentKind === 'maximize' ? direction * 0.28 * bounce : currentKind === 'minimize' ? 0.12 * Math.sin(2 * Math.PI * p) : 0),
        scaleY: mix(start.scaleY, 1, e) + (currentKind === 'maximize' ? direction * 0.28 * bounce : currentKind === 'minimize' ? 0.65 * lift : 0),
        y: mix(start.y, 0, e) - (currentKind === 'minimize' ? 5 * lift : 0),
        first, second
      };
      frame = requestAnimationFrame(step);
    }
    frame = requestAnimationFrame(step);
  }

  $effect(() => {
    const current = { kind, maximized, hovered, trigger };
    const milliseconds = animationStore.isOff ? 0 : (duration ?? animationStore.timing * 2.5) * 1000;
    untrack(() => {
      const before = previous;
      previous = current;
      if (!before || !mounted || milliseconds <= 0) {
        animate(0);
      } else if (
        current.kind !== before.kind || current.maximized !== before.maximized ||
        (current.hovered && !before.hovered) ||
        (current.trigger !== undefined && current.trigger !== before.trigger)
      ) {
        animate(milliseconds);
      }
    });
  });

  onMount(() => { mounted = true; });
  onDestroy(() => { if (frame !== null) cancelAnimationFrame(frame); });
</script>

{#snippet frontSquare(stroke: string, fill: string)}
  <rect x={12 - motion.extent - motion.back * 1.5} y={12 - motion.extent + motion.back * 1.5}
    width={motion.extent * 2} height={motion.extent * 2} rx="1.5"
    {fill} {stroke} stroke-width="1.8" />
{/snippet}

<svg xmlns="http://www.w3.org/2000/svg" viewBox={kind === 'close' ? '7.33 4 8 8' : '0 0 24 24'}
  width={size} height={size} class="window-control-icon {className}" {style}
  role="img" aria-hidden={title ? undefined : 'true'}>
  {#if title}<title>{title}</title>{/if}
  {#if kind === 'close'}
    <CrossStrokes first={motion.first} second={motion.second} />
  {:else}
    <g transform="translate(12 {12 + motion.y}) scale({motion.scaleX} {motion.scaleY}) translate(-12 -12)">
      {#if kind === 'minimize'}
        <rect x="4" y="11" width="16" height="2" rx="1" fill="currentColor" />
      {:else}
        <defs>
          <mask id="{id}-front-cutout" maskUnits="userSpaceOnUse" x="-24" y="-24" width="72" height="72" style="mask-type: luminance">
            <rect x="-24" y="-24" width="72" height="72" fill="white" />
            <!-- Cover the entire front silhouette, including its stroke, in every frame. -->
            {@render frontSquare('black', 'black')}
          </mask>
        </defs>
        <!-- A closed outline has no exposed line caps at the overlap. -->
        <rect x="8" y="4" width="12" height="12" rx="1.5"
          fill="none" stroke="currentColor" stroke-width="1.8"
          opacity={motion.back} mask="url(#{id}-front-cutout)" />
        {@render frontSquare('currentColor', 'none')}
      {/if}
    </g>
  {/if}
</svg>

<style>
  .window-control-icon {
    display: inline-block;
    vertical-align: middle;
    flex-shrink: 0;
    overflow: visible;
  }
</style>
