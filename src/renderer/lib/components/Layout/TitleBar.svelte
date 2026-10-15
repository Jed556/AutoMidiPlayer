<script lang="ts">
  import { onMount } from 'svelte';
  import { themeStore } from '../../state/themeStore.svelte';
  import { settingsStore } from '../../state/settingsStore.svelte';
  import { uiStore } from '../../state/uiStore.svelte';
  import { libraryStore } from '../../state/libraryStore.svelte';
  import { api } from '../../backend/index';
  import { t } from '../../i18n';
  import { revealThemeFrom } from '../../utils/themeTransition';
  import FluentIcon from '../Common/FluentIcon.svelte';

  let isMaximized = $state(false);
  let hoveredControl = $state<'minimize' | 'maximize' | 'close' | null>(null);
  let focusedControl = $state<'minimize' | 'maximize' | 'close' | null>(null);
  let minimizeTrigger = $state(0);

  onMount(() => {
    let disposed = false;
    let revision = 0;
    async function syncMaximized() {
      const current = ++revision;
      const maximized = await api.window.isMaximized();
      if (!disposed && current === revision) isMaximized = maximized;
    }
    const stopListening = api.window.onResized(() => { void syncMaximized(); });
    void syncMaximized();
    return () => { disposed = true; stopListening(); };
  });

  async function handleMinimize() {
    minimizeTrigger++;
    hoveredControl = null;
    await api.window.minimize();
  }

  async function handleMaximize() {
    isMaximized = await api.window.maximize();
  }

  async function handleClose() {
    await api.window.close();
  }

  function toggleTheme(event: MouseEvent) {
    void revealThemeFrom(event.currentTarget as HTMLButtonElement, () => {
      settingsStore.update('theme', themeStore.isDark ? 'Light' : 'Dark');
    });
  }

  function openAbout() {
    uiStore.activeTab = 'about';
  }

  function openUpdate() {
    uiStore.openDialog('update');
  }
</script>

<header class="titlebar" data-tauri-drag-region>
  <div class="titlebar-drag" data-tauri-drag-region>
    <!-- App Brand -->
    <div class="brand" data-tauri-drag-region>
      <img src="./logo-fill.svg" alt="Logo" class="app-icon" data-tauri-drag-region />
      <span class="app-name" data-tauri-drag-region>{t('titleBar.appName')}</span>
    </div>
  </div>

  <!-- Non-draggable Controls Area -->
  <div class="titlebar-actions">
    <!-- Update Download Icon Button -->
    {#if settingsStore.updateAvailable}
      <button
        class="action-btn icon-btn update-btn"
        onclick={openUpdate}
        title={t('titleBar.updateTooltip', { version: settingsStore.latestVersion })}
        aria-label={t('titleBar.updateAvailable')}
      >
        <FluentIcon name="ArrowDownload" filled size={15} />
      </button>
    {/if}

    {#if libraryStore.hasFileIssues}
      <button
        type="button"
        class="action-btn icon-btn file-issues-btn"
        onclick={() => uiStore.openDialog('fileIssues')}
        title={t('fileIssues.warningTooltip', { count: libraryStore.fileIssues.length })}
        aria-label={t('fileIssues.warningTooltip', { count: libraryStore.fileIssues.length })}
      >
        <FluentIcon name="Warning" filled size={14} />
      </button>
    {/if}

    <!-- Theme Toggle -->
    <button
      class="action-btn icon-btn"
      onclick={toggleTheme}
      title={themeStore.isDark ? t('titleBar.toggleLightMode') : t('titleBar.toggleDarkMode')}
      aria-label={themeStore.isDark ? t('titleBar.toggleLightMode') : t('titleBar.toggleDarkMode')}
    >
      <FluentIcon name="Moon" filled isDark={themeStore.isDark} size={14} />
    </button>

    <!-- About -->
    <button
      class="action-btn icon-btn"
      onclick={openAbout}
      title={t('titleBar.aboutAndLicenses')}
    >
      <FluentIcon name="Info" filled size={14} />
    </button>

    <!-- Window System Buttons -->
    <div class="window-buttons">
      <button
        type="button"
        class="win-btn minimize"
        onpointerenter={() => (hoveredControl = 'minimize')}
        onpointerleave={() => (hoveredControl = null)}
        onfocus={(event) => (focusedControl = event.currentTarget.matches(':focus-visible') ? 'minimize' : null)}
        onblur={() => (focusedControl = null)}
        onclick={handleMinimize}
        aria-label={t('titleBar.minimize')}
      >
        <FluentIcon name="WindowMinimize" size={12} trigger={minimizeTrigger}
          hovered={hoveredControl === 'minimize' || focusedControl === 'minimize'} />
      </button>

      <button
        type="button"
        class="win-btn maximize"
        onpointerenter={() => (hoveredControl = 'maximize')}
        onpointerleave={() => (hoveredControl = null)}
        onfocus={(event) => (focusedControl = event.currentTarget.matches(':focus-visible') ? 'maximize' : null)}
        onblur={() => (focusedControl = null)}
        onclick={handleMaximize}
        aria-label={t('titleBar.maximize')}
      >
        <FluentIcon name="WindowMaximize" size={12} maximized={isMaximized}
          hovered={hoveredControl === 'maximize' || focusedControl === 'maximize'} />
      </button>

      <button
        type="button"
        class="win-btn close"
        onpointerenter={() => (hoveredControl = 'close')}
        onpointerleave={() => (hoveredControl = null)}
        onfocus={(event) => (focusedControl = event.currentTarget.matches(':focus-visible') ? 'close' : null)}
        onblur={() => (focusedControl = null)}
        onclick={handleClose}
        aria-label={t('titleBar.close')}
      >
        <FluentIcon name="WindowClose" size={12}
          hovered={hoveredControl === 'close' || focusedControl === 'close'} />
      </button>
    </div>
  </div>
</header>

<style>
  .titlebar {
    height: var(--titlebar-height);
    background: var(--bg-app, #000000);
    display: flex;
    align-items: center;
    justify-content: space-between;
    user-select: none;
    z-index: 1000;
    position: relative;
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

  .titlebar-actions {
    display: flex;
    align-items: center;
    height: 100%;
    -webkit-app-region: no-drag;
  }

  .action-btn {
    background: transparent;
    border: none;
    height: 100%;
    padding: 0 10px;
    display: flex;
    align-items: center;
    justify-content: center;
    color: var(--text-secondary);
    cursor: pointer;
    font-size: 12px;
    transition: background var(--transition-fast), color var(--transition-fast);
  }

  .action-btn:hover {
    background: var(--bg-hover);
    color: var(--text-primary);
  }

  .action-btn.icon-btn,
  .win-btn {
    width: 44px;
    flex-shrink: 0;
    padding: 0;
  }

  .action-btn.icon-btn {
    color: var(--text-primary);
    opacity: 0.4;
    transition: opacity var(--transition-normal, 0.22s cubic-bezier(0, 0, 0.2, 1));
  }

  .action-btn.icon-btn.file-issues-btn {
    color: var(--warning-color);
    opacity: 0.8;
  }

  .action-btn.icon-btn:hover {
    background: transparent;
    opacity: 1;
  }

  .action-btn.icon-btn:active {
    opacity: 0.75;
  }

  .action-btn.icon-btn.update-btn {
    color: #1DB954;
  }

  .window-buttons {
    display: flex;
    height: 100%;
  }

  .win-btn {
    height: 100%;
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
</style>
