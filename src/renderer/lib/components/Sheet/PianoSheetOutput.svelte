<script lang="ts">
  import { onMount, untrack } from 'svelte';
  import { themeStore } from '../../state/themeStore.svelte';
  import { playbackStore } from '../../state/playbackStore.svelte';
  import { persistScroll } from '../../actions/persistScroll';
  import type { SheetPart } from '../../utils/pianoSheet';
  import { layoutSheetRows, sheetVisibleRange, sheetAttackMs, type SheetFragment, type SheetRow } from '../../utils/sheetViewport';
  import { sheetGlowValue, startSheetGlow, releaseSheetGlow, sheetGlowAnimating, type SheetGlowPulse } from '../../utils/sheetGlow';

  let { parts, activeParts, strikeParts, playing, resetKey, speed, label }: {
    parts: SheetPart[]; activeParts: Set<number>; strikeParts: Set<number>; playing: boolean;
    resetKey: object; speed: number; label: string;
  } = $props();
  const text = $derived(parts.map(part => part.text).join(''));
  let host: HTMLDivElement;
  let input: HTMLTextAreaElement;
  let baseCanvas: HTMLCanvasElement;
  let glowCanvas: HTMLCanvasElement;
  let invalidate = (_colorsChanged = false) => {};

  $effect(() => { parts; speed; untrack(() => invalidate()); });
  $effect(() => { activeParts; strikeParts; playing; resetKey; untrack(() => invalidate()); });
  $effect(() => { themeStore.accentColor; themeStore.themeProgress; untrack(() => invalidate(true)); });

  onMount(() => {
    const baseContext = baseCanvas.getContext('2d');
    const glowContext = glowCanvas.getContext('2d');
    const colorContext = document.createElement('canvas').getContext('2d', { willReadFrequently: true });
    if (!baseContext || !glowContext || !colorContext) {
      input.style.color = 'var(--text-primary)';
      input.style.webkitTextFillColor = 'currentColor';
      return;
    }
    const base: CanvasRenderingContext2D = baseContext;
    const glow: CanvasRenderingContext2D = glowContext;
    colorContext.canvas.width = colorContext.canvas.height = 1;
    const font = '13px Consolas, "Courier New", monospace';
    const lineHeight = 20.8;
    const padding = 14;
    base.font = font;
    const metrics = base.measureText('Mg');
    const cellWidth = base.measureText('M').width;
    const ascent = metrics.fontBoundingBoxAscent ?? 12;
    const descent = metrics.fontBoundingBoxDescent ?? 3;
    const baseline = (lineHeight - ascent - descent) / 2 + ascent;
    const widths = new Map<string, number>();
    const widthOf = (character: string) => {
      let width = widths.get(character);
      if (width === undefined) {
        width = base.measureText(character).width / cellWidth;
        widths.set(character, width);
      }
      return width;
    };
    let rows: SheetRow[] = [];
    let visible = new Map<number, Array<{ fragment: SheetFragment; row: number }>>();
    let previousParts: SheetPart[] | undefined;
    let previousActive = new Set<number>();
    let previousStrikes: Set<number> | undefined;
    let previousResetKey: object | undefined;
    let columns = 0;
    let width = 0, height = 0, ratio = 0, scrollTop = -1, scrollLeft = -1;
    let frame = 0;
    let disposed = false;
    let baseDirty = true;
    let baseColor = '#ffffff', background = '#1f1f1f', accent = '#1db954';
    let primaryRgba = [255, 255, 255, 255], accentRgba = [29, 185, 84, 255];
    const pulses = new Map<number, SheetGlowPulse>();
    const motion = matchMedia('(prefers-reduced-motion: reduce)');
    const rgba = (color: string) => {
      colorContext.clearRect(0, 0, 1, 1);
      colorContext.fillStyle = color;
      colorContext.fillRect(0, 0, 1, 1);
      return [...colorContext.getImageData(0, 0, 1, 1).data];
    };
    const requestPaint = () => {
      if (!disposed && !frame && !document.hidden) frame = requestAnimationFrame(paint);
    };
    const sync = (colorsChanged = false) => {
      const now = performance.now();
      let changed = colorsChanged;
      if (previousParts !== parts) {
        previousParts = parts;
        columns = 0;
        pulses.clear();
        previousActive = new Set();
        baseDirty = true;
      }
      if (previousResetKey !== resetKey || !playing) {
        changed ||= pulses.size > 0;
        pulses.clear();
        previousActive = new Set();
        previousResetKey = resetKey;
      }
      for (const index of previousActive) {
        if (!activeParts.has(index)) {
          changed = true;
          const pulse = pulses.get(index);
          if (pulse) releaseSheetGlow(pulse, now);
        }
      }
      for (const index of playing ? activeParts : []) {
        if (!previousActive.has(index)) {
          const part = parts[index];
          if (!part?.presses) continue;
          changed = true;
          const pulse = pulses.get(index);
          pulses.set(index, startSheetGlow(now, sheetAttackMs(part, speed), pulse ? sheetGlowValue(pulse, now, motion.matches) : 0));
        }
      }
      if (playing && previousStrikes !== strikeParts) {
        for (const index of strikeParts) {
          if (pulses.has(index)) continue;
          const part = parts[index];
          if (!part?.presses) continue;
          const pulse = startSheetGlow(now, sheetAttackMs(part, speed));
          if (!activeParts.has(index)) releaseSheetGlow(pulse, now);
          pulses.set(index, pulse);
          changed = true;
        }
      }
      previousStrikes = strikeParts;
      previousActive = playing ? activeParts : new Set();
      if (colorsChanged) {
        const styles = getComputedStyle(host);
        baseColor = styles.color;
        background = styles.backgroundColor;
        accent = styles.getPropertyValue('--accent-color').trim();
        primaryRgba = rgba(baseColor);
        accentRgba = rgba(accent);
        baseDirty = true;
      }
      if (changed || baseDirty) requestPaint();
    };
    function paint(now: number) {
      frame = 0;
      if (disposed || document.hidden) return;
      const nextWidth = input.clientWidth, nextHeight = input.clientHeight;
      if (nextWidth <= 0 || nextHeight <= 0) return;
      const nextRatio = window.devicePixelRatio || 1;
      if (nextWidth !== width || nextHeight !== height || nextRatio !== ratio) {
        width = nextWidth; height = nextHeight; ratio = nextRatio;
        for (const canvas of [baseCanvas, glowCanvas]) {
          canvas.width = Math.max(1, Math.round(width * ratio));
          canvas.height = Math.max(1, Math.round(height * ratio));
          canvas.style.width = `${width}px`; canvas.style.height = `${height}px`;
        }
        for (const context of [base, glow]) {
          context.setTransform(ratio, 0, 0, ratio, 0, 0);
          context.font = font;
          context.textBaseline = 'alphabetic';
        }
        baseDirty = true;
      }
      const nextColumns = Math.max(1, (width - padding * 2) / cellWidth);
      if (columns !== nextColumns) {
        columns = nextColumns;
        rows = layoutSheetRows(parts, columns, widthOf);
        baseDirty = true;
      }
      if (scrollTop !== input.scrollTop || scrollLeft !== input.scrollLeft) {
        scrollTop = input.scrollTop; scrollLeft = input.scrollLeft; baseDirty = true;
      }
      if (baseDirty) {
        base.clearRect(0, 0, width, height);
        visible = new Map();
        const range = sheetVisibleRange(rows.length, Math.max(0, scrollTop - padding), height, lineHeight);
        for (let rowIndex = range.start; rowIndex < range.end; rowIndex++) {
          let run = '', runColumn = 0, runDim = false;
          const flush = () => {
            if (!run) return;
            base.globalAlpha = runDim ? 0.35 : 1;
            base.fillStyle = baseColor;
            base.fillText(run, padding + runColumn * cellWidth - scrollLeft, padding + rowIndex * lineHeight + baseline - scrollTop);
            run = '';
          };
          for (const fragment of rows[rowIndex].fragments) {
            const part = parts[fragment.partIndex];
            const dim = !!(part.delimiter || part.notation);
            if (run && runDim !== dim) flush();
            if (!run) { runColumn = fragment.column; runDim = dim; }
            run += fragment.text;
            if (part.presses) {
              const locations = visible.get(fragment.partIndex) ?? [];
              locations.push({ fragment, row: rowIndex });
              visible.set(fragment.partIndex, locations);
            }
          }
          flush();
        }
        base.globalAlpha = 1;
        baseDirty = false;
      }
      glow.clearRect(0, 0, width, height);
      const drawing: Array<{ index: number; value: number; fragment: SheetFragment; row: number }> = [];
      let animating = false;
      for (const [index, pulse] of pulses) {
        const value = sheetGlowValue(pulse, now, motion.matches);
        if (pulse.releaseAt !== null && now >= pulse.releaseAt && value <= 0.001) { pulses.delete(index); continue; }
        const locations = visible.get(index);
        if (!locations) continue;
        if (sheetGlowAnimating(pulse, now, motion.matches)) animating = true;
        for (const location of locations) drawing.push({ index, value, ...location });
      }
      // Erase the static glyphs beneath active cells before painting their eased colors.
      glow.globalAlpha = 1;
      glow.fillStyle = background;
      for (const { fragment, row } of drawing) {
        glow.fillRect(padding + fragment.column * cellWidth - scrollLeft, padding + row * lineHeight - scrollTop,
          fragment.width * cellWidth, lineHeight);
      }
      for (const { index, value, fragment, row } of drawing) {
        const channels = primaryRgba.map((channel, i) => channel + (accentRgba[i] - channel) * value);
        glow.fillStyle = `rgba(${channels[0]},${channels[1]},${channels[2]},${channels[3] / 255})`;
        glow.globalAlpha = parts[index].notation ? 0.35 : 1;
        glow.shadowColor = `rgba(${accentRgba[0]},${accentRgba[1]},${accentRgba[2]},${0.65 * value})`;
        glow.shadowBlur = 8 * value;
        glow.fillText(fragment.text, padding + fragment.column * cellWidth - scrollLeft, padding + row * lineHeight + baseline - scrollTop);
      }
      glow.globalAlpha = 1; glow.shadowBlur = 0;
      if (animating) requestPaint();
    }
    invalidate = sync;
    const observer = new ResizeObserver(() => { baseDirty = true; requestPaint(); });
    observer.observe(input);
    input.addEventListener('scroll', requestPaint, { passive: true });
    window.addEventListener('resize', requestPaint);
    motion.addEventListener('change', requestPaint);
    document.addEventListener('visibilitychange', requestPaint);
    sync(true);
    return () => {
      disposed = true;
      invalidate = () => {};
      cancelAnimationFrame(frame);
      observer.disconnect();
      input.removeEventListener('scroll', requestPaint);
      window.removeEventListener('resize', requestPaint);
      motion.removeEventListener('change', requestPaint);
      document.removeEventListener('visibilitychange', requestPaint);
    };
  });
</script>

<div class="sheet-output" bind:this={host}>
  <canvas class="sheet-base" bind:this={baseCanvas} aria-hidden="true"></canvas>
  <canvas class="sheet-glow" bind:this={glowCanvas} aria-hidden="true"></canvas>
  <textarea class="sheet-native" bind:this={input} use:persistScroll={`sheet:${playbackStore.activeSong?.id ?? 'none'}`} value={text} readonly aria-label={label} spellcheck="false" wrap="soft"></textarea>
</div>

<style>
  .sheet-output {
    position: relative;
    flex: 1;
    min-height: 0;
    width: 100%;
    overflow: hidden;
    contain: strict;
    background: var(--card-bg);
    color: var(--text-primary);
    font: 13px / 20.8px 'Consolas', 'Courier New', monospace;
  }
  canvas {
    position: absolute;
    left: 0;
    top: 0;
    pointer-events: none;
  }
  .sheet-native {
    position: absolute;
    inset: 0;
    width: 100%;
    height: 100%;
    margin: 0;
    padding: 14px;
    border: 0;
    outline: none;
    resize: none;
    background: transparent;
    color: transparent;
    -webkit-text-fill-color: transparent;
    caret-color: transparent;
    font: inherit;
    font-variant-ligatures: none;
    white-space: pre-wrap;
    word-break: break-all;
    line-break: anywhere;
    overflow-wrap: anywhere;
    user-select: text;
    cursor: text;
  }
  .sheet-native::selection {
    color: transparent;
    background: color-mix(in srgb, var(--accent-color) 30%, transparent);
  }
</style>
