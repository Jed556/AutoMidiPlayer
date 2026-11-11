<script lang="ts">
  import { onMount } from 'svelte';
  import type { SystemSnapshot } from '$shared/types/system';
  import { api } from '../../backend';
  import { gameStore } from '../../state/gameStore.svelte';
  import { midiInputStore } from '../../state/midiInputStore.svelte';
  import { t } from '../../i18n';
  import Button from '../Common/Button.svelte';
  import { pollSystemSnapshot, systemInfoRows } from './systemInfo';

  let snapshot = $state<SystemSnapshot | null>(null);
  let failed = $state(false);
  let retry: (() => void) | undefined;
  let rows = $derived(systemInfoRows(snapshot, failed, gameStore.activeGame, midiInputStore.state, t));

  onMount(() => {
    const poller = pollSystemSnapshot(() => api.system.getSnapshot(), value => {
      snapshot = value;
      failed = false;
    }, () => { failed = true; }, {
      document, window,
      setInterval: (callback, ms) => window.setInterval(callback, ms),
      clearInterval: timer => window.clearInterval(timer as number),
    });
    retry = () => poller.refresh();
    return () => { retry = undefined; poller.stop(); };
  });
</script>

<div class="system-info" role="group" aria-label={t('systemInfo.title')}>
  <dl class="info-grid environment">
    {#each rows.environment as row (row.id)}
      <div class="info-row" data-system-row={row.id}>
        <dt>{row.label}</dt>
        <dd>{row.value}</dd>
      </div>
    {/each}
  </dl>
  <dl class="info-grid services">
    {#each rows.services as row (row.id)}
      <div class="info-row" data-system-row={row.id} title={row.detail || undefined}>
        <dt>{row.label}</dt>
        <dd>
          <span class="indicator {row.tone}" aria-hidden="true"></span>
          <span class="value">{row.value}
            {#if row.status}<span class="status">{row.status}</span>{/if}
            {#if row.detail}<span class="error-detail">{row.detail}</span>{/if}
          </span>
        </dd>
      </div>
    {/each}
  </dl>
  {#if failed}
    <div class="refresh-error" role="status">
      <span>{t('systemInfo.refreshFailed')}</span>
      <Button variant="ghost" size="sm" onclick={() => retry?.()}>{t('systemInfo.retry')}</Button>
    </div>
  {/if}
</div>

<style>
  .system-info { container-type: inline-size; border-radius: 6px; padding: 14px; background: var(--control-fill, rgba(128, 128, 128, 0.06)); }
  .info-grid { display: grid; grid-template-columns: minmax(0, 1fr); gap: 16px 24px; margin: 0; }
  .services { border-top: 1px solid var(--border-subtle); margin-top: 16px; padding-top: 16px; }
  .info-row { min-width: 0; }
  dt { color: var(--text-secondary); font-size: 12px; line-height: 1.4; margin-bottom: 4px; }
  dd { display: flex; align-items: baseline; gap: 7px; margin: 0; color: var(--text-primary); font-size: 13px; line-height: 1.5; overflow-wrap: anywhere; }
  .value { min-width: 0; }
  .status { display: block; color: var(--text-secondary); font-size: 12px; }
  .indicator { width: 6px; height: 6px; border-radius: 50%; flex: 0 0 6px; align-self: flex-start; margin-top: 7px; background: var(--text-tertiary); }
  .indicator.good { background: var(--success-color, var(--accent-color)); }
  .indicator.warning { background: var(--warning-color); }
  .error-detail { display: block; color: var(--text-secondary); font-size: 12px; line-height: 1.4; margin-top: 4px; overflow-wrap: anywhere; }
  .refresh-error { display: flex; align-items: center; flex-wrap: wrap; gap: 4px 8px; margin-top: 12px; font-size: 12px; color: var(--text-secondary); }
  @container (min-width: 500px) { .info-grid { grid-template-columns: repeat(2, minmax(0, 1fr)); } }
</style>
