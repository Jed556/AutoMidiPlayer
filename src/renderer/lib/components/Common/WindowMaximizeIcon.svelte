<script module lang="ts">
  import type { IconMetadata } from './iconData';
  export const metadata: IconMetadata = {
    name: 'Window Maximize / Restore',
    description: 'An elastic square shrinks into Restore and enlarges into Maximize; the front square masks the closed rear outline throughout the animation',
    animationParamId: 'maximized',
    params: [
      { type: 'toggle', id: 'maximized', label: 'Maximized (Restore Icon)', defaultValue: false },
      { type: 'toggle', id: 'hovered', label: 'Hover State', defaultValue: false }
    ]
  };
</script>

<script lang="ts">
  import WindowControlGlyph, { type WindowControlIconProps } from './WindowControlGlyph.svelte';
  let props: WindowControlIconProps = $props();
</script>

<WindowControlGlyph {...props} kind="maximize" />
