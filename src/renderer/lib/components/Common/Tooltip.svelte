<script lang="ts">
  import type { Snippet } from 'svelte';
  import { tooltip } from '../../actions/tooltip';
  import type { TooltipPosition, TooltipVariant } from '../../ui/tooltipManager.svelte';

  interface Props {
    text?: string;
    position?: TooltipPosition;
    delay?: number;
    exitDelay?: number;
    arrow?: boolean;
    disabled?: boolean;
    variant?: TooltipVariant;
    maxWidth?: string;
    interactive?: boolean;
    followCursor?: boolean;
    className?: string;
    children?: Snippet;
  }

  let {
    text,
    position = 'top',
    delay = 80,
    exitDelay = 150,
    arrow = false,
    disabled = false,
    variant = 'dark',
    maxWidth,
    interactive = false,
    followCursor,
    className = '',
    children
  }: Props = $props();
</script>

<div
  class="amp-tooltip-trigger {className}"
  use:tooltip={{
    text,
    position,
    delay,
    exitDelay,
    arrow,
    disabled,
    variant,
    maxWidth,
    interactive,
    followCursor
  }}
>
  {@render children?.()}
</div>

<style>
  .amp-tooltip-trigger {
    display: inline-flex;
    align-items: center;
  }
</style>
