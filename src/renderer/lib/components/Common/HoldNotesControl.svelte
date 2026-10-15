<script lang="ts">
  import ToggleSwitch from './ToggleSwitch.svelte';
  import { tooltip } from '../../actions/tooltip';
  import { t } from '../../i18n';

  let {
    checked, thresholdMs, onchange, onthresholdchange,
  }: {
    checked: boolean;
    thresholdMs: number;
    onchange: (checked: boolean) => void;
    onthresholdchange: (value: number) => void;
  } = $props();

  const inputId = $props.id();
</script>

<div class="hold-notes-control">
  <ToggleSwitch label={t('settings.holdNotes')} {checked} {onchange} />
  <div class="threshold-field" class:disabled={!checked} use:tooltip={t('settings.tapThresholdTooltip')}>
    <label for={inputId}>{t('settings.tapThreshold')}</label>
    <div class="threshold-input">
      <input
        id={inputId}
        type="number"
        min="0"
        max="10000"
        step="1"
        value={thresholdMs}
        disabled={!checked}
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
  .hold-notes-control { display: flex; flex-wrap: wrap; align-items: center; gap: 12px 18px; min-width: 0; }
  .threshold-field { display: flex; flex-direction: column; align-items: flex-start; gap: 4px; min-width: 0; transition: opacity var(--anim-fast, 150ms) ease; }
  .threshold-field.disabled { opacity: 0.45; }
  label { color: var(--text-secondary); font-size: 11.5px; }
  .threshold-input { display: flex; align-items: center; gap: 5px; }
  input { box-sizing: border-box; width: 64px; min-width: 0; padding: 5px 6px; border: none; border-radius: 4px; background: var(--bg-tertiary); color: var(--text-primary); font-family: inherit; font-size: 12px; font-variant-numeric: tabular-nums; text-align: center; }
  input:hover:enabled { background: var(--bg-hover); }
  input:focus-visible { outline: 2px solid var(--accent); outline-offset: 2px; }
  input:disabled { cursor: not-allowed; }
  .threshold-input > span { color: var(--text-tertiary); font-size: 11px; }
  .sr-only { position: absolute; width: 1px; height: 1px; padding: 0; margin: -1px; overflow: hidden; clip-path: inset(50%); white-space: nowrap; }
</style>
