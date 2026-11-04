<script lang="ts">
  interface Props {
    checked?: boolean;
    disabled?: boolean;
    label: string;
    description?: string;
    onchange?: (checked: boolean) => void;
  }

  let { checked = $bindable(false), disabled = false, label, description = '', onchange }: Props = $props();
</script>

<label class="checkbox" class:is-disabled={disabled}>
  <input type="checkbox" bind:checked {disabled} onchange={() => onchange?.(checked)} />
  <span class="box" aria-hidden="true">
    <svg viewBox="0 0 16 16" fill="none">
      <path d="M3.5 8 6.5 11 12.5 5" pathLength="1" />
    </svg>
  </span>
  <span class="text">
    <span class="label">{label}</span>
    {#if description}<span class="description">{description}</span>{/if}
  </span>
</label>

<style>
  .checkbox { position: relative; display: inline-flex; flex-direction: row; align-items: flex-start; gap: 10px; cursor: pointer; user-select: none; min-width: 0; }
  input { position: absolute; width: 1px; height: 1px; opacity: 0; margin: 0; }
  .box { display: grid; place-items: center; flex: 0 0 18px; width: 18px; height: 18px; box-sizing: border-box; margin-top: 1px; border: 1px solid var(--switch-border, var(--text-secondary)); border-radius: 4px; background: var(--switch-bg, transparent); transition: background var(--transition-fast, 0.15s), border-color var(--transition-fast, 0.15s); }
  .checkbox:hover:not(.is-disabled) .box { border-color: var(--text-primary); }
  input:focus-visible + .box { outline: 2px solid var(--accent-color, var(--accent)); outline-offset: 3px; }
  input:checked + .box { background: var(--accent-color, var(--accent)); border-color: var(--accent-color, var(--accent)); }
  svg { width: 16px; height: 16px; overflow: visible; }
  path { stroke: var(--text-on-accent, #fff); stroke-width: 1.8; stroke-linecap: round; stroke-linejoin: round; stroke-dasharray: 1; stroke-dashoffset: 1; opacity: 0; transition: stroke-dashoffset var(--transition-normal, 0.2s) ease, opacity var(--transition-fast, 0.15s); }
  input:checked + .box path { stroke-dashoffset: 0; opacity: 1; }
  .text { display: flex; flex-direction: column; gap: 5px; min-width: 0; }
  .label { color: var(--text-primary); font-size: 13px; line-height: 20px; }
  .description { color: var(--text-secondary); font-size: 11px; line-height: 1.5; }
  .is-disabled { opacity: 0.45; cursor: not-allowed; }
  @media (prefers-reduced-motion: reduce) { .box, path { transition: none; } }
</style>
