<script lang="ts">
  import { tooltip } from '../../actions/tooltip';

  interface Props {
    checked?: boolean;
    disabled?: boolean;
    label?: string;
    description?: string;
    title?: string;
    /** Position of the switch relative to its label. */
    position?: 'left' | 'right';
    onchange?: (checked: boolean) => void;
  }

  let {
    checked = $bindable(false),
    disabled = false,
    label = '',
    description = '',
    title = '',
    position = 'left',
    onchange
  }: Props = $props();

  let hasInteracted = $state(false);

  function toggle() {
    if (disabled) return;
    hasInteracted = true;
    checked = !checked;
    onchange?.(checked);
  }
</script>

<div class="toggle-container {disabled ? 'is-disabled' : ''}" class:switch-right={position === 'right'} use:tooltip={title || undefined}>
  <button
    type="button"
    class="switch-pill {checked ? 'is-checked' : ''}"
    class:interacted={hasInteracted}
    disabled={disabled}
    onclick={toggle}
    role="switch"
    aria-checked={checked}
    aria-label={label || title || 'Toggle switch'}
  >
    <div class="switch-thumb"></div>
  </button>
  {#if label || description}
    <div class="toggle-text" onclick={toggle} role="presentation">
      {#if label}
        <span class="toggle-label">{label}</span>
      {/if}
      {#if description}
        <span class="toggle-desc">{description}</span>
      {/if}
    </div>
  {/if}
</div>

<style>
  .toggle-container {
    display: inline-flex;
    align-items: center;
    gap: 10px;
    min-width: 0;
    user-select: none;
    outline: none;
  }

  .toggle-container.is-disabled {
    opacity: 0.45;
    cursor: not-allowed;
  }

  .toggle-container.switch-right {
    flex-direction: row-reverse;
    justify-content: space-between;
  }

  .switch-pill:focus-visible {
    outline: 2px solid var(--accent-color);
    outline-offset: 3px;
  }

  .switch-pill {
    width: 40px;
    height: 20px;
    border-radius: 10px;
    background: var(--switch-bg);
    border: 1px solid var(--switch-border);
    position: relative;
    cursor: pointer;
    transition: background var(--transition-fast, 0.18s cubic-bezier(0.1, 0.9, 0.2, 1)),
                border-color var(--transition-fast, 0.18s cubic-bezier(0.1, 0.9, 0.2, 1));
    flex-shrink: 0;
    padding: 0;
    outline: none;
  }

  .switch-pill:hover:not(:disabled) {
    background: var(--switch-hover-bg);
    border-color: var(--switch-hover-border);
  }

  .switch-pill.is-checked {
    background: var(--accent-color, var(--accent, #1DB954));
    border-color: var(--accent-color, var(--accent, #1DB954));
  }

  .switch-pill.is-checked:hover:not(:disabled) {
    background: var(--accent-color, var(--accent, #1DB954));
    border-color: var(--accent-color, var(--accent, #1DB954));
  }

  .switch-thumb {
    width: 14px;
    height: 14px;
    position: absolute;
    top: 2px;
    left: 2px;
    transform: translateX(0);
    transition: transform var(--transition-normal, 0.2s cubic-bezier(0.2, 0.9, 0.3, 1));
    will-change: transform;
    pointer-events: none;
  }

  .switch-pill.is-checked .switch-thumb {
    transform: translateX(20px);
  }

  /* Inner visual circle that handles the stretch/smear without interrupting translation */
  .switch-thumb::before {
    content: '';
    display: block;
    width: 100%;
    height: 100%;
    border-radius: 7px;
    background: var(--switch-thumb-off, #CECECE);
    box-shadow: 0 1px 3px rgba(0, 0, 0, 0.35);
    transform-origin: center center;
    transform: scale(1, 1);
    will-change: transform;
    transition: background-color var(--transition-fast, 0.15s ease);
  }

  .switch-pill:hover:not(.is-checked) .switch-thumb::before {
    background: var(--switch-thumb-off-hover, #FFFFFF);
  }

  .switch-pill.is-checked .switch-thumb::before {
    background: var(--switch-thumb-checked, #000000);
  }

  /* Pure scale smear keyframes — independent of translation for silky 60/120fps glide */
  @keyframes thumb-smear-on {
    0% {
      transform: scale(1, 1);
    }
    40% {
      transform: scale(1.36, 0.9);
    }
    100% {
      transform: scale(1, 1);
    }
  }

  @keyframes thumb-smear-off {
    0% {
      transform: scale(1, 1);
    }
    40% {
      transform: scale(1.36, 0.9);
    }
    100% {
      transform: scale(1, 1);
    }
  }

  /* Only run the smear stretch animation on user toggle */
  .switch-pill.interacted.is-checked .switch-thumb::before {
    animation: thumb-smear-on var(--anim-duration, 0.2s) cubic-bezier(0.2, 0.9, 0.3, 1);
  }

  .switch-pill.interacted:not(.is-checked) .switch-thumb::before {
    animation: thumb-smear-off var(--anim-duration, 0.2s) cubic-bezier(0.2, 0.9, 0.3, 1);
  }


  .toggle-text {
    display: flex;
    flex-direction: column;
    cursor: pointer;
    min-width: 0;
  }

  .toggle-label {
    font-size: 13px;
    color: var(--text-primary);
  }

  .toggle-desc {
    font-size: 11px;
    color: var(--text-secondary);
    line-height: 1.35;
  }
</style>
