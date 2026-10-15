<script lang="ts">
  import type { Snippet } from 'svelte';
  import FluentIcon from '../Common/FluentIcon.svelte';
  import CustomScrollViewer from '../Common/CustomScrollViewer.svelte';
  import { uiStore } from '../../state/uiStore.svelte';

  interface Props {
    title?: string;
    subtitle?: string;
    icon?: string;
    iconColor?: string;
    showCloseButton?: boolean;
    showBackButton?: boolean;
    onClose?: () => void;
    onBack?: () => void;
    hideFooter?: boolean;
    dividers?: boolean;
    size?: 'normal' | 'large';
    class?: string;
    bodyClass?: string;
    scrollContainer?: HTMLDivElement;
    header?: Snippet;
    topRight?: Snippet;
    toolbar?: Snippet;
    children?: Snippet;
    footer?: Snippet;
  }

  let {
    title = '',
    subtitle = '',
    icon,
    iconColor,
    showCloseButton = true,
    showBackButton = false,
    onClose = () => uiStore.closeDialog(),
    onBack,
    hideFooter = false,
    dividers = true,
    size = 'normal',
    class: className = '',
    bodyClass = '',
    scrollContainer = $bindable(),
    header,
    topRight,
    toolbar,
    children,
    footer
  }: Props = $props();
</script>

<div class="content-dialog {className}" class:large-dialog={size === 'large'} class:dividers>
  <div class="dialog-header">
    {#if header}
      {@render header()}
    {:else}
      <div class="title-wrap">
        {#if showBackButton && onBack}
          <button type="button" class="back-btn" onclick={onBack} aria-label="Back">
            <FluentIcon name="ChevronLeft" size={16} />
          </button>
        {/if}
        {#if icon}
          <div class="icon-wrap" style={iconColor ? `color: ${iconColor}` : undefined}>
            <FluentIcon name={icon} size={size === 'large' ? 20 : 16} />
          </div>
        {/if}
        <div class="title-col">
          {#if title}
            <span class="hdr-title">{title}</span>
          {/if}
          {#if subtitle}
            <span class="hdr-subtitle">{subtitle}</span>
          {/if}
        </div>
      </div>
    {/if}

    <div class="top-right-wrap">
      {#if topRight}
        {@render topRight()}
      {/if}
      {#if showCloseButton}
        <button type="button" class="close-btn" onclick={onClose} aria-label="Close">
          <FluentIcon name="Dismiss" size={size === 'large' ? 16 : 14} />
        </button>
      {/if}
    </div>
  </div>

  {#if toolbar}
    <div class="dialog-toolbar">{@render toolbar()}</div>
  {/if}

  <CustomScrollViewer
    bind:scrollContainer
    class="dialog-body-viewer"
    contentClass="dialog-body {bodyClass}"
    padding={size === 'large' ? '20px 24px' : '18px'}
    fadeHeight={28}
  >
    {#if children}
      {@render children()}
    {/if}
  </CustomScrollViewer>

  {#if footer && !hideFooter}
    <div class="dialog-footer">
      {@render footer()}
    </div>
  {/if}
</div>

<style>
  .content-dialog {
    display: flex;
    flex-direction: column;
    width: 100%;
    height: 100%;
    min-height: 0;
    flex: 1;
    overflow: hidden;
  }

  .dialog-header {
    display: flex;
    align-items: center;
    justify-content: space-between;
    padding: 14px 18px;
    flex-shrink: 0;
    gap: 12px;
  }

  .dividers .dialog-header {
    border-bottom: 1px solid var(--border-subtle);
  }

  .dialog-toolbar {
    padding: 0 18px;
    flex-shrink: 0;
  }

  .large-dialog .dialog-toolbar {
    padding: 0 24px;
  }

  .large-dialog .dialog-header {
    padding: 16px 24px;
  }

  .title-wrap {
    display: flex;
    align-items: center;
    gap: 10px;
    min-width: 0;
  }

  .large-dialog .title-wrap {
    gap: 12px;
  }

  .icon-wrap {
    display: flex;
    align-items: center;
    justify-content: center;
    flex-shrink: 0;
    color: var(--accent);
  }

  .title-col {
    display: flex;
    flex-direction: column;
    gap: 2px;
    min-width: 0;
  }

  .hdr-title {
    font-size: 14px;
    font-weight: 600;
    color: var(--text-primary);
    line-height: 1.25;
  }

  .large-dialog .hdr-title {
    font-size: 18px;
  }

  .hdr-subtitle {
    font-size: 12px;
    color: var(--text-tertiary);
    line-height: 1.2;
  }

  .large-dialog .hdr-subtitle {
    font-size: 13px;
  }

  .top-right-wrap {
    display: flex;
    align-items: center;
    gap: 6px;
    flex-shrink: 0;
  }

  .back-btn {
    background: transparent;
    border: none;
    color: var(--text-secondary);
    cursor: pointer;
    display: flex;
    align-items: center;
    justify-content: center;
    width: 30px;
    height: 30px;
    border-radius: var(--radius-sm);
    transition: all var(--transition-fast);
  }

  .large-dialog .back-btn {
    width: 32px;
    height: 32px;
  }

  .back-btn:hover {
    background: var(--bg-hover);
    color: var(--text-primary);
  }

  .close-btn {
    background: transparent;
    border: none;
    color: var(--text-tertiary);
    cursor: pointer;
    display: flex;
    align-items: center;
    justify-content: center;
    width: 28px;
    height: 28px;
    border-radius: var(--radius-sm);
    transition: all var(--transition-fast);
  }

  .large-dialog .close-btn {
    width: 32px;
    height: 32px;
  }

  .close-btn:hover {
    background: var(--bg-hover);
    color: var(--text-primary);
  }

  :global(.dialog-body-viewer) {
    flex: 1;
    min-height: 0;
  }

  .dialog-footer {
    display: flex;
    align-items: center;
    justify-content: flex-end;
    gap: 8px;
    padding: 12px 18px;
    flex-shrink: 0;
  }

  .dividers .dialog-footer {
    border-top: 1px solid var(--border-subtle);
  }

  .large-dialog .dialog-footer {
    padding: 14px 24px;
    gap: 10px;
  }

  .dialog-footer:empty {
    display: none !important;
  }

  .dialog-footer :global(.fluent-btn) {
    min-width: 80px;
    height: 32px;
    font-size: 13px;
  }

  .large-dialog .dialog-footer :global(.fluent-btn) {
    min-width: 90px;
    height: 34px;
  }
</style>
