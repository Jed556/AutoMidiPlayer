<script lang="ts">
  import { createFrameResizeObserver } from '../../utils/frameResizeObserver';

  interface Props {
    text: string;
    hoverBehavior?: 'bounce' | 'loop' | 'none';
    /** Optional ancestor selector whose hover/focus activates the text. */
    hoverTarget?: string;
    showTooltip?: boolean;
    speed?: number;
  }

  let { text, hoverBehavior = 'bounce', hoverTarget, showTooltip = true, speed = 32 }: Props = $props();
  let viewport = $state<HTMLSpanElement>();
  let content = $state<HTMLSpanElement>();
  let distance = $state(0);
  let textWidth = $state(0);
  let active = $state(false);
  const gap = 40;
  let overflowing = $derived(distance > 2);
  let duration = $derived(hoverBehavior === 'loop'
    ? Math.max(3.5, (textWidth + gap) / Math.max(1, speed))
    : Math.max(3, distance / Math.max(1, speed) * 2 + 2));

  // Observe both sizes: column resizing, font loading, and text changes can all
  // change the overflow distance without changing the surrounding row.
  $effect(() => {
    if (!viewport || !content) return;
    const measure = () => {
      textWidth = content!.getBoundingClientRect().width;
      distance = Math.max(0, textWidth - viewport!.clientWidth);
    };
    const observer = createFrameResizeObserver(measure);
    observer.observe(viewport);
    observer.observe(content);
    measure();
    return () => observer.disconnect();
  });

  $effect(() => {
    if (!viewport) return;
    const target = (hoverTarget ? viewport.closest(hoverTarget) : viewport) ?? viewport;
    const update = () => { active = target.matches(':hover, :focus-within'); };
    let disposed = false;
    // Focusout can fire while Svelte removes this text's parent row.
    const handleInteraction = () => queueMicrotask(() => { if (!disposed) update(); });
    target.addEventListener('mouseenter', handleInteraction);
    target.addEventListener('mouseleave', handleInteraction);
    target.addEventListener('focusin', handleInteraction);
    target.addEventListener('focusout', handleInteraction);
    update();
    return () => {
      disposed = true;
      target.removeEventListener('mouseenter', handleInteraction);
      target.removeEventListener('mouseleave', handleInteraction);
      target.removeEventListener('focusin', handleInteraction);
      target.removeEventListener('focusout', handleInteraction);
    };
  });
</script>

<span
  class="overflow-text"
  class:overflowing
  class:running={active && overflowing && hoverBehavior !== 'none'}
  class:loop={hoverBehavior === 'loop'}
  bind:this={viewport}
  title={showTooltip ? text : undefined}
  style="--overflow-distance: -{distance}px; --loop-distance: -{textWidth + gap}px; --overflow-duration: {duration}s;"
>
  {#key text}
    <span class="track">
      <span class="content" bind:this={content}>{text}</span>
      {#if overflowing && hoverBehavior === 'loop'}
        <span class="content" aria-hidden="true">{text}</span>
      {/if}
    </span>
  {/key}
</span>

<style>
  .overflow-text {
    display: block;
    min-width: 0;
    max-width: 100%;
    overflow: hidden;
    white-space: nowrap;
    --fade-left: #000;
    --fade-right: transparent;
  }

  .overflowing {
    -webkit-mask-image: linear-gradient(to right, var(--fade-left), #000 min(16px, 20%), #000 max(80%, calc(100% - 24px)), var(--fade-right));
    mask-image: linear-gradient(to right, var(--fade-left), #000 min(16px, 20%), #000 max(80%, calc(100% - 24px)), var(--fade-right));
  }

  .track {
    display: flex;
    width: max-content;
    gap: 40px;
    transform: translateX(0);
  }

  .content { flex: none; }

  .running {
    animation: bounce-mask var(--overflow-duration) linear infinite;
    animation-delay: var(--anim-duration, 0.2s);
  }

  .running .track {
    animation: bounce var(--overflow-duration) linear infinite;
    animation-delay: var(--anim-duration, 0.2s);
    will-change: transform;
  }

  .running.loop {
    animation: none;
    --fade-left: transparent;
  }

  .running.loop .track { animation-name: loop; }

  @keyframes bounce {
    0%, 10%, 100% { transform: translateX(0); }
    45%, 55% { transform: translateX(var(--overflow-distance)); }
    90% { transform: translateX(0); }
  }

  @keyframes bounce-mask {
    0%, 10%, 90%, 100% { --fade-left: #000; --fade-right: transparent; }
    11%, 44%, 56%, 89% { --fade-left: transparent; --fade-right: transparent; }
    45%, 55% { --fade-left: transparent; --fade-right: #000; }
  }

  @keyframes loop {
    to { transform: translateX(var(--loop-distance)); }
  }

  @media (prefers-reduced-motion: reduce) {
    .running, .running .track { animation: none; }
    .running.loop { --fade-left: #000; }
  }

  :global(.no-animations) .running,
  :global(.no-animations) .running .track { animation: none; }
  :global(.no-animations) .running.loop { --fade-left: #000; }
</style>
