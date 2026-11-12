<script module lang="ts">
  import type { IconMetadata } from './iconData';

  export const metadata: IconMetadata = {
    name: 'Chord',
    description: 'Lyre with a simplified small-size shape and detailed outline and filled variants',
    params: [
      { type: 'toggle', id: 'filled', label: 'Filled', defaultValue: false },
      { type: 'toggle', id: 'isActive', label: 'Chord Enabled', defaultValue: true },
    ],
  };
</script>

<script lang="ts">
  import outlineIcon from '../../../assets/icons/Chords/Chord.svg?raw';
  import filledIcon from '../../../assets/icons/Chords/Chord Filled.svg?raw';
  import compactOutlineIcon from '../../../assets/icons/Chords/Chord Compact.svg?raw';
  import compactFilledIcon from '../../../assets/icons/Chords/Chord Compact Filled.svg?raw';

  interface Props {
    size?: number;
    filled?: boolean;
    isActive?: boolean;
    compact?: boolean;
    class?: string;
  }

  let { size = 16, filled = false, isActive = true, compact, class: customClass = '' }: Props = $props();
  const useCompact = $derived(compact ?? (size <= 20));
</script>

<span class="chord-icon {customClass}" class:filled class:active={isActive} style:width="{size}px" style:height="{size}px" aria-hidden="true">
  <span class="icon-layer outline">{@html useCompact ? compactOutlineIcon : outlineIcon}</span>
  <span class="icon-layer fill">{@html useCompact ? compactFilledIcon : filledIcon}</span>
</span>

<style>
  .chord-icon {
    display: inline-grid;
    align-items: center;
    justify-content: center;
    vertical-align: middle;
    flex-shrink: 0;
    opacity: 0.5;
    transition: opacity var(--transition-fast, 0.15s ease-out);
  }

  .chord-icon.active { opacity: 0.85; }

  .icon-layer {
    grid-area: 1 / 1;
    width: 100%;
    height: 100%;
    transition: opacity var(--transition-fast, 0.15s ease-out);
  }

  .fill { opacity: 0; }
  .filled .outline { opacity: 0; }
  .filled .fill { opacity: 1; }

  @media (prefers-reduced-motion: reduce) {
    .chord-icon, .icon-layer { transition: none; }
  }

  .chord-icon :global(svg) {
    display: block;
    width: 100%;
    height: 100%;
  }
</style>
