<script lang="ts">
  import { searchOverflow } from '../../actions/searchOverflow';
  import FluentIcon from './FluentIcon.svelte';

  interface Props {
    value?: string;
    placeholder?: string;
    width?: string;
    class?: string;
    onchange?: (val: string) => void;
    oninput?: (val: string) => void;
    onkeydown?: (e: KeyboardEvent) => void;
  }

  let {
    value = $bindable(''),
    placeholder = '',
    width,
    class: className = '',
    onchange,
    oninput,
    onkeydown
  }: Props = $props();

  function handleInput(e: Event) {
    const target = e.target as HTMLInputElement;
    value = target.value;
    oninput?.(value);
  }

  function handleChange(e: Event) {
    const target = e.target as HTMLInputElement;
    value = target.value;
    onchange?.(value);
  }

  function clearSearch() {
    value = '';
    oninput?.('');
    onchange?.('');
  }
</script>

<div class="fluent-search-box {className}" style={width ? `width: ${width};` : ''}>
  <span class="search-icon-slot">
    <FluentIcon name="Search" size={13} />
  </span>
  <input
    type="text"
    {placeholder}
    value={value}
    use:searchOverflow={{ value, placeholder }}
    oninput={handleInput}
    onchange={handleChange}
    {onkeydown}
    class="fluent-search-input"
  />
  {#if value}
    <button
      type="button"
      class="clear-search-btn"
      onclick={clearSearch}
      tabindex="-1"
      aria-label="Clear"
    >
      <FluentIcon name="Dismiss" size={11} />
    </button>
  {/if}
</div>

<style>
  .fluent-search-box {
    position: relative;
    display: inline-flex;
    align-items: center;
    box-sizing: border-box;
    width: 250px;
    height: 32px;
    border-radius: var(--radius-sm, 4px);
    background: var(--control-fill);
    border: 1px solid transparent;
    transition: border-color var(--transition-fast, 0.16s ease), background var(--transition-fast, 0.16s ease), box-shadow var(--transition-fast, 0.16s ease);
  }

  .search-icon-slot {
    position: absolute;
    left: 9px;
    top: 50%;
    transform: translateY(-50%);
    display: flex;
    align-items: center;
    justify-content: center;
    color: var(--text-tertiary);
    pointer-events: none;
    z-index: 2;
  }

  .fluent-search-input {
    position: absolute;
    inset: 0;
    width: 100%;
    height: 100%;
    min-width: 0;
    padding: 0 27px 0 29px;
    background: transparent;
    border: none;
    color: var(--text-primary);
    font-size: 12px;
    font-family: inherit;
    outline: none;
    box-sizing: border-box;
  }

  .fluent-search-box:hover {
    background: var(--control-fill-hover);
    border-color: transparent;
  }

  .fluent-search-box:focus-within {
    background: var(--bg-card);
    border-color: var(--accent);
    box-shadow: 0 0 0 2px var(--accent-glow, rgba(29, 185, 84, 0.35)), 0 0 8px var(--accent-glow, rgba(29, 185, 84, 0.2));
  }

  .fluent-search-input::placeholder {
    color: var(--text-tertiary);
    opacity: 0.8;
  }

  .clear-search-btn {
    position: absolute;
    right: 5px;
    top: 50%;
    transform: translateY(-50%);
    background: transparent;
    border: none;
    color: var(--text-tertiary);
    cursor: pointer;
    width: 20px;
    height: 20px;
    border-radius: 50%;
    display: flex;
    align-items: center;
    justify-content: center;
    z-index: 2;
    padding: 0;
    transition: color var(--transition-fast, 0.12s ease), background var(--transition-fast, 0.12s ease);
  }

  .clear-search-btn:hover {
    color: var(--text-primary);
    background: var(--bg-hover);
  }
</style>
