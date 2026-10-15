<script lang="ts">
  import type { InstrumentLayoutStyle } from '../../../../shared/types/games';
  import type { InstrumentKey } from '../../../../shared/utils/instrumentLayout';
  import { t } from '../../i18n';

  let { layoutStyle, rows, activePitches, onplay }: {
    layoutStyle: InstrumentLayoutStyle;
    rows: Array<Array<InstrumentKey | null>>;
    activePitches: Set<number>;
    onplay: (pitch: number) => void;
  } = $props();

  const columns = $derived(Math.max(1, ...rows.map(row => row.length)));
  const columnWeights = $derived(Array.from({ length: columns }, (_, column) =>
    rows.some(row => row[column] && !row[column]!.sharp) ? 1 : rows.some(row => row[column]?.sharp) ? 0.625 : 1));
  const heartopiaColumns = $derived(columnWeights.map(weight => `${weight}fr`).join(' '));
  const heartopiaWidth = $derived(columnWeights.reduce((sum, weight) => sum + weight * 64, 0) + (columns - 1) * 8);
</script>

<div class="instrument-layout" class:sky={layoutStyle === 'sky'} class:genshin={layoutStyle === 'genshin'}
  class:heartopia={layoutStyle === 'heartopia'} class:drums={layoutStyle === 'drums'}
  style:--columns={columns} style:--heartopia-columns={heartopiaColumns} style:--heartopia-width={`${heartopiaWidth}px`}>
  {#each rows as row, rowIndex}
    <div class="instrument-row-group">
    {#if layoutStyle === 'genshin' && rows.length === 3 && rows.every(row => row.length === 7)}
      <div class="row-header">{t(`instrument.${['highTreble', 'midAlto', 'lowBass'][rowIndex]}`)}</div>
    {/if}
    <div class="instrument-row">
      {#each row as key}
        {#if key}
          <button type="button" class="instrument-key" class:sharp={key.sharp}
            class:active={activePitches.has(key.pitch)} onclick={() => onplay(key.pitch)}
            aria-label={`${key.note}${key.label ? ` (${key.label})` : ''}`} title={key.note}>
            {#if layoutStyle === 'sky'}
              <span class="key-binding">{key.label}</span>
              <span class="key-solfege">{key.solfege}</span>
              <span class="key-note">{key.note}</span>
            {:else}
              <span class="key-solfege">{key.solfege}</span>
              <span class="key-binding">{key.label}</span>
              {#if layoutStyle !== 'heartopia' || !key.sharp}<span class="key-note">{key.note}</span>{/if}
            {/if}
          </button>
        {:else}
          <span class="key-spacer" aria-hidden="true"></span>
        {/if}
      {/each}
    </div>
    </div>
  {/each}
</div>

<style>
  .instrument-layout { display: flex; flex-direction: column; gap: 12px; width: 100%; max-width: 720px; }
  .instrument-row { display: grid; grid-template-columns: repeat(var(--columns), minmax(0, 1fr)); gap: 8px; }
  .instrument-key {
    display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 3px;
    min-width: 0; height: 72px; padding: 6px 0; border: none; border-radius: var(--radius-md);
    background: var(--bg-card); color: var(--text-primary); font: inherit; cursor: pointer;
    box-shadow: 0 2px 4px rgba(0, 0, 0, 0.1); user-select: none;
    transition: transform var(--transition-fast), background var(--transition-fast), box-shadow var(--transition-fast);
  }
  .instrument-key:hover { transform: translateY(-2px); }
  .instrument-key:focus, .instrument-key:focus-visible { outline: none; }
  .instrument-key.active { background: var(--accent); transform: translateY(2px); box-shadow: 0 0 16px var(--accent-subtle); }
  .key-binding { font-size: 14px; font-weight: 700; }
  .key-note { color: var(--text-tertiary); font-size: 9.5px; }
  .key-solfege {
    display: flex; align-items: center; justify-content: center; width: 22px; height: 22px;
    border-radius: 50%; background: var(--bg-tertiary); color: var(--accent); font-size: 11px; font-weight: 700;
  }
  .instrument-key.active .key-solfege { background: rgba(255,255,255,0.25); }
  .instrument-key.active span { color: white; }
  .genshin .instrument-row-group { padding: 10px 14px; }
  .row-header { margin-bottom: 8px; font-size: 11px; font-weight: 600; color: var(--text-tertiary); text-transform: uppercase; letter-spacing: 0.04em; }
  .sky { width: max-content; max-width: 100%; padding: 20px; border-radius: var(--radius-lg); background: var(--bg-secondary); }
  .sky .instrument-row { gap: 12px; grid-template-columns: repeat(var(--columns), 68px); }
  .sky .instrument-key { height: 68px; border-radius: 50%; box-shadow: none; gap: 0; }
  .sky .key-binding { font-size: 13px; }
  .sky .key-solfege { background: transparent; width: auto; height: auto; font-size: 11px; }
  .sky .key-note { font-size: 9px; }
  .sky .instrument-key:hover { transform: scale(1.05); }
  .sky .instrument-key.active { transform: scale(0.96); }
  .drums { max-width: 600px; gap: 14px; }
  .drums .instrument-row { gap: 14px; }
  .drums .instrument-key { height: 100px; }
  .heartopia { max-width: var(--heartopia-width); gap: 18px; }
  .heartopia .instrument-row { grid-template-columns: var(--heartopia-columns); align-items: start; }
  .heartopia .instrument-key { height: auto; aspect-ratio: 1; border-radius: 24%; gap: 2px; }
  .heartopia .key-solfege { height: auto; width: auto; background: transparent; font-size: 16px; }
  .heartopia .key-binding { font-size: 11px; }
  .heartopia .key-note { font-size: 8px; }
  .heartopia .instrument-key.sharp { border-radius: 50%; padding: 3px 0; }
  .heartopia .sharp .key-solfege { font-size: 12px; }
  .heartopia .sharp .key-binding { font-size: 9px; }
</style>
