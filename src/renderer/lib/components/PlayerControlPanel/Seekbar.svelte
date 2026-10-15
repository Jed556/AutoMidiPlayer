<script lang="ts">
  import { onMount, onDestroy } from 'svelte';
  import { createFrameResizeObserver } from '../../utils/frameResizeObserver';
  import { portal } from '../../actions/portal';
  import { animationStore } from '../../state/animationStore.svelte';

  interface Props {
    songId?: string;
    value?: number;
    min?: number;
    max?: number;
    minimum?: number;
    maximum?: number;
    tickFrequency?: number;
    isSnapToTickEnabled?: boolean;
    animateThumbTransitions?: boolean;
    animationDurationMs?: number;
    thumbToolTipOptions?: string;
    thumbToolTipFallback?: string;
    disabled?: boolean;
    previewValue?: number;
    isPreviewActive?: boolean;
    isSelectionRangeEnabled?: boolean;
    selectionStart?: number;
    selectionEnd?: number;
    class?: string;
    onseek?: (val: number, options?: { immediate?: boolean }) => void;
    onchange?: (val: number) => void;
    onpreview?: (val: number, active: boolean) => void;
  }

  let {
    songId,
    value = $bindable(0),
    min = 0,
    max = 100,
    minimum,
    maximum,
    tickFrequency = 1,
    isSnapToTickEnabled = false,
    animateThumbTransitions = true,
    animationDurationMs,
    thumbToolTipOptions = '',
    thumbToolTipFallback = '0:00',
    disabled = false,
    previewValue = $bindable(0),
    isPreviewActive = $bindable(false),
    isSelectionRangeEnabled = false,
    selectionStart = 0,
    selectionEnd = 0,
    class: className = '',
    onseek,
    onchange,
    onpreview
  }: Props = $props();

  // Support both min/max and minimum/maximum property aliases matching WPF
  let effectiveMin = $derived(minimum !== undefined ? minimum : min);
  let effectiveMax = $derived(maximum !== undefined ? maximum : max);

  // Link animation duration to animationStore in Settings
  let activeAnimDurationMs = $derived(
    !animateThumbTransitions || animationStore.isOff
      ? 0
      : (animationDurationMs !== undefined ? animationDurationMs : animationStore.durationMs)
  );

  let isHovered = $state(false);
  let isPointerDown = $state(false);
  let isScrubbing = $state(false);

  let startPointerX = 0;
  let startPointerY = 0;
  let pendingClickValue = 0;
  let isEasingToClick = $state(false);
  let clickAnimTargetValue = $state(0);
  let currentDragValue = $state(0);

  let hoverPercent = $state(0);
  let showHoverFill = $state(false);
  let isTooltipVisible = $state(false);
  let tooltipText = $state('0:00');
  let tooltipX = $state(0);

  let containerElement: HTMLDivElement | null = $state(null);
  let trackElement: HTMLDivElement | null = $state(null);
  let popupElement: HTMLDivElement | null = $state(null);

  let containerWidth = $state(0);
  let popupWidth = $state(44);
  let hoverHideTimer: any = null;
  let easingResetTimer: any = null;

  $effect(() => {
    // When songId or duration changes or position resets externally while not holding pointer,
    // cancel any in-flight click-easing so seekbar snaps immediately.
    songId;
    effectiveMax;
    if (!isPointerDown) {
      isEasingToClick = false;
      isPreviewActive = false;
      showHoverFill = false;
      isTooltipVisible = false;
      if (easingResetTimer) {
        clearTimeout(easingResetTimer);
        easingResetTimer = null;
      }
    }
  });

  // The displayed value:
  // 1. Direct value while actively dragging/scrubbing
  // 2. Click target value during easing to clicked section
  // 3. Normal bound value (playback position)
  let displayValue = $derived(
    isScrubbing ? currentDragValue : (isEasingToClick ? clickAnimTargetValue : value)
  );

  let progressPercent = $derived(
    effectiveMax > effectiveMin
      ? Math.max(0, Math.min(100, ((displayValue - effectiveMin) / (effectiveMax - effectiveMin)) * 100))
      : 0
  );

  let selectionRangeLeft = $derived(
    effectiveMax > effectiveMin
      ? Math.max(0, Math.min(100, ((selectionStart - effectiveMin) / (effectiveMax - effectiveMin)) * 100))
      : 0
  );

  let selectionRangeWidth = $derived(
    effectiveMax > effectiveMin && selectionEnd > selectionStart
      ? Math.max(0, Math.min(100 - selectionRangeLeft, ((selectionEnd - selectionStart) / (effectiveMax - effectiveMin)) * 100))
      : 0
  );

  // Tooltip horizontal clamping within container bounds (matches WPF ComputePopupOffsets)
  let clampedPopupLeft = $derived.by(() => {
    if (!containerWidth || containerWidth <= 0) return 0;
    const halfW = (popupWidth || 44) / 2;
    return Math.max(halfW, Math.min(containerWidth - halfW, tooltipX));
  });

  let fixedPopupTop = $state(0);
  let fixedPopupLeft = $state(0);

  function updateFixedPopupCoords() {
    if (!containerElement) return;
    const rect = containerElement.getBoundingClientRect();
    const halfW = (popupWidth || 44) / 2;
    let targetLeft = rect.left + clampedPopupLeft;
    let targetTop = rect.top - 8;

    // Clamp horizontally to stay inside viewport
    if (targetLeft - halfW < 8) {
      targetLeft = halfW + 8;
    } else if (targetLeft + halfW > window.innerWidth - 8) {
      targetLeft = window.innerWidth - halfW - 8;
    }

    fixedPopupTop = targetTop;
    fixedPopupLeft = targetLeft;
  }

  $effect(() => {
    if (isTooltipVisible) {
      updateFixedPopupCoords();
      const onScroll = () => updateFixedPopupCoords();
      const onResize = () => updateFixedPopupCoords();
      window.addEventListener('scroll', onScroll, true);
      window.addEventListener('resize', onResize);
      return () => {
        window.removeEventListener('scroll', onScroll, true);
        window.removeEventListener('resize', onResize);
      };
    }
  });

  function snap(raw: number): number {
    const clamped = Math.max(effectiveMin, Math.min(effectiveMax, raw));
    if (!isSnapToTickEnabled || tickFrequency <= 0) {
      return clamped;
    }
    const steps = Math.round((clamped - effectiveMin) / tickFrequency);
    const snapped = effectiveMin + steps * tickFrequency;
    return Math.max(effectiveMin, Math.min(effectiveMax, snapped));
  }

  function formatPreviewText(val: number): string {
    if (thumbToolTipOptions) {
      const opts = thumbToolTipOptions
        .split('|')
        .map((s) => s.trim())
        .filter((s) => s.length > 0);
      if (opts.length > 0) {
        const idx = Math.round(val - effectiveMin);
        if (idx >= 0 && idx < opts.length) {
          return opts[idx];
        }
      }
    }

    const totalSeconds = Math.max(0, Math.round(val));
    const hours = Math.floor(totalSeconds / 3600);
    const minutes = Math.floor((totalSeconds % 3600) / 60);
    const seconds = totalSeconds % 60;
    const secStr = seconds < 10 ? `0${seconds}` : `${seconds}`;

    if (hours > 0) {
      const minStr = minutes < 10 ? `0${minutes}` : `${minutes}`;
      return `${hours}:${minStr}:${secStr}`;
    }
    return `${minutes}:${secStr}`;
  }

  function resolveTargetValueFromEvent(e: PointerEvent): { ratio: number; value: number; x: number } {
    if (!trackElement) return { ratio: 0, value: effectiveMin, x: 0 };
    const rect = trackElement.getBoundingClientRect();
    const x = Math.max(0, Math.min(rect.width, e.clientX - rect.left));
    const ratio = rect.width > 0 ? x / rect.width : 0;
    const raw = effectiveMin + ratio * (effectiveMax - effectiveMin);
    return { ratio, value: snap(raw), x };
  }

  function handlePointerEnter() {
    if (disabled) return;
    if (hoverHideTimer) {
      clearTimeout(hoverHideTimer);
      hoverHideTimer = null;
    }
    isHovered = true;
  }

  function handlePointerLeave(e: PointerEvent) {
    if (disabled) return;
    // Do not hide tooltip if mouse button is held down (during click or drag)
    if (isPointerDown || isScrubbing) return;

    // Check if moving to an inner element
    if (e.relatedTarget && containerElement?.contains(e.relatedTarget as Node)) {
      return;
    }

    if (hoverHideTimer) clearTimeout(hoverHideTimer);
    // 80ms delay matching WPF HoverHideDelayMs = 80
    hoverHideTimer = setTimeout(() => {
      isHovered = false;
      showHoverFill = false;
      isTooltipVisible = false;
    }, 80);
  }

  function handlePointerMove(e: PointerEvent) {
    if (disabled) return;
    if (hoverHideTimer) {
      clearTimeout(hoverHideTimer);
      hoverHideTimer = null;
    }
    isHovered = true;
    isTooltipVisible = true;

    const { ratio, value: resolvedVal, x } = resolveTargetValueFromEvent(e);

    if (isPointerDown) {
      // Check drag threshold (WPF HasExceededDragThreshold ~4px)
      if (!isScrubbing) {
        const dist = Math.hypot(e.clientX - startPointerX, e.clientY - startPointerY);
        if (dist >= 4) {
          isScrubbing = true;
          isEasingToClick = false;
          if (easingResetTimer) clearTimeout(easingResetTimer);
        }
      }

      if (isScrubbing) {
        currentDragValue = resolvedVal;
        previewValue = resolvedVal;
        tooltipText = formatPreviewText(resolvedVal);
        tooltipX = x;
        showHoverFill = false;
        onpreview?.(resolvedVal, true);
        onseek?.(resolvedVal, { immediate: true });
        return;
      }
    }

    // Normal hover movement (or click held within threshold)
    hoverPercent = ratio * 100;
    showHoverFill = true;
    tooltipText = formatPreviewText(resolvedVal);
    tooltipX = x;
    previewValue = resolvedVal;
    onpreview?.(resolvedVal, isPointerDown);
  }

  function handlePointerDown(e: PointerEvent) {
    if (disabled || e.button !== 0) return;
    e.preventDefault();

    if (hoverHideTimer) {
      clearTimeout(hoverHideTimer);
      hoverHideTimer = null;
    }
    if (easingResetTimer) {
      clearTimeout(easingResetTimer);
      easingResetTimer = null;
    }

    isPointerDown = true;
    isScrubbing = false;
    startPointerX = e.clientX;
    startPointerY = e.clientY;

    try {
      (e.currentTarget as HTMLElement).setPointerCapture(e.pointerId);
    } catch {
      // ignore
    }

    const { ratio, value: resolvedVal, x } = resolveTargetValueFromEvent(e);
    pendingClickValue = resolvedVal;
    currentDragValue = resolvedVal;
    clickAnimTargetValue = resolvedVal;

    // Start easing transition to clicked section immediately!
    isEasingToClick = true;
    isPreviewActive = true;
    previewValue = resolvedVal;
    tooltipText = formatPreviewText(resolvedVal);
    tooltipX = x;
    isTooltipVisible = true;
    showHoverFill = false;

    onpreview?.(resolvedVal, true);
    onseek?.(resolvedVal, { immediate: false });
  }

  function handlePointerUp(e: PointerEvent) {
    if (!isPointerDown) return;

    try {
      if ((e.currentTarget as HTMLElement).hasPointerCapture(e.pointerId)) {
        (e.currentTarget as HTMLElement).releasePointerCapture(e.pointerId);
      }
    } catch {
      // ignore
    }

    const finalVal = isScrubbing ? currentDragValue : pendingClickValue;
    const wasScrubbing = isScrubbing;
    isPointerDown = false;
    isScrubbing = false;
    isPreviewActive = false;

    // Commit seek to value
    value = finalVal;
    if (wasScrubbing) {
      onseek?.(finalVal, { immediate: true });
    }
    onchange?.(finalVal);

    // Allow easing animation to complete smoothly before relinquishing displayValue back to value
    if (wasScrubbing) {
      isEasingToClick = false;
    } else {
      if (easingResetTimer) clearTimeout(easingResetTimer);
      easingResetTimer = setTimeout(() => {
        isEasingToClick = false;
      }, Math.max(10, activeAnimDurationMs));
    }

    // Restore hover state if cursor is still over container
    if (containerElement) {
      const rect = containerElement.getBoundingClientRect();
      if (
        e.clientX >= rect.left &&
        e.clientX <= rect.right &&
        e.clientY >= rect.top &&
        e.clientY <= rect.bottom
      ) {
        isHovered = true;
        const { ratio, value: hoverVal, x } = resolveTargetValueFromEvent(e);
        hoverPercent = ratio * 100;
        showHoverFill = true;
        tooltipText = formatPreviewText(hoverVal);
        tooltipX = x;
        isTooltipVisible = true;
      } else {
        isHovered = false;
        showHoverFill = false;
        isTooltipVisible = false;
      }
    }
  }

  function handleLostPointerCapture() {
    if (!isPointerDown) return;
    const finalVal = isScrubbing ? currentDragValue : pendingClickValue;
    const wasScrubbing = isScrubbing;
    isPointerDown = false;
    isScrubbing = false;
    isPreviewActive = false;
    value = finalVal;
    onseek?.(finalVal);
    onchange?.(finalVal);

    if (wasScrubbing) {
      isEasingToClick = false;
    } else {
      if (easingResetTimer) clearTimeout(easingResetTimer);
      easingResetTimer = setTimeout(() => {
        isEasingToClick = false;
      }, Math.max(10, activeAnimDurationMs));
    }
  }

  function handleKeyDown(e: KeyboardEvent) {
    if (disabled) return;
    const step = isSnapToTickEnabled && tickFrequency > 0 ? tickFrequency : (effectiveMax - effectiveMin) / 100 || 1;
    let nextVal = value;

    if (e.key === 'ArrowLeft' || e.key === 'ArrowDown') {
      nextVal = Math.max(effectiveMin, value - step);
    } else if (e.key === 'ArrowRight' || e.key === 'ArrowUp') {
      nextVal = Math.min(effectiveMax, value + step);
    } else if (e.key === 'PageDown') {
      nextVal = Math.max(effectiveMin, value - step * 5);
    } else if (e.key === 'PageUp') {
      nextVal = Math.min(effectiveMax, value + step * 5);
    } else if (e.key === 'Home') {
      nextVal = effectiveMin;
    } else if (e.key === 'End') {
      nextVal = effectiveMax;
    } else {
      return;
    }

    e.preventDefault();
    value = snap(nextVal);
    onseek?.(value);
    onchange?.(value);
  }

  let resizeObserver: ReturnType<typeof createFrameResizeObserver> | null = null;

  onMount(() => {
    if (typeof ResizeObserver !== 'undefined' && containerElement) {
      resizeObserver = createFrameResizeObserver((entries) => {
        for (const entry of entries) {
          if (entry.target === containerElement) {
            containerWidth = entry.contentRect.width;
          } else if (entry.target === popupElement) {
            popupWidth = entry.contentRect.width;
          }
        }
      });
      resizeObserver.observe(containerElement);
      containerWidth = containerElement.getBoundingClientRect().width;
    }
  });

  onDestroy(() => {
    if (hoverHideTimer) clearTimeout(hoverHideTimer);
    if (easingResetTimer) clearTimeout(easingResetTimer);
    if (resizeObserver) resizeObserver.disconnect();
  });

  $effect(() => {
    if (popupElement && resizeObserver) {
      resizeObserver.observe(popupElement);
      const w = popupElement.getBoundingClientRect().width;
      if (w > 0) popupWidth = w;
    }
  });
</script>

<div
  class="amp-seekbar {className}"
  class:is-disabled={disabled}
  class:is-hovered={isHovered}
  class:is-pointer-down={isPointerDown}
  class:is-scrubbing={isScrubbing}
  style="--seek-duration: {isEasingToClick && !isScrubbing ? activeAnimDurationMs : 0}ms; --anim-fast-ms: {animationStore.isOff ? 0 : Math.round(activeAnimDurationMs * 0.75)}ms;"
  bind:this={containerElement}
  onpointerenter={handlePointerEnter}
  onpointerleave={handlePointerLeave}
  onpointermove={handlePointerMove}
  onpointerdown={handlePointerDown}
  onpointerup={handlePointerUp}
  onlostpointercapture={handleLostPointerCapture}
  role="slider"
  aria-valuemin={effectiveMin}
  aria-valuemax={effectiveMax}
  aria-valuenow={displayValue}
  aria-disabled={disabled}
  tabindex={disabled ? -1 : 0}
  onkeydown={handleKeyDown}
>
  <!-- Invisible 20px hit area to match WPF SliderContainer Border Height="20" -->
  <div class="hit-area"></div>

  <!-- Track Container -->
  <div class="seekbar-track" bind:this={trackElement}>
    <!-- Optional Selection Range (WPF IsSelectionRangeEnabled) -->
    {#if isSelectionRangeEnabled && selectionRangeWidth > 0}
      <div
        class="selection-range"
        style="left: {selectionRangeLeft}%; width: {selectionRangeWidth}%;"
      ></div>
    {/if}

    <!-- Hover Fill Overlay (WPF HoverFillOverlay: semi-transparent ghost seekbar) -->
    <div
      class="hover-fill-overlay"
      class:visible={showHoverFill && !isScrubbing}
      style="width: {hoverPercent}%;"
    ></div>

    <!-- Filled Track (WPF SliderRepeatButtonFilled: White when idle, Accent on hover/drag, smooth easing) -->
    <div
      class="filled-track"
      style="width: {progressPercent}%;"
    ></div>

    <!-- Thumb (WPF SliderThumb: 12x12 white circular handle, smoothly eases to clicked area) -->
    <div
      class="seekbar-thumb"
      style="left: {progressPercent}%;"
    ></div>
  </div>

  <!-- Tooltip Popup (Portaled to document.body with max z-index so it is NEVER masked by containers) -->
  {#if isTooltipVisible && !disabled}
    <div
      use:portal
      class="seekbar-popup visible"
      style="top: {fixedPopupTop}px; left: {fixedPopupLeft}px;"
      bind:this={popupElement}
      bind:clientWidth={popupWidth}
    >
      <span class="popup-text">{tooltipText}</span>
    </div>
  {/if}
</div>

<style>
  .amp-seekbar {
    position: relative;
    width: 100%;
    height: 20px;
    min-height: 20px;
    display: flex;
    align-items: center;
    cursor: pointer;
    outline: none;
    user-select: none;
    touch-action: none;
    box-sizing: border-box;
  }

  .amp-seekbar.is-disabled {
    opacity: 0.35;
    cursor: not-allowed;
    pointer-events: none;
  }

  /* All internal elements must have pointer-events: none so that the container is the single,
     uninterrupted pointer event target — completely preventing any flicker or child-crossing leave events */
  .hit-area,
  .seekbar-track,
  .selection-range,
  .hover-fill-overlay,
  .filled-track,
  .seekbar-thumb,
  .seekbar-popup {
    pointer-events: none !important;
  }

  /* Invisible hit area to match WPF's 20px hit area */
  .hit-area {
    position: absolute;
    top: 0;
    bottom: 0;
    left: 0;
    right: 0;
    height: 20px;
    background: transparent;
  }

  /* 4px Main Track Groove matching WPF BaseStyles.xaml (PART_Track) */
  .seekbar-track {
    position: relative;
    width: 100%;
    height: 4px;
    border-radius: 2px;
    background: var(--track-groove-bg, rgba(255, 255, 255, 0.12));
    overflow: visible;
  }

  :global(:root.light) .seekbar-track,
  :global(:root[data-theme="light"]) .seekbar-track {
    background: rgba(0, 0, 0, 0.12);
  }

  /* Selection Range Highlight */
  .selection-range {
    position: absolute;
    top: 0;
    bottom: 0;
    height: 4px;
    border-radius: 2px;
    background: var(--accent-subtle, rgba(29, 185, 84, 0.25));
  }

  /* Ghost Seekbar (WPF HoverFillOverlay) */
  .hover-fill-overlay {
    position: absolute;
    left: 0;
    top: 0;
    bottom: 0;
    height: 4px;
    border-radius: 2px;
    background: rgba(255, 255, 255, 0.5);
    opacity: 0;
    transition: opacity 120ms cubic-bezier(0, 0, 0.2, 1);
  }

  :global(:root.light) .hover-fill-overlay,
  :global(:root[data-theme="light"]) .hover-fill-overlay {
    background: rgba(0, 0, 0, 0.35);
  }

  .hover-fill-overlay.visible {
    opacity: 0.55;
  }

  /* Filled Progress Track matching WPF SliderRepeatButtonFilled.
     When clicked or during playback, it smoothly glides with cubic-bezier easing linked to settings. */
  .filled-track {
    position: absolute;
    left: 0;
    top: 0;
    bottom: 0;
    height: 4px;
    border-radius: 2px;
    background-color: var(--text-primary, #FFFFFF);
    transition:
      background-color var(--transition-fast, 0.16s ease),
      width var(--seek-duration, 0s) cubic-bezier(0.4, 0, 0.2, 1);
  }

  /* WPF BaseStyles Trigger: On Hover / Pointer Down / Drag, Filled Track turns to SystemAccentColor */
  .amp-seekbar.is-hovered .filled-track,
  .amp-seekbar.is-pointer-down .filled-track,
  .amp-seekbar.is-scrubbing .filled-track {
    background-color: var(--accent-color, var(--accent, #1DB954));
  }

  /* Immediate zero-lag width tracking only when actively scrubbing (dragging) */
  .amp-seekbar.is-scrubbing .filled-track {
    transition: background-color var(--transition-fast, 0.16s ease), width 0s linear !important;
  }

  /* Thumb Handle matching WPF SliderThumb.
     Smoothly glides with cubic-bezier easing to clicked position. */
  .seekbar-thumb {
    position: absolute;
    top: 50%;
    width: 12px;
    height: 12px;
    border-radius: 50%;
    background: var(--text-primary, #FFFFFF);
    box-shadow: 0 1px 4px rgba(0, 0, 0, 0.4);
    transform: translate(-50%, -50%) scale(0);
    opacity: 0;
    transition:
      transform var(--transition-fast, 0.15s cubic-bezier(0, 0, 0.2, 1)),
      opacity var(--transition-fast, 0.15s cubic-bezier(0, 0, 0.2, 1)),
      left var(--seek-duration, 0s) cubic-bezier(0.4, 0, 0.2, 1);
  }

  /* WPF BaseStyles Trigger: On Hover / Pointer Down / Drag, Thumb scales and fades in */
  .amp-seekbar.is-hovered .seekbar-thumb,
  .amp-seekbar.is-pointer-down .seekbar-thumb,
  .amp-seekbar.is-scrubbing .seekbar-thumb {
    transform: translate(-50%, -50%) scale(1);
    opacity: 1;
  }

  /* Immediate zero-lag thumb tracking only when actively scrubbing (dragging) */
  .amp-seekbar.is-scrubbing .seekbar-thumb {
    transition:
      transform var(--transition-fast, 0.15s cubic-bezier(0, 0, 0.2, 1)),
      opacity var(--transition-fast, 0.15s cubic-bezier(0, 0, 0.2, 1)),
      left 0s linear !important;
  }

  /* Floating Popup Tooltip matching WPF _hoverPopup & _dragPopup.
     Portaled directly to document.body to avoid container clipping. */
  .seekbar-popup {
    position: fixed;
    z-index: 2147483647;
    margin: 0;
    padding: 4px 8px;
    border-radius: 6px;
    font-size: 11px;
    font-variant-numeric: tabular-nums;
    font-weight: 500;
    white-space: nowrap;
    background: rgba(30, 30, 30, 0.95);
    color: #f3f4f6;
    border: none;
    box-shadow: 0 10px 25px -5px rgba(0, 0, 0, 0.5), 0 8px 10px -6px rgba(0, 0, 0, 0.4);
    backdrop-filter: blur(12px);
    -webkit-backdrop-filter: blur(12px);
    pointer-events: none;
    transform: translate(-50%, -100%) scale(0.95);
    transform-origin: bottom center;
    opacity: 0;
    visibility: hidden;
    transition:
      opacity var(--anim-fast-ms, 0.14s) cubic-bezier(0, 0, 0.2, 1),
      transform var(--anim-fast-ms, 0.14s) cubic-bezier(0, 0, 0.2, 1),
      visibility var(--anim-fast-ms, 0.14s);
  }

  :global(:root.light) .seekbar-popup,
  :global(:root[data-theme="light"]) .seekbar-popup {
    background: rgba(255, 255, 255, 0.96);
    color: #18181b;
    border: none;
    box-shadow: 0 10px 25px -5px rgba(0, 0, 0, 0.15), 0 8px 10px -6px rgba(0, 0, 0, 0.08);
  }

  .seekbar-popup.visible {
    opacity: 1;
    visibility: visible;
    transform: translate(-50%, -100%) scale(1);
  }

  .popup-text {
    display: block;
    line-height: 1;
  }
</style>
