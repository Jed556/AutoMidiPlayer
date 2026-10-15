<script module lang="ts">
  import type { IconMetadata } from './iconData';
  export const metadata: IconMetadata = {
    name: 'Window Minimize',
    description: 'A rounded bar pops up, stretches and settles back; plays on hover or trigger',
    params: [{ type: 'toggle', id: 'hovered', label: 'Hover State', defaultValue: false }]
  };
</script>

<script lang="ts">
  import WindowControlGlyph, { type WindowControlIconProps } from './WindowControlGlyph.svelte';
  let props: WindowControlIconProps = $props();
</script>

<WindowControlGlyph {...props} kind="minimize" />
