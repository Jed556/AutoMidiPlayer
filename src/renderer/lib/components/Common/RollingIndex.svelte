<script lang="ts">
  import { fly } from 'svelte/transition';
  import { cubicOut, cubicIn } from 'svelte/easing';

  interface Props {
    value: number;
    accent?: boolean;
    class?: string;
  }

  let { value, accent = false, class: className = '' }: Props = $props();

  let prevValue = $state<number | null>(null);
  let dir = $state(1);

  $effect.pre(() => {
    if (prevValue !== null && value !== prevValue) {
      dir = value > prevValue ? 1 : -1;
    }
    prevValue = value;
  });
</script>

<div class="rolling-counter {className}" class:accent>
  {#key value}
    <span
      class="counter-digit"
      in:fly={{ y: dir * 14, duration: 220, easing: cubicOut }}
      out:fly={{ y: -dir * 14, duration: 180, easing: cubicIn }}
    >
      {value}
    </span>
  {/key}
</div>

<style>
  .rolling-counter {
    position: relative;
    display: inline-flex;
    align-items: center;
    justify-content: center;
    height: 24px;
    min-width: 28px;
    overflow: hidden;
    /* Vertical gradient alpha fade mask at top and bottom edges */
    mask-image: linear-gradient(
      to bottom,
      transparent 0%,
      rgba(0, 0, 0, 0.4) 18%,
      black 35%,
      black 65%,
      rgba(0, 0, 0, 0.4) 82%,
      transparent 100%
    );
    -webkit-mask-image: linear-gradient(
      to bottom,
      transparent 0%,
      rgba(0, 0, 0, 0.4) 18%,
      black 35%,
      black 65%,
      rgba(0, 0, 0, 0.4) 82%,
      transparent 100%
    );
    color: var(--text-primary);
    opacity: 0.6;
    font-variant-numeric: tabular-nums;
    font-size: 12px;
    font-weight: 500;
    user-select: none;
    -webkit-user-select: none;
  }

  .rolling-counter.accent {
    color: var(--accent, #1DB954);
    opacity: 1;
    font-weight: 600;
  }

  .counter-digit {
    position: absolute;
    display: flex;
    align-items: center;
    justify-content: center;
    white-space: nowrap;
    font-variant-numeric: tabular-nums;
    will-change: transform, opacity;
  }
</style>
