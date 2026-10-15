<script module lang="ts">
  import type { IconMetadata } from './iconData';
  export const metadata: IconMetadata = {
    name: 'Window Close',
    description: 'The shared mute cross grows its rounded strokes from their starting points in sequence; hover to preview',
    params: [{ type: 'toggle', id: 'hovered', label: 'Hover State', defaultValue: false }]
  };
</script>

<script lang="ts">
  import WindowControlGlyph, { type WindowControlIconProps } from './WindowControlGlyph.svelte';
  let props: WindowControlIconProps = $props();
</script>

<WindowControlGlyph {...props} kind="close" />
