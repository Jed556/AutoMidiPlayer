<script lang="ts">
  import { onMount } from 'svelte';
  import type { KeyBinding, PedalBindings } from '$shared/types/settings';
  import { resolvePedalBindings } from '$shared/utils/pedalBindings';
  import { gameStore } from '../../state/gameStore.svelte';
  import { api } from '../../backend/index.js';
  import { uiStore } from '../../state/uiStore.svelte';
  import { t } from '../../i18n';
  import ContentDialog from './ContentDialog.svelte';
  import KeyEditControl from '../Settings/KeyEditControl.svelte';
  import Button from '../Common/Button.svelte';

  // Keep edits attached to the layout that opened this dialog.
  const gameId = gameStore.activeGame?.id;
  const layoutId = gameStore.activeLayout?.id;
  let layout = $derived(gameStore.games.find(game => game.id === gameId)?.layouts.find(layout => layout.id === layoutId));
  let saving = $state(false);
  let error = $state('');
  let defaults = $state<PedalBindings | null>(null);
  const fields = ['sustainKey', 'sostenutoKey', 'unaCordaKey'] as const;
  const labels = { sustainKey: 'instrument.sustainDamper', sostenutoKey: 'instrument.sostenuto', unaCordaKey: 'instrument.softPedal' } as const;
  let bindings = $derived(layout ? resolvePedalBindings(layout) : null);
  let canReset = $derived(Boolean(bindings && defaults && fields.some(field => bindings![field] !== defaults![field])));

  onMount(() => {
    let active = true;
    if (layoutId) api.games.getLayoutPedalDefaults(layoutId).then(value => {
      if (active) defaults = value;
    }).catch(cause => { if (active) error = String(cause); });
    return () => { active = false; };
  });

  function keyBinding(key: string | null): KeyBinding {
    return { key: key ?? '', ctrl: false, alt: false, shift: false };
  }

  async function save(value: Partial<PedalBindings> | null) {
    if (!layout || saving) return;
    saving = true;
    error = '';
    try {
      await gameStore.saveLayoutPedalBindings(layout.id, value);
    } catch (cause) {
      error = String(cause);
    } finally {
      saving = false;
    }
  }

  function setKey(field: keyof PedalBindings, key: string | null) {
    if (bindings) void save({ [field]: key });
  }
</script>

<ContentDialog title={t('instrument.pedalBindings')} subtitle={layout?.name} icon="Settings">
  <p class="description">{t('instrument.pedalBindingsDescription')}</p>
  {#if bindings}
    <div class="bindings">
      {#each fields as field}
        <KeyEditControl
          label={t(labels[field])}
          binding={keyBinding(bindings[field])}
          singleKey
          disabled={saving}
          onchange={(updated) => setKey(field, updated.key)}
          onclear={() => setKey(field, null)}
        />
      {/each}
    </div>
  {/if}
  {#if error}<p class="error" role="alert">{error}</p>{/if}
  {#snippet footer()}
    <Button variant="ghost" icon="ArrowClockwise" disabled={!canReset || saving}
      title={t('instrument.resetPedalsTooltip')}
      onclick={() => save(null)}>{t('instrument.resetPedals')}</Button>
    <Button variant="primary" onclick={() => uiStore.closeDialog()}>{t('dialogs.close')}</Button>
  {/snippet}
</ContentDialog>

<style>
  .description { margin: 0 0 16px; color: var(--text-secondary); font-size: 13px; line-height: 1.5; }
  .bindings { display: grid; gap: 8px; --hotkey-label-width: 120px; }
  .error { color: var(--danger, #e74856); font-size: 13px; margin: 12px 0 0; }
</style>
