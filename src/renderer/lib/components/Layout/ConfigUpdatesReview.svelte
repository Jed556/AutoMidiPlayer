<script lang="ts">
  import { defaultConfigStore as store } from '../../state/defaultConfigStore.svelte';
  import { t } from '../../i18n';
  import { changedConfigFields, configReviewHeading, configReviewItems } from '../../utils/configUpdateReview';
  import { tooltip } from '../../actions/tooltip';
  import Button from '../Common/Button.svelte';
  import Checkbox from '../Common/Checkbox.svelte';
  import CustomScrollViewer from '../Common/CustomScrollViewer.svelte';
  import JsonDiffViewer from '../Common/JsonDiffViewer.svelte';

  let { oncontinue, onskip }: { oncontinue: () => void; onskip: () => void } = $props();
  const conflicts = $derived(configReviewItems(store.report));
  let selectedId = $state('');
  let replacements = $state(new Set<string>());
  const selected = $derived(conflicts.find(conflict => conflict.id === selectedId) ?? conflicts[0]);
  const heading = $derived(selected ? configReviewHeading(selected) : '');
  const replaceCount = $derived(conflicts.filter(conflict => replacements.has(conflict.id)).length);
  const fields = $derived(selected ? changedConfigFields(selected.currentJson, selected.bundledJson) : []);
  const locked = $derived(store.loading || store.saving);

  function setReplacement(id: string, replace: boolean) {
    const next = new Set(replacements);
    if (replace) next.add(id); else next.delete(id);
    replacements = next;
  }

  async function apply() {
    if (await store.apply(replacements)) oncontinue();
    else replacements = new Set();
  }
</script>

<div class="config-review">
  <div class="config-review-body">
  <p class="description">{t('defaultConfigs.description')}</p>
  {#if store.error}<p class="error" role="alert">{store.error}</p>{/if}
  {#each store.report?.issues ?? [] as issue}
    <p class="error" role="alert">{issue.path}: {issue.message}</p>
  {/each}
  {#if conflicts.length}
    <div class="review-summary">
      <Checkbox label={t('defaultConfigs.replaceAll')} checked={replaceCount === conflicts.length} disabled={locked}
        onchange={checked => { replacements = new Set(checked ? conflicts.map(conflict => conflict.id) : []); }} />
      <span>{t('defaultConfigs.selection', { replace: replaceCount, keep: conflicts.length - replaceCount })}</span>
    </div>
    <div class="review-grid">
      <CustomScrollViewer class="config-list-viewer" contentClass="config-file-list" overlayScrollbar={true} padding="0 12px 0 0" fadeHeight={16}>
        {#each conflicts as conflict (conflict.id)}
          <button type="button" class="config-file" class:selected={selected?.id === conflict.id}
            aria-pressed={selected?.id === conflict.id}
            use:tooltip={conflict.duplicate ? [conflict.duplicate.keep.path, ...conflict.duplicate.olderFiles.map(file => file.path)].join(' · ') : conflict.path}
            disabled={locked}
            onclick={() => { selectedId = conflict.id; }}>
            <span class="file-name">{conflict.duplicate ? conflict.duplicate.configId : conflict.path.split('/').at(-1)}</span>
            <span class="file-kind">{t(conflict.duplicate ? 'defaultConfigs.duplicateId' : `defaultConfigs.${conflict.kind}`)}</span>
            <span class="file-choice" class:replace={replacements.has(conflict.id)}>
              {t(replacements.has(conflict.id) ? 'defaultConfigs.replace' : conflict.duplicate ? 'defaultConfigs.keepBoth' : 'defaultConfigs.keep')}
            </span>
          </button>
        {/each}
      </CustomScrollViewer>
      {#if selected}
        <div class="comparison">
          <h2>{heading}</h2>
          <p class="file-path">{selected.path}</p>
          {#if fields.length}<p class="changed-fields">{t('defaultConfigs.changedFields', { fields: fields.join(', ') })}</p>{/if}
          {#if selected.duplicate}
            <p class="duplicate-explanation">{t('defaultConfigs.duplicateExplanation')}</p>
            <div class="duplicate-files">
              <p><strong>{t('defaultConfigs.fileToKeep')}</strong> {selected.duplicate.keep.path}
                <time>{new Date(selected.duplicate.keep.modifiedMs).toLocaleString()}</time></p>
              {#each selected.duplicate.olderFiles as file}
                <p><strong>{t('defaultConfigs.fileToRemove')}</strong> {file.path}
                  <time>{new Date(file.modifiedMs).toLocaleString()}</time></p>
              {/each}
            </div>
          {:else if selected.path !== selected.id}<p class="bundled-path">{t('defaultConfigs.bundledPath', { path: selected.id })}</p>{/if}
          {#key selected.id}
            <JsonDiffViewer before={selected.currentJson} after={selected.bundledJson}
              height="100%"
              showChoiceTabs={true}
              choice={replacements.has(selected.id) ? 'replace' : 'keep'}
              keepLabel={t(selected.duplicate ? 'defaultConfigs.keepBoth' : 'defaultConfigs.keep')}
              replaceLabel={t('defaultConfigs.replace')}
              replaceTitle={t(selected.duplicate ? 'defaultConfigs.replaceDuplicate' : 'defaultConfigs.replaceFile')}
              choiceLabel={heading}
              choiceDisabled={locked}
              onchoicechange={next => setReplacement(selected.id, next === 'replace')}
              beforeLabel={t(selected.duplicate ? 'defaultConfigs.olderFile' : 'defaultConfigs.current')}
              afterLabel={t(selected.duplicate ? 'defaultConfigs.fileToKeep' : 'defaultConfigs.bundled')} />
          {/key}
        </div>
      {/if}
    </div>
  {:else if store.loading}
    <p class="status" role="status">{t('defaultConfigs.checking')}</p>
  {:else if !store.error && !store.report?.issues.length}
    <p class="status">{t('defaultConfigs.upToDate')}</p>
  {/if}
  </div>
  <footer class="review-footer">
    {#if conflicts.length}
      <p class="backup-note">{t('defaultConfigs.backupNote')}</p>
    {/if}
    <div class="footer-actions">
      {#if store.error}<Button variant="secondary" disabled={locked} onclick={() => store.load()}>{t('defaultConfigs.retry')}</Button>{/if}
      <Button variant="secondary" disabled={locked} onclick={onskip}>
        {t('defaultConfigs.later')}
      </Button>
      {#if conflicts.length}
        <Button variant="accent" disabled={locked} onclick={apply}>
          {t(store.saving ? 'defaultConfigs.applying' : 'defaultConfigs.apply')}
        </Button>
      {/if}
      {#if !conflicts.length && !store.error}<Button variant="accent" disabled={locked} onclick={oncontinue}>{t('defaultConfigs.continue')}</Button>{/if}
    </div>
  </footer>
</div>

<style>
  .config-review { display: flex; flex-direction: column; min-height: 0; flex: 1; overflow: hidden; }
  .config-review-body { display: flex; flex-direction: column; flex: 1; min-height: 0; overflow: hidden; }
  .review-footer { display: flex; flex-wrap: wrap; align-items: center; justify-content: space-between; gap: 12px 16px; padding: 16px 0 0; flex-shrink: 0; }
  .footer-actions { display: flex; flex-wrap: wrap; justify-content: flex-end; gap: 10px; margin-left: auto; flex-shrink: 0; }
  .review-footer :global(.fluent-btn) { border: none; }
  .description { flex-shrink: 0; margin: 0 0 18px; font-size: clamp(13px, .85vw, 15px); line-height: 1.6; color: var(--text-secondary); }
  .review-summary { flex-shrink: 0; display: flex; flex-wrap: wrap; justify-content: space-between; align-items: center; gap: 12px; margin-bottom: 16px; }
  .review-summary > span, .backup-note, .status { font-size: 12px; color: var(--text-secondary); line-height: 1.5; }
  .review-grid { flex: 1; min-height: 0; display: grid; grid-template-columns: clamp(200px, 18vw, 300px) minmax(0, 1fr); grid-template-rows: minmax(0, 1fr); gap: clamp(20px, 2vw, 36px); }
  :global(.config-list-viewer) { height: 100%; min-height: 0; }
  :global(.config-file-list) { display: grid; align-content: start; gap: 8px; }
  .config-file { display: grid; grid-template-columns: minmax(0, 1fr) auto; gap: 5px 8px; width: 100%; padding: 12px; text-align: left; border: none; border-radius: var(--radius-sm); background: var(--control-fill); color: var(--text-secondary); font: inherit; cursor: pointer; }
  .config-file:hover { background: var(--control-fill-hover); }
  .config-file.selected { background: var(--control-fill-hover); color: var(--text-primary); }
  .config-file:focus-visible { outline: 2px solid var(--accent); outline-offset: -2px; }
  .config-file:disabled { cursor: default; opacity: .6; }
  .file-name { grid-column: 1 / -1; font-size: var(--config-detail-size, 12px); font-weight: 600; overflow-wrap: anywhere; }
  .file-kind, .file-choice { font-size: clamp(11px, .75vw, 13px); }
  .file-choice.replace { color: var(--accent); }
  .comparison { display: flex; flex-direction: column; min-width: 0; min-height: 0; height: 100%; --json-diff-font-size: var(--config-code-size, 12px); --json-diff-label-size: var(--config-detail-size, 12px); }
  h2 { flex-shrink: 0; margin: 0 0 6px; color: var(--text-primary); font-size: clamp(14px, .9vw, 18px); font-weight: 600; }
  .file-path { flex-shrink: 0; margin: 0 0 4px; font-size: var(--config-detail-size, 12px); line-height: 1.5; color: var(--text-primary); overflow-wrap: anywhere; user-select: text; }
  .bundled-path, .changed-fields { flex-shrink: 0; font-size: 11px; line-height: 1.5; color: var(--text-secondary); overflow-wrap: anywhere; }
  .changed-fields { margin: 0; }
  .bundled-path { margin: 4px 0 0; }
  .duplicate-explanation { flex-shrink: 0; color: var(--text-secondary); font-size: 12px; line-height: 1.5; }
  .duplicate-files { flex-shrink: 0; font-size: 11px; color: var(--text-secondary); overflow-wrap: anywhere; }
  .duplicate-files p { margin: 8px 0; }
  .duplicate-files strong { color: var(--text-primary); font-weight: 600; }
  time { display: block; font-size: 10px; margin-top: 3px; }
  .backup-note { flex: 1 1 260px; min-width: 0; margin: 0; }
  .status { flex-shrink: 0; }
  .error { flex-shrink: 0; color: var(--danger, #e74856); font-size: 12px; line-height: 1.5; overflow-wrap: anywhere; }
  @media (max-width: 700px) {
    .review-grid { grid-template-columns: minmax(0, 1fr); grid-template-rows: auto minmax(0, 1fr); gap: 12px; }
    :global(.config-list-viewer) { height: auto; max-height: 140px; }
  }
</style>
