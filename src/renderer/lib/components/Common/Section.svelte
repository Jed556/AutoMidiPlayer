<script lang="ts">
  import type { Snippet } from 'svelte';
  import FluentIcon from './FluentIcon.svelte';

  interface Props {
    title?: string;
    description?: string;
    icon?: any;
    children?: Snippet;
    action?: Snippet;
    compact?: boolean;
  }

  let {
    title = '',
    description = '',
    icon = null,
    children,
    action,
    compact = false
  }: Props = $props();
</script>

<div class="section-card" class:compact>
  {#if title || action || description}
    <div class="section-header">
      <div class="header-left">
        {#if icon}
          <div class="header-icon">
            {#if typeof icon === 'string'}
              <FluentIcon name={icon} size={18} />
            {:else}
              {@const Icon = icon}
              <Icon size={18} />
            {/if}
          </div>
        {/if}
        <div class="header-text">
          {#if title}
            <h3 class="section-title">{title}</h3>
          {/if}
          {#if description}
            <p class="section-desc">{description}</p>
          {/if}
        </div>
      </div>
      {#if action}
        <div class="header-action">
          {@render action()}
        </div>
      {/if}
    </div>
  {/if}

  {#if children}
    <div class="section-body">
      {@render children()}
    </div>
  {/if}
</div>

<style>
  .section-card {
    background: var(--bg-card);
    border: none;
    border-radius: var(--radius-md);
    overflow: hidden;
    margin-bottom: 12px;
    box-shadow: var(--shadow-sm);
    transition: background var(--transition-fast), box-shadow var(--transition-fast);
  }

  .section-card:hover {
    background: var(--card-highlight, #2A2A2A);
  }

  .section-card.compact {
    margin-bottom: 8px;
  }

  .section-header {
    display: flex;
    align-items: center;
    justify-content: space-between;
    padding: 12px 16px;
    background: var(--control-fill);
    border-bottom: none;
    gap: 12px;
  }

  .header-left {
    display: flex;
    align-items: center;
    gap: 10px;
    min-width: 0;
  }

  .header-icon {
    display: flex;
    align-items: center;
    justify-content: center;
    width: 28px;
    height: 28px;
    border-radius: var(--radius-sm);
    background: var(--bg-tertiary);
    color: var(--accent);
    flex-shrink: 0;
  }

  .header-text {
    min-width: 0;
  }

  .section-title {
    font-size: 13.5px;
    font-weight: 600;
    color: var(--text-primary);
    margin: 0;
  }

  .section-desc {
    font-size: 11.5px;
    color: var(--text-tertiary);
    margin: 2px 0 0 0;
  }

  .header-action {
    flex-shrink: 0;
  }

  .section-body {
    padding: 14px 16px;
  }

  .section-card.compact .section-body {
    padding: 10px 14px;
  }
</style>
