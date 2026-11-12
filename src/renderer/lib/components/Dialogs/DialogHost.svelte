<script lang="ts">
  import { uiStore } from '../../state/uiStore.svelte';
  import { easedDialogResize } from '../../actions/easedDialogResize';
  import { dialogBackdrop, dialogScaleIn, dialogScaleOut } from '../../utils/transitions';
  import EditDialog from './EditDialog.svelte';
  import UpdateDialog from './UpdateDialog.svelte';
  import FileIssuesDialog from './FileIssuesDialog.svelte';
  import ResetConfirmDialog from './ResetConfirmDialog.svelte';
  import ThirdPartyLicensesDialog from './ThirdPartyLicensesDialog.svelte';
  import ActionDialog from './ActionDialog.svelte';
  import PedalSettingsDialog from './PedalSettingsDialog.svelte';
  import SheetLegendDialog from './SheetLegendDialog.svelte';

  let { onreset }: { onreset: () => Promise<void> } = $props();

  function handleBackdrop(e: MouseEvent) {
    if (e.target === e.currentTarget) {
      uiStore.closeDialog();
    }
  }

  function handleKeydown(e: KeyboardEvent) {
    if (e.key === 'Escape' && uiStore.activeDialog) {
      uiStore.closeDialog();
    }
  }
</script>

<svelte:window onkeydown={handleKeydown} />

{#if uiStore.activeDialog}
  <div
    class="dialog-backdrop"
    transition:dialogBackdrop
    onclick={handleBackdrop}
    role="presentation"
  >
    <div
      class="dialog-box dialog-{uiStore.activeDialog}"
      use:easedDialogResize={uiStore.activeDialog === 'fileIssues'}
      style={uiStore.dialogData?.dialogMaxWidth ? `max-width: ${uiStore.dialogData.dialogMaxWidth}px;` : undefined}
      in:dialogScaleIn
      out:dialogScaleOut
      role="dialog"
      aria-modal="true"
    >
      {#if uiStore.activeDialog === 'edit'}
        <EditDialog />
      {:else if uiStore.activeDialog === 'update'}
        <UpdateDialog />
      {:else if uiStore.activeDialog === 'fileIssues'}
        <FileIssuesDialog />
      {:else if uiStore.activeDialog === 'resetConfirm'}
        <ResetConfirmDialog {onreset} />
      {:else if uiStore.activeDialog === 'licenses'}
        <ThirdPartyLicensesDialog />
      {:else if uiStore.activeDialog === 'action'}
        <ActionDialog />
      {:else if uiStore.activeDialog === 'pedalSettings'}
        <PedalSettingsDialog />
      {:else if uiStore.activeDialog === 'sheetLegend'}
        <SheetLegendDialog />
      {/if}
    </div>
  </div>
{/if}

<style>
  .dialog-backdrop {
    position: fixed;
    inset: 0;
    z-index: 1000;
    background: rgba(0, 0, 0, 0.45);
    display: grid;
    place-items: center;
    grid-template-areas: 'dialog';
    user-select: none;
  }

  .dialog-box {
    grid-area: dialog;
    background: var(--bg-dialog, var(--card-bg, #27272A));
    border: none;
    border-radius: var(--radius-lg);
    box-shadow: var(--shadow-xl);
    overflow: hidden;
    display: flex;
    flex-direction: column;
    min-height: 0;
    max-width: calc(100vw - 32px);
    max-height: calc(100dvh - 32px);
    transform-origin: center center;
    will-change: transform, opacity;
  }

  .dialog-box.dialog-edit {
    width: 620px;
  }

  .dialog-box.dialog-update {
    width: 480px;
  }

  .dialog-box.dialog-fileIssues {
    width: 640px;
  }

  .dialog-box.dialog-resetConfirm {
    width: 420px;
  }

  .dialog-box.dialog-action,
  .dialog-box.dialog-pedalSettings,
  .dialog-box.dialog-sheetLegend {
    width: 480px;
  }

  .dialog-box.dialog-licenses {
    width: min(960px, 75vw);
    min-width: min(560px, calc(100vw - 32px));
    height: min(780px, 84vh);
    min-height: min(480px, 75vh);
  }
</style>
