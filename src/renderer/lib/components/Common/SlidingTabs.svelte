<script lang="ts">
  import FluentIcon from './FluentIcon.svelte';

  interface Tab {
    id: string;
    label: string;
    icon?: string;
    count?: number;
  }

  interface Props {
    tabs: readonly Tab[];
    value: string;
    onselect: (id: string) => void;
    label: string;
    divider?: boolean;
    idPrefix?: string;
    panelId?: string;
  }

  let { tabs, value, onselect, label, divider = true, idPrefix, panelId }: Props = $props();
  const activeIndex = $derived(Math.max(0, tabs.findIndex(tab => tab.id === value)));

  function handleKeydown(event: KeyboardEvent, index: number) {
    let nextIndex: number;
    switch (event.key) {
      case 'ArrowRight': nextIndex = (index + 1) % tabs.length; break;
      case 'ArrowLeft': nextIndex = (index - 1 + tabs.length) % tabs.length; break;
      case 'Home': nextIndex = 0; break;
      case 'End': nextIndex = tabs.length - 1; break;
      default: return;
    }
    event.preventDefault();
    const button = event.currentTarget as HTMLButtonElement;
    button.parentElement?.querySelectorAll<HTMLButtonElement>('[role="tab"]')[nextIndex]?.focus();
    onselect(tabs[nextIndex].id);
  }
</script>

<div class="sliding-tabs" class:divider role="tablist" aria-label={label}
  style:--tab-index={activeIndex} style:--tab-count={tabs.length}>
  {#each tabs as tab, index (tab.id)}
    <button type="button" class="tab-btn" class:active={value === tab.id}
      id={idPrefix ? `${idPrefix}-${tab.id}` : undefined}
      role="tab" aria-selected={value === tab.id} aria-controls={panelId}
      aria-label={tab.count !== undefined ? `${tab.label} (${tab.count})` : tab.label}
      tabindex={value === tab.id ? 0 : -1} title={tab.label}
      onclick={() => onselect(tab.id)} onkeydown={event => handleKeydown(event, index)}>
      {#if tab.icon}<FluentIcon name={tab.icon} size={14} />{/if}
      <span class="tab-label">{tab.label}{#if tab.count !== undefined}<span class="tab-count">{tab.count}</span>{/if}</span>
    </button>
  {/each}
  <div class="tab-indicator" aria-hidden="true"></div>
</div>

<style>
  .sliding-tabs {
    position: relative;
    display: flex;
    flex-shrink: 0;
    background: rgba(255, 255, 255, 0.02);
    overflow: hidden;
  }

  .sliding-tabs.divider {
    border-bottom: 1px solid var(--border-subtle, rgba(255, 255, 255, 0.06));
  }

  .tab-btn {
    flex: 1;
    min-width: 0;
    border: 0;
    border-bottom: 2px solid transparent;
    background: transparent;
    color: var(--text-secondary);
    padding: 10px 4px;
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 4px;
    font: inherit;
    font-size: 10px;
    cursor: pointer;
    transition: color var(--transition-fast), background var(--transition-fast);
  }

  .tab-btn:hover {
    color: var(--text-primary);
    background: var(--bg-hover);
  }

  .tab-label {
    max-width: 100%;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }

  .tab-count {
    margin-left: 5px;
    opacity: 0.7;
    font-variant-numeric: tabular-nums;
  }

  .tab-btn.active {
    color: var(--accent);
  }

  .tab-indicator {
    position: absolute;
    bottom: 0;
    left: 0;
    width: calc(100% / var(--tab-count));
    height: 2px;
    background: var(--accent);
    border-radius: 1px;
    pointer-events: none;
    transform: translateX(calc(var(--tab-index) * 100%));
    transition: transform var(--transition-slow);
  }

  .tab-btn:focus-visible {
    outline: 2px solid var(--accent);
    outline-offset: -3px;
  }

  @media (prefers-reduced-motion: reduce) {
    .tab-indicator { transition: none; }
  }
</style>
