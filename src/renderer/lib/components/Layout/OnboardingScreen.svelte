<script lang="ts">
  import { canSkipOnboarding } from './onboardingPolicy';
  import { tick, untrack, onMount, onDestroy } from 'svelte';
  import type { AppSettings, ThemeMode } from '$shared/types/settings';
  import type { LegacyDataStatus } from '$shared/types/ipc';
  import { settingsStore } from '../../state/settingsStore.svelte';
  import { themeStore, ACCENT_COLORS } from '../../state/themeStore.svelte';
  import { animationStore } from '../../state/animationStore.svelte';
  import { api } from '../../backend';
  import { t, i18n, AVAILABLE_LOCALES, type LocaleCode } from '../../i18n';
  import { dialogBackdrop, dialogScaleIn, dialogScaleOut } from '../../utils/transitions';
  import Button from '../Common/Button.svelte';
  import Dropdown from '../Common/Dropdown.svelte';
  import FluentIcon from '../Icons/FluentIcon.svelte';
  import ToggleSwitch from '../Common/ToggleSwitch.svelte';
  import Checkbox from '../Common/Checkbox.svelte';
  import PathText from '../Common/PathText.svelte';
  import HoldNotesControl from '../Common/HoldNotesControl.svelte';
  import OverlapBehaviorPicker from '../Settings/OverlapBehaviorPicker.svelte';
  import ContentDialog from '../Dialogs/ContentDialog.svelte';
  import CustomScrollViewer from '../Common/CustomScrollViewer.svelte';

  let { onfinished }: { onfinished: () => Promise<void> } = $props();
  // Playback defaults and folder imports stay deferred; language and appearance preview live.
  let draft = $state<AppSettings>(untrack(() => ({ ...settingsStore.settings, telemetryOptIn: true })));
  let legacyStatus = $state<LegacyDataStatus>({
    detected: false,
    songCount: 0,
    playlistCount: 0,
    hasUserConfig: false,
  });
  let removeLegacy = $state(false);
  let legacyCheckPending = $state(true);
  let migrationChoice = $state<'migrate' | 'fresh'>('migrate');
  let isLegacyDetected = $derived(legacyStatus.detected);
  let step = $state(0);
  let targetStep = $state(0);
  let isSkipping = $state(false);
  let showSkipConfirm = $state(false);
  let mounted = $state(false);
  let busy = $state(false);
  let browsing = $state(false);
  let completed = $state(false);
  let foundSongs = $state(0);
  let error = $state('');
  let heading: HTMLHeadingElement;
  let overlay: HTMLDivElement;
  let stepViewport: HTMLDivElement;
  let scrollContainer = $state<HTMLDivElement>();
  let animating = $state(false);
  let isExiting = $state(false);
  let stepAnimations: Animation[] = [];
  let destroyed = false;
  let isMaximized = $state(false);
  let hoveredControl = $state<'minimize' | 'maximize' | 'close' | null>(null);
  let focusedControl = $state<'minimize' | 'maximize' | 'close' | null>(null);
  let minimizeTrigger = $state(0);

  onMount(() => {
    requestAnimationFrame(() => {
      if (!destroyed) mounted = true;
    });

    void api.settings.getLegacyDataStatus?.().then(status => {
      if (status && !destroyed) {
        legacyStatus = status;
        if (status.detected && status.midiFolder && !draft.midiFolder) {
          draft.midiFolder = status.midiFolder;
        }
      }
    }).catch(console.warn).finally(() => { legacyCheckPending = false; });

    let revision = 0;
    async function syncMaximized() {
      const current = ++revision;
      const maximized = await api.window.isMaximized().catch(() => false);
      if (!destroyed && current === revision) isMaximized = maximized;
    }
    const stopListening = api.window.onResized(() => { void syncMaximized(); });
    void syncMaximized();
    return () => { stopListening(); };
  });

  onDestroy(() => {
    destroyed = true;
    stepAnimations.forEach(animation => animation.cancel());
  });
  const PAGE_ICONS: Record<string, string> = {
    language: 'Globe',
    migration: 'ArrowSync',
    telemetry: 'ShieldCheckmark',
    defaults: 'MusicNote2',
    player: 'Play',
    folder: 'Folder',
    updates: 'ArrowCircleUp',
    appearance: 'Color',
    ready: 'Checkmark',
  };

  let allPages = $derived(isLegacyDetected
    ? ['language', 'migration', 'telemetry', 'defaults', 'player', 'folder', 'updates', 'appearance', 'ready']
    : ['language', 'telemetry', 'folder', 'defaults', 'player', 'updates', 'appearance', 'ready']
  );

  function isPageSkipped(pageKey: string): boolean {
    return isLegacyDetected && migrationChoice === 'migrate' && pageKey === 'folder';
  }

  let pages = $derived(allPages.filter(p => !isPageSkipped(p)));
  let page = $derived(pages[step] ?? pages[0]);
  let effectiveStep = $derived(animating ? targetStep : step);
  let canSkip = $derived(canSkipOnboarding(effectiveStep, pages.length, legacyCheckPending));

  $effect(() => {
    if (step >= pages.length) {
      step = Math.max(0, pages.length - 1);
    }
  });
  let staggerDelays = $derived.by(() => {
    const delays: string[] = [];
    for (let i = 0; i < pages.length; i++) {
      if (isSkipping && i > step && i <= targetStep) {
        delays.push(`${(i - step) * 35}ms`);
      } else {
        delays.push('0ms');
      }
    }
    return delays;
  });

  $effect(() => {
    step; completed;
    void tick().then(() => {
      heading?.focus({ preventScroll: true });
      scrollContainer?.scrollTo({ top: 0, behavior: 'instant' });
    });
  });

  async function navigate(next: number) {
    if (animating || next === step || (legacyCheckPending && next > step)) return;
    error = '';
    const duration = window.matchMedia('(prefers-reduced-motion: reduce)').matches ? 0 : animationStore.durationMs;
    const isSkip = Math.abs(next - step) > 1;
    isSkipping = isSkip;
    targetStep = next;

    if (!duration) {
      step = next;
      isSkipping = false;
      return;
    }
    const direction = next > step ? 1 : -1;
    const distance = Math.min(100, stepViewport.clientWidth * 0.2);
    animating = true;
    async function slide(from: number, to: number, entering: boolean) {
      stepAnimations = [...stepViewport.children].map(element => element.animate([
        { transform: `translate3d(${from}px, 0, 0)`, opacity: entering ? 0 : 1 },
        { transform: `translate3d(${to}px, 0, 0)`, opacity: entering ? 1 : 0 },
      ], {
        duration: duration * (entering ? 1.25 : 0.75),
        easing: entering ? 'cubic-bezier(0.16, 1, 0.3, 1)' : 'cubic-bezier(0.4, 0, 1, 1)',
        fill: 'both',
      }));
      await Promise.allSettled(stepAnimations.map(animation => animation.finished));
    }
    try {
      await slide(0, -direction * distance, false);
      if (destroyed) return;
      step = next;
      await tick();
      scrollContainer?.scrollTo({ top: 0, behavior: 'instant' });
      stepAnimations.forEach(animation => animation.cancel());
      await slide(direction * distance, 0, true);
    } finally {
      stepAnimations.forEach(animation => animation.cancel());
      if (!destroyed) {
        animating = false;
        isSkipping = false;
        await tick();
        heading?.focus({ preventScroll: true });
      }
    }
  }

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

  async function browse() {
    browsing = true;
    error = '';
    try {
      const folder = await api.settings.browseFolder(t('settings.midiFolder'));
      if (folder) draft.midiFolder = folder;
    } catch (cause) {
      error = String(cause);
    } finally {
      browsing = false;
    }
  }

  async function finish() {
    if (busy) return;
    busy = true;
    error = '';
    try {
      await api.settings.migrateLegacyData?.(isLegacyDetected && migrationChoice === 'migrate', isLegacyDetected && removeLegacy);
      foundSongs = await settingsStore.finishOnboarding(draft, {
        locale: i18n.currentLocale,
        animationTiming: animationStore.timing,
      });
      completed = true;
    } catch (cause) {
      error = String(cause);
    } finally {
      busy = false;
    }
  }

  async function openLibrary() {
    if (busy || isExiting) return;
    isExiting = true;
    const duration = window.matchMedia('(prefers-reduced-motion: reduce)').matches ? 0 : 320;
    try {
      await Promise.all([
        new Promise(resolve => setTimeout(resolve, duration)),
        onfinished()
      ]);
    } catch (cause) {
      isExiting = false;
      error = String(cause);
    }
  }

  function promptSkip() {
    if (!canSkip || busy || browsing || animating) return;
    showSkipConfirm = true;
  }

  function confirmSkip() {
    showSkipConfirm = false;
    if (!canSkip) return;
    void navigate(pages.length - 1);
  }

  function handleMouseUp(event: MouseEvent) {
    if (showSkipConfirm || busy || browsing || animating || completed) return;
    if (event.button === 3) {
      // Mouse Back button (XButton1)
      if (step > 0) {
        event.preventDefault();
        event.stopPropagation();
        void navigate(step - 1);
      }
    } else if (event.button === 4) {
      // Mouse Forward button (XButton2)
      if (step < pages.length - 1) {
        event.preventDefault();
        event.stopPropagation();
        void navigate(step + 1);
      }
    }
  }

  function handleAuxClick(event: MouseEvent) {
    if (event.button === 3 || event.button === 4) {
      event.preventDefault();
      event.stopPropagation();
    }
  }

  function handleKeydown(event: KeyboardEvent) {
    if (event.key === 'Escape' && showSkipConfirm) {
      event.preventDefault();
      event.stopPropagation();
      showSkipConfirm = false;
      return;
    }
    if (!showSkipConfirm && !busy && !browsing && !animating && !completed) {
      if (event.altKey && event.key === 'ArrowLeft') {
        if (step > 0) {
          event.preventDefault();
          event.stopPropagation();
          void navigate(step - 1);
          return;
        }
      } else if (event.altKey && event.key === 'ArrowRight') {
        if (step < pages.length - 1) {
          event.preventDefault();
          event.stopPropagation();
          void navigate(step + 1);
          return;
        }
      }
    }
    trapFocus(event);
  }

  function trapFocus(event: KeyboardEvent) {
    if (event.key !== 'Tab' || !overlay || showSkipConfirm) return;
    const elements = [...overlay.querySelectorAll<HTMLElement>('button:not(:disabled), input:not(:disabled), select:not(:disabled), [tabindex="0"]')]
      .filter(element => element.getClientRects().length > 0);
    const first = elements[0];
    const last = elements.at(-1);
    if (!first || !last) { event.preventDefault(); return; }
    if (event.shiftKey && (document.activeElement === first || document.activeElement === heading)) {
      event.preventDefault(); last.focus();
    } else if (!event.shiftKey && document.activeElement === last) {
      event.preventDefault(); first.focus();
    }
  }
</script>

<svelte:window
  onkeydown={handleKeydown}
  onmouseup={handleMouseUp}
  onauxclick={handleAuxClick}
/>

<div
  class="onboarding-screen"
  class:finished={isExiting}
  bind:this={overlay}
  role="dialog"
  aria-modal="true"
  aria-labelledby="onboarding-title"
  tabindex="-1"
  style:--sweep-duration={`${animationStore.isOff ? 0 : Math.max(260, Math.round(animationStore.durationMs * 1.5))}ms`}
  style:--shrink-duration={`${animationStore.isOff ? 0 : Math.max(340, Math.round(animationStore.durationMs * 1.4))}ms`}
  style:--fade-duration={`${animationStore.isOff ? 0 : Math.max(200, Math.round(animationStore.durationMs * 0.8))}ms`}
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

  <!-- Pinned Top Progress Bar: Fixed and consistent in one place, not moving with steps -->
  <div class="top-progress-area">
    <div class="top-progress-inner">
      <div class="progress-caption">
        <span class="caption-welcome">{isLegacyDetected ? t('onboarding.welcomeBack') : t('onboarding.welcome')}</span>
        <span class="caption-step">{t('onboarding.step', { current: effectiveStep + 1, total: pages.length })}</span>
      </div>
      <ol class="step-progress" aria-label={t('onboarding.progress')}>
        {#each allPages as pageKey (pageKey)}
          {@const isSkipped = isPageSkipped(pageKey)}
          {@const activeIndex = pages.indexOf(pageKey)}
          <li
            class="step-progress-item"
            class:shrunk={isSkipped}
            class:completed={!isSkipped && activeIndex < effectiveStep}
            class:active={!isSkipped && activeIndex === effectiveStep}
            class:unreached={!isSkipped && activeIndex > effectiveStep}
            class:clickable={!isSkipped && activeIndex < effectiveStep && !busy && !animating}
            aria-current={!isSkipped && activeIndex === effectiveStep ? 'step' : undefined}
            aria-hidden={isSkipped ? 'true' : undefined}
          >
            <button
              type="button"
              class="step-bar-button"
              tabindex={isSkipped ? -1 : undefined}
              disabled={isSkipped || activeIndex >= effectiveStep || busy || animating}
              onclick={() => { if (!isSkipped && activeIndex >= 0) navigate(activeIndex); }}
              aria-label={t(`onboarding.${pageKey}Title`)}
              title={!isSkipped && activeIndex < effectiveStep ? t(`onboarding.${pageKey}Title`) : undefined}
            >
              <div class="linear-bar-track">
                <div
                  class="linear-bar-fill"
                  style:transform={`scaleX(${mounted && !isSkipped && activeIndex <= effectiveStep ? 1 : 0})`}
                  style:transition-delay={!isSkipped && activeIndex >= 0 ? (staggerDelays[activeIndex] ?? '0ms') : '0ms'}
                ></div>
              </div>
            </button>
            <span class="sr-only">{t(`onboarding.${pageKey}Title`)}</span>
          </li>
        {/each}
      </ol>
    </div>
  </div>

  <div class="setup-layout">
    <div class="setup-content">
      <div
        class="step-viewport"
        class:is-animating={animating}
        bind:this={stepViewport}
        inert={animating}
      >
        <div class="page-heading">
          <div class="page-icon"><FluentIcon name={PAGE_ICONS[page] || 'Settings'} size={26} /></div>
          <h1 id="onboarding-title" tabindex="-1" bind:this={heading}>{t(completed ? 'onboarding.completeTitle' : `onboarding.${pages[step]}Title`)}</h1>
          <p>{t(completed ? 'onboarding.completeDescription' : `onboarding.${pages[step]}Description`)}</p>
        </div>

        <CustomScrollViewer
          bind:scrollContainer
          class="page-scroll-viewer"
          contentClass="page-body"
          padding="6px 8px 12px 4px"
          fadeHeight={32}
          fadeRampDistance={28}
          fadeTop={true}
          fadeBottom={true}
        >
          {#if page === 'language'}
            <div class="language-field field"><span>{t('settings.language')}</span>
              <Dropdown portalMenu ariaLabel={t('settings.language')} value={i18n.currentLocale} options={AVAILABLE_LOCALES.map(locale => ({ value: locale.code, label: locale.name }))} onchange={value => i18n.setLocale(value as LocaleCode, false)} />
            </div>
            <p class="note">{t('onboarding.systemLanguage')}</p>
          {:else if page === 'migration'}
            <div class="migration-options" role="radiogroup" aria-label={t('onboarding.migrationTitle')}>
              <button
                type="button"
                class="migration-card"
                class:selected={migrationChoice === 'migrate'}
                onclick={() => {
                  migrationChoice = 'migrate';
                  if (legacyStatus.midiFolder && !draft.midiFolder) {
                    draft.midiFolder = legacyStatus.midiFolder;
                  }
                }}
                role="radio"
                aria-checked={migrationChoice === 'migrate'}
              >
                <div class="card-icon">
                  <FluentIcon name="ArrowSync" size={24} />
                </div>
                <div class="card-content">
                  <div class="card-header-row">
                    <span class="card-title">{t('onboarding.migrationOptionMigrate')}</span>
                    <span class="badge recommended">{t('onboarding.migrationBadgeRecommended')}</span>
                  </div>
                  <p class="card-desc">
                    {t('onboarding.migrationOptionMigrateDesc')}
                  </p>
                  <div class="detected-stats">
                    {#if legacyStatus.songCount > 0}
                      <span class="stat-pill"><FluentIcon name="MusicNote2" size={14} />{t('onboarding.migrationSongsFound', { count: legacyStatus.songCount })}</span>
                    {/if}
                    {#if legacyStatus.playlistCount > 0}
                      <span class="stat-pill"><FluentIcon name="AppsListDetail" size={14} />{t('onboarding.migrationPlaylistsFound', { count: legacyStatus.playlistCount })}</span>
                    {/if}
                    {#if legacyStatus.midiFolder}
                      <span class="stat-pill folder-pill"><FluentIcon name="Folder" size={14} /><PathText path={legacyStatus.midiFolder} /></span>
                    {/if}
                  </div>
                </div>
              </button>

              <button
                type="button"
                class="migration-card"
                class:selected={migrationChoice === 'fresh'}
                onclick={() => {
                  migrationChoice = 'fresh';
                }}
                role="radio"
                aria-checked={migrationChoice === 'fresh'}
              >
                <div class="card-icon">
                  <FluentIcon name="Sparkle" size={24} />
                </div>
                <div class="card-content">
                  <div class="card-header-row">
                    <span class="card-title">{t('onboarding.migrationOptionFresh')}</span>
                  </div>
                  <p class="card-desc">
                    {t('onboarding.migrationOptionFreshDesc')}
                  </p>
                </div>
              </button>
            </div>
            <div class="legacy-cleanup">
              <Checkbox bind:checked={removeLegacy} label={t('onboarding.removeLegacy')} description={t('onboarding.removeLegacyHelp')} />
            </div>
          {:else if page === 'telemetry'}
            <div class="setting-row">
              <ToggleSwitch label={t('onboarding.telemetryChoice')} description={t('onboarding.telemetryHelp')}
                checked={draft.telemetryOptIn} onchange={value => draft.telemetryOptIn = value} />
            </div>
            <p class="note">{t('onboarding.changeLater')}</p>
          {:else if page === 'folder'}
            <div class="folder-choice">
              <FluentIcon name="Folder" size={24} />
              <div class="folder-text"><span>{t('settings.midiFolder')}</span><strong>{draft.midiFolder || t('settings.noFolderSelected')}</strong></div>
              <div class="folder-actions">
                <Button variant="secondary" disabled={browsing} onclick={browse}>{t('onboarding.browse')}</Button>
                {#if draft.midiFolder}<Button variant="ghost" colorMode="danger" size="icon" icon="Delete" title={t('settings.clearFolder')} disabled={browsing} onclick={() => draft.midiFolder = ''} />{/if}
              </div>
            </div>
            <div class="setting-row"><ToggleSwitch label={t('onboarding.watchFolder')} checked={draft.autoScanMidiFolder} onchange={value => draft.autoScanMidiFolder = value} /></div>
            <p class="note"><FluentIcon name="Info" size={16} />{t('onboarding.deferredScan')}</p>
          {:else if page === 'defaults'}
            <div class="fields-grid">
              <label>{t('settings.baseKeyOffset')}<input type="number" min="-12" max="12" value={draft.defaultBaseKeyOffset} oninput={event => { if (event.currentTarget.validity.valid && Number.isInteger(event.currentTarget.valueAsNumber)) draft.defaultBaseKeyOffset = event.currentTarget.valueAsNumber; }} onblur={event => event.currentTarget.value = String(draft.defaultBaseKeyOffset)} /></label>
              <label>{t('settings.keyOffset')}<input type="number" min="-24" max="24" value={draft.defaultKeyOffset} oninput={event => { if (event.currentTarget.validity.valid && Number.isInteger(event.currentTarget.valueAsNumber)) draft.defaultKeyOffset = event.currentTarget.valueAsNumber; }} onblur={event => event.currentTarget.value = String(draft.defaultKeyOffset)} /></label>
            </div>
            <div class="setting-row"><ToggleSwitch label={t('settings.autoDetectBaseKey')} checked={draft.autoDetectBaseKey} onchange={value => draft.autoDetectBaseKey = value} /></div>
            <div class="fields-grid">
              <div class="field"><span>{t('settings.defaultTranspose')}</span><Dropdown portalMenu ariaLabel={t('settings.defaultTranspose')} value={draft.defaultTranspose} options={['Smart', 'Ignore', 'Up', 'Down'].map(mode => ({value: mode, label: t(`settings.transpose${mode}Desc`)}))} onchange={value => draft.defaultTranspose = value} /></div>
              <div class="field"><span>{t('settings.defaultSpeed')}</span><Dropdown portalMenu ariaLabel={t('settings.defaultSpeed')} value={draft.defaultSpeed} options={[0.25, 0.5, 0.75, 1, 1.25, 1.5, 1.75, 2].map(speed => ({value: speed, label: `${speed}×`}))} onchange={value => draft.defaultSpeed = value} /></div>
            </div>
          {:else if page === 'player'}
            <div class="setting-row"><ToggleSwitch label={t('settings.autoListenMode')} description={t('settings.autoListenModeTooltip')} checked={draft.autoEnableListenMode} onchange={value => draft.autoEnableListenMode = value} /></div>
            <div class="overlap-setting">
              <OverlapBehaviorPicker value={draft.overlapBehavior} onchange={value => draft.overlapBehavior = value}
                repressGapMs={draft.repressGapMs} onrepressgapchange={value => draft.repressGapMs = value} />
            </div>
            <div class="setting-row"><HoldNotesControl checked={draft.defaultHoldNotes} thresholdMs={draft.defaultTapThresholdMs} onchange={value => draft.defaultHoldNotes = value} onthresholdchange={value => draft.defaultTapThresholdMs = value} /></div>
            <div class="setting-row merge-row">
              <ToggleSwitch label={t('settings.mergeNotes')} checked={draft.defaultMergeNotes} onchange={value => draft.defaultMergeNotes = value} />
              <label class="inline-field">{t('settings.toleranceMs')}<input type="number" min="1" max="1000" disabled={!draft.defaultMergeNotes} value={draft.defaultMergeToleranceMs} oninput={event => { if (event.currentTarget.validity.valid && Number.isInteger(event.currentTarget.valueAsNumber)) draft.defaultMergeToleranceMs = event.currentTarget.valueAsNumber; }} onblur={event => event.currentTarget.value = String(draft.defaultMergeToleranceMs)} /></label>
            </div>
          {:else if page === 'updates'}
            <div class="setting-row"><ToggleSwitch label={t('settings.autoCheckUpdatesToggle')} checked={draft.autoCheckUpdates} onchange={value => draft.autoCheckUpdates = value} /></div>
            <div class="frequency-field field"><span>{t('settings.updateFrequency')}</span><Dropdown portalMenu ariaLabel={t('settings.updateFrequency')} disabled={!draft.autoCheckUpdates} value={draft.updateCheckFrequency} options={[{value: '10min', label: t('settings.freq10Min')}, {value: 'hourly', label: t('settings.freqHourly')}, {value: 'daily', label: t('settings.freqDaily')}]} onchange={value => draft.updateCheckFrequency = value} /></div>
            <div class="setting-row"><ToggleSwitch label={t('settings.autoDownloadUpdatesToggle')} description={t('settings.autoDownloadUpdatesSubtext')} checked={draft.autoDownloadUpdates} onchange={value => draft.autoDownloadUpdates = value} /></div>
            <div class="setting-row"><ToggleSwitch label={t('settings.betaUpdatesToggle')} checked={draft.includeBetaUpdates} onchange={value => draft.includeBetaUpdates = value} /></div>
          {:else if page === 'appearance'}
            <div class="fields-grid">
              <div class="field"><span>{t('settings.theme')}</span><Dropdown portalMenu ariaLabel={t('settings.theme')} value={draft.theme} options={['System', 'Light', 'Dark'].map(mode => ({value: mode, label: t(`settings.theme${mode}`)}))} onchange={value => { draft.theme = value as ThemeMode; themeStore.setTheme(draft.theme); }} /></div>
              <div class="field"><span>{t('settings.animationTiming')}</span><Dropdown portalMenu ariaLabel={t('settings.animationTiming')} value={animationStore.timing} options={[...new Set([0, 0.1, 0.2, 0.3, 0.5, animationStore.timing])].sort((a,b) => a-b).map(timing => ({value: timing, label: timing === 0 ? t('settings.animOff') : `${timing} ${t('settings.animSeconds')}`}))} onchange={value => animationStore.setTiming(value, undefined, false)} /></div>
            </div>
            <div class="accent-field"><span>{t('settings.accentColor')}</span><div class="color-palette">
              {#each ACCENT_COLORS as color}<button type="button" class="color-dot" style:background={color.value} aria-label={color.name} aria-pressed={draft.accentColor === color.value} onclick={() => { draft.accentColor = color.value; themeStore.setAccentColor(color.value); }}>{#if draft.accentColor === color.value}<FluentIcon name="Checkmark" size={16} />{/if}</button>{/each}
            </div></div>
            <div class="setting-row"><ToggleSwitch label={t('settings.minimizeToTray')} checked={true} /></div>
            <div class="setting-row"><ToggleSwitch label={t('settings.closeToTray')} checked={false} /></div>
          {:else}
            <dl class="summary">
              {#if isLegacyDetected}
                <div>
                  <dt>{t('onboarding.migrationModeLabel')}</dt>
                  <dd class="migration-mode-badge" class:is-migrate={migrationChoice === 'migrate'}>
                    {migrationChoice === 'migrate' ? t('onboarding.migrationModeMigrated') : t('onboarding.migrationModeFresh')}
                    {#if migrationChoice === 'migrate' && legacyStatus.songCount > 0}
                      · {t('onboarding.foundSongs', { count: legacyStatus.songCount })}
                    {/if}
                  </dd>
                </div>
              {/if}
              <div><dt>{t('settings.midiFolder')}</dt><dd class="folder-summary">{draft.midiFolder || t('onboarding.noFolder')}</dd></div>
              {#if completed && draft.midiFolder}<div><dt>{t('onboarding.library')}</dt><dd class="song-count">{t('onboarding.foundSongs', { count: foundSongs })}</dd></div>{/if}
              <div><dt>{t('onboarding.telemetryTitle')}</dt><dd>{t(draft.telemetryOptIn ? 'settings.optedIn' : 'settings.optedOut')}</dd></div>
              <div><dt>{t('settings.defaultTranspose')}</dt><dd>{t(`settings.transpose${draft.defaultTranspose}Desc`)} · {draft.defaultSpeed}×</dd></div>
              <div><dt>{t('settings.updates')}</dt><dd>{t(draft.autoCheckUpdates ? 'onboarding.automaticUpdates' : 'onboarding.manualUpdates')}</dd></div>
              <div><dt>{t('settings.language')}</dt><dd>{AVAILABLE_LOCALES.find(locale => locale.code === i18n.currentLocale)?.name}</dd></div>
              <div><dt>{t('settings.theme')}</dt><dd>{t(`settings.theme${draft.theme}`)}</dd></div>
            </dl>
            <p class="note">{t(completed ? 'onboarding.nextDestination' : draft.midiFolder ? 'onboarding.finishScan' : 'onboarding.addFolderLater')}</p>
          {/if}
        </CustomScrollViewer>
      </div>
      {#if error}<p class="setup-error" role="alert">{error}</p>{/if}
    </div>
  </div>

  <!-- Pinned Bottom Actions Area: Stays consistently in one place across all steps -->
  <footer class="bottom-actions-area">
    <div class="bottom-actions-inner">
      <div class="actions-left">
        {#if !completed}
          <Button
            variant="secondary"
            disabled={step === 0 || busy || browsing || animating}
            onclick={() => navigate(step - 1)}
          >
            <FluentIcon name="ChevronLeft" size={16} />
            {t('onboarding.previous')}
          </Button>
          <div class="skip-availability" class:available={canSkip} inert={!canSkip} aria-hidden={!canSkip}>
            <Button
              variant="secondary"
              class="skip-btn"
              disabled={!canSkip || busy || browsing || animating}
              onclick={promptSkip}
            >
              {t('onboarding.skip')}
            </Button>
          </div>
        {/if}
      </div>

      <div class="actions-right">
        {#if completed}
          <Button variant="accent" disabled={busy} onclick={openLibrary}>
            {t('onboarding.openLibrary')}
            <FluentIcon name="ArrowRight" size={16} />
          </Button>
        {:else if step === pages.length - 1}
          <Button variant="accent" disabled={busy || animating} onclick={finish}>
            <FluentIcon name="Checkmark" loading={busy} size={16} />
            {t(busy ? 'onboarding.finishing' : 'onboarding.finish')}
          </Button>
        {:else}
          <Button variant="accent" disabled={legacyCheckPending || browsing || animating} onclick={() => navigate(step + 1)}>
            {t('onboarding.next')}
            <FluentIcon name="ChevronRight" size={16} />
          </Button>
        {/if}
      </div>
    </div>
  </footer>

  {#if showSkipConfirm}
    <div
      class="dialog-backdrop"
      transition:dialogBackdrop
      onclick={(e) => { if (e.target === e.currentTarget) showSkipConfirm = false; }}
      role="presentation"
    >
      <div
        class="dialog-box dialog-skipConfirm"
        in:dialogScaleIn
        out:dialogScaleOut
        role="dialog"
        aria-modal="true"
        aria-labelledby="skip-confirm-title"
      >
        <ContentDialog
          title={t('onboarding.skipConfirmTitle')}
          icon="Info"
          onClose={() => showSkipConfirm = false}
        >
          <p class="skip-dialog-text">
            {t('onboarding.skipConfirmDescription')}
          </p>

          {#snippet footer()}
            <Button variant="secondary" onclick={() => showSkipConfirm = false}>
              {t('dialogs.cancel')}
            </Button>
            <Button variant="accent" onclick={confirmSkip}>
              {t('onboarding.skip')}
            </Button>
          {/snippet}
        </ContentDialog>
      </div>
    </div>
  {/if}
</div>

<style>
  .onboarding-screen {
    position: fixed;
    inset: 0;
    z-index: 900;
    display: flex;
    flex-direction: column;
    background: var(--card-bg, #1f1f1f);
    color: var(--text-primary);
  }

  .onboarding-screen.finished {
    animation: onboarding-out 320ms ease-in-out forwards;
    animation-duration: 320ms !important;
    pointer-events: none;
  }

  @keyframes onboarding-out {
    from { opacity: 1; }
    to { opacity: 0; }
  }

  /* Window TitleBar Header */
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
  .win-btn.close {
    opacity: 0.7;
    transition: background var(--transition-fast), color var(--transition-fast), opacity var(--transition-fast);
  }
  .win-btn.close:hover,
  .win-btn.close:focus-visible {
    background: #e81123;
    color: #ffffff;
    opacity: 1;
  }

  /* Pinned Top Progress Bar */
  .top-progress-area {
    flex-shrink: 0;
    width: 100%;
    padding: 18px 0 32px;
    user-select: none;
  }
  .top-progress-inner {
    width: 100%;
    max-width: 620px;
    margin: 0 auto;
    padding: 0 28px;
    box-sizing: border-box;
    display: flex;
    flex-direction: column;
    gap: 8px;
  }
  .progress-caption {
    display: flex;
    justify-content: space-between;
    align-items: center;
    gap: 12px;
    font-size: 11px;
    color: var(--text-tertiary);
    font-weight: 500;
  }
  .caption-welcome {
    letter-spacing: 0.01em;
  }
  .caption-step {
    font-variant-numeric: tabular-nums;
    color: var(--text-secondary);
  }
  .step-progress {
    display: flex;
    padding: 0;
    margin: 0;
    list-style: none;
    width: 100%;
  }
  .step-progress-item {
    flex: 1 1 0%;
    min-width: 0;
    margin-right: 6px;
    overflow: hidden;
    display: flex;
    transition:
      flex var(--shrink-duration, 380ms) cubic-bezier(0.16, 1, 0.3, 1),
      max-width var(--shrink-duration, 380ms) cubic-bezier(0.16, 1, 0.3, 1),
      margin-right var(--shrink-duration, 380ms) cubic-bezier(0.16, 1, 0.3, 1),
      opacity var(--fade-duration, 240ms) ease;
  }
  .step-progress-item:last-child {
    margin-right: 0;
  }
  .step-progress-item.shrunk {
    flex: 0 0 0px !important;
    max-width: 0px !important;
    margin-right: 0px !important;
    opacity: 0 !important;
    pointer-events: none !important;
    visibility: hidden;
    transition:
      flex var(--shrink-duration, 380ms) cubic-bezier(0.16, 1, 0.3, 1),
      max-width var(--shrink-duration, 380ms) cubic-bezier(0.16, 1, 0.3, 1),
      margin-right var(--shrink-duration, 380ms) cubic-bezier(0.16, 1, 0.3, 1),
      opacity var(--fade-duration, 180ms) ease,
      visibility 0s var(--shrink-duration, 380ms);
  }

  @media (prefers-reduced-motion: reduce) {
    .step-progress-item,
    .step-progress-item.shrunk {
      transition: none !important;
    }
  }
  .step-bar-button {
    flex: 1;
    min-width: 0;
    padding: 5px 0;
    margin: -5px 0;
    background: transparent;
    border: none;
    cursor: default;
    display: flex;
    align-items: center;
    outline: none;
  }
  .step-progress-item.clickable .step-bar-button {
    cursor: pointer;
  }
  .step-bar-button:focus-visible .linear-bar-track {
    outline: 2px solid var(--accent);
    outline-offset: 2px;
  }
  .linear-bar-track {
    width: 100%;
    height: 4px;
    background: var(--border-subtle, rgba(255, 255, 255, 0.12));
    border-radius: 999px;
    overflow: hidden;
    position: relative;
    transition: background var(--transition-fast, 0.15s ease);
  }
  .step-progress-item.clickable:hover .linear-bar-track {
    background: var(--border-medium, rgba(255, 255, 255, 0.22));
  }
  .linear-bar-fill {
    position: absolute;
    inset: 0;
    background: var(--accent);
    border-radius: inherit;
    transform-origin: left center;
    transition: transform var(--sweep-duration, 360ms) cubic-bezier(0.16, 1, 0.3, 1);
    will-change: transform;
  }
  /* Sweep highlight on active / sweeping step */
  .linear-bar-fill::after {
    content: '';
    position: absolute;
    top: 0;
    right: 0;
    bottom: 0;
    width: 18px;
    background: linear-gradient(90deg, transparent, rgba(255, 255, 255, 0.45));
    border-radius: inherit;
    opacity: 0;
    transition: opacity 160ms ease;
    pointer-events: none;
  }
  .step-progress-item.active .linear-bar-fill::after {
    opacity: 1;
  }

  /* Setup Layout & Step Viewport */
  .setup-layout {
    flex: 1;
    min-height: 0;
    overflow: hidden;
    padding: 0 28px;
    display: flex;
    flex-direction: column;
    justify-content: center;
    align-items: center;
  }
  .setup-content {
    display: flex;
    flex-direction: column;
    justify-content: center;
    width: 100%;
    max-width: 620px;
    max-height: 100%;
    min-width: 0;
    min-height: 0;
    margin: auto auto;
  }
  .step-viewport {
    display: flex;
    flex-direction: column;
    justify-content: center;
    max-height: 100%;
    min-height: 0;
    position: relative;
    box-sizing: border-box;
    /* Extend wider so the gradient fade zone falls outside the content bounds */
    margin: 0 -28px;
    padding: 0 28px;
    /* Soft left gradient; right side is solid black so the scrollbar is completely unmasked and on top */
    mask-image: linear-gradient(to right, transparent 0px, #000 28px, #000 100%);
    -webkit-mask-image: linear-gradient(to right, transparent 0px, #000 28px, #000 100%);
  }
  .step-viewport.is-animating {
    mask-image: linear-gradient(to right, transparent 0px, #000 28px, #000 calc(100% - 28px), transparent 100%);
    -webkit-mask-image: linear-gradient(to right, transparent 0px, #000 28px, #000 calc(100% - 28px), transparent 100%);
  }
  .page-heading, .setup-error { flex-shrink: 0; }
  .page-heading { display: grid; grid-template-columns: 26px minmax(0, 1fr); gap: 10px 16px; align-items: start; }
  .page-icon { grid-column: 1; grid-row: 1 / 3; display: flex; color: var(--accent); margin-top: 4px; }
  h1 { grid-column: 2; font-size: clamp(24px, 4vw, 30px); line-height: 1.2; letter-spacing: -0.6px; margin: 0; font-weight: 650; outline: none; }
  .page-heading p { grid-column: 2; margin: 0; color: var(--text-secondary); font-size: 13px; line-height: 1.6; max-width: 540px; }
  :global(.page-scroll-viewer) {
    flex: 0 1 auto !important;
    max-height: 100%;
    min-height: 0;
    width: 100%;
    margin-top: 18px;
  }
  :global(.page-scroll-viewer .page-body) {
    display: flex;
    flex-direction: column;
    width: 100%;
    box-sizing: border-box;
  }
  .setting-row { padding: 16px 0; border-bottom: 1px solid var(--border-subtle, #ffffff12); }
  .setting-row:first-child { padding-top: 0; }
  .setting-row:last-child { border-bottom: none; }
  .fields-grid { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 20px; padding: 12px 0; }
  label, .field { display: flex; flex-direction: column; min-width: 0; gap: 8px; color: var(--text-secondary); font-size: 12px; }
  input { font: inherit; font-size: 13px; color: var(--text-primary); background: var(--bg-tertiary, #ffffff0a); border: 1px solid var(--border-subtle, #ffffff12); border-radius: 5px; padding: 9px 12px; min-width: 0; width: 100%; box-sizing: border-box; }

  input:focus-visible, button:focus-visible { outline: 2px solid var(--accent); outline-offset: 3px; }
  input:disabled { opacity: 0.45; }
  .folder-choice { display: flex; align-items: center; gap: 16px; padding: 18px; border: 1px solid var(--border-subtle, #ffffff12); border-radius: 8px; color: var(--text-secondary); }
  .folder-text { flex: 1; min-width: 0; display: flex; flex-direction: column; gap: 6px; font-size: 11px; }
  .folder-text strong { font-size: 13px; font-weight: 500; color: var(--text-primary); overflow-wrap: anywhere; }
  .folder-actions { display: flex; align-items: center; gap: 8px; flex-shrink: 0; }
  .note { display: flex; align-items: flex-start; gap: 8px; margin: 16px 0 0; font-size: 12px; line-height: 1.6; color: var(--text-tertiary); }
  .overlap-setting { margin-top: 16px; }
  .merge-row { display: flex; align-items: center; justify-content: space-between; gap: 16px; flex-wrap: wrap; }
  .inline-field { flex-direction: row; align-items: center; gap: 10px; }
  .inline-field input { width: 82px; }
  .frequency-field { max-width: 240px; margin: 12px 0; }
  .language-field { max-width: 320px; }
  .accent-field { display: flex; flex-direction: column; gap: 14px; padding: 16px 0; font-size: 12px; color: var(--text-secondary); }
  .color-palette { display: flex; flex-wrap: wrap; gap: 12px; }
  .color-dot { display: grid; place-items: center; width: 32px; height: 32px; border: 0; border-radius: 50%; color: #fff; cursor: pointer; }
  .color-dot[aria-pressed="true"] { outline: 2px solid var(--text-primary); outline-offset: 3px; }
  /* Migration Options Cards */
  .skip-availability { opacity: 0; pointer-events: none; transition: opacity var(--transition-normal, 0.22s); }
  .skip-availability.available { opacity: 1; pointer-events: auto; }
  .legacy-cleanup { margin-top: 18px; }
  .migration-options {
    display: flex;
    flex-direction: column;
    gap: 12px;
    width: 100%;
    margin-bottom: 8px;
  }
  .migration-card {
    display: flex;
    align-items: flex-start;
    gap: 14px;
    padding: 16px 18px;
    background: var(--card-bg-subtle, rgba(255, 255, 255, 0.04));
    border: 1px solid var(--border-subtle, rgba(255, 255, 255, 0.09));
    border-radius: 8px;
    color: var(--text-primary);
    cursor: pointer;
    text-align: left;
    outline: none;
    transition: background var(--transition-fast, 0.15s ease),
                border-color var(--transition-fast, 0.15s ease),
                box-shadow var(--transition-fast, 0.15s ease);
    width: 100%;
    box-sizing: border-box;
  }
  .migration-card:hover {
    background: var(--bg-hover, rgba(255, 255, 255, 0.07));
    border-color: var(--border-medium, rgba(255, 255, 255, 0.18));
  }
  .migration-card.selected {
    background: var(--accent-subtle, rgba(29, 185, 84, 0.08));
    border-color: var(--accent, #1DB954);
    box-shadow: 0 0 0 1px var(--accent, #1DB954);
  }
  .migration-card:focus-visible {
    outline: 2px solid var(--accent, #1DB954);
    outline-offset: 2px;
  }
  .card-icon {
    margin-top: 2px;
    color: var(--accent, #1DB954);
    flex-shrink: 0;
    display: flex;
    align-items: center;
    justify-content: center;
  }
  .card-content {
    flex: 1;
    min-width: 0;
    display: flex;
    flex-direction: column;
    gap: 4px;
  }
  .card-header-row {
    display: flex;
    align-items: center;
    gap: 8px;
  }
  .card-title {
    font-size: 14px;
    font-weight: 600;
    color: var(--text-primary);
  }
  .card-desc {
    margin: 0;
    font-size: 12.5px;
    line-height: 1.45;
    color: var(--text-secondary);
  }
  .badge {
    font-size: 10px;
    font-weight: 700;
    text-transform: uppercase;
    letter-spacing: 0.04em;
    padding: 2px 7px;
    border-radius: 999px;
    line-height: 1.2;
  }
  .badge.recommended {
    background: var(--accent-subtle, rgba(29, 185, 84, 0.2));
    color: var(--accent, #1DB954);
    border: 1px solid var(--accent, #1DB954);
  }
  .detected-stats {
    display: flex;
    flex-wrap: wrap;
    gap: 6px;
    margin-top: 6px;
  }
  .stat-pill {
    display: inline-flex;
    align-items: center;
    gap: 5px;
    font-size: 11px;
    padding: 3px 8px;
    border-radius: 6px;
    background: var(--bg-hover, rgba(255, 255, 255, 0.08));
    color: var(--text-secondary);
    max-width: 100%;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }
  .stat-pill.folder-pill {
    width: 320px;
    min-width: 0;
    max-width: 100%;
    box-sizing: border-box;
  }
  .migration-mode-badge {
    font-weight: 600;
    color: var(--text-primary);
  }
  .migration-mode-badge.is-migrate {
    color: var(--accent, #1DB954);
  }

  .summary { margin: 0; }
  .summary > div { display: flex; justify-content: space-between; gap: 24px; padding: 13px 0; border-bottom: 1px solid var(--border-subtle, #ffffff12); font-size: 13px; }
  dt { color: var(--text-secondary); flex-shrink: 0; }
  dd { min-width: 0; margin: 0; text-align: right; overflow-wrap: anywhere; }
  .folder-summary { overflow-wrap: anywhere; }
  .song-count { color: var(--accent); font-weight: 600; }
  .setup-error { color: var(--danger, #ff7777); font-size: 12px; line-height: 1.5; margin: 8px 0 0; overflow-wrap: anywhere; }
  .sr-only { position: absolute; width: 1px; height: 1px; padding: 0; margin: -1px; overflow: hidden; clip-path: inset(50%); white-space: nowrap; }

  /* Pinned Bottom Actions Area */
  .bottom-actions-area {
    flex-shrink: 0;
    width: 100%;
    padding: 32px 0 36px;
    background: transparent;
    user-select: none;
  }
  .bottom-actions-inner {
    width: 100%;
    max-width: 620px;
    margin: 0 auto;
    padding: 0 28px;
    box-sizing: border-box;
    display: flex;
    justify-content: space-between;
    align-items: center;
    gap: 16px;
  }
  .actions-left {
    display: flex;
    align-items: center;
    gap: 10px;
  }
  .actions-right {
    margin-left: auto;
    display: flex;
    align-items: center;
  }
  /* Remove thin lines / strokes on navigation and modal buttons */
  :global(.onboarding-screen .fluent-btn) {
    border-color: transparent !important;
    box-shadow: none !important;
  }
  :global(.onboarding-screen .fluent-btn:hover:not(:disabled)) {
    border-color: transparent !important;
  }
  :global(.bottom-actions-inner .skip-btn) {
    color: var(--text-secondary);
  }
  :global(.bottom-actions-inner .skip-btn:hover:not(:disabled)) {
    color: var(--text-primary);
  }

  /* Skip Confirmation Dialog */
  .dialog-backdrop {
    position: fixed;
    inset: 0;
    z-index: 1100;
    background: rgba(0, 0, 0, 0.55);
    backdrop-filter: blur(4px);
    display: grid;
    place-items: center;
    user-select: none;
  }
  .dialog-box {
    background: var(--bg-dialog, var(--card-bg, #27272A));
    border: 1px solid var(--border-medium);
    border-radius: var(--radius-lg, 8px);
    box-shadow: var(--shadow-xl, 0 20px 25px -5px rgba(0, 0, 0, 0.45));
    overflow: hidden;
    display: flex;
    flex-direction: column;
    max-width: calc(100vw - 32px);
    max-height: calc(100vh - 64px);
    transform-origin: center center;
    will-change: transform, opacity;
  }
  .dialog-box.dialog-skipConfirm {
    width: 440px;
  }
  .skip-dialog-text {
    font-size: 13px;
    line-height: 1.6;
    color: var(--text-secondary);
    margin: 0;
  }

  @media (max-height: 640px) {
    .top-progress-area { padding: 12px 0 16px; }
    .bottom-actions-area { padding: 16px 0 20px; }
    :global(.page-scroll-viewer) { margin-top: 10px; }
    .setting-row { padding: 10px 0; }
  }
  @media (max-width: 500px) {
    .top-progress-inner { padding: 0 20px; }
    .setup-layout { padding: 0 20px; }
    .bottom-actions-inner { padding: 0 20px; }
    .fields-grid { grid-template-columns: minmax(0, 1fr); gap: 12px; }
    .folder-choice { flex-wrap: wrap; gap: 12px; }
    .folder-text { min-width: 0; }
    .summary > div { flex-direction: column; gap: 7px; }
    dd { text-align: left; }
  }
  @media (prefers-reduced-motion: reduce) {
    .skip-availability { transition: none; }
    .onboarding-screen.finished { animation: none !important; opacity: 0; }
    .linear-bar-fill { transition: none !important; }
    .linear-bar-fill::after { display: none !important; }
  }
</style>


