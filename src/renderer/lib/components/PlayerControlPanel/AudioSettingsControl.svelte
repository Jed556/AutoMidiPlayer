<script lang="ts">
  import { onMount } from 'svelte';
  import { createFrameResizeObserver } from '../../utils/frameResizeObserver';
  import { playbackStore } from '../../state/playbackStore.svelte';
  import { animationStore } from '../../state/animationStore.svelte';
  import { uiStore } from '../../state/uiStore.svelte';
  import { popupScaleIn } from '../../utils/transitions';
  import { createDropdownState } from '../../ui/dropdownState.svelte';
  import { t } from '../../i18n';
  import { generateKeyOptions, getKeyNoteNameWithOctave } from '../../utils/musicConstants';
  import FluentIcon from '../Common/FluentIcon.svelte';
  import Slider from '../Common/Slider.svelte';
  import GradientSweep from '../Common/GradientSweep.svelte';
  import CustomScrollViewer from '../Common/CustomScrollViewer.svelte';
  import { tooltipManager } from '../../ui/tooltipManager.svelte';
  import type { TransposeMode } from '$shared/types/midi';

  // Dropdown open states
  const keyDropdown = createDropdownState();
  const speedDropdown = createDropdownState();
  const transposeDropdown = createDropdownState();
  const volumeDropdown = createDropdownState();
  let keyMenuOpen = $derived(keyDropdown.isOpen);
  let speedMenuOpen = $derived(speedDropdown.isOpen);
  let transposeMenuOpen = $derived(transposeDropdown.isOpen);
  let volumeMenuOpen = $derived(volumeDropdown.isOpen);
  let prevVolume = $state(0.85);
  let isVolumeInline = $state(false);

  function observePanelWidth(node: HTMLElement) {
    const panel = node.closest('.player-control-panel');
    if (!panel) return;
    const observer = createFrameResizeObserver(([entry]) => {
      isVolumeInline = entry.contentRect.width >= 1200;
      if (isVolumeInline) volumeDropdown.isOpen = false;
    });
    observer.observe(panel);
    return { destroy: () => observer.disconnect() };
  }

  function toggleMute() {
    if (playbackStore.volume > 0) {
      prevVolume = playbackStore.volume;
      void playbackStore.setVolume(0);
    } else {
      void playbackStore.setVolume(prevVolume > 0 ? prevVolume : 0.85);
    }
  }

  let isAnyMenuOpen = $derived(keyMenuOpen || speedMenuOpen || transposeMenuOpen || volumeMenuOpen);

  $effect(() => {
    uiStore.isBottomPickerActive = isAnyMenuOpen;
  });

  let isMediaCompact = $state(typeof window !== 'undefined' && Boolean(window.matchMedia?.('(max-width: 900px)').matches));

  onMount(() => {
    if (typeof window === 'undefined' || !window.matchMedia) return;
    const mql = window.matchMedia('(max-width: 900px)');
    isMediaCompact = mql.matches;
    const handler = (e: MediaQueryListEvent) => {
      isMediaCompact = e.matches;
    };
    mql.addEventListener('change', handler);
    return () => mql.removeEventListener('change', handler);
  });

  let isCompact = $derived(isMediaCompact || uiStore.windowWidth <= 900);

  // Keep zero relative to the corrected base key; the picker edits the manual offset.
  const keyOffsets = $derived(generateKeyOptions(playbackStore.keyOffsetBase)
    .map(item => ({ offset: item.value, note: getKeyNoteNameWithOctave(
      item.value + playbackStore.keyOffsetBase),
    })));

  const speedOptions = [
    0.25, 0.5, 0.75, 1.0, 1.25, 1.5, 1.75, 2.0, 2.5, 3.0
  ];

  const transposeModes: { id: TransposeMode; icon: string }[] = [
    { id: 'Smart', icon: 'TransposeSmart' },
    { id: 'Ignore', icon: 'TransposeIgnore' },
    { id: 'Up', icon: 'TransposeUp' },
    { id: 'Down', icon: 'TransposeDown' },
  ];

  let currentKeyNote = $derived(getKeyNoteNameWithOctave(playbackStore.keyOffset));

  const adjustment = $derived.by(() => {
    const active = playbackStore.activeKeyAdjustment;
    if (!active) return null;
    const { marker, offset } = active;
    const value = getKeyNoteNameWithOctave(offset);
    return {
      marker, value,
      tooltip: marker
        ? t('player.markerKeyAdjustment', { value, time: (marker.timeMs / 1000).toFixed(3) })
        : t('player.smartKeyAdjustment', { value }),
    };
  });

  let currentTransposeIcon = $derived.by(() => {
    const mode = playbackStore.transposeMode;
    if (mode === 'Smart') return 'TransposeSmart';
    if (mode === 'Up') return 'TransposeUp';
    if (mode === 'Down') return 'TransposeDown';
    return 'TransposeIgnore';
  });

  let isKeyActive = $derived(playbackStore.keyOffset !== 0);
  let isSpeedActive = $derived(playbackStore.playbackSpeed !== 1.0);
  let isTransposeActive = $derived(playbackStore.transposeMode !== 'Ignore');

  let openDownward = $state(false);

  function checkAvailableSpace(e: MouseEvent) {
    const trigger = e.currentTarget as HTMLElement | null;
    if (trigger) {
      const rect = trigger.getBoundingClientRect();
      const spaceAbove = rect.top;
      const spaceBelow = window.innerHeight - rect.bottom;
      openDownward = spaceAbove < 220 && spaceBelow > spaceAbove;
    }
  }

  function closeAll() {
    keyDropdown.isOpen = false;
    speedDropdown.isOpen = false;
    transposeDropdown.isOpen = false;
    volumeDropdown.isOpen = false;
    tooltipManager.hide();
  }

  function selectKey(offset: number) {
    playbackStore.setKeyOffset(offset);
    closeAll();
  }

  function selectSpeed(s: number) {
    playbackStore.playbackSpeed = s;
    closeAll();
  }

  function selectTranspose(mode: TransposeMode) {
    playbackStore.setTransposeMode(mode);
    closeAll();
  }

  // Shared hidden measurement ruler to read target text width without causing live DOM reflows
  let measurementRuler: HTMLSpanElement | null = null;

  function measureNaturalWidth(text: string, node: HTMLElement): number {
    if (typeof document === 'undefined') return 0;
    if (!measurementRuler || !document.body.contains(measurementRuler)) {
      measurementRuler = document.createElement('span');
      measurementRuler.setAttribute('aria-hidden', 'true');
      measurementRuler.style.position = 'fixed';
      measurementRuler.style.top = '-9999px';
      measurementRuler.style.left = '-9999px';
      measurementRuler.style.visibility = 'hidden';
      measurementRuler.style.pointerEvents = 'none';
      measurementRuler.style.whiteSpace = 'nowrap';
      measurementRuler.style.height = 'auto';
      measurementRuler.style.width = 'auto';
      measurementRuler.style.padding = '0';
      measurementRuler.style.margin = '0';
      measurementRuler.style.border = 'none';
      measurementRuler.style.boxSizing = 'border-box';
      measurementRuler.style.display = 'inline-block';
      document.body.appendChild(measurementRuler);
    }

    const style = window.getComputedStyle(node);
    measurementRuler.style.fontFamily = style.fontFamily;
    measurementRuler.style.fontSize = style.fontSize;
    measurementRuler.style.fontWeight = style.fontWeight;
    measurementRuler.style.letterSpacing = style.letterSpacing;
    measurementRuler.style.fontVariantNumeric = style.fontVariantNumeric;
    measurementRuler.textContent = text;
    return measurementRuler.getBoundingClientRect().width;
  }

  function smoothWidth(node: HTMLElement, trigger: any) {
    let currentListener: ((e: TransitionEvent) => void) | null = null;

    function cleanup() {
      if (currentListener) {
        node.removeEventListener('transitionend', currentListener);
        currentListener = null;
      }
    }

    function parseTrigger(val: any): { text: string; compact: boolean } {
      if (val && typeof val === 'object' && 'text' in val) {
        return { text: String(val.text ?? ''), compact: Boolean(val.compact) };
      }
      return { text: String(val ?? ''), compact: false };
    }

    const initial = parseTrigger(trigger);
    let lastText = initial.text;
    let lastCompact = initial.compact;

    // Set initial width to lock the layout and prevent snapping when text changes
    const initialNaturalWidth = Math.ceil(measureNaturalWidth(initial.text, node));
    const initialWidth = initial.compact ? 0 : initialNaturalWidth;
    node.style.width = `${initialWidth}px`;
    node.style.opacity = initial.compact ? '0' : '1';
    node.style.setProperty('--mask-left-btn', initial.compact ? 'transparent' : '#000000');

    requestAnimationFrame(() => {
      const parsed = parseTrigger(trigger);
      const natW = Math.ceil(measureNaturalWidth(parsed.text, node));
      const w = parsed.compact ? 0 : natW;
      if (!node.style.width || Math.abs(node.getBoundingClientRect().width - w) > 1) {
        node.style.width = `${w}px`;
        node.style.opacity = parsed.compact ? '0' : '1';
        node.style.setProperty('--mask-left-btn', parsed.compact ? 'transparent' : '#000000');
      }
    });

    return {
      update(newTrigger: any) {
        const { text: newText, compact: isCompact } = parseTrigger(newTrigger);
        if (newText === lastText && isCompact === lastCompact) {
          return;
        }
        lastText = newText;
        lastCompact = isCompact;

        cleanup();

        const naturalWidth = Math.ceil(measureNaturalWidth(newText, node));
        const targetWidth = isCompact ? 0 : naturalWidth;
        const targetOpacity = isCompact ? '0' : '1';

        // Current visual width (handles mid-flight interrupted animations smoothly)
        const currentVisualWidth = node.getBoundingClientRect().width;
        const startWidth = currentVisualWidth >= 0 ? currentVisualWidth : targetWidth;

        const isOff = animationStore.isOff || document.documentElement.classList.contains('no-animations');
        if (isOff || Math.abs(startWidth - targetWidth) < 0.5) {
          node.style.transition = 'none';
          node.style.width = `${targetWidth}px`;
          node.style.opacity = targetOpacity;
          node.style.setProperty('--mask-left-btn', isCompact ? 'transparent' : '#000000');
          return;
        }

        const durationSec = Math.max(0.1, animationStore.timing);
        const easeCurve = 'cubic-bezier(0.4, 0, 0.2, 1)';

        // Anchor start position without transition
        node.style.transition = 'none';
        node.style.width = `${startWidth}px`;

        let opacityTransition: string;
        let maskTransition: string;
        let startMask: string;
        let targetMask: string;

        if (isCompact) {
          // Shrinking into compact mode:
          // Keep opacity solid (1.0) while width shrinks past the initial characters,
          // delaying the fade until ~1 character is left (the last 45% of the animation).
          const fadeDuration = (durationSec * 0.45).toFixed(2);
          const fadeDelay = (durationSec * 0.55).toFixed(2);
          opacityTransition = `opacity ${fadeDuration}s ${easeCurve} ${fadeDelay}s`;

          startMask = '#000000';
          targetMask = 'transparent';
          maskTransition = `--mask-left-btn ${durationSec}s ${easeCurve}`;
        } else {
          // Expanding out to full width:
          // Opacity fades in smoothly right from the start as characters emerge.
          opacityTransition = `opacity ${(durationSec * 0.85).toFixed(2)}s ${easeCurve} 0s`;

          startMask = 'transparent';
          targetMask = '#000000';
          maskTransition = `--mask-left-btn ${durationSec}s ${easeCurve}`;
        }

        node.style.setProperty('--mask-left-btn', startMask);
        void node.offsetWidth; // Force synchronous layout reflow so start frame is registered

        node.style.transition = `width var(--anim-duration, ${durationSec}s) ${easeCurve}, ${opacityTransition}, ${maskTransition}`;
        node.style.width = `${targetWidth}px`;
        node.style.opacity = targetOpacity;
        node.style.setProperty('--mask-left-btn', targetMask);

        currentListener = (e: TransitionEvent) => {
          if (e.propertyName !== 'width') return;
          cleanup();
          // Lock target values without abruptly setting transition='none' which can clip in-flight opacity
          node.style.width = `${targetWidth}px`;
          node.style.opacity = targetOpacity;
          node.style.setProperty('--mask-left-btn', targetMask);
        };
        node.addEventListener('transitionend', currentListener);
      },
      destroy() {
        cleanup();
        node.style.removeProperty('--mask-left-btn');
      }
    };
  }
</script>

{#snippet volumeSlider()}
  <Slider
    variant="seekbar"
    ariaLabel={t('player.volumeTooltip')}
    formatValue={(v) => `${Math.round(v * 100)}%`}
    min={0}
    max={2}
    step={0.01}
    enableWheel={true}
    wheelStep={0.05}
    areas={[
      { min: 0, max: 1, type: 'sliding', step: 0.01, color: 'var(--slider-fill-color)' },
      { min: 1, max: 2, type: 'sliding', step: 0.01, color: 'var(--slider-boost-color)' }
    ]}
    snapPoints={[1]}
    snapRadius={0.04}
    value={playbackStore.volume}
    oninput={(val) => playbackStore.setVolume(val)}
    onchange={(val) => playbackStore.setVolume(val)}
    formatTooltip={(v) => `${Math.round(v * 100)}%`}
  />
{/snippet}

{#snippet volumeControls()}
  <div class="volume-popup-body">
    <button
      type="button"
      class="vol-mute-btn"
      onclick={toggleMute}
      aria-label={playbackStore.volume === 0 ? 'Unmute' : 'Mute'}
      title={playbackStore.volume === 0 ? 'Unmute' : 'Mute'}
    >
      <FluentIcon
        name={playbackStore.volume === 0 ? 'Volume0' : playbackStore.volume < 0.33 ? 'Volume1' : playbackStore.volume < 0.66 ? 'Volume2' : 'Volume3'}
        volume={playbackStore.volume}
        size={16}
      />
    </button>
    <div class="volume-slider-container">
      {@render volumeSlider()}
    </div>
    <span class="volume-pct-label" class:is-overshoot={playbackStore.volume > 1}>{Math.round(playbackStore.volume * 100)}%</span>
  </div>
{/snippet}

<svelte:window onclick={(e) => {
  const target = e.target as HTMLElement;
  if (!target.closest('.selector-dropdown')) {
    closeAll();
  }
}} />

<div class="audio-settings-control" class:compact={isCompact} data-tooltip-disabled={isAnyMenuOpen ? 'true' : undefined}>
  <!-- Key Dropdown -->
  <div class="selector-dropdown">
    <button
      class="pill-btn"
      class:compact={isCompact}
      class:active={isKeyActive}
      class:open={keyMenuOpen}
      data-tooltip-disabled={keyMenuOpen || isAnyMenuOpen ? 'true' : undefined}
      onclick={(e) => { e.stopPropagation(); tooltipManager.hide(); checkAvailableSpace(e); keyDropdown.isOpen = !keyMenuOpen; }}
      title={keyMenuOpen || isAnyMenuOpen ? undefined : `${t('player.keyOffsetTooltip')} (${currentKeyNote})`}
    >
      <FluentIcon name="MusicNote1" size={18} class="btn-icon" />
      <span class="btn-text" use:smoothWidth={{ text: currentKeyNote, compact: isCompact }}>{currentKeyNote}</span>
    </button>

    {#if adjustment}
      <span class="key-adjustment" title={adjustment.tooltip} aria-label={adjustment.tooltip}>
        <FluentIcon name={adjustment.marker ? 'Location' : 'Checkmark'} filled={Boolean(adjustment.marker)} size={10} />
        <span class="btn-text adjustment-text" use:smoothWidth={{ text: adjustment.value, compact: isCompact }}>{adjustment.value}</span>
      </span>
    {/if}

    {#if keyMenuOpen}
      <div
        class="dropdown-popup key-popup"
        use:keyDropdown.layer
        class:open-downward={openDownward}
        in:popupScaleIn
        out:keyDropdown.outro
      >
        <CustomScrollViewer
          class="audio-popup-scroll-viewer"
          contentClass="popup-scroll"
          padding="4px"
          gap="1px"
          fadeHeight={20}
          fadeRampDistance={18}
        >
          {#each keyOffsets as item}
            {@const isSelected = item.offset === playbackStore.keyOffset}
            <button
              class="menu-item key-item"
              class:selected={isSelected}
              onclick={() => selectKey(item.offset)}
            >
              <span class="offset-val">{item.offset > 0 ? `+${item.offset}` : item.offset}</span>
              <span class="note-val">{item.note}</span>
            </button>
          {/each}
        </CustomScrollViewer>
      </div>
    {/if}
  </div>

  <!-- Speed Dropdown -->
  <div class="selector-dropdown">
    <button
      class="pill-btn"
      class:compact={isCompact}
      class:active={isSpeedActive}
      class:open={speedMenuOpen}
      data-tooltip-disabled={speedMenuOpen || isAnyMenuOpen ? 'true' : undefined}
      onclick={(e) => { e.stopPropagation(); tooltipManager.hide(); checkAvailableSpace(e); speedDropdown.isOpen = !speedMenuOpen; }}
      title={speedMenuOpen || isAnyMenuOpen ? undefined : `${t('player.speedTooltip')} (${playbackStore.playbackSpeed}x)`}
    >
      <FluentIcon name="TopSpeed" speed={playbackStore.playbackSpeed} size={18} class="btn-icon" />
      <span class="btn-text" use:smoothWidth={{ text: `${playbackStore.playbackSpeed}x`, compact: isCompact }}>{playbackStore.playbackSpeed}x</span>
    </button>

    {#if speedMenuOpen}
      <div
        class="dropdown-popup speed-popup"
        use:speedDropdown.layer
        class:open-downward={openDownward}
        in:popupScaleIn
        out:speedDropdown.outro
      >
        <CustomScrollViewer
          class="audio-popup-scroll-viewer"
          contentClass="popup-scroll"
          padding="4px"
          gap="1px"
          fadeHeight={20}
          fadeRampDistance={18}
        >
          {#each speedOptions as s}
            {@const isSelected = s === playbackStore.playbackSpeed}
            <button
              class="menu-item speed-item"
              class:selected={isSelected}
              onclick={() => selectSpeed(s)}
            >
              <span class="speed-val">{s.toFixed(2).replace(/\.?0+$/, '')}x</span>
            </button>
          {/each}
        </CustomScrollViewer>
      </div>
    {/if}
  </div>

  <!-- Transpose Mode Dropdown -->
  <div class="selector-dropdown">
    <button
      class="pill-btn"
      class:compact={isCompact}
      class:active={isTransposeActive}
      class:open={transposeMenuOpen}
      data-tooltip-disabled={transposeMenuOpen || isAnyMenuOpen ? 'true' : undefined}
      onclick={(e) => { e.stopPropagation(); tooltipManager.hide(); checkAvailableSpace(e); transposeDropdown.isOpen = !transposeMenuOpen; }}
      title={transposeMenuOpen || isAnyMenuOpen ? undefined : `${t('player.transposeTooltip')} (${t('player.transpose' + playbackStore.transposeMode)})`}
    >
      <FluentIcon name={currentTransposeIcon} size={20} class="btn-icon" />
      <span class="btn-text" use:smoothWidth={{ text: t('player.transpose' + playbackStore.transposeMode), compact: isCompact }}>{t('player.transpose' + playbackStore.transposeMode)}</span>
    </button>

    {#if transposeMenuOpen}
      <div
        class="dropdown-popup transpose-popup"
        use:transposeDropdown.layer
        class:open-downward={openDownward}
        in:popupScaleIn
        out:transposeDropdown.outro
      >
        <CustomScrollViewer
          class="audio-popup-scroll-viewer"
          contentClass="popup-scroll"
          padding="4px"
          gap="1px"
          fadeHeight={20}
          fadeRampDistance={18}
        >
          {#each transposeModes as item}
            {@const isSelected = item.id === playbackStore.transposeMode}
            <button
              class="menu-item transpose-item"
              class:selected={isSelected}
              onclick={() => selectTranspose(item.id)}
            >
              <FluentIcon name={item.icon} size={18} />
              <span class="transpose-text">{t('player.transpose' + item.id)}</span>
            </button>
          {/each}
        </CustomScrollViewer>
      </div>
    {/if}
  </div>

  <!-- Volume stays inline on wide panels and uses a popup on smaller panels. -->
  <div class="selector-dropdown volume-selector" use:observePanelWidth>
    <button
      class="pill-btn volume-trigger"
      class:compact={isCompact || isVolumeInline}
      class:active={!isVolumeInline && playbackStore.volume > 0}
      class:open={volumeMenuOpen}
      class:is-overshoot={playbackStore.volume > 1}
      data-tooltip-disabled={volumeMenuOpen || isAnyMenuOpen ? 'true' : undefined}
      onclick={(e) => {
        e.stopPropagation();
        tooltipManager.hide();
        if (isVolumeInline) {
          toggleMute();
        } else {
          volumeDropdown.isOpen = !volumeMenuOpen;
        }
      }}
      title={volumeMenuOpen || isAnyMenuOpen ? undefined : `${t('player.volumeTooltip')} (${Math.round(playbackStore.volume * 100)}%)`}
    >
      <FluentIcon
        name={playbackStore.volume === 0 ? 'Volume0' : playbackStore.volume < 0.33 ? 'Volume1' : playbackStore.volume < 0.66 ? 'Volume2' : 'Volume3'}
        volume={playbackStore.volume}
        size={16}
        class="btn-icon"
      />
      <span class="btn-text" use:smoothWidth={{ text: `${Math.round(playbackStore.volume * 100)}%`, compact: isCompact || isVolumeInline }}>{Math.round(playbackStore.volume * 100)}%</span>
    </button>

    <GradientSweep visible={isVolumeInline} anchor="left" overflowVisibleOnComplete={false}>
      <div class="inline-volume-slider" inert={!isVolumeInline}>
        {@render volumeSlider()}
      </div>
    </GradientSweep>

    {#if volumeMenuOpen}
      <div
        class="dropdown-popup volume-popup"
        use:volumeDropdown.layer
        in:popupScaleIn
        out:volumeDropdown.outro
        role="presentation"
        onclick={(e) => e.stopPropagation()}
        onkeydown={(e) => e.stopPropagation()}
      >
        {@render volumeControls()}
      </div>
    {/if}
  </div>
</div>

<style>
  .audio-settings-control {
    display: flex;
    align-items: center;
    justify-content: flex-end;
    gap: 8px;
    width: 100%;
    min-width: 0;
    user-select: none;
    justify-self: end;
    transition: gap var(--anim-duration, 0.25s) cubic-bezier(0.4, 0, 0.2, 1);
  }

  .audio-settings-control.compact {
    gap: 4px;
  }

  .selector-dropdown {
    position: relative;
    display: flex;
    align-items: center;
    flex-shrink: 0;
  }

  .key-adjustment {
    position: absolute;
    top: calc(100% - 7px);
    right: 8px;
    display: flex;
    align-items: center;
    gap: 2px;
    color: var(--accent-color, var(--accent, #1DB954));
    font-size: 10px;
    line-height: 12px;
    white-space: nowrap;
    transition: gap var(--anim-duration, 0.25s) cubic-bezier(0.4, 0, 0.2, 1),
                right var(--anim-duration, 0.25s) cubic-bezier(0.4, 0, 0.2, 1);
  }

  .key-adjustment .adjustment-text {
    font-size: inherit;
    line-height: inherit;
  }

  .compact .key-adjustment {
    gap: 0;
    right: 11px;
  }

  /* Ghost button style matching WPF ButtonGhostDimmed & MainPlaybackControl .ctrl-btn */
  .pill-btn {
    display: inline-flex;
    align-items: center;
    justify-content: center;
    gap: 6px;
    padding: 0 8px;
    height: 32px;
    min-height: 32px;
    min-width: 32px;
    border-radius: var(--radius-sm, 4px);
    background: transparent;
    border: none;
    color: var(--text-primary);
    cursor: pointer;
    font-size: 12px;
    font-weight: 500;
    opacity: 0.35;
    white-space: nowrap;
    box-sizing: border-box;
    flex-shrink: 0;
    transition: opacity var(--transition-normal, 0.22s cubic-bezier(0, 0, 0.2, 1)),
                color var(--transition-fast, 0.16s ease),
                gap var(--anim-duration, 0.25s) cubic-bezier(0.4, 0, 0.2, 1),
                padding var(--anim-duration, 0.25s) cubic-bezier(0.4, 0, 0.2, 1);
    outline: none;
    user-select: none;
  }

  .pill-btn:hover {
    background: transparent;
    opacity: 1;
  }

  .pill-btn:active {
    opacity: 0.5;
  }

  .pill-btn.active {
    color: var(--accent-color, var(--accent, #1DB954));
    opacity: 0.8;
  }

  .pill-btn.active:hover {
    color: var(--accent-color, var(--accent, #1DB954));
    opacity: 1;
  }

  .pill-btn.open {
    opacity: 1;
  }

  .pill-btn.compact {
    gap: 0;
    padding: 0;
  }

  .btn-text {
    line-height: 1.2;
    font-size: 12px;
    display: inline-flex;
    align-items: center;
    justify-content: flex-start;
    vertical-align: middle;
    white-space: nowrap;
    overflow: hidden;
    box-sizing: border-box;
    flex-shrink: 0;
    --mask-left-btn: #000000;
    -webkit-mask-image: linear-gradient(
      to left,
      var(--mask-left-btn) 0%,
      #000000 1ch,
      #000000 100%
    );
    mask-image: linear-gradient(
      to left,
      var(--mask-left-btn) 0%,
      #000000 1ch,
      #000000 100%
    );
    -webkit-mask-size: 100% 100%;
    mask-size: 100% 100%;
    -webkit-mask-repeat: no-repeat;
    mask-repeat: no-repeat;
  }

  .pill-btn.compact .btn-text {
    --mask-left-btn: transparent;
  }

  .pill-btn :global(.btn-icon) {
    flex-shrink: 0;
  }

  /* Dropdown popup menu styled to match WPF WPF-UI Popups */
  .dropdown-popup {
    position: absolute;
    bottom: calc(100% + 6px);
    right: 0;
    background: var(--bg-dialog, var(--card-bg, #27272A));
    border: none;
    border-radius: var(--radius-md, 8px);
    box-shadow: var(--popup-shadow);
    z-index: 120;
    overflow: hidden;
    transform-origin: bottom right;
    box-sizing: border-box;
  }

  .key-popup {
    min-width: 88px;
    width: max-content;
  }

  .speed-popup {
    min-width: 64px;
    width: max-content;
  }

  .transpose-popup {
    min-width: 80px;
    width: max-content;
  }

  :global(.audio-popup-scroll-viewer) {
    max-height: 220px;
    width: 100%;
  }

  .menu-item {
    display: flex;
    align-items: center;
    justify-content: space-between;
    width: 100%;
    padding: 6px 8px;
    border-radius: 4px;
    background: transparent;
    border: none;
    color: var(--text-primary);
    font-size: 12px;
    font-family: inherit;
    cursor: pointer;
    text-align: left;
    transition: background var(--transition-fast);
  }

  .menu-item:hover {
    background: var(--control-fill-hover);
  }

  .menu-item.selected {
    font-weight: 600;
    color: var(--accent-color, var(--accent, #1DB954));
    background: var(--accent-subtle, rgba(29, 185, 84, 0.12));
  }

  .key-item {
    display: grid;
    grid-template-columns: 24px 1fr;
    gap: 6px;
    padding: 6px 8px;
  }

  .offset-val {
    text-align: right;
    opacity: 0.7;
    font-variant-numeric: tabular-nums;
  }

  .note-val {
    text-align: left;
  }

  .speed-item {
    justify-content: flex-end;
    text-align: right;
    padding: 6px 8px;
  }

  .speed-val {
    width: 100%;
    text-align: right;
    font-variant-numeric: tabular-nums;
  }

  .transpose-item {
    display: flex;
    align-items: center;
    justify-content: flex-end;
    gap: 6px;
    padding: 6px 8px;
  }

  .transpose-text {
    flex: 1;
    text-align: left;
  }

  .volume-popup {
    min-width: 200px;
    width: max-content;
    padding: 8px 12px;
  }

  .volume-popup-body {
    display: flex;
    align-items: center;
    gap: 10px;
  }

  .vol-mute-btn {
    background: transparent;
    border: none;
    color: var(--text-primary);
    cursor: pointer;
    display: flex;
    align-items: center;
    justify-content: center;
    padding: 2px;
    opacity: 0.8;
    border-radius: 4px;
    flex-shrink: 0;
    transition: opacity var(--transition-fast), color var(--transition-fast);
  }

  .vol-mute-btn:hover {
    opacity: 1;
    color: var(--accent-color, var(--accent, #1DB954));
  }

  .volume-slider-container {
    flex: 1;
    min-width: 130px;
  }

  .volume-pct-label {
    font-size: 11px;
    font-variant-numeric: tabular-nums;
    color: var(--text-primary);
    opacity: 0.8;
    min-width: 34px;
    text-align: right;
    flex-shrink: 0;
    transition: color var(--transition-fast, 0.15s ease), opacity var(--transition-fast, 0.15s ease);
  }

  .volume-pct-label.is-overshoot {
    color: var(--warning-color, #F59E0B);
    opacity: 1;
    font-weight: 600;
  }

  .pill-btn.is-overshoot .btn-text,
  .pill-btn.is-overshoot :global(.btn-icon) {
    color: var(--warning-color, #F59E0B) !important;
  }

  .volume-trigger {
    opacity: 0.8;
  }

  .inline-volume-slider {
    width: clamp(80px, 7cqi, 110px);
    flex-shrink: 0;
  }

  .pill-btn:focus-visible,
  .vol-mute-btn:focus-visible {
    outline: 2px solid var(--accent-color, #1DB954);
    outline-offset: 2px;
  }


</style>
