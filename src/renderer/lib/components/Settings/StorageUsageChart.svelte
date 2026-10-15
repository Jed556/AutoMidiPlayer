<script lang="ts">
  import { onMount } from 'svelte';
  import type { StorageUsage } from '$shared/types/ipc';
  import { t } from '../../i18n';
  import { animationStore } from '../../state/animationStore.svelte';

  let { usage, loading, error, onrefresh }: {
    usage: StorageUsage | null;
    loading: boolean;
    error: string;
    onrefresh: () => void;
  } = $props();

  let element: HTMLDivElement;
  let visible = $state(false);
  let categories = $derived([
    { key: 'app', label: t('settings.appDataSize'), bytes: usage?.appDataBytes ?? 0 },
    { key: 'online', label: t('settings.onlineMidiFolderSize'), bytes: usage?.onlineMidiBytes ?? 0 },
    { key: 'cache', label: t('settings.discoverCacheSize'), bytes: usage?.discoverCacheBytes ?? 0 },
  ]);
  const format = (bytes: number) => {
    if (bytes < 1024) return `${bytes} B`;
    const unit = Math.min(3, Math.floor(Math.log(bytes) / Math.log(1024)));
    return `${(bytes / 1024 ** unit).toFixed(1)} ${['B', 'KB', 'MB', 'GB'][unit]}`;
  };
  let description = $derived(usage ? categories.map(c => `${c.label} ${format(c.bytes)}`).join(', ') : '');

  onMount(() => {
    const observer = new IntersectionObserver(([entry]) => {
      const entering = entry.isIntersecting && !visible;
      visible = entry.isIntersecting;
      if (entering) onrefresh();
    }, { threshold: 0.2 });
    observer.observe(element);
    const refresh = () => { if (visible && !document.hidden) onrefresh(); };
    const timer = setInterval(refresh, 30_000);
    window.addEventListener('focus', refresh);
    return () => {
      observer.disconnect();
      clearInterval(timer);
      window.removeEventListener('focus', refresh);
    };
  });
</script>

<div bind:this={element} class="storage-usage" class:visible class:ready={!!usage}
  aria-busy={loading} style:--storage-duration={`${animationStore.durationMs * 2.5}ms`}>
  <div class="usage-summary">
    <span>{t('settings.localStorage')}</span>
    <span class="total" aria-live="polite">
      {usage ? format(usage.totalBytes) : t(error ? 'settings.storageUnavailable' : 'settings.storageMeasuring')}
    </span>
  </div>
  <div class="usage-track" role="img" aria-label={`${t('settings.localStorage')}: ${description || t(error ? 'settings.storageUnavailable' : 'settings.storageMeasuring')}`}>
    <div class="usage-fill">
      {#each categories as category (category.key)}
        <span class="segment {category.key}" style:flex-grow={category.bytes}
          style:display={category.bytes > 0 ? 'block' : 'none'}></span>
      {/each}
    </div>
  </div>
  <div class="usage-legend">
    {#each categories as category (category.key)}
      <div class="legend-item">
        <span class="dot {category.key}" aria-hidden="true"></span>
        <span class="label">{category.label}</span>
        <span class="value">{usage ? format(category.bytes) : '—'}</span>
      </div>
    {/each}
  </div>
  {#if error}
    <p class="storage-error" role="alert">{t('settings.storageReadError')}</p>
  {:else if usage?.totalBytes === 0}
    <p class="empty-state">{t('settings.storageEmpty')}</p>
  {/if}
</div>

<style>
  .storage-usage { display: flex; flex-direction: column; gap: 12px; min-width: 0; }
  .usage-summary { display: flex; justify-content: space-between; gap: 16px; color: var(--text-secondary); font-size: 12px; }
  .total, .value { color: var(--text-primary); font-variant-numeric: tabular-nums; }
  .total { font-weight: 600; }
  .usage-track { height: 10px; overflow: hidden; border-radius: 3px; background: var(--bg-tertiary, rgba(127,127,127,0.12)); }
  .usage-fill { height: 100%; display: flex; gap: 3px; transform-origin: left; transform: scaleX(0); opacity: 0; transition: transform var(--storage-duration) cubic-bezier(0.16,1,0.3,1), opacity var(--anim-normal, 0.2s); }
  .visible.ready .usage-fill { transform: scaleX(1); opacity: 1; }
  .segment { min-width: 0; flex-basis: 0; border-radius: 2px; }
  .app { background: var(--accent, #1db954); }
  .online { background: var(--text-secondary); }
  .cache { background: var(--warning-color, #d5a85f); }
  .usage-legend { display: flex; flex-wrap: wrap; gap: 8px 24px; }
  .legend-item { display: flex; align-items: center; gap: 7px; font-size: 12px; }
  .dot { width: 6px; height: 6px; border-radius: 2px; flex-shrink: 0; }
  .label { color: var(--text-secondary); }
  .value { font-weight: 600; }
  .storage-error, .empty-state { margin: 0; font-size: 12px; color: var(--text-secondary); }
  .storage-error { color: var(--danger-color, #e06055); }
  @media (prefers-reduced-motion: reduce) { .usage-fill { transition: none; } }
</style>
