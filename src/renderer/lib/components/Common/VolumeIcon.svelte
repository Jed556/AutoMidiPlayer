<script module lang="ts">
  import type { IconMetadata } from './iconData';

  export const metadata: IconMetadata = {
    name: 'Volume',
    description: 'The existing sound arc expands into the outer high-volume arc as a new inner arc grows in; the mute cross cuts matching clearance into the speaker',
    animationParamId: 'volume',
    params: [{
      type: 'slider', id: 'volume', label: 'Volume',
      min: 0, max: 1, step: 0.01, defaultValue: 1,
      presets: [
        { label: 'Muted', value: 0 },
        { label: 'Low', value: 0.2 },
        { label: 'Medium', value: 0.5 },
        { label: 'High', value: 1 }
      ]
    }]
  };
</script>

<script lang="ts">
  import { onMount, onDestroy, untrack } from 'svelte';
  import { animationStore } from '../../state/animationStore.svelte';
  import CrossStrokes, { crossStrokeProgress } from './CrossStrokes.svelte';

  interface Props {
    volume?: number;
    size?: number;
    class?: string;
    style?: string;
    title?: string;
    duration?: number;
  }

  let { volume = 1, size = 16, class: className = '', style = '', title, duration }: Props = $props();
  const id = $props.id();
  const speaker = 'M9.741.85a.75.75 0 0 1 .375.65v13a.75.75 0 0 1-1.125.65l-6.925-4a3.64 3.64 0 0 1-1.33-4.967 3.64 3.64 0 0 1 1.33-1.332l6.925-4a.75.75 0 0 1 .75 0zm-6.924 5.3a2.14 2.14 0 0 0 0 3.7l5.8 3.35V2.8z';

  function targetFor(value: number) {
    const muted = value <= 0;
    const high = value >= 0.66;
    return {
      primary: muted ? 0 : 1,
      primaryScale: high ? 2.5 : value >= 0.33 ? 1.45 : 1,
      inner: high ? 1 : 0,
      first: muted ? 1 : 0,
      second: muted ? 1 : 0,
    };
  }

  const initial = targetFor(untrack(() => volume));
  let motion = $state({ ...initial });
  let mounted = false;
  let frame: number | null = null;

  function animate(value: number, milliseconds: number) {
    if (frame !== null) cancelAnimationFrame(frame);
    frame = null;
    const target = targetFor(value);
    if (!mounted || milliseconds <= 0) {
      motion = target;
      return;
    }
    const start = { ...motion };
    const started = performance.now();
    const ease = (p: number) => 1 - Math.pow(1 - p, 3);
    const mix = (a: number, b: number, p: number) => a + (b - a) * p;
    function step(now: number) {
      const p = Math.min(1, (now - started) / milliseconds);
      if (p >= 1) {
        motion = target;
        frame = null;
        return;
      }
      const e = ease(p);
      // The upper-right stroke leads; the upper-left stroke starts 20% later.
      const { first, second } = crossStrokeProgress(p);
      motion = {
        primary: mix(start.primary, target.primary, e),
        primaryScale: mix(start.primaryScale, target.primaryScale, e),
        inner: mix(start.inner, target.inner, ease(Math.max(0, (p - 0.12) / 0.88))),
        first: mix(start.first, target.first, first),
        second: mix(start.second, target.second, second),
      };
      frame = requestAnimationFrame(step);
    }
    frame = requestAnimationFrame(step);
  }

  $effect(() => {
    const value = volume;
    const milliseconds = animationStore.isOff ? 0 : (duration !== undefined ? duration * 1000 : animationStore.durationMs);
    untrack(() => animate(value, milliseconds));
  });

  onMount(() => { mounted = true; });
  onDestroy(() => { if (frame !== null) cancelAnimationFrame(frame); });
</script>

<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 16 16" width={size} height={size}
  class="volume-icon {className}" {style} role="img" aria-hidden={title ? undefined : 'true'}>
  {#if title}<title>{title}</title>{/if}
  <defs>
    <mask id="{id}-speaker" maskUnits="userSpaceOnUse" x="0" y="0" width="16" height="16" style="mask-type: luminance">
      <rect width="16" height="16" fill="white" />
      <CrossStrokes first={motion.first} second={motion.second} strokeWidth={4} color="black" />
    </mask>
    <clipPath id="{id}-waves" clipPathUnits="userSpaceOnUse">
      <rect x="11.5" y="0" width="4.5" height="16" />
    </clipPath>
  </defs>
  <path d={speaker} fill="currentColor" mask="url(#{id}-speaker)" />
  <!-- Clip AFTER scaling: both rings keep their left edge at x=11.5.
       Keep the original arc alive as it expands into the outer high-volume arc.
       Only the new inner arc grows in, with a consistent stroke weight. -->
  <g clip-path="url(#{id}-waves)">
    <g transform="translate(10.116 8) scale({motion.primary * motion.primaryScale})">
      <circle r="2" fill="none" stroke="currentColor" stroke-width={1.5 / motion.primaryScale} />
    </g>
    <g transform="translate(10.116 8) scale({motion.inner})">
      <circle r="2" fill="none" stroke="currentColor" stroke-width="1.5" />
    </g>
  </g>
  <CrossStrokes first={motion.first} second={motion.second} />
</svg>

<style>
  .volume-icon {
    display: inline-block;
    vertical-align: middle;
    flex-shrink: 0;
    overflow: visible;
  }
</style>
