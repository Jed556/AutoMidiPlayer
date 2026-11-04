<script lang="ts">
  import { tooltip } from '../../actions/tooltip';
  import { createFrameResizeObserver } from '../../utils/frameResizeObserver';

  let { path }: { path: string } = $props();
  let element = $state<HTMLSpanElement>();
  let displayPath = $state('');

  $effect(() => {
    if (!element) return;
    const node = element;
    const value = path;
    const context = document.createElement('canvas').getContext('2d');
    const measure = () => {
      if (!context) { displayPath = value; return; }
      context.font = getComputedStyle(node).font;
      const fits = (text: string) => context.measureText(text).width <= node.clientWidth;
      if (fits(value)) { displayPath = value; return; }
      const root = value.match(/^(?:[A-Za-z]:[\\/]|\\\\[^\\]+\\|\/)/)?.[0] ?? '';
      const remainder = value.slice(root.length);
      const separator = value.includes('\\') ? '\\' : '/';
      const prefix = root + '…' + separator;
      // Prefer whole trailing folders, keeping the drive at the start.
      const parts = remainder.split(/[\\/]/);
      for (let i = 1; i < parts.length; i++) {
        const candidate = prefix + parts.slice(i).join(separator);
        if (fits(candidate)) { displayPath = candidate; return; }
      }
      // Very long final names still retain their rightmost characters.
      let low = 0;
      let high = remainder.length;
      while (low < high) {
        const mid = Math.ceil((low + high) / 2);
        if (fits(prefix + remainder.slice(-mid))) low = mid;
        else high = mid - 1;
      }
      displayPath = prefix + (low ? remainder.slice(-low) : '');
    };
    const observer = createFrameResizeObserver(measure);
    observer.observe(node);
    measure();
    return () => observer.disconnect();
  });
</script>

<span bind:this={element} use:tooltip={path} aria-label={path}>{displayPath || path}</span>

<style>
  span { display: block; flex: 1; min-width: 0; overflow: hidden; white-space: nowrap; }
</style>
