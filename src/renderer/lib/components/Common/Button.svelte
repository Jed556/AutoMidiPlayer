<script lang="ts">
  import type { Snippet } from 'svelte';
  import FluentIcon from './FluentIcon.svelte';
  import { tooltip } from '../../actions/tooltip';

  interface Props {
    variant?: 'filled' | 'outlined' | 'secondary' | 'primary' | 'accent' | 'danger' | 'warning' | 'ghost' | 'ghost-dimmed' | 'picker';
    colorMode?: 'default' | 'accent' | 'warning' | 'danger';
    size?: 'sm' | 'md' | 'lg' | 'icon';
    compact?: boolean;
    active?: boolean;
    pressed?: boolean;
    disabled?: boolean;
    title?: string;
    id?: string;
    role?: 'button' | 'combobox';
    ariaLabel?: string;
    ariaHaspopup?: 'listbox' | 'menu';
    ariaExpanded?: boolean;
    ariaControls?: string;
    ariaActiveDescendant?: string;
    class?: string;
    icon?: string;
    iconSize?: number;
    iconFilled?: boolean;
    onclick?: (e: MouseEvent) => void;
    onkeydown?: (e: KeyboardEvent) => void;
    children?: Snippet;
  }

  let {
    variant = 'filled',
    colorMode = 'default',
    size = 'md',
    compact = false,
    active = false,
    pressed,
    disabled = false,
    title = '',
    id,
    role,
    ariaLabel,
    ariaHaspopup,
    ariaExpanded,
    ariaControls,
    ariaActiveDescendant,
    class: customClass = '',
    icon,
    iconSize,
    iconFilled = false,
    onclick,
    onkeydown,
    children
  }: Props = $props();

  let effectiveSize = $derived(compact ? 'sm' : size);
</script>

<button
  type="button"
  use:tooltip={title || undefined}
  {disabled}
  {id}
  {role}
  aria-label={ariaLabel ?? (icon && !children ? title || undefined : undefined)}
  aria-haspopup={ariaHaspopup}
  aria-expanded={ariaExpanded}
  aria-controls={ariaControls}
  aria-activedescendant={ariaActiveDescendant}
  aria-pressed={pressed}
  class="fluent-btn variant-{variant} color-{colorMode} size-{effectiveSize} {active ? 'is-active' : ''} {customClass}"
  {onclick}
  {onkeydown}
>
  {#if icon}
    <FluentIcon name={icon} size={iconSize ?? (effectiveSize === 'sm' ? 12 : 14)} filled={iconFilled} />
  {/if}
  {#if children}
    {@render children()}
  {/if}
</button>

<style>
  .fluent-btn {
    display: inline-flex;
    align-items: center;
    justify-content: center;
    gap: 6px;
    border-radius: var(--radius-sm, 4px);
    font-family: inherit;
    font-size: 12.5px;
    font-weight: 500;
    cursor: pointer;
    transition: background var(--transition-fast, 0.16s cubic-bezier(0, 0, 0.2, 1)),
                border-color var(--transition-fast, 0.16s cubic-bezier(0, 0, 0.2, 1)),
                color var(--transition-fast, 0.16s cubic-bezier(0, 0, 0.2, 1)),
                opacity var(--transition-normal, 0.22s cubic-bezier(0, 0, 0.2, 1)),
                filter var(--transition-fast, 0.16s cubic-bezier(0, 0, 0.2, 1)),
                transform var(--transition-fast, 0.16s cubic-bezier(0, 0, 0.2, 1));
    border: 1px solid transparent;
    outline: none;
    user-select: none;
    white-space: nowrap;
    position: relative;
    box-sizing: border-box;
  }

  .fluent-btn:focus-visible {
    outline: 2px solid var(--accent);
    outline-offset: 1px;
  }

  .fluent-btn:active:not(:disabled) {
    opacity: 0.85;
    transform: scale(0.985);
  }

  .fluent-btn:disabled {
    opacity: 0.45;
    cursor: not-allowed;
  }

  /* Borderless field button for pickers, with the same hover/press feedback. */
  .variant-picker {
    width: 100%;
    height: 34px;
    justify-content: space-between;
    padding: 0 10px;
    border: none;
    border-radius: var(--radius-sm, 4px);
    background: var(--bg-tertiary);
    color: var(--text-primary);
    text-align: left;
    transition: background var(--transition-fast), color var(--transition-fast),
                transform var(--transition-fast), opacity var(--transition-fast);
  }

  .variant-picker:hover:not(:disabled),
  .variant-picker[aria-expanded='true'] {
    background: var(--control-fill-hover);
  }

  /* Sizes */
  .size-sm {
    padding: 0 10px;
    font-size: 11.5px;
    height: 26px;
  }

  .size-md {
    padding: 0 14px;
    height: 32px;
    font-size: 12.5px;
  }

  .size-lg {
    padding: 0 18px;
    height: 38px;
    font-size: 13.5px;
  }

  .size-icon {
    width: 32px;
    height: 32px;
    padding: 0;
    min-width: 32px;
  }

  /* Standard Filled Button (Matches WPF ButtonFilled) */
  .variant-filled {
    background: var(--control-fill);
    border-color: var(--control-stroke);
    color: var(--text-primary);
  }

  .variant-filled:hover:not(:disabled) {
    background: var(--control-fill-hover);
    border-color: var(--border-medium);
  }

  .variant-filled:active:not(:disabled) {
    background: var(--control-fill-active);
  }

  .variant-filled.is-active {
    border-color: var(--accent-color);
    color: var(--accent-color);
  }

  /* Outlined / Secondary Button (Matches WPF ButtonOutlined & WPF UI Secondary Button) */
  .variant-outlined,
  .variant-secondary {
    background: var(--control-fill);
    border-color: var(--control-stroke);
    color: var(--text-primary);
  }

  .variant-outlined:hover:not(:disabled),
  .variant-secondary:hover:not(:disabled) {
    background: var(--control-fill-hover);
    border-color: var(--border-medium);
  }

  .variant-outlined:active:not(:disabled),
  .variant-secondary:active:not(:disabled) {
    background: var(--control-fill-active);
  }

  .variant-outlined.is-active,
  .variant-secondary.is-active {
    border-color: var(--accent-color);
    color: var(--accent-color);
  }

  /* Accent / Primary Button (Matches WPF ButtonFilled ColorMode=Accent & WPF UI Primary) */
  .variant-accent,
  .variant-primary {
    background: var(--accent-color);
    border-color: transparent;
    color: #FFFFFF;
    font-weight: 600;
  }

  .variant-accent:hover:not(:disabled),
  .variant-primary:hover:not(:disabled) {
    background: var(--accent-color);
    filter: brightness(1.06);
  }

  .variant-accent:active:not(:disabled),
  .variant-primary:active:not(:disabled) {
    opacity: 0.88;
  }

  .variant-accent.is-active,
  .variant-primary.is-active {
    filter: brightness(1.1);
  }

  /* Danger Button (Matches WPF ButtonDangerNormalColor #FFC42B1C & ButtonDangerHoverColor #FFE04F3F) */
  .variant-danger {
    background: #C42B1C;
    border-color: transparent;
    color: #FFFFFF;
    font-weight: 600;
  }

  .variant-danger:hover:not(:disabled) {
    background: #E04F3F;
  }

  .variant-danger:active:not(:disabled) {
    opacity: 0.88;
  }

  /* Warning Button (Matches WPF ButtonDangerWarningColor #FFB900) */
  .variant-warning {
    background: #FFB900;
    border-color: transparent;
    color: #000000;
    font-weight: 600;
  }

  .variant-warning:hover:not(:disabled) {
    background: #FFE0A500;
  }

  /* Ghost Button (Matches WPF ButtonGhost.xaml: NO background, opacity changes to 1 on hover) */
  .variant-ghost {
    background: transparent !important;
    border-color: transparent !important;
    box-shadow: none !important;
    color: var(--text-primary);
    opacity: 0.4;
    transition: opacity var(--transition-normal, 0.22s cubic-bezier(0, 0, 0.2, 1)),
                color var(--transition-fast, 0.16s ease);
  }

  .variant-ghost.color-accent {
    color: var(--accent-color);
    opacity: 0.8;
  }

  .variant-ghost.color-danger {
    color: #C42B1C;
    opacity: 0.8;
  }

  .variant-ghost.color-warning {
    color: #FFB900;
    opacity: 0.8;
  }

  .variant-ghost:hover:not(:disabled) {
    background: transparent !important;
    opacity: 1 !important;
  }

  .variant-ghost:active:not(:disabled) {
    background: transparent !important;
    opacity: 0.75 !important;
    transform: none;
  }

  .variant-ghost.is-active {
    color: var(--accent-color);
    opacity: 0.8;
  }

  .variant-ghost.is-active:hover:not(:disabled) {
    opacity: 1 !important;
  }

  /* Ghost Dimmed Button (Matches WPF ButtonGhostDimmed.xaml: opacity 0.25 -> 0.75 on hover) */
  .variant-ghost-dimmed {
    background: transparent !important;
    border-color: transparent !important;
    box-shadow: none !important;
    color: var(--text-primary);
    opacity: 0.25;
    transition: opacity var(--transition-normal, 0.22s cubic-bezier(0, 0, 0.2, 1)),
                color var(--transition-fast, 0.16s ease);
  }

  .variant-ghost-dimmed.color-accent {
    color: var(--accent-color);
    opacity: 0.8;
  }

  .variant-ghost-dimmed:hover:not(:disabled) {
    background: transparent !important;
    opacity: 0.75 !important;
  }

  .variant-ghost-dimmed:active:not(:disabled) {
    background: transparent !important;
    opacity: 0.5 !important;
    transform: none;
  }

  .variant-ghost-dimmed.is-active {
    color: var(--accent-color);
    opacity: 0.8;
  }

  .variant-ghost-dimmed.is-active:hover:not(:disabled) {
    opacity: 1 !important;
  }

  @media (prefers-reduced-motion: reduce) {
    .fluent-btn { transition: none; }
  }
</style>
