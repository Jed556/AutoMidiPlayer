<script lang="ts">
  import { uiStore } from '../../state/uiStore.svelte';
  import { updateStore } from '../../state/updateStore.svelte';
  import { playbackStore } from '../../state/playbackStore.svelte';
  import Button from '../Common/Button.svelte';
  import FluentIcon from '../Common/FluentIcon.svelte';
  import ContentDialog from './ContentDialog.svelte';
  import { t } from '../../i18n';

  function downloadUpdate() {
    void updateStore.downloadUpdate(() => playbackStore.prepareForUpdate());
    uiStore.closeDialog();
  }

  function openReleaseNotes() {
    updateStore.openReleaseNotes();
  }
</script>

<ContentDialog
  title={t('dialogs.updateTitle')}
  icon="Sparkle"
  iconColor="var(--status-success)"
  onClose={() => uiStore.closeDialog()}
>
  <div class="update-body">
    <!-- Version Banner -->
    <div class="version-banner">
      <div class="banner-top">
        <div class="v-tag">
          {t('dialogs.versionLabel', { version: updateStore.latestVersion.replace(/^v/i, '') })}
        </div>
        {#if updateStore.latestRelease?.prerelease}
          <span class="beta-badge">{t('dialogs.betaBadge')}</span>
        {/if}
      </div>

      <div class="v-title">
        {updateStore.latestRelease?.name || updateStore.latestVersion}
      </div>

      <div class="v-status">{t('dialogs.updateBanner')}</div>
    </div>

    <!-- Native updates select the package matching the current installation. -->
    <div class="package-row">
      <span>{t('dialogs.inAppUpdatePackage')}</span>
      {#if updateStore.updateDownloaded}
        <span>{t('dialogs.updateReady')}</span>
      {:else if updateStore.isDownloadingUpdate}
        <span>{t('startup.downloadingUpdate')}</span>
      {/if}
    </div>

    <!-- Changelog Box -->
    <div class="changelog-box">
      <h4 class="cl-title">{t('dialogs.whatsNew', { version: updateStore.latestVersion })}</h4>
      <ul class="cl-list">
        {#if updateStore.changelogList.length > 0}
          {#each updateStore.changelogList as item}
            <li>{item}</li>
          {/each}
        {:else}
          <li>{t('dialogs.clItem1')}</li>
          <li>{t('dialogs.clItem2')}</li>
          <li>{t('dialogs.clItem3')}</li>
          <li>{t('dialogs.clItem4')}</li>
          <li>{t('dialogs.clItem5')}</li>
        {/if}
      </ul>
    </div>
  </div>

  {#snippet footer()}
    <div class="footer-actions">
      <Button variant="subtle" onclick={openReleaseNotes}>
        <FluentIcon name="Open" size={13} />
        <span>{t('dialogs.viewReleaseNotes')}</span>
      </Button>
      <div class="footer-right">
        <Button variant="secondary" onclick={() => uiStore.closeDialog()}>{t('dialogs.remindLater')}</Button>
        <Button variant="accent" disabled={updateStore.isPreparingUpdate || updateStore.isApplyingUpdate} onclick={downloadUpdate}>
          {t(updateStore.updateDownloaded ? 'dialogs.installUpdate' : 'dialogs.downloadInstall')}
        </Button>
      </div>
    </div>
  {/snippet}
</ContentDialog>

<style>
  .update-body {
    display: flex;
    flex-direction: column;
    gap: 12px;
  }

  .version-banner {
    background: rgba(16, 185, 129, 0.08);
    border: 1px solid rgba(16, 185, 129, 0.22);
    border-radius: var(--radius-sm);
    padding: 10px 14px;
    display: flex;
    flex-direction: column;
    gap: 2px;
  }

  .banner-top {
    display: flex;
    align-items: center;
    gap: 8px;
  }

  .v-tag {
    font-size: 13.5px;
    font-weight: 700;
    color: var(--status-success);
  }

  .beta-badge {
    font-size: 10px;
    font-weight: 600;
    text-transform: uppercase;
    background: rgba(245, 158, 11, 0.15);
    color: #f59e0b;
    border: 1px solid rgba(245, 158, 11, 0.3);
    padding: 1px 6px;
    border-radius: 10px;
    letter-spacing: 0.3px;
  }

  .v-title {
    font-size: 12px;
    font-weight: 600;
    color: var(--text-primary);
    margin-top: 2px;
  }

  .v-status {
    font-size: 11.5px;
    color: var(--text-secondary);
  }

  .package-row {
    display: flex;
    align-items: center;
    justify-content: space-between;
    background: var(--bg-tertiary);
    border-radius: var(--radius-sm);
    padding: 8px 12px;
    font-size: 11.5px;
  }

  .changelog-box {
    background: var(--bg-tertiary);
    border-radius: var(--radius-sm);
    padding: 12px 14px;
    max-height: 220px;
    overflow-y: auto;
  }

  .cl-title {
    font-size: 12px;
    font-weight: 600;
    color: var(--text-primary);
    margin: 0 0 6px 0;
  }

  .cl-list {
    margin: 0;
    padding-left: 18px;
    font-size: 11.5px;
    color: var(--text-secondary);
    line-height: 1.6;
  }

  .cl-list li {
    margin-bottom: 3px;
  }

  .footer-actions {
    display: flex;
    align-items: center;
    justify-content: space-between;
    width: 100%;
  }

  .footer-right {
    display: flex;
    gap: 8px;
  }
</style>
