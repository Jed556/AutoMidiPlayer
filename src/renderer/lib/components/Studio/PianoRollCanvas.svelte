<script lang="ts">
  import { DEFAULT_UI_PREFERENCES } from '../../../../shared/defaults/settings';
  import { onMount, onDestroy } from 'svelte';
  import { uiStore } from '../../state/uiStore.svelte';
  import { playbackStore } from '../../state/playbackStore.svelte';
  import { themeStore } from '../../state/themeStore.svelte';
  import { animationStore } from '../../state/animationStore.svelte';
  import { api } from '../../backend/index';
  import type { StudioRenderTrack } from './studioNotes';
  import { noteStart, noteDuration } from '../../utils/noteIndex';
  import { getNoteTextColor } from '../../utils/noteAppearance';
  import { PianoStrikeEffects, type PianoStrike } from './pianoStrikeEffects';
  import type { RollNote, KeyChangeMarker } from '$shared/types/midi';
  import MarkerActionPopup from './MarkerActionPopup.svelte';
  import FluentIcon from '../Common/FluentIcon.svelte';

  interface Props {
    renderTracks: StudioRenderTrack[];
    zoomX?: number;
    zoomY?: number;
    scrollX?: number;
    scrollY?: number;
    onScrollChange?: (scrollX: number, scrollY: number) => void;
    hoverTime?: number | null;
    isLockedToPlayhead?: boolean;
    highlightedTrackIndex?: number | null;
  }

  let {
    renderTracks,
    zoomX = $bindable(DEFAULT_UI_PREFERENCES.studio.zoomX), // pixels per second
    zoomY = $bindable(DEFAULT_UI_PREFERENCES.studio.zoomY), // pixels per semitone
    scrollX = $bindable(0),
    scrollY = $bindable(0),
    onScrollChange,
    hoverTime = $bindable(null),
    isLockedToPlayhead = $bindable(DEFAULT_UI_PREFERENCES.studio.isLockedToPlayhead),
    highlightedTrackIndex = null,
  }: Props = $props();

  function formatHoverTime(seconds: number): string {
    const m = Math.floor(seconds / 60);
    const s = Math.floor(seconds % 60);
    const ms = Math.floor((seconds % 1) * 10);
    return `${m}:${s < 10 ? '0' : ''}${s}.${ms}`;
  }

  let canvasContainer: HTMLDivElement;
  let canvas: HTMLCanvasElement;
  let effectsCanvas: HTMLCanvasElement;
  let strikeEffects: PianoStrikeEffects | null = null;
  let reducedMotion = $state(false);
  let motionQuery: MediaQueryList | null = null;
  const handleMotionChange = () => { reducedMotion = motionQuery?.matches ?? false; };
  let ctx: CanvasRenderingContext2D | null = null;
  let animFrameId: number;

  const KEY_WIDTH = 58;
  const MIN_PITCH = 21; // A0
  const MAX_PITCH = 108; // C8
  const TOTAL_PITCHES = MAX_PITCH - MIN_PITCH + 1;

  const layeredTracks = $derived(highlightedTrackIndex === null ? renderTracks : [
    ...renderTracks.filter(track => track.index !== highlightedTrackIndex),
    ...renderTracks.filter(track => track.index === highlightedTrackIndex),
  ]);
  let dirty = true;
  let width = 0;
  let height = 0;
  let pixelRatio = 1;
  let sizeDirty = false;
  $effect(() => {
    uiStore.activeTab;
    zoomX; zoomY; scrollX; scrollY; hoverTime; isLockedToPlayhead;
    renderTracks; themeStore.themeProgress; themeStore.accentColor;
    highlightedTrackIndex; animationStore.durationMs; reducedMotion; hoveredTarget;
    playbackStore.currentTimeSeconds; playbackStore.isPlaying;
    playbackStore.isScrubbing; playbackStore.activeNotes;
    playbackStore.selectedMarkerId;
    JSON.stringify(playbackStore.activeSong?.keyMarkers);
    dirty = true;
  });

  // Hover state
  let hoveredTarget: { note: RollNote; track: StudioRenderTrack } | null = $state.raw(null);
  const hoveredNote = $derived(hoveredTarget?.note ?? null);
  let hoverStartedAt = 0;
  const HOVER_GAP = 4;
  let hoveredMarker: KeyChangeMarker | null = $state(null);
  let tooltipPos = $state({ x: 0, y: 0 });

  $effect(() => {
    if (hoveredTarget && (!renderTracks.includes(hoveredTarget.track) || hoveredTarget.track.muted)) {
      hoveredTarget = null;
    }
  });

  // Panning & dragging state
  let isMiddlePanning = $state(false);
  let isSpacePressed = $state(false);
  let panStartX = 0;
  let panStartY = 0;
  let initialScrollX = 0;
  let initialScrollY = 0;
  let isDraggingPlayhead = false;
  let isPointerDownPlayhead = false;
  let playheadScrubScrollX = 0;
  let startPlayheadMouseX = 0;
  let startPlayheadMouseY = 0;

  // Key marker dragging & action popup state
  let isDraggingMarker = $state(false);
  let draggedMarker: KeyChangeMarker | null = null;
  let dragStartMouseX = 0;
  let dragStartTimeMs = 0;
  let activePopupMarker: KeyChangeMarker | null = $state(null);
  let popupPos = $state({ x: 0, y: 0 });

  // Note scrub preview tracking
  let scrubAuditionedPitches = new Set<number>();

  const NOTE_NAMES = ['C', 'C#', 'D', 'D#', 'E', 'F', 'F#', 'G', 'G#', 'A', 'A#', 'B'];
  const IS_BLACK_KEY = [false, true, false, true, false, false, true, false, true, false, true, false];

  function getNoteName(pitch: number): string {
    const octave = Math.floor(pitch / 12) - 1;
    const note = NOTE_NAMES[pitch % 12];
    return `${note}${octave}`;
  }

  function resizeCanvas() {
    if (!canvas || !canvasContainer) return;
    const dpr = window.devicePixelRatio || 1;
    const nextWidth = canvasContainer.clientWidth;
    const nextHeight = canvasContainer.clientHeight;
    if (nextWidth <= 0 || nextHeight <= 0) return;
    if (width === nextWidth && height === nextHeight && pixelRatio === dpr) return;
    width = nextWidth;
    height = nextHeight;
    pixelRatio = dpr;
    dirty = true;
    sizeDirty = true;
  }

  function applyCanvasSize() {
    // A bitmap resize clears its pixels. Commit it immediately before drawing,
    // never in ResizeObserver, which can run between animation frames and paint.
    const targetW = Math.round(width * pixelRatio);
    const targetH = Math.round(height * pixelRatio);
    if (canvas.width !== targetW) canvas.width = targetW;
    if (canvas.height !== targetH) canvas.height = targetH;
    ctx!.setTransform(pixelRatio, 0, 0, pixelRatio, 0, 0);
    strikeEffects?.resize(width, height, pixelRatio);
    sizeDirty = false;
  }

  function noteBounds(note: RollNote) {
    const w = Math.max(4, noteDuration(note) * zoomX);
    const h = Math.max(3, zoomY - 2);
    return {
      x: KEY_WIDTH + noteStart(note) * zoomX - scrollX,
      y: (MAX_PITCH - (note.pitch ?? note.noteNumber)) * zoomY - scrollY + 1,
      w, h,
      radius: Math.min(3, w / 2, h / 2),
    };
  }

  function drawNote(context: CanvasRenderingContext2D, note: RollNote, track: StudioRenderTrack, renderTime: number) {
    const { x, y, w, h, radius } = noteBounds(note);
    if (y + h < 0 || y > height) return;
    context.fillStyle = track.color;
    context.beginPath();
    context.roundRect(x, y, w, h, radius);
    context.fill();
    const start = noteStart(note);
    if (renderTime >= start && renderTime < start + noteDuration(note)) {
      context.save();
      context.strokeStyle = track.color;
      context.shadowColor = track.color;
      context.shadowBlur = playbackStore.isPlaying && !animationStore.isOff && !reducedMotion ? 14 : 0;
      context.lineWidth = 1;
      context.stroke();
      context.restore();
    }
    if (w > 32 && h > 11) {
      context.fillStyle = track.labelColor;
      context.font = '600 10px system-ui, sans-serif';
      context.fillText(getNoteName(note.pitch ?? note.noteNumber), x + 4, y + h - 4);
    }
  }

  function render() {
    if (!ctx || !canvas || !canvasContainer) return;

    animFrameId = requestAnimationFrame(render);
    if (uiStore.activeTab !== 'studio' || width <= 0 || height <= 0 || document.hidden) {
      strikeEffects?.clear();
      return;
    }
    if (sizeDirty) applyCanvasSize();
    if (!dirty) {
      strikeEffects?.draw(performance.now());
      return;
    }
    dirty = false;
    const renderTime = playbackStore.getAnimatedTime();
    // Manual panning unlocks follow; locked scrubbing also uses the keyboard edge.
    if (isLockedToPlayhead && !isMiddlePanning && !isDraggingMarker) {
      // The keyboard edge is the strike line, including at the end of a song.
      scrollX = Math.max(0, renderTime * zoomX);
    }

    const song = playbackStore.activeSong;
    const tp = themeStore.themeProgress; // 0 = Dark, 1 = Light
    const accentColor = themeStore.accentColor || '#1db954';

    // Clear background
    const bgVal = Math.round(32 + (250 - 32) * tp);
    ctx.fillStyle = `rgb(${bgVal}, ${bgVal}, ${bgVal})`;
    ctx.fillRect(0, 0, width, height);

    const viewportW = width - KEY_WIDTH;

    // 1. Draw horizontal pitch rows
    const blackLaneAlpha = 0.22 - (0.22 - 0.05) * tp;
    const whiteLaneAlpha = 0.04 + (0.50 - 0.04) * tp;
    const dividerColor = tp > 0.5 ? 'rgba(0, 0, 0, 0.05)' : 'rgba(255, 255, 255, 0.05)';
    const octaveColor = tp > 0.5 ? 'rgba(0, 0, 0, 0.15)' : 'rgba(255, 255, 255, 0.15)';

    for (let p = MAX_PITCH; p >= MIN_PITCH; p--) {
      const y = (MAX_PITCH - p) * zoomY - scrollY;
      if (y + zoomY < 0 || y > height) continue;

      const isBlack = IS_BLACK_KEY[p % 12];
      ctx.fillStyle = isBlack
        ? `rgba(0, 0, 0, ${blackLaneAlpha.toFixed(3)})`
        : `rgba(255, 255, 255, ${whiteLaneAlpha.toFixed(3)})`;
      ctx.fillRect(KEY_WIDTH, y, viewportW, zoomY);

      // Pitch divider line
      ctx.strokeStyle = dividerColor;
      ctx.lineWidth = 1;
      ctx.beginPath();
      ctx.moveTo(KEY_WIDTH, y);
      ctx.lineTo(width, y);
      ctx.stroke();

      // Octave separator line (C notes)
      if (p % 12 === 0) {
        ctx.strokeStyle = octaveColor;
        ctx.lineWidth = 1.5;
        ctx.beginPath();
        ctx.moveTo(KEY_WIDTH, y);
        ctx.lineTo(width, y);
        ctx.stroke();
      }
    }

    // 2. Draw vertical time grid lines (measures and beats)
    const bpm = song?.tempo || 120;
    const beatDuration = 60 / bpm;
    const measureDuration = beatDuration * 4;

    const startSec = Math.max(0, scrollX / zoomX);
    const endSec = (scrollX + viewportW) / zoomX;

    const firstMeasure = Math.floor(startSec / measureDuration);
    const lastMeasure = Math.ceil(endSec / measureDuration);
    const gridLineColor = tp > 0.5 ? 'rgba(0, 0, 0, 0.12)' : 'rgba(255, 255, 255, 0.12)';
    const gridTextColor = tp > 0.5 ? 'rgba(0, 0, 0, 0.35)' : 'rgba(255, 255, 255, 0.35)';
    const beatLineColor = tp > 0.5 ? 'rgba(0, 0, 0, 0.04)' : 'rgba(255, 255, 255, 0.04)';

    for (let m = firstMeasure; m <= lastMeasure; m++) {
      const measureTime = m * measureDuration;
      const x = KEY_WIDTH + (measureTime * zoomX) - scrollX;

      if (x >= KEY_WIDTH && x <= width) {
        // Measure line
        ctx.strokeStyle = gridLineColor;
        ctx.lineWidth = 1.5;
        ctx.beginPath();
        ctx.moveTo(x, 0);
        ctx.lineTo(x, height);
        ctx.stroke();

        // Measure number
        ctx.fillStyle = gridTextColor;
        ctx.font = '10px Inter, system-ui, sans-serif';
        ctx.fillText(`${m + 1}`, x + 4, 14);
      }

      // Beat subdivisions
      for (let b = 1; b < 4; b++) {
        const beatTime = measureTime + b * beatDuration;
        const bx = KEY_WIDTH + (beatTime * zoomX) - scrollX;
        if (bx >= KEY_WIDTH && bx <= width) {
          ctx.strokeStyle = beatLineColor;
          ctx.lineWidth = 1;
          ctx.beginPath();
          ctx.moveTo(bx, 0);
          ctx.lineTo(bx, height);
          ctx.stroke();
        }
      }
    }

    // 3. Draw Key Change Markers (Matching WPF PianoRollCanvas with badges and lines)
    const keyMarkers = song?.keyMarkers || [];
    const selectedMarkerId = playbackStore.selectedMarkerId;

    for (const km of keyMarkers) {
      const mTime = km.time ?? (km.timeMs / 1000);
      const kx = KEY_WIDTH + (mTime * zoomX) - scrollX;

      if (kx >= KEY_WIDTH && kx <= width) {
        const isSelected = km.id === selectedMarkerId;
        const color = isSelected ? accentColor : '#f59e0b';

        // Dashed vertical marker line
        ctx.strokeStyle = color;
        ctx.lineWidth = isSelected ? 2.5 : 1.5;
        ctx.setLineDash([4, 4]);
        ctx.beginPath();
        ctx.moveTo(kx, 0);
        ctx.lineTo(kx, height);
        ctx.stroke();
        ctx.setLineDash([]);

        // Top marker badge
        const badgeText = `${km.noteDisplay || km.key} (${km.offsetDisplay})`;
        ctx.font = 'bold 9.5px Inter, system-ui, sans-serif';
        const tm = ctx.measureText(badgeText);
        const bw = tm.width + 10;
        const bh = 15;
        const bx = Math.max(KEY_WIDTH + 2, Math.min(width - bw - 2, kx - bw / 2));

        ctx.fillStyle = color;
        ctx.fillRect(bx, 1, bw, bh);
        ctx.fillStyle = '#000000';
        ctx.textAlign = 'center';
        ctx.fillText(badgeText, bx + bw / 2, 12);
        ctx.textAlign = 'start';
      }
    }

    // 4. Draw Notes from all enabled tracks & determine active key colors
    const activePitchColors = new Map<number, string>();
    const strikes = new Map<number, PianoStrike>();
    const strikeX = isLockedToPlayhead ? KEY_WIDTH : KEY_WIDTH + renderTime * zoomX - scrollX;

    ctx.save();
    ctx.beginPath();
    ctx.rect(KEY_WIDTH, 0, viewportW, height);
    ctx.clip();
    ctx.save();
    const hover = hoveredTarget;
    if (hover) {
      const progress = reducedMotion || animationStore.isOff ? 1
        : Math.min(1, (performance.now() - hoverStartedAt) / animationStore.durationMs);
      const gap = HOVER_GAP * (1 - Math.pow(1 - progress, 3));
      const { x, y, w, h, radius } = noteBounds(hover.note);
      // Cut only the other notes out of this space, preserving the lane/grid below.
      const mask = new Path2D();
      mask.rect(KEY_WIDTH, 0, viewportW, height);
      mask.roundRect(x - gap, y - gap, w + gap * 2, h + gap * 2, radius + gap);
      ctx.clip(mask, 'evenodd');
      if (progress < 1) dirty = true;
    }
    for (const track of layeredTracks) {
      if (track.muted) continue;
      for (const note of track.between(renderTime, renderTime)) {
        const pitch = note.pitch ?? note.noteNumber;
        if (renderTime >= noteStart(note) + noteDuration(note)) continue;
        activePitchColors.set(pitch, track.color);
        const y = (MAX_PITCH - pitch + 0.5) * zoomY - scrollY;
        if (y >= 0 && y <= height && strikeX >= KEY_WIDTH && strikeX <= width) {
          strikes.set(pitch, { id: `${track.index}:${note.id}`, x: strikeX, y, height: Math.max(2, zoomY - 2), color: track.color });
        }
      }
      for (const note of track.between(startSec - 4 / zoomX, endSec)) {
        if (hover?.track === track && hover.note === note) continue;
        drawNote(ctx, note, track, renderTime);
      }
    }
    ctx.restore();
    // Draw the hovered note last, including above overlaps within its own track.
    if (hover) drawNote(ctx, hover.note, hover.track, renderTime);
    ctx.restore();

    // 5. Draw Piano Keyboard on Left Margin (Pinned)
    const blackCapW = KEY_WIDTH * 0.62;
    const extW = KEY_WIDTH - blackCapW;
    const fontSize = Math.min(10, Math.max(7.5, zoomY * 0.65));

    const whiteKeyVal = Math.round(240 + (255 - 240) * tp);
    const whiteKeyBg = `rgb(${whiteKeyVal}, ${whiteKeyVal}, ${whiteKeyVal})`;
    const blackCapR = Math.round(26 + (39 - 26) * tp);
    const blackCapG = Math.round(26 + (39 - 26) * tp);
    const blackCapB = Math.round(30 + (42 - 30) * tp);
    const blackCapBg = `rgb(${blackCapR}, ${blackCapG}, ${blackCapB})`;
    const blackExtR = Math.round(42 + (74 - 42) * tp);
    const blackExtG = Math.round(42 + (74 - 42) * tp);
    const blackExtB = Math.round(48 + (80 - 48) * tp);
    const blackExtBg = `rgb(${blackExtR}, ${blackExtG}, ${blackExtB})`;
    // Black keys are always dark in both themes, so their text remains light
    const blackKeyTextColor = 'rgba(215, 215, 220, 0.75)';

    for (let p = MAX_PITCH; p >= MIN_PITCH; p--) {
      const y = (MAX_PITCH - p) * zoomY - scrollY;
      if (y + zoomY < 0 || y > height) continue;

      const pitchClass = p % 12;
      const isBlack = IS_BLACK_KEY[pitchClass];
      const activeColor = activePitchColors.get(p);
      const isKeyActive = activeColor !== undefined;
      const label = getNoteName(p);

      if (isKeyActive) {
        ctx.fillStyle = activeColor;
        ctx.fillRect(0, y, KEY_WIDTH, zoomY);

        ctx.strokeStyle = 'rgba(128, 128, 128, 0.25)';
        ctx.lineWidth = 1;
        ctx.strokeRect(0, y, KEY_WIDTH, zoomY);

        if (!isBlack || zoomY >= 11) {
          ctx.fillStyle = getNoteTextColor(activeColor);
          ctx.font = `600 ${fontSize}px Inter, system-ui, sans-serif`;
          const textMetrics = ctx.measureText(label);
          const textX = Math.max(4, KEY_WIDTH - textMetrics.width - 4);
          const textY = y + (zoomY + fontSize) / 2 - 1.5;
          ctx.fillText(label, textX, textY);
        }
      } else if (isBlack) {
        ctx.fillStyle = blackCapBg;
        ctx.fillRect(0, y, blackCapW, zoomY);

        ctx.fillStyle = blackExtBg;
        ctx.fillRect(blackCapW, y, extW, zoomY);

        ctx.strokeStyle = 'rgba(128, 128, 128, 0.16)';
        ctx.lineWidth = 1;
        ctx.strokeRect(0, y, blackCapW, zoomY);
        ctx.strokeRect(blackCapW, y, extW, zoomY);

        if (zoomY >= 11) {
          ctx.fillStyle = blackKeyTextColor;
          ctx.font = `${fontSize}px Inter, system-ui, sans-serif`;
          const textMetrics = ctx.measureText(label);
          const textX = Math.max(blackCapW + 2, KEY_WIDTH - textMetrics.width - 4);
          const textY = y + (zoomY + fontSize) / 2 - 1.5;
          ctx.fillText(label, textX, textY);
        }
      } else {
        ctx.fillStyle = whiteKeyBg;
        ctx.fillRect(0, y, KEY_WIDTH, zoomY);

        ctx.strokeStyle = 'rgba(128, 128, 128, 0.16)';
        ctx.lineWidth = 1;
        ctx.strokeRect(0, y, KEY_WIDTH, zoomY);

        if (pitchClass === 0 || zoomY >= 11) {
          ctx.fillStyle = 'rgba(20, 20, 20, 0.75)';
          ctx.font = `${fontSize}px Inter, system-ui, sans-serif`;
          const textMetrics = ctx.measureText(label);
          const textX = Math.max(4, KEY_WIDTH - textMetrics.width - 4);
          const textY = y + (zoomY + fontSize) / 2 - 1.5;
          ctx.fillText(label, textX, textY);
        }
      }
    }

    // 1px keyboard separator border
    const sepAlpha = 0.4 - (0.4 - 0.15) * tp;
    ctx.strokeStyle = `rgba(0, 0, 0, ${sepAlpha.toFixed(3)})`;
    ctx.lineWidth = 1;
    ctx.beginPath();
    ctx.moveTo(KEY_WIDTH + 0.5, 0);
    ctx.lineTo(KEY_WIDTH + 0.5, height);
    ctx.stroke();

    strikeEffects?.update(
      [...strikes.values()],
      renderTime,
      playbackStore.isPlaying && !playbackStore.isScrubbing && !animationStore.isOff && !reducedMotion,
      tp > 0.5,
    );
    strikeEffects?.draw(performance.now());

    // 5.5 Draw Hover Guide Line & Timestamp Pill (theme-dependent, linked with TimelineScrubber)
    const isDark = tp < 0.5;
    if (hoverTime !== null && hoverTime >= 0 && !isDraggingPlayhead && !isMiddlePanning) {
      const hoverX = KEY_WIDTH + (hoverTime * zoomX) - scrollX;
      if (hoverX >= KEY_WIDTH && hoverX <= width) {
        // Vertical dashed guide line
        ctx.strokeStyle = isDark ? 'rgba(255, 255, 255, 0.45)' : 'rgba(0, 0, 0, 0.45)';
        ctx.lineWidth = 1;
        ctx.setLineDash([3, 3]);
        ctx.beginPath();
        ctx.moveTo(hoverX, 0);
        ctx.lineTo(hoverX, height);
        ctx.stroke();
        ctx.setLineDash([]);

        // Small timestamp pill at bottom (matching WPF PianoRollHoverOverlay)
        const timeStr = formatHoverTime(hoverTime);
        ctx.font = '500 10px Inter, system-ui, sans-serif';
        const tm = ctx.measureText(timeStr);
        const pillW = Math.round(tm.width + 12);
        const pillH = 18;
        const pillX = Math.round(Math.max(KEY_WIDTH + 4, Math.min(width - pillW - 4, hoverX - pillW / 2)));
        const pillY = Math.round(height - pillH - 6);

        ctx.fillStyle = isDark ? 'rgba(24, 24, 28, 0.92)' : 'rgba(250, 250, 252, 0.94)';
        ctx.beginPath();
        ctx.roundRect(pillX, pillY, pillW, pillH, 4);
        ctx.fill();

        ctx.strokeStyle = isDark ? 'rgba(255, 255, 255, 0.22)' : 'rgba(0, 0, 0, 0.18)';
        ctx.lineWidth = 1;
        ctx.stroke();

        ctx.fillStyle = isDark ? '#ffffff' : '#111111';
        ctx.textAlign = 'center';
        ctx.textBaseline = 'middle';
        ctx.fillText(timeStr, pillX + pillW / 2, pillY + pillH / 2);
        ctx.textAlign = 'start';
        ctx.textBaseline = 'alphabetic';
      }
    }

    // 6. Draw Playhead (theme-dependent: White in dark mode, Black in light mode matching WPF)
    const playheadX = KEY_WIDTH + (renderTime * zoomX) - scrollX;
    if (!isLockedToPlayhead && playheadX >= KEY_WIDTH && playheadX <= width) {
      const playheadR = Math.round(255 - 231 * tp);
      const playheadG = Math.round(255 - 231 * tp);
      const playheadB = Math.round(255 - 227 * tp);
      const playheadColor = `rgb(${playheadR}, ${playheadG}, ${playheadB})`;

      ctx.strokeStyle = playheadColor;
      ctx.lineWidth = 1.8;
      ctx.beginPath();
      ctx.moveTo(playheadX, 0);
      ctx.lineTo(playheadX, height);
      ctx.stroke();

      ctx.fillStyle = playheadColor;
      ctx.beginPath();
      ctx.moveTo(playheadX - 6, 0);
      ctx.lineTo(playheadX + 6, 0);
      ctx.lineTo(playheadX, 10);
      ctx.closePath();
      ctx.fill();
    }

  }

  function hitTestMarker(canvasX: number, canvasY?: number): KeyChangeMarker | null {
    const song = playbackStore.activeSong;
    if (!song?.keyMarkers) return null;
    for (const km of song.keyMarkers) {
      const mTime = km.time ?? (km.timeMs / 1000);
      const kx = KEY_WIDTH + (mTime * zoomX) - scrollX;
      if (canvasY !== undefined && canvasY <= 24) {
        // Hit-test on top badge
        if (Math.abs(canvasX - kx) <= 24) {
          return km;
        }
      } else if (Math.abs(canvasX - kx) <= 8) {
        // Hit-test along vertical line
        return km;
      }
    }
    return null;
  }

  function auditionScrubNotes(currentSec: number) {
    const activePitches = new Set<number>();
    for (const track of renderTracks) {
      if (track.muted) continue;
      for (const note of track.between(currentSec, currentSec)) {
        const pitch = note.pitch ?? note.noteNumber;
        activePitches.add(pitch);
        if (!scrubAuditionedPitches.has(pitch)) {
          scrubAuditionedPitches.add(pitch);
          // Scrub preview must never send keystrokes to a game.
          api.games.tapNotePreview(pitch, true).catch(console.error);
        }
      }
    }

    for (const p of scrubAuditionedPitches) {
      if (!activePitches.has(p)) {
        scrubAuditionedPitches.delete(p);
      }
    }
  }

  function handleWheel(e: WheelEvent) {
    e.preventDefault();
    if (!canvas) return;
    hoveredTarget = null;

    const rect = canvas.getBoundingClientRect();
    const mouseCanvasX = e.clientX - rect.left;

    if (e.ctrlKey && !e.shiftKey) {
      // Horizontal Zoom X anchored around mouse cursor position
      const mouseSec = Math.max(0, (mouseCanvasX - KEY_WIDTH + scrollX) / zoomX);
      const factor = e.deltaY < 0 ? 1.18 : 0.82;
      const newZoomX = Math.min(400, Math.max(20, zoomX * factor));
      scrollX = Math.max(0, mouseSec * newZoomX - (mouseCanvasX - KEY_WIDTH));
      zoomX = newZoomX;
      onScrollChange?.(scrollX, scrollY);
    } else if (e.altKey || (e.shiftKey && e.ctrlKey)) {
      // Vertical Zoom Y (key height)
      const delta = e.deltaY < 0 ? 2 : -2;
      zoomY = Math.min(40, Math.max(2, zoomY + delta));
    } else if (e.shiftKey) {
      // Horizontal Scroll X
      isLockedToPlayhead = false;
      scrollX = Math.max(0, scrollX + e.deltaY);
      onScrollChange?.(scrollX, scrollY);
    } else {
      // Vertical Scroll Y
      const maxScrollY = Math.max(0, TOTAL_PITCHES * zoomY - (canvasContainer?.clientHeight || 400));
      scrollY = Math.max(0, Math.min(maxScrollY, scrollY + e.deltaY));
      onScrollChange?.(scrollX, scrollY);
    }
  }

  function handleMouseDown(e: MouseEvent) {
    if (!canvas) return;
    hoveredTarget = null;
    const rect = canvas.getBoundingClientRect();
    const x = e.clientX - rect.left;
    const y = e.clientY - rect.top;

    // 1. Middle mouse button OR Space/Alt held with left click => 2D Panning
    if (e.button === 1 || (e.button === 0 && (isSpacePressed || e.altKey))) {
      e.preventDefault();
      isMiddlePanning = true;
      isLockedToPlayhead = false;
      panStartX = e.clientX;
      panStartY = e.clientY;
      initialScrollX = scrollX;
      initialScrollY = scrollY;
      return;
    }

    // 2. Left click on canvas
    if (e.button === 0 && x >= KEY_WIDTH) {
      // Check if user clicked on or near a marker line or badge
      const marker = hitTestMarker(x, y);
      if (marker) {
        playbackStore.selectMarker(marker);
        const mTime = marker.time ?? (marker.timeMs / 1000);
        playbackStore.seek(mTime);

        // Start horizontal dragging of marker
        isDraggingMarker = true;
        draggedMarker = marker;
        dragStartMouseX = e.clientX;
        dragStartTimeMs = marker.timeMs ?? Math.round(mTime * 1000);

        // Open action popup
        activePopupMarker = marker;
        const kx = KEY_WIDTH + (mTime * zoomX) - scrollX;
        popupPos = { x: Math.max(KEY_WIDTH + 48, Math.min(rect.width - 48, kx)), y: 22 };
        return;
      }

      // Deselect popup when clicking canvas background
      activePopupMarker = null;

      // Otherwise playhead click / scrub
      playheadScrubScrollX = scrollX;
      playbackStore.beginScrub();
      isPointerDownPlayhead = true;
      isDraggingPlayhead = false;
      startPlayheadMouseX = e.clientX;
      startPlayheadMouseY = e.clientY;
      scrubAuditionedPitches.clear();
      updatePlayheadFromMouse(x, false);
    }
  }

  function handleMouseMove(e: MouseEvent) {
    if (!canvas) return;
    const rect = canvas.getBoundingClientRect();
    const x = e.clientX - rect.left;
    const y = e.clientY - rect.top;

    if (isMiddlePanning) {
      hoverTime = null;
      const dx = e.clientX - panStartX;
      const dy = e.clientY - panStartY;
      scrollX = Math.max(0, initialScrollX - dx);
      const maxScrollY = Math.max(0, TOTAL_PITCHES * zoomY - (canvasContainer?.clientHeight || 400));
      scrollY = Math.max(0, Math.min(maxScrollY, initialScrollY - dy));
      onScrollChange?.(scrollX, scrollY);
      return;
    }

    if (isDraggingMarker && draggedMarker) {
      hoverTime = null;
      const dx = e.clientX - dragStartMouseX;
      const dxSec = dx / zoomX;
      const rawMs = Math.max(0, dragStartTimeMs + dxSec * 1000);
      // Snap to 10ms intervals
      const snappedMs = Math.round(rawMs / 10) * 10;
      draggedMarker.timeMs = snappedMs;
      draggedMarker.time = snappedMs / 1000;

      const kx = KEY_WIDTH + (draggedMarker.time * zoomX) - scrollX;
      popupPos = { x: Math.max(KEY_WIDTH + 48, Math.min(rect.width - 48, kx)), y: 22 };
      return;
    }

    if (isPointerDownPlayhead && !isDraggingPlayhead) {
      const dist = Math.hypot(e.clientX - startPlayheadMouseX, e.clientY - startPlayheadMouseY);
      if (dist >= 4) {
        isDraggingPlayhead = true;
      }
    }

    if (isDraggingPlayhead) {
      hoverTime = null;
      updatePlayheadFromMouse(x, true);
      auditionScrubNotes(playbackStore.currentTimeSeconds);
    } else {
      if (x >= KEY_WIDTH && x <= rect.width && y >= 0 && y <= rect.height) {
        const totalDur = playbackStore.durationSeconds || 120;
        const targetX = x - KEY_WIDTH + scrollX;
        hoverTime = Math.max(0, Math.min(totalDur, targetX / zoomX));
      } else {
        hoverTime = null;
      }
      hoveredMarker = hitTestMarker(x, y);
      detectNoteHover(x, y);
    }
  }

  function handleWindowMouseMove(e: MouseEvent) {
    if (canvasContainer?.contains(e.target as Node)) return;
    if (isPointerDownPlayhead || isMiddlePanning || isDraggingMarker) handleMouseMove(e);
  }

  function handleMouseUp() {
    if (isDraggingMarker && draggedMarker && playbackStore.activeSong) {
      playbackStore.activeSong.keyMarkers?.sort((a, b) => a.timeMs - b.timeMs);
      api.playback.setKeyMarkers(playbackStore.activeSong.keyMarkers || []);
      playbackStore.scheduleAutoSaveSongSettings();
    }
    isDraggingMarker = false;
    draggedMarker = null;
    isMiddlePanning = false;
    if (isPointerDownPlayhead) playbackStore.endScrub();
    isPointerDownPlayhead = false;
    isDraggingPlayhead = false;
    dirty = true;
    scrubAuditionedPitches.clear();
  }

  function handleDblClick(e: MouseEvent) {
    if (!canvas) return;
    const rect = canvas.getBoundingClientRect();
    const x = e.clientX - rect.left;
    if (x >= KEY_WIDTH) {
      const targetSec = Math.max(0, (x - KEY_WIDTH + scrollX) / zoomX);
      playbackStore.addKeyMarker(targetSec);
    }
  }

  function updatePlayheadFromMouse(canvasX: number, immediate = false) {
    // Keep the drag's time mapping stable while the locked viewport moves.
    const originX = isPointerDownPlayhead ? playheadScrubScrollX : scrollX;
    const targetX = canvasX - KEY_WIDTH + originX;
    const targetSec = Math.max(0, targetX / zoomX);
    playbackStore.seek(targetSec, { immediate });
  }

  function detectNoteHover(canvasX: number, canvasY: number) {
    if (canvasX < KEY_WIDTH || canvasX > width || canvasY < 0 || canvasY > height) {
      hoveredTarget = null;
      return;
    }

    const time = (canvasX - KEY_WIDTH + scrollX) / zoomX;
    for (const track of [...layeredTracks].reverse()) {
      if (track.muted) continue;
      let hit: RollNote | null = null;
      for (const note of track.between(time - 4 / zoomX, time)) {
        const { x, y, w, h } = noteBounds(note);
        if (canvasX >= x && canvasX <= x + w && canvasY >= y && canvasY <= y + h) {
          hit = note;
        }
      }
      // The last matching note is the one painted on top within this track.
      if (hit) {
        if (hoveredTarget?.note !== hit || hoveredTarget.track !== track) {
          hoverStartedAt = performance.now();
          hoveredTarget = { note: hit, track };
        }
        tooltipPos = { x: canvasX + 10, y: canvasY - 10 };
        return;
      }
    }
    hoveredTarget = null;
  }

  export function fitKeyHeight() {
    if (!canvasContainer) return;
    const h = canvasContainer.clientHeight;
    if (h <= 0) return;

    let lowest = Infinity;
    let highest = -Infinity;
    for (const track of renderTracks) {
      if (track.muted) continue;
      for (const note of track.notes) {
        const pitch = note.pitch ?? note.noteNumber;
        if (pitch < MIN_PITCH || pitch > MAX_PITCH) continue;
        lowest = Math.min(lowest, pitch);
        highest = Math.max(highest, pitch);
      }
    }
    if (!Number.isFinite(lowest)) {
      lowest = 48;
      highest = 83;
    }
    const paddedLow = Math.max(MIN_PITCH, lowest - 3);
    const paddedHigh = Math.min(MAX_PITCH, highest + 3);
    zoomY = Math.max(2, Math.min(40, h / (paddedHigh - paddedLow + 1)));
    const middleY = (MAX_PITCH - (lowest + highest) / 2 + 0.5) * zoomY;
    const maxScrollY = Math.max(0, TOTAL_PITCHES * zoomY - h);
    scrollY = Math.max(0, Math.min(maxScrollY, middleY - h / 2));
    onScrollChange?.(scrollX, scrollY);
  }

  function handleGlobalKeyDown(e: KeyboardEvent) {
    if (uiStore.activeTab !== 'studio' || uiStore.activeDialog) return;
    if (e.defaultPrevented || (e.target instanceof Element &&
        e.target.closest('button, a, [role="tab"], [role="switch"]'))) return;
    const activeEl = document.activeElement;
    const isInput = activeEl instanceof HTMLInputElement ||
                    activeEl instanceof HTMLTextAreaElement ||
                    activeEl instanceof HTMLSelectElement ||
                    activeEl?.getAttribute('contenteditable') === 'true';

    if (isInput) return;

    if (e.code === 'Space') {
      e.preventDefault();
      isSpacePressed = true;
      if (!e.repeat) playbackStore.togglePlay();
      return;
    }

    if (e.key === 'ArrowLeft') {
      e.preventDefault();
      const step = e.shiftKey ? 5 : 1;
      playbackStore.seek(Math.max(0, playbackStore.currentTimeSeconds - step));
      return;
    }

    if (e.key === 'ArrowRight') {
      e.preventDefault();
      const step = e.shiftKey ? 5 : 1;
      playbackStore.seek(Math.min(playbackStore.durationSeconds, playbackStore.currentTimeSeconds + step));
      return;
    }

    if (e.key === 'ArrowUp') {
      e.preventDefault();
      scrollY = Math.max(0, scrollY - 32);
      onScrollChange?.(scrollX, scrollY);
      return;
    }

    if (e.key === 'ArrowDown') {
      e.preventDefault();
      const maxScrollY = Math.max(0, TOTAL_PITCHES * zoomY - (canvasContainer?.clientHeight || 400));
      scrollY = Math.min(maxScrollY, scrollY + 32);
      onScrollChange?.(scrollX, scrollY);
      return;
    }

    if ((e.ctrlKey || e.metaKey) && (e.key === '=' || e.key === '+')) {
      e.preventDefault();
      zoomX = Math.min(400, zoomX * 1.25);
      return;
    }

    if ((e.ctrlKey || e.metaKey) && (e.key === '-' || e.key === '_')) {
      e.preventDefault();
      zoomX = Math.max(20, zoomX * 0.8);
      return;
    }

    if ((e.ctrlKey || e.metaKey) && e.key === '0') {
      e.preventDefault();
      zoomX = DEFAULT_UI_PREFERENCES.studio.zoomX;
      return;
    }

    if (e.altKey && (e.key === '=' || e.key === '+')) {
      e.preventDefault();
      zoomY = Math.min(40, zoomY + 2);
      return;
    }

    if (e.altKey && (e.key === '-' || e.key === '_')) {
      e.preventDefault();
      zoomY = Math.max(2, zoomY - 2);
      return;
    }

    if (e.altKey && e.key === '0') {
      e.preventDefault();
      fitKeyHeight();
      return;
    }

    if (e.key === '[') {
      e.preventDefault();
      playbackStore.navigateMarker('prev');
      return;
    }

    if (e.key === ']') {
      e.preventDefault();
      playbackStore.navigateMarker('next');
      return;
    }

    if (e.key === 'Delete' || e.key === 'Backspace') {
      if (playbackStore.selectedMarkerId) {
        e.preventDefault();
        playbackStore.deleteSelectedMarker();
      }
      return;
    }

    if (e.key === 'Home') {
      e.preventDefault();
      playbackStore.seek(0);
      scrollX = 0;
      onScrollChange?.(scrollX, scrollY);
      return;
    }

    if (e.key === 'End') {
      e.preventDefault();
      playbackStore.seek(playbackStore.durationSeconds);
      return;
    }
  }

  function handleGlobalKeyUp(e: KeyboardEvent) {
    if (e.code === 'Space') {
      isSpacePressed = false;
    }
  }

  let resizeObserver: ResizeObserver | null = null;

  onMount(() => {
    ctx = canvas.getContext('2d');
    strikeEffects = new PianoStrikeEffects(effectsCanvas);
    motionQuery = window.matchMedia('(prefers-reduced-motion: reduce)');
    handleMotionChange();
    motionQuery.addEventListener('change', handleMotionChange);
    resizeCanvas();

    if (typeof ResizeObserver !== 'undefined' && canvasContainer) {
      resizeObserver = new ResizeObserver(() => {
        resizeCanvas();
      });
      resizeObserver.observe(canvasContainer);
    }

    window.addEventListener('resize', resizeCanvas);
    window.addEventListener('mouseup', handleMouseUp);
    window.addEventListener('mousemove', handleWindowMouseMove);
    window.addEventListener('blur', handleMouseUp);
    window.addEventListener('keydown', handleGlobalKeyDown);
    window.addEventListener('keyup', handleGlobalKeyUp);
    animFrameId = requestAnimationFrame(render);

    // Initial center around pitch 60
    const initialCenterPitch = 60;
    const containerH = canvasContainer.clientHeight || 400;
    scrollY = Math.max(0, (MAX_PITCH - initialCenterPitch) * zoomY - containerH / 2);
  });

  onDestroy(() => {
    strikeEffects?.clear();
    motionQuery?.removeEventListener('change', handleMotionChange);
    if (animFrameId) cancelAnimationFrame(animFrameId);
    if (resizeObserver) {
      resizeObserver.disconnect();
      resizeObserver = null;
    }
    window.removeEventListener('resize', resizeCanvas);
    handleMouseUp();
    window.removeEventListener('mouseup', handleMouseUp);
    window.removeEventListener('mousemove', handleWindowMouseMove);
    window.removeEventListener('blur', handleMouseUp);
    window.removeEventListener('keydown', handleGlobalKeyDown);
    window.removeEventListener('keyup', handleGlobalKeyUp);
  });
</script>

<div
  class="canvas-container"
  class:is-panning={isMiddlePanning}
  class:is-space-grab={isSpacePressed && !isMiddlePanning}
  class:is-marker-hover={hoveredMarker !== null}
  bind:this={canvasContainer}
  onwheel={handleWheel}
  onmousedown={handleMouseDown}
  onmousemove={handleMouseMove}
  onmouseup={handleMouseUp}
  ondblclick={handleDblClick}
  onmouseleave={() => { hoveredTarget = null; hoveredMarker = null; hoverTime = null; }}
  role="region"
  aria-label="Piano Roll Interactive Editor"
>
  <canvas bind:this={canvas} class="roll-canvas"></canvas>
  <canvas bind:this={effectsCanvas} class="strike-canvas" aria-hidden="true"></canvas>

  {#if hoveredNote}
    <div
      class="note-tooltip"
      style="left: {tooltipPos.x}px; top: {tooltipPos.y}px;"
    >
      <div class="tip-pitch">{getNoteName(hoveredNote.pitch ?? hoveredNote.noteNumber)} (MIDI {hoveredNote.pitch ?? hoveredNote.noteNumber})</div>
      <div class="tip-detail tip-duration">
        <span>Start: {noteStart(hoveredNote).toFixed(2)}s | Dur: {(hoveredNote.rawDuration ?? noteDuration(hoveredNote)).toFixed(2)}s</span>
        {#if hoveredNote.tapDurationMs !== undefined}
          <FluentIcon name="ChevronRight" size={10} />
          <span>{hoveredNote.tapDurationMs}ms</span>
        {/if}
      </div>
      <div class="tip-detail">Vel: {hoveredNote.velocity ?? 80}</div>
    </div>
  {/if}

  {#if activePopupMarker}
    <MarkerActionPopup
      marker={activePopupMarker}
      x={popupPos.x}
      y={popupPos.y}
      onDelete={(id) => {
        playbackStore.deleteKeyMarker(id);
        activePopupMarker = null;
      }}
      onEdit={(m) => {
        playbackStore.selectMarker(m);
      }}
      onClose={() => {
        activePopupMarker = null;
      }}
    />
  {/if}
</div>

<style>
  .canvas-container {
    width: 100%;
    min-height: 0;
    position: relative;
    overflow: hidden;
    user-select: none;
    cursor: crosshair;
    flex: 1 1 0;
    outline: none;
  }

  .canvas-container.is-panning {
    cursor: grabbing !important;
  }

  .canvas-container.is-space-grab {
    cursor: grab !important;
  }

  .canvas-container.is-marker-hover {
    cursor: pointer !important;
  }

  .roll-canvas {
    display: block;
    width: 100% !important;
    height: 100% !important;
  }

  .strike-canvas {
    position: absolute;
    inset: 0;
    width: 100%;
    height: 100%;
    pointer-events: none;
  }

  .note-tooltip {
    position: absolute;
    pointer-events: none;
    background: var(--bg-card, #27272a);
    border: none;
    border-radius: var(--radius-sm);
    box-shadow: var(--shadow-xl);
    padding: 6px 10px;
    z-index: 10;
    white-space: nowrap;
    animation: tip-in var(--anim-fast, 0.1s) ease;
  }

  .tip-pitch {
    font-size: 11.5px;
    font-weight: 600;
    color: var(--accent);
  }

  .tip-detail {
    font-size: 10.5px;
    color: var(--text-secondary);
    margin-top: 1px;
  }

  .tip-duration { display: flex; align-items: center; gap: 3px; }

  @keyframes tip-in {
    from { opacity: 0; transform: translateY(4px); }
    to { opacity: 1; transform: translateY(0); }
  }
</style>
