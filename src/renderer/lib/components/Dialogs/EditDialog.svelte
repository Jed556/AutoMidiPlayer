<script lang="ts">
  import { onMount } from 'svelte';
  import type { MidiFile, TransposeMode } from '$shared/types/midi';
  import type { SongDetailsUpdate, SongSettingsScope } from '$shared/types/ipc';
  import { uiStore } from '../../state/uiStore.svelte';
  import { playbackStore } from '../../state/playbackStore.svelte';
  import { settingsStore } from '../../state/settingsStore.svelte';
  import { animationStore } from '../../state/animationStore.svelte';
  import { gameStore } from '../../state/gameStore.svelte';
  import { libraryStore } from '../../state/libraryStore.svelte';
  import { notificationCenter } from '../../ui/notificationCenter.svelte';
  import { api } from '../../backend/index';
  import { getKeyNoteName, formatSignedOffset } from '../../utils/musicConstants';
  import Button from '../Common/Button.svelte';
  import SplitButton from '../Common/SplitButton.svelte';
  import Picker from '../Common/Picker.svelte';
  import ToggleSwitch from '../Common/ToggleSwitch.svelte';
  import HoldNotesControl from '../Common/HoldNotesControl.svelte';
  import FluentIcon from '../Common/FluentIcon.svelte';
  import ContentDialog from './ContentDialog.svelte';
  import { t } from '../../i18n';

  const entry = uiStore.dialogData as MidiFile;
  const song = playbackStore.activeSong?.id === entry.id ? playbackStore.activeSong : entry;
  const context = gameStore.songSettingsContext;
  const gameName = gameStore.activeGame?.displayName ?? '';
  const instrumentName = gameStore.activeInstrument?.name ?? '';
  const initial = {
    title: song.title,
    artist: song.artist === 'Unknown Artist' ? '' : song.artist,
    album: song.album === 'Unknown Album' ? '' : song.album,
    baseKey: song.baseKey === null ? null : (song.baseKey ?? song.baseKeyOffset ?? 0),
    key: song.keyOffset ?? 0,
    transpose: song.transposeMode ?? 'Ignore',
    bpm: song.bpmOverride ?? undefined,
    speed: song.speed ?? 1,
    mergeNotes: song.mergeNotes ?? false,
    mergeMilliseconds: song.mergeToleranceMs ?? 100,
    holdNotes: song.holdNotes ?? false,
    tapThresholdMs: song.tapThresholdMs ?? 0,
  };
  let title = $state(initial.title);
  let artist = $state(initial.artist);
  let album = $state(initial.album);
  let baseKey = $state<number | null>(initial.baseKey);
  let key = $state(initial.key);
  let transpose = $state<TransposeMode>(initial.transpose);
  let bpm = $state<number | undefined>(initial.bpm);
  let nativeBpm = $state(song.nativeBpm ?? song.bpm ?? 120);
  let speed = $state(initial.speed);
  let mergeNotes = $state(initial.mergeNotes);
  let mergeMilliseconds = $state<number | undefined>(initial.mergeMilliseconds);
  let holdNotes = $state(initial.holdNotes);
  let tapThresholdMs = $state(initial.tapThresholdMs);
  let fileDate = $state(song.dateAdded);
  let isSaving = $state(false);
  let isScanning = $state(false);
  let error = $state('');
  let titleInput: HTMLInputElement;

  const offsets = Array.from({ length: 55 }, (_, index) => index - 27);
  const speeds = [...Array.from({ length: 20 }, (_, i) => (i + 1) / 10), 2.5, 3, 3.5, 4];
  const transposeValues: TransposeMode[] = ['Up', 'Smart', 'Ignore', 'Down'];
  const baseKeyOptions = [
    { value: null as number | null, label: 'Legacy (None)' },
    ...offsets.map(offset => ({ value: offset, label: formatSignedOffset(offset), detail: noteLabel(offset) })),
  ];
  let keyOptions = $derived(offsets.map(offset => ({
    value: offset - (baseKey ?? 0),
    label: formatSignedOffset(offset - (baseKey ?? 0)),
    detail: noteLabel(offset),
  })));
  const transposeOptions = transposeValues.map(mode => ({ value: mode, label: mode }));
  let speedOptions = $derived((speeds.includes(speed) ? speeds : [...speeds, speed].sort((a, b) => a - b))
    .map(value => ({ value, label: `${Number(value.toFixed(2))}x` })));
  let formattedDate = $derived(fileDate ? new Date(fileDate).toLocaleString() : '');
  function noteLabel(offset: number) {
    return `${getKeyNoteName(offset)}${Math.floor(offset / 12) + 3}`;
  }
  function clampKey() {
    key = Math.max(-27 - (baseKey ?? 0), Math.min(27 - (baseKey ?? 0), key));
  }

  onMount(() => {
    titleInput?.focus();
    titleInput?.select();
    let disposed = false;
    // Read file information without replacing draft settings or touching playback.
    void api.songs.analyzeSongFile(song.filePath).then(analysis => {
      if (disposed) return;
      nativeBpm = analysis.nativeBpm;
      fileDate = analysis.fileDate;
    }).catch(() => {}); // Missing files can still have their saved details edited.
    return () => { disposed = true; };
  });

  async function rescan() {
    isScanning = true;
    error = '';
    try {
      const analysis = await api.songs.analyzeSongFile(song.filePath);
      nativeBpm = analysis.nativeBpm;
      fileDate = analysis.fileDate;
      if (analysis.detectedBaseKey !== null) baseKey = analysis.detectedBaseKey;
      clampKey();
    } catch (e) {
      error = String(e);
    } finally {
      isScanning = false;
    }
  }

  async function resetDefaults() {
    title = initial.title;
    artist = initial.artist;
    album = initial.album;
    baseKey = initial.baseKey;
    key = initial.key;
    transpose = initial.transpose;
    bpm = initial.bpm;
    speed = initial.speed;
    mergeNotes = initial.mergeNotes;
    mergeMilliseconds = initial.mergeMilliseconds;
    holdNotes = initial.holdNotes;
    tapThresholdMs = initial.tapThresholdMs;
    error = '';
    if (settingsStore.settings.autoDetectBaseKey) {
      await rescan();
    } else {
      baseKey = 0;
      key = 0;
    }
  }

  async function revealFile() {
    try { await api.songs.revealSongFile(song.filePath); }
    catch (e) { notificationCenter.error('Unable to open file location', String(e)); }
  }

  async function save(kind: SongSettingsScope['kind'] = 'instrument') {
    if (isSaving || isScanning) return;
    if (kind !== 'global' && !context) return;
    const scope: SongSettingsScope = kind === 'global' ? { kind }
      : kind === 'game' ? { kind, gameId: context!.gameId } : { kind, ...context! };
    if (bpm !== undefined && (!Number.isFinite(bpm) || bpm <= 0 || bpm > 999)) {
      error = 'BPM must be between 0 and 999, or blank to use the native BPM.';
      return;
    }
    const validTolerance = Number.isInteger(mergeMilliseconds) && mergeMilliseconds! >= 1 && mergeMilliseconds! <= 1000;
    if (mergeNotes && !validTolerance) {
      error = 'Merge tolerance must be a whole number from 1 to 1000 ms.';
      return;
    }
    const savedTitle = title.trim() || song.filePath.split(/[/\\]/).pop()?.replace(/\.midi?$/i, '') || song.title;
    const bpmOverride = bpm === undefined || Math.abs(bpm - nativeBpm) < 0.01 ? null : bpm;
    const details: SongDetailsUpdate = {
      title: savedTitle, artist: artist.trim(), album: album.trim(), baseKey, key,
      transpose: { Ignore: 0, Smart: 1, Up: 2, Down: 3 }[transpose],
      bpm: bpmOverride, speed, mergeNotes, mergeMilliseconds: validTolerance ? mergeMilliseconds! : 100, holdNotes, tapThresholdMs,
      dateAdded: fileDate,
    };
    isSaving = true;
    error = '';
    try {
      await playbackStore.flushPendingSongSettings();
      if (!await api.songs.saveSongDetails(song.id, details, scope)) throw new Error('Song details could not be saved.');
    } catch (e) {
      error = String(e);
      isSaving = false;
      return;
    }
    try {
      const updated = await api.songs.getSongById(song.id);
      if (updated) await playbackStore.applyEditedSong(updated);
      await libraryStore.reload();
    } catch (e) {
      notificationCenter.warning('Song saved', `Playback settings could not be refreshed: ${String(e)}`);
    }
    notificationCenter.success(t('dialogs.songUpdated'), t('dialogs.songUpdatedMsg', { title: savedTitle }));
    uiStore.closeDialog();
  }
</script>

<ContentDialog title={t('dialogs.editTitle')} icon="Edit" class="edit-song-dialog" dividers={false} onClose={() => uiStore.closeDialog()}>
  <div class="edit-fields" class:no-motion={animationStore.isOff}>
    <p class="save-context">{t('dialogs.songSettingsScope', { game: gameName, instrument: instrumentName })}</p>

    <section class="edit-section" aria-labelledby="edit-song-details" style="--section-index: 0">
      <div class="section-header">
        <h3 id="edit-song-details">{t('dialogs.songDetailsSection')}</h3>
        <span class="global-badge" title={t('dialogs.savedGloballyTooltip')}>
          <span class="global-badge-text">{t('dialogs.savedGlobally')}</span>
          <FluentIcon name="Globe" size={13} />
        </span>
      </div>
      <div class="field-pair">
        <div class="form-field">
          <label for="edit-song-title">Title</label>
          <input id="edit-song-title" bind:this={titleInput} type="text" bind:value={title} />
        </div>
        <div class="form-field">
          <label for="edit-song-album">Album</label>
          <input id="edit-song-album" type="text" bind:value={album} />
        </div>
      </div>
      <div class="form-field">
        <label for="edit-song-artist">Artist</label>
        <input id="edit-song-artist" type="text" bind:value={artist} />
      </div>
    </section>

    <section class="edit-section" aria-labelledby="edit-song-key-settings" style="--section-index: 1">
      <h3 id="edit-song-key-settings">{t('dialogs.keyTranspositionsSection')}</h3>
      <div class="field-pair">
        <div class="form-field">
          <label for="edit-song-base-key">Base Key Offset</label>
          <div class="inline-field">
            <Picker id="edit-song-base-key" label="Base Key Offset" bind:value={baseKey} options={baseKeyOptions} onchange={clampKey} />
            <Button variant="ghost" size="icon" icon="ScanDash" title="Auto-scan base key offset from MIDI" disabled={isScanning || isSaving} onclick={rescan} />
          </div>
        </div>
        <div class="form-field">
          <label for="edit-song-key">Key Offset</label>
          <Picker id="edit-song-key" label="Key Offset" bind:value={key} options={keyOptions} />
        </div>
      </div>
      <div class="form-field">
        <label for="edit-song-transpose">Transpose</label>
        <Picker id="edit-song-transpose" label="Transpose" bind:value={transpose} options={transposeOptions} />
      </div>
    </section>

    <section class="edit-section" aria-labelledby="edit-song-timings" style="--section-index: 2">
      <h3 id="edit-song-timings">{t('dialogs.songTimingsSection')}</h3>
      <div class="field-pair">
        <div class="form-field">
          <label for="edit-song-bpm">BPM</label>
          <input id="edit-song-bpm" type="number" min="0.1" max="999" step="0.1" bind:value={bpm} placeholder={nativeBpm.toFixed(1)} />
        </div>
        <div class="form-field">
          <label for="edit-song-speed">Speed</label>
          <Picker id="edit-song-speed" label="Speed" bind:value={speed} options={speedOptions} />
        </div>
      </div>
    </section>

    <section class="edit-section" aria-labelledby="edit-song-note-playback" style="--section-index: 3">
      <h3 id="edit-song-note-playback">{t('dialogs.notePlaybackSection')}</h3>
      <div class="note-controls">
        <HoldNotesControl checked={holdNotes} thresholdMs={tapThresholdMs}
          onchange={(value) => { holdNotes = value; }}
          onthresholdchange={(value) => { tapThresholdMs = value; }} />
        <div class="merge-control">
          <ToggleSwitch bind:checked={mergeNotes} label="Merge Notes" />
          <div class="form-field tolerance-field" class:disabled={!mergeNotes}>
            <label for="edit-song-tolerance">Tolerance (ms)</label>
            <input id="edit-song-tolerance" type="number" min="1" max="1000" step="1" bind:value={mergeMilliseconds} disabled={!mergeNotes} />
          </div>
        </div>
      </div>
    </section>
    <div class="file-info">
      <button class="file-path" title={`Open in File Explorer: ${song.filePath}`} onclick={revealFile}>{song.filePath || '(unknown)'}</button>
      <span>{formattedDate}</span>
    </div>
    {#if error}<p class="form-error" role="alert">{error}</p>{/if}
  </div>
  {#snippet footer()}
    <div class="edit-footer">
      <Button variant="danger" disabled={isSaving || isScanning} onclick={resetDefaults}>Reset Defaults</Button>
      <span class="footer-spacer"></span>
      <Button variant="secondary" onclick={() => uiStore.closeDialog()}>{t('dialogs.cancel')}</Button>
      <SplitButton label={isSaving ? t('dialogs.saving') : t('dialogs.save')}
        title={t('dialogs.saveForInstrument', { game: gameName, instrument: instrumentName })}
        menuLabel={t('dialogs.saveOptions')} disabled={isSaving || isScanning || playbackStore.isChangingSongContext}
        primaryDisabled={!context}
        onclick={() => { if (context) void save(); }}
        options={[
          { label: t('dialogs.saveForGame', { game: gameName }), disabled: !context, onclick: () => void save('game') },
          { label: t('dialogs.saveForAllGames'), onclick: () => void save('global') },
        ]} />
    </div>
  {/snippet}
</ContentDialog>

<style>
  .edit-fields { display: flex; flex-direction: column; gap: 12px; }
  .save-context { margin: 0 0 4px; font-size: 12px; line-height: 1.5; color: var(--text-secondary); }
  .edit-section { display: flex; flex-direction: column; gap: 12px; padding: 14px; border-radius: var(--radius-md, 8px); background: var(--control-fill); animation: section-enter var(--anim-normal, 0.2s) cubic-bezier(0.16, 1, 0.3, 1) both; animation-delay: calc(var(--anim-normal, 0.2s) * var(--section-index) * 0.15); }
  .section-header { display: flex; align-items: center; justify-content: space-between; gap: 8px; }
  h3 { margin: 0; color: var(--text-primary); font-size: 12.5px; font-weight: 600; line-height: 1.3; }
  .global-badge { display: inline-flex; align-items: center; gap: 5px; font-size: 11px; font-weight: 500; color: var(--text-secondary); padding: 2px 7px; border-radius: 10px; background: var(--bg-tertiary); user-select: none; line-height: 1.2; transition: color var(--transition-fast), background var(--transition-fast); }
  .global-badge:hover { color: var(--text-primary); }
  .global-badge :global(.fluent-system-icon) { color: currentColor; opacity: 0.85; }
  .field-pair { display: grid; grid-template-columns: minmax(0, 1fr) minmax(0, 1fr); gap: 16px; }
  .form-field { display: flex; flex-direction: column; gap: 6px; min-width: 0; }
  label { font-size: 11.5px; color: var(--text-secondary); font-weight: 500; }
  input { box-sizing: border-box; width: 100%; min-width: 0; height: 34px; padding: 6px 10px; background: var(--bg-tertiary); border: none; border-radius: var(--radius-sm); color: var(--text-primary); font: inherit; font-size: 13px; outline: none; transition: background var(--transition-fast), opacity var(--transition-fast); }
  input:hover:enabled, input:focus { background: var(--control-fill-hover); }
  input:focus-visible { outline: 2px solid var(--accent); outline-offset: 2px; }
  input:disabled { opacity: 0.5; }
  .inline-field { display: flex; align-items: center; gap: 6px; min-height: 34px; }
  .note-controls { display: flex; flex-wrap: wrap; align-items: center; gap: 16px 24px; }
  .merge-control { display: flex; flex-wrap: wrap; align-items: center; gap: 12px 18px; }
  .tolerance-field { gap: 4px; transition: opacity var(--transition-fast); }
  .tolerance-field.disabled { opacity: 0.45; }
  .tolerance-field input { width: 76px; height: 28px; text-align: center; }
  .file-info { display: flex; flex-direction: column; gap: 4px; padding: 4px 2px 0; color: var(--text-secondary); font-size: 11px; }
  .file-path { padding: 0; border: 0; background: transparent; color: var(--accent); text-align: left; font: inherit; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; cursor: pointer; }
  .form-error { margin: 0; color: var(--danger, #f87171); font-size: 12px; }
  .edit-footer { display: flex; align-items: center; gap: 8px; width: 100%; }
  .footer-spacer { flex: 1; }
  :global(.edit-song-dialog .fluent-btn) { border: none; }
  :global(.edit-song-dialog .switch-pill) { border: none; }
  :global(.edit-song-dialog .switch-thumb) { top: 3px; left: 3px; }
  @keyframes section-enter { from { opacity: 0; transform: translateY(8px); } to { opacity: 1; transform: translateY(0); } }
  .no-motion .edit-section { animation: none; }
  @media (prefers-reduced-motion: reduce) { .edit-section { animation: none; } input, .tolerance-field { transition: none; } }
  @media (max-width: 560px) {
    .field-pair { grid-template-columns: minmax(0, 1fr); gap: 12px; }
    .edit-section { padding: 12px; }
    .edit-footer { display: grid; grid-template-columns: minmax(0, 1fr) minmax(0, 1fr); }
    .edit-footer :global(.split-button) { grid-column: 1 / -1; display: flex; }
    .edit-footer :global(.split-main) { flex: 1; }
    .footer-spacer { display: none; }
  }
</style>
