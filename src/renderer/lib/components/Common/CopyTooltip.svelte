<script lang="ts">
  import type { Snippet } from 'svelte';
  import { tooltip } from '../../actions/tooltip';
  import type { TooltipPosition, TooltipVariant } from '../../ui/tooltipManager.svelte';

  interface Props {
    textToCopy: string;
    label?: string;
    copiedLabel?: string;
    position?: TooltipPosition;
    delay?: number;
    arrow?: boolean;
    variant?: TooltipVariant;
    className?: string;
    children?: Snippet;
    onCopySuccess?: () => void;
  }

  let {
    textToCopy,
    label = 'Copy',
    copiedLabel = 'Copied!',
    position = 'top',
    delay = 80,
    arrow = false,
    variant = 'dark',
    className = '',
    children,
    onCopySuccess
  }: Props = $props();

  let isCopied = $state(false);
  let copyTimeout: any = null;

  async function handleClick(e: MouseEvent) {
    e.stopPropagation();
    if (!textToCopy) return;

    try {
      await navigator.clipboard.writeText(textToCopy);
      isCopied = true;
      onCopySuccess?.();

      if (copyTimeout) clearTimeout(copyTimeout);
      copyTimeout = setTimeout(() => {
        isCopied = false;
      }, 2000);
    } catch (err) {
      console.error('Failed to copy to clipboard:', err);
    }
  }
</script>

<!-- svelte-ignore a11y_click_events_have_key_events -->
<!-- svelte-ignore a11y_no_static_element_interactions -->
<div
  class="amp-copy-tooltip {className}"
  onclick={handleClick}
  use:tooltip={{
    text: isCopied ? copiedLabel : label,
    position,
    delay: isCopied ? 0 : delay,
    arrow,
    variant
  }}
>
  {@render children?.()}
</div>

<style>
  .amp-copy-tooltip {
    display: inline-flex;
    align-items: center;
    cursor: pointer;
  }
</style>
