<script lang="ts">
  import FluentIcon from '../Icons/FluentIcon.svelte';
  import { tooltip } from '../../actions/tooltip';

  interface Tab {
    id: string;
    label: string;
    icon?: string;
    count?: number;
    title?: string;
  }

  interface Props {
    tabs: readonly Tab[];
    value: string;
    onselect: (id: string) => void;
    label: string;
    divider?: boolean;
    variant?: 'underline' | 'pill';
    disabled?: boolean;
    idPrefix?: string;
    panelId?: string;
  }

  let {
    tabs,
    value,
    onselect,
    label,
    divider = true,
    variant = 'underline',
    disabled = false,
    idPrefix,
    panelId,
  }: Props = $props();
  const activeIndex = $derived(Math.max(0, tabs.findIndex(tab => tab.id === value)));

  function handleKeydown(event: KeyboardEvent, index: number) {
    if (disabled) return;
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

<div class="sliding-tabs" class:divider={divider && variant === 'underline'} class:pill={variant === 'pill'}
  role="tablist" aria-label={label}
  style:--tab-index={activeIndex} style:--tab-count={tabs.length}>
  {#each tabs as tab, index (tab.id)}
    <button type="button" class="tab-btn" class:active={value === tab.id}
      id={idPrefix ? `${idPrefix}-${tab.id}` : undefined}
      role="tab" aria-selected={value === tab.id} aria-controls={panelId}
      aria-label={tab.count !== undefined ? `${tab.label} (${tab.count})` : tab.label}
      tabindex={value === tab.id ? 0 : -1} use:tooltip={variant === 'pill' ? tab.title : (tab.title ?? tab.label)} {disabled}
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

  .sliding-tabs.pill {
    display: inline-grid;
    grid-auto-flow: column;
    grid-auto-columns: minmax(0, 1fr);
    padding: 2px;
    border-radius: var(--radius-sm);
    background: var(--control-fill);
  }

  .tab-btn {
    position: relative;
    z-index: 1;
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

  .sliding-tabs.pill .tab-btn {
    flex-direction: row;
    justify-content: center;
    padding: 6px 12px;
    border-bottom: 0;
    border-radius: 3px;
    font-size: 12px;
    font-weight: 500;
  }

  .tab-btn:hover:not(:disabled) {
    color: var(--text-primary);
    background: var(--bg-hover);
  }

  .sliding-tabs.pill .tab-btn:hover:not(:disabled).active {
    background: transparent;
  }

  .tab-btn:disabled {
    opacity: 0.5;
    cursor: default;
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

  .sliding-tabs.pill .tab-btn.active {
    color: #ffffff;
    font-weight: 600;
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

  .sliding-tabs.pill .tab-indicator {
    top: 2px;
    bottom: 2px;
    left: 2px;
    height: auto;
    width: calc((100% - 4px) / var(--tab-count));
    border-radius: 3px;
    background: var(--accent);
    z-index: 0;
  }

  .tab-btn:focus-visible {
    outline: 2px solid var(--accent);
    outline-offset: -3px;
  }

  .sliding-tabs.pill .tab-btn:focus-visible {
    outline-offset: -2px;
  }

  @media (prefers-reduced-motion: reduce) {
    .tab-indicator { transition: none; }
  }
</style>
