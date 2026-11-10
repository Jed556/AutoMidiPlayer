<script lang="ts">
  import { uiStore } from '../../state/uiStore.svelte';
  import Button from '../Common/Button.svelte';
  import ContentDialog from './ContentDialog.svelte';
  import { t } from '../../i18n';

  let { onreset }: { onreset: () => Promise<void> } = $props();
  let resetting = $state(false);

  async function confirmReset() {
    if (resetting) return;
    resetting = true;
    uiStore.closeDialog();
    await onreset();
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
    <Button variant="danger" disabled={resetting} onclick={confirmReset}>{t('dialogs.resetConfirm')}</Button>
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
