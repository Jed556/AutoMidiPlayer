<script lang="ts" generics="T extends string | number | null">
  import { tick } from 'svelte';
  import { createDropdownState } from '../../ui/dropdownState.svelte';
  import { popupScaleIn } from '../../utils/transitions';
  import Button from './Button.svelte';
  import FluentIcon from './FluentIcon.svelte';
  import CustomScrollViewer from './CustomScrollViewer.svelte';

  interface Option { value: T; label: string; detail?: string; disabled?: boolean }
  let { value = $bindable(), options, label, id, disabled = false, onchange }: {
    value?: T;
    options: Option[];
    label: string;
    id?: string;
    disabled?: boolean;
    onchange?: (value: T) => void;
  } = $props();

  const dropdown = createDropdownState();
  const menuId = $props.id();
  let open = $derived(dropdown.isOpen);
  let selected = $derived(options.find(option => option.value === value));
  let highlighted = $state(0);
  let container: HTMLDivElement;
  let menu = $state<HTMLDivElement>();
  let scrollContainer = $state<HTMLDivElement>();

  function focusTrigger() { container.querySelector('button')?.focus(); }

  function closePicker() {
    if (menu) menu.inert = true;
    dropdown.isOpen = false;
  }

  function enterPopup(node: Element) {
    return window.matchMedia('(prefers-reduced-motion: reduce)').matches ? { duration: 0 } : popupScaleIn(node);
  }

  function exitPopup(node: Element) {
    return window.matchMedia('(prefers-reduced-motion: reduce)').matches ? { duration: 0 } : dropdown.outro(node);
  }

  function scrollToHighlighted() {
    const item = menu?.querySelectorAll<HTMLButtonElement>('[role="option"]')[highlighted];
    if (item && scrollContainer) {
      scrollContainer.scrollTop = item.offsetTop - scrollContainer.clientHeight / 2 + item.offsetHeight / 2;
    }
  }

  async function toggle(event: MouseEvent | KeyboardEvent) {
    event.stopPropagation();
    if (disabled || !options.length) return;
    if (open) { closePicker(); return; }
    highlighted = Math.max(0, options.findIndex(option => option.value === value));
    dropdown.isOpen = true;
    if (menu) menu.inert = false;
    await tick();
    scrollToHighlighted();
  }

  function select(index: number) {
    const option = options[index];
    if (!option || option.disabled) return;
    value = option.value;
    onchange?.(option.value);
    closePicker();
    focusTrigger();
  }

  function handleKeydown(event: KeyboardEvent) {
    if (!open) {
      if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
        event.preventDefault();
        void toggle(event);
      }
      return;
    }
    if (event.key === 'Escape') {
      event.preventDefault();
      event.stopPropagation();
      closePicker();
      focusTrigger();
    } else if (event.key === 'Tab') {
      closePicker();
    } else if (event.key === 'Enter' || event.key === ' ') {
      event.preventDefault();
      event.stopPropagation();
      select(highlighted);
    } else if (['ArrowDown', 'ArrowUp', 'Home', 'End'].includes(event.key)) {
      event.preventDefault();
      const step = event.key === 'ArrowUp' || event.key === 'End' ? -1 : 1;
      let next = event.key === 'Home' ? 0 : event.key === 'End' ? options.length - 1
        : (highlighted + step + options.length) % options.length;
      for (let count = 0; count < options.length && options[next]?.disabled; count++) {
        next = (next + step + options.length) % options.length;
      }
      highlighted = next;
      scrollToHighlighted();
    }
  }

  // The native top layer escapes dialog scroll clipping while Svelte owns motion.
  function showPopup(node: HTMLDivElement) {
    const margin = 8;
    let frame = 0;
    function position() {
      const rect = container.getBoundingClientRect();
      const viewport = container.closest('.scroll-content')?.getBoundingClientRect();
      if (rect.bottom <= Math.max(margin, viewport?.top ?? 0) ||
          rect.top >= Math.min(window.innerHeight - margin, viewport?.bottom ?? window.innerHeight)) {
        closePicker();
        return;
      }
      const below = window.innerHeight - rect.bottom - margin - 6;
      const above = rect.top - margin - 6;
      node.style.minWidth = `${Math.min(rect.width, window.innerWidth - margin * 2)}px`;
      node.style.setProperty('--picker-max-height', `${Math.max(32, Math.min(240, Math.max(above, below)))}px`);
      const upward = below < node.offsetHeight && above > below;
      node.style.left = `${Math.max(margin, Math.min(rect.left, window.innerWidth - node.offsetWidth - margin))}px`;
      node.style.top = `${Math.max(margin, upward ? rect.top - node.offsetHeight - 6 : rect.bottom + 6)}px`;
      node.style.transformOrigin = upward ? 'bottom center' : 'top center';
    }
    node.showPopover();
    position();

    const schedulePosition = () => {
      if (!frame) frame = requestAnimationFrame(() => { frame = 0; position(); });
    };
    const onScroll = (event: Event) => {
      if (event.target instanceof Node && !node.contains(event.target)) schedulePosition();
    };
    window.addEventListener('resize', schedulePosition);
    window.addEventListener('scroll', onScroll, true);
    return { destroy() {
      cancelAnimationFrame(frame);
      window.removeEventListener('resize', schedulePosition);
      window.removeEventListener('scroll', onScroll, true);
    } };
  }
</script>

<svelte:window onclick={event => {
  const target = event.target as Node;
  if (open && !container.contains(target) && !menu?.contains(target)) closePicker();
}} />

<div class="picker" bind:this={container}>
  <Button variant="picker" role="combobox" {id} disabled={disabled || !options.length} ariaLabel={label} ariaHaspopup="listbox"
    ariaExpanded={open} ariaControls={menuId} ariaActiveDescendant={open ? `${menuId}-${highlighted}` : undefined}
    onclick={toggle} onkeydown={handleKeydown}>
    <span class="picker-value">
      <span class:offset={selected?.detail}>{selected?.label ?? label}</span>
      {#if selected?.detail}<span>{selected.detail}</span>{/if}
    </span>
    <span class="picker-chevron" class:open><FluentIcon name="ChevronDown" size={12} /></span>
  </Button>
</div>

{#if open}
  <div class="picker-menu" id={menuId} bind:this={menu} popover="manual" use:showPopup use:dropdown.layer
    role="listbox" aria-label={label} tabindex="-1" onkeydown={handleKeydown}
    in:enterPopup out:exitPopup>
    <CustomScrollViewer class="picker-scroll" bind:scrollContainer padding="4px" gap="1px" fadeHeight={20} fadeRampDistance={18}>
      {#each options as option, index (option.value)}
        <button type="button" id={`${menuId}-${index}`} role="option" tabindex="-1" aria-selected={option.value === value} disabled={option.disabled}
          class="picker-option" class:has-detail={option.detail} class:highlighted={highlighted === index}
          onpointermove={() => { highlighted = index; }} onclick={() => select(index)}>
          <span class:offset={option.detail}>{option.label}</span>
          {#if option.detail}<span>{option.detail}</span>{/if}
        </button>
      {/each}
    </CustomScrollViewer>
  </div>
{/if}

<style>
  .picker { width: 100%; min-width: 0; }
  .picker-value { display: flex; align-items: center; gap: 6px; overflow: hidden; white-space: nowrap; text-overflow: ellipsis; }
  .offset { opacity: 0.7; font-variant-numeric: tabular-nums; }
  .picker-chevron { display: flex; flex-shrink: 0; color: var(--text-secondary); transition: transform var(--transition-fast); }
  .picker-chevron.open { transform: rotate(180deg); }
  .picker-menu { position: fixed; inset: auto; margin: 0; padding: 0; max-width: calc(100vw - 16px); border: none; border-radius: var(--radius-md, 8px); background: var(--bg-dialog, var(--card-bg)); color: var(--text-primary); box-shadow: var(--popup-shadow); overflow: hidden; }
  :global(.picker-scroll) { max-height: var(--picker-max-height, 240px); }
  .picker-option { display: flex; align-items: center; width: 100%; min-height: 30px; padding: 6px 10px; border: none; border-radius: 4px; color: inherit; background: transparent; font: inherit; font-size: 12.5px; text-align: left; cursor: pointer; transition: background var(--transition-fast), color var(--transition-fast), transform var(--transition-fast); }
  .picker-option.has-detail { display: grid; grid-template-columns: 24px 1fr; gap: 6px; padding: 6px 8px; }
  .picker-option .offset { text-align: right; }
  .picker-option.highlighted, .picker-option:hover:enabled { background: var(--control-fill-hover); }
  .picker-option[aria-selected='true'] { color: var(--accent); background: var(--accent-subtle); font-weight: 600; }
  .picker-option:active:enabled { transform: scale(0.98); }
  .picker-option:disabled { opacity: 0.45; cursor: not-allowed; }
  .picker-option:focus-visible { outline: 2px solid var(--accent); outline-offset: -2px; }
  @media (prefers-reduced-motion: reduce) { .picker-chevron, .picker-option { transition: none; } }
</style>
