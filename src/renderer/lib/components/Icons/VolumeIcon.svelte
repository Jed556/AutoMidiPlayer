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
  import CrossStrokes from './CrossStrokes.svelte';
  import { sameVolumeMotion, stepVolumeMotion, volumeTargetFor } from './volumeIconUtils';

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

  const initial = volumeTargetFor(untrack(() => volume));
  let motion = $state({ ...initial });
  let activeTarget = { ...initial };
  let mounted = false;
  let frame: number | null = null;

  function animate(value: number, milliseconds: number) {
    const target = volumeTargetFor(value);
    if (!mounted || !(milliseconds > 0)) {
      if (frame !== null) cancelAnimationFrame(frame);
      frame = null;
      activeTarget = target;
      motion = target;
      return;
    }
    if (sameVolumeMotion(activeTarget, target) && (frame !== null || sameVolumeMotion(motion, target))) {
      return;
    }
    if (frame !== null) cancelAnimationFrame(frame);
    frame = null;
    activeTarget = target;
    const start = { ...motion };
    const started = performance.now();
    function step(now: number) {
      const next = stepVolumeMotion(start, target, now - started, milliseconds);
      motion = next.motion;
      if (next.done) {
        frame = null;
        return;
      }
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
      <circle r="2" fill="none" stroke="currentColor" stroke-width={1.5 / Math.max(1, motion.primaryScale)} />
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
