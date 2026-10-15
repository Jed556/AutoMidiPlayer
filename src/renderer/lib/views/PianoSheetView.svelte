<script lang="ts">
  import { playbackStore } from '../state/playbackStore.svelte';
  import { gameStore } from '../state/gameStore.svelte';
  import { notificationCenter } from '../ui/notificationCenter.svelte';
  import { t } from '../i18n';
  import { instrumentKeyPairs } from '../../../shared/utils/instrumentLayout';
  import FluentIcon from '../components/Common/FluentIcon.svelte';
  import Button from '../components/Common/Button.svelte';
  import Dropdown from '../components/Common/Dropdown.svelte';
  import NumberBox from '../components/Common/NumberBox.svelte';

  let delimiter = $state(' ');
  let bars = $state(4);
  let beats = $state(4);
  let shorten = $state(1);
  let isCopied = $state(false);
  const layoutOptions = $derived(gameStore.getInstrumentLayouts(gameStore.activeInstrument).map(layout => ({
    value: layout.id,
    label: layout.name.toLowerCase().includes('layout') ? layout.name : `${layout.name} Layout`,
  })));
  const layoutPlaceholder = $derived(gameStore.isLayoutMissing ? 'Missing Layout' : 'No Layouts');
  const pitchToKey = $derived(new Map(instrumentKeyPairs(gameStore.activeInstrument, gameStore.activeLayout)));

  function getNoteKey(pitch: number): string {
    return pitchToKey.get(pitch) ?? '·';
  }

  let generatedSheet = $derived.by(() => {
    const song = playbackStore.activeSong;
    if (!song) return '';

    const dur = song.durationSeconds ?? (song.durationMs ? song.durationMs / 1000 : 0);
    const lines: string[] = [];
    lines.push(`// ${song.title} - ${song.artist}`);
    lines.push(`// BPM: ${song.bpm || 120} | Time: ${Math.floor(dur / 60)}:${Math.floor(dur % 60).toString().padStart(2, '0')}`);
    lines.push(`// Layout: ${gameStore.activeLayout?.name ?? layoutPlaceholder}`);
    lines.push('');

    // Generate bar patterns
    const activeTracks = (song.tracks || []).filter(t => !(t.muted ?? t.isMuted));
    const chords: string[] = [];

    if (activeTracks.length > 0) {
      activeTracks.forEach(t => {
        (t.notes || []).forEach(n => {
          chords.push(getNoteKey(n.pitch ?? n.noteNumber ?? 60));
        });
      });
    } else if (song.notes && song.notes.length > 0) {
      song.notes.forEach((n: any) => {
        chords.push(getNoteKey(n.pitch ?? n.noteNumber ?? 60));
      });
    }

    if (chords.length === 0) {
      // Sample sheet notes for demonstration
      chords.push('A', 'S', 'D', 'F', 'G', 'H', 'J', '[AQ]', '[SW]', '[DE]', '[FR]', 'G', 'H', 'J', 'Q', 'W', 'E', 'R', 'T', 'Y', 'U');
    }

    const perLine = Math.max(1, Math.trunc(bars || 1)) * Math.max(1, Math.trunc(beats || 1));
    for (let i = 0; i < chords.length; i += perLine) {
      const slice = chords.slice(i, i + perLine);
      lines.push(slice.join(delimiter));
    }

    return lines.join('\n');
  });

  async function copySheet() {
    if (!generatedSheet) return;
    try {
      await navigator.clipboard.writeText(generatedSheet);
      isCopied = true;
      notificationCenter.success(t('sheet.copiedToastTitle'), t('sheet.copiedToastMsg'));
      setTimeout(() => isCopied = false, 2000);
    } catch {
      notificationCenter.error(t('sheet.copyFailedTitle'), t('sheet.copyFailedMsg'));
    }
  }

  let isResetting = $state(false);

  function resetDefaults() {
    isResetting = true;
    delimiter = ' ';
    bars = 4;
    beats = 4;
    shorten = 1;
    notificationCenter.info(t('sheet.resetDefaultsToastTitle'), t('sheet.resetDefaultsToastMsg'));
    setTimeout(() => {
      isResetting = false;
    }, 100);
  }

  function showLegend() {
    notificationCenter.info(
      t('sheet.legendToastTitle'),
      t('sheet.legendToastMsg')
    );
  }
</script>

<div class="piano-sheet-view">
  <div class="top-controls-wrap">
    <div class="format-controls">
      <label class="control-field">
        <span class="control-label">{t('sheet.delimiter')}</span>
        <input
          type="text"
          bind:value={delimiter}
          class="delimiter-input"
          maxlength="4"
        />
      </label>
      <div class="control-field layout-field" role="group" aria-label={t('sheet.layout')}>
        <span class="control-label">{t('sheet.layout')}</span>
        <Dropdown
          value={gameStore.activeLayout?.id ?? ''}
          options={layoutOptions}
          placeholder={layoutPlaceholder}
          tooltipText={gameStore.layoutDeletionTooltip}
          disabled={layoutOptions.length === 0}
          onchange={(id) => gameStore.syncActiveInstrumentMapping(undefined, id)}
          class="sheet-layout-dropdown"
          width="100%"
        />
      </div>
    </div>

    <div class="vertical-divider" aria-hidden="true"></div>

    <div class="split-size-group" role="group" aria-label={t('sheet.splitSize')}>
      <span class="split-size-label" aria-hidden="true">{t('sheet.splitSize')}</span>
      <div class="split-size-inputs">
        <div class="control-field number-field">
          <span class="control-label">{t('sheet.bars')}</span>
          <NumberBox min={1} max={32} bind:value={bars} label={t('sheet.bars')} />
        </div>
        <div class="control-field number-field">
          <span class="control-label">{t('sheet.beats')}</span>
          <NumberBox min={1} max={16} bind:value={beats} label={t('sheet.beats')} />
        </div>
        <div class="control-field number-field" title={t('sheet.shortenTooltip')}>
          <span class="control-label">{t('sheet.shortenEvery')}</span>
          <NumberBox min={1} max={16} bind:value={shorten} label={t('sheet.shortenEvery')} />
        </div>
      </div>
    </div>

    <div class="vertical-divider" aria-hidden="true"></div>

    <div class="actions-stack">
      <Button variant="ghost" size="sm" onclick={showLegend} class="sheet-action-btn">
        <FluentIcon name="Info" size={15} />
        <span>{t('sheet.legend')}</span>
      </Button>

      <Button variant="ghost" size="sm" colorMode="danger" onclick={resetDefaults} class="sheet-action-btn">
        <FluentIcon name="ArrowClockwise" loading={isResetting} size={15} />
        <span>{t('sheet.resetDefaults')}</span>
      </Button>
    </div>
  </div>

  <div class="sheet-editor-box">
    {#if playbackStore.activeSong}
      <div class="editor-header">
        <span class="sheet-title">{t('sheet.sheetTitle', { title: playbackStore.activeSong.title })}</span>
        <Button variant="ghost" class="copy-btn" onclick={copySheet} title={t('sheet.copySheet')}>
          {#if isCopied}
            <FluentIcon name="Checkmark" size={14} />
            <span>{t('sheet.copied')}</span>
          {:else}
            <FluentIcon name="Copy" size={14} />
            <span>{t('sheet.copySheet')}</span>
          {/if}
        </Button>
      </div>

      <textarea
        readonly
        class="sheet-textarea"
        value={generatedSheet}
        spellcheck="false"
      ></textarea>
    {:else}
      <div class="no-song-container">
        <FluentIcon name="Document" size={48} class="empty-icon" />
        <span class="empty-msg">{t('sheet.noSongToGenerate')}</span>
      </div>
    {/if}
  </div>
</div>

<style>
  .piano-sheet-view {
    display: flex;
    flex-direction: column;
    height: 100%;
    width: 100%;
    padding: 14px 18px;
    overflow: hidden;
    user-select: none;
    gap: 12px;
  }

  .top-controls-wrap {
    display: flex;
    align-items: center;
    flex-wrap: wrap;
    column-gap: 18px;
    row-gap: 14px;
    flex-shrink: 0;
  }

  .format-controls {
    display: flex;
    align-items: center;
    gap: 12px;
    min-width: 0;
    max-width: 100%;
  }

  .control-field {
    display: flex;
    flex-direction: column;
    gap: 6px;
    min-width: 0;
  }

  .layout-field {
    width: 248px;
    min-width: 0;
  }

  .control-label {
    font-size: 11px;
    font-weight: 500;
    color: var(--text-secondary);
    white-space: nowrap;
    transition: color var(--transition-fast);
  }

  .control-field:focus-within > .control-label {
    color: var(--text-primary);
  }

  .vertical-divider {
    width: 1px;
    height: 28px;
    background: var(--divider-color, rgba(255, 255, 255, 0.08));
    margin: 0 2px;
    flex-shrink: 0;
  }

  .layout-field :global(.sheet-layout-dropdown .dropdown-trigger) {
    height: 34px;
    border-radius: 6px;
    border: none;
    outline: none;
    box-shadow: none;
    animation: none;
  }

  .layout-field :global(.sheet-layout-dropdown .dropdown-trigger:focus-visible),
  .layout-field :global(.sheet-layout-dropdown .dropdown-trigger.is-open) {
    background: var(--control-fill-hover);
  }

  .delimiter-input {
    width: 50px;
    height: 34px;
    padding: 0 8px;
    border-radius: 6px;
    background: var(--control-fill);
    border: none;
    color: var(--text-primary);
    font-size: 13px;
    text-align: center;
    outline: none;
    font-family: inherit;
    transition: background var(--transition-fast);
  }

  .delimiter-input:hover, .delimiter-input:focus {
    background: var(--control-fill-hover);
  }

  .number-field {
    width: 70px;
  }

  .number-field :global(.number-box) {
    width: 100%;
    height: 34px;
    padding: 0 4px 0 10px;
    border-radius: 6px;
    background: var(--control-fill);
    border: none;
    box-shadow: none;
    animation: none;
  }

  .number-field :global(.number-box:hover),
  .number-field :global(.number-box:focus-within) {
    background: var(--control-fill-hover);
  }

  .number-field :global(.num-input) {
    width: 100%;
    min-width: 0;
    padding: 0;
    font-size: 13px;
    font-variant-numeric: tabular-nums;
  }

  .number-field :global(.steppers) {
    flex-shrink: 0;
  }

  .number-field :global(.stepper-btn:focus-visible) {
    outline: none;
    background: var(--control-fill-active);
    color: var(--text-primary);
  }

  .split-size-group {
    display: flex;
    align-items: center;
    gap: 10px;
  }

  .split-size-label {
    writing-mode: vertical-rl;
    transform: rotate(180deg);
    font-size: 11px;
    font-weight: 500;
    color: var(--text-secondary);
    white-space: nowrap;
  }

  .split-size-inputs {
    display: flex;
    align-items: center;
    gap: 10px;
  }

  .actions-stack {
    display: flex;
    flex-direction: column;
    gap: 4px;
  }

  .actions-stack :global(.sheet-action-btn) {
    justify-content: flex-start;
    padding: 0 8px;
    height: 26px;
    font-size: 12px;
  }

  .top-controls-wrap :global(.fluent-btn:focus-visible) {
    outline: none;
    background: var(--control-fill-hover);
    color: var(--text-primary);
  }

  .sheet-editor-box {
    flex: 1;
    min-height: 0;
    display: flex;
    flex-direction: column;
    border-radius: 6px;
    background: var(--card-bg);
    overflow: hidden;
  }

  .editor-header {
    display: flex;
    align-items: center;
    justify-content: space-between;
    padding: 8px 14px;
    background: var(--control-fill);
  }

  .sheet-title {
    font-size: 12.5px;
    font-weight: 500;
    color: var(--text-secondary);
  }

  .editor-header :global(.copy-btn) {
    padding: 4px 10px;
    height: auto;
    font-size: 12px;
  }

  .sheet-textarea {
    flex: 1;
    width: 100%;
    background: transparent;
    border: none;
    padding: 14px;
    color: var(--text-primary);
    font-family: 'Consolas', 'Courier New', monospace;
    font-size: 13px;
    line-height: 1.6;
    resize: none;
    outline: none;
    overflow-y: auto;
  }

  .no-song-container {
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    height: 100%;
    color: var(--text-tertiary);
    gap: 12px;
  }

  .no-song-container :global(.empty-icon) {
    opacity: 0.3;
  }

  .empty-msg {
    font-size: 13px;
  }
</style>
