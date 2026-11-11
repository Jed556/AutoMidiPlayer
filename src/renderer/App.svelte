<script lang="ts">
  import { onMount, tick, untrack } from 'svelte';
  import { telemetry } from './lib/infrastructure/telemetry';
  import { startRuntime } from './lib/backend/startupGate';
  import { startupProgress, nativeStartupProgress } from './lib/backend/startupProgress';
  import { libraryStore } from './lib/state/libraryStore.svelte';
  import { midiInputStore } from './lib/state/midiInputStore.svelte';
  import { playbackStore } from './lib/state/playbackStore.svelte';
  import { uiStore } from './lib/state/uiStore.svelte';
  import { themeStore } from './lib/state/themeStore.svelte';
  import { animationStore } from './lib/state/animationStore.svelte';
  import { notificationCenter } from './lib/ui/notificationCenter.svelte';
  import { settingsStore } from './lib/state/settingsStore.svelte';
  import { updateStore } from './lib/state/updateStore.svelte';
  import { t } from './lib/i18n';
  import { api, isTauri } from './lib/backend';
  import { connectMediaControls } from './lib/backend/mediaControls';
  import { createWindowCloseHandler } from './lib/backend/windowClose';

  // Layout components
  import TitleBar from './lib/components/Layout/TitleBar.svelte';
  import StartupScreen from './lib/components/Layout/StartupScreen.svelte';
  import OnboardingScreen from './lib/components/Layout/OnboardingScreen.svelte';
  import NavigationDrawer from './lib/components/Layout/NavigationDrawer.svelte';
  import GameSelectorPopup from './lib/components/Layout/GameSelectorPopup.svelte';
  import PlayerControlPanel from './lib/components/PlayerControlPanel/PlayerControlPanel.svelte';
  import ToastContainer from './lib/components/Common/ToastContainer.svelte';
  import TooltipHost from './lib/components/Common/TooltipHost.svelte';
  import DialogHost from './lib/components/Dialogs/DialogHost.svelte';
  import { viewEntrance } from './lib/utils/transitions';

  // Load only the current view; Studio and other tools load when opened.
  import { loadView } from './lib/views/viewLoader';
  import FeatureUnderConstruction from './lib/components/Common/FeatureUnderConstruction.svelte';

  let startupStatus = $state('startup.settings');
  let startupReady = $state(false);
  let showStartup = $state(true);
  let showOnboarding = $state(false);
  let onboardingReady = $state(false);
  let finishingSetup = $state(false);
  let startingNativeRuntime = $state(false);
  let clearingAppData = $state(false);
  let splashEntered: (() => void) | undefined;

  async function initializeRuntime() {
    telemetry.init();
    const updateCheck = settingsStore.settings.autoCheckUpdates
      ? updateStore.checkForUpdates(false) : Promise.resolve();
    await playbackStore.initialize(status => { startupStatus = status; });
    await midiInputStore.init();
    startupStatus = 'startup.view';
    if (currentTab !== 'discover' && currentTab !== 'new-playlist') await loadView(currentTab);
    if (updateStore.isCheckingUpdate) startupStatus = 'startup.updates';
    await updateCheck;
    await api.updates.complete().catch(error => console.warn('Could not clear update resume state:', error));
    startupReady = true;
  }

  async function finishSetup(setup: () => Promise<void>) {
    finishingSetup = true;
    startingNativeRuntime = true;
    startupStatus = 'startup.settings';
    startupProgress.beginStartup();
    const entered = new Promise<void>(resolve => { splashEntered = resolve; });
    showStartup = true;
    try {
      await entered;
      await startRuntime(stage => {
        startupProgress.advanceStartup(nativeStartupProgress[stage] ?? 2);
        startupStatus = 'startup.player';
      });
      startingNativeRuntime = false;
      startupStatus = 'startup.settings';
      settingsStore.suspendHotkeys();
      await settingsStore.refreshAutoCorrectLayouts(settingsStore.settings.autoCorrectThreshold);
      await setup();
      await initializeRuntime();
      libraryStore.selectedPlaylistId = 'songs';
      uiStore.activeTab = 'songs';
      uiStore.tabHistory = ['songs'];
      uiStore.tabHistoryIndex = 0;
      showOnboarding = false;
      onboardingReady = false;
      settingsStore.resumeHotkeys();
    } catch (error) {
      showStartup = false;
      throw error;
    } finally {
      finishingSetup = false;
      startingNativeRuntime = false;
      splashEntered = undefined;
    }
  }

  async function clearAppData() {
    if (clearingAppData || updateStore.isApplyingUpdate) return;
    clearingAppData = true;
    startupStatus = 'startup.clearingData';
    startupProgress.beginStartup();
    const entered = new Promise<void>(resolve => { splashEntered = resolve; });
    showStartup = true;
    try {
      await entered;
      // Stop persistence effects and unmount views before draining their writes.
      startupReady = false;
      await tick();
      settingsStore.suspendHotkeys();
      updateStore.configureTimer({ ...settingsStore.settings, autoCheckUpdates: false });
      await playbackStore.stop();
      await playbackStore.flushPersistence().catch(error => console.warn('Unable to flush data before clearing:', error));
      await api.settings.clearAppData();
      localStorage.clear();
      sessionStorage.clear();
    } catch (error) {
      // A partial reset still needs fresh stores. Surface the failure after reload.
      sessionStorage.setItem('appDataResetError', String(error));
    } finally {
      window.location.reload();
    }
  }

  // Native media sessions work even when the window is minimized or unfocused.
  // Publish progress once per second instead of on every engine/UI frame.
  const mediaPositionSeconds = $derived(Math.floor(playbackStore.currentTimeSeconds));
  $effect(() => {
    if (!isTauri || !startupReady || showOnboarding || clearingAppData || updateStore.isApplyingUpdate) return;
    const song = playbackStore.activeSong;
    void api.playback.syncMediaControls({
      title: song?.title ?? '',
      artist: song?.artist ?? '',
      album: song?.album ?? '',
      durationSeconds: song ? playbackStore.durationSeconds : 0,
      positionSeconds: mediaPositionSeconds,
      status: playbackStore.status,
      volume: playbackStore.volume,
    }).catch(error => console.error('Unable to sync system media controls:', error));
  });

  $effect(() => {
    if (!isTauri || !startupReady || showOnboarding || clearingAppData || updateStore.isApplyingUpdate) return;
    return connectMediaControls(api.playback,
      command => playbackStore.handleMediaControl(command),
      error => console.error('Unable to handle system media control:', error));
  });

  $effect(() => {
    if (showOnboarding && settingsStore.settings.onboardingCompleted) {
      libraryStore.selectedPlaylistId = 'songs';
      uiStore.activeTab = 'songs';
      uiStore.tabHistory = ['songs'];
      uiStore.tabHistoryIndex = 0;
    }
  });

  $effect(() => {
    if (!startupReady || clearingAppData || updateStore.isApplyingUpdate) return;
    uiStore.persistPreferences(libraryStore.selectedPlaylistId);
  });

  $effect(() => {
    if (!startupReady || clearingAppData || updateStore.isApplyingUpdate) return;
    playbackStore.status;
    playbackStore.persistSession(untrack(() => playbackStore.currentTimeSeconds));
  });

  $effect(() => {
    const title = playbackStore.windowTitle;
    if (typeof document !== 'undefined') {
      document.title = title;
    }
    if (isTauri) {
      void api.window.setTitle(title).catch(() => {});
    }
  });

  async function flushSession() {
    if (!startupReady || clearingAppData || updateStore.isApplyingUpdate) return;
    uiStore.persistPreferences(libraryStore.selectedPlaylistId);
    await playbackStore.flushPersistence();
  }

  $effect(() => {
    if (startupReady && playbackStore.activeSong) {
      // Reapply the effective offset when the saved base key or transpose mode changes.
      void playbackStore.syncKeyOffset().catch(error => console.error('Unable to apply key offset:', error));
    }
  });

  $effect(() => {
    if (updateStore.isApplyingUpdate) {
      showStartup = true;
      settingsStore.suspendHotkeys();
      return () => settingsStore.resumeHotkeys();
    }
  });

  onMount(() => {
    let disposed = false;
    let removeCloseListener: (() => void) | undefined;

    if (isTauri) {
      void import('@tauri-apps/api/window').then(async ({ getCurrentWindow }) => {
        const appWindow = getCurrentWindow();
        const unlisten = await appWindow.onCloseRequested(createWindowCloseHandler(
          api.window,
          flushSession,
          error => notificationCenter.error('Unable to save session', String(error)),
        ));
        if (disposed) unlisten();
        else removeCloseListener = unlisten;
      }).catch(error => console.error('Unable to register session close handler:', error));
    }

    // Checkpoint the playhead without writing to SQLite on every animation frame.
    const checkpoint = setInterval(() => {
      if (startupReady && !clearingAppData && !updateStore.isApplyingUpdate) playbackStore.persistSession(playbackStore.currentTimeSeconds);
    }, 5000);
    const saveOnHide = () => {
      if (document.hidden) void flushSession().catch(console.error);
    };
    document.addEventListener('visibilitychange', saveOnHide);

    async function initialize() {
      themeStore.init();
      await Promise.all([animationStore.init(), settingsStore.init()]);
      if (disposed) return;
      const resetError = sessionStorage.getItem('appDataResetError');
      sessionStorage.removeItem('appDataResetError');
      if (resetError) notificationCenter.error(t('dialogs.resetFailed'), resetError);
      showOnboarding = !settingsStore.settings.onboardingCompleted;
      if (showOnboarding) {
        onboardingReady = true;
        return;
      }
      await initializeRuntime();
    }

    // Paint the loading screen before beginning initialization.
    const startupFrame = requestAnimationFrame(() => {
      void initialize().catch((error) => {
        console.error('App initialization failed:', error);
        if (!disposed) {
          startupReady = true;
          notificationCenter.error(t('common.error'), String(error));
        }
      });
    });

    // 3. Listen to keyboard shortcuts (e.g. Space to play/pause, Alt+Arrows for navigation)
    window.addEventListener('keydown', handleGlobalKeydown);
    window.addEventListener('mouseup', handleGlobalMouseUp);
    window.addEventListener('auxclick', handleGlobalAuxClick);

    const handleWindowResize = () => {
      uiStore.updateWindowWidth(window.innerWidth);
    };
    window.addEventListener('resize', handleWindowResize);
    uiStore.updateWindowWidth(window.innerWidth);

    // 4. Listen to Electron window app-command navigation events (WM_APPCOMMAND)
    const unbindBack = window.api?.window?.onNavigateBack?.(() => {
      uiStore.goBack();
    });
    const unbindForward = window.api?.window?.onNavigateForward?.(() => {
      uiStore.goForward();
    });

    return () => {
      disposed = true;
      playbackStore.stopSoundFontMonitoring();
      cancelAnimationFrame(startupFrame);
      clearInterval(checkpoint);
      document.removeEventListener('visibilitychange', saveOnHide);
      removeCloseListener?.();
      window.removeEventListener('keydown', handleGlobalKeydown);
      window.removeEventListener('mouseup', handleGlobalMouseUp);
      window.removeEventListener('auxclick', handleGlobalAuxClick);
      window.removeEventListener('resize', handleWindowResize);
      unbindBack?.();
      unbindForward?.();
    };
  });

  function handleGlobalMouseUp(e: MouseEvent) {
    if (showStartup || showOnboarding) return;
    if (e.button === 3) {
      // Mouse Back button (XButton1)
      e.preventDefault();
      e.stopPropagation();
      uiStore.goBack();
    } else if (e.button === 4) {
      // Mouse Forward button (XButton2)
      e.preventDefault();
      e.stopPropagation();
      uiStore.goForward();
    }
  }

  function handleGlobalAuxClick(e: MouseEvent) {
    if (showStartup || showOnboarding) return;
    if (e.button === 3 || e.button === 4) {
      e.preventDefault();
      e.stopPropagation();
    }
  }

  function handleGlobalKeydown(e: KeyboardEvent) {
    if (updateStore.isApplyingUpdate) {
      if (e.key === 'F5' || (e.ctrlKey && e.key.toLowerCase() === 'r')) e.preventDefault();
      return;
    }
    if (showStartup || showOnboarding) return;
    const activeEl = document.activeElement;
    const isInput = activeEl instanceof HTMLInputElement || activeEl instanceof HTMLTextAreaElement || activeEl instanceof HTMLSelectElement;

    // Alt + Left / Right for back & forward navigation
    if (e.altKey && e.key === 'ArrowLeft') {
      e.preventDefault();
      uiStore.goBack();
      return;
    }
    if (e.altKey && e.key === 'ArrowRight') {
      e.preventDefault();
      uiStore.goForward();
      return;
    }

    // Reload page on F5 or Ctrl+R
    if (e.key === 'F5' || (e.ctrlKey && (e.key === 'r' || e.key === 'R'))) {
      e.preventDefault();
      void flushSession().then(() => window.location.reload()).catch(error => {
        notificationCenter.error('Unable to save session', String(error));
      });
      return;
    }

    if (isInput) return;

    if (e.code === 'Space') {
      e.preventDefault();
      playbackStore.togglePlay();
    }
  }

  let currentTab = $derived((uiStore.activeTab || 'songs').toLowerCase());
  let renderedView = $state<{
    tab: string;
    view?: Awaited<ReturnType<typeof loadView>>;
    error?: string;
  } | null>(null);

  $effect(() => {
    if (!startupReady) return;
    const tab = currentTab;
    let cancelled = false;

    if (tab === 'discover' || tab === 'new-playlist') {
      renderedView = { tab };
    } else {
      // Keep the current content until the import and its styles are ready.
      // Mount the destination with its entrance transition, never an empty shell.
      void loadView(tab).then(
        view => { if (!cancelled) renderedView = { tab, view }; },
        error => { if (!cancelled) renderedView = { tab, error: String(error) }; },
      );
    }

    // A slower import must not replace a more recently requested destination.
    return () => { cancelled = true; };
  });

  let isResizing = $state(false);
  let didResize = false;

  function handleResizeStart(e: MouseEvent) {
    if (e.button !== 0) return;
    e.preventDefault();
    didResize = false;
    isResizing = true;
    document.body.classList.add('col-resizing');

    const startX = e.clientX;
    const startWidth = uiStore.libraryWidth;
    const maxW = uiStore.isSmallScreen ? 280 : 385;

    function onMouseMove(moveEvent: MouseEvent) {
      const delta = moveEvent.clientX - startX;
      if (!didResize && Math.abs(delta) < 3) return;
      didResize = true;
      let target = startWidth + delta;
      if (target < 150) {
        uiStore.setLibraryWidth(80);
      } else {
        uiStore.setLibraryWidth(Math.min(Math.max(target, 220), maxW));
      }
    }

    function onMouseUp() {
      isResizing = false;
      document.body.classList.remove('col-resizing');
      window.removeEventListener('mousemove', onMouseMove);
      window.removeEventListener('mouseup', onMouseUp);
    }

    window.addEventListener('mousemove', onMouseMove);
    window.addEventListener('mouseup', onMouseUp);
  }

  function handleResizeClick() {
    if (!didResize) uiStore.toggleLibraryExpanded();
  }
</script>

{#if showStartup}
  <StartupScreen
    status={updateStore.isApplyingUpdate ? `startup.${updateStore.progress.phase}Update` : startupStatus}
    progress={updateStore.isApplyingUpdate ? updateStore.progress.progress : startingNativeRuntime ? 0 : undefined}
    entering={updateStore.isApplyingUpdate || finishingSetup || clearingAppData}
    finished={(startupReady || (onboardingReady && !finishingSetup)) && !clearingAppData && !updateStore.isApplyingUpdate}
    onentered={() => { if (finishingSetup || clearingAppData) splashEntered?.(); else updateStore.onSplashEntered(); }}
    onfinished={() => { if (!updateStore.isApplyingUpdate) showStartup = false; }}
  />
{/if}

{#if onboardingReady && showOnboarding && !updateStore.isApplyingUpdate}
  <OnboardingScreen onfinished={finishSetup} />
{/if}

{#if startupReady}
<div class="app-root" inert={showStartup || showOnboarding}>
  <!-- Frameless Title Bar -->
  <TitleBar />

  <!-- Main Application Body -->
  <div class="app-main" style="--sidebar-width: {uiStore.libraryWidth}px;">
    <!-- Adjustable Left Navigation Drawer -->
    <NavigationDrawer isResizing={isResizing} />

    <!-- Drag Resize Divider Handle with VS Code-style 3 dots -->
    <div
      class="library-resizer"
      class:is-resizing={isResizing}
      onmousedown={handleResizeStart}
      onclick={handleResizeClick}
      onkeydown={(event) => {
        if (event.key === 'Enter' || event.key === ' ') {
          event.preventDefault();
          event.stopPropagation();
          uiStore.toggleLibraryExpanded();
        }
      }}
      title="Drag / click to resize"
      role="button"
      tabindex="0"
      aria-label="Resize sidebar"
      data-tooltip-position="right"
      data-tooltip-follow="false"
    >
      <div class="resizer-line"></div>
      <div class="resizer-dots" aria-hidden="true">
        <span class="resizer-dot"></span>
        <span class="resizer-dot"></span>
        <span class="resizer-dot"></span>
      </div>
    </div>

    <!-- Active Game Selector Popup Flyout -->
    <GameSelectorPopup />

    <!-- Active View Area with Centralized Entrance Transition -->
    <main class="view-viewport" class:is-studio={renderedView?.tab === 'studio'}>
      {#if renderedView}
        {#key renderedView.tab}
          <div class="view-container" in:viewEntrance>
            {#if renderedView.tab === 'discover'}
              <FeatureUnderConstruction
                featureName={t('nav.discover')}
                description={t('discover.constructionDescription')}
              />
            {:else if renderedView.tab === 'new-playlist'}
              <FeatureUnderConstruction featureName={t('playlists.newPlaylist')} />
            {:else if renderedView.view}
              {@const View = renderedView.view.default}
              <View />
            {:else if renderedView.error}
              <p role="alert">{t('common.error')}: {renderedView.error}</p>
            {/if}
          </div>
        {/key}
      {/if}
    </main>
  </div>

  <!-- Bottom Player Bar -->
  <PlayerControlPanel />

  <!-- Global Modals & Notifications -->
  <DialogHost onreset={clearAppData} />
  <TooltipHost />
</div>
{/if}

{#if !showStartup}
  <ToastContainer />
{/if}

<style>
  .app-root {
    /* Keep all player layers below the startup screen, including the title bar. */
    isolation: isolate;
    display: flex;
    flex-direction: column;
    height: 100vh;
    width: 100vw;
    overflow: hidden;
    background: var(--bg-app, #000000);
    color: var(--text-primary);
    position: relative;
  }

  .app-main {
    display: flex;
    flex: 1;
    min-height: 0;
    position: relative;
    overflow: hidden;
    background: var(--bg-app, #000000);
    padding: 0 0 0 8px;
    gap: 0;
  }

  .library-resizer {
    width: 8px;
    height: 100%;
    cursor: col-resize;
    position: relative;
    display: flex;
    align-items: center;
    justify-content: center;
    user-select: none;
    z-index: 40;
    flex-shrink: 0;
  }

  .resizer-line {
    position: absolute;
    left: 50%;
    top: 8px;
    bottom: 8px;
    width: 2px;
    background: var(--resizer-line, rgba(255, 255, 255, 0.35));
    border-radius: 1px;
    transform: translateX(-50%) scaleY(0);
    transform-origin: center;
    opacity: 0;
    transition: transform 0.22s cubic-bezier(0.4, 0, 0.2, 1),
                opacity 0.18s ease-in 0.04s;
    pointer-events: none;
  }

  .library-resizer:hover .resizer-line,
  .library-resizer.is-resizing .resizer-line {
    transform: translateX(-50%) scaleY(1);
    opacity: 1;
    background: var(--resizer-line-hover, rgba(255, 255, 255, 0.65));
    transition: transform 0.22s cubic-bezier(0.16, 1, 0.3, 1),
                opacity 0.12s ease-out;
  }

  /* VS Code-style 3 dots centered on resize divider */
  .resizer-dots {
    position: absolute;
    top: 50%;
    left: 50%;
    transform: translate(-50%, -50%);
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    gap: 3px;
    pointer-events: none;
    padding: 4px 2px;
    border-radius: 4px;
    background: var(--bg-app, #000000);
    z-index: 2;
  }

  .resizer-dot {
    width: 3px;
    height: 3px;
    border-radius: 50%;
    background: var(--text-tertiary, rgba(255, 255, 255, 0.4));
    transition: background 0.15s ease, transform 0.15s ease;
  }

  .library-resizer:hover .resizer-dot {
    background: var(--text-primary, #ffffff);
    transform: scale(1.2);
  }

  .library-resizer.is-resizing .resizer-dot {
    background: var(--accent, #1DB954);
    transform: scale(1.3);
  }

  .view-viewport {
    flex: 1;
    min-width: 0;
    height: 100%;
    position: relative;
    overflow: hidden;
    display: flex;
    flex-direction: column;
    border-radius: 8px 0 0 8px;
    background: var(--surface-color, #121212);
  }

  .view-viewport.is-studio {
    background: transparent;
    border-radius: 0;
  }

  .view-container {
    width: 100%;
    height: 100%;
    overflow: hidden;
    display: flex;
    flex-direction: column;
    flex: 1;
  }
</style>
