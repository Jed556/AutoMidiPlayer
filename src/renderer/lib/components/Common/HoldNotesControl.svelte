<script lang="ts">
  import ToggleSwitch from './ToggleSwitch.svelte';
  import { tooltip } from '../../actions/tooltip';
  import { t } from '../../i18n';

  let {
    checked, thresholdMs, onchange, onthresholdchange, showLabel = true, disableThresholdWhenUnchecked = true,
  }: {
    checked: boolean;
    thresholdMs: number;
    onchange: (checked: boolean) => void;
    onthresholdchange: (value: number) => void;
    showLabel?: boolean;
    disableThresholdWhenUnchecked?: boolean;
  } = $props();

  const inputId = $props.id();
</script>

<div class="hold-notes-control">
  <ToggleSwitch label={showLabel ? t('settings.holdNotes') : ''} {checked} {onchange} />
  <div class="threshold-field" class:disabled={disableThresholdWhenUnchecked && !checked} use:tooltip={t('settings.tapThresholdTooltip')}>
    <label for={inputId}>{t('settings.tapThreshold')}</label>
    <div class="threshold-input">
      <input
        id={inputId}
        type="number"
        min="0"
        max="10000"
        step="1"
        value={thresholdMs}
        disabled={disableThresholdWhenUnchecked && !checked}
        oninput={(event) => {
          const input = event.currentTarget;
          if (input.validity.valid && Number.isInteger(input.valueAsNumber)) onthresholdchange(input.valueAsNumber);
        }}
        onblur={(event) => { event.currentTarget.value = String(thresholdMs); }}
        aria-describedby={`${inputId}-help`}
      />
      <span>ms</span>
    </div>
    <span id={`${inputId}-help`} class="sr-only">{t('settings.tapThresholdTooltip')}</span>
  </div>
</div>

<style>
  .hold-notes-control { display: flex; flex-direction: column; align-items: flex-start; gap: 8px; min-width: 0; }
  .threshold-field { display: flex; align-items: center; gap: 8px; min-width: 0; transition: opacity var(--anim-fast, 150ms) ease; }
  .threshold-field.disabled { opacity: 0.45; }
  label { color: var(--text-secondary); font-size: 10.5px; line-height: 1; }
  .threshold-input { display: flex; align-items: center; gap: 5px; }
  input { box-sizing: border-box; width: 76px; height: 28px; min-width: 0; padding: 6px 10px; border: none; border-radius: var(--radius-sm); background: var(--bg-tertiary); color: var(--text-primary); font: inherit; font-size: 13px; font-variant-numeric: tabular-nums; text-align: center; }
  input:hover:enabled { background: var(--bg-hover); }
  input:focus-visible { outline: 2px solid var(--accent); outline-offset: 2px; }
  input:disabled { cursor: not-allowed; }
  .threshold-input > span { color: var(--text-tertiary); font-size: 11px; }
  .sr-only { position: absolute; width: 1px; height: 1px; padding: 0; margin: -1px; overflow: hidden; clip-path: inset(50%); white-space: nowrap; }
</style>
