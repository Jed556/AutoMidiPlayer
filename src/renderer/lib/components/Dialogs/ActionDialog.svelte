<script lang="ts">
  import { uiStore } from '../../state/uiStore.svelte';
  import ContentDialog from './ContentDialog.svelte';
  import Button from '../Common/Button.svelte';

  const request = $derived(uiStore.dialogData || {});

  const hasButtons = $derived(() => {
    if (request.hideFooter) return false;
    return !!(
      request.confirmButton ||
      request.cancelButton ||
      request.customButton ||
      (request.buttons && request.buttons.length > 0)
    );
  });

  async function handleButtonClick(btn: any, defaultOutcome: string) {
    if (btn?.action) {
      const res = await btn.action();
      if (res === false) return;
    }
    if (request._resolve) {
      request._resolve(defaultOutcome);
    }
    uiStore.closeDialog();
  }

  function handleClose() {
    if (request.onClose) {
      request.onClose();
    }
    if (request._resolve) {
      request._resolve('Cancelled');
    }
    uiStore.closeDialog();
  }
</script>

<ContentDialog
  title={request.title || ''}
  subtitle={request.subtitle || ''}
  icon={request.icon}
  iconColor={request.iconColor}
  showCloseButton={request.showCloseButton !== false}
  onClose={handleClose}
  hideFooter={!hasButtons()}
  size={request.size || 'normal'}
>
  {#if request.body}
    <p class="dialog-body-text">{request.body}</p>
  {/if}
  {#if request.content}
    <div class="dialog-custom-content">
      {#if typeof request.content === 'function'}
        {@const CustomComponent = request.content}
        <CustomComponent />
      {:else}
        {request.content}
      {/if}
    </div>
  {/if}

  {#if hasButtons()}
    {#snippet footer()}
      {#if request.customButton}
        <Button
          variant={request.customButton.variant || 'secondary'}
          onclick={() => handleButtonClick(request.customButton, 'Custom')}
        >
          {request.customButton.text}
        </Button>
      {/if}
      {#if request.cancelButton}
        <Button
          variant={request.cancelButton.variant || 'secondary'}
          onclick={() => handleButtonClick(request.cancelButton, 'Cancelled')}
        >
          {request.cancelButton.text}
        </Button>
      {/if}
      {#if request.confirmButton}
        <Button
          variant={request.confirmButton.variant || 'accent'}
          onclick={() => handleButtonClick(request.confirmButton, 'Confirmed')}
        >
          {request.confirmButton.text}
        </Button>
      {/if}
      {#if request.buttons}
        {#each request.buttons as btn}
          <Button
            variant={btn.variant || 'secondary'}
            onclick={() => handleButtonClick(btn, btn.text)}
          >
            {btn.text}
          </Button>
        {/each}
      {/if}
    {/snippet}
  {/if}
</ContentDialog>

<style>
  .dialog-body-text {
    font-size: 13px;
    line-height: 1.5;
    color: var(--text-secondary);
    margin: 0;
  }

  .dialog-custom-content {
    margin-top: 10px;
  }
</style>
