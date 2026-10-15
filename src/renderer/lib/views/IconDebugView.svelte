<script lang="ts">
  import { searchOverflow } from '../actions/searchOverflow';
  import { onDestroy, tick, type Component } from 'svelte';
  import { uiStore } from '../state/uiStore.svelte';
  import { animationStore } from '../state/animationStore.svelte';
  import { notificationCenter } from '../ui/notificationCenter.svelte';
  import { t } from '../i18n';
  import FluentIcon from '../components/Common/FluentIcon.svelte';
  import ToggleSwitch from '../components/Common/ToggleSwitch.svelte';
  import Slider from '../components/Common/Slider.svelte';
  import {
    ICONS,
    type IconMetadata,
    type IconParamPreset,
    type TogglePreset,
    type OptionPreset
  } from '../components/Common/iconData';
  import { metadata as speedMetadata } from '../components/Common/SpeedIcon.svelte';
  import { metadata as transposeMetadata } from '../components/Common/TransposeIcon.svelte';
  import { metadata as shuffleMetadata } from '../components/Common/ShuffleIcon.svelte';
  import { metadata as headphonesMetadata } from '../components/Common/HeadphonesIcon.svelte';
  import { metadata as refreshMetadata } from '../components/Common/RefreshIcon.svelte';
  import { metadata as playPauseMetadata } from '../components/Common/PlayPauseIcon.svelte';
  import { metadata as loopMetadata } from '../components/Common/LoopIcon.svelte';
  import { metadata as themeToggleMetadata } from '../components/Common/ThemeToggleIcon.svelte';

  // --- Icon Catalog & Categorization ---
  // Discover standalone components as well as generated SVG definitions. New icon
  // components appear automatically, even before they have showcase metadata.
  const iconModules = import.meta.glob<{
    default: Component<any>;
    metadata?: IconMetadata;
  }>('../components/Common/*Icon.svelte', { eager: true });
  // Existing component families use their established catalog names.
  const componentKeys: Record<string, string> = {
    Volume: 'Volume3', Speed: 'TopSpeed', Transpose: 'TransposeSmart',
    Refresh: 'ArrowClockwise', PlayPause: 'Play', Loop: 'Repeat', ThemeToggle: 'Moon'
  };
  const standaloneIcons = Object.fromEntries(
    Object.entries(iconModules)
      .filter(([path]) => !path.endsWith('/FluentIcon.svelte'))
      .map(([path, module]) => {
        const name = path.split('/').pop()!.replace(/Icon\.svelte$/, '');
        return [componentKeys[name] || name, module];
      })
  );
  const ALL_ICON_KEYS = [...new Set([...Object.keys(ICONS), ...Object.keys(standaloneIcons)])];
  const TOTAL_COUNT = ALL_ICON_KEYS.length;
  // Match the filled action icons used in the title bar.
  const TITLEBAR_FILLED_ICONS = new Set(['ArrowDownload', 'Warning', 'Moon', 'Info']);

  // Curated Primary Animated Icons
  const PRIMARY_ANIMATED_ICONS = [
    { key: 'Moon', name: 'Moon / Sun (Theme)', desc: 'Moon-to-sun CCW morph, ray trim path & solar pulse' },
    { key: 'Play', name: 'Play / Pause', desc: 'Smooth cubic-bezier morph between Play triangle and Pause bars' },
    { key: 'Repeat', name: 'Repeat / Loop', desc: 'Track follow-through & line breathing along rounded rectangle' },
    { key: 'Shuffle', name: 'Shuffle', desc: 'Continuous path trim & wing collapse' },
    { key: 'Headphones', name: 'Headphones', desc: 'Elastic ear cup stretch & headband dip' },
    { key: 'TopSpeed', name: 'Speed / TopSpeed', desc: 'Analog gauge needle & tick proximity shrink' },
    { key: 'TransposeSmart', name: 'Transpose (Smart)', desc: '5-bar equalizer height transitions' },
    { key: 'ArrowClockwise', name: 'Refresh (Clockwise)', desc: 'Tail brake, continuous spin & wing unfold' },
    { key: 'Volume3', name: 'Volume', desc: 'The existing arc expands outward as a new inner arc appears; the mute cross masks the speaker as it grows' },
    ...Object.entries(standaloneIcons)
      .filter(([key, module]) => !ICONS[key] && module.metadata)
      .map(([key, module]) => ({ key, name: module.metadata!.name, desc: module.metadata!.description || '' }))
  ];

  function isAnimatedIcon(key: string): boolean {
    const k = key.toLowerCase();
    return (
      Boolean(standaloneIcons[key]?.metadata) ||
      /^volume[0-3]$/.test(k) ||
      k === 'moon' ||
      k === 'sun' ||
      k === 'daynight' ||
      k === 'weathermoon' ||
      k === 'weathersunny' ||
      k === 'play' ||
      k === 'pause' ||
      k === 'repeat' ||
      k === 'repeatone' ||
      k === 'loop' ||
      k === 'shuffle' ||
      k === 'headphones' ||
      k === 'headphone' ||
      k === 'topspeed' ||
      k === 'speed' ||
      k.startsWith('transpose') ||
      k === 'arrowclockwise' ||
      k === 'arrowreset' ||
      k === 'refresh'
    );
  }

  function isMediaIcon(key: string): boolean {
    const k = key.toLowerCase();
    return (
      k.includes('play') ||
      k.includes('pause') ||
      k.includes('stop') ||
      k.includes('skip') ||
      k.includes('forward') ||
      k.includes('backward') ||
      k.includes('record') ||
      k.includes('volume') ||
      k.includes('speaker') ||
      k.includes('mute') ||
      k.includes('music') ||
      k.includes('headphone') ||
      k.includes('shuffle') ||
      k.includes('repeat') ||
      k.includes('disc') ||
      k.includes('track') ||
      k.includes('playlist') ||
      k.includes('sound') ||
      k.includes('guitar') ||
      k.includes('piano') ||
      k.includes('pedal') ||
      k.includes('radio')
    );
  }

  function isUIIcon(key: string): boolean {
    return !isAnimatedIcon(key) && !isMediaIcon(key);
  }

  // --- Component State ---
  let selectedIcon = $state<string>('Shuffle');
  let searchQuery = $state<string>('');
  let activeCategory = $state<'all' | 'animated' | 'media' | 'ui' | 'filled'>('all');

  // Variations & Modifiers
  const MAX_HERO_SIZE = 256;
  let isFilled = $state<boolean>(false);
  let isActive = $state<boolean>(false);
  let heroHovered = $state(false);
  let heroFocused = $state(false);
  let hoveredTile = $state<string | null>(null);
  let focusedTile = $state<string | null>(null);
  let heroSize = $state<number>(MAX_HERO_SIZE); // Render size defaults to max size!
  let playbackSpeed = $state<number>(1.0); // Animation speed/duration multiplier

  // Dedicated Animated Icon Special Parameters (Embedded in icons)
  let dialSpeed = $state<number>(1.0); // Needle speed parameter specifically for Speed / TopSpeed icon
  let transposeMode = $state<string>('TransposeSmart');
  let isRefreshLoading = $state<boolean>(false);

  // Animation Trigger & Status Tracking
  let triggerCount = $state<number>(0);
  let animStatus = $state<'idle' | 'animating' | 'spamming' | 'looping'>('idle');
  let isLooping = $state<boolean>(false);
  let spamCount = $state<number>(0);

  let animResetTimeout: ReturnType<typeof setTimeout> | null = null;
  let spamInterval: ReturnType<typeof setInterval> | null = null;
  let loopInterval: ReturnType<typeof setInterval> | null = null;

  // Filtered List
  let filteredIcons = $derived.by(() => {
    let list = ALL_ICON_KEYS;

    // 1. Category Filter
    if (activeCategory === 'animated') {
      const primaryKeys = PRIMARY_ANIMATED_ICONS.map(i => i.key);
      const remainingAnimated = list.filter(k => isAnimatedIcon(k) && !primaryKeys.includes(k));
      list = [...primaryKeys, ...remainingAnimated];
    } else if (activeCategory === 'media') {
      list = list.filter(k => isMediaIcon(k));
    } else if (activeCategory === 'ui') {
      list = list.filter(k => isUIIcon(k));
    } else if (activeCategory === 'filled') {
      list = list.filter(
        k => Boolean(ICONS[k]?.filledPaths && ICONS[k].filledPaths!.length > 0) || (isAnimatedIcon(k) && Boolean(ICONS[k]) && !/^Volume[0-3]$/.test(k))
      );
    }

    // 2. Search Query Filter
    const q = searchQuery.trim().toLowerCase();
    if (q) {
      list = list.filter(k => k.toLowerCase().includes(q));
    }

    return list;
  });

  // Category counts
  let animatedCount = $derived(ALL_ICON_KEYS.filter(k => isAnimatedIcon(k)).length);
  let mediaCount = $derived(ALL_ICON_KEYS.filter(k => isMediaIcon(k)).length);
  let uiCount = $derived(ALL_ICON_KEYS.filter(k => isUIIcon(k)).length);
  let filledCount = $derived(
    ALL_ICON_KEYS.filter(
      k => Boolean(ICONS[k]?.filledPaths && ICONS[k].filledPaths!.length > 0) || (isAnimatedIcon(k) && Boolean(ICONS[k]) && !/^Volume[0-3]$/.test(k))
    ).length
  );

  // Selected Icon Metadata
  let isSelectedAnimated = $derived(isAnimatedIcon(selectedIcon));
  let isSelectedShuffle = $derived(selectedIcon.toLowerCase() === 'shuffle');
  let isSelectedHeadphones = $derived(selectedIcon.toLowerCase().includes('headphone'));
  let isSelectedSpeed = $derived(
    selectedIcon.toLowerCase().includes('speed') || selectedIcon === 'TopSpeed'
  );
  let isSelectedTranspose = $derived(selectedIcon.toLowerCase().includes('transpose'));
  let isSelectedRefresh = $derived(
    selectedIcon.toLowerCase().includes('clockwise') ||
    selectedIcon.toLowerCase() === 'refresh' ||
    selectedIcon.toLowerCase() === 'arrowreset'
  );
  let isSelectedPlayPause = $derived(
    selectedIcon.toLowerCase() === 'play' ||
    selectedIcon.toLowerCase() === 'pause'
  );

  let isSelectedRepeat = $derived(
    selectedIcon.toLowerCase().includes('repeat') || selectedIcon.toLowerCase().includes('loop')
  );

  let isSelectedTheme = $derived(
    selectedIcon.toLowerCase() === 'moon' ||
    selectedIcon.toLowerCase() === 'sun' ||
    selectedIcon.toLowerCase() === 'daynight' ||
    selectedIcon.toLowerCase().includes('moon') ||
    selectedIcon.toLowerCase().includes('sunny')
  );

  let selectedDef = $derived(ICONS[selectedIcon] || { viewBox: '0 0 24 24', paths: [] });
  let hasFilledVariant = $derived(
    Boolean(
      (selectedDef.filledPaths && selectedDef.filledPaths.length > 0) ||
      (isAnimatedIcon(selectedIcon) && Boolean(ICONS[selectedIcon]) && !/^Volume[0-3]$/.test(selectedIcon))
    )
  );
  let displayedPaths = $derived(
    isFilled && selectedDef.filledPaths && selectedDef.filledPaths.length > 0
      ? selectedDef.filledPaths
      : selectedDef.paths || []
  );

  function getIconMetadata(key: string): IconMetadata | null {
    const k = key.toLowerCase();
    if (standaloneIcons[key]?.metadata) return standaloneIcons[key].metadata!;
    if (/^volume[0-3]$/.test(k)) return standaloneIcons.Volume3.metadata!;
    if (k === 'moon' || k === 'sun' || k === 'daynight' || k.includes('moon') || k.includes('sunny')) return themeToggleMetadata;
    if (k === 'play' || k === 'pause') return playPauseMetadata;
    if (k.includes('repeat') || k.includes('loop')) return loopMetadata;
    if (k.includes('topspeed') || k.includes('speed')) return speedMetadata;
    if (k.startsWith('transpose')) return transposeMetadata;
    if (k === 'shuffle') return shuffleMetadata;
    if (k.includes('headphone')) return headphonesMetadata;
    if (k.includes('clockwise') || k === 'refresh' || k === 'arrowreset') return refreshMetadata;
    return null;
  }

  let currentMetadata = $derived(getIconMetadata(selectedIcon));
  let standaloneIcon = $derived(!ICONS[selectedIcon] ? standaloneIcons[selectedIcon]?.default : undefined);
  let isSelectedVolume = $derived(/^Volume[0-3]$/.test(selectedIcon));
  let isPlaying = $state<boolean>(false);
  let isThemeDark = $state<boolean>(true);
  let paramValues = $state<Record<string, any>>({
    volume: 1,
    speed: 1.0,
    mode: 'TransposeSmart',
    loopMode: 'off',
    active: false,
    filled: false,
    loading: false,
    playing: false,
    isDark: true
  });

  function isPropsMatch(props?: Record<string, any>): boolean {
    if (!props || Object.keys(props).length === 0) return false;
    for (const [key, expectedVal] of Object.entries(props)) {
      if (key === 'isDark') {
        if (isThemeDark !== Boolean(expectedVal)) return false;
      } else if (key === 'playing') {
        if (isPlaying !== Boolean(expectedVal)) return false;
      } else if (key === 'filled') {
        if (isFilled !== Boolean(expectedVal)) return false;
      } else if (key === 'active') {
        if (isActive !== Boolean(expectedVal)) return false;
      } else if (key === 'loading') {
        if (isRefreshLoading !== Boolean(expectedVal)) return false;
      } else if (key === 'speed') {
        if (Math.abs(dialSpeed - Number(expectedVal)) > 0.001) return false;
      } else if (key === 'playbackSpeed') {
        if (Math.abs(playbackSpeed - Number(expectedVal)) > 0.001) return false;
      } else if (key === 'mode') {
        if (selectedIcon.startsWith('Transpose')) {
          if (transposeMode !== String(expectedVal)) return false;
        } else {
          const currentMode = selectedIcon.toLowerCase().includes('one') ? 'single' : (isActive ? 'all' : 'off');
          if (currentMode !== String(expectedVal)) return false;
        }
      } else if (key === 'iconName') {
        const isPlayPauseAlias =
          (selectedIcon === 'Play' || selectedIcon === 'Pause') &&
          (expectedVal === 'Play' || expectedVal === 'Pause');
        const isThemeAlias =
          (selectedIcon === 'Moon' || selectedIcon === 'Sun' || selectedIcon === 'DayNight' || selectedIcon === 'WeatherMoon' || selectedIcon === 'WeatherSunny') &&
          (expectedVal === 'Moon' || expectedVal === 'Sun' || expectedVal === 'WeatherMoon' || expectedVal === 'WeatherSunny' || expectedVal === 'DayNight');
        if (selectedIcon !== String(expectedVal) && transposeMode !== String(expectedVal) && !isPlayPauseAlias && !isThemeAlias) {
          return false;
        }
      } else if (key === 'size') {
        if (heroSize !== Number(expectedVal)) return false;
      } else {
        if (paramValues[key] !== expectedVal) return false;
      }
    }
    return true;
  }

  function isTogglePresetActive(param: TogglePreset): boolean {
    if (param.onProps && Object.keys(param.onProps).length > 0) {
      return isPropsMatch(param.onProps);
    }
    return Boolean(paramValues[param.id]);
  }

  function isOptionPresetActive(param: OptionPreset, opt: { value: any; props?: Record<string, any> }): boolean {
    if (opt.props && Object.keys(opt.props).length > 0) {
      return isPropsMatch(opt.props);
    }
    return (param.id === 'mode' ? transposeMode : paramValues[param.id]) === opt.value;
  }

  function applyParamValue(param: IconParamPreset, value: any, props?: Record<string, any>) {
    paramValues[param.id] = value;

    const effectiveProps =
      props ||
      (value === false && param.type === 'toggle' && param.onProps
        ? Object.fromEntries(
            Object.entries(param.onProps).map(([k, v]) => [k, typeof v === 'boolean' ? !v : v])
          )
        : undefined);

    if (effectiveProps) {
      if ('playing' in effectiveProps) isPlaying = Boolean(effectiveProps.playing);
      if ('active' in effectiveProps) isActive = Boolean(effectiveProps.active);
      if ('filled' in effectiveProps) toggleFilledVariant(Boolean(effectiveProps.filled));
      if ('iconName' in effectiveProps) {
        selectedIcon = String(effectiveProps.iconName);
        if (effectiveProps.iconName.startsWith('Transpose')) transposeMode = effectiveProps.iconName;
      }
      if ('mode' in effectiveProps && !selectedIcon.startsWith('Transpose')) {
        if (effectiveProps.mode === 'single') {
          selectedIcon = 'RepeatOne';
          isActive = true;
        } else if (effectiveProps.mode === 'all') {
          selectedIcon = 'Repeat';
          isActive = true;
        } else if (effectiveProps.mode === 'off') {
          selectedIcon = 'Repeat';
          isActive = false;
        }
      }
      if ('loading' in effectiveProps) {
        isRefreshLoading = Boolean(effectiveProps.loading);
        if (isRefreshLoading) {
          startLoop();
        } else {
          stopLoop();
        }
      }
      if ('speed' in effectiveProps) dialSpeed = Number(effectiveProps.speed);
      if ('percentage' in effectiveProps) paramValues['percentage'] = Number(effectiveProps.percentage);
      if ('isDark' in effectiveProps) isThemeDark = Boolean(effectiveProps.isDark);
    } else {
      if (param.id === 'playing') {
        isPlaying = Boolean(value);
      } else if (param.id === 'isDark') {
        isThemeDark = Boolean(value);
      } else if (param.id === 'speed') {
        dialSpeed = Number(value);
      } else if (param.id === 'loading') {
        isRefreshLoading = Boolean(value);
        if (isRefreshLoading) {
          startLoop();
        } else {
          stopLoop();
        }
      } else if (param.id === 'mode') {
        transposeMode = String(value);
        selectedIcon = String(value);
      }
    }
  }

  // Effective animation duration derived from playbackSpeed modifier
  let effectiveDuration = $derived.by(() => {
    const base = 0.40;
    return Math.max(0.08, base / playbackSpeed);
  });

  // Code Snippet Generator
  let codeSnippet = $derived.by(() => {
    if (standaloneIcon) {
      const params = currentMetadata?.params.map(p => ` ${p.id}={${JSON.stringify(paramValues[p.id] ?? p.defaultValue)}}`).join('') || '';
      return `<${selectedIcon}Icon size={${heroSize}}${params} />`;
    }
    let props = `name="${selectedIcon}" size={${heroSize}}`;
    if (isSelectedVolume) props += ` volume={${paramValues.volume}}`;
    if (isFilled) props += ' filled';
    if (isActive) props += ' active';
    if (isSelectedTheme) props += ` isDark={${isThemeDark}}`;
    if (isSelectedSpeed && dialSpeed !== 1.0) props += ` speed={${dialSpeed}}`;
    if (isSelectedPlayPause && isPlaying) props += ' playing={true}';
    if (playbackSpeed !== 1.0) props += ` duration={${effectiveDuration.toFixed(2)}}`;
    if (isSelectedRefresh && isRefreshLoading) props += ' loading={true}';
    return `<FluentIcon ${props} />`;
  });

  // --- Animation Execution Logic ---
  function fireTriggerCycle() {
    triggerCount++;

    // Only icons that explicitly declare animationParamId rely on cycling a parameter to animate
    if (!currentMetadata || !currentMetadata.animationParamId) return;

    const animParamId = currentMetadata.animationParamId;
    const param = currentMetadata.params.find(p => p.id === animParamId);
    if (!param) return;

    if (param.type === 'slider') {
      const presets = param.presets?.map(p => p.value) || [1.0, 1.5, 2.0, 3.0, 0.5, 0.25];
      const curVal = param.id === 'speed' ? dialSpeed : Number(paramValues[param.id] ?? param.defaultValue);
      const curIdx = presets.findIndex(v => Math.abs(v - curVal) < 0.01);
      const nextIdx = (curIdx + 1) % presets.length;
      const nextVal = presets[nextIdx];
      applyParamValue(param, nextVal);
    } else if (param.type === 'option') {
      const curIdx = param.options.findIndex(o => isOptionPresetActive(param, o));
      const nextIdx = curIdx >= 0 ? (curIdx + 1) % param.options.length : 0;
      const nextOpt = param.options[nextIdx];
      applyParamValue(param, nextOpt.value, nextOpt.props);
    } else if (param.type === 'toggle') {
      const curVal = isTogglePresetActive(param);
      const nextVal = !curVal;
      applyParamValue(param, nextVal, nextVal ? param.onProps : param.offProps);
    }
  }

  // Button 1: "Play"
  function handlePlay() {
    if (isLooping) {
      stopLoop();
    }
    if (spamInterval) {
      clearInterval(spamInterval);
      spamInterval = null;
    }
    if (animResetTimeout) {
      clearTimeout(animResetTimeout);
      animResetTimeout = null;
    }

    animStatus = 'animating';
    fireTriggerCycle();

    const timeoutMs = (effectiveDuration * 1000) + 120;
    animResetTimeout = setTimeout(() => {
      if (animStatus === 'animating') {
        animStatus = 'idle';
      }
      animResetTimeout = null;
    }, timeoutMs);
  }

  // Button 2: "Spam"
  function runSpamTest() {
    if (isLooping) {
      stopLoop();
    }
    if (spamInterval) {
      clearInterval(spamInterval);
      spamInterval = null;
    }
    if (animResetTimeout) {
      clearTimeout(animResetTimeout);
      animResetTimeout = null;
    }

    animStatus = 'spamming';
    spamCount = 0;
    const totalBursts = 10;

    spamInterval = setInterval(() => {
      spamCount++;
      fireTriggerCycle();

      if (spamCount >= totalBursts) {
        if (spamInterval) {
          clearInterval(spamInterval);
          spamInterval = null;
        }
        animResetTimeout = setTimeout(() => {
          animStatus = 'idle';
          animResetTimeout = null;
        }, (effectiveDuration * 1000) + 150);
      }
    }, 110);
  }

  // Button 3: "Loop"
  function toggleLoop() {
    if (isLooping) {
      stopLoop();
    } else {
      startLoop();
    }
  }

  function startLoop() {
    if (spamInterval) {
      clearInterval(spamInterval);
      spamInterval = null;
    }
    if (animResetTimeout) {
      clearTimeout(animResetTimeout);
      animResetTimeout = null;
    }

    isLooping = true;
    animStatus = 'looping';

    if (isSelectedRefresh || isSelectedRepeat) {
      isRefreshLoading = true;
      return;
    }

    fireTriggerCycle();

    const loopDelayMs = Math.max(300, (effectiveDuration * 1000) + 220);
    loopInterval = setInterval(() => {
      fireTriggerCycle();
    }, loopDelayMs);
  }

  function stopLoop() {
    isLooping = false;
    if (loopInterval) {
      clearInterval(loopInterval);
      loopInterval = null;
    }
    if (isSelectedRefresh || isSelectedRepeat) {
      isRefreshLoading = false;
    }
    if (animStatus === 'looping') {
      animStatus = 'idle';
    }
  }

  function selectIcon(key: string) {
    selectedIcon = key;
    if (TITLEBAR_FILLED_ICONS.has(key)) toggleFilledVariant(true);
    const lowerKey = key.toLowerCase();
    for (const param of getIconMetadata(key)?.params || []) {
      paramValues[param.id] = param.defaultValue;
    }
    if (/^Volume[0-3]$/.test(key)) {
      paramValues.volume = [0, 0.2, 0.5, 1][Number(key.slice(-1))];
    }

    if (key.startsWith('Transpose')) {
      transposeMode = key;
    } else if (lowerKey === 'pause') {
      isPlaying = true;
    } else if (lowerKey === 'play') {
      isPlaying = false;
    } else if (lowerKey.includes('sunny') || lowerKey === 'sun') {
      isThemeDark = false;
    } else if (lowerKey.includes('moon')) {
      isThemeDark = true;
    }

    if (isLooping) {
      // Re-arm loop for the newly selected icon
      stopLoop();
      startLoop();
    } else {
      const isExplicitVariant =
        /^Volume[0-3]$/.test(key) ||
        key === 'Play' ||
        key === 'Pause' ||
        key === 'Moon' ||
        key === 'Sun' ||
        key === 'WeatherMoon' ||
        key === 'WeatherSunny' ||
        (key.startsWith('Transpose') && key !== 'TransposeSmart');

      if (!isExplicitVariant) {
        handlePlay();
      }
    }
  }

  function toggleFilledVariant(val: boolean) {
    isFilled = val;
  }

  async function copySnippet() {
    try {
      await navigator.clipboard.writeText(codeSnippet);
      notificationCenter.success(t('iconDebug.title'), t('iconDebug.copiedSnippet'));
    } catch {
      // Fallback
    }
  }

  onDestroy(() => {
    if (animResetTimeout) {
      clearTimeout(animResetTimeout);
      animResetTimeout = null;
    }
    if (spamInterval) {
      clearInterval(spamInterval);
      spamInterval = null;
    }
    if (loopInterval) {
      clearInterval(loopInterval);
      loopInterval = null;
    }
  });
</script>

<div class="icon-debug-page">
  <!-- Top Navigation & Header Bar -->
  <header class="debug-header">
    <div class="header-left">
      <button
        type="button"
        class="back-btn"
        onclick={() => (uiStore.activeTab = 'settings')}
        title={t('iconDebug.backToSettings')}
      >
        <FluentIcon name="ArrowLeft" size={16} />
        <span>{t('iconDebug.backToSettings')}</span>
      </button>

      <div class="header-titles">
        <h1 class="page-title">{t('iconDebug.title')}</h1>
        <p class="page-subtitle">
          {t('iconDebug.subtitle', { count: TOTAL_COUNT })}
        </p>
      </div>
    </div>

    <div class="header-stats">
      <span class="stat-pill total">
        <FluentIcon name="Grid" size={13} />
        <span>{TOTAL_COUNT} Icons</span>
      </span>
      <span class="stat-pill animated">
        <FluentIcon name="Sparkle" size={13} />
        <span>{PRIMARY_ANIMATED_ICONS.length} Animated</span>
      </span>
    </div>
  </header>

  <!-- Two-Column Split Layout -->
  <div class="debug-body">
    <!-- Left Column: Hero Showcase & Diagnostic Controls -->
    <aside class="showcase-column">
      <!-- 1. Square Hero Preview Card -->
      <div class="hero-card">
        <div class="hero-card-header">
          <span class="card-caption">{t('iconDebug.heroTitle')}</span>
          {#if isSelectedAnimated}
            <span class="animated-chip">
              <FluentIcon name="Sparkle" size={11} />
              <span>Animated</span>
            </span>
          {/if}
        </div>

        <!-- 1:1 Square Viewer Stage -->
        <button
          type="button"
          class="hero-icon-stage"
          class:is-active-state={isActive}
          onclick={handlePlay}
          onpointerenter={() => (heroHovered = true)}
          onpointerleave={() => (heroHovered = false)}
          onfocus={(event) => (heroFocused = event.currentTarget.matches(':focus-visible'))}
          onblur={() => (heroFocused = false)}
          title={t('iconDebug.clickToAnimate')}
        >
          {#if standaloneIcon}
            {@const StandaloneIcon = standaloneIcon}
            <StandaloneIcon {...paramValues} hovered={heroHovered || heroFocused || Boolean(paramValues.hovered)}
              size={heroSize} duration={effectiveDuration} trigger={triggerCount} />
          {:else}
          <FluentIcon
            name={selectedIcon}
            size={heroSize}
            filled={isFilled}
            active={isActive}
            hovered={heroHovered || heroFocused || Boolean(paramValues.hovered)}
            maximized={Boolean(paramValues.maximized)}
            playing={isPlaying}
            speed={dialSpeed}
            volume={paramValues.volume}
            duration={effectiveDuration}
            loading={isRefreshLoading}
            isDark={isThemeDark}
            trigger={triggerCount}
          />
          {/if}
        </button>

        <div class="hero-meta-bar">
          <span class="icon-name-tag" title={selectedIcon}>{selectedIcon}</span>
          {#if ICONS[selectedIcon]}
          <span class="meta-tag">{selectedDef.viewBox || '0 0 24 24'}</span>
          <span class="meta-tag">{displayedPaths.length} {displayedPaths.length === 1 ? 'path' : 'paths'}</span>
          {/if}
          {#if hasFilledVariant}
            <span class="meta-tag" style="color: var(--accent-color, #1db954);">Filled Available</span>
          {/if}
        </div>
      </div>

      <!-- 2. Real-Time Animation Status & Action Buttons (Play / Spam / Loop) -->
      <div class="status-action-box">
        <div class="status-readout">
          <span class="status-label">{t('iconDebug.animStatusLabel')}</span>
          <span
            class="status-pill"
            class:is-idle={animStatus === 'idle'}
            class:is-animating={animStatus === 'animating'}
            class:is-spamming={animStatus === 'spamming'}
            class:is-looping={animStatus === 'looping'}
          >
            <span class="pulse-dot"></span>
            {#if animStatus === 'idle'}
              <span>{t('iconDebug.statusIdle')}</span>
            {:else if animStatus === 'animating'}
              <span>{t('iconDebug.statusAnimating')}</span>
            {:else if animStatus === 'spamming'}
              <span>{t('iconDebug.statusSpamming', { count: spamCount })}</span>
            {:else}
              <span>{t('iconDebug.statusLooping')}</span>
            {/if}
          </span>
        </div>

        <!-- 3 Buttons: "Play", "Rapid", "Loop" -->
        <div class="action-buttons-row">
          <button
            type="button"
            class="action-btn"
            class:primary={animStatus === 'animating'}
            onclick={handlePlay}
          >
            Play
          </button>

          <button
            type="button"
            class="action-btn spam"
            class:active={animStatus === 'spamming'}
            onclick={runSpamTest}
          >
            Rapid
          </button>

          <button
            type="button"
            class="action-btn loop"
            class:active={isLooping}
            onclick={toggleLoop}
          >
            Loop
          </button>
        </div>

        <!-- Embedded Icon Special Parameters / Presets (Rendered directly below Play / Rapid / Loop buttons) -->
        {#if currentMetadata && currentMetadata.params.length > 0}
          <div class="embedded-presets-box">
            {#each currentMetadata.params as param}
              <div class="param-control-block">
                {#if param.type === 'toggle'}
                  {@const curVal = isTogglePresetActive(param)}
                  <label class="control-row">
                    <span class="row-label">{param.label}</span>
                    <ToggleSwitch
                      checked={curVal}
                      onchange={(val) => applyParamValue(param, val, val ? param.onProps : param.offProps)}
                    />
                  </label>
                {:else}
                  <div class="preset-box-header">
                    <span class="preset-box-title">{param.label}</span>
                    {#if param.type === 'slider'}
                      <span class="value-badge">{Number(param.id === 'speed' ? dialSpeed : (paramValues[param.id] ?? param.defaultValue)).toFixed(2)}{param.unit || ''}</span>
                    {:else if param.type === 'option'}
                      {@const activeOpt = param.options.find(o => isOptionPresetActive(param, o))}
                      <span class="value-badge">{activeOpt ? activeOpt.label : 'Custom'}</span>
                    {/if}
                  </div>

                  {#if param.type === 'slider'}
                    <div class="slider-container">
                      <Slider
                        min={param.min}
                        max={param.max}
                        step={param.step || 1}
                        value={param.id === 'speed' ? dialSpeed : (paramValues[param.id] ?? param.defaultValue)}
                        onchange={(v) => applyParamValue(param, v)}
                        oninput={(v) => applyParamValue(param, v)}
                      />
                    </div>
                    {#if param.presets && param.presets.length > 0}
                      <div class="quick-presets-row">
                        {#each param.presets as p}
                          <button
                            type="button"
                            class="preset-chip"
                            class:selected={Math.abs((param.id === 'speed' ? dialSpeed : Number(paramValues[param.id] ?? param.defaultValue)) - p.value) < 0.01}
                            onclick={() => applyParamValue(param, p.value)}
                          >
                            {p.label}
                          </button>
                        {/each}
                      </div>
                    {/if}

                  {:else if param.type === 'option'}
                    <div class="segmented-bar" class:four-items={param.options.length >= 4}>
                      {#each param.options as opt}
                        <button
                          type="button"
                          class="segment-btn"
                          class:active={isOptionPresetActive(param, opt)}
                          onclick={() => applyParamValue(param, opt.value, opt.props)}
                        >
                          {opt.label}
                        </button>
                      {/each}
                    </div>
                  {/if}
                {/if}
              </div>
            {/each}
          </div>
        {/if}
      </div>

      <!-- 3. Variations & Modifiers Section -->
      <div class="controls-card">
        <h3 class="controls-title">{t('iconDebug.variationsHeader')}</h3>

        <!-- Filled Variant Toggle -->
        <label class="control-row" class:is-disabled={!hasFilledVariant}>
          <span class="row-label">
            {t('iconDebug.styleFilled')}
            {#if !hasFilledVariant}
              <span class="sub-label" style="opacity: 0.6; font-size: 11px; margin-left: 6px;">(Outlined only)</span>
            {/if}
          </span>
          <ToggleSwitch checked={isFilled && hasFilledVariant} disabled={!hasFilledVariant} onchange={toggleFilledVariant} />
        </label>

        <!-- Active State Toggle -->
        <label class="control-row">
          <span class="row-label">{t('iconDebug.styleActive')}</span>
          <ToggleSwitch checked={isActive} onchange={(v) => (isActive = v)} />
        </label>

        <!-- Playback Speed Modifier -->
        <div class="control-group">
          <div class="group-header">
            <span class="row-label">{t('iconDebug.playbackSpeed')}</span>
            <span class="value-badge">{playbackSpeed.toFixed(2)}x</span>
          </div>

          <div class="slider-container">
            <Slider
              min={0.25}
              max={3.0}
              step={0.05}
              bind:value={playbackSpeed}
              formatTooltip={(v) => `${v.toFixed(2)}x`}
            />
          </div>

          <div class="quick-presets-row">
            {#each [0.25, 0.5, 0.75, 1.0, 1.5, 2.0, 3.0] as sp}
              <button
                type="button"
                class="preset-chip"
                class:selected={Math.abs(playbackSpeed - sp) < 0.01}
                onclick={() => (playbackSpeed = sp)}
              >
                {sp}x
              </button>
            {/each}
          </div>
        </div>

        <!-- Hero Render Size (Defaults to Max Size: 160px) -->
        <div class="control-group">
          <div class="group-header">
            <span class="row-label">{t('iconDebug.iconSizeLabel')}</span>
            <span class="value-badge">{heroSize}px</span>
          </div>

          <div class="slider-container">
            <Slider
              min={16}
              max={MAX_HERO_SIZE}
              step={4}
              bind:value={heroSize}
              formatTooltip={(v) => `${v}px`}
            />
          </div>

          <div class="quick-presets-row">
            {#each [16, 24, 32, 48, 64, 96, 128, 160, 200, 256] as sz}
              <button
                type="button"
                class="preset-chip"
                class:selected={heroSize === sz}
                onclick={() => (heroSize = sz)}
              >
                {sz}
              </button>
            {/each}
          </div>
        </div>

      </div>

      <!-- 4. Code Snippet & Copy Action -->
      <div class="snippet-card">
        <div class="snippet-header">
          <span class="snippet-title">Svelte Code</span>
          <button type="button" class="copy-btn" onclick={copySnippet} title="Copy code">
            <FluentIcon name="Copy" size={13} />
            <span>Copy</span>
          </button>
        </div>
        <pre class="snippet-code"><code>{codeSnippet}</code></pre>
      </div>
    </aside>

    <!-- Right Column: Searchable & Scrollable Grid -->
    <main class="grid-column">
      <!-- Search & Category Filters Bar -->
      <div class="grid-controls-bar">
        <!-- Search Input -->
        <div class="search-input-wrap">
          <FluentIcon name="Search" size={15} class="search-icon" />
          <input
            type="text"
            placeholder={t('iconDebug.searchPlaceholder', { count: TOTAL_COUNT })}
            bind:value={searchQuery}
            use:searchOverflow={{ value: searchQuery, placeholder: t('iconDebug.searchPlaceholder', { count: TOTAL_COUNT }) }}
            class="search-input"
          />
          {#if searchQuery}
            <button
              type="button"
              class="clear-search-btn"
              onclick={() => (searchQuery = '')}
              title={t('iconDebug.clearSearch')}
            >
              <FluentIcon name="Dismiss" size={13} />
            </button>
          {/if}
        </div>

        <!-- Category Pills -->
        <div class="category-pills-row">
          <button
            type="button"
            class="cat-pill"
            class:active={activeCategory === 'all'}
            onclick={() => (activeCategory = 'all')}
          >
            {t('iconDebug.filterAll')}
            <span class="pill-count">{TOTAL_COUNT}</span>
          </button>

          <button
            type="button"
            class="cat-pill special-anim"
            class:active={activeCategory === 'animated'}
            onclick={() => (activeCategory = 'animated')}
          >
            {t('iconDebug.filterAnimated')}
            <span class="pill-count">{animatedCount}</span>
          </button>

          <button
            type="button"
            class="cat-pill"
            class:active={activeCategory === 'media'}
            onclick={() => (activeCategory = 'media')}
          >
            {t('iconDebug.filterMedia')}
            <span class="pill-count">{mediaCount}</span>
          </button>

          <button
            type="button"
            class="cat-pill"
            class:active={activeCategory === 'ui'}
            onclick={() => (activeCategory = 'ui')}
          >
            {t('iconDebug.filterUI')}
            <span class="pill-count">{uiCount}</span>
          </button>

          <button
            type="button"
            class="cat-pill"
            class:active={activeCategory === 'filled'}
            onclick={() => (activeCategory = 'filled')}
          >
            {t('iconDebug.filterFilled')}
            <span class="pill-count">{filledCount}</span>
          </button>
        </div>
      </div>

      <!-- Icon Cards Grid Scroll Container -->
      <div class="grid-scroll-area">
        {#if filteredIcons.length === 0}
          <div class="empty-results-state">
            <FluentIcon name="Search" size={36} class="empty-icon" />
            <p class="empty-title">{t('iconDebug.noIconsFound')}</p>
            <button
              type="button"
              class="clear-filter-btn"
              onclick={() => {
                searchQuery = '';
                activeCategory = 'all';
              }}
            >
              Reset filters
            </button>
          </div>
        {:else}
          <div class="icons-grid">
            {#each filteredIcons as iconKey (iconKey)}
              {@const isCardAnimated = isAnimatedIcon(iconKey)}
              {@const isCardSelected = selectedIcon === iconKey}
              <button
                type="button"
                class="icon-tile-btn"
                class:selected={isCardSelected}
                class:is-animated={isCardAnimated}
                onclick={() => selectIcon(iconKey)}
                onpointerenter={() => (hoveredTile = iconKey)}
                onpointerleave={() => (hoveredTile = null)}
                onfocus={(event) => (focusedTile = event.currentTarget.matches(':focus-visible') ? iconKey : null)}
                onblur={() => (focusedTile = null)}
                title={iconKey}
              >
                {#if isCardAnimated}
                  <span class="star-badge" title="Animated Icon">★</span>
                {/if}
                <div class="tile-icon-box">
                  {#if !ICONS[iconKey] && standaloneIcons[iconKey]}
                    {@const TileIcon = standaloneIcons[iconKey].default}
                    <TileIcon size={24} hovered={hoveredTile === iconKey || focusedTile === iconKey} />
                  {:else}
                    <FluentIcon name={iconKey} filled={TITLEBAR_FILLED_ICONS.has(iconKey)} size={24} />
                  {/if}
                </div>
                <span class="tile-label">{iconKey}</span>
              </button>
            {/each}
          </div>
        {/if}
      </div>
    </main>
  </div>
</div>

<style>
  .icon-debug-page {
    display: flex;
    flex-direction: column;
    height: 100%;
    width: 100%;
    overflow: hidden;
    user-select: none;
    background: transparent;
    color: var(--text-primary, #ffffff);
  }

  /* --- Top Header Bar --- */
  .debug-header {
    display: flex;
    align-items: center;
    justify-content: space-between;
    padding: 14px 20px;
    border-bottom: 1px solid rgba(0, 0, 0, 0.4);
    background: rgba(0, 0, 0, 0.25);
    flex-shrink: 0;
  }

  .header-left {
    display: flex;
    align-items: center;
    gap: 16px;
  }

  .back-btn {
    display: inline-flex;
    align-items: center;
    gap: 8px;
    height: 32px;
    padding: 0 12px;
    border-radius: 6px;
    background: var(--card-bg, #1f1f1f);
    border: none;
    color: var(--text-primary, #ffffff);
    font-size: 13px;
    font-weight: 500;
    cursor: pointer;
    transition: background 0.15s ease, transform 0.15s ease;
  }

  .back-btn:hover {
    background: #2a2a2a;
    transform: translateX(-1px);
  }

  .header-titles {
    display: flex;
    flex-direction: column;
    gap: 2px;
  }

  .page-title {
    font-size: 18px;
    font-weight: 600;
    margin: 0;
    letter-spacing: -0.2px;
  }

  .page-subtitle {
    font-size: 12px;
    color: var(--text-tertiary, rgba(255, 255, 255, 0.45));
    margin: 0;
  }

  .header-stats {
    display: flex;
    align-items: center;
    gap: 10px;
  }

  .stat-pill {
    display: inline-flex;
    align-items: center;
    gap: 6px;
    font-size: 12px;
    font-weight: 500;
    padding: 4px 10px;
    border-radius: 12px;
    background: var(--card-bg, #1f1f1f);
    border: none;
    color: var(--text-secondary, rgba(255, 255, 255, 0.7));
  }

  .stat-pill.animated {
    background: rgba(234, 179, 8, 0.15);
    border: none;
    color: #ffd043;
  }

  /* --- Body 2-Column Split --- */
  .debug-body {
    display: flex;
    flex: 1;
    min-height: 0;
    overflow: hidden;
  }

  /* --- Left Column: Showcase & Controls --- */
  .showcase-column {
    width: 390px;
    min-width: 360px;
    max-width: 420px;
    flex-shrink: 0;
    height: 100%;
    overflow-y: auto;
    border-right: 1px solid rgba(0, 0, 0, 0.4);
    padding: 16px;
    display: flex;
    flex-direction: column;
    gap: 14px;
    background: rgba(0, 0, 0, 0.15);
  }

  /* Custom Scrollbars */
  .showcase-column::-webkit-scrollbar,
  .grid-scroll-area::-webkit-scrollbar {
    width: 6px;
  }

  .showcase-column::-webkit-scrollbar-thumb,
  .grid-scroll-area::-webkit-scrollbar-thumb {
    background: rgba(255, 255, 255, 0.14);
    border-radius: 3px;
  }

  /* 1. Hero Card */
  .hero-card {
    background: var(--card-bg, #1f1f1f);
    border: none;
    border-radius: 8px;
    box-shadow: var(--card-shadow, 0 2px 6px rgba(0, 0, 0, 0.12));
    padding: 14px;
    display: flex;
    flex-direction: column;
    gap: 12px;
  }

  .hero-card-header {
    display: flex;
    align-items: center;
    justify-content: space-between;
  }

  .card-caption {
    font-size: 11.5px;
    font-weight: 600;
    text-transform: uppercase;
    letter-spacing: 0.5px;
    color: var(--text-tertiary, rgba(255, 255, 255, 0.45));
  }

  .animated-chip {
    display: inline-flex;
    align-items: center;
    gap: 4px;
    padding: 2px 8px;
    border-radius: 10px;
    font-size: 11px;
    font-weight: 600;
    background: rgba(234, 179, 8, 0.16);
    border: none;
    color: #ffd043;
  }

  /* Square Hero Viewer Stage (1:1 Aspect Ratio) */
  .hero-icon-stage {
    width: 100%;
    aspect-ratio: 1 / 1;
    display: flex;
    align-items: center;
    justify-content: center;
    background: rgba(0, 0, 0, 0.32);
    border: none;
    border-radius: 8px;
    cursor: pointer;
    transition: background 0.2s cubic-bezier(0.16, 1, 0.3, 1);
    color: var(--text-primary, #ffffff);
    padding: 12px;
    position: relative;
    overflow: hidden;
  }

  .hero-icon-stage:hover {
    background: rgba(0, 0, 0, 0.45);
  }

  .hero-icon-stage.is-active-state {
    color: var(--accent-color, #1db954);
  }

  .hero-meta-bar {
    display: flex;
    align-items: center;
    gap: 6px;
    flex-wrap: wrap;
  }

  .icon-name-tag {
    font-size: 13px;
    font-weight: 600;
    color: var(--text-primary, #ffffff);
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
    max-width: 180px;
  }

  .meta-tag {
    font-size: 11px;
    padding: 2px 7px;
    border-radius: 4px;
    background: rgba(255, 255, 255, 0.07);
    border: none;
    color: var(--text-secondary, rgba(255, 255, 255, 0.7));
  }

  /* 2. Status & Action Bar */
  .status-action-box {
    background: var(--card-bg, #1f1f1f);
    border: none;
    border-radius: 8px;
    box-shadow: var(--card-shadow, 0 2px 6px rgba(0, 0, 0, 0.12));
    padding: 12px;
    display: flex;
    flex-direction: column;
    gap: 10px;
  }

  .status-readout {
    display: flex;
    align-items: center;
    justify-content: space-between;
  }

  .status-label {
    font-size: 12px;
    font-weight: 500;
    color: var(--text-secondary, rgba(255, 255, 255, 0.7));
  }

  .status-pill {
    display: inline-flex;
    align-items: center;
    gap: 6px;
    padding: 3px 10px;
    border-radius: 12px;
    font-size: 12px;
    font-weight: 600;
    background: rgba(255, 255, 255, 0.08);
    border: none;
    color: var(--text-primary, #ffffff);
  }

  .status-pill.is-idle {
    background: rgba(34, 197, 94, 0.16);
    border: none;
    color: #4ade80;
  }

  .status-pill.is-idle .pulse-dot {
    background: #4ade80;
  }

  .status-pill.is-animating,
  .status-pill.is-looping {
    background: rgba(59, 130, 246, 0.2);
    border: none;
    color: #60a5fa;
  }

  .status-pill.is-animating .pulse-dot,
  .status-pill.is-looping .pulse-dot {
    background: #60a5fa;
    animation: dotPulse 0.8s infinite alternate;
  }

  .status-pill.is-spamming {
    background: rgba(245, 158, 11, 0.22);
    border: none;
    color: #fbbf24;
  }

  .status-pill.is-spamming .pulse-dot {
    background: #fbbf24;
    animation: dotPulse 0.3s infinite alternate;
  }

  .pulse-dot {
    width: 7px;
    height: 7px;
    border-radius: 50%;
    display: inline-block;
  }

  @keyframes dotPulse {
    0% {
      opacity: 0.3;
      transform: scale(0.8);
    }
    100% {
      opacity: 1;
      transform: scale(1.2);
    }
  }

  /* Action Buttons: "Play", "Spam", "Loop" */
  .action-buttons-row {
    display: flex;
    gap: 8px;
  }

  .action-btn {
    flex: 1;
    display: inline-flex;
    align-items: center;
    justify-content: center;
    height: 34px;
    border-radius: 6px;
    font-size: 13px;
    font-weight: 600;
    cursor: pointer;
    border: none;
    background: rgba(255, 255, 255, 0.08);
    color: var(--text-primary, #ffffff);
    transition: all 0.15s ease;
  }

  .action-btn:hover {
    background: rgba(255, 255, 255, 0.15);
    transform: translateY(-1px);
  }

  .action-btn.primary {
    background: var(--accent-color, #1db954);
    color: #ffffff;
  }

  .action-btn.primary:hover {
    filter: brightness(1.1);
  }

  .action-btn.spam {
    background: rgba(245, 158, 11, 0.18);
    color: #fbbf24;
  }

  .action-btn.spam:hover {
    background: rgba(245, 158, 11, 0.28);
  }

  .action-btn.spam.active {
    background: #f59e0b;
    color: #000000;
  }

  .action-btn.loop.active {
    background: var(--accent-color, #1db954);
    color: #ffffff;
    box-shadow: 0 0 10px var(--accent-glow, rgba(29, 185, 84, 0.4));
  }

  /* Embedded Icon Presets (Below Play / Rapid / Loop buttons) */
  .embedded-presets-box {
    border-top: 1px solid rgba(255, 255, 255, 0.08);
    padding-top: 10px;
    display: flex;
    flex-direction: column;
    gap: 8px;
  }

  .preset-box-header {
    display: flex;
    align-items: center;
    justify-content: space-between;
  }

  .preset-box-title {
    font-size: 11px;
    font-weight: 600;
    text-transform: uppercase;
    letter-spacing: 0.5px;
    color: var(--text-tertiary, rgba(255, 255, 255, 0.5));
  }

  .param-control-block {
    display: flex;
    flex-direction: column;
    gap: 8px;
  }

  /* 3. Controls Card */
  .controls-card {
    background: var(--card-bg, #1f1f1f);
    border: none;
    border-radius: 8px;
    box-shadow: var(--card-shadow, 0 2px 6px rgba(0, 0, 0, 0.12));
    padding: 14px;
    display: flex;
    flex-direction: column;
    gap: 12px;
  }

  .controls-title {
    font-size: 13px;
    font-weight: 600;
    margin: 0;
    color: var(--text-primary, #ffffff);
  }

  .control-row {
    display: flex;
    align-items: center;
    justify-content: space-between;
    font-size: 13px;
    cursor: pointer;
  }

  .row-label {
    color: var(--text-secondary, rgba(255, 255, 255, 0.8));
    font-weight: 400;
  }

  .control-group {
    display: flex;
    flex-direction: column;
    gap: 6px;
  }

  .group-header {
    display: flex;
    align-items: center;
    justify-content: space-between;
  }

  .value-badge {
    font-size: 11px;
    font-weight: 600;
    padding: 2px 6px;
    border-radius: 4px;
    background: rgba(255, 255, 255, 0.08);
    color: var(--accent-color, #1db954);
  }

  .slider-container {
    padding: 4px 0;
  }


  .quick-presets-row {
    display: flex;
    gap: 4px;
    flex-wrap: wrap;
  }

  .preset-chip {
    padding: 3px 8px;
    font-size: 11px;
    font-weight: 500;
    border-radius: 4px;
    background: rgba(255, 255, 255, 0.07);
    border: none;
    color: var(--text-secondary, rgba(255, 255, 255, 0.7));
    cursor: pointer;
    transition: all 0.12s ease;
  }

  .preset-chip:hover {
    background: rgba(255, 255, 255, 0.14);
    color: #ffffff;
  }

  .preset-chip.selected {
    background: var(--accent-color, #1db954);
    color: #ffffff;
    font-weight: 600;
  }

  .segmented-bar {
    display: flex;
    background: rgba(0, 0, 0, 0.32);
    border: none;
    border-radius: 6px;
    padding: 2px;
    gap: 2px;
  }

  .segment-btn {
    flex: 1;
    height: 28px;
    font-size: 12px;
    font-weight: 500;
    border: none;
    border-radius: 4px;
    background: transparent;
    color: var(--text-secondary, rgba(255, 255, 255, 0.7));
    cursor: pointer;
    transition: all 0.15s ease;
  }

  .segment-btn:hover {
    color: #ffffff;
    background: rgba(255, 255, 255, 0.06);
  }

  .segment-btn.active {
    background: var(--accent-color, #1db954);
    color: #ffffff;
    font-weight: 600;
  }

  /* 4. Snippet Card */
  .snippet-card {
    background: rgba(0, 0, 0, 0.35);
    border: none;
    border-radius: 8px;
    padding: 10px 12px;
    display: flex;
    flex-direction: column;
    gap: 6px;
  }

  .snippet-header {
    display: flex;
    align-items: center;
    justify-content: space-between;
  }

  .snippet-title {
    font-size: 11px;
    font-weight: 600;
    text-transform: uppercase;
    color: var(--text-tertiary, rgba(255, 255, 255, 0.45));
  }

  .copy-btn {
    display: inline-flex;
    align-items: center;
    gap: 4px;
    font-size: 11px;
    font-weight: 500;
    background: rgba(255, 255, 255, 0.08);
    border: none;
    color: var(--text-secondary, rgba(255, 255, 255, 0.8));
    border-radius: 4px;
    padding: 3px 8px;
    cursor: pointer;
    transition: all 0.12s ease;
  }

  .copy-btn:hover {
    background: rgba(255, 255, 255, 0.16);
    color: #ffffff;
  }

  .snippet-code {
    margin: 0;
    padding: 6px 8px;
    background: rgba(0, 0, 0, 0.45);
    border: none;
    border-radius: 4px;
    font-family: 'Cascadia Code', 'Fira Code', Consolas, monospace;
    font-size: 12px;
    color: #93c5fd;
    overflow-x: auto;
    white-space: nowrap;
  }

  /* --- Right Column: Searchable Grid --- */
  .grid-column {
    flex: 1;
    min-width: 0;
    height: 100%;
    display: flex;
    flex-direction: column;
    overflow: hidden;
    background: transparent;
  }

  .grid-controls-bar {
    padding: 14px 20px 12px 20px;
    border-bottom: 1px solid rgba(0, 0, 0, 0.4);
    display: flex;
    flex-direction: column;
    gap: 10px;
    flex-shrink: 0;
    background: rgba(0, 0, 0, 0.12);
  }

  .search-input-wrap {
    display: flex;
    align-items: center;
    gap: 8px;
    height: 36px;
    background: rgba(0, 0, 0, 0.35);
    border: none;
    border-radius: 6px;
    padding: 0 12px;
    position: relative;
    transition: background 0.15s ease;
  }

  .search-input-wrap:focus-within {
    background: rgba(0, 0, 0, 0.5);
    box-shadow: inset 0 0 0 1px var(--accent-color, #1db954);
  }

  :global(.search-icon) {
    color: var(--text-tertiary, rgba(255, 255, 255, 0.45));
  }

  .search-input {
    flex: 1;
    min-width: 0;
    padding: 0;
    border: none;
    background: transparent;
    color: var(--text-primary, #ffffff);
    font-size: 13px;
    outline: none;
  }

  .search-input::placeholder {
    color: var(--text-tertiary, rgba(255, 255, 255, 0.4));
  }

  .clear-search-btn {
    border: none;
    background: transparent;
    color: var(--text-tertiary, rgba(255, 255, 255, 0.5));
    cursor: pointer;
    display: flex;
    align-items: center;
    justify-content: center;
    padding: 2px;
    border-radius: 4px;
  }

  .clear-search-btn:hover {
    color: #ffffff;
    background: rgba(255, 255, 255, 0.12);
  }

  .category-pills-row {
    display: flex;
    gap: 6px;
    flex-wrap: wrap;
  }

  .cat-pill {
    display: inline-flex;
    align-items: center;
    gap: 6px;
    height: 28px;
    padding: 0 10px;
    border-radius: 14px;
    font-size: 12px;
    font-weight: 500;
    background: rgba(255, 255, 255, 0.06);
    border: none;
    color: var(--text-secondary, rgba(255, 255, 255, 0.75));
    cursor: pointer;
    transition: all 0.15s ease;
  }

  .cat-pill:hover {
    background: rgba(255, 255, 255, 0.12);
    color: #ffffff;
  }

  .cat-pill.active {
    background: var(--accent-color, #1db954);
    color: #ffffff;
    font-weight: 600;
  }

  .cat-pill.special-anim {
    color: #ffd043;
    background: rgba(234, 179, 8, 0.15);
    border: none;
  }

  .cat-pill.special-anim.active {
    background: #eab308;
    color: #000000;
    border: none;
  }

  .pill-count {
    font-size: 11px;
    padding: 1px 5px;
    border-radius: 10px;
    background: rgba(0, 0, 0, 0.3);
    font-weight: 600;
  }

  /* Grid Scroll Area */
  .grid-scroll-area {
    flex: 1;
    overflow-y: auto;
    padding: 16px 20px 30px 20px;
  }

  .icons-grid {
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(88px, 1fr));
    gap: 8px;
  }

  .icon-tile-btn {
    position: relative;
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    gap: 8px;
    height: 80px;
    padding: 8px 6px;
    border-radius: 8px;
    background: var(--card-bg, #1f1f1f);
    border: none;
    color: var(--text-primary, #ffffff);
    cursor: pointer;
    transition: background 0.14s cubic-bezier(0.16, 1, 0.3, 1), transform 0.14s cubic-bezier(0.16, 1, 0.3, 1), box-shadow 0.14s cubic-bezier(0.16, 1, 0.3, 1);
  }

  .icon-tile-btn:hover {
    background: #2a2a2a;
    transform: translateY(-2px);
    box-shadow: 0 4px 12px rgba(0, 0, 0, 0.35);
  }

  .icon-tile-btn.selected {
    background: #262626;
    box-shadow: inset 0 0 0 2px var(--accent-color, #1db954);
  }

  .icon-tile-btn.selected .tile-label {
    color: #ffffff;
    font-weight: 600;
  }

  .star-badge {
    position: absolute;
    top: 4px;
    right: 5px;
    font-size: 10px;
    color: #ffd043;
    line-height: 1;
  }

  .tile-icon-box {
    display: flex;
    align-items: center;
    justify-content: center;
    width: 32px;
    height: 32px;
  }

  .tile-label {
    font-size: 11px;
    color: var(--text-secondary, rgba(255, 255, 255, 0.7));
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
    width: 100%;
    text-align: center;
  }

  /* Empty State */
  .empty-results-state {
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    gap: 12px;
    height: 300px;
    color: var(--text-tertiary, rgba(255, 255, 255, 0.45));
  }

  .empty-title {
    font-size: 14px;
    margin: 0;
  }

  .clear-filter-btn {
    padding: 6px 14px;
    border-radius: 6px;
    background: rgba(255, 255, 255, 0.08);
    border: none;
    color: var(--text-primary, #ffffff);
    font-size: 12px;
    font-weight: 500;
    cursor: pointer;
    transition: all 0.15s ease;
  }

  .clear-filter-btn:hover {
    background: rgba(255, 255, 255, 0.16);
  }
</style>
