<script lang="ts">
  interface Props {
    isPlaying?: boolean;
    size?: number;
    width?: number;
    height?: number;
    class?: string;
  }

  let {
    isPlaying = false,
    size = 14,
    width,
    height,
    class: customClass = ''
  }: Props = $props();

  let resolvedSize = $derived(size || width || height || 14);
</script>

<div
  class="equalizer-control {customClass}"
  class:is-playing={isPlaying}
  style="--eq-size: {resolvedSize}px;"
  aria-label="Equalizer indicator"
>
  <span class="eq-bar bar-1"></span>
  <span class="eq-bar bar-2"></span>
  <span class="eq-bar bar-3"></span>
  <span class="eq-bar bar-4"></span>
</div>

<style>
  .equalizer-control {
    display: inline-flex;
    align-items: flex-end;
    justify-content: space-between;
    width: var(--eq-size, 14px);
    height: var(--eq-size, 14px);
    padding: 0;
    box-sizing: border-box;
    flex-shrink: 0;
    gap: 1.5px;
    overflow: hidden;
  }

  .eq-bar {
    flex: 1;
    min-width: 2px;
    background-color: var(--accent, #1DB954);
    border-radius: 1px 1px 0 0;
    transition: height var(--transition-fast, 0.15s ease-out);
  }

  /* Static heights when paused or stopped */
  .bar-1 { height: 28%; }
  .bar-2 { height: 57%; }
  .bar-3 { height: 42%; }
  .bar-4 { height: 71%; }

  /* Animated when playing (percentage-based to guarantee zero container overflow) */
  .is-playing .bar-1 {
    animation: eq-anim-1 0.6s ease-in-out infinite alternate;
  }

  .is-playing .bar-2 {
    animation: eq-anim-2 0.5s ease-in-out 0.1s infinite alternate;
  }

  .is-playing .bar-3 {
    animation: eq-anim-3 0.7s ease-in-out 0.05s infinite alternate;
  }

  .is-playing .bar-4 {
    animation: eq-anim-4 0.56s ease-in-out 0.15s infinite alternate;
  }

  @keyframes eq-anim-1 {
    from { height: 25%; }
    to { height: 95%; }
  }

  @keyframes eq-anim-2 {
    from { height: 75%; }
    to { height: 25%; }
  }

  @keyframes eq-anim-3 {
    from { height: 35%; }
    to { height: 100%; }
  }

  @keyframes eq-anim-4 {
    from { height: 85%; }
    to { height: 35%; }
  }
</style>
