<script lang="ts">
  import { onMount } from 'svelte';
  import type { ChordDefinition, KeyboardLayoutConfig } from '../../../../shared/types/games';
  import type { ChordWheelState } from '../../../../shared/types/ipc';
  import { api } from '../../backend';
  import { playbackStore } from '../../state/playbackStore.svelte';
  import { notificationCenter } from '../../ui/notificationCenter.svelte';
  import { t } from '../../i18n';
  import Button from '../Common/Button.svelte';
  import Dropdown from '../Common/Dropdown.svelte';
  import ChordIcon from '../Icons/ChordIcon.svelte';

  let { wheel, pads, fretMode = false }: {
    wheel: NonNullable<KeyboardLayoutConfig['chordWheel']>;
    pads: ChordDefinition[];
    fretMode?: boolean;
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
  const visiblePads = $derived(wheelState.page === null ? [] :
    pads.slice(wheelState.page * wheel.positions.length, (wheelState.page + 1) * wheel.positions.length));

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
    <div class="wheel-title"><ChordIcon size={16} /><span>{t(fretMode ? 'instrument.fretboard' : 'instrument.chordWheel')}</span></div>
    <div class="wheel-paging" role="group" aria-label={t('instrument.chordWheelPages')}>
      <Button variant="ghost" size="sm" icon="ChevronLeft"
        disabled={locked || wheelState.page === null || wheel.pageCount < 2}
        title={t('instrument.chordWheelPreviousKey', { key: wheel.pageKeys[0].toUpperCase() })}
        onclick={() => setPage((wheelState.page! - 1 + wheel.pageCount) % wheel.pageCount, true)}>{t('instrument.chordWheelPrevious')}</Button>
      <span class="wheel-page" aria-live="polite">{wheelState.page === null ? t('instrument.chordWheelUnknownPage') :
        t('instrument.chordWheelPage', { page: wheelState.page + 1, count: wheel.pageCount })}</span>
      <Button variant="ghost" size="sm" icon="ChevronRight"
        disabled={locked || wheelState.page === null || wheel.pageCount < 2}
        title={t('instrument.chordWheelNextKey', { key: wheel.pageKeys[1].toUpperCase() })}
        onclick={() => setPage((wheelState.page! + 1) % wheel.pageCount, true)}>{t('instrument.chordWheelNext')}</Button>
    </div>
    <div class="wheel-sync">
      <span>{t('instrument.chordWheelSyncPage')}</span>
      <Dropdown variant="ghost" options={pageOptions} value={wheelState.page}
        placeholder={t('instrument.chordWheelUnknownPage')} ariaLabel={t('instrument.chordWheelSyncPage')}
        title={t('instrument.chordWheelSyncHint')} disabled={locked} width="150px"
        onchange={(page: number) => setPage(page, false)} />
    </div>
  </div>
  <div class="wheel-chords" aria-label={t('instrument.chordWheelAvailableChords')}>
    {#each visiblePads as pad}
      <span class="wheel-chord">{pad.name}</span>
    {/each}
  </div>
  <p class="wheel-hint">{t(fretMode ? 'instrument.guitarSharedFret' : 'instrument.chordWheelHint')}</p>
</section>

<style>
  .chord-wheel-controls {
    flex: 0 0 auto; margin: 0 28px 8px; padding: 12px 16px;
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
  .wheel-hint { margin: 10px 0 0; color: var(--text-tertiary); font-size: 11px; line-height: 1.5; }
  @media (max-width: 700px) {
    .chord-wheel-controls { margin-right: 16px; margin-left: 16px; }
    .wheel-title { width: 100%; }
  }
</style>
