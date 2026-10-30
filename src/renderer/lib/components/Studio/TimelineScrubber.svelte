<script lang="ts">
  import { DEFAULT_UI_PREFERENCES } from '../../../../shared/defaults/settings';
  import { onMount, onDestroy } from 'svelte';
  import { uiStore } from '../../state/uiStore.svelte';
  import { playbackStore } from '../../state/playbackStore.svelte';
  import { themeStore } from '../../state/themeStore.svelte';
  import FluentIcon from '../Common/FluentIcon.svelte';
  import { t } from '../../i18n';
  import type { StudioRenderTrack } from './studioNotes';
  import { noteStart, noteDuration } from '../../utils/noteIndex';
  import type { KeyChangeMarker } from '$shared/types/midi';

  interface Props {
    renderTracks: StudioRenderTrack[];
    scrollX: number;
    zoomX: number;
    zoomY?: number;
    viewportWidth: number;
    onScroll: (x: number) => void;
    onZoomChange: (z: number) => void;
    onFitNotes: () => void;
    onZoomYChange?: (y: number) => void;
    hoverTime?: number | null;
    isLockedToPlayhead?: boolean;
  }

  let {
    renderTracks,
    scrollX = 0,
    zoomX = DEFAULT_UI_PREFERENCES.studio.zoomX,
    zoomY = $bindable(DEFAULT_UI_PREFERENCES.studio.zoomY),
    viewportWidth = 800,
    onScroll,
    onZoomChange,
    onFitNotes,
    onZoomYChange,
    hoverTime = $bindable(null),
    isLockedToPlayhead = $bindable(DEFAULT_UI_PREFERENCES.studio.isLockedToPlayhead)
  }: Props = $props();

  let scrubberCanvas: HTMLCanvasElement;
  let scrubberContainer: HTMLDivElement;
  let canvasWrap: HTMLDivElement;
  let ctx: CanvasRenderingContext2D | null = null;
  let animId: number;

  const SCRUBBER_HEIGHT = 44;

  let isDraggingScrubber = $state(false);
  let isPointerDownScrubber = false;
  let isPointerDownViewport = false;
  let startMouseX = 0;
  let startMouseY = 0;
  let downCanvasX = 0;
  let isDraggingViewport = $state(false);
  let viewportDragOffset = $state(0);

  let dirty = true;
  let notesDirty = true;
  let noteLayer: HTMLCanvasElement;
  let cachedTracks: StudioRenderTrack[] | undefined;
  let cachedDuration = 0;
  let width = 0;
  let height = 0;
  let pixelRatio = 1;
  let sizeDirty = false;
  $effect(() => {
    renderTracks; playbackStore.durationSeconds;
    notesDirty = true;
    dirty = true;
  });
  $effect(() => {
    uiStore.activeTab;
    scrollX; zoomX; viewportWidth; hoverTime;
    themeStore.themeProgress; themeStore.accentColor;
    playbackStore.currentTimeSeconds; playbackStore.selectedMarkerId;
    JSON.stringify(playbackStore.activeSong?.keyMarkers);
    dirty = true;
  });

  function getTotalDuration(): number {
    return Math.max(1, playbackStore.durationSeconds);
  }

  function renderMiniMap() {
    if (!ctx || !scrubberCanvas || !canvasWrap) return;

    animId = requestAnimationFrame(renderMiniMap);
    // Timing projections replace the index even when the source MIDI is unchanged.
    // Check the cache identity before the dirty guard so threshold edits repaint it.
    if (cachedTracks !== renderTracks || cachedDuration !== getTotalDuration()) {
      notesDirty = true;
      dirty = true;
    }
    if (uiStore.activeTab !== 'studio' || !dirty || width <= 0 || height <= 0 || document.hidden) return;
    if (sizeDirty) applyCanvasSize();
    dirty = false;
    const w = width;
    const h = height;

    const song = playbackStore.activeSong;
    const tp = themeStore.themeProgress; // 0 = dark, 1 = light
    const isDark = tp < 0.5;

    // Background
    const bgVal = Math.round(26 + (244 - 26) * tp);
    ctx.fillStyle = `rgb(${bgVal}, ${bgVal}, ${bgVal})`;
    ctx.fillRect(0, 0, w, h);

    const totalDur = getTotalDuration();

    if (song) {
      if (notesDirty) {
        const layer = noteLayer.getContext('2d')!;
        layer.clearRect(0, 0, w, h);
        for (const track of renderTracks) {
          if (track.muted) continue;
          layer.fillStyle = track.color;
          for (const note of track.notes) {
            const nx = noteStart(note) / totalDur * w;
            const nw = Math.max(1, noteDuration(note) / totalDur * w);
            const pitch = note.pitch ?? note.noteNumber;
            const ny = Math.max(2, Math.min(h - 16, h - ((pitch - 21) / 88) * (h - 18) - 12));
            layer.fillRect(nx, ny, nw, 2);
          }
        }
        notesDirty = false;
        cachedTracks = renderTracks;
        cachedDuration = totalDur;
      }
      ctx.drawImage(noteLayer, 0, 0, w, h);

      // 2. Ruler ticks and timestamps (every 10s, 30s, or 60s)
      const stepSec = totalDur <= 60 ? 10 : totalDur <= 180 ? 30 : totalDur <= 600 ? 60 : 120;
      ctx.fillStyle = isDark ? 'rgba(255, 255, 255, 0.45)' : 'rgba(0, 0, 0, 0.5)';
      ctx.font = '9px Inter, system-ui, sans-serif';
      ctx.textAlign = 'center';

      for (let sec = 0; sec <= totalDur; sec += stepSec) {
        const rx = (sec / totalDur) * w;
        ctx.strokeStyle = isDark ? 'rgba(255, 255, 255, 0.18)' : 'rgba(0, 0, 0, 0.18)';
        ctx.lineWidth = 1;
        ctx.beginPath();
        ctx.moveTo(rx, h - 8);
        ctx.lineTo(rx, h - 3);
        ctx.stroke();

        const m = Math.floor(sec / 60);
        const s = Math.floor(sec % 60);
        const text = `${m}:${s < 10 ? '0' : ''}${s}`;
        const textX = Math.max(12, Math.min(w - 12, rx));
        ctx.fillText(text, textX, h - 1);
      }

      // 3. Key Change Markers (amber vertical lines and badge tags at top)
      const markers = song.keyMarkers || [];
      const selectedId = playbackStore.selectedMarkerId;
      const accentColor = themeStore.accentColor || '#1db954';

      for (const marker of markers) {
        const mTime = marker.time ?? (marker.timeMs / 1000);
        const mx = (mTime / totalDur) * w;
        const isSelected = marker.id === selectedId;

        // Vertical marker line
        ctx.strokeStyle = isSelected ? accentColor : '#f59e0b';
        ctx.lineWidth = isSelected ? 2 : 1.5;
        ctx.beginPath();
        ctx.moveTo(mx, 0);
        ctx.lineTo(mx, h - 10);
        ctx.stroke();

        // Badge at top
        const badgeText = marker.noteDisplay || marker.key || 'C';
        ctx.font = 'bold 9px Inter, system-ui, sans-serif';
        const tm = ctx.measureText(badgeText);
        const bw = tm.width + 6;
        const bh = 11;
        const bx = Math.max(1, Math.min(w - bw - 1, mx - bw / 2));

        ctx.fillStyle = isSelected ? accentColor : '#f59e0b';
        ctx.fillRect(bx, 1, bw, bh);
        ctx.fillStyle = '#000000';
        ctx.fillText(badgeText, bx + bw / 2, 10);
      }

      // Theme-dependent colors for viewport and playhead matching WPF TimelineCanvas
      const playheadR = Math.round(255 - 231 * tp);
      const playheadG = Math.round(255 - 231 * tp);
      const playheadB = Math.round(255 - 227 * tp);
      const playheadColor = `rgb(${playheadR}, ${playheadG}, ${playheadB})`;

      const vpVal = Math.round(255 * (1 - tp));
      const vpFill = `rgba(${vpVal}, ${vpVal}, ${vpVal}, 0.125)`;
      const vpBorder = `rgba(${vpVal}, ${vpVal}, ${vpVal}, ${(0.78 - 0.08 * tp).toFixed(2)})`;
      const vpGrip = `rgba(${vpVal}, ${vpVal}, ${vpVal}, ${(0.94 - 0.08 * tp).toFixed(2)})`;

      // 4. Viewport highlight box (showing currently visible piano roll horizon ahead)
      const totalRollW = totalDur * zoomX;
      if (totalRollW > 0) {
        const viewLeftRatio = scrollX / totalRollW;
        const viewWidthRatio = viewportWidth / totalRollW;

        const vx = Math.max(0, viewLeftRatio * w);
        const vw = Math.min(w - vx, Math.max(24, viewWidthRatio * w));

        ctx.fillStyle = vpFill;
        ctx.fillRect(vx, 1, vw, h - 2);
        ctx.strokeStyle = vpBorder;
        ctx.lineWidth = 1.5;
        ctx.strokeRect(vx, 1, vw, h - 2);

        // Tactile grip lines on the right edge of viewport
        if (vw > 16) {
          ctx.fillStyle = vpGrip;
          ctx.fillRect(vx + vw - 3, 4, 2, h - 8);
        }
      }

      // 5. Hover guide line (linked with PianoRollCanvas)
      if (hoverTime !== null && hoverTime >= 0 && hoverTime <= totalDur && !isDraggingScrubber && !isDraggingViewport) {
        const hx = (hoverTime / totalDur) * w;
        ctx.strokeStyle = isDark ? 'rgba(255, 255, 255, 0.45)' : 'rgba(0, 0, 0, 0.45)';
        ctx.lineWidth = 1;
        ctx.setLineDash([3, 3]);
        ctx.beginPath();
        ctx.moveTo(hx, 0);
        ctx.lineTo(hx, h);
        ctx.stroke();
        ctx.setLineDash([]);
      }

      // 6. Playhead vertical line & head
      const renderTime = playbackStore.getAnimatedTime(isDraggingScrubber);
      const px = (renderTime / totalDur) * w;
      ctx.strokeStyle = playheadColor;
      ctx.lineWidth = 2;
      ctx.beginPath();
      ctx.moveTo(px, 0);
      ctx.lineTo(px, h);
      ctx.stroke();

      // Top handle matching WPF PlayheadHandleGeometry
      ctx.fillStyle = playheadColor;
      ctx.beginPath();
      ctx.moveTo(px - 5, 0);
      ctx.lineTo(px + 5, 0);
      ctx.lineTo(px + 5, 6);
      ctx.lineTo(px, 11);
      ctx.lineTo(px - 5, 6);
      ctx.closePath();
      ctx.fill();
    }

  }

  function hitTestMarker(canvasX: number): KeyChangeMarker | null {
    const song = playbackStore.activeSong;
    if (!song?.keyMarkers || !canvasWrap) return null;
    const w = canvasWrap.clientWidth;
    const totalDur = getTotalDuration();
    if (w <= 0 || totalDur <= 0) return null;

    for (const marker of song.keyMarkers) {
      const mTime = marker.time ?? (marker.timeMs / 1000);
      const mx = (mTime / totalDur) * w;
      if (Math.abs(canvasX - mx) <= 10) {
        return marker;
      }
    }
    return null;
  }

  function handleCanvasMouseDown(e: MouseEvent) {
    if (!canvasWrap || e.button !== 0) return;
    const rect = canvasWrap.getBoundingClientRect();
    const x = e.clientX - rect.left;
    const w = rect.width;
    const totalDur = getTotalDuration();
    if (w <= 0 || totalDur <= 0) return;

    // 1. Check if clicked on a marker
    const hit = hitTestMarker(x);
    if (hit) {
      playbackStore.selectMarker(hit);
      const mTime = hit.time ?? (hit.timeMs / 1000);
      playbackStore.seek(mTime);
      return;
    }

    // 2. Check if clicked directly on or near the playhead needle
    const renderTime = playbackStore.getAnimatedTime(isDraggingScrubber);
    const px = (renderTime / totalDur) * w;
    if (Math.abs(x - px) <= 6) {
      playbackStore.beginScrub();
      isPointerDownScrubber = true;
      isDraggingScrubber = true;
      startMouseX = e.clientX;
      startMouseY = e.clientY;
      updateScrubberPosition(x, true);
      return;
    }

    // 3. Check if clicked inside viewport box (visible area indicator)
    const totalRollW = totalDur * zoomX;
    const vx = (scrollX / totalRollW) * w;
    const vw = Math.min(w - vx, Math.max(24, (viewportWidth / totalRollW) * w));

    if (x >= vx && x <= vx + vw) {
      isPointerDownViewport = true;
      isDraggingViewport = false;
      viewportDragOffset = x - vx;
      downCanvasX = x;
      startMouseX = e.clientX;
      startMouseY = e.clientY;
      return;
    }

    // 4. Playhead click / scrub outside viewport
    playbackStore.beginScrub();
    isPointerDownScrubber = true;
    isDraggingScrubber = false;
    startMouseX = e.clientX;
    startMouseY = e.clientY;
    updateScrubberPosition(x, false);
  }

  function handleCanvasMouseMove(e: MouseEvent) {
    if (isDraggingViewport || isDraggingScrubber || isPointerDownViewport || isPointerDownScrubber) {
      hoverTime = null;
      return;
    }
    if (!canvasWrap) return;
    const rect = canvasWrap.getBoundingClientRect();
    const x = e.clientX - rect.left;
    const y = e.clientY - rect.top;
    const w = rect.width;
    const totalDur = getTotalDuration();

    if (x >= 0 && x <= w && y >= 0 && y <= rect.height && totalDur > 0) {
      hoverTime = Math.max(0, Math.min(totalDur, (x / w) * totalDur));
    } else {
      hoverTime = null;
    }
  }

  function handleCanvasMouseLeave() {
    if (!isDraggingViewport && !isDraggingScrubber && !isPointerDownViewport && !isPointerDownScrubber) {
      hoverTime = null;
    }
  }

  function handleWindowMouseMove(e: MouseEvent) {
    if (!canvasWrap) return;

    // Scrubber drag threshold check
    if (isPointerDownScrubber && !isDraggingScrubber) {
      const dist = Math.hypot(e.clientX - startMouseX, e.clientY - startMouseY);
      if (dist >= 4) {
        isDraggingScrubber = true;
        hoverTime = null;
      }
    }

    // Viewport drag threshold check (hold & drag to pan visible area)
    if (isPointerDownViewport && !isDraggingViewport) {
      const dist = Math.hypot(e.clientX - startMouseX, e.clientY - startMouseY);
      if (dist >= 4) {
        isDraggingViewport = true;
        isLockedToPlayhead = false;
        hoverTime = null;
      }
    }

    if (isDraggingViewport) {
      hoverTime = null;
      const rect = canvasWrap.getBoundingClientRect();
      const x = e.clientX - rect.left;
      const w = rect.width;
      const totalDur = getTotalDuration();
      const totalRollW = totalDur * zoomX;
      const targetVx = x - viewportDragOffset;
      const newScrollX = Math.max(0, Math.min(Math.max(0, totalRollW - viewportWidth), (targetVx / w) * totalRollW));
      onScroll(newScrollX);
    } else if (isDraggingScrubber) {
      hoverTime = null;
      const rect = canvasWrap.getBoundingClientRect();
      const x = e.clientX - rect.left;
      updateScrubberPosition(x, true);
    }
  }

  function handleWindowMouseUp() {
    if (isPointerDownViewport) {
      if (!isDraggingViewport) {
        // User clicked inside visible area indicator without dragging -> move playhead there!
        updateScrubberPosition(downCanvasX, false);
      }
      isPointerDownViewport = false;
      isDraggingViewport = false;
    }

    if (isPointerDownScrubber) playbackStore.endScrub();
    isPointerDownScrubber = false;
    isDraggingScrubber = false;
    dirty = true;
  }

  function updateScrubberPosition(canvasX: number, immediate = false) {
    if (!canvasWrap) return;
    const w = canvasWrap.clientWidth;
    const totalDur = getTotalDuration();
    if (w <= 0 || totalDur <= 0) return;

    const ratio = Math.max(0, Math.min(1, canvasX / w));
    const seekSec = ratio * totalDur;
    playbackStore.seek(seekSec, { immediate });
  }

  function zoomInX() {
    onZoomChange(Math.min(400, Math.round(zoomX * 1.25)));
  }

  function zoomOutX() {
    onZoomChange(Math.max(20, Math.round(zoomX * 0.8)));
  }

  function resetZoomX() {
    onZoomChange(DEFAULT_UI_PREFERENCES.studio.zoomX);
  }

  function zoomInY() {
    const nextY = Math.min(40, zoomY + 2);
    zoomY = nextY;
    onZoomYChange?.(nextY);
  }

  function zoomOutY() {
    const nextY = Math.max(2, zoomY - 2);
    zoomY = nextY;
    onZoomYChange?.(nextY);
  }

  function resetZoomY() {
    onFitNotes();
  }

  function resize() {
    if (!scrubberCanvas || !canvasWrap) return;
    const dpr = window.devicePixelRatio || 1;
    const nextWidth = canvasWrap.clientWidth;
    const nextHeight = canvasWrap.clientHeight;
    if (nextWidth <= 0 || nextHeight <= 0) return;
    if (width === nextWidth && height === nextHeight && pixelRatio === dpr) return;
    width = nextWidth;
    height = nextHeight;
    pixelRatio = dpr;
    dirty = true;
    notesDirty = true;
    sizeDirty = true;
  }

  function applyCanvasSize() {
    // Resize and repaint together so layout changes cannot expose a blank bitmap.
    const targetW = Math.round(width * pixelRatio);
    const targetH = Math.round(height * pixelRatio);
    if (scrubberCanvas.width !== targetW) scrubberCanvas.width = targetW;
    if (scrubberCanvas.height !== targetH) scrubberCanvas.height = targetH;
    if (noteLayer.width !== targetW) noteLayer.width = targetW;
    if (noteLayer.height !== targetH) noteLayer.height = targetH;
    noteLayer.getContext('2d')!.setTransform(pixelRatio, 0, 0, pixelRatio, 0, 0);
    ctx!.setTransform(pixelRatio, 0, 0, pixelRatio, 0, 0);
    sizeDirty = false;
  }

  let resizeObserver: ResizeObserver | null = null;

  onMount(() => {
    noteLayer = document.createElement('canvas');
    ctx = scrubberCanvas.getContext('2d');
    resize();

    if (typeof ResizeObserver !== 'undefined' && canvasWrap) {
      resizeObserver = new ResizeObserver(() => {
        resize();
      });
      resizeObserver.observe(canvasWrap);
    }

    window.addEventListener('resize', resize);
    window.addEventListener('mousemove', handleWindowMouseMove);
    window.addEventListener('mouseup', handleWindowMouseUp);
    window.addEventListener('blur', handleWindowMouseUp);
    animId = requestAnimationFrame(renderMiniMap);
  });

  onDestroy(() => {
    if (animId) cancelAnimationFrame(animId);
    if (resizeObserver) {
      resizeObserver.disconnect();
      resizeObserver = null;
    }
    window.removeEventListener('resize', resize);
    window.removeEventListener('mousemove', handleWindowMouseMove);
    handleWindowMouseUp();
    window.removeEventListener('mouseup', handleWindowMouseUp);
    window.removeEventListener('blur', handleWindowMouseUp);
  });
</script>

<div class="scrubber-bar" bind:this={scrubberContainer}>
  <div
    class="canvas-wrap"
    class:dragging={isDraggingViewport}
    bind:this={canvasWrap}
    onmousedown={handleCanvasMouseDown}
    onmousemove={handleCanvasMouseMove}
    onmouseleave={handleCanvasMouseLeave}
    role="presentation"
  >
    <canvas bind:this={scrubberCanvas} class="mini-canvas"></canvas>
  </div>

  <div class="zoom-controls-wrapper">
    <!-- Lock to Playhead Button -->
    <button
      class="tool-btn lock-btn"
      class:is-locked={isLockedToPlayhead}
      aria-pressed={isLockedToPlayhead}
      onclick={() => { isLockedToPlayhead = !isLockedToPlayhead; }}
      title={isLockedToPlayhead
        ? (t('studio.lockToPlayheadLocked') || 'Follow Playhead: Locked (Click to unlock view)')
        : (t('studio.lockToPlayheadUnlocked') || 'Follow Playhead: Unlocked (Click to lock to playhead)')}
    >
      <FluentIcon name={isLockedToPlayhead ? "LockClosed" : "LockOpen"} size={13} />
    </button>

    <span class="tool-divider" aria-hidden="true"></span>

    <!-- Horizontal Zoom Controls -->
    <div class="zoom-group">
      <button type="button" class="tool-btn" onclick={resetZoomX} title={t('studio.resetZoom')}>
        <span class="zoom-axis-icon">
          <FluentIcon name="ArrowBidirectionalLeftRight" size={13} />
        </span>
      </button>
      <button class="tool-btn" onclick={zoomInX} title={t('studio.zoomIn') || 'Horizontal Zoom In (Ctrl+Wheel)'}>
        <FluentIcon name="Add" size={13} />
      </button>
      <button class="tool-btn" onclick={zoomOutX} title={t('studio.zoomOut') || 'Horizontal Zoom Out (Ctrl+Wheel)'}>
        <FluentIcon name="Subtract" size={13} />
      </button>
    </div>

    <span class="tool-divider" aria-hidden="true"></span>

    <!-- Vertical Zoom Controls (Key Height) -->
    <div class="zoom-group">
      <button type="button" class="tool-btn" onclick={resetZoomY} title={t('studio.resetZoomY')}>
        <span class="zoom-axis-icon is-vertical">
          <FluentIcon name="ArrowBidirectionalLeftRight" size={13} />
        </span>
      </button>
      <button class="tool-btn" onclick={zoomInY} title={t('studio.zoomInY') || 'Vertical Zoom In (Alt+Wheel)'}>
        <FluentIcon name="Add" size={13} />
      </button>
      <button class="tool-btn" onclick={zoomOutY} title={t('studio.zoomOutY') || 'Vertical Zoom Out (Alt+Wheel)'}>
        <FluentIcon name="Subtract" size={13} />
      </button>
    </div>
  </div>
</div>

<style>
  .scrubber-bar {
    display: flex;
    align-items: center;
    background: var(--bg-secondary);
    border-top: none;
    height: 44px;
    padding: 0 10px;
    gap: 8px;
    user-select: none;
    width: 100%;
    box-sizing: border-box;
    flex-shrink: 0;
  }

  .canvas-wrap {
    flex: 1;
    height: 34px;
    position: relative;
    border-radius: var(--radius-sm);
    overflow: hidden;
    cursor: pointer;
    border: none;
    background: rgba(0, 0, 0, 0.25);
  }

  .canvas-wrap.dragging {
    cursor: grabbing;
  }

  .mini-canvas {
    display: block;
    width: 100%;
    height: 100%;
  }

  .zoom-controls-wrapper {
    display: flex;
    align-items: center;
    gap: 6px;
    flex-shrink: 0;
  }

  .lock-btn {
    width: 28px;
    height: 28px;
    border-radius: var(--radius-sm);
    background: transparent;
    border: none;
    color: var(--text-secondary);
    opacity: 0.65;
    display: flex;
    align-items: center;
    justify-content: center;
    cursor: pointer;
    transition: all var(--transition-fast, 0.15s ease);
    flex-shrink: 0;
  }

  .lock-btn:hover {
    opacity: 1;
    color: var(--text-primary);
  }

  .lock-btn.is-locked {
    background: transparent;
    color: var(--accent);
    opacity: 1;
  }

  .lock-btn.is-locked:hover {
    filter: brightness(1.1);
  }

  .zoom-group {
    display: flex;
    align-items: center;
    gap: 2px;
    background: transparent;
    border-radius: var(--radius-sm);
    padding: 2px 0;
    flex-shrink: 0;
  }

  .tool-divider {
    width: 1px;
    height: 16px;
    background: var(--border-subtle);
    flex-shrink: 0;
  }

  .zoom-axis-icon {
    display: flex;
    align-items: center;
    justify-content: center;
    width: 16px;
    height: 16px;
    user-select: none;
    pointer-events: none;
  }

  .zoom-axis-icon.is-vertical {
    transform: rotate(90deg);
  }

  .tool-btn {
    width: 24px;
    height: 24px;
    border-radius: var(--radius-sm);
    background: transparent;
    border: none;
    color: var(--text-primary);
    opacity: 0.5;
    display: flex;
    align-items: center;
    justify-content: center;
    cursor: pointer;
    transition: opacity var(--transition-normal, 0.22s cubic-bezier(0, 0, 0.2, 1));
  }

  .tool-btn:hover {
    background: transparent;
    opacity: 1;
  }

  .tool-btn:active {
    opacity: 0.75;
  }
  .tool-btn:focus-visible {
    outline: 2px solid var(--accent);
    outline-offset: 2px;
  }
</style>
