<script lang="ts">
  import { onMount } from 'svelte';
  import type { FileIssueAction } from '$shared/types/ipc';
  import { uiStore } from '../../state/uiStore.svelte';
  import { libraryStore } from '../../state/libraryStore.svelte';
  import { notificationCenter } from '../../ui/notificationCenter.svelte';
  import { api } from '../../backend';
  import { groupDuplicateFiles } from '../../utils/fileIssueGroups';
  import Button from '../Common/Button.svelte';
  import RefreshIcon from '../Common/RefreshIcon.svelte';
  import SlidingTabs from '../Common/SlidingTabs.svelte';
  import ContentDialog from './ContentDialog.svelte';
  import { t } from '../../i18n';

  let busy = $state<string | null>(null);
  let pendingDelete = $state<string | null>(null);
  let refreshing = $state(false);
  let selectedKind = $state<string | null>(null);
  let scrollContainer = $state<HTMLDivElement>();
  onMount(() => { void libraryStore.reload(); });
  const groups = $derived([
    { kind: 'missing', label: t('fileIssues.missing'), tabLabel: t('fileIssues.tabs.missing'), icon: 'DocumentQuestionMark', description: t('fileIssues.missingDescription') },
    { kind: 'invalid', label: t('fileIssues.invalid'), tabLabel: t('fileIssues.tabs.invalid'), icon: 'Warning', description: t('fileIssues.invalidDescription') },
    { kind: 'duplicate', label: t('fileIssues.duplicate'), tabLabel: t('fileIssues.tabs.duplicate'), icon: 'Copy', description: t('fileIssues.duplicateDescription') },
    { kind: 'excluded', label: t('fileIssues.excluded'), tabLabel: t('fileIssues.tabs.excluded'), icon: 'Delete', description: t('fileIssues.excludedDescription') },
  ].map(group => ({ ...group, issues: libraryStore.fileIssues.filter(issue => issue.kind === group.kind) })));
  const activeGroup = $derived(groups.find(group => group.kind === selectedKind)
    ?? groups.find(group => group.issues.length > 0) ?? groups[0]);
  const tabs = $derived(groups.map(group => ({
    id: group.kind, label: group.tabLabel, icon: group.icon, count: group.issues.length,
  })));
  const duplicateGroups = $derived(groupDuplicateFiles(libraryStore.fileIssues, libraryStore.songs));

  function selectKind(kind: string) {
    selectedKind = kind;
    pendingDelete = null;
    scrollContainer?.scrollTo({ top: 0, behavior: 'instant' });
  }

  async function refresh() {
    if (refreshing || busy) return;
    refreshing = true;
    try { await libraryStore.reload(); }
    finally { refreshing = false; }
  }

  async function resolve(path: string, action: FileIssueAction) {
    if (busy) return;
    busy = path;
    try {
      await libraryStore.resolveFileIssue(path, action);
      pendingDelete = null;
      if (action === 'restore') notificationCenter.success(t('fileIssues.restored'), path);
    } catch (error) {
      notificationCenter.error(t('common.error'), String(error));
    } finally {
      busy = null;
    }
  }

  async function reveal(path: string) {
    try { await api.songs.revealSongFile(path); }
    catch (error) { notificationCenter.error(t('common.error'), String(error)); }
  }
</script>

<ContentDialog title={t('fileIssues.title')} icon="Warning" iconColor="var(--warning-color)"
  showCloseButton={false} dividers={false} bind:scrollContainer onClose={() => uiStore.closeDialog()}>
  {#snippet topRight()}
    <Button variant="ghost" size="icon" title={t('fileIssues.refresh')} ariaLabel={t('fileIssues.refresh')}
      disabled={busy !== null || refreshing} onclick={refresh}>
      <RefreshIcon loading={refreshing} size={14} />
    </Button>
  {/snippet}
  {#snippet toolbar()}
    <div class="conflict-selectors">
    <SlidingTabs {tabs} value={activeGroup.kind} label={t('fileIssues.title')} divider={false}
      idPrefix="file-issues-tab" panelId="file-issues-panel"
      onselect={selectKind} />
    </div>
  {/snippet}
  <div id="file-issues-panel" role="tabpanel" aria-labelledby={`file-issues-tab-${activeGroup.kind}`} tabindex="0">
    {#if libraryStore.hasFileIssues}
      <div class="group-heading"><h3>{activeGroup.label}</h3><span class="issue-count">{activeGroup.issues.length}</span></div>
      <p class="description">{activeGroup.description}</p>
      {#if activeGroup.issues.length}
        <div class="issues-list">
          {#if activeGroup.kind === 'duplicate'}
            {#each duplicateGroups as group (group.id)}
              <article class="issue-item duplicate-group" aria-label={group.title}>
                <div class="duplicate-heading">
                  <h4>{group.title}</h4>
                  <span class="copy-count">{t('fileIssues.copyCount', { count: group.copies.length })}</span>
                </div>
                <ul class="duplicate-files">
                  {#each group.copies as copy (copy.path)}
                    <li class="issue-main duplicate-file">
                      <div class="file-details">
                        <div class="file-name">{copy.name}</div>
                        <div class="file-path" title={copy.path}>{copy.path}</div>
                      </div>
                      <div class="issue-actions">
                        <Button variant="ghost" size="icon" icon="Folder" title={t('fileIssues.reveal')}
                          ariaLabel={`${t('fileIssues.reveal')}: ${copy.path}`}
                          disabled={busy !== null} onclick={() => reveal(copy.path)} />
                        {#if copy.isCurrent}
                          <span class="current-copy">{t('fileIssues.inLibrary')}</span>
                        {:else}
                          <Button variant="secondary" size="sm" disabled={busy !== null}
                            ariaLabel={`${t('fileIssues.useCopy')}: ${copy.path}`}
                            onclick={() => resolve(copy.path, 'useCopy')}>{t('fileIssues.useCopy')}</Button>
                        {/if}
                      </div>
                    </li>
                  {/each}
                </ul>
              </article>
            {/each}
          {:else}
          {#each activeGroup.issues as issue (issue.path)}
            <article class="issue-item">
              <div class="issue-main">
                <div class="file-details">
                  <div class="file-name">{issue.title}</div>
                  <div class="file-path" title={issue.path}>{issue.path}</div>
                </div>
                <div class="issue-actions">
                  {#if issue.kind !== 'missing'}
                    <Button variant="ghost" size="icon" icon="Folder" title={t('fileIssues.reveal')}
                      disabled={busy !== null} onclick={() => reveal(issue.path)} />
                  {/if}
                  {#if issue.kind === 'missing' || issue.kind === 'invalid'}
                    <Button variant="ghost" size="icon" icon="Dismiss" title={t('fileIssues.forget')}
                      disabled={busy !== null} onclick={() => resolve(issue.path, 'forget')} />
                  {/if}
                  {#if issue.kind === 'excluded' || issue.kind === 'invalid'}
                    <Button variant="secondary" size="sm" disabled={busy !== null}
                      onclick={() => resolve(issue.path, 'restore')}>
                      {issue.kind === 'invalid' ? t('fileIssues.retry') : t('fileIssues.restore')}
                    </Button>
                  {/if}
                  {#if issue.kind === 'excluded'}
                    <Button variant="ghost" colorMode="danger" size="icon" icon="Delete"
                      title={t('fileIssues.deleteFile')} disabled={busy !== null}
                      onclick={() => pendingDelete = pendingDelete === issue.path ? null : issue.path} />
                  {/if}
                </div>
              </div>
              {#if issue.relatedPath}<div class="issue-details">{t('fileIssues.currentCopy', { path: issue.relatedPath })}</div>{/if}
              {#if issue.details && issue.kind === 'invalid'}<div class="issue-details">{issue.details}</div>{/if}
              {#if pendingDelete === issue.path}
                <div class="delete-confirmation" role="alert">
                  <p>{t('fileIssues.deleteConfirmation')}</p>
                  <div class="confirmation-actions">
                    <Button variant="secondary" size="sm" disabled={busy !== null}
                      onclick={() => pendingDelete = null}>{t('fileIssues.cancel')}</Button>
                    <Button variant="danger" size="sm" disabled={busy !== null}
                      onclick={() => resolve(issue.path, 'deleteFile')}>{t('fileIssues.deleteFile')}</Button>
                  </div>
                </div>
              {/if}
            </article>
          {/each}
          {/if}
        </div>
      {:else}
        <p class="description empty-state">{t('fileIssues.emptyCategory')}</p>
      {/if}
    {:else}
      <p class="description empty-state">{t('fileIssues.empty')}</p>
    {/if}
  </div>
  {#snippet footer()}
    <Button variant="accent" onclick={() => uiStore.closeDialog()}>{t('dialogs.dismiss')}</Button>
  {/snippet}
</ContentDialog>

<style>
  .conflict-selectors { padding-bottom: 16px; }
  .issue-actions :global(.fluent-btn), .confirmation-actions :global(.fluent-btn) { border-color: transparent !important; }
  [role="tabpanel"]:focus-visible { outline: 2px solid var(--accent); outline-offset: 3px; }
  .group-heading { display: flex; align-items: center; gap: 8px; }
  h3 { margin: 0; color: var(--text-primary); font-size: 13px; font-weight: 600; }
  .issue-count { color: var(--text-secondary); font-size: 11px; font-variant-numeric: tabular-nums; }
  .description { margin: 5px 0 10px; font-size: 12px; line-height: 1.5; color: var(--text-secondary); }
  .issues-list { display: flex; flex-direction: column; gap: 6px; }
  .issue-item { padding: 10px 12px; background: var(--bg-tertiary); border-radius: var(--radius-sm); }
  .duplicate-group { padding: 0 12px; }
  .duplicate-heading { display: flex; align-items: baseline; gap: 12px; padding: 12px 0 4px; }
  .duplicate-heading h4 { flex: 1; min-width: 0; margin: 0; font-size: 12px; font-weight: 600; color: var(--text-primary); overflow-wrap: anywhere; }
  .copy-count { flex-shrink: 0; color: var(--text-tertiary); font-size: 11px; font-variant-numeric: tabular-nums; }
  .duplicate-files { margin: 0; padding: 0; list-style: none; }
  .duplicate-file { padding: 12px 0; }
  .duplicate-file + .duplicate-file { border-top: 1px solid var(--border-subtle); }
  .duplicate-file .file-name { font-weight: 500; }
  .current-copy { color: var(--text-secondary); font-size: 11px; padding: 0 10px; text-align: center; }
  .issue-main { display: flex; align-items: center; gap: 12px; }
  .file-details { flex: 1; min-width: 0; }
  .file-name { font-size: 12px; font-weight: 600; color: var(--text-primary); overflow-wrap: anywhere; }
  .file-path { margin-top: 3px; color: var(--text-tertiary); font-size: 11px; line-height: 1.45; overflow-wrap: anywhere; user-select: text; }
  .issue-actions, .confirmation-actions { display: flex; align-items: center; gap: 6px; flex-shrink: 0; }
  .issue-details { margin-top: 8px; color: var(--text-secondary); font-size: 11px; line-height: 1.5; overflow-wrap: anywhere; }
  .delete-confirmation { margin-top: 10px; padding-top: 10px; }
  .delete-confirmation p { margin: 0 0 8px; font-size: 12px; color: var(--warning-color); }
  .confirmation-actions { justify-content: flex-end; }
  .empty-state { margin: 0; }
  @media (max-width: 560px) {
    .issue-main { align-items: flex-start; flex-direction: column; }
    .issue-actions { align-self: flex-end; }
  }
</style>
