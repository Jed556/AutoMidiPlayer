<script lang="ts">
  import { onMount, tick } from 'svelte';
  import { themeStore, ACCENT_COLORS } from '../state/themeStore.svelte';
  import { animationStore, ANIMATION_PRESETS } from '../state/animationStore.svelte';
  import { settingsStore } from '../state/settingsStore.svelte';
  import { updateStore } from '../state/updateStore.svelte';
  import { uiStore } from '../state/uiStore.svelte';
  import { gameStore } from '../state/gameStore.svelte';
  import { notificationCenter } from '../ui/notificationCenter.svelte';
  import FluentIcon from '../components/Common/FluentIcon.svelte';
  import Button from '../components/Common/Button.svelte';
  import OverflowText from '../components/Common/OverflowText.svelte';
  import ToggleSwitch from '../components/Common/ToggleSwitch.svelte';
  import HoldNotesControl from '../components/Common/HoldNotesControl.svelte';
  import CustomScrollViewer from '../components/Common/CustomScrollViewer.svelte';
  import Dropdown from '../components/Common/Dropdown.svelte';
  import KeyPicker from '../components/Common/KeyPicker.svelte';
  import Slider from '../components/Common/Slider.svelte';
  import KeyEditControl from '../components/Settings/KeyEditControl.svelte';
  import StorageUsageChart from '../components/Settings/StorageUsageChart.svelte';
  import OverlapBehaviorPicker from '../components/Settings/OverlapBehaviorPicker.svelte';
  import RefreshIcon from '../components/Common/RefreshIcon.svelte';
  import { playbackStore } from '../state/playbackStore.svelte';
  import { api } from '../backend/index';
  import { logger } from '../infrastructure/logger';
  import { telemetry } from '../infrastructure/telemetry';
  import { revealThemeFrom } from '../utils/themeTransition';
  import { t, i18n, AVAILABLE_LOCALES, type LocaleCode } from '../i18n';
  import type { MouseStopClickOption, ThemeMode } from '$shared/types/settings';
  import type { TransposeMode } from '$shared/types/midi';

  let rightContainerRef: HTMLDivElement | null = $state(null);
  let themeControlRef: HTMLDivElement;
  let activeSectionId = $state('section-player');
  let pageWidth = $state(0);
  let isDrawerExpanded = $derived(uiStore.libraryWidth > 85);
  // Auto-clip TOC out of bounds to the left ONLY if drawer is extended on non-wide displays
  let isTocClipped = $derived(
    isDrawerExpanded && pageWidth > 0 && pageWidth < 880
  );

  // Navigation sections list for TOC
  const TOC_ITEMS = [
    { id: 'section-player', icon: 'PlayCircle', labelKey: 'settings.player' },
    { id: 'section-audio', icon: 'Headphones', labelKey: 'settings.audio' },
    { id: 'section-hotkeys', icon: 'Keyboard', labelKey: 'settings.hotkeys' },
    { id: 'section-song-defaults', icon: 'MusicNote2', labelKey: 'settings.newSongDefaults' },
    { id: 'section-notes', icon: 'MusicNote1', labelKey: 'settings.notes' },
    { id: 'section-style', icon: 'Color', labelKey: 'settings.style' },
    { id: 'section-updates', icon: 'ArrowCircleUp', labelKey: 'settings.updates' },
    { id: 'section-locations', icon: 'Location', labelKey: 'settings.locations' },
    { id: 'section-storage', icon: 'FolderSync', labelKey: 'settings.storage' },
    { id: 'section-crash-reporting', icon: 'ShieldCheckmark', labelKey: 'settings.crashReporting' },
    { id: 'section-alerts', icon: 'Warning', labelKey: 'settings.alerts' },
    { id: 'section-debug', icon: 'LockClosed', labelKey: 'settings.debug' }
  ];

  let synthModeOptions = $derived([
    { value: 'adapted', label: t('settings.synthModeGamePreview') },
    { value: 'raw', label: t('settings.synthModeFullChromatic') }
  ]);

  async function handleBrowseSoundFont() {
    try {
      const path = await api.settings.browseSoundFont();
      if (path) {
        await playbackStore.loadCustomSoundFont(path);
      }
    } catch (e) {
      console.error('Failed to browse SoundFont:', e);
    }
  }

  let isResettingSoundFont = $state(false);

  async function handleResetSoundFont() {
    if (isResettingSoundFont) return;
    isResettingSoundFont = true;
    try {
      await playbackStore.resetSoundFont();
    } catch (e) {
      console.error('Failed to reset SoundFont:', e);
    } finally {
      setTimeout(() => {
        isResettingSoundFont = false;
      }, 600);
    }
  }

  let prevVolume = $state(1);
  function handleToggleVolumeMute() {
    if (playbackStore.volume > 0) {
      prevVolume = playbackStore.volume;
      playbackStore.setVolume(0);
    } else {
      playbackStore.setVolume(prevVolume > 0 ? prevVolume : 1);
    }
  }

  let isResettingConfigs = $state(false);

  async function handleResetConfigs() {
    if (isResettingConfigs) return;
    const outcome = await uiStore.showActionDialog({
      title: t('settings.resetConfigsTitle'),
      body: t('settings.resetConfigsWarning'),
      icon: 'Warning',
      iconColor: 'var(--status-error)',
      confirmButton: { text: t('settings.resetConfigs'), variant: 'danger' },
      cancelButton: { text: t('dialogs.cancel'), variant: 'secondary' },
    });
    if (outcome !== 'Confirmed' || isResettingConfigs) return;
    isResettingConfigs = true;
    try {
      if (!await gameStore.resetConfigs()) return;
      await settingsStore.refreshAutoCorrectLayouts(settingsStore.settings.autoCorrectThreshold);
      notificationCenter.success(t('settings.resetConfigsTitle'), t('settings.resetConfigsSuccess'));
    } catch (e) {
      notificationCenter.error(t('common.error'), String(e));
    } finally {
      setTimeout(() => {
        isResettingConfigs = false;
      }, 600);
    }
  }

  // Pitch bend options (hidden for now)
  // let pitchBendOptions = $derived([
  //   { value: 'Semitone Step', label: t('settings.pitchBendSemitone') },
  //   { value: 'Continuous Quantize', label: t('settings.pitchBendContinuous') },
  //   { value: 'Ignore', label: t('settings.pitchBendIgnore') }
  // ]);

  let mouseClickOptions = $derived([
    { value: 'Disabled', label: t('settings.mouseClickDisabled') },
    { value: 'LeftClick', label: t('settings.mouseClickLeft') },
    { value: 'RightClick', label: t('settings.mouseClickRight') },
    { value: 'AnyClick', label: t('settings.mouseClickAny') }
  ]);

  let autoCleanOptions = $derived([
    { value: 'daily', label: t('settings.cleanDaily') },
    { value: 'weekly', label: t('settings.cleanWeekly') },
    { value: 'monthly', label: t('settings.cleanMonthly') },
    { value: 'never', label: t('settings.cleanNever') }
  ]);

  let maxCacheSizeOptions = $derived([
    { value: 0, label: t('settings.cacheUnlimited') },
    { value: 50, label: '50 MB' },
    { value: 100, label: '100 MB' },
    { value: 250, label: '250 MB' },
    { value: 500, label: '500 MB' },
    { value: 1000, label: '1.0 GB' },
    { value: 2000, label: '2.0 GB' },
    { value: 5000, label: '5.0 GB' }
  ]);

  let languageOptions = $derived(
    AVAILABLE_LOCALES.map(loc => ({ value: loc.code, label: loc.name }))
  );

  let themeOptions = $derived([
    { value: 'dark', label: t('settings.themeDark') },
    { value: 'light', label: t('settings.themeLight') },
    { value: 'system', label: t('settings.themeSystem') }
  ]);

  let animationTimingOptions = $derived(
    ANIMATION_PRESETS.map(p => ({
      value: p.value,
      label: p.labelKey.startsWith('settings.') ? t(p.labelKey as any) : p.labelKey
    }))
  );

  let defaultSpeedOptions = $derived([
    { value: 0.5, label: '0.5x' },
    { value: 0.75, label: '0.75x' },
    { value: 1.0, label: t('settings.speedStandard') },
    { value: 1.25, label: '1.25x' },
    { value: 1.5, label: '1.5x' },
    { value: 2.0, label: '2.0x' }
  ]);

  let defaultTransposeOptions = $derived([
    { value: 'Smart', label: t('settings.transposeSmartDesc') },
    { value: 'Ignore', label: t('settings.transposeIgnoreDesc') },
    { value: 'Up', label: t('settings.transposeUpDesc') },
    { value: 'Down', label: t('settings.transposeDownDesc') }
  ]);

  let updateCheckFrequencyOptions = $derived([
    { value: '10min', label: t('settings.freq10Min') },
    { value: 'hourly', label: t('settings.freqHourly') },
    { value: 'daily', label: t('settings.freqDaily') },
    { value: 'weekly', label: t('settings.freqWeekly') },
    { value: 'launch', label: t('settings.freqLaunch') }
  ]);

  let isProgrammaticScroll = false;
  let scrollReleaseTimer: ReturnType<typeof setTimeout> | null = null;

  // Smooth scroll click handler respecting animation setting and locking scroll tracking to prevent flicker
  function scrollToSection(id: string) {
    activeSectionId = id;
    const el = document.getElementById(id);
    if (!el || !rightContainerRef) return;

    isProgrammaticScroll = true;
    if (scrollReleaseTimer) clearTimeout(scrollReleaseTimer);

    const containerRect = rightContainerRef.getBoundingClientRect();
    const elRect = el.getBoundingClientRect();
    const targetTop = rightContainerRef.scrollTop + (elRect.top - containerRect.top) - 16;

    const behavior = animationStore.isOff ? 'auto' : 'smooth';
    rightContainerRef.scrollTo({ top: targetTop, behavior });

    // Release programmatic scroll lock after smooth scrolling has settled
    const releaseDelay = animationStore.isOff ? 40 : Math.max(500, animationStore.durationMs + 250);
    scrollReleaseTimer = setTimeout(() => {
      isProgrammaticScroll = false;
    }, releaseDelay);
  }

  // Scroll listener to update active section in TOC only during manual user scrolling
  function handleContainerScroll() {
    if (isProgrammaticScroll || !rightContainerRef) return;

    const scrollHeight = rightContainerRef.scrollHeight;
    const clientHeight = rightContainerRef.clientHeight;
    const scrollTop = rightContainerRef.scrollTop;
    const maxScroll = scrollHeight - clientHeight;

    // If at or very close to the bottom of the container, the last item is active
    if (maxScroll > 0 && maxScroll - scrollTop < 24) {
      activeSectionId = TOC_ITEMS[TOC_ITEMS.length - 1].id;
      return;
    }

    const containerRect = rightContainerRef.getBoundingClientRect();
    const scrollRatio = maxScroll > 0 ? Math.min(1, Math.max(0, scrollTop / maxScroll)) : 0;
    // Dynamic threshold: starts at 90px near the top, smoothly transitions up to 260px near the bottom
    // so small cards at the bottom properly register as active without needing an excessive empty spacer
    const threshold = containerRect.top + 90 + scrollRatio * Math.min(170, clientHeight * 0.35);

    let current = TOC_ITEMS[0].id;
    for (const item of TOC_ITEMS) {
      const el = document.getElementById(item.id);
      if (el) {
        const rect = el.getBoundingClientRect();
        if (rect.top <= threshold) {
          current = item.id;
        }
      }
    }
    activeSectionId = current;
  }

  // Release lock immediately if user manually interacts (wheel or pointer)
  function handleUserScrollInteraction() {
    if (isProgrammaticScroll) {
      isProgrammaticScroll = false;
      if (scrollReleaseTimer) clearTimeout(scrollReleaseTimer);
    }
  }

  // Debug samples
  function showSampleMessageBox() {
    notificationCenter.info(t('settings.debugMessageBox'), t('settings.toastDebugSample'));
  }

  function showSampleDialog() {
    uiStore.openDialog('licenses');
  }

  function showSampleSnackbars() {
    notificationCenter.info('Info Notification', 'Standard information message.');
    notificationCenter.success('Success Notification', 'Action completed successfully.');
    notificationCenter.warning('Warning Notification', 'Attention needed for this setting.');
    notificationCenter.error('Error Notification', 'Simulation of a recoverable failure.');
  }

  let isSendingSentryTest = $state(false);

  async function sendSentryTest() {
    if (isSendingSentryTest) return;
    isSendingSentryTest = true;
    try {
      const res = await telemetry.sendTest();
      if (res.success) {
        notificationCenter.success(t('settings.toastTitleSentry'), res.message || t('settings.toastSentrySuccess'));
      } else {
        notificationCenter.warning(t('settings.toastTitleSentry'), res.message);
      }
    } finally {
      isSendingSentryTest = false;
    }
  }

  onMount(() => {
    logger.pageVisit('Settings', 'view-mount');
    const container = rightContainerRef;
    const handleScrollEnd = () => {
      isProgrammaticScroll = false;
      if (scrollReleaseTimer) clearTimeout(scrollReleaseTimer);
    };
    container?.addEventListener('scrollend', handleScrollEnd);

    // Sync initial active game locations if games are present
    if (gameStore.games.length > 0) {
      for (const g of gameStore.games) {
        if (g.customExecutablePath && !settingsStore.settings.gameLocations[g.displayName]) {
          settingsStore.settings.gameLocations[g.displayName] = g.customExecutablePath;
        }
      }
    }

    return () => {
      container?.removeEventListener('scrollend', handleScrollEnd);
      if (scrollReleaseTimer) clearTimeout(scrollReleaseTimer);
    };
  });
</script>

<div
  class="settings-page-layout"
  class:toc-clipped={isTocClipped}
  bind:clientWidth={pageWidth}
  onwheel={(e) => {
    if (rightContainerRef && !rightContainerRef.contains(e.target as Node)) {
      const toc = (e.target as HTMLElement)?.closest?.('.settings-toc-sidebar');
      if (!toc || isTocClipped) {
        rightContainerRef.scrollTop += e.deltaY;
      }
    }
  }}
>
  <div class="settings-center-container">
    <!-- Left Column: Table of Contents (~1/3) -->
    <aside class="settings-toc-sidebar" aria-label={t('settings.tableOfContents')}>
      <CustomScrollViewer
        class="toc-scroll-viewer"
        contentClass="toc-scroll-content"
        gap="12px"
        fadeHeight={24}
        fadeRampDistance={20}
      >
        <div class="toc-header">
          <div class="toc-title-row">
            <FluentIcon name="Settings" size={20} class="toc-title-icon" />
            <h2 class="toc-title">{t('nav.settings')}</h2>
          </div>
          <span class="toc-subtext">{t('settings.quickNav')}</span>
        </div>

        <nav class="toc-nav-list">
          {#each TOC_ITEMS as item}
            {@const isActive = activeSectionId === item.id}
            <button
              type="button"
              class="toc-nav-item"
              class:active={isActive}
              onclick={() => scrollToSection(item.id)}
            >
              <span class="item-icon-wrap">
                <FluentIcon
                  name={item.id === 'section-debug' && settingsStore.settings.debugModeEnabled ? 'LockOpen' : item.icon}
                  size={16}
                />
              </span>
              <span class="item-label"><OverflowText text={t(item.labelKey as any)} hoverTarget=".toc-nav-item" /></span>
            </button>
          {/each}
        </nav>
      </CustomScrollViewer>
    </aside>

  <!-- Right Column: Cards Container (~2/3) -->
  <CustomScrollViewer
    class="settings-cards-viewer"
    contentClass="settings-cards-viewport"
    bind:scrollContainer={rightContainerRef}
    onscroll={handleContainerScroll}
    onwheel={handleUserScrollInteraction}
    onpointerdown={handleUserScrollInteraction}
    padding="16px 28px 24px 24px"
    fadeHeight={36}
  >
    <div class="cards-column">
      <!-- 1. Player Section -->
      <section id="section-player" class="settings-card">
        <div class="card-header">
          <div class="header-text">
            <h3 class="card-title">{t('settings.player')}</h3>
            <span class="card-subtext">{t('settings.playerSubtext')}</span>
          </div>
        </div>

        <div class="card-body">
          <div class="settings-grid-2col">
            <!-- Auto Enable Listen Mode -->
            <div class="switch-row">
              <ToggleSwitch
                label={t('settings.autoListenMode')}
                title={t('settings.autoListenModeTooltip')}
                checked={settingsStore.settings.autoEnableListenMode}
                onchange={(v) => settingsStore.update('autoEnableListenMode', v)}
              />
            </div>

            <!-- Key-up & Keypress Delay Row -->
            <div class="inline-controls-row">
              <div class="switch-row inline-switch">
                <ToggleSwitch
                  label={t('settings.enableKeyUp')}
                  title={t('settings.enableKeyUpTooltip')}
                  checked={settingsStore.settings.enableKeyUp}
                  onchange={(v) => settingsStore.update('enableKeyUp', v)}
                />
              </div>

              <div class="stepper-row">
                <span class="stepper-label">{t('settings.keypressDelay')}</span>
                <input
                  type="number"
                  min="0"
                  max="1000"
                  value={settingsStore.settings.keyPressDelayMs}
                  oninput={(e) => settingsStore.update('keyPressDelayMs', parseInt((e.target as HTMLInputElement).value) || 0)}
                  class="fluent-num-input"
                  title={t('settings.keypressDelayTooltip')}
                />
                <span class="input-hint">{t('settings.keypressDelayDefault')}</span>
              </div>
            </div>

            <!-- Pitch Bend Method Group (hidden for now)
            <div class="setting-group">
              <span class="setting-label">{t('settings.pitchBendMethod')}</span>
              <Dropdown
                variant="ghost"
                width="fit-content"
                value="Semitone Step"
                options={pitchBendOptions}
              />
              <p class="setting-description">
                {t('settings.pitchBendSemitone')}
              </p>
            </div>
            -->

            <div class="setting-group grid-col-full">
              <OverlapBehaviorPicker value={settingsStore.settings.overlapBehavior}
                onchange={(value) => settingsStore.update('overlapBehavior', value)}
                repressGapMs={settingsStore.settings.repressGapMs}
                onrepressgapchange={(value) => settingsStore.update('repressGapMs', value)} />
            </div>

            <!-- Auto Correct Threshold (Smart Transpose Threshold) - Spans Full Width -->
            <div class="setting-group grid-col-full">
              <div class="group-header-row">
                <span class="setting-label">{t('settings.autoCorrectThreshold')}</span>
                <span class="group-subtext">{t('settings.autoCorrectThresholdSubtext')}</span>
              </div>

              <!-- Auto Correct Threshold Slider with Custom Ticks -->
              <div style="margin: 10px 0 16px 0;">
                <Slider
                  ticks={settingsStore.autoCorrectThresholdTicks}
                  compactLabels={settingsStore.autoCorrectThresholdTicks.map(k => k === 0 ? t('settings.autoCorrectOff') : String(k))}
                  labels={settingsStore.autoCorrectThresholdTicks.map(k => k === 0 ? t('settings.autoCorrectOff') : t('settings.autoCorrectKeys', { count: k }))}
                  value={settingsStore.settings.autoCorrectThreshold}
                  onchange={(val) => settingsStore.update('autoCorrectThreshold', val)}
                  minTickGap={48}
                  proportionalSpacing
                  staggerLabels
                />
              </div>

              <p class="setting-description">
                {#if settingsStore.settings.autoCorrectThreshold === 0}
                  {t('settings.autoCorrectDisabledDesc')}
                {:else}
                  {t('settings.autoCorrectEnabledDesc', { count: settingsStore.settings.autoCorrectThreshold })}
                {/if}
              </p>
            </div>
          </div>
        </div>
      </section>

      <!-- Audio Synthesizer Section -->
      <section id="section-audio" class="settings-card">
        <div class="card-header">
          <div class="header-text">
            <h3 class="card-title">{t('settings.audio')}</h3>
            <span class="card-subtext">{t('settings.audioSubtext')}</span>
          </div>

          {#if playbackStore.customSoundFontName !== 'Default (General MIDI)'}
            <Button variant="ghost" size="icon" colorMode="danger"
              onclick={handleResetSoundFont}
              title={t('settings.resetSoundFont')}
            >
              <FluentIcon name="ArrowClockwise" loading={isResettingSoundFont} size={15} />
            </Button>
          {/if}
        </div>

        <div class="card-body">
          <div class="settings-grid-2col">
            <!-- SoundFont Selection -->
            <div class="setting-group grid-col-full">
              <span class="setting-label">{t('settings.soundFont')}</span>
              <p class="setting-description" style="margin-bottom: 8px;">
                {t('settings.soundFontDesc')}
              </p>

              <div class="soundfont-picker-row">
                <div class="soundfont-badge">
                  <FluentIcon name="MusicNote2" size={16} />
                  <span class="soundfont-label-sub">{t('settings.activeSoundFont')}</span>
                  <span class="soundfont-name"><OverflowText text={playbackStore.customSoundFontName} /></span>
                </div>
                <div class="soundfont-actions">
                  <Button variant="ghost" size="md" colorMode="default"
                    onclick={handleBrowseSoundFont}
                  >
                    <FluentIcon name="Folder" size={14} />
                    <span>{t('settings.browseSoundFont')}</span>
                  </Button>
                  {#if playbackStore.customSoundFontName !== 'Default (General MIDI)'}
                    <Button variant="ghost" size="md" colorMode="danger"
                      onclick={handleResetSoundFont}
                    >
                      <FluentIcon name="ArrowClockwise" loading={isResettingSoundFont} size={14} />
                      <span>{t('settings.resetSoundFont')}</span>
                    </Button>
                  {/if}
                </div>
              </div>
            </div>

            <!-- Synthesis Mode Group -->
            <div class="setting-group">
              <span class="setting-label">{t('settings.synthMode')}</span>
              <Dropdown
                variant="ghost"
                width="fit-content"
                value={playbackStore.synthMode}
                options={synthModeOptions}
                onchange={(v) => playbackStore.setSynthMode(v as 'adapted' | 'raw')}
              />
              <p class="setting-description">
                {#if playbackStore.synthMode === 'adapted'}
                  {t('settings.synthModeGamePreviewDesc')}
                {:else}
                  {t('settings.synthModeFullChromaticDesc')}
                {/if}
              </p>
            </div>

            <!-- Master Volume Group -->
            <div class="setting-group">
              <span class="setting-label" class:is-overshoot={playbackStore.volume > 1}>
                {t('settings.masterVolume')} ({Math.round(playbackStore.volume * 100)}%)
              </span>
              <div class="volume-slider-row">
                <button
                  type="button"
                  class="vol-mute-btn"
                  onclick={handleToggleVolumeMute}
                  aria-label={playbackStore.volume === 0 ? 'Unmute' : 'Mute'}
                  title={playbackStore.volume === 0 ? 'Unmute' : 'Mute'}
                >
                  <FluentIcon
                    name={playbackStore.volume === 0 ? 'Volume0' : playbackStore.volume < 0.33 ? 'Volume1' : playbackStore.volume < 0.66 ? 'Volume2' : 'Volume3'}
                    volume={playbackStore.volume}
                    size={16}
                  />
                </button>
                <div class="volume-slider-wrap">
                  <Slider
                    min={0}
                    max={2}
                    step={0.01}
                    areas={[
                      { min: 0, max: 1, type: 'sliding', step: 0.01 },
                      { min: 1, max: 2, type: 'sliding', step: 0.01, color: 'var(--warning-color, #F59E0B)' }
                    ]}
                    snapPoints={[1]}
                    snapRadius={0.04}
                    value={playbackStore.volume}
                    oninput={(val) => playbackStore.setVolume(val)}
                    onchange={(val) => playbackStore.setVolume(val)}
                    formatTooltip={(v) => `${Math.round(v * 100)}%`}
                  />
                </div>
              </div>
            </div>
          </div>
        </div>
      </section>

      <!-- 2. Hotkeys Section -->
      <section id="section-hotkeys" class="settings-card">
        <div class="card-header">
          <div class="header-text">
            <h3 class="card-title">{t('settings.hotkeys')}</h3>
            <span class="card-subtext">{t('settings.hotkeysSubtext')}</span>
          </div>

          <Button variant="ghost" size="icon" colorMode="danger"
            onclick={() => settingsStore.resetHotkeys()}
            title={t('settings.resetHotkeys')}
          >
            <FluentIcon name="ArrowClockwise" loading={settingsStore.isResettingHotkeys} size={15} />
          </Button>
        </div>

        <div class="card-body">
          <!-- Enable Hotkeys Toggle -->
          <div class="switch-row">
            <ToggleSwitch
              label={t('settings.enableHotkeys')}
              title={t('settings.enableHotkeysTooltip')}
              checked={settingsStore.settings.hotkeysEnabled}
              onchange={(v) => settingsStore.update('hotkeysEnabled', v)}
            />
          </div>

          <div class="card-accordion" class:expanded={settingsStore.settings.hotkeysEnabled}>
            <div class="card-accordion-inner">
              <div class="card-accordion-content hotkeys-expand-flow">
                <!-- Stop Playback on Mouse Click -->
                <div class="settings-grid-2col" style="margin-bottom: 20px;">
                  <div class="setting-group">
                    <span class="setting-label">{t('settings.stopPlaybackMouseClick')}</span>
                    <Dropdown
                      variant="ghost"
                      width="fit-content"
                      value={settingsStore.settings.mouseStopClick}
                      options={mouseClickOptions}
                      onchange={(v) => settingsStore.update('mouseStopClick', v as MouseStopClickOption)}
                    />
                  </div>
                </div>

                <!-- Hotkeys Binding List -->
                <div class="hotkeys-list">
                  {#each settingsStore.settings.hotkeys as hotkey}
                    <KeyEditControl
                      label={hotkey.label}
                      binding={hotkey}
                      onchange={(updated) => settingsStore.updateHotkey(hotkey.action, updated)}
                      onclear={() => settingsStore.clearHotkey(hotkey.action)}
                    />
                  {/each}
                </div>
              </div>
            </div>
          </div>
        </div>
      </section>

      <!-- 3. New Song Defaults Section -->
      <section id="section-song-defaults" class="settings-card">
        <div class="card-header">
          <div class="header-text">
            <h3 class="card-title">{t('settings.newSongDefaults')}</h3>
            <span class="card-subtext">{t('settings.newSongDefaultsSubtext')}</span>
          </div>

          <Button variant="ghost" size="icon" colorMode="danger"
            onclick={() => settingsStore.resetSongDefaults()}
            title={t('settings.resetSongDefaultsTooltip')}
          >
            <FluentIcon name="ArrowClockwise" loading={settingsStore.isResettingSongDefaults} size={15} />
          </Button>
        </div>

        <div class="card-body">
          <!-- Key Defaults Group -->
          <div class="setting-group">
            <span class="setting-label">{t('settings.keyDefaults')}</span>

            <div class="key-offsets-grid">
              <div class="sub-setting">
                <span class="sub-label">{t('settings.baseKeyOffset')}</span>
                <KeyPicker
                  variant="ghost"
                  value={settingsStore.settings.defaultBaseKeyOffset}
                  min={-12}
                  max={12}
                  onchange={(v) => settingsStore.update('defaultBaseKeyOffset', v)}
                />
              </div>

              <div class="sub-setting">
                <span class="sub-label">{t('settings.keyOffset')}</span>
                <KeyPicker
                  variant="ghost"
                  value={settingsStore.settings.defaultKeyOffset}
                  min={-24}
                  max={24}
                  onchange={(v) => settingsStore.update('defaultKeyOffset', v)}
                />
              </div>
            </div>

            <div class="switch-row">
              <ToggleSwitch
                label={t('settings.autoDetectBaseKey')}
                title={t('settings.autoDetectBaseKeyTooltip')}
                checked={settingsStore.settings.autoDetectBaseKey}
                onchange={(v) => settingsStore.update('autoDetectBaseKey', v)}
              />
            </div>
          </div>

          <div class="settings-grid-2col">
            <!-- Transpose Mode -->
            <div class="setting-group">
              <span class="setting-label">{t('settings.defaultTranspose')}</span>
              <Dropdown
                variant="ghost"
                width="fit-content"
                value={settingsStore.settings.defaultTranspose}
                options={defaultTransposeOptions}
                onchange={(v) => settingsStore.update('defaultTranspose', v as TransposeMode)}
              />
            </div>

            <!-- Default Playback Speed -->
            <div class="setting-group">
              <span class="setting-label">{t('settings.defaultSpeed')}</span>
              <Dropdown
                variant="ghost"
                width="fit-content"
                value={settingsStore.settings.defaultSpeed ?? 1.0}
                options={defaultSpeedOptions}
                onchange={(v) => settingsStore.update('defaultSpeed', v as number)}
              />
            </div>
          </div>
        </div>
      </section>

      <!-- 4. Notes Section -->
      <section id="section-notes" class="settings-card">
        <div class="card-header">
          <div class="header-text">
            <h3 class="card-title">{t('settings.notes')}</h3>
            <span class="card-subtext">{t('settings.notesSubtext')}</span>
          </div>

          <Button variant="ghost" size="icon" colorMode="danger"
            onclick={() => settingsStore.resetNotesDefaults()}
            title={t('settings.resetNotesDefaultsTooltip')}
          >
            <FluentIcon name="ArrowClockwise" loading={settingsStore.isResettingNotesDefaults} size={15} />
          </Button>
        </div>

        <div class="card-body">
          <div class="settings-grid-2col">
            <!-- Hold Notes -->
            <div class="switch-row">
              <HoldNotesControl
                checked={settingsStore.settings.defaultHoldNotes}
                thresholdMs={settingsStore.settings.defaultTapThresholdMs}
                onchange={(v) => settingsStore.update('defaultHoldNotes', v)}
                onthresholdchange={(v) => settingsStore.update('defaultTapThresholdMs', v)}
              />
            </div>

            <!-- Merge Notes Row -->
            <div class="inline-controls-row">
              <div class="switch-row inline-switch">
                <ToggleSwitch
                  label={t('settings.mergeNotes')}
                  checked={settingsStore.settings.defaultMergeNotes}
                  onchange={(v) => settingsStore.update('defaultMergeNotes', v)}
                />
              </div>

              <div class="stepper-row">
                <span class="stepper-label">{t('settings.toleranceMs')}</span>
                <input
                  type="number"
                  min="1"
                  max="1000"
                  value={settingsStore.settings.defaultMergeToleranceMs}
                  oninput={(e) => settingsStore.update('defaultMergeToleranceMs', parseInt((e.target as HTMLInputElement).value) || 25)}
                  disabled={!settingsStore.settings.defaultMergeNotes}
                  class="fluent-num-input"
                />
              </div>
            </div>
          </div>
        </div>
      </section>

      <!-- 5. Style Section -->
      <section id="section-style" class="settings-card">
        <div class="card-header">
          <div class="header-text">
            <h3 class="card-title">{t('settings.style')}</h3>
            <span class="card-subtext">{t('settings.styleSubtext')}</span>
          </div>
        </div>

        <div class="card-body">
          <div class="settings-grid-2col">
            <!-- Theme Selection -->
            <div class="setting-group" bind:this={themeControlRef}>
              <span class="setting-label">{t('settings.theme')}</span>
              <Dropdown
                variant="ghost"
                width="fit-content"
                value={themeStore.theme}
                options={themeOptions}
                onchange={(val) => {
                  const mode: ThemeMode = val === 'light' ? 'Light' : val === 'system' ? 'System' : 'Dark';
                  if (mode === themeStore.mode) return;
                  const origin = themeControlRef.querySelector('button') ?? themeControlRef;
                  void revealThemeFrom(origin, () => settingsStore.update('theme', mode));
                }}
              />
            </div>

            <!-- Language Selection -->
            <div class="setting-group">
              <span class="setting-label">{t('settings.language')}</span>
              <Dropdown
                variant="ghost"
                width="fit-content"
                value={i18n.currentLocale}
                options={languageOptions}
                onchange={(val) => i18n.setLocale(val as LocaleCode)}
              />
            </div>

            <!-- Accent Color Palette -->
            <div class="setting-group">
              <span class="setting-label">{t('settings.accentColor')}</span>
              <div class="color-palette">
                {#each ACCENT_COLORS as color}
                  <button
                    type="button"
                    class="color-dot"
                    class:selected={themeStore.accentColor === color.value}
                    style="background: {color.value};"
                    onclick={() => {
                      themeStore.setAccentColor(color.value);
                      settingsStore.update('accentColor', color.value);
                    }}
                    title={color.name}
                    aria-label={color.name}
                  >
                    {#if themeStore.accentColor === color.value}
                      <FluentIcon name="Checkmark" size={12} />
                    {/if}
                  </button>
                {/each}
              </div>
            </div>

            <!-- Animation Timing -->
            <div class="setting-group">
              <span class="setting-label">{t('settings.animationTiming')}</span>
              <span class="input-hint">{t('settings.animationTimingDesc')}</span>
              <div class="anim-control-wrap">
                <Dropdown
                  variant="ghost"
                  width="fit-content"
                  value={animationStore.selectedPreset}
                  options={animationTimingOptions}
                  onchange={(val) => animationStore.setPreset(val as string)}
                />
                <div class="card-accordion" class:expanded={animationStore.selectedPreset === 'custom'}>
                  <div class="card-accordion-inner">
                    <div class="card-accordion-content">
                      <div class="stepper-row anim-custom-stepper" style="margin-top: 8px;">
                        <input
                          type="number"
                          min="0.05"
                          max="2.00"
                          step="0.05"
                          value={animationStore.customSeconds}
                          oninput={(e) => animationStore.setTiming(parseFloat((e.target as HTMLInputElement).value) || 0.20, 'custom')}
                          class="fluent-num-input"
                        />
                        <span class="input-hint">{t('settings.animSeconds')}</span>
                      </div>
                    </div>
                  </div>
                </div>
              </div>
            </div>

            <!-- Tray Toggles -->
            <div class="setting-group">
              <div class="switch-row">
                <ToggleSwitch
                  label={t('settings.minimizeToTray')} checked={true}
                />
              </div>

              <div class="switch-row">
                <ToggleSwitch
                  label={t('settings.closeToTray')} checked={false}
                />
              </div>
            </div>
          </div>
        </div>
      </section>

      <!-- 6. Updates Section -->
      <section id="section-updates" class="settings-card">
        <div class="card-header">
          <div class="header-text">
            <h3 class="card-title">{t('settings.updates')}</h3>
            <span class="card-subtext">{t('settings.updatesSubtext')}</span>
          </div>

          {#if settingsStore.updateAvailable}
            <span class="badge-pill update-badge">{t('settings.newUpdateBadge')}</span>
          {/if}
        </div>

        <div class="card-body">
          <!-- Version Info Row -->
          <div class="version-display-row">
            <div class="version-info">
              <span class="version-tag">{t('settings.currentVersion', { version: settingsStore.programVersion })}</span>
              {#if settingsStore.updateAvailable}
                <FluentIcon name="TriangleRight" size={12} class="version-arrow" />
                <span class="target-version">{settingsStore.latestVersion}</span>
              {/if}
            </div>

            <div class="version-actions">
              <Button variant="ghost" size="md" colorMode="default"
                disabled={settingsStore.isCheckingUpdate}
                onclick={() => settingsStore.checkForUpdates()}
              >
                <FluentIcon name="ArrowClockwise" loading={settingsStore.isCheckingUpdate} size={14} />
                <span>{settingsStore.isCheckingUpdate ? t('settings.checkingUpdates') : t('settings.checkForUpdates')}</span>
              </Button>

              {#if settingsStore.updateAvailable}
                <Button variant="ghost" size="md" colorMode="accent"
                  onclick={() => uiStore.openDialog('update')}
                >
                  <FluentIcon name="ArrowCircleUp" size={14} />
                  <span>{t('settings.installUpdate')}</span>
                </Button>
              {/if}
            </div>
          </div>

          <!-- Update Options Grid -->
          <div class="settings-grid-2col">
            <!-- Left: Auto-Check Updates & Frequency -->
            <div class="setting-group">
              <div class="auto-check-row">
                <div class="switch-row inline-switch">
                  <ToggleSwitch
                    label={t('settings.autoCheckUpdatesToggle')}
                    checked={settingsStore.settings.autoCheckUpdates}
                    onchange={(v) => settingsStore.update('autoCheckUpdates', v)}
                  />
                </div>

                <div class="sub-setting update-frequency-setting" class:disabled={!settingsStore.settings.autoCheckUpdates}>
                  <span class="sub-label">{t('settings.updateFrequency')}</span>
                  <Dropdown
                    variant="ghost"
                    width="fit-content"
                    value={settingsStore.settings.updateCheckFrequency}
                    options={updateCheckFrequencyOptions}
                    disabled={!settingsStore.settings.autoCheckUpdates}
                    onchange={(v) => settingsStore.update('updateCheckFrequency', v as any)}
                  />
                </div>
              </div>
            </div>

            <!-- Right: Auto-Download Updates & Beta Updates -->
            <div class="setting-group">
              <div class="switch-row with-desc">
                <ToggleSwitch
                  label={t('settings.autoDownloadUpdatesToggle')}
                  description={t('settings.autoDownloadUpdatesSubtext')}
                  checked={settingsStore.settings.autoDownloadUpdates}
                  onchange={(v) => settingsStore.update('autoDownloadUpdates', v)}
                />
              </div>

              <div class="switch-row">
                <ToggleSwitch
                  label={t('settings.betaUpdatesToggle')}
                  checked={settingsStore.settings.includeBetaUpdates}
                  onchange={(v) => settingsStore.update('includeBetaUpdates', v)}
                />
              </div>
            </div>
          </div>
        </div>
      </section>

      <!-- 7. Locations Section -->
      <section id="section-locations" class="settings-card">
        <div class="card-header">
          <div class="header-text">
            <h3 class="card-title">{t('settings.locations')}</h3>
            <span class="card-subtext">{t('settings.locationsSubtext')}</span>
          </div>
        </div>

        <div class="card-body">
          <!-- Game Locations Collapsible -->
          <div class="sub-group-card">
            <button
              type="button"
              class="sub-group-header"
              onclick={() => settingsStore.toggleGameLocationsExpanded()}
            >
              <div class="sub-group-title-row">
                <span class="sub-group-title">{t('settings.gameLocations')}</span>
                <span class="sub-group-desc">{t('settings.gameLocationSubtext')}</span>
              </div>
              <span class="expand-chevron" class:rotated={settingsStore.isGameLocationsExpanded}>
                <FluentIcon name="ChevronDown" size={14} />
              </span>
            </button>

            <div class="card-accordion" class:expanded={settingsStore.isGameLocationsExpanded}>
              <div class="card-accordion-inner">
                <div class="card-accordion-content">
                  <div class="game-locations-list">
                    {#each gameStore.games as game}
                      {@const customPath = settingsStore.settings.gameLocations[game.displayName] || ''}
                      <div class="game-path-row">
                        <div class="game-meta">
                          <span class="game-name">{game.displayName} {t('settings.locationSuffix')}</span>
                          <span class="game-current-path" title={customPath || t('settings.autoDetectedPath')}>
                            <OverflowText text={customPath || t('settings.autoDetectedPath')} />
                          </span>
                        </div>

                        <div class="game-path-actions">
                          <Button variant="ghost" size="sm" colorMode="default"
                            onclick={async () => {
                              try {
                                const path = await api.settings.browseGameExecutable();
                                if (path) settingsStore.setGameLocation(game.displayName, path);
                              } catch (error) {
                                notificationCenter.error(t('settings.browseGameLocation'), String(error));
                              }
                            }}
                            title={t('settings.browseGameLocation')}
                          >
                            <FluentIcon name="Folder" size={14} />
                            <span>{t('settings.browse')}</span>
                          </Button>

                          {#if customPath}
                            <Button variant="ghost" size="icon" colorMode="danger"
                              onclick={() => settingsStore.clearGameLocation(game.displayName)}
                              title={t('settings.clearFolder')}
                            >
                              <FluentIcon name="Delete" size={14} />
                            </Button>
                          {/if}
                        </div>
                      </div>
                    {/each}
                  </div>
                </div>
              </div>
            </div>
          </div>

          <!-- MIDI Folder Group -->
          <div class="setting-group">
            <span class="setting-label">{t('settings.midiFolder')}</span>
            <span class="input-hint">{t('settings.midiFolderSubtext')}</span>

            <div class="path-browse-row">
              <div class="fluent-path-input">
                <OverflowText text={settingsStore.settings.midiFolder || t('settings.noFolderSelected')} hoverBehavior="none" showTooltip={false} />
              </div>
              <Button variant="ghost" size="md" colorMode="default" onclick={() => settingsStore.browseMidiFolder()}>
                <FluentIcon name="Folder" size={14} />
                <span>{t('settings.browse')}</span>
              </Button>
              <Button variant="ghost" size="icon" colorMode="default"
                onclick={() => settingsStore.openMidiFolder()}
                title={t('settings.openInExplorer')}
              >
                <FluentIcon name="Open" size={15} />
              </Button>
              <Button variant="ghost" size="icon" colorMode="default"
                onclick={() => settingsStore.scanMidiFolder()}
                title={t('settings.scanFolder')}
              >
                <FluentIcon name="ArrowClockwise" loading={settingsStore.isScanningFolder} size={15} />
              </Button>
              {#if settingsStore.settings.midiFolder}
                <Button variant="ghost" size="icon" colorMode="danger"
                  onclick={() => settingsStore.clearMidiFolder()}
                  title={t('settings.clearFolder')}
                >
                  <FluentIcon name="Delete" size={14} />
                </Button>
              {/if}
            </div>

            <div class="switch-row">
              <ToggleSwitch
                label={t('settings.autoScanMidiFolder')}
                checked={settingsStore.settings.autoScanMidiFolder}
                onchange={(v) => settingsStore.update('autoScanMidiFolder', v)}
              />
            </div>
          </div>

          <!-- Data Location Group -->
          <div class="setting-group">
            <span class="setting-label">{t('settings.dataLocation')}</span>
            <span class="input-hint">{t('settings.dataLocationSubtext')}</span>

            <div class="path-browse-row">
              <div class="fluent-path-input muted">
                <OverflowText text={settingsStore.dataLocationPath} hoverBehavior="none" showTooltip={false} />
              </div>
              <Button variant="ghost" size="icon" colorMode="default" onclick={() => settingsStore.openDataFolder()} title={t('settings.openDataFolder')}>
                <FluentIcon name="Folder" size={15} />
              </Button>
            </div>
          </div>

          <!-- Game & Layout Configurations Group -->
          <div class="setting-group">
            <span class="setting-label">{t('settings.gameConfigsTitle')}</span>
            <span class="input-hint">{t('settings.gameConfigsSubtext')}</span>

            <div class="path-browse-row">
              <Button variant="ghost" size="md" colorMode="default" onclick={() => gameStore.openGamesFolder()}>
                <FluentIcon name="Folder" size={14} />
                <span>{t('settings.openGamesFolder')}</span>
              </Button>
              <Button variant="ghost" size="md" colorMode="default" onclick={() => gameStore.openLayoutsFolder()}>
                <FluentIcon name="Folder" size={14} />
                <span>{t('settings.openLayoutsFolder')}</span>
              </Button>
              <Button variant="ghost" size="md" colorMode="default" onclick={handleResetConfigs} disabled={isResettingConfigs}>
                <FluentIcon name="ArrowClockwise" loading={isResettingConfigs} size={14} />
                <span>{t('settings.resetConfigs')}</span>
              </Button>
            </div>
          </div>
        </div>
      </section>

      <!-- 8. Storage Section -->
      <section id="section-storage" class="settings-card">
        <div class="card-header">
          <div class="header-text">
            <h3 class="card-title">{t('settings.storage')}</h3>
            <span class="card-subtext">{t('settings.storageSubtext')}</span>
          </div>
          <Button variant="ghost" size="icon" title={t('settings.storageRefresh')}
            disabled={settingsStore.storageLoading || settingsStore.isClearingCache}
            onclick={() => settingsStore.refreshStorage()}>
            <RefreshIcon loading={settingsStore.storageLoading} size={16} />
          </Button>
        </div>

        <div class="card-body">
          <StorageUsageChart usage={settingsStore.storageUsage} loading={settingsStore.storageLoading}
            error={settingsStore.storageError} onrefresh={() => settingsStore.refreshStorage()} />

          <!-- Cache Settings Grid -->
          <div class="storage-settings-grid">
            <!-- Auto-clean Interval -->
            <div class="setting-group">
              <span class="setting-label">{t('settings.autoCleanCache')}</span>
              <Dropdown
                variant="ghost"
                width="fit-content"
                value={settingsStore.settings.cacheAutoClean}
                options={autoCleanOptions}
                onchange={(v) => settingsStore.update('cacheAutoClean', v as any)}
              />
            </div>

            <!-- Max Cache Size -->
            <div class="setting-group">
              <span class="setting-label">{t('settings.maxCacheSize')}</span>
              <Dropdown
                variant="ghost"
                width="fit-content"
                value={settingsStore.settings.cacheMaxSizeMb}
                options={maxCacheSizeOptions}
                onchange={(v) => settingsStore.update('cacheMaxSizeMb', v as number)}
              />
            </div>
          </div>

          <div class="storage-actions">
            <Button variant="secondary" size="md"
              onclick={() => settingsStore.clearCache()}
              disabled={settingsStore.isClearingCache || settingsStore.storageLoading || !settingsStore.storageUsage?.discoverCacheBytes}
              title={t('settings.clearCacheTooltip')}
            >
              <FluentIcon name="Delete" size={14} />
              <span>{t(settingsStore.isClearingCache ? 'settings.storageClearing' : 'settings.clearCache')}</span>
            </Button>
            <Button variant="ghost" size="md" colorMode="danger"
              onclick={() => uiStore.openDialog('resetConfirm')}
              disabled={settingsStore.isClearingCache}
              title={t('settings.resetAppDataTooltip')}
            >
              <FluentIcon name="ArrowClockwise" size={14} />
              <span>{t('settings.clearData')}</span>
            </Button>
          </div>
        </div>
      </section>

      <!-- 9. Crash Reporting & Analytics Section -->
      <section id="section-crash-reporting" class="settings-card">
        <div class="card-header">
          <div class="header-text">
            <h3 class="card-title">{t('settings.crashReporting')}</h3>
            <span class="card-subtext">{t('settings.crashReportingSubtext')}</span>
          </div>
        </div>

        <div class="card-body">
          <div class="switch-row">
            <ToggleSwitch
              label={settingsStore.settings.telemetryOptIn ? t('settings.optedIn') : t('settings.optedOut')}
              checked={settingsStore.settings.telemetryOptIn}
              onchange={(v) => settingsStore.update('telemetryOptIn', v)}
            />
          </div>
        </div>
      </section>

      <!-- 10. Alerts Section -->
      <section id="section-alerts" class="settings-card">
        <div class="card-header">
          <div class="header-text">
            <h3 class="card-title">{t('settings.alerts')}</h3>
            <span class="card-subtext">{t('settings.alertsSubtext')}</span>
          </div>
        </div>

        <div class="card-body">
          <div class="switch-row">
            <ToggleSwitch
              label={t('settings.notifyAccountRisk')}
              title={t('settings.notifyAccountRiskTooltip')}
              checked={settingsStore.settings.notifyAccountRisk}
              onchange={(v) => settingsStore.update('notifyAccountRisk', v)}
            />
          </div>
        </div>
      </section>

      <!-- 11. Debug Section -->
      <section id="section-debug" class="settings-card">
        <div class="card-header">
          <div class="header-text">
            <h3 class="card-title">{t('settings.debug')}</h3>
            <span class="card-subtext">{t('settings.debugSubtext')}</span>
          </div>

          <Button variant="ghost" size="icon" colorMode="default"
            active={settingsStore.settings.debugModeEnabled}
            onclick={() => settingsStore.toggleDebugMode()}
            title={t('settings.debugModeToggleTooltip')}
          >
            <FluentIcon
              name={settingsStore.settings.debugModeEnabled ? 'LockOpen' : 'LockClosed'}
              size={16}
            />
          </Button>
        </div>

        <div class="card-body debug-card-body">
          <!-- Collapsible Locked Hint Box -->
          <div class="card-accordion" class:expanded={!settingsStore.settings.debugModeEnabled}>
            <div class="card-accordion-inner">
              <div class="card-accordion-content">
                <button
                  type="button"
                  class="locked-hint-box"
                  onclick={() => settingsStore.toggleDebugMode()}
                  title={t('settings.debugModeToggleTooltip')}
                >
                  <FluentIcon name="LockClosed" size={16} class="lock-hint-icon" />
                  <span>{t('settings.debugModeToggleTooltip')}</span>
                </button>
              </div>
            </div>
          </div>

          <!-- Collapsible Debug Content -->
          <div class="card-accordion" class:expanded={settingsStore.settings.debugModeEnabled}>
            <div class="card-accordion-inner">
              <div class="card-accordion-content debug-content-flow">
                <!-- Debug Test Buttons -->
                <div class="debug-buttons-grid">
                  <Button variant="ghost" size="sm" colorMode="default" onclick={showSampleMessageBox}>
                    <FluentIcon name="Info" size={14} />
                    <span>{t('settings.debugMessageBox')}</span>
                  </Button>

                  <Button variant="ghost" size="sm" colorMode="default" onclick={showSampleDialog}>
                    <FluentIcon name="Document" size={14} />
                    <span>{t('settings.debugDialog')}</span>
                  </Button>

                  <Button variant="ghost" size="sm" colorMode="default" onclick={showSampleSnackbars}>
                    <FluentIcon name="Sparkle" size={14} />
                    <span>{t('settings.debugSnackbars')}</span>
                  </Button>

                  <Button variant="ghost" size="sm" colorMode="default" disabled={isSendingSentryTest} onclick={sendSentryTest} title={t('settings.sentryCooldownTooltip')}>
                    <FluentIcon name="Pulse" size={14} />
                    <span>{t('settings.sendSentryTest')}</span>
                  </Button>

                  <Button variant="ghost" size="sm" colorMode="default"
                    onclick={() => uiStore.activeTab = 'icon-debug'}
                    title={t('settings.debugCheckIconsTooltip')}
                  >
                    <FluentIcon name="Eye" size={14} />
                    <span>{t('settings.debugCheckIcons')}</span>
                  </Button>
                </div>

                <!-- Crash Log Verbosity -->
                <div class="setting-group" style="margin-top: 6px;">
                  <span class="setting-label">{t('settings.crashLogVerbosity')}</span>

                  <!-- Crash Log Verbosity Slider with Step Icons & Labels -->
                  <div style="margin: 10px 0 16px 0;">
                    <Slider
                      min={0}
                      max={2}
                      step={1}
                      value={settingsStore.settings.crashLogVerbosity}
                      steps={[
                        { value: 0, label: t('settings.verbosityErrors'), icon: 'DismissCircle' },
                        { value: 1, label: t('settings.verbosityWarnings'), icon: 'Warning' },
                        { value: 2, label: t('settings.verbosityAll'), icon: 'Document' }
                      ]}
                      onchange={(val) => settingsStore.update('crashLogVerbosity', val)}
                    />
                  </div>

                  <p class="setting-description">
                    {#if settingsStore.settings.crashLogVerbosity === 0}
                      {t('settings.verbosityErrorsDesc')}
                    {:else if settingsStore.settings.crashLogVerbosity === 1}
                      {t('settings.verbosityWarningsDesc')}
                    {:else}
                      {t('settings.verbosityAllDesc')}
                    {/if}
                  </p>
                </div>

                <!-- Log Played Notes -->
                <div class="switch-row">
                  <ToggleSwitch
                    label={t('settings.logPlayedNotes')}
                    title={t('settings.logPlayedNotesTooltip')}
                    checked={settingsStore.settings.logPlayedNotes}
                    onchange={(v) => settingsStore.update('logPlayedNotes', v)}
                  />
                </div>

                <!-- Page Caching -->
                <div class="switch-row">
                  <ToggleSwitch
                    label={t('settings.pageCaching')}
                    title={t('settings.pageCachingTooltip')}
                    checked={settingsStore.settings.pageCaching}
                    onchange={(v) => settingsStore.update('pageCaching', v)}
                  />
                </div>

                <!-- Bottom Debug Actions -->
                <div class="log-actions-row">
                  <Button variant="ghost" size="md" colorMode="default"
                    onclick={() => updateStore.testSimulateVersion('7.14.3')}
                    title="Simulate v7.14.3 to test update detection against live GitHub releases"
                  >
                    <FluentIcon name="ArrowCircleUp" size={14} />
                    <span>Test Update Detection (v7.14.3)</span>
                  </Button>

                  <Button variant="ghost" size="md" colorMode="default"
                    onclick={async () => {
                      await logger.openLogsFolder();
                      notificationCenter.info(t('settings.toastTitleLogs'), t('settings.toastLogsOpened'));
                    }}
                  >
                    <FluentIcon name="Document" size={14} />
                    <span>{t('settings.openLogsFolder')}</span>
                  </Button>

                  <Button variant="ghost" size="md" colorMode="default"
                    onclick={async () => {
                      await logger.clearLogs();
                      notificationCenter.success(t('settings.toastTitleLogs'), 'Logs cleared successfully.');
                    }}
                  >
                    <FluentIcon name="DismissCircle" size={14} />
                    <span>Clear Logs</span>
                  </Button>

                  <Button variant="ghost" size="md" colorMode="danger"
                    onclick={() => settingsStore.resetAllDefaults()}
                  >
                    <FluentIcon name="ArrowClockwise" loading={settingsStore.isResettingAll} size={14} />
                    <span>{t('settings.resetAllDefaults')}</span>
                  </Button>
                </div>
              </div>
            </div>
          </div>
        </div>
      </section>

      <!-- Bottom spacer allows scrolling past the bottom so small bottom sections can align to top and be marked -->
      <div class="scroll-bottom-spacer" aria-hidden="true"></div>
    </div>
  </CustomScrollViewer>
  </div>
</div>

<style>
  .settings-page-layout {
    container-type: inline-size;
    container-name: settings-page;
    height: 100%;
    width: 100%;
    overflow: hidden;
    display: flex;
    justify-content: center;
    user-select: none;
    background: transparent;
  }

  .settings-center-container {
    display: flex;
    width: 100%;
    max-width: 1420px;
    height: 100%;
    position: relative;
    overflow: hidden;
  }

  /* --- Left Sidebar: Table of Contents (~1/3) --- */
  .settings-toc-sidebar {
    width: 260px;
    min-width: 260px;
    max-width: 260px;
    flex-shrink: 0;
    height: 100%;
    overflow: hidden;
    border-right: 1px solid var(--border-subtle, rgba(255, 255, 255, 0.08));
    background: transparent;
    padding: 16px 14px 28px 16px;
    display: flex;
    flex-direction: column;
    box-sizing: border-box;
    transition: margin-left var(--transition-normal, 0.22s cubic-bezier(0, 0, 0.2, 1)),
                opacity var(--transition-normal, 0.22s cubic-bezier(0, 0, 0.2, 1)),
                border-color var(--transition-normal, 0.22s cubic-bezier(0, 0, 0.2, 1));
  }

  :global(.toc-scroll-viewer) {
    flex: 1;
    min-height: 0;
    width: 100%;
  }

  /* Slide TOC out of bounds to the left when container space is tight because drawer is extended */
  .settings-page-layout.toc-clipped .settings-toc-sidebar {
    margin-left: -261px;
    min-width: 0;
    opacity: 0;
    pointer-events: none;
    border-right-color: transparent;
  }

  :global(.no-animations) .settings-toc-sidebar {
    transition: none !important;
  }

  .toc-header {
    padding: 4px 6px 10px 6px;
    border-bottom: 1px solid var(--border-subtle, rgba(255, 255, 255, 0.06));
    display: flex;
    flex-direction: column;
    gap: 2px;
  }

  .toc-title-row {
    display: flex;
    align-items: center;
    gap: 8px;
    color: var(--text-primary);
  }

  .toc-title {
    font-size: 16px;
    font-weight: 700;
    margin: 0;
    line-height: 1.2;
  }

  .toc-subtext {
    font-size: 11px;
    color: var(--text-tertiary);
    text-transform: uppercase;
    letter-spacing: 0.5px;
    margin-top: 4px;
  }

  .toc-nav-list {
    display: flex;
    flex-direction: column;
    gap: 2px;
  }

  .toc-nav-item {
    display: flex;
    align-items: center;
    gap: 10px;
    padding: 7px 10px 7px 12px;
    border-radius: 6px;
    background: transparent;
    border: none;
    color: var(--text-secondary);
    font-size: 12.5px;
    font-weight: 500;
    text-align: left;
    cursor: pointer;
    position: relative;
    transition: background var(--transition-fast, 0.16s), color var(--transition-fast, 0.16s);
    outline: none;
  }

  .toc-nav-item::before {
    content: '';
    position: absolute;
    left: 2px;
    top: 6px;
    bottom: 6px;
    width: 3px;
    border-radius: 2px;
    background: var(--accent, #1DB954);
    opacity: 0;
    transform: scaleY(0.3);
    transition: opacity var(--transition-fast, 0.16s ease), transform var(--transition-fast, 0.16s ease);
  }

  .toc-nav-item:hover {
    background: var(--control-fill-hover, rgba(255, 255, 255, 0.05));
    color: var(--text-primary);
  }

  .toc-nav-item.active {
    background: var(--card-bg-selected, rgba(29, 185, 84, 0.10));
    color: var(--accent, #1DB954);
    font-weight: 600;
  }

  .toc-nav-item.active::before {
    opacity: 1;
    transform: scaleY(1);
  }

  .item-icon-wrap {
    display: flex;
    align-items: center;
    justify-content: center;
    width: 20px;
    height: 20px;
    flex-shrink: 0;
  }

  .item-label {
    min-width: 0;
    flex: 1;
    white-space: nowrap;
    overflow: hidden;
  }

  /* --- Right Pane: Scrollable Cards (~2/3) --- */
  :global(.settings-cards-viewer) {
    flex: 1;
    min-width: 0;
    height: 100%;
  }

  .cards-column {
    container-type: inline-size;
    container-name: cards;
    display: flex;
    flex-direction: column;
    gap: 16px;
    width: 100%;
  }

  /* Responsive grid: default 1 single column when approaching small widths */
  .settings-grid-2col {
    display: grid;
    grid-template-columns: 1fr;
    gap: 24px;
  }

  /* Expand to 2 columns only when cards container is wide enough (>= 680px) */
  @container cards (min-width: 680px) {
    .settings-grid-2col {
      grid-template-columns: 1fr 1fr;
      align-items: start;
    }
  }

  /* Fallback for engines without container queries */
  @supports not (container-type: inline-size) {
    @media (min-width: 1080px) {
      .settings-grid-2col {
        grid-template-columns: 1fr 1fr;
        align-items: start;
      }
    }
  }

  .grid-col-full {
    grid-column: 1 / -1;
  }

  /* Spacer to allow comfortable scrolling past bottom without excessive empty space */
  .scroll-bottom-spacer {
    height: clamp(140px, 18vh, 180px);
    flex-shrink: 0;
    pointer-events: none;
  }

  /* Fluent Settings Card */
  .settings-card {
    --slider-mask-color: var(--card-bg);
    border: none;
    border-radius: 8px;
    background: var(--card-bg, rgba(255, 255, 255, 0.04));
    box-shadow: var(--card-shadow, 0 2px 6px rgba(0, 0, 0, 0.12));
    padding: 20px 22px;
    display: flex;
    flex-direction: column;
    gap: 20px;
    transition: background var(--transition-fast, 0.16s),
                box-shadow var(--transition-fast, 0.16s);
    scroll-margin-top: 16px;
  }

  .settings-card:hover {
    box-shadow: 0 4px 12px rgba(0, 0, 0, 0.16);
  }

  .card-header {
    display: flex;
    align-items: flex-start;
    justify-content: space-between;
    gap: 12px;
  }

  .header-text {
    display: flex;
    flex-direction: column;
  }

  .card-title {
    font-size: 15px;
    font-weight: 600;
    color: var(--text-primary);
    line-height: 1.25;
    margin: 0;
  }

  .card-subtext {
    display: block;
    font-size: 12px;
    color: var(--text-secondary);
    opacity: 0.8;
    margin-top: 3px;
    line-height: 1.4;
  }

  .card-body {
    display: flex;
    flex-direction: column;
    gap: 20px;
  }

  /* Setting Groups */
  .setting-group {
    display: flex;
    flex-direction: column;
    gap: 8px;
  }

  .group-header-row {
    display: flex;
    align-items: baseline;
    justify-content: space-between;
    gap: 6px 12px;
    flex-wrap: wrap;
  }

  .setting-label {
    font-size: 12.5px;
    font-weight: 600;
    color: var(--text-primary);
    user-select: none;
  }

  .group-subtext {
    font-size: 11px;
    color: var(--text-tertiary);
  }

  .setting-description {
    font-size: 11.5px;
    color: var(--text-secondary);
    opacity: 0.8;
    margin: 2px 0 0 0;
    line-height: 1.35;
  }

  /* Switch row */
  .switch-row {
    display: flex;
    align-items: center;
    justify-content: flex-start;
    padding: 6px 0;
    cursor: pointer;
  }

  .switch-row.with-desc {
    align-items: center;
  }

  .inline-controls-row {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 12px;
    flex-wrap: wrap;
    min-width: 0;
  }

  .inline-switch {
    gap: 12px;
    padding: 0;
    flex-shrink: 0;
  }

  .stepper-row {
    display: flex;
    align-items: center;
    gap: 8px;
    flex-wrap: wrap;
  }

  .stepper-label {
    font-size: 12px;
    color: var(--text-secondary);
  }

  .fluent-num-input {
    width: 76px;
    height: 30px;
    border-radius: 6px;
    background: var(--control-fill, rgba(255, 255, 255, 0.06));
    border: 1px solid transparent;
    color: var(--text-primary);
    font-size: 12.5px;
    padding: 0 8px;
    outline: none;
    transition: background var(--transition-fast), border-color var(--transition-fast), box-shadow var(--transition-fast);
  }

  .fluent-num-input:hover {
    background: var(--control-fill-hover, rgba(255, 255, 255, 0.09));
    border-color: transparent;
  }

  .fluent-num-input:focus {
    border-color: var(--accent, #1DB954);
    box-shadow: 0 0 0 2px var(--accent-glow, rgba(29, 185, 84, 0.35)), 0 0 8px var(--accent-glow, rgba(29, 185, 84, 0.2));
  }

  .input-hint {
    font-size: 11.5px;
    color: var(--text-tertiary);
  }

  /* Reserve enough room for the label, key combination, and actions in each entry. */
  .hotkeys-list {
    display: grid;
    grid-template-columns: repeat(auto-fit, minmax(min(100%, 440px), 1fr));
    gap: 8px 16px;
    margin-top: 4px;
  }

  /* Path browse row */
  .path-browse-row {
    display: flex;
    align-items: center;
    gap: 8px;
  }

  .fluent-path-input {
    display: flex;
    align-items: center;
    min-width: 0;
    flex: 1;
    height: 32px;
    padding: 0 10px;
    border-radius: 6px;
    background: var(--control-fill, rgba(255, 255, 255, 0.04));
    border: 1px solid transparent;
    color: var(--text-primary);
    font-size: 12px;
    overflow: hidden;
    user-select: none;
    pointer-events: none;
  }

  .fluent-path-input :global(.overflow-text) {
    width: 100%;
  }

  .fluent-path-input.muted {
    opacity: 0.75;
  }

/* Sub group card (e.g. Game Locations) */
  .sub-group-card {
    border: 1px solid var(--border-subtle, rgba(255, 255, 255, 0.06));
    border-radius: 6px;
    background: var(--control-fill, rgba(255, 255, 255, 0.02));
    overflow: hidden;
  }

  .sub-group-header {
    width: 100%;
    display: flex;
    align-items: center;
    justify-content: space-between;
    padding: 10px 14px;
    background: transparent;
    border: none;
    cursor: pointer;
    text-align: left;
    outline: none;
  }

  .sub-group-header:hover {
    background: var(--control-fill-hover, rgba(255, 255, 255, 0.04));
  }

  .sub-group-title-row {
    display: flex;
    flex-direction: column;
    gap: 2px;
  }

  .sub-group-title {
    font-size: 13px;
    font-weight: 600;
    color: var(--text-primary);
  }

  .sub-group-desc {
    font-size: 11px;
    color: var(--text-tertiary);
  }

  .expand-chevron {
    color: var(--text-secondary);
    display: inline-flex;
    align-items: center;
    justify-content: center;
    transition: transform var(--transition-fast, 0.2s);
  }

  .expand-chevron.rotated {
    transform: rotate(180deg);
  }

  .game-locations-list {
    padding: 0 14px 12px 14px;
    display: grid;
    grid-template-columns: 1fr;
    gap: 8px 14px;
    border-top: 1px solid var(--border-subtle, rgba(255, 255, 255, 0.04));
    margin-top: 4px;
  }

  @container cards (min-width: 720px) {
    .game-locations-list {
      grid-template-columns: 1fr 1fr;
    }
  }

  @supports not (container-type: inline-size) {
    @media (min-width: 1100px) {
      .game-locations-list {
        grid-template-columns: 1fr 1fr;
      }
    }
  }

  .game-path-row {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 12px;
    padding: 6px 8px;
    background: var(--card-bg, rgba(255, 255, 255, 0.02));
    border-radius: 4px;
  }

  .game-meta {
    display: flex;
    flex-direction: column;
    min-width: 0;
    flex: 1;
  }

  .game-name {
    font-size: 12px;
    font-weight: 500;
    color: var(--text-primary);
  }

  .game-current-path {
    font-size: 11px;
    color: var(--text-secondary);
    opacity: 0.75;
    white-space: nowrap;
    overflow: hidden;
  }

  .game-path-actions {
    display: flex;
    align-items: center;
    gap: 6px;
    flex-shrink: 0;
  }

  /* Keep cache preferences together, with cleanup actions in a separate footer. */
  .storage-settings-grid {
    display: grid;
    grid-template-columns: repeat(auto-fit, minmax(min(100%, 180px), 1fr));
    gap: 20px 24px;
    padding-top: 4px;
  }

  .storage-actions {
    display: flex;
    align-items: center;
    gap: 8px;
    flex-wrap: wrap;
    padding-top: 16px;
    border-top: 1px solid var(--border-subtle, rgba(255, 255, 255, 0.06));
  }

  /* Key Offsets Grid */
  .key-offsets-grid {
    display: grid;
    grid-template-columns: 1fr 1fr;
    gap: 12px;
  }

  .sub-setting {
    display: flex;
    flex-direction: column;
    gap: 4px;
  }

  .sub-label {
    font-size: 11.5px;
    color: var(--text-secondary);
  }

  .auto-check-row {
    display: flex;
    align-items: center;
    gap: 16px;
    flex-wrap: wrap;
    min-width: 0;
  }

  .update-frequency-setting {
    margin-bottom: 0;
    max-width: 360px;
    transition: opacity var(--transition-fast, 0.18s ease);
  }

  .update-frequency-setting.disabled {
    opacity: 0.45;
    pointer-events: none;
  }

  /* Version display */
  .version-display-row {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 12px;
    padding-bottom: 6px;
    border-bottom: 1px solid var(--border-subtle, rgba(255, 255, 255, 0.06));
  }

  .version-info {
    display: flex;
    align-items: center;
    gap: 8px;
  }

  .version-tag {
    font-size: 13.5px;
    font-weight: 600;
    color: var(--text-primary);
  }

  .target-version {
    font-size: 13px;
    font-weight: 600;
    color: var(--accent, #1DB954);
  }

  .version-actions {
    display: flex;
    align-items: center;
    gap: 8px;
  }

  .badge-pill {
    display: inline-flex;
    align-items: center;
    padding: 3px 8px;
    border-radius: 12px;
    font-size: 11px;
    font-weight: 600;
  }

  .update-badge {
    background: var(--accent, #1DB954);
    color: #ffffff;
  }

  /* Color palette */
  .color-palette {
    display: flex;
    align-items: center;
    gap: 8px;
    padding: 4px 0;
    flex-wrap: wrap;
  }

  .color-dot {
    width: 24px;
    height: 24px;
    border-radius: 50%;
    border: none;
    cursor: pointer;
    display: flex;
    align-items: center;
    justify-content: center;
    color: #FFFFFF;
    padding: 0;
    box-shadow: 0 1px 2px rgba(0, 0, 0, 0.2);
    transition: transform var(--transition-fast), filter var(--transition-fast);
  }

  .color-dot:hover:not(.selected) {
    transform: scale(1.12);
  }

  .color-dot.selected {
    box-shadow: inset 0 0 0 2px rgba(255, 255, 255, 0.6), 0 1px 4px rgba(0, 0, 0, 0.3);
  }

  .anim-control-wrap {
    display: flex;
    flex-direction: column;
    gap: 6px;
  }

  .anim-custom-stepper {
    margin-top: 2px;
  }

  /* Debug grid */
  .debug-buttons-grid {
    display: flex;
    align-items: center;
    gap: 8px;
    flex-wrap: wrap;
  }

  .log-actions-row {
    display: flex;
    align-items: center;
    gap: 10px;
    margin-top: 6px;
  }

  /* Expandable Card Accordions */
  .card-accordion {
    display: grid;
    grid-template-rows: 0fr;
    transition: grid-template-rows var(--transition-slow, 0.25s cubic-bezier(0.1, 0.9, 0.2, 1));
  }

  .card-accordion.expanded {
    grid-template-rows: 1fr;
  }

  .card-accordion-inner {
    min-height: 0;
    overflow: hidden;
    visibility: hidden;
    pointer-events: none;
    transition: visibility var(--transition-slow, 0.25s);
  }

  .card-accordion.expanded .card-accordion-inner {
    visibility: visible;
    pointer-events: auto;
  }

  .card-accordion-content {
    opacity: 0;
    transform: translateY(-8px);
    transition: opacity var(--transition-normal, 0.2s cubic-bezier(0.1, 0.9, 0.2, 1)),
                transform var(--transition-slow, 0.25s cubic-bezier(0.1, 0.9, 0.2, 1));
  }

  .card-accordion.expanded .card-accordion-content {
    opacity: 1;
    transform: translateY(0);
  }

  :global(.no-animations) .card-accordion,
  :global(.no-animations) .card-accordion-inner,
  :global(.no-animations) .card-accordion-content {
    transition: none !important;
  }

  .debug-card-body {
    gap: 0;
  }

  .debug-content-flow {
    display: flex;
    flex-direction: column;
    gap: 20px;
    padding-top: 4px;
  }

  .hotkeys-expand-flow {
    display: flex;
    flex-direction: column;
    padding-top: 4px;
  }

  .locked-hint-box {
    display: flex;
    align-items: center;
    gap: 8px;
    width: 100%;
    padding: 9px 12px;
    border-radius: 6px;
    background: var(--control-fill, rgba(255, 255, 255, 0.02));
    border: 1px dashed var(--border-subtle, rgba(255, 255, 255, 0.08));
    font-size: 12px;
    font-family: inherit;
    color: var(--text-tertiary);
    cursor: pointer;
    text-align: left;
    outline: none;
    transition: background var(--transition-fast, 0.16s),
                color var(--transition-fast, 0.16s),
                border-color var(--transition-fast, 0.16s);
  }

  .locked-hint-box:hover {
    background: var(--control-fill-hover, rgba(255, 255, 255, 0.05));
    color: var(--text-secondary);
    border-color: var(--border-medium, rgba(255, 255, 255, 0.16));
  }

  .soundfont-picker-row {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 12px;
    padding: 10px 14px;
    background: var(--control-fill, rgba(255, 255, 255, 0.03));
    border: 1px solid var(--border-subtle, rgba(255, 255, 255, 0.08));
    border-radius: 6px;
    flex-wrap: wrap;
  }

  .soundfont-badge {
    min-width: 0;
    max-width: 100%;
    flex: 1;
    display: flex;
    align-items: center;
    gap: 8px;
    font-size: 13px;
    color: var(--text-primary);
  }

  .soundfont-label-sub {
    color: var(--text-secondary);
    font-size: 12px;
  }

  .soundfont-name {
    min-width: 0;
    color: var(--accent-color, var(--accent, #1DB954));
    font-weight: 600;
  }

  .soundfont-actions {
    display: flex;
    align-items: center;
    gap: 8px;
  }

  .setting-label.is-overshoot {
    color: var(--warning-color, #F59E0B);
  }

  .volume-slider-row {
    display: flex;
    align-items: center;
    gap: 10px;
    width: 100%;
    margin: 4px 0 2px 0;
  }

  .vol-mute-btn {
    background: transparent;
    border: none;
    color: var(--text-primary);
    cursor: pointer;
    display: flex;
    align-items: center;
    justify-content: center;
    padding: 6px;
    border-radius: 4px;
    flex-shrink: 0;
    transition: opacity var(--transition-fast), color var(--transition-fast), background var(--transition-fast);
  }

  .vol-mute-btn:hover {
    background: var(--control-fill-hover, rgba(255, 255, 255, 0.08));
    color: var(--accent-color, var(--accent, #1DB954));
  }

  .vol-mute-btn:focus-visible {
    outline: 2px solid var(--accent-color, #1DB954);
    outline-offset: 2px;
  }

  .volume-slider-wrap {
    flex: 1;
    min-width: 0;
  }
</style>
