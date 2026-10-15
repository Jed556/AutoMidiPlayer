<script lang="ts">
  import { onDestroy, tick, untrack, type Snippet } from 'svelte';
  import { portal } from '../../actions/portal';
  import { createFrameResizeObserver } from '../../utils/frameResizeObserver';
  import { popupScaleIn } from '../../utils/transitions';
  import { createDropdownState } from '../../ui/dropdownState.svelte';
  import { animationStore } from '../../state/animationStore.svelte';
  import OverflowText from './OverflowText.svelte';
  import FluentIcon from './FluentIcon.svelte';
  import CustomScrollViewer from './CustomScrollViewer.svelte';
  import { tooltip } from '../../actions/tooltip';

  export interface DropdownSubmenu {
    isSubmenu: true;
    label: string;
    icon?: string;
    items: DropdownOption[];
  }

  export interface DropdownOption {
    value: any;
    label: string;
    description?: string;
    icon?: string;
    disabled?: boolean;
    isSubmenu?: boolean;
    items?: DropdownOption[];
    [key: string]: any;
  }

  interface Props {
    options?: (DropdownOption | DropdownSubmenu | string | number)[];
    items?: (DropdownOption | DropdownSubmenu | string | number)[]; // alias for options
    value?: any;
    selectedId?: any; // alias for value
    placeholder?: string;
    title?: string;
    showTooltips?: boolean;
    tooltipText?: string;
    variant?: 'standard' | 'ghost';
    chevronSize?: number;
    width?: string;
    minWidth?: string;
    class?: string;
    disabled?: boolean;
    placement?: 'auto' | 'bottom' | 'top';
    align?: 'left' | 'right' | 'auto';
    scaleAnchor?: 'center' | 'corner';
    scaleOrigin?: 'center' | 'corner';
    onchange?: (value: any, option?: DropdownOption) => void;
    onselect?: (option: DropdownOption) => void;
    children?: Snippet;
    itemSnippet?: Snippet<[DropdownOption]>;
  }

  let {
    options,
    items,
    value = $bindable(),
    selectedId = $bindable(),
    placeholder = '',
    title = '',
    showTooltips = true,
    tooltipText = '',
    variant = 'standard',
    chevronSize,
    width = '',
    minWidth = '',
    class: customClass = '',
    disabled = false,
    placement = 'auto',
    align = 'auto',
    scaleAnchor = 'center',
    scaleOrigin,
    onchange,
    onselect,
    children,
    itemSnippet
  }: Props = $props();

  let effectiveScaleAnchor = $derived(scaleAnchor ?? scaleOrigin ?? 'center');

  const dropdown = createDropdownState();
  let isOpen = $derived(dropdown.isOpen);
  let openUpward = $state(false);
  let alignRight = $state(false);
  let highlightedIndex = $state(-1);
  let dropdownRef = $state<HTMLDivElement | null>(null);
  let menuRef = $state<HTMLDivElement | null>(null);

  // Sync value and selectedId aliases
  let effectiveValue = $derived(value !== undefined ? value : selectedId);
  let effectiveWidth = $derived(width ? width : (variant === 'ghost' ? 'auto' : '100%'));
  let effectiveChevronSize = $derived(
    chevronSize !== undefined
      ? chevronSize
      : (variant === 'ghost' ? 10 : 11)
  );

  let hoveredSubmenu = $state<number | null>(null);
  let flyoutRef = $state<HTMLDivElement | null>(null);
  let flyoutSurface = $state<HTMLDivElement>();
  let flyoutContent = $state<HTMLDivElement>();
  let flyoutList = $state<HTMLDivElement>();
  let flyoutAnimations: Animation[] = [];
  let outgoingList: HTMLElement | null = null;
  let flyoutRevision = 0;
  let updateFlyoutPosition = () => {};
  let submenuAnchor: HTMLElement | null = null;
  let submenuCloseTimer: ReturnType<typeof setTimeout> | null = null;
  let activeSubmenu = $derived(hoveredSubmenu === null ? null : normalizedOptions[hoveredSubmenu]);

  function cancelFlyoutMorph() {
    flyoutAnimations.forEach(animation => animation.cancel());
    flyoutAnimations = [];
    outgoingList?.remove();
    outgoingList = null;
  }

  $effect.pre(() => {
    const submenu = activeSubmenu;
    const open = isOpen;
    untrack(() => {
      const revision = ++flyoutRevision;
      if (!open || !submenu) {
        cancelFlyoutMorph();
        return;
      }
      const before = flyoutSurface?.getBoundingClientRect();
      const previousList = flyoutList?.cloneNode(true) as HTMLDivElement | undefined;
      const previousWidth = flyoutList?.offsetWidth ?? 0;
      const previousScroll = flyoutList?.scrollTop ?? 0;

      void (async () => {
        await tick();
        if (revision !== flyoutRevision || !flyoutRef || !flyoutSurface || !flyoutContent || !flyoutList) return;
        cancelFlyoutMorph();
        flyoutList.scrollTop = 0;
        updateFlyoutPosition();
        const duration = animationStore.durationMs;
        if (!before?.width || !before.height || !previousList || !duration ||
          document.documentElement.classList.contains('no-animations') ||
          window.matchMedia('(prefers-reduced-motion: reduce)').matches) return;

        const after = flyoutSurface.getBoundingClientRect();
        if (!after.width || !after.height) return;
        const timing = { duration, easing: 'cubic-bezier(0.16, 1, 0.3, 1)' };
        // Move one persistent flyout and resize its surface without stretching the text.
        flyoutAnimations.push(flyoutRef.animate([
          { translate: `${before.left - after.left}px ${before.top - after.top}px` },
          { translate: '0px 0px' }
        ], timing));
        flyoutAnimations.push(flyoutSurface.animate([
          { transform: `scale(${before.width / after.width}, ${before.height / after.height})` },
          { transform: 'scale(1, 1)' }
        ], timing));
        flyoutAnimations.push(flyoutContent.animate([
          { clipPath: `inset(0px ${after.width - before.width}px ${after.height - before.height}px 0px)` },
          { clipPath: 'inset(0px 0px 0px 0px)' }
        ], timing));

        outgoingList = previousList;
        previousList.setAttribute('aria-hidden', 'true');
        previousList.inert = true;
        previousList.style.cssText = `position:absolute;left:0;top:0;width:${previousWidth}px;pointer-events:none;`;
        flyoutContent.appendChild(previousList);
        previousList.scrollTop = previousScroll;
        // Use the tooltip's shared diagonal edge to sweep between the two lists.
        const slant = 24;
        const extent = Math.max(previousWidth, flyoutList.offsetWidth) + slant;
        const reveal = (edge: number) => `polygon(0px 0%, ${edge + slant}px 0%, ${edge}px 100%, 0px 100%)`;
        const conceal = (edge: number) => `polygon(${edge + slant}px 0%, ${extent}px 0%, ${extent}px 100%, ${edge}px 100%)`;
        flyoutAnimations.push(previousList.animate([
          { clipPath: conceal(-slant) }, { clipPath: conceal(extent) }
        ], timing));
        const sweep = flyoutList.animate([
          { clipPath: reveal(-slant) }, { clipPath: reveal(extent) }
        ], timing);
        flyoutAnimations.push(sweep);
        void sweep.finished.then(() => {
          if (revision === flyoutRevision) cancelFlyoutMorph();
        }).catch(() => {});
      })();
    });
  });

  function cancelSubmenuClose() {
    if (submenuCloseTimer) clearTimeout(submenuCloseTimer);
    submenuCloseTimer = null;
  }

  function closeSubmenu() {
    cancelSubmenuClose();
    hoveredSubmenu = null;
    submenuAnchor = null;
  }

  function scheduleSubmenuClose() {
    cancelSubmenuClose();
    submenuCloseTimer = setTimeout(closeSubmenu, 150);
  }

  function openSubmenu(index: number, anchor: HTMLElement) {
    cancelSubmenuClose();
    submenuAnchor = anchor;
    hoveredSubmenu = index;
  }

  $effect(() => {
    if (disabled) dropdown.isOpen = false;
    if (!isOpen) closeSubmenu();
  });

  onDestroy(() => {
    ++flyoutRevision;
    cancelFlyoutMorph();
    cancelSubmenuClose();
    if (changeTimer) clearTimeout(changeTimer);
  });

  function positionSubmenu(node: HTMLDivElement) {
    const parentMenu = menuRef;
    function updatePosition() {
      if (!submenuAnchor || !parentMenu) return;
      const row = submenuAnchor.getBoundingClientRect();
      const menu = parentMenu.getBoundingClientRect();
      const margin = 8;
      const gap = 4;
      const width = node.offsetWidth;
      const height = node.offsetHeight;
      const spaceRight = window.innerWidth - menu.right - gap - margin;
      const spaceLeft = menu.left - gap - margin;
      const openLeft = width > spaceRight && spaceLeft > spaceRight;
      node.style.setProperty('--flyout-origin', openLeft ? 'right top' : 'left top');
      const left = openLeft ? menu.left - width - gap : menu.right + gap;
      node.style.left = `${Math.max(margin, Math.min(left, window.innerWidth - width - margin))}px`;
      node.style.top = `${Math.max(margin, Math.min(row.top - 4, window.innerHeight - height - margin))}px`;
    }

    function handleScroll(event: Event) {
      // Scrolling the flyout itself must keep it open; other scrolls move its anchor.
      if (event.target instanceof Node && node.contains(event.target)) return;
      closeSubmenu();
    }

    updatePosition();
    updateFlyoutPosition = updatePosition;
    const observer = createFrameResizeObserver(updatePosition);
    observer.observe(node);
    if (parentMenu) observer.observe(parentMenu);
    parentMenu?.addEventListener('introend', updatePosition);
    window.addEventListener('resize', updatePosition);
    document.addEventListener('scroll', handleScroll, true);
    return {
      destroy() {
        if (updateFlyoutPosition === updatePosition) updateFlyoutPosition = () => {};
        observer.disconnect();
        parentMenu?.removeEventListener('introend', updatePosition);
        window.removeEventListener('resize', updatePosition);
        document.removeEventListener('scroll', handleScroll, true);
      }
    };
  }

  // Raw array from either options or items prop
  let rawList = $derived(options ?? items ?? []);

  // Normalize options into a uniform array of DropdownOption (including submenus)
  let normalizedOptions = $derived<DropdownOption[]>(
    rawList.map(opt => {
      if (opt !== null && typeof opt === 'object') {
        if ('isSubmenu' in opt && opt.isSubmenu) {
          const subItems = ((opt as any).items || []).map((sub: any) => {
            const subVal = sub.value !== undefined ? sub.value : sub.id;
            const subLbl = sub.label !== undefined ? sub.label : (sub.name !== undefined ? sub.name : String(subVal ?? ''));
            return {
              ...sub,
              value: subVal,
              label: subLbl
            };
          });
          return {
            ...opt,
            value: (opt as any).label,
            label: (opt as any).label,
            isSubmenu: true,
            items: subItems
          };
        }
        const val = opt.value !== undefined ? opt.value : (opt as any).id;
        const lbl = opt.label !== undefined ? opt.label : ((opt as any).name !== undefined ? (opt as any).name : String(val ?? ''));
        return {
          ...opt,
          value: val,
          label: lbl
        };
      }
      return { value: opt, label: String(opt) };
    })
  );

  function findOptionRecursively(list: DropdownOption[], targetVal: any): DropdownOption | null {
    for (const item of list) {
      if (item.isSubmenu && item.items) {
        const found = findOptionRecursively(item.items, targetVal);
        if (found) return found;
      } else if (String(item.value) === String(targetVal)) {
        return item;
      }
    }
    return null;
  }

  let selectedOption = $derived.by(() => {
    const found = findOptionRecursively(normalizedOptions, effectiveValue);
    if (found) return found;
    if (placeholder) return null;
    const first = normalizedOptions[0];
    if (first && first.isSubmenu && first.items && first.items.length > 0) {
      return first.items[0];
    }
    return first || null;
  });

  function toggle(e: MouseEvent) {
    if (disabled) return;
    e.stopPropagation();

    if (!isOpen && dropdownRef) {
      const rect = dropdownRef.getBoundingClientRect();
      if (placement === 'top') {
        openUpward = true;
      } else if (placement === 'bottom') {
        openUpward = false;
      } else {
        const spaceBelow = window.innerHeight - rect.bottom;
        openUpward = spaceBelow < 250 && rect.top > spaceBelow;
      }

      if (align === 'right') {
        alignRight = true;
      } else if (align === 'left') {
        alignRight = false;
      } else {
        const spaceRight = window.innerWidth - rect.left;
        alignRight = spaceRight < 220 && rect.right > 220;
      }

      const selectedIdx = normalizedOptions.findIndex(o => String(o.value) === String(effectiveValue));
      highlightedIndex = selectedIdx >= 0 ? selectedIdx : 0;
    }

    dropdown.isOpen = !isOpen;
  }

  let hasChanged = $state(false);
  let changeTimer: ReturnType<typeof setTimeout> | null = null;

  function select(opt: DropdownOption) {
    if (disabled || opt.disabled) return;
    const isNew = String(opt.value) !== String(effectiveValue);
    value = opt.value;
    selectedId = opt.value;
    dropdown.isOpen = false;
    if (isNew) {
      hasChanged = true;
      if (changeTimer) clearTimeout(changeTimer);
      changeTimer = setTimeout(() => {
        hasChanged = false;
      }, 700);
    }
    onchange?.(opt.value, opt);
    onselect?.(opt);
  }

  function handleDocumentClick(e: MouseEvent) {
    if (dropdownRef && !dropdownRef.contains(e.target as Node) && !flyoutRef?.contains(e.target as Node)) {
      dropdown.isOpen = false;
    }
  }

  function handleKeydown(e: KeyboardEvent) {
    if (!dropdownRef?.contains(e.target as Node) && !flyoutRef?.contains(e.target as Node)) return;
    if (!isOpen) {
      if ((e.key === 'ArrowDown' || e.key === 'ArrowUp' || e.key === 'Enter') && document.activeElement === dropdownRef?.querySelector('button')) {
        e.preventDefault();
        toggle(e as any);
      }
      return;
    }

    if (e.key === 'Escape') {
      dropdown.isOpen = false;
      e.stopPropagation();
      dropdownRef?.querySelector('button')?.focus();
    } else if (e.key === 'ArrowDown') {
      e.preventDefault();
      highlightedIndex = (highlightedIndex + 1) % normalizedOptions.length;
      scrollToHighlighted();
    } else if (e.key === 'ArrowUp') {
      e.preventDefault();
      highlightedIndex = (highlightedIndex - 1 + normalizedOptions.length) % normalizedOptions.length;
      scrollToHighlighted();
    } else if (e.key === 'Enter' || e.key === ' ') {
      e.preventDefault();
      if (highlightedIndex >= 0 && highlightedIndex < normalizedOptions.length) {
        select(normalizedOptions[highlightedIndex]);
      }
    }
  }

  function scrollToHighlighted() {
    if (!menuRef) return;
    const items = menuRef.querySelectorAll<HTMLButtonElement>('.dropdown-item');
    if (items[highlightedIndex]) {
      items[highlightedIndex].scrollIntoView({ block: 'nearest' });
    }
  }
</script>

<svelte:window onclick={handleDocumentClick} onkeydown={handleKeydown} />

<div
  class="fluent-dropdown variant-{variant} {customClass}"
  style="width: {effectiveWidth}; {minWidth ? `min-width: ${minWidth};` : ''}"
  bind:this={dropdownRef}
  data-tooltip-disabled={!showTooltips && !tooltipText ? 'true' : undefined}
  use:tooltip={tooltipText ? { text: tooltipText, followCursor: false, disabled: false } : undefined}
>
  <button
    type="button"
    class="dropdown-trigger"
    class:is-open={isOpen}
    class:is-disabled={disabled}
    class:has-changed={hasChanged}
    title={showTooltips ? title : undefined}
    {disabled}
    onclick={toggle}
    aria-haspopup="listbox"
    aria-expanded={isOpen}
  >
    <div class="trigger-content">
      {#if children}
        {@render children()}
      {:else if selectedOption}
        {#if selectedOption.icon}
          <FluentIcon name={selectedOption.icon} size={14} class="opt-icon" />
        {/if}
        <span class="trigger-label"><OverflowText text={selectedOption.label} hoverTarget="button" showTooltip={showTooltips} /></span>
      {:else}
        <span class="trigger-label trigger-placeholder"><OverflowText text={placeholder} hoverTarget="button" showTooltip={showTooltips} /></span>
      {/if}
    </div>
    <div class="chevron-icon" class:rotated={isOpen}>
      <FluentIcon name="ChevronDown" size={effectiveChevronSize} />
    </div>
  </button>

  {#if isOpen}
    <div
      class="dropdown-menu"
      class:open-upward={openUpward}
      class:align-right={alignRight}
      class:anchor-corner={effectiveScaleAnchor === 'corner'}
      bind:this={menuRef}
      use:dropdown.layer
      role="listbox"
      in:popupScaleIn
      out:dropdown.outro
    >
      <CustomScrollViewer
        class="menu-scroll-viewer"
        contentClass="menu-scroll"
        padding="4px"
        gap="2px"
        fadeHeight={20}
        fadeRampDistance={18}
      >
        {#each normalizedOptions as opt, idx}
          {#if opt.isSubmenu}
            <div
              class="dropdown-submenu-wrap"
              role="group"
              aria-label={opt.label}
              onmouseenter={(e) => openSubmenu(idx, e.currentTarget)}
              onmouseleave={scheduleSubmenuClose}
            >
              <button
                type="button"
                class="dropdown-item submenu-trigger"
                class:is-active={hoveredSubmenu === idx}
                aria-haspopup="listbox"
                aria-expanded={hoveredSubmenu === idx}
                onclick={(e) => {
                  e.stopPropagation();
                  if (hoveredSubmenu === idx) closeSubmenu();
                  else openSubmenu(idx, e.currentTarget);
                }}
              >
                <div class="item-main">
                  {#if opt.icon}
                    <FluentIcon name={opt.icon} size={14} class="item-icon" />
                  {:else}
                    <FluentIcon name="Folder" size={13} class="item-icon folder-icon" />
                  {/if}
                  <span class="item-label"><OverflowText text={opt.label} hoverTarget="button" showTooltip={showTooltips} /></span>
                </div>
                <div class="submenu-chevron-wrap">
                  <FluentIcon name="ChevronRight" size={10} class="submenu-chevron" />
                </div>
              </button>
            </div>
          {:else}
            {@const isSelected = String(opt.value) === String(effectiveValue)}
            {@const isHighlighted = idx === highlightedIndex}
            <button
              type="button"
              class="dropdown-item"
              class:is-selected={isSelected}
              class:is-highlighted={isHighlighted}
              class:is-disabled={opt.disabled}
              disabled={opt.disabled}
              role="option"
              aria-selected={isSelected}
              onclick={() => select(opt)}
              onmouseenter={() => (highlightedIndex = idx)}
            >
              {#if itemSnippet}
                {@render itemSnippet(opt)}
              {:else}
                <div class="item-main">
                  {#if opt.icon}
                    <FluentIcon name={opt.icon} size={14} class="item-icon" />
                  {/if}
                  <div class="item-text-stack">
                    <span class="item-label"><OverflowText text={opt.label} hoverTarget="button" showTooltip={showTooltips} /></span>
                    {#if opt.description}
                      <span class="item-desc">{opt.description}</span>
                    {/if}
                  </div>
                </div>
                {#if isSelected}
                  <div class="check-indicator">
                    <FluentIcon name="Checkmark" size={13} />
                  </div>
                {/if}
              {/if}
            </button>
          {/if}
        {/each}
      </CustomScrollViewer>
    </div>
  {/if}
</div>

{#if isOpen && activeSubmenu?.items}
  <div
    class="dropdown-submenu-flyout"
    bind:this={flyoutRef}
    use:portal
    use:dropdown.layer
    use:positionSubmenu
    role="listbox"
    tabindex="-1"
    aria-label={activeSubmenu.label}
    data-tooltip-disabled={!showTooltips ? 'true' : undefined}
    onmouseenter={cancelSubmenuClose}
    onmouseleave={scheduleSubmenuClose}
  >
    <div class="dropdown-submenu-panel" in:popupScaleIn out:dropdown.outro>
      <div class="dropdown-submenu-surface" bind:this={flyoutSurface}></div>
      <div class="dropdown-submenu-content" bind:this={flyoutContent}>
        <div class="dropdown-submenu-list" bind:this={flyoutList}>
          {#each activeSubmenu.items as subOpt}
            {@const isSubSelected = String(subOpt.value) === String(effectiveValue)}
            <button
              type="button"
              class="dropdown-item"
              class:is-selected={isSubSelected}
              class:is-disabled={subOpt.disabled}
              disabled={subOpt.disabled}
              role="option"
              aria-selected={isSubSelected}
              onclick={() => select(subOpt)}
            >
              <div class="item-main">
                {#if subOpt.icon}
                  <FluentIcon name={subOpt.icon} size={14} class="item-icon" />
                {/if}
                <span class="item-label"><OverflowText text={subOpt.label} hoverTarget="button" showTooltip={showTooltips} /></span>
              </div>
              {#if isSubSelected}
                <div class="check-indicator">
                  <FluentIcon name="Checkmark" size={13} />
                </div>
              {/if}
            </button>
          {/each}
        </div>
      </div>
    </div>
  </div>
{/if}

<style>
  .fluent-dropdown {
    position: relative;
    display: inline-block;
    user-select: none;
    box-sizing: border-box;
  }

  /* Ghost Variant - Spotify text/icon button aesthetic */
  .fluent-dropdown.variant-ghost {
    max-width: 100%;
    align-self: flex-start;
    display: inline-flex;
    width: auto;
  }

  .fluent-dropdown.variant-ghost .dropdown-trigger {
    max-width: 100%;
    background: transparent;
    border: none;
    padding: 1px 0;
    height: auto;
    min-height: auto;
    width: auto;
    gap: 4px;
    color: var(--text-secondary);
    font-size: 11.5px;
    border-radius: 0;
    justify-content: flex-start;
    align-items: center;
    box-shadow: none;
    outline: none;
    opacity: 0.75;
    line-height: 1.25;
    transition: opacity var(--transition-fast, 0.15s ease), color var(--transition-fast, 0.15s ease);
  }

  .fluent-dropdown.variant-ghost .dropdown-trigger:hover:not(:disabled) {
    background: transparent;
    color: var(--text-primary);
    opacity: 1;
    box-shadow: none;
    border: none;
  }

  .fluent-dropdown.variant-ghost .dropdown-trigger:focus-visible,
  .fluent-dropdown.variant-ghost .dropdown-trigger.is-open {
    background: transparent;
    border: none;
    box-shadow: none;
    color: var(--text-primary);
    opacity: 1;
  }

  .fluent-dropdown.variant-ghost .dropdown-trigger.has-changed {
    border: none;
    box-shadow: none;
    animation: none;
  }

  .fluent-dropdown.variant-ghost .trigger-content {
    flex: 0 1 auto;
    overflow: hidden;
    gap: 4px;
    display: inline-flex;
    align-items: center;
    line-height: 1.25;
  }

  .fluent-dropdown.variant-ghost .trigger-label {
    overflow: hidden;
    white-space: nowrap;
    line-height: 1.25;
    display: inline-block;
    transition: color var(--transition-fast, 0.15s ease);
  }

  .fluent-dropdown.variant-ghost .chevron-icon {
    display: inline-flex;
    align-items: center;
    justify-content: center;
    align-self: center;
    opacity: 0.7;
    margin-left: 1px;
    margin-top: 1px;
    line-height: 1;
    transform-origin: center center;
    transition: transform var(--transition-normal), opacity var(--transition-fast, 0.15s ease), color var(--transition-fast, 0.15s ease);
  }

  .fluent-dropdown.variant-ghost .dropdown-trigger:hover:not(:disabled) .chevron-icon,
  .fluent-dropdown.variant-ghost .dropdown-trigger.is-open .chevron-icon {
    opacity: 1;
    color: var(--text-primary);
  }

  .fluent-dropdown.variant-ghost .dropdown-menu {
    min-width: 140px;
    width: max-content;
    border: none;
  }

  .dropdown-trigger {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 8px;
    width: 100%;
    height: 32px;
    padding: 0 10px;
    border-radius: 6px;
    background: var(--control-fill);
    border: 1px solid transparent;
    color: var(--text-primary);
    font-size: 12.5px;
    font-family: inherit;
    font-weight: normal;
    cursor: pointer;
    transition: background var(--transition-fast), border-color var(--transition-fast), box-shadow var(--transition-fast);
    outline: none;
    box-sizing: border-box;
    text-align: left;
  }

  .dropdown-trigger:hover:not(:disabled) {
    background: var(--control-fill-hover);
    border-color: transparent;
  }

  .dropdown-trigger:focus-visible,
  .dropdown-trigger.is-open {
    border-color: var(--accent);
    box-shadow: 0 0 0 2px var(--accent-glow, rgba(29, 185, 84, 0.35)), 0 0 8px var(--accent-glow, rgba(29, 185, 84, 0.2));
    outline: none;
  }

  .dropdown-trigger.has-changed {
    border-color: var(--accent);
    box-shadow: 0 0 0 2px var(--accent-glow, rgba(29, 185, 84, 0.45)), 0 0 14px var(--accent-glow, rgba(29, 185, 84, 0.35));
    animation: changeGlowPulse 0.7s ease-out;
  }

  @keyframes changeGlowPulse {
    0% {
      box-shadow: 0 0 0 2.5px var(--accent, #1DB954), 0 0 16px var(--accent-glow, rgba(29, 185, 84, 0.5));
    }
    100% {
      box-shadow: 0 0 0 2px var(--accent-glow, rgba(29, 185, 84, 0.35)), 0 0 8px var(--accent-glow, rgba(29, 185, 84, 0.2));
    }
  }

  .dropdown-trigger.is-disabled {
    opacity: 0.5;
    cursor: not-allowed;
  }

  .trigger-content {
    display: flex;
    align-items: center;
    gap: 8px;
    min-width: 0;
    flex: 1;
    overflow: hidden;
  }

  .trigger-label {
    min-width: 0;
    overflow: hidden;
    white-space: nowrap;
    color: var(--text-primary);
    font-size: 12.5px;
  }

  .trigger-placeholder {
    color: var(--text-secondary);
    opacity: 0.7;
    font-size: 12.5px;
  }

  .chevron-icon {
    display: inline-flex;
    align-items: center;
    justify-content: center;
    color: var(--text-secondary);
    transform-origin: center center;
    transition: transform var(--transition-normal);
    flex-shrink: 0;
    line-height: 1;
    will-change: transform;
  }

  .chevron-icon.rotated {
    transform: rotate(180deg);
  }

  /* Popup Menu matching WPF SelectorMenuPopup */
  .dropdown-menu {
    position: absolute;
    top: calc(100% + 4px);
    left: 0;
    min-width: 100%;
    width: max-content;
    max-width: 380px;
    background: var(--bg-dialog, var(--card-bg, #27272A));
    border: none;
    border-radius: var(--radius-md, 8px);
    box-shadow: var(--popup-shadow, 0 6px 18px rgba(0, 0, 0, 0.25));
    z-index: 1000;
    overflow: hidden;
    transform-origin: top center;
    box-sizing: border-box;
  }

  .dropdown-menu.align-right {
    left: auto;
    right: 0;
  }

  .dropdown-menu.open-upward {
    top: auto;
    bottom: calc(100% + 4px);
    transform-origin: bottom center;
  }

  /* When anchored to corners (kept as a feature) */
  .dropdown-menu.anchor-corner {
    transform-origin: top left;
  }

  .dropdown-menu.anchor-corner.align-right {
    transform-origin: top right;
  }

  .dropdown-menu.anchor-corner.open-upward {
    transform-origin: bottom left;
  }

  .dropdown-menu.anchor-corner.open-upward.align-right {
    transform-origin: bottom right;
  }

  :global(.menu-scroll-viewer) {
    max-height: 240px;
    width: 100%;
  }

  .menu-scroll {
    max-height: 240px;
    overflow-y: auto;
    padding: 4px;
    display: flex;
    flex-direction: column;
    gap: 2px;
  }

  .dropdown-item {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 12px;
    width: 100%;
    padding: 6px 10px;
    border-radius: 5px;
    background: transparent;
    border: none;
    color: var(--text-primary);
    font-size: 12.5px;
    font-family: inherit;
    cursor: pointer;
    text-align: left;
    transition: background var(--transition-fast), color var(--transition-fast);
    outline: none;
    box-sizing: border-box;
  }

  .dropdown-item:hover:not(:disabled),
  .dropdown-item.is-highlighted:not(:disabled) {
    background: var(--control-fill-hover);
    color: var(--text-primary);
  }

  .dropdown-item.is-selected {
    background: var(--accent-subtle);
    color: var(--accent);
    font-weight: 600;
  }

  .dropdown-item.is-selected:hover,
  .dropdown-item.is-selected.is-highlighted {
    background: var(--accent-subtle);
    filter: brightness(1.05);
  }

  .dropdown-item.is-disabled {
    opacity: 0.45;
    cursor: not-allowed;
  }

  .item-main {
    display: flex;
    align-items: center;
    gap: 8px;
    min-width: 0;
    flex: 1;
  }

  .item-text-stack {
    display: flex;
    flex-direction: column;
    gap: 1px;
    min-width: 0;
  }

  .item-label {
    min-width: 0;
    overflow: hidden;
    white-space: nowrap;
    font-size: 12.5px;
  }

  .item-desc {
    font-size: 11px;
    color: var(--text-secondary);
    opacity: 0.75;
    white-space: normal;
  }

  .check-indicator {
    display: flex;
    align-items: center;
    justify-content: center;
    color: var(--accent);
    flex-shrink: 0;
  }

  /* Submenu Styles */
  .dropdown-submenu-wrap {
    position: relative;
    width: 100%;
  }

  .submenu-trigger {
    display: flex;
    align-items: center;
    justify-content: space-between;
    width: 100%;
  }

  .submenu-trigger.is-active {
    background: var(--control-fill-hover, rgba(255, 255, 255, 0.08));
    color: var(--text-primary);
  }

  .submenu-chevron-wrap {
    display: flex;
    align-items: center;
    justify-content: center;
    color: var(--text-tertiary);
    opacity: 0.8;
  }

  :global(.folder-icon) {
    color: var(--accent);
    opacity: 0.85;
  }

  .dropdown-submenu-flyout {
    position: fixed;
    width: max-content;
    min-width: min(175px, calc(100vw - 16px));
    max-width: calc(100vw - 16px);
    z-index: 1050;
  }

  .dropdown-submenu-panel {
    position: relative;
    border-radius: var(--radius-md, 8px);
    transform-origin: var(--flyout-origin, left top);
  }

  .dropdown-submenu-surface {
    position: absolute;
    inset: 0;
    background: var(--bg-dialog, var(--card-bg, #27272A));
    border: none;
    border-radius: inherit;
    box-shadow: var(--popup-shadow, 0 6px 18px rgba(0, 0, 0, 0.25));
    transform-origin: top left;
    pointer-events: none;
  }

  .dropdown-submenu-content {
    position: relative;
    clip-path: inset(0);
  }

  .dropdown-submenu-list {
    max-height: min(260px, calc(100vh - 16px));
    overflow-y: auto;
    padding: 4px;
    display: flex;
    flex-direction: column;
    gap: 2px;
    box-sizing: border-box;
  }

  .dropdown-submenu-list > .dropdown-item {
    flex-shrink: 0;
  }
</style>
