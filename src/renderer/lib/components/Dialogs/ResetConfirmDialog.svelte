<script lang="ts">
  import { uiStore } from '../../state/uiStore.svelte';
  import { notificationCenter } from '../../ui/notificationCenter.svelte';
  import Button from '../Common/Button.svelte';
  import ContentDialog from './ContentDialog.svelte';
  import { t } from '../../i18n';

  function confirmReset() {
    notificationCenter.success(t('dialogs.resetComplete'), t('dialogs.resetCompleteMsg'));
    uiStore.closeDialog();
  }
</script>

<ContentDialog
  title={t('dialogs.resetTitle')}
  icon="Warning"
  iconColor="var(--status-error)"
  onClose={() => uiStore.closeDialog()}
>
  <p class="warning-text">
    {t('dialogs.resetWarning')}
  </p>

  {#snippet footer()}
    <Button variant="secondary" onclick={() => uiStore.closeDialog()}>{t('dialogs.cancel')}</Button>
    <Button variant="danger" onclick={confirmReset}>{t('dialogs.resetConfirm')}</Button>
  {/snippet}
</ContentDialog>

<style>
  .warning-text {
    font-size: 13px;
    line-height: 1.5;
    color: var(--text-secondary);
    margin: 0;
  }
</style>
