<script lang="ts">
  import { tick } from 'svelte';
  import { createDropdownState } from '../../ui/dropdownState.svelte';
  import Button from './Button.svelte';
  import FluentIcon from './FluentIcon.svelte';

  interface Option { label: string; onclick: () => void; disabled?: boolean }
  let { label, title = '', menuLabel, disabled = false, primaryDisabled = false, onclick, options }: {
    label: string; title?: string; menuLabel: string; disabled?: boolean; primaryDisabled?: boolean;
    onclick: () => void; options: Option[];
  } = $props();
  let trigger: HTMLButtonElement;
  let menu: HTMLDivElement;
  let open = $state(false);
  const dropdown = createDropdownState();
  const menuId = $props.id();

  async function toggleMenu(focusLast = false) {
    if (open || disabled) { menu.hidePopover(); return; }
    const rect = trigger.getBoundingClientRect();
    menu.style.top = 'auto';
    menu.style.right = `${Math.max(8, window.innerWidth - rect.right)}px`;
    menu.style.bottom = `${window.innerHeight - rect.top + 6}px`;
    dropdown.isOpen = true;
    menu.showPopover();
    menu.style.right = `${Math.min(Math.max(8, window.innerWidth - rect.right), Math.max(8, window.innerWidth - menu.offsetWidth - 8))}px`;
    const popup = menu.getBoundingClientRect();
    const downward = popup.top < 8;
    menu.style.bottom = downward ? 'auto' : `${window.innerHeight - rect.top + 6}px`;
    menu.style.top = downward ? `${Math.max(8, Math.min(rect.bottom + 6, window.innerHeight - menu.offsetHeight - 8))}px` : 'auto';
    menu.style.transformOrigin = downward ? 'top right' : 'bottom right';
    await tick();
    const items = menu.querySelectorAll<HTMLButtonElement>('button:not(:disabled)');
    items[focusLast ? items.length - 1 : 0]?.focus();
  }

  $effect(() => {
    if (open && !dropdown.isOpen) menu.hidePopover();
  });

  function navigate(event: KeyboardEvent) {
    if (event.key === 'Escape') { event.stopPropagation(); menu.hidePopover(); trigger.focus(); return; }
    if (event.key === 'Tab') { menu.hidePopover(); return; }
    const items = [...menu.querySelectorAll<HTMLButtonElement>('button:not(:disabled)')];
    const index = items.indexOf(document.activeElement as HTMLButtonElement);
    const next = event.key === 'Home' ? 0 : event.key === 'End' ? items.length - 1
      : event.key === 'ArrowDown' ? (index + 1) % items.length
      : event.key === 'ArrowUp' ? (index - 1 + items.length) % items.length : -1;
    if (next >= 0) { event.preventDefault(); items[next]?.focus(); }
  }
</script>

<div class="split-button">
  <Button variant="accent" class="split-main" {title} disabled={disabled || primaryDisabled} {onclick}>{label}</Button>
  <button type="button" class="split-toggle" bind:this={trigger} {disabled}
    aria-label={menuLabel} aria-haspopup="menu" aria-expanded={open} aria-controls={menuId}
    onclick={() => toggleMenu()} onkeydown={event => {
      if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
        event.preventDefault(); if (!open) void toggleMenu(event.key === 'ArrowUp');
      }
    }}><span class="split-chevron" class:open><FluentIcon name="ChevronDown" size={12} /></span></button>
</div>
<div bind:this={menu} id={menuId} popover="auto" role="menu" aria-label={menuLabel}
  use:dropdown.layer
  class="split-menu" tabindex="-1" onkeydown={navigate}
  ontoggle={event => {
    open = event.newState === 'open';
    if (!open) dropdown.isOpen = false;
  }}>
  {#each options as option}
    <button type="button" role="menuitem" disabled={option.disabled || disabled}
      onclick={() => { menu.hidePopover(); trigger.focus(); option.onclick(); }}>{option.label}</button>
  {/each}
</div>

<style>
  .split-button { display: inline-flex; }
  .split-button :global(.split-main) { border-radius: var(--radius-sm, 4px) 0 0 var(--radius-sm, 4px); }
  .split-toggle { display: grid; place-items: center; width: 30px; height: 32px; padding: 0; color: #fff; background: var(--accent-color); border: 0; border-radius: 0 var(--radius-sm, 4px) var(--radius-sm, 4px) 0; cursor: pointer; transition: filter var(--transition-fast), transform var(--transition-fast), opacity var(--transition-fast); }
  .split-toggle:hover:not(:disabled) { filter: brightness(1.1); }
  .split-toggle:active:not(:disabled) { transform: scale(0.96); }
  .split-chevron { display: flex; transition: transform var(--transition-fast); }
  .split-chevron.open { transform: rotate(180deg); }
  .split-toggle:disabled { opacity: .45; cursor: not-allowed; }
  .split-toggle:focus-visible, .split-menu button:focus-visible { outline: 2px solid var(--accent); outline-offset: 2px; }
  .split-menu { position: fixed; inset: auto; margin: 0; padding: 4px; min-width: 210px; max-width: calc(100vw - 16px); background: var(--bg-dialog, var(--bg-secondary)); color: var(--text-primary); border: none; border-radius: var(--radius-md, 8px); box-shadow: var(--popup-shadow); opacity: 0; transform: scale(0.92); transform-origin: bottom right; transition: opacity var(--anim-normal, 0.2s) ease, transform var(--anim-normal, 0.2s) cubic-bezier(0.16, 1, 0.3, 1), display var(--anim-normal, 0.2s) allow-discrete, overlay var(--anim-normal, 0.2s) allow-discrete; }
  .split-menu:popover-open { opacity: 1; transform: scale(1); }
  @starting-style { .split-menu:popover-open { opacity: 0; transform: scale(0.92); } }
  .split-menu button { display: block; width: 100%; padding: 9px 12px; border: 0; border-radius: 4px; color: inherit; background: transparent; text-align: left; font: inherit; font-size: 12.5px; cursor: pointer; transition: background var(--transition-fast), transform var(--transition-fast); }
  .split-menu button:hover, .split-menu button:focus { background: var(--control-fill-hover); }
  .split-menu button:active:not(:disabled) { transform: scale(0.98); }
  .split-menu button:disabled { opacity: .45; cursor: not-allowed; }
  @media (prefers-reduced-motion: reduce) { .split-menu, .split-toggle, .split-chevron, .split-menu button { transition: none; } }
</style>
