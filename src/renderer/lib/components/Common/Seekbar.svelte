<script lang="ts">
  import type { ComponentProps } from 'svelte';
  import Seekbar from '../PlayerControlPanel/Seekbar.svelte';

  let {
    value = $bindable(0),
    previewValue = $bindable(0),
    isPreviewActive = $bindable(false),
    ...props
  }: ComponentProps<typeof Seekbar> = $props();
</script>

<Seekbar {...props} bind:value bind:previewValue bind:isPreviewActive />
