<script lang="ts">
  import { popupScaleIn } from '../../utils/transitions';
  import { createDropdownState } from '../../ui/dropdownState.svelte';
  import FluentIcon from './FluentIcon.svelte';
  import CustomScrollViewer from './CustomScrollViewer.svelte';

  export interface KeyPickerOption {
    offset: number;
    note: string;
    offsetDisplay: string;
  }

  interface Props {
    value?: number;
    min?: number;
    max?: number;
    variant?: 'dropdown' | 'pill' | 'ghost';
    placement?: 'top' | 'bottom' | 'auto';
    align?: 'left' | 'right' | 'auto';
    scaleAnchor?: 'center' | 'corner';
    scaleOrigin?: 'center' | 'corner';
    width?: string;
    disabled?: boolean;
    title?: string;
    class?: string;
    onchange?: (val: number) => void;
  }

  let {
    value = $bindable(0),
    min = -12,
    max = 12,
    variant = 'dropdown',
    placement = 'auto',
    align = 'auto',
    scaleAnchor = 'center',
    scaleOrigin,
    width = '96px',
    disabled = false,
    title = '',
    class: customClass = '',
    onchange
  }: Props = $props();

  let effectiveScaleAnchor = $derived(scaleAnchor ?? scaleOrigin ?? 'center');
  let openUpward = $state(true);
  let alignRight = $state(false);

  const dropdown = createDropdownState();
  let isOpen = $derived(dropdown.isOpen);
  let containerRef = $state<HTMLDivElement | null>(null);
  let scrollContainerRef = $state<HTMLDivElement>();
  let itemElements = $state<Record<number, HTMLButtonElement>>({});

  const NOTE_NAMES = ['C', 'C♯', 'D', 'D♯', 'E', 'F', 'F♯', 'G', 'G♯', 'A', 'A♯', 'B'] as const;

  export function getNoteWithOctave(offset: number): string {
    const midiNote = 48 + offset; // C3 is MIDI 48
    const noteIndex = ((midiNote % 12) + 12) % 12;
    const octave = Math.floor(midiNote / 12) - 1;
    return `${NOTE_NAMES[noteIndex]}${octave}`;
  }

  // Generate options from min to max, ensuring current value is included
  let options = $derived.by<KeyPickerOption[]>(() => {
    const effectiveMin = Math.min(min, value);
    const effectiveMax = Math.max(max, value);
    const list: KeyPickerOption[] = [];

    for (let offset = effectiveMin; offset <= effectiveMax; offset++) {
      const offsetDisplay = offset > 0 ? `+${offset}` : `${offset}`;
      const note = getNoteWithOctave(offset);
      list.push({ offset, note, offsetDisplay });
    }
    return list;
  });

  let currentOption = $derived.by<KeyPickerOption>(() => {
    const found = options.find(o => o.offset === value);
    if (found) return found;
    const offsetDisplay = value > 0 ? `+${value}` : `${value}`;
    return { offset: value, note: getNoteWithOctave(value), offsetDisplay };
  });

  function toggle(e: MouseEvent) {
    if (disabled) return;
    e.stopPropagation();

    if (!isOpen && containerRef) {
      const rect = containerRef.getBoundingClientRect();
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
        alignRight = spaceRight < 120 && rect.right > 120;
      }
    }

    dropdown.isOpen = !isOpen;
  }

  function close() {
    dropdown.isOpen = false;
  }

  function selectOption(offset: number) {
    value = offset;
    onchange?.(offset);
    close();
  }

  // Auto-scroll to selected element when popup opens
  $effect(() => {
    if (isOpen && scrollContainerRef) {
      const targetEl = itemElements[value];
      if (targetEl) {
        requestAnimationFrame(() => {
          if (!scrollContainerRef || !targetEl) return;
          const containerHeight = scrollContainerRef.clientHeight;
          const targetTop = targetEl.offsetTop - (containerHeight / 2) + (targetEl.clientHeight / 2);
          scrollContainerRef.scrollTop = Math.max(0, targetTop);
        });
      }
    }
  });

  function handleWindowClick(e: MouseEvent) {
    if (!isOpen) return;
    const target = e.target as HTMLElement;
    if (containerRef && !containerRef.contains(target)) {
      close();
    }
  }

  function handleKeydown(e: KeyboardEvent) {
    if (!isOpen || !containerRef?.contains(e.target as Node)) return;

    if (e.key === 'Escape') {
      e.preventDefault();
      close();
    } else if (e.key === 'ArrowUp') {
      e.preventDefault();
      const currIdx = options.findIndex(o => o.offset === value);
      if (currIdx > 0) {
        value = options[currIdx - 1].offset;
        onchange?.(value);
      }
    } else if (e.key === 'ArrowDown') {
      e.preventDefault();
      const currIdx = options.findIndex(o => o.offset === value);
      if (currIdx < options.length - 1) {
        value = options[currIdx + 1].offset;
        onchange?.(value);
      }
    } else if (e.key === 'Enter') {
      e.preventDefault();
      close();
    }
  }
</script>

<svelte:window onclick={handleWindowClick} onkeydown={handleKeydown} />

<div
  class="key-picker {customClass}"
  bind:this={containerRef}
  style:width={variant !== 'pill' ? width : 'auto'}
>
  {#if variant !== 'pill'}
    <button
      type="button"
      class="dropdown-trigger"
      class:ghost={variant === 'ghost'}
      class:is-open={isOpen}
      {disabled}
      {title}
      onclick={toggle}
    >
      <div class="trigger-label">
        <span class="offset-val">{currentOption.offsetDisplay}</span>
        <span class="note-val">{currentOption.note}</span>
      </div>
      <span class="chevron-icon" class:rotated={isOpen}>
        <FluentIcon name="ChevronDown" size={12} />
      </span>
    </button>
  {:else}
    <button
      type="button"
      class="pill-trigger"
      class:is-open={isOpen}
      class:active={value !== 0}
      {disabled}
      {title}
      onclick={toggle}
    >
      <span class="btn-text">{currentOption.note}</span>
      <FluentIcon name="MusicNote1" size={14} class="btn-icon" />
    </button>
  {/if}

  {#if isOpen}
    <div
      class="dropdown-popup key-popup"
      use:dropdown.layer
      class:open-upward={openUpward}
      class:open-downward={!openUpward}
      class:align-right={alignRight}
      class:anchor-corner={effectiveScaleAnchor === 'corner'}
      in:popupScaleIn
      out:dropdown.outro
    >
      <CustomScrollViewer
        class="key-popup-scroll-viewer"
        contentClass="popup-scroll"
        bind:scrollContainer={scrollContainerRef}
        padding="4px"
        gap="1px"
        fadeHeight={20}
        fadeRampDistance={18}
      >
        {#each options as item (item.offset)}
          {@const isSelected = item.offset === value}
          <button
            type="button"
            class="menu-item key-item"
            class:selected={isSelected}
            bind:this={itemElements[item.offset]}
            onclick={() => selectOption(item.offset)}
          >
            <span class="offset-val">{item.offsetDisplay}</span>
            <span class="note-val">{item.note}</span>
          </button>
        {/each}
      </CustomScrollViewer>
    </div>
  {/if}
</div>

<style>
  .key-picker {
    position: relative;
    display: inline-flex;
    align-items: center;
    user-select: none;
    box-sizing: border-box;
  }

  /* Fluent standard dropdown trigger style matching WPF StandardDropdown */
  .dropdown-trigger {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 6px;
    width: 100%;
    height: 30px;
    padding: 0 8px;
    border-radius: var(--radius-sm, 4px);
    background: var(--control-fill);
    border: 1px solid var(--control-stroke);
    border-bottom: 1px solid var(--border-medium);
    color: var(--text-primary);
    font-size: 12px;
    font-family: inherit;
    cursor: pointer;
    outline: none;
    transition: background var(--transition-fast), border-color var(--transition-fast), box-shadow var(--transition-fast);
    box-sizing: border-box;
    text-align: left;
  }

  .dropdown-trigger.ghost {
    background: transparent;
    border-color: transparent;
  }

  .dropdown-trigger.ghost:hover:not(:disabled),
  .dropdown-trigger.ghost.is-open {
    background: var(--bg-hover);
    border-color: transparent;
  }

  .dropdown-trigger:hover:not(:disabled) {
    background: var(--control-fill-hover);
    border-color: var(--control-stroke-hover, var(--border-medium));
  }

  .dropdown-trigger.is-open {
    border-color: var(--accent);
    outline: none;
  }

  .dropdown-trigger:disabled {
    opacity: 0.5;
    cursor: not-allowed;
  }

  .trigger-label {
    display: flex;
    align-items: center;
    gap: 6px;
    min-width: 0;
  }

  .chevron-icon {
    display: flex;
    align-items: center;
    justify-content: center;
    color: var(--text-secondary);
    transition: transform var(--transition-fast);
    flex-shrink: 0;
  }

  .chevron-icon.rotated {
    transform: rotate(180deg);
  }

  /* Pill button variant matching AudioSettingsControl */
  .pill-trigger {
    display: flex;
    align-items: center;
    gap: 5px;
    padding: 4px 8px;
    border-radius: 4px;
    background: transparent;
    border: 1px solid transparent;
    color: var(--text-primary);
    cursor: pointer;
    font-size: 12px;
    font-weight: 500;
    transition: background var(--transition-fast), border-color var(--transition-fast), color var(--transition-fast);
    outline: none;
  }

  .pill-trigger:hover {
    background: var(--bg-hover);
    border-color: rgba(255, 255, 255, 0.08);
  }

  .pill-trigger.is-open {
    background: var(--bg-tertiary);
    border-color: rgba(255, 255, 255, 0.12);
  }

  .pill-trigger.active {
    color: var(--accent);
  }

  .btn-text {
    line-height: 1;
    font-size: 12px;
  }

  /* Dropdown popup menu styled to match WPF WPF-UI / AudioSettingsControl Popups */
  .dropdown-popup {
    position: absolute;
    bottom: calc(100% + 6px);
    left: 0;
    background: var(--bg-dialog, var(--card-bg, #27272A));
    border: none;
    border-radius: var(--radius-md, 8px);
    box-shadow: var(--popup-shadow);
    z-index: 120;
    min-width: 88px;
    overflow: hidden;
    transform-origin: bottom center;
    box-sizing: border-box;
  }

  .dropdown-popup.open-downward {
    bottom: auto;
    top: calc(100% + 6px);
    transform-origin: top center;
  }

  .dropdown-popup.align-right {
    left: auto;
    right: 0;
  }

  /* When anchored to corners (kept as a feature) */
  .dropdown-popup.anchor-corner {
    transform-origin: bottom left;
  }

  .dropdown-popup.anchor-corner.open-downward {
    transform-origin: top left;
  }

  .dropdown-popup.anchor-corner.align-right {
    transform-origin: bottom right;
  }

  .dropdown-popup.anchor-corner.open-downward.align-right {
    transform-origin: top right;
  }

  :global(.key-popup-scroll-viewer) {
    max-height: 220px;
    width: 100%;
  }

  .popup-scroll {
    max-height: 220px;
    overflow-y: auto;
    padding: 4px;
    display: flex;
    flex-direction: column;
    gap: 1px;
  }

  .menu-item {
    display: flex;
    align-items: center;
    justify-content: space-between;
    width: 100%;
    padding: 6px 10px;
    border-radius: 4px;
    background: transparent;
    border: none;
    color: var(--text-primary);
    font-size: 12px;
    font-family: inherit;
    cursor: pointer;
    text-align: left;
    transition: background var(--transition-fast);
    box-sizing: border-box;
  }

  .menu-item:hover {
    background: var(--control-fill-hover);
  }

  .menu-item.selected {
    font-weight: 600;
    color: var(--accent);
    background: var(--accent-subtle, rgba(29, 185, 84, 0.12));
  }

  .key-item {
    display: grid;
    grid-template-columns: 24px 1fr;
    gap: 6px;
    padding: 6px 8px;
    align-items: center;
  }

  .offset-val {
    text-align: right;
    opacity: 0.7;
    font-variant-numeric: tabular-nums;
  }

  .note-val {
    text-align: left;
  }
</style>
