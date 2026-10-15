<script module lang="ts">
  // Shared by the mute and window-close icons: upper-right stroke leads by 20%.
  export function crossStrokeProgress(progress: number) {
    const ease = (p: number) => 1 - Math.pow(1 - p, 3);
    return {
      first: ease(Math.min(1, progress / 0.8)),
      second: ease(Math.max(0, (progress - 0.2) / 0.8))
    };
  }
</script>

<script lang="ts">
  let { first = 1, second = 1, strokeWidth = 1.5, color = 'currentColor' }: {
    first?: number;
    second?: number;
    strokeWidth?: number;
    color?: string;
  } = $props();
</script>

<!-- Grow each whole stroke from its starting point, including its round cap.
     The speaker cutout uses these exact transforms with a wider black stroke. -->
<path d="M13.33 6 9.33 10" fill="none" stroke={color} stroke-width={strokeWidth}
  stroke-linecap="round" transform="translate(13.33 6) scale({first}) translate(-13.33 -6)" />
<path d="M9.33 6 13.33 10" fill="none" stroke={color} stroke-width={strokeWidth}
  stroke-linecap="round" transform="translate(9.33 6) scale({second}) translate(-9.33 -6)" />
