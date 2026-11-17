<script lang="ts">
  import { onMount } from 'svelte';
  import { api } from '../../backend';
  import { t } from '../../i18n';
  import FluentIcon from '../Icons/FluentIcon.svelte';
  import TooltipHost from '../Common/TooltipHost.svelte';
  import ConfigUpdatesReview from './ConfigUpdatesReview.svelte';

  let { oncontinue, onskip }: { oncontinue: () => void; onskip: () => void } = $props();
  let maximized = $state(false);
  onMount(() => {
    let alive = true;
    const sync = async () => {
      const value = await api.window.isMaximized().catch(() => false);
      if (alive) maximized = value;
    };
    const stop = api.window.onResized(() => { void sync(); });
    void sync();
    return () => { alive = false; stop(); };
  });
</script>

<div class="config-updates-screen">
  <header class="setup-titlebar" data-tauri-drag-region>
    <div class="brand" data-tauri-drag-region>
      <img src="./logo-fill.svg" alt="" width="18" height="18" />
      <span>{t('titleBar.appName')}</span>
    </div>
    <div class="window-controls">
      <button type="button" aria-label={t('titleBar.minimize')} onclick={() => api.window.minimize().catch(console.warn)}>
        <FluentIcon name="WindowMinimize" size={12} />
      </button>
      <button type="button" aria-label={t(maximized ? 'titleBar.restore' : 'titleBar.maximize')}
        onclick={async () => { maximized = await api.window.maximize().catch(() => false); }}>
        <FluentIcon name="WindowMaximize" size={12} {maximized} />
      </button>
      <button type="button" class="close-window" aria-label={t('titleBar.close')} onclick={() => api.window.close().catch(console.warn)}>
        <FluentIcon name="WindowClose" size={12} />
      </button>
    </div>
  </header>
  <main aria-labelledby="config-review-title">
    <p class="setup-caption">{t('defaultConfigs.beforeStartup')}</p>
    <h1 id="config-review-title">{t('defaultConfigs.title')}</h1>
    <ConfigUpdatesReview {oncontinue} {onskip} />
  </main>
  <TooltipHost />
</div>

<style>
  .config-updates-screen { position: fixed; inset: 0; display: flex; flex-direction: column; background: var(--card-bg, #1f1f1f); color: var(--text-primary); }
  .setup-titlebar { display: flex; justify-content: space-between; align-items: center; height: var(--titlebar-height, 42px); flex-shrink: 0; user-select: none; }
  .brand { flex: 1; height: 100%; display: flex; gap: 8px; align-items: center; padding-left: 14px; font-size: 12.5px; font-weight: 600; -webkit-app-region: drag; }
  .window-controls { display: flex; height: 100%; -webkit-app-region: no-drag; }
  .window-controls button { display: grid; place-items: center; width: 44px; border: none; background: transparent; color: var(--text-secondary); cursor: pointer; }
  .window-controls button:hover { background: var(--bg-hover); color: var(--text-primary); }
  .window-controls button:focus-visible { outline: 2px solid var(--accent); outline-offset: -3px; }
  .window-controls .close-window:hover { background: #e81123; color: white; }
  main {
    display: flex; flex-direction: column; flex: 0 1 auto; min-height: 0;
    width: calc(100% - clamp(40px, 8vw, 160px)); max-width: 1760px;
    height: min(1040px, calc(100dvh - 106px)); box-sizing: border-box; margin: auto;
    --config-detail-size: clamp(12px, .8vw, 14px);
    --config-code-size: clamp(11px, .75vw, 14px);
  }
  .setup-caption { margin: 0 0 8px; font-size: 12px; color: var(--accent); }
  h1 { margin: 0 0 20px; font-size: clamp(28px, 1.5vw, 36px); line-height: 1.25; letter-spacing: -.02em; font-weight: 600; }
  @media (max-width: 700px) {
    main { width: calc(100% - 40px); height: calc(100dvh - 82px); }
    h1 { font-size: 24px; }
  }
</style>
