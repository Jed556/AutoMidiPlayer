<script module lang="ts">
  let cancelActiveEdit: (() => void) | undefined;
</script>

<script lang="ts" generics="T extends KeyBinding">
  import { onDestroy } from 'svelte';
  import FluentIcon from '../Icons/FluentIcon.svelte';
  import type { KeyBinding } from '$shared/types/settings';
  import { pedalKeyFromEvent } from '$shared/utils/pedalBindings';
  import { hotkeyBindingFromEvent } from '$shared/utils/hotkeyBindings';
  import { settingsStore } from '../../state/settingsStore.svelte';
  import { t } from '../../i18n';

  interface Props {
    label: string;
    binding: T;
    displayTrigger?: string;
    onchange: (updated: T) => void;
    onclear: () => void;
    onmeasurechips?: (width: number) => void;
    singleKey?: boolean;
    disabled?: boolean;
  }

  let { label, binding, displayTrigger, onchange, onclear, onmeasurechips, singleKey = false, disabled = false }: Props = $props();

  let isEditing = $state(false);
  let tempCtrl = $state(false);
  let tempAlt = $state(false);
  let tempShift = $state(false);
  let tempMeta = $state(false);
  const metaLabel = /Mac|iPhone|iPad/.test(navigator.platform) ? 'Cmd' : 'Super';
  let chipsWidth = $state(0);

  $effect(() => {
    if (chipsWidth > 0 && onmeasurechips) {
      onmeasurechips(chipsWidth);
    }
  });

  function formatKeyDisplay(k: string): string {
    if (!k) return '';
    const map: Record<string, string> = {
      'Space': 'Space',
      'ArrowUp': 'Up',
      'ArrowDown': 'Down',
      'ArrowLeft': 'Left',
      'ArrowRight': 'Right',
      'Escape': 'Esc',
      'Enter': 'Enter',
      'Tab': 'Tab',
      'Backspace': 'Backspace',
      'Delete': 'Del',
      'Insert': 'Ins',
      'Home': 'Home',
      'End': 'End',
      'PageUp': 'PgUp',
      'PageDown': 'PgDn',
      'Control': 'Ctrl',
      'Shift': 'Shift',
      'Alt': 'Alt',
      'Meta': 'Win'
    };
    if (k === 'Meta') return metaLabel;
    if (/^Key[A-Z]$/.test(k)) return k.slice(3);
    if (/^Digit[0-9]$/.test(k)) return k.slice(5);
    const punctuation: Record<string, string> = { Minus: '-', Equal: '=', BracketLeft: '[',
      BracketRight: ']', Backslash: '\\', Semicolon: ';', Quote: "'", Comma: ',', Period: '.', Slash: '/', Backquote: '`' };
    return punctuation[k] || map[k] || k.toUpperCase();
  }

  async function startEditing() {
    if (disabled) return;
    cancelActiveEdit?.();
    cancelActiveEdit = stopEditing;
    isEditing = true;
    tempCtrl = false;
    tempAlt = false;
    tempShift = false;
    tempMeta = false;
    if (settingsStore.isWindows) settingsStore.suspendHotkeys();
    else await settingsStore.suspendHotkeys();
    if (!isEditing) return;
    window.addEventListener('keydown', handleKeyDown, true);
    window.addEventListener('keyup', handleKeyUp, true);
  }

  function stopEditing() {
    if (!isEditing) return;
    isEditing = false;
    if (cancelActiveEdit === stopEditing) cancelActiveEdit = undefined;
    settingsStore.resumeHotkeys();
    window.removeEventListener('keydown', handleKeyDown, true);
    window.removeEventListener('keyup', handleKeyUp, true);
  }

  function handleKeyDown(e: KeyboardEvent) {
    if (!isEditing) return;
    e.preventDefault();
    e.stopImmediatePropagation();

    if (e.key === 'Escape') {
      stopEditing();
      return;
    }

    if (singleKey) {
      const key = pedalKeyFromEvent(e);
      if (key) {
        stopEditing();
        onchange({ ...binding, key, ctrl: false, alt: false, shift: false, meta: false });
      }
      return;
    }

    tempCtrl = e.ctrlKey;
    tempAlt = e.altKey;
    tempShift = e.shiftKey;
    tempMeta = e.metaKey;

    if (settingsStore.isWindows) {
      if (['Control', 'Alt', 'Shift', 'Meta'].includes(e.key)) return;
      let key = e.key;
      if (e.code.startsWith('Key')) key = e.code.replace('Key', '');
      else if (e.code.startsWith('Digit')) key = e.code.replace('Digit', '');
      else if (e.code.startsWith('Numpad')) key = e.code;
      else if (e.code === 'Space') key = 'Space';
      else if (e.code.startsWith('Arrow')) key = e.code.replace('Arrow', '');
      onchange({ ...binding, key, ctrl: tempCtrl, alt: tempAlt, shift: tempShift });
      stopEditing();
      return;
    }
    const shortcut = hotkeyBindingFromEvent(e);
    if (!shortcut) return;
    stopEditing();
    onchange({ ...binding, ...shortcut });
  }

  function handleKeyUp(e: KeyboardEvent) {
    if (!isEditing) return;
    tempCtrl = e.ctrlKey;
    tempAlt = e.altKey;
    tempShift = e.shiftKey;
    tempMeta = e.metaKey;
  }

  $effect(() => { if (disabled) stopEditing(); });
  onDestroy(stopEditing);

  let hasBinding = $derived(Boolean(binding.key));
</script>

<div class="key-edit-row" class:is-editing={isEditing}>
  <div class="key-label" title={label}>{label}</div>

  <div class="key-chips-wrapper">
    {#if isEditing}
      <div class="recording-chips">
        {#if tempMeta}
          <span class="key-chip modifier">{metaLabel}</span>
          <span class="chip-separator">+</span>
        {/if}
        {#if tempCtrl}
          <span class="key-chip modifier">Ctrl</span>
          <span class="chip-separator">+</span>
        {/if}
        {#if tempAlt}
          <span class="key-chip modifier">Alt</span>
          <span class="chip-separator">+</span>
        {/if}
        {#if tempShift}
          <span class="key-chip modifier">Shift</span>
          <span class="chip-separator">+</span>
        {/if}
        <span class="recording-prompt">{t('settings.pressKeysPrompt')}</span>
      </div>
    {:else if displayTrigger}
      <div class="chips-list" bind:clientWidth={chipsWidth}>
        <span class="key-chip primary-key">{displayTrigger}</span>
      </div>
    {:else if hasBinding}
      <div class="chips-list" bind:clientWidth={chipsWidth}>
        {#if binding.meta}
          <span class="key-chip modifier">{metaLabel}</span>
          <span class="chip-separator">+</span>
        {/if}
        {#if binding.ctrl}
          <span class="key-chip modifier">Ctrl</span>
          <span class="chip-separator">+</span>
        {/if}
        {#if binding.alt}
          <span class="key-chip modifier">Alt</span>
          <span class="chip-separator">+</span>
        {/if}
        {#if binding.shift}
          <span class="key-chip modifier">Shift</span>
          <span class="chip-separator">+</span>
        {/if}
        <span class="key-chip primary-key">{formatKeyDisplay(binding.key)}</span>
      </div>
    {:else}
      <span class="unbound-chip">{t('settings.noHotkeyBound')}</span>
    {/if}
  </div>

  <div class="key-actions">
    {#if isEditing}
      <button
        type="button"
        class="icon-action-btn cancel-btn theme-ghost-button"
        onclick={stopEditing}
        aria-label={`${t('settings.cancelEditHotkey')}: ${label}`}
        title={t('settings.cancelEditHotkey')}
      >
        <FluentIcon name="Dismiss" size={14} />
      </button>
    {:else}
      <button
        type="button"
        class="icon-action-btn edit-btn theme-ghost-button"
        onclick={startEditing}
        {disabled}
        aria-label={`${t('settings.editHotkey')}: ${label}`}
        title={t('settings.editHotkey')}
      >
        <FluentIcon name="Edit" size={14} />
      </button>

      {#if hasBinding}
        <button
          type="button"
          class="icon-action-btn delete-btn theme-ghost-button color-danger"
          onclick={onclear}
          {disabled}
          aria-label={`${t('settings.clearHotkey')}: ${label}`}
          title={t('settings.clearHotkey')}
        >
          <FluentIcon name="Delete" size={14} />
        </button>
      {/if}
    {/if}
  </div>
</div>

<style>
  .key-edit-row {
    display: grid;
    grid-template-columns: minmax(0, var(--hotkey-label-width, 130px)) minmax(max-content, 1fr) auto;
    min-width: 0;
    align-items: center;
    justify-content: space-between;
    padding: 7px 10px;
    border-radius: 6px;
    background: var(--control-fill, rgba(255, 255, 255, 0.03));
    border: 1px solid transparent;
    gap: 12px;
    transition: background var(--transition-fast, 0.16s), border-color var(--transition-fast, 0.16s), box-shadow var(--transition-fast, 0.16s);
  }

  .key-edit-row:hover {
    background: var(--control-fill-hover, rgba(255, 255, 255, 0.06));
    border-color: transparent;
  }

  .key-edit-row.is-editing {
    border-color: var(--accent);
    box-shadow: 0 0 0 2px var(--accent-glow, rgba(29, 185, 84, 0.35)), 0 0 8px var(--accent-glow, rgba(29, 185, 84, 0.2));
    background: var(--control-fill-active, rgba(255, 255, 255, 0.08));
  }

  .key-label {
    font-size: 13px;
    font-weight: 500;
    color: var(--text-primary);
    min-width: 0;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }

  .key-chips-wrapper {
    display: flex;
    align-items: center;
    justify-content: flex-start;
    min-width: 0;
    min-height: 26px;
  }

  .chips-list,
  .recording-chips {
    display: flex;
    align-items: center;
    gap: 4px;
    flex-wrap: nowrap;
    white-space: nowrap;
  }

  .key-chip {
    flex-shrink: 0;
    display: inline-flex;
    align-items: center;
    justify-content: center;
    padding: 2px 8px;
    font-size: 11.5px;
    font-weight: 600;
    border-radius: 4px;
    background: var(--bg-tertiary, rgba(255, 255, 255, 0.08));
    border: 1px solid var(--border-medium, rgba(255, 255, 255, 0.15));
    color: var(--text-primary);
    box-shadow: 0 1px 2px rgba(0, 0, 0, 0.12);
  }

  .key-chip.primary-key {
    background: var(--card-bg, rgba(255, 255, 255, 0.12));
    border-color: var(--accent, #1DB954);
    color: var(--accent-color, var(--text-primary));
  }

  .chip-separator {
    font-size: 11px;
    color: var(--text-tertiary);
    opacity: 0.6;
    user-select: none;
  }

  .recording-prompt {
    font-size: 12px;
    font-style: italic;
    color: var(--accent);
    animation: pulse 1.2s infinite ease-in-out;
  }

  @keyframes pulse {
    0%, 100% { opacity: 0.6; }
    50% { opacity: 1; }
  }

  .unbound-chip {
    font-size: 12px;
    color: var(--text-tertiary);
    font-style: italic;
  }

  .key-actions {
    display: flex;
    align-items: center;
    gap: 4px;
    flex-shrink: 0;
  }

  .icon-action-btn {
    width: 28px;
    height: 28px;
    border-radius: 4px;
    display: inline-flex;
    align-items: center;
    justify-content: center;
  }

</style>
