<script module lang="ts">
  import type { IconMetadata } from './iconData';

  export const metadata: IconMetadata = {
    name: 'PlayPause',
    description: 'Smooth cubic-bezier morph between Play triangle and Pause bars',
    animationParamId: 'playbackStore',
    params: [
      {
        type: 'option',
        id: 'playbackStore',
        label: 'Playback State',
        defaultValue: 'play',
        options: [
          { label: 'Play', value: 'play', props: { iconName: 'Play', playing: false } },
          { label: 'Pause', value: 'pause', props: { iconName: 'Pause', playing: true } }
        ]
      }
    ]
  };
</script>

<script lang="ts">
  interface Props {
    playing?: boolean;
    size?: number;
    class?: string;
    style?: string;
    title?: string;
    duration?: number;
    trigger?: number | boolean;
  }

  let {
    playing = false,
    size = 18,
    class: customClass = '',
    style = '',
    title,
    duration,
    trigger
  }: Props = $props();

  let isEffectivelyPlaying = $derived(playing);
</script>

<span
  class="play-pause-icon {customClass}"
  class:playing={isEffectivelyPlaying}
  style="--icon-size: {size}px; {duration !== undefined ? `--morph-duration-left: ${duration * 0.85}s; --morph-duration-right: ${duration}s;` : ''} {style}"
  role="img"
  aria-hidden={title ? undefined : 'true'}
  {title}
>
  <svg viewBox="0 0 16 16" aria-hidden="true" focusable="false">
    <path class="shape shape-left" />
    <path class="shape shape-right" />
  </svg>
</span>

<style>
  .play-pause-icon {
    /*
      Spotify Official SVG Coordinates (viewBox 0 0 16 16):
      Corner radius r = 0.7 on all vertices and pause bars.
      Each path has 8 cubic Béziers (M C C C C C C C C Z) enabling native,
      smooth CSS transition interpolation between Play and Pause states.
    */
    --d-left-play: path("M 3 1.713 C 3 1.3264 3.715 0.913 4.05 1.106 C 5.4 1.886 6.75 2.665 8.1 3.445 C 8.1 3.445 8.1 3.445 8.1 3.445 C 8.1 6.48 8.1 9.52 8.1 12.555 C 8.1 12.555 8.1 12.555 8.1 12.555 C 6.75 13.335 5.4 14.114 4.05 14.894 C 3.715 15.087 3 14.6736 3 14.288 C 3 10.096 3 5.904 3 1.713 Z");
    --d-left-pause: path("M 2 1.7 C 2 1.3134 2.3134 1 2.7 1 C 3.5667 1 4.4333 1 5.3 1 C 5.6866 1 6 1.3134 6 1.7 C 6 5.9 6 10.1 6 14.3 C 6 14.6866 5.6866 15 5.3 15 C 4.4333 15 3.5667 15 2.7 15 C 2.3134 15 2 14.6866 2 14.3 C 2 10.1 2 5.9 2 1.7 Z");
    --d-right-play: path("M 7.9 3.33 C 7.9 3.33 7.9 3.33 7.9 3.33 C 10.247 4.685 12.593 6.039 14.94 7.394 C 15.15 7.515 15.25 7.75 15.25 8 C 15.25 8.25 15.15 8.485 14.94 8.606 C 12.593 9.961 10.247 11.315 7.9 12.67 C 7.9 12.67 7.9 12.67 7.9 12.67 C 7.9 11.113 7.9 9.557 7.9 8 C 7.9 6.443 7.9 4.887 7.9 3.33 Z");
    --d-right-pause: path("M 10 1.7 C 10 1.3134 10.3134 1 10.7 1 C 11.5667 1 12.4333 1 13.3 1 C 13.6866 1 14 1.3134 14 1.7 C 14 5.9 14 10.1 14 14.3 C 14 14.6866 13.6866 15 13.3 15 C 12.4333 15 11.5667 15 10.7 15 C 10.3134 15 10 14.6866 10 14.3 C 10 10.1 10 5.9 10 1.7 Z");

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

  .play-pause-icon svg {
    width: 100%;
    height: 100%;
    display: block;
    overflow: visible;
  }

  .shape {
    fill: currentColor;
    will-change: d;
  }

  /* DEFAULT PLAY STATE (playing === false): Left trapezoid + Right triangle */
  .shape-left {
    d: var(--d-left-play);
    transition: d var(--morph-duration-left, 0.30s) cubic-bezier(0.4, 0, 0.2, 1);
  }

  .shape-right {
    d: var(--d-right-play);
    /* Right shape forms the triangle slowly and smoothly */
    transition: d var(--morph-duration-right, 0.44s) cubic-bezier(0.2, 0, 0, 1);
  }

  /* PLAYING STATE: PAUSE ICON (playing === true): 2 rounded vertical bars */
  .playing .shape-left {
    d: var(--d-left-pause);
    transition: d var(--morph-duration-left, 0.30s) cubic-bezier(0.4, 0, 0.2, 1);
  }

  .playing .shape-right {
    d: var(--d-right-pause);
    transition: d var(--morph-duration-right, 0.38s) cubic-bezier(0.25, 1, 0.5, 1);
  }
</style>
