<script lang="ts">
  import { onMount } from 'svelte';
  import type { ChordDefinition, KeyboardLayoutConfig } from '../../../../shared/types/games';
  import type { ArrangementState, ChordWheelState } from '../../../../shared/types/ipc';
  import { api } from '../../backend';
  import { playbackStore } from '../../state/playbackStore.svelte';
  import { notificationCenter } from '../../ui/notificationCenter.svelte';
  import { t } from '../../i18n';
  import Button from '../Common/Button.svelte';
  import Dropdown from '../Common/Dropdown.svelte';
  import ChordIcon from '../Icons/ChordIcon.svelte';

  let { wheel, pads, fretMode, paged, mixed, needsModeSync, physicalMode, onsyncmode, action, activeSlots }: {
    wheel: NonNullable<KeyboardLayoutConfig['chordWheel']>;
    pads: ChordDefinition[];
    fretMode: boolean;
    paged: boolean;
    mixed: boolean;
    needsModeSync: boolean;
    physicalMode: 'frets' | 'chords' | null;
    onsyncmode: (mode: 'frets' | 'chords') => Promise<void>;
    action: ArrangementState | null;
    activeSlots: ReadonlySet<number>;
  } = $props();

  let wheelState = $state<ChordWheelState>({ page: null, busy: false, error: null });
  let pending = $state(false);
  let loaded = $state(false);
  let revision = 0;
  let lastError: string | null = null;
  const locked = $derived(!loaded || pending || wheelState.busy || playbackStore.isPlaying);
  const pageOptions = $derived(Array.from({ length: wheel.pageCount }, (_, page) => ({
    value: page,
    label: t('instrument.chordWheelPage', { page: page + 1, count: wheel.pageCount }),
  })));
  const page = $derived(!paged ? 0 : action?.page ?? wheelState.page);
  const pageStart = $derived((page ?? 0) * wheel.positions.length);
  const visiblePads = $derived(page === null ? [] : pads.slice(pageStart, pageStart + wheel.positions.length));

  async function syncMode(mode: 'chords' | 'frets') {
    if (locked) return;
    pending = true;
    try {
      await onsyncmode(mode);
      const beforeRead = revision;
      const state = await api.playback.getChordWheelState();
      if (revision === beforeRead) receiveState(state);
    } catch (error) {
      notificationCenter.error(t('instrument.chordWheelError'), String(error));
    } finally {
      pending = false;
    }
  }

  function receiveState(state: ChordWheelState) {
    wheelState = state;
    loaded = true;
    if (state.error && state.error !== lastError) {
      notificationCenter.error(t('instrument.chordWheelError'), state.error);
    }
    lastError = state.error;
  }

  onMount(() => {
    let disposed = false;
    const unsubscribe = api.playback.onChordWheelState(state => {
      revision++;
      if (!disposed) receiveState(state);
    });
    const beforeRead = revision;
    void api.playback.getChordWheelState().then(state => {
      if (!disposed && revision === beforeRead) receiveState(state);
    }).catch(error => {
      if (!disposed) notificationCenter.error(t('instrument.chordWheelError'), String(error));
    });
    return () => {
      disposed = true;
      unsubscribe();
    };
  });

  async function setPage(page: number, send: boolean) {
    if (locked || !Number.isInteger(page) || page < 0 || page >= wheel.pageCount) return;
    pending = true;
    try {
      if (!await api.playback.setChordWheelPage(page, send)) throw new Error(t('instrument.chordWheelPageFailed'));
      const beforeRead = revision;
      const state = await api.playback.getChordWheelState();
      if (revision === beforeRead) receiveState(state);
    } catch (error) {
      notificationCenter.error(t('instrument.chordWheelError'), String(error));
    } finally {
      pending = false;
    }
  }
</script>

<section class="chord-wheel-controls" aria-label={t('instrument.chordWheel')}>
  <div class="wheel-heading">
    <div class="wheel-title"><ChordIcon size={16} /><span>{t(fretMode ? 'instrument.fretboard' : mixed && !physicalMode ? 'instrument.guitarView' : 'instrument.chordWheel')}</span></div>
    {#if mixed}
      <div class="wheel-sync">
        <span>{t('instrument.guitarSyncMode')}</span>
        <Dropdown variant="ghost" options={(['chords', 'frets'] as const).map(value => ({ value, label: t(`instrument.guitarMode_${value}`) }))}
          value={physicalMode} placeholder={t('instrument.guitarModeUnknown')} ariaLabel={t('instrument.guitarSyncMode')}
          disabled={locked} width="130px" onchange={syncMode} />
      </div>
    {:else if needsModeSync && physicalMode}
      <Button variant="ghost" size="sm" disabled={locked}
        onclick={() => syncMode(physicalMode!)}>{t('instrument.guitarConfirmMode', { mode: t(`instrument.guitarMode_${physicalMode}`) })}</Button>
    {/if}
    {#if paged && wheel.pageCount > 1 && !needsModeSync}
      <div class="wheel-paging" role="group" aria-label={t('instrument.chordWheelPages')}>
        <Button variant="ghost" size="sm" icon="ChevronLeft"
          disabled={locked || wheelState.page === null}
          title={t('instrument.chordWheelPreviousKey', { key: wheel.pageKeys[0].toUpperCase() })}
          onclick={() => setPage((wheelState.page! - 1 + wheel.pageCount) % wheel.pageCount, true)}>{t('instrument.chordWheelPrevious')}</Button>
        <span class="wheel-page" aria-live="polite">{page === null ? t('instrument.chordWheelUnknownPage') :
          t('instrument.chordWheelPage', { page: page + 1, count: wheel.pageCount })}</span>
        <Button variant="ghost" size="sm" icon="ChevronRight"
          disabled={locked || wheelState.page === null}
          title={t('instrument.chordWheelNextKey', { key: wheel.pageKeys[1].toUpperCase() })}
          onclick={() => setPage((wheelState.page! + 1) % wheel.pageCount, true)}>{t('instrument.chordWheelNext')}</Button>
      </div>
      <div class="wheel-sync">
        <span>{t('instrument.chordWheelSyncPage')}</span>
        <Dropdown variant="ghost" options={pageOptions} value={page}
          placeholder={t('instrument.chordWheelUnknownPage')} ariaLabel={t('instrument.chordWheelSyncPage')}
          title={t('instrument.chordWheelSyncHint')} disabled={locked} width="150px"
          onchange={(page: number) => setPage(page, false)} />
      </div>
    {/if}
  </div>
  {#if visiblePads.length}
    <div class="wheel-chords" aria-label={t(fretMode ? 'instrument.fretboard' : 'instrument.chordWheelAvailableChords')}>
      {#each visiblePads as pad, index}
        {#if pad.name}<span class="wheel-chord" class:active={activeSlots.has(pageStart + index)}>{pad.name}</span>{/if}
      {/each}
    </div>
  {/if}
  {#if mixed && !physicalMode}
    <p class="wheel-hint">{t('instrument.guitarSyncModeHint')}</p>
  {:else if fretMode || paged && wheel.pageCount > 1}
    <p class="wheel-hint">{t(fretMode ? 'instrument.guitarSharedFret' : 'instrument.chordWheelHint')}</p>
  {/if}
</section>

<style>
  .chord-wheel-controls {
    flex: 0 0 auto; margin: 12px 28px 8px; padding: 12px 16px;
    border: 1px solid var(--border-subtle); border-radius: var(--radius-md);
    background: var(--bg-secondary);
  }
  .wheel-heading { display: flex; align-items: center; flex-wrap: wrap; column-gap: 20px; row-gap: 6px; }
  .wheel-title { display: flex; align-items: center; gap: 8px; margin-right: auto; font-size: 13px; font-weight: 600; color: var(--text-primary); }
  .wheel-paging { display: flex; align-items: center; gap: 6px; }
  .wheel-sync { display: flex; align-items: center; gap: 6px; color: var(--text-tertiary); font-size: 11px; }
  .wheel-page { min-width: 84px; text-align: center; font-size: 12px; color: var(--text-secondary); font-variant-numeric: tabular-nums; }
  .wheel-chords { display: flex; flex-wrap: wrap; gap: 8px; margin-top: 10px; }
  .wheel-chord { min-width: 46px; padding: 4px 10px; border-radius: var(--radius-sm); background: var(--bg-tertiary); color: var(--text-primary); font-size: 12px; font-weight: 600; text-align: center; }
  .wheel-chord.active { background: var(--accent); color: white; }
  .wheel-hint { margin: 10px 0 0; color: var(--text-tertiary); font-size: 11px; line-height: 1.5; }
  @media (max-width: 700px) {
    .chord-wheel-controls { margin-right: 16px; margin-left: 16px; }
    .wheel-title { width: 100%; }
  }
</style>
