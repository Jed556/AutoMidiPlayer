<script lang="ts">
  import { gameStore } from '../../state/gameStore.svelte';
  import { uiStore } from '../../state/uiStore.svelte';
  import { t } from '../../i18n';
  import { instrumentKeyPairs } from '../../../../shared/utils/instrumentLayout';
  import { createSheetLegendRows } from '../../utils/sheetLegend';
  import ContentDialog from './ContentDialog.svelte';
  import Button from '../Common/Button.svelte';

  const rows = $derived(createSheetLegendRows(instrumentKeyPairs(gameStore.activeInstrument, gameStore.activeLayout)));
  const subtitle = $derived([gameStore.activeInstrument?.name, gameStore.activeLayout?.name].filter(Boolean).join(' · '));
</script>

<ContentDialog title={t('sheet.legendToastTitle')} {subtitle} icon="Info" bodyClass="sheet-legend-body">
  <div class="legend-description">
    <p>{t('sheet.legendToastMsg')}</p>
    <p>{t('sheet.legendModifiers')}</p>
    <p>{t('sheet.legendDelimiter', { delimiter: uiStore.dialogData?.delimiter ?? '·' })}</p>
  </div>
  {#if rows.length}
    <div class="legend-grid" aria-label={t('sheet.legend')}>
      {#each rows as row, index (`${index}:${row.pitch}`)}
        <div class="legend-entry">
          <kbd aria-label={`${t('sheet.legendKey')}: ${row.key}`}>{row.key === ' ' ? 'Space' : row.key}</kbd>
          <span aria-label={`${t('sheet.legendNote')}: ${row.note}`}>{row.note}</span>
        </div>
      {/each}
    </div>
  {:else}
    <p class="legend-empty">{t('sheet.legendEmpty')}</p>
  {/if}
  {#snippet footer()}
    <Button variant="secondary" onclick={() => uiStore.closeDialog()}>{t('dialogs.close')}</Button>
  {/snippet}
</ContentDialog>

<style>
  .legend-description { color: var(--text-secondary); font-size: 12px; line-height: 1.6; }
  .legend-description p { margin: 0 0 8px; }
  .legend-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(112px, 1fr)); gap: 8px 18px; margin-top: 16px; }
  .legend-entry { display: flex; align-items: center; justify-content: space-between; gap: 12px; padding: 6px 0; border-bottom: 1px solid var(--border-subtle); }
  kbd { min-width: 30px; padding: 3px 7px; border-radius: 4px; border: 1px solid var(--border-subtle); background: var(--bg-secondary); font: 12px var(--font-mono, monospace); text-align: center; white-space: pre; }
  .legend-entry span { font-size: 12px; font-variant-numeric: tabular-nums; }
  .legend-empty { color: var(--text-secondary); font-size: 13px; }
</style>
