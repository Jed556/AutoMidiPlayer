<script lang="ts">
  import { onMount, onDestroy } from 'svelte';
  import FluentIcon from '../Common/FluentIcon.svelte';
  import type { HotkeyBinding } from '$shared/types/settings';
  import { settingsStore } from '../../state/settingsStore.svelte';
  import { t } from '../../i18n';

  interface Props {
    label: string;
    binding: HotkeyBinding;
    onchange: (updated: HotkeyBinding) => void;
    onclear: () => void;
  }

  let { label, binding, onchange, onclear }: Props = $props();

  let isEditing = $state(false);
  let tempKey = $state('');
  let tempCtrl = $state(false);
  let tempAlt = $state(false);
  let tempShift = $state(false);

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
      'PageDown': 'PgDn'
    };
    return map[k] || k.toUpperCase();
  }

  function startEditing() {
    isEditing = true;
    tempKey = '';
    tempCtrl = false;
    tempAlt = false;
    tempShift = false;
    settingsStore.suspendHotkeys();
    window.addEventListener('keydown', handleKeyDown, true);
    window.addEventListener('keyup', handleKeyUp, true);
  }

  function stopEditing() {
    if (!isEditing) return;
    isEditing = false;
    settingsStore.resumeHotkeys();
    window.removeEventListener('keydown', handleKeyDown, true);
    window.removeEventListener('keyup', handleKeyUp, true);
  }

  function handleKeyDown(e: KeyboardEvent) {
    if (!isEditing) return;
    e.preventDefault();
    e.stopPropagation();

    if (e.key === 'Escape') {
      stopEditing();
      return;
    }

    tempCtrl = e.ctrlKey;
    tempAlt = e.altKey;
    tempShift = e.shiftKey;

    // Ignore standalone modifier key presses
    if (['Control', 'Alt', 'Shift', 'Meta'].includes(e.key)) {
      return;
    }

    // Determine normalized key
    let keyName = e.key;
    if (e.code.startsWith('Key')) {
      keyName = e.code.replace('Key', '');
    } else if (e.code.startsWith('Digit')) {
      keyName = e.code.replace('Digit', '');
    } else if (e.code.startsWith('Numpad')) {
      keyName = e.code;
    } else if (e.code === 'Space') {
      keyName = 'Space';
    } else if (e.code.startsWith('Arrow')) {
      keyName = e.code.replace('Arrow', '');
    }

    onchange({
      ...binding,
      key: keyName,
      ctrl: tempCtrl,
      alt: tempAlt,
      shift: tempShift
    });

    stopEditing();
  }

  function handleKeyUp(e: KeyboardEvent) {
    if (!isEditing) return;
    tempCtrl = e.ctrlKey;
    tempAlt = e.altKey;
    tempShift = e.shiftKey;
  }

  onDestroy(() => {
    if (isEditing) {
      settingsStore.resumeHotkeys();
      window.removeEventListener('keydown', handleKeyDown, true);
      window.removeEventListener('keyup', handleKeyUp, true);
    }
  });

  let hasBinding = $derived(Boolean(binding.key));
</script>

<div class="key-edit-row" class:is-editing={isEditing}>
  <div class="key-label" title={label}>{label}</div>

  <div class="key-chips-wrapper">
    {#if isEditing}
      <div class="recording-chips">
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
    {:else if hasBinding}
      <div class="chips-list">
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
        class="icon-action-btn cancel-btn"
        onclick={stopEditing}
        title={t('settings.cancelEditHotkey')}
      >
        <FluentIcon name="Dismiss" size={14} />
      </button>
    {:else}
      <button
        type="button"
        class="icon-action-btn edit-btn"
        onclick={startEditing}
        title={t('settings.editHotkey')}
      >
        <FluentIcon name="Edit" size={14} />
      </button>

      {#if hasBinding}
        <button
          type="button"
          class="icon-action-btn delete-btn"
          onclick={onclear}
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
    grid-template-columns: minmax(0, 130px) minmax(max-content, 1fr) auto;
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
    background: transparent;
    border: none;
    color: var(--text-secondary);
    display: inline-flex;
    align-items: center;
    justify-content: center;
    cursor: pointer;
    opacity: 0.75;
    transition: color var(--transition-fast, 0.16s), transform var(--transition-fast, 0.16s), opacity var(--transition-fast, 0.16s);
  }

  .icon-action-btn:hover {
    background: transparent;
    color: var(--text-primary);
    opacity: 1;
    transform: scale(1.1);
  }

  .icon-action-btn:active {
    background: transparent;
    transform: scale(0.92);
    opacity: 0.7;
  }

  .icon-action-btn.delete-btn:hover {
    background: transparent;
    color: var(--danger-color, #ef4444);
    opacity: 1;
    transform: scale(1.1);
  }

  .icon-action-btn.cancel-btn:hover {
    background: transparent;
    color: var(--text-primary);
    opacity: 1;
    transform: scale(1.1);
  }
</style>
