<script lang="ts">
  import FluentIcon from './FluentIcon.svelte';

  interface Props {
    value?: number;
    min?: number;
    max?: number;
    step?: number;
    unit?: string;
    label?: string;
    disabled?: boolean;
    onchange?: (val: number) => void;
  }

  let {
    value = $bindable(0),
    min = -Infinity,
    max = Infinity,
    step = 1,
    unit = '',
    label,
    disabled = false,
    onchange
  }: Props = $props();

  let hasChanged = $state(false);
  let changeTimer: ReturnType<typeof setTimeout> | null = null;

  function triggerChangeGlow() {
    hasChanged = true;
    if (changeTimer) clearTimeout(changeTimer);
    changeTimer = setTimeout(() => {
      hasChanged = false;
    }, 600);
  }

  function increment() {
    if (disabled) return;
    const next = Math.min(max, (value ?? 0) + step);
    value = Number(next.toFixed(2));
    triggerChangeGlow();
    onchange?.(value);
  }

  function decrement() {
    if (disabled) return;
    const next = Math.max(min, (value ?? 0) - step);
    value = Number(next.toFixed(2));
    triggerChangeGlow();
    onchange?.(value);
  }

  function handleInput(e: Event) {
    const target = e.target as HTMLInputElement;
    let num = parseFloat(target.value);
    if (isNaN(num)) num = min > 0 ? min : 0;
    if (num < min) num = min;
    if (num > max) num = max;
    value = num;
    triggerChangeGlow();
    onchange?.(value);
  }
</script>

<div class="number-box" class:disabled class:has-changed={hasChanged}>
  <input
    type="number"
    class="num-input"
    aria-label={label}
    {value}
    {min}
    {max}
    {step}
    {disabled}
    oninput={handleInput}
  />
  {#if unit}
    <span class="num-unit">{unit}</span>
  {/if}
  <div class="steppers">
    <button
      type="button"
      class="stepper-btn"
      onclick={increment}
      disabled={disabled || value >= max}
      aria-label="Increase"
    >
      <FluentIcon name="ChevronUp" size={12} />
    </button>
    <button
      type="button"
      class="stepper-btn"
      onclick={decrement}
      disabled={disabled || value <= min}
      aria-label="Decrease"
    >
      <FluentIcon name="ChevronDown" size={12} />
    </button>
  </div>
</div>

<style>
  .number-box {
    display: inline-flex;
    align-items: center;
    background: var(--bg-tertiary);
    border: 1px solid transparent;
    border-radius: var(--radius-sm);
    height: 32px;
    padding: 0 4px 0 8px;
    position: relative;
    transition: background var(--transition-fast), border-color var(--transition-fast), box-shadow var(--transition-fast);
  }

  .number-box:hover:not(.disabled) {
    background: var(--control-fill-hover, #3F3F3F);
    border-color: transparent;
  }

  .number-box:focus-within {
    border-color: var(--accent);
    box-shadow: 0 0 0 2px var(--accent-glow, rgba(29, 185, 84, 0.35)), 0 0 8px var(--accent-glow, rgba(29, 185, 84, 0.2));
  }

  .number-box.has-changed {
    border-color: var(--accent);
    box-shadow: 0 0 0 2px var(--accent-glow, rgba(29, 185, 84, 0.45)), 0 0 14px var(--accent-glow, rgba(29, 185, 84, 0.35));
    animation: changeGlowPulse 0.6s ease-out;
  }

  @keyframes changeGlowPulse {
    0% {
      box-shadow: 0 0 0 2.5px var(--accent, #1DB954), 0 0 16px var(--accent-glow, rgba(29, 185, 84, 0.5));
    }
    100% {
      box-shadow: 0 0 0 2px var(--accent-glow, rgba(29, 185, 84, 0.35)), 0 0 8px var(--accent-glow, rgba(29, 185, 84, 0.2));
    }
  }

  .number-box.disabled {
    opacity: 0.5;
    pointer-events: none;
  }

  .num-input {
    width: 60px;
    border: none;
    outline: none;
    background: transparent;
    color: var(--text-primary);
    font-size: 13px;
    font-family: inherit;
    -moz-appearance: textfield;
  }

  .num-input::-webkit-outer-spin-button,
  .num-input::-webkit-inner-spin-button {
    -webkit-appearance: none;
    margin: 0;
  }

  .num-unit {
    font-size: 11px;
    color: var(--text-tertiary);
    margin-right: 4px;
    user-select: none;
  }

  .steppers {
    display: flex;
    flex-direction: column;
    margin-left: auto;
  }

  .stepper-btn {
    display: flex;
    align-items: center;
    justify-content: center;
    height: 12px;
    width: 18px;
    background: transparent;
    border: none;
    color: var(--text-tertiary);
    cursor: pointer;
    border-radius: 2px;
    transition: all var(--transition-fast);
  }

  .stepper-btn:hover:not(:disabled) {
    background: var(--bg-hover);
    color: var(--text-primary);
  }

  .stepper-btn:disabled {
    opacity: 0.3;
    cursor: not-allowed;
  }
</style>
