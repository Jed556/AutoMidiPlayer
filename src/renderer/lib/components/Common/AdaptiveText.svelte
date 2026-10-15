<script lang="ts">
  import { onDestroy, tick, untrack } from 'svelte';
  import { animationStore } from '../../state/animationStore.svelte';
  import { createFrameResizeObserver } from '../../utils/frameResizeObserver';
  import { animateTextSweep } from '../../utils/textSweep';

  interface Props {
    text: string;
    compactText?: string;
  }

  let { text, compactText }: Props = $props();
  let container = $state<HTMLSpanElement>();
  let measure = $state<HTMLSpanElement>();
  let compact = $state(false);
  let measured = $state(false);
  let label = $state<HTMLSpanElement>();
  const displayedText = $derived(compact && compactText ? compactText : text);
  let initialized = false;
  let revision = 0;
  let animations: Animation[] = [];
  let outgoing: HTMLElement | null = null;
  let pendingFrame: number | null = null;
  let pendingLabel: { element: HTMLSpanElement | undefined; width: number | undefined; animate: boolean } | null = null;

  function cancelSweep() {
    animations.forEach(animation => animation.cancel());
    animations = [];
    outgoing?.remove();
    outgoing = null;
  }

  onDestroy(() => {
    revision++;
    if (pendingFrame !== null) cancelAnimationFrame(pendingFrame);
    pendingLabel = null;
    cancelSweep();
  });

  $effect.pre(() => {
    const nextText = displayedText;
    const ready = measured;
    untrack(() => {
      // Keep the label from before this change while the full/short fit measurement settles.
      pendingLabel ??= {
        element: label?.cloneNode(true) as HTMLSpanElement | undefined,
        width: label?.offsetWidth,
        animate: initialized,
      };
      initialized = ready;
      const currentRevision = ++revision;
      void tick().then(() => {
        if (currentRevision !== revision) return;
        if (pendingFrame !== null) cancelAnimationFrame(pendingFrame);
        pendingFrame = requestAnimationFrame(() => {
          pendingFrame = null;
          if (currentRevision !== revision || !container || !label) return;
          const previous = pendingLabel;
          pendingLabel = null;
          cancelSweep();
          const duration = animationStore.durationMs;
          if (!previous?.animate || !previous.element || previous.element.textContent === nextText || !duration
            || document.documentElement.classList.contains('no-animations')
            || window.matchMedia('(prefers-reduced-motion: reduce)').matches) return;
          outgoing = previous.element;
          animations = animateTextSweep(label, previous.element, container, duration, previous.width);
        });
      });
    });
  });

  $effect(() => {
    if (animationStore.isOff) cancelSweep();
  });

  $effect(() => {
    text;
    compactText;
    if (!container || !measure) return;
    const update = () => {
      compact = measure!.getBoundingClientRect().width > container!.getBoundingClientRect().width + 0.5;
      measured = true;
    };
    const observer = createFrameResizeObserver(update);
    observer.observe(container);
    observer.observe(measure);
    update();
    return () => observer.disconnect();
  });
</script>

<span class="adaptive-text" bind:this={container}>
  <!-- Keep measuring the full text so switching labels cannot cause a resize loop. -->
  <span class="measure" aria-hidden="true" bind:this={measure}>{text}</span>
  <span class="label" bind:this={label}>{displayedText}</span>
</span>

<style>
  .adaptive-text {
    position: relative;
    display: grid;
    grid-template-columns: minmax(0, 1fr);
    min-width: 0;
    max-width: 100%;
    overflow: hidden;
  }

  .measure,
  .label {
    grid-area: 1 / 1;
    white-space: nowrap;
  }

  .measure {
    visibility: hidden;
    pointer-events: none;
    width: max-content;
  }

  .label {
    min-width: 0;
    overflow: hidden;
    text-overflow: ellipsis;
  }
</style>
