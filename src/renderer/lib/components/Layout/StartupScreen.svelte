<script lang="ts">
  import { onMount, onDestroy } from 'svelte';
  import { startupProgress, frontendStartupProgress } from '../../backend/startupProgress';
  import { api } from '../../backend';
  import { t } from '../../i18n';
  import FluentIcon from '../Common/FluentIcon.svelte';

  let { status, finished, entering = false, progress, onentered, onfinished }: {
    status: string;
    finished: boolean;
    entering?: boolean;
    progress?: number;
    onentered?: () => void;
    onfinished: () => void;
  } = $props();

  let displayedProgress = $state(startupProgress.current);
  const unsubscribe = startupProgress.subscribe((value) => { displayedProgress = value; });
  onDestroy(unsubscribe);

  $effect(() => {
    if (progress !== undefined) startupProgress.advance(progress);
    else startupProgress.advanceStartup(finished ? 100 : frontendStartupProgress[status] ?? 48);
  });

  let isMaximized = $state(false);
  let hoveredControl = $state<'minimize' | 'maximize' | 'close' | null>(null);
  let focusedControl = $state<'minimize' | 'maximize' | 'close' | null>(null);
  let minimizeTrigger = $state(0);

  onMount(() => {
    let disposed = false;
    let revision = 0;
    async function syncMaximized() {
      const current = ++revision;
      const maximized = await api.window.isMaximized().catch(() => false);
      if (!disposed && current === revision) isMaximized = maximized;
    }
    const stopListening = api.window.onResized(() => { void syncMaximized(); });
    void syncMaximized();
    return () => { disposed = true; stopListening(); };
  });

  async function handleMinimize() {
    minimizeTrigger++;
    hoveredControl = null;
    await api.window.minimize().catch(console.warn);
  }

  async function handleMaximize() {
    isMaximized = await api.window.maximize().catch(() => false);
  }

  async function handleClose() {
    await api.window.close().catch(console.warn);
  }
</script>

<div
  class="startup-screen"
  class:finished
  class:entering
  data-tauri-drag-region
  onanimationend={(event) => {
    if (event.target !== event.currentTarget) return;
    // Svelte scopes keyframe names, so use the screen's current phase.
    if (finished) onfinished();
    else if (entering) onentered?.();
  }}
>
  <header class="titlebar" data-tauri-drag-region>
    <div class="titlebar-drag" data-tauri-drag-region>
      <div class="brand" data-tauri-drag-region>
        <img src="./logo-fill.svg" alt="Logo" class="app-icon" data-tauri-drag-region />
        <span class="app-name" data-tauri-drag-region>{t('titleBar.appName')}</span>
      </div>
    </div>
    <div class="window-buttons">
      <button
        type="button"
        class="win-btn minimize"
        aria-label={t('titleBar.minimize')}
        onpointerenter={() => hoveredControl = 'minimize'}
        onpointerleave={() => hoveredControl = null}
        onfocus={event => focusedControl = event.currentTarget.matches(':focus-visible') ? 'minimize' : null}
        onblur={() => focusedControl = null}
        onclick={handleMinimize}
      >
        <FluentIcon
          name="WindowMinimize"
          size={12}
          trigger={minimizeTrigger}
          hovered={hoveredControl === 'minimize' || focusedControl === 'minimize'}
        />
      </button>
      <button
        type="button"
        class="win-btn maximize"
        aria-label={t('titleBar.maximize')}
        onpointerenter={() => hoveredControl = 'maximize'}
        onpointerleave={() => hoveredControl = null}
        onfocus={event => focusedControl = event.currentTarget.matches(':focus-visible') ? 'maximize' : null}
        onblur={() => focusedControl = null}
        onclick={handleMaximize}
      >
        <FluentIcon
          name="WindowMaximize"
          size={12}
          maximized={isMaximized}
          hovered={hoveredControl === 'maximize' || focusedControl === 'maximize'}
        />
      </button>
      <button
        type="button"
        class="win-btn close"
        aria-label={t('titleBar.close')}
        onpointerenter={() => hoveredControl = 'close'}
        onpointerleave={() => hoveredControl = null}
        onfocus={event => focusedControl = event.currentTarget.matches(':focus-visible') ? 'close' : null}
        onblur={() => focusedControl = null}
        onclick={handleClose}
      >
        <FluentIcon
          name="WindowClose"
          size={12}
          hovered={hoveredControl === 'close' || focusedControl === 'close'}
        />
      </button>
    </div>
  </header>

  <div class="startup-center" data-tauri-drag-region>
    <img class="startup-logo" src="./logo-fill.svg" alt={t('titleBar.appName')} draggable="false" />
    <div class="startup-status" role="status" aria-live="polite" aria-atomic="true">
      {#key status}
        <p class="startup-text">{t(status)}</p>
      {/key}
      <div
        class="startup-progress"
        role="progressbar"
        aria-label={t(status)}
        aria-valuemin={0}
        aria-valuemax={100}
        aria-valuenow={Math.round(displayedProgress)}
      >
        <div class="startup-progress-fill" style:transform="scaleX({displayedProgress / 100})"></div>
      </div>
    </div>
  </div>
</div>

<style>
  .startup-screen {
    position: fixed;
    inset: 0;
    z-index: 1000;
    display: flex;
    flex-direction: column;
    background: var(--card-bg, #1f1f1f);
    color: var(--text-tertiary, rgba(255, 255, 255, 0.45));
  }

  /* TitleBar Header */
  .titlebar {
    height: var(--titlebar-height, 42px);
    background: transparent;
    display: flex;
    align-items: center;
    justify-content: space-between;
    user-select: none;
    z-index: 1000;
    position: relative;
    flex-shrink: 0;
  }
  .titlebar-drag {
    flex: 1;
    height: 100%;
    display: flex;
    align-items: center;
    padding-left: 14px;
    gap: 12px;
    -webkit-app-region: drag;
    min-width: 0;
  }
  .brand {
    display: flex;
    align-items: center;
    gap: 8px;
    flex-shrink: 0;
  }
  .app-icon {
    width: 18px;
    height: 18px;
    object-fit: contain;
  }
  .app-name {
    font-size: 12.5px;
    font-weight: 600;
    letter-spacing: -0.01em;
    color: var(--text-primary);
  }
  .window-buttons {
    display: flex;
    height: 100%;
    -webkit-app-region: no-drag;
  }
  .win-btn {
    height: 100%;
    width: 44px;
    flex-shrink: 0;
    padding: 0;
    background: transparent;
    border: none;
    display: flex;
    align-items: center;
    justify-content: center;
    color: var(--text-secondary);
    cursor: pointer;
    transition: background var(--transition-fast), color var(--transition-fast);
  }
  .win-btn:hover {
    background: var(--bg-hover);
    color: var(--text-primary);
  }
  .win-btn.close:hover {
    background: #e81123;
    color: #ffffff;
  }

  .startup-center {
    flex: 1;
    min-height: 0;
    width: 100%;
    display: grid;
    place-items: center;
    position: relative;
    -webkit-app-region: drag;
  }

  .startup-logo,
  :global(:root.no-animations) .startup-logo {
    width: 88px;
    height: 88px;
    object-fit: contain;
    filter: grayscale(1);
    opacity: 0.65;
    animation: startup-pulse 1800ms cubic-bezier(0.45, 0, 0.55, 1) infinite;
    /* Startup timings also override the global "animations off" rule. */
    animation-duration: 1800ms !important;
  }

  .startup-status {
    position: absolute;
    top: calc(50% + 108px);
    left: 24px;
    right: 24px;
    text-align: center;
  }

  .startup-text,
  :global(:root.no-animations) .startup-text {
    font-size: 13px;
    line-height: 1.5;
    animation: startup-text-in 240ms ease-out both;
    animation-duration: 240ms !important;
  }

  .startup-screen.finished {
    animation: startup-out 320ms ease-in-out forwards;
    animation-duration: 320ms !important;
  }

  .startup-screen.entering:not(.finished) {
    animation: startup-in 320ms ease-in-out both;
    animation-duration: 320ms !important;
  }

  .startup-progress {
    width: min(200px, 100%);
    height: 2px;
    margin: 18px auto 0;
    overflow: hidden;
    border-radius: 2px;
    background: var(--border-subtle, rgba(255, 255, 255, 0.08));
  }

  .startup-progress-fill {
    height: 100%;
    background: currentColor;
    transform-origin: left;
  }

  @keyframes startup-pulse {
    0%, 100% { opacity: 0.65; }
    50% { opacity: 0.9; }
  }

  @keyframes startup-text-in {
    from { opacity: 0; }
    to { opacity: 1; }
  }

  @keyframes startup-out {
    from { opacity: 1; }
    to { opacity: 0; }
  }

  @keyframes startup-in {
    from { opacity: 0; }
    to { opacity: 1; }
  }

  @media (prefers-reduced-motion: reduce) {
    .startup-logo { animation: none; }
  }
</style>
