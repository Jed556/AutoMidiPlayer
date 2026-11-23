<script lang="ts">
  import { uiStore } from '../../state/uiStore.svelte';
  import { updateStore } from '../../state/updateStore.svelte';
  import { settingsStore } from '../../state/settingsStore.svelte';
  import { playbackStore } from '../../state/playbackStore.svelte';
  import Button from '../Common/Button.svelte';
  import FluentIcon from '../Icons/FluentIcon.svelte';
  import ContentDialog from './ContentDialog.svelte';
  import MarkdownReader from '../Common/MarkdownReader.svelte';
  import { getReleaseNotesMarkdown } from '../../data/releaseNotesData';
  import { api } from '../../backend/index.js';
  import { t } from '../../i18n';

  let releaseNotes = $derived(
    uiStore.dialogData?.markdown || getReleaseNotesMarkdown(updateStore.latestRelease?.body)
  );

  let versionTag = $derived(
    uiStore.dialogData?.version || updateStore.latestRelease?.tagName || updateStore.latestVersion || `v${settingsStore.programVersion}`
  );

  function openOnGitHub() {
    const url = updateStore.latestRelease?.htmlUrl || 'https://github.com/Jed556/AutoMidiPlayer/releases';
    void api.settings.openUrl(url);
  }

  function downloadUpdate() {
    void updateStore.downloadUpdate(() => playbackStore.prepareForUpdate());
    uiStore.closeDialog();
  }
</script>

<ContentDialog
  size="large"
  title={t('dialogs.releaseNotesTitle')}
  subtitle={versionTag}
  icon="Notepad24"
  showCloseButton={true}
  onClose={() => uiStore.closeDialog()}
>
  {#snippet topRight()}
    <Button
      variant="ghost"
      size="small"
      onclick={openOnGitHub}
      title={t('dialogs.openOnGitHub')}
    >
      <FluentIcon name="Open" size={13} />
      <span>{t('dialogs.openOnGitHub')}</span>
    </Button>
  {/snippet}

  <div class="release-notes-container">
    <MarkdownReader markdown={releaseNotes} />
  </div>

  {#snippet footer()}
    <div class="footer-actions">
      {#if updateStore.updateAvailable}
        <Button variant="secondary" onclick={() => uiStore.closeDialog()}>{t('dialogs.remindLater')}</Button>
        <Button
          variant="accent"
          disabled={updateStore.isPreparingUpdate || updateStore.isApplyingUpdate}
          onclick={downloadUpdate}
        >
          {t(updateStore.updateDownloaded ? 'dialogs.installUpdate' : 'dialogs.downloadInstall')}
        </Button>
      {:else}
        <Button variant="secondary" onclick={() => uiStore.closeDialog()}>{t('dialogs.close')}</Button>
      {/if}
    </div>
  {/snippet}
</ContentDialog>

<style>
  .release-notes-container {
    padding: 6px 4px 16px 4px;
  }

  .footer-actions {
    display: flex;
    align-items: center;
    justify-content: flex-end;
    gap: 8px;
    width: 100%;
  }
</style>
