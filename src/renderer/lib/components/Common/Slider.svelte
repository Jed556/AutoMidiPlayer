<script lang="ts" module>
  /**
   * Represents an individual step / tick stop on the slider.
   */
  export interface SliderStep {
    /** The raw numeric value of this step */
    value: number;
    /** Human-readable text label displayed under the step */
    label?: string;
    /** FluentIcon name or icon identifier displayed above the label */
    icon?: string;
    /** Hover tooltip text specific to this step */
    title?: string;
    /** Whether this specific step is disabled/unselectable */
    disabled?: boolean;
  }

  /**
   * Represents a designated area/zone along the slider track.
   * Areas can independently define whether movement is 'sliding' (continuous) or 'steps' (discrete),
   * have custom step increments, snapping rules, magnetic boundaries, and distinct visual colors.
   */
  export interface SliderArea {
    /** Start value of this area (inclusive). Defaults to slider min if omitted or first area. */
    min?: number;
    /** Alias for min */
    from?: number;
    /** End value of this area (inclusive). Defaults to slider max if omitted or last area. */
    max?: number;
    /** Alias for max */
    to?: number;
    /**
     * Area movement behavior:
     * - 'sliding': smooth continuous sliding
     * - 'steps': discrete step stops
     */
    type?: 'sliding' | 'steps';
    /**
     * Step increment within this area.
     * When type is 'steps', snaps to increments of this step.
     * When type is 'sliding', specifies fine value resolution/rounding.
     */
    step?: number;
    /** Explicit step / tick points within this area (for 'steps' type) */
    steps?: (number | SliderStep)[];
    /** Whether to enable magnetic snap at the boundaries of this area (default: true) */
    snapBoundary?: boolean;
    /** Specific snap points within this area */
    snapPoints?: number[];
    /** Magnetic snap radius for points in this area */
    snapRadius?: number;
    /** Custom fill color / accent for this area (e.g. 'var(--warning-color, #F59E0B)') */
    color?: string;
    /** Optional custom CSS class name for styling this area */
    class?: string;
    /** Optional label for this area */
    label?: string;
  }

  /**
   * Props for the Slider component.
   */
  export interface SliderProps {
    /** Visual treatment; seekbar uses a quiet fill and reveals its thumb on interaction. */
    variant?: 'default' | 'seekbar';
    orientation?: 'horizontal' | 'vertical';
    /** Defaults to bottom horizontally and right vertically. */
    labelPosition?: 'top' | 'bottom' | 'left' | 'right';
    /** Move crowded labels to the opposite side. Defaults to false. */
    staggerLabels?: boolean;
    /** Accessible name for the interactive slider. */
    ariaLabel?: string;
    /** Current numeric value (two-way bindable) */
    value?: number;
    /** Minimum value (defaults to 0 or min step value) */
    min?: number;
    /** Maximum value (defaults to 100 or max step value) */
    max?: number;
    /** Uniform step increment (e.g. 1, 5, 0.1) */
    step?: number;

    /** Defined modular areas across the slider range with individual 'sliding' or 'steps' behaviors */
    areas?: SliderArea[];
    /** Magnetic snap points across the slider (e.g. [1.0] for snapping at 100%) */
    snapPoints?: number[];
    /**
     * Distance in value units within which values will snap to snap points or area boundaries.
     * Defaults to ~2.5% of the total slider range if omitted.
     */
    snapRadius?: number;
    /** Show visual divider ticks at boundaries between areas (default: true if areas.length > 1) */
    showAreaDividers?: boolean;

    /** Defined steps / tick points with optional labels and icons */
    steps?: (number | SliderStep)[];
    /** Shorthand for numerical tick values (e.g. [0, 15, 21, 37, 61, 88]) */
    ticks?: number[];
    /** Parallel array of text labels corresponding to steps/ticks */
    labels?: string[];
    /** Short labels used when neighboring ticks have less than minTickGap (or 48px) of space. */
    compactLabels?: string[];
    /** Parallel array of FluentIcon names corresponding to steps/ticks */
    icons?: string[];

    /** Whether to snap to steps/ticks when dragging or clicking. If smooth is true, defaults to false. */
    snap?: boolean;
    /** If true, moves completely smoothly without snapping to discrete step stops */
    smooth?: boolean;
    /** Keep ticks, thumb and pointer input proportional to numeric values, ignoring minimum tick gaps. */
    proportionalSpacing?: boolean;
    /** Minimum pixel gap between adjacent ticks to prevent crowding/overlapping */
    minTickGap?: number;
    /** Alias for minTickGap */
    minimumTickGap?: number;

    /** Show tick pips on the track groove (default: true if steps/ticks defined) */
    showStepMarkers?: boolean;
    /** Convenience flag: if true, hides tick pips on the track while keeping text/icons */
    hideStepMarkers?: boolean;
    /** Show step text and/or icons row below the track (default: true if any step has label/icon) */
    showStepLabels?: boolean;
    /** Control whether step icons are rendered in the steps row (default: true) */
    showStepIcons?: boolean;
    /** Control whether step text labels are rendered in the steps row (default: true) */
    showStepText?: boolean;
    /** Alignment strategy for step labels ('center', 'edges', or 'auto') */
    alignLabels?: 'center' | 'edges' | 'auto';

    /** Whether to ease transitions between steps when holding and dragging (default: true if steps/snap defined) */
    easeOnDrag?: boolean;

    /** Whether to animate thumb and fill transitions on click/programmatic changes (default: true) */
    animateTransitions?: boolean;
    /** Override duration in milliseconds; if omitted, links to settings animationStore */
    animationDurationMs?: number;

    /** Show floating tooltip above thumb (default: true) */
    showTooltip?: boolean;
    /** If true, only shows tooltip during active drag scrubbing (default: false) */
    tooltipOnDragOnly?: boolean;
    /** Pipe-separated labels string (e.g. "Errors|Warnings|All") matching WPF ThumbToolTipOptions */
    thumbToolTipOptions?: string | string[];
    /** Format string fallback (e.g. "Volume: {0}%") matching WPF ThumbToolTipFallback */
    thumbToolTipFallback?: string;
    /** Custom tooltip formatter function */
    formatTooltip?: (val: number, step?: SliderStep) => string;

    /** Show inline value display on the right side of the slider (default: false) */
    showValue?: boolean;
    /** Formatter for right-side inline value display */
    formatValue?: (val: number) => string;

    /** Whether the slider is disabled */
    disabled?: boolean;
    /** Tooltip title on the root element */
    title?: string;
    /** Custom CSS class names */
    class?: string;
    /** Inline style string */
    style?: string;
    /** HTML id */
    id?: string;
    /** HTML name */
    name?: string;

    /** Whether to allow adjusting the slider using the mouse wheel (default: false) */
    enableWheel?: boolean;
    /** Alias for enableWheel */
    scrollable?: boolean;
    /** Alias for enableWheel */
    enableScroll?: boolean;
    /** Step amount when scrolling with mouse wheel (defaults to step, or 5% of range) */
    wheelStep?: number;

    /** Event fired when value commits (pointer up, keyboard change, step click) */
    onchange?: (val: number) => void;
    /** Event fired continuously during pointer movement / drag */
    oninput?: (val: number) => void;
    /** Event fired during seeking/scrubbing */
    onseek?: (val: number) => void;
  }
</script>

<script lang="ts">
  import { portal } from '../../actions/portal';
  import { animationStore } from '../../state/animationStore.svelte';
  import FluentIcon from './FluentIcon.svelte';

  let {
    variant = 'default',
    orientation = 'horizontal',
    labelPosition,
    staggerLabels = false,
    ariaLabel,
    value = $bindable(0),
    min,
    max,
    step,
    areas,
    snapPoints,
    snapRadius,
    showAreaDividers,
    steps,
    ticks,
    labels,
    compactLabels,
    icons,
    snap,
    smooth = false,
    proportionalSpacing = false,
    minTickGap = 0,
    minimumTickGap = 0,
    showStepMarkers,
    hideStepMarkers = false,
    showStepLabels,
    showStepIcons = true,
    showStepText = true,
    alignLabels = 'auto',
    easeOnDrag,
    animateTransitions = true,
    animationDurationMs,
    showTooltip = true,
    tooltipOnDragOnly = false,
    thumbToolTipOptions = '',
    thumbToolTipFallback = '',
    formatTooltip,
    showValue = false,
    formatValue = (v) => String(v),
    enableWheel = false,
    scrollable = false,
    enableScroll = false,
    wheelStep,
    disabled = false,
    title = '',
    class: className = '',
    style = '',
    id,
    name,
    onchange,
    oninput,
    onseek
  }: SliderProps = $props();

  // ==========================================
  // Options & Steps Normalization
  // ==========================================
  let resolvedOptions = $derived.by<string[]>(() => {
    if (!thumbToolTipOptions) return [];
    if (Array.isArray(thumbToolTipOptions)) return thumbToolTipOptions;
    return thumbToolTipOptions
      .split('|')
      .map((s) => s.trim())
      .filter((s) => s.length > 0);
  });

  let normalizedSteps = $derived.by<SliderStep[]>(() => {
    // 1. Explicit steps array
    if (steps && steps.length > 0) {
      return steps.map((s, idx) => {
        if (typeof s === 'number') {
          const lbl = labels?.[idx] ?? resolvedOptions[idx];
          const ic = icons?.[idx];
          return { value: s, label: lbl, icon: ic };
        }
        return {
          value: s.value,
          label: s.label ?? labels?.[idx] ?? resolvedOptions[idx],
          icon: s.icon ?? icons?.[idx],
          title: s.title,
          disabled: s.disabled
        };
      });
    }

    // 2. Explicit ticks array
    if (ticks && ticks.length > 0) {
      return ticks.map((t, idx) => ({
        value: t,
        label: labels?.[idx] ?? resolvedOptions[idx],
        icon: icons?.[idx]
      }));
    }

    // 3. ThumbToolTipOptions pipe array without explicit steps
    if (resolvedOptions.length > 0) {
      const baseMin = min ?? 0;
      return resolvedOptions.map((opt, idx) => ({
        value: baseMin + idx,
        label: opt,
        icon: icons?.[idx]
      }));
    }

    // 4. Uniform step with labels or icons provided
    if (step && step > 0 && min !== undefined && max !== undefined && (labels || icons)) {
      const count = Math.round((max - min) / step);
      const generated: SliderStep[] = [];
      for (let i = 0; i <= count; i++) {
        const v = min + i * step;
        generated.push({
          value: v,
          label: labels?.[i],
          icon: icons?.[i]
        });
      }
      return generated;
    }

    return [];
  });

  // ==========================================
  // Areas Normalization & Magnetic Snap
  // ==========================================
  interface NormalizedArea {
    from: number;
    to: number;
    type: 'sliding' | 'steps';
    step?: number;
    steps?: SliderStep[];
    snapBoundary: boolean;
    snapPoints: number[];
    snapRadius?: number;
    color?: string;
    className?: string;
    label?: string;
  }

  let normalizedAreas = $derived.by<NormalizedArea[]>(() => {
    if (!areas || areas.length === 0) return [];
    return areas
      .map((a, idx) => {
        const from = a.from ?? a.min ?? (idx === 0 ? (min ?? 0) : 0);
        const to = a.to ?? a.max ?? (max ?? 100);

        let parsedSteps: SliderStep[] | undefined = undefined;
        if (a.steps && a.steps.length > 0) {
          parsedSteps = a.steps.map((s) => (typeof s === 'number' ? { value: s } : s));
        }

        return {
          from: Math.min(from, to),
          to: Math.max(from, to),
          type: a.type ?? 'sliding',
          step: a.step,
          steps: parsedSteps,
          snapBoundary: a.snapBoundary !== false,
          snapPoints: a.snapPoints ?? [],
          snapRadius: a.snapRadius,
          color: a.color,
          className: a.class,
          label: a.label
        };
      })
      .sort((a, b) => a.from - b.from);
  });

  // Effective min & max
  let effectiveMin = $derived.by(() => {
    if (min !== undefined) return min;
    if (normalizedAreas.length > 0) {
      return normalizedAreas[0].from;
    }
    if (normalizedSteps.length > 0) {
      return Math.min(...normalizedSteps.map((s) => s.value));
    }
    return 0;
  });

  let effectiveMax = $derived.by(() => {
    if (max !== undefined) return max;
    if (normalizedAreas.length > 0) {
      return normalizedAreas[normalizedAreas.length - 1].to;
    }
    if (normalizedSteps.length > 0) {
      return Math.max(...normalizedSteps.map((s) => s.value));
    }
    return 100;
  });

  // Magnetic snap points (boundaries and explicit points)
  let allMagneticSnapPoints = $derived.by<number[]>(() => {
    const pts = new Set<number>();

    if (snapPoints && snapPoints.length > 0) {
      for (const p of snapPoints) {
        pts.add(p);
      }
    }

    if (normalizedAreas.length > 0) {
      for (let i = 0; i < normalizedAreas.length; i++) {
        const area = normalizedAreas[i];
        if (area.snapPoints) {
          for (const sp of area.snapPoints) {
            pts.add(sp);
          }
        }
        if (area.snapBoundary) {
          // Snap at internal boundaries
          if (i > 0 && area.from > effectiveMin && area.from < effectiveMax) {
            pts.add(area.from);
          }
          if (i < normalizedAreas.length - 1 && area.to > effectiveMin && area.to < effectiveMax) {
            pts.add(area.to);
          }
        }
      }
    }

    return Array.from(pts);
  });

  let effectiveSnapRadius = $derived.by(() => {
    if (snapRadius !== undefined && snapRadius > 0) return snapRadius;
    const range = effectiveMax - effectiveMin;
    if (range <= 0) return 0;
    return range * 0.025; // 2.5% of range
  });

  let hasSteps = $derived(normalizedSteps.length > 0);

  // Effective minTickGap (0 means natural proportional layout)
  let effectiveMinTickGap = $derived(minTickGap > 0 ? minTickGap : minimumTickGap);

  // Snapping behavior:
  // If smooth is explicitly true, no snapping.
  // If snap is explicitly provided, use that.
  // If areas are defined, delegate to area type.
  // Otherwise, snap if custom steps/ticks are defined, or uniform step has <= 30 divisions.
  let isSnapActive = $derived.by(() => {
    if (smooth) return false;
    if (snap !== undefined) return snap;
    if (normalizedAreas.length > 0) {
      const curArea = normalizedAreas.find((a) => value >= a.from && value <= a.to);
      if (curArea) return curArea.type === 'steps';
    }
    if (hasSteps || (ticks && ticks.length > 0)) return true;
    if (step !== undefined && step > 0) {
      const intervals = (effectiveMax - effectiveMin) / step;
      return intervals <= 30;
    }
    return false;
  });

  // Drag stepping easing:
  // When in stepped mode, ease to each step as cursor moves.
  // When in smooth mode (or fine continuous drag), track pointer instantly (0s duration) so it glides smoothly.
  let effectiveEaseOnDrag = $derived.by(() => {
    if (easeOnDrag !== undefined) return easeOnDrag;
    if (normalizedAreas.length > 0) {
      const curArea = normalizedAreas.find((a) => value >= a.from && value <= a.to);
      if (curArea?.type === 'sliding') return false;
      if (curArea?.type === 'steps') return true;
    }
    if (smooth || !isSnapActive) return false;
    return true;
  });

  // Display conditions
  let showTickPips = $derived(
    !hideStepMarkers &&
    (showStepMarkers !== undefined
      ? showStepMarkers
      : hasSteps || (ticks && ticks.length > 0))
  );

  let showStepsRow = $derived(
    (showStepLabels !== undefined
      ? showStepLabels
      : hasSteps && normalizedSteps.some((s) => Boolean(s.label || s.icon))) &&
    normalizedSteps.length > 0
  );

  // ==========================================
  // Easing & Animation Settings Duration
  // ==========================================
  let activeAnimDurationMs = $derived(
    !animateTransitions || animationStore.isOff
      ? 0
      : (animationDurationMs !== undefined ? animationDurationMs : animationStore.durationMs)
  );

  // ==========================================
  // State & Metrics
  // ==========================================
  let isHovered = $state(false);
  let isPointerDown = $state(false);
  let isDragging = $state(false);
  let startPointerX = 0;
  let startPointerY = 0;

  let trackElement: HTMLDivElement | null = $state(null);
  let containerWidth = $state(0);
  let trackLength = $state(0);
  let labelSizes = $state<Record<number, number>>({});
  let labelCrossSizes = $state<Record<number, number>>({});
  const vertical = $derived(orientation === 'vertical');
  const primaryBefore = $derived(vertical ? labelPosition === 'left' : labelPosition === 'top');

  function measureTrack(node: HTMLElement) {
    const observer = new ResizeObserver(() => {
      trackLength = vertical ? node.clientHeight : node.clientWidth;
    });
    observer.observe(node);
    return { destroy: () => observer.disconnect() };
  }

  function measureLabel(node: HTMLElement, index: number) {
    const observer = new ResizeObserver(() => {
      labelSizes = { ...labelSizes, [index]: vertical ? node.offsetHeight : node.offsetWidth };
      labelCrossSizes = { ...labelCrossSizes, [index]: vertical ? node.offsetWidth : node.offsetHeight };
    });
    observer.observe(node);
    return { destroy: () => observer.disconnect() };
  }

  let oppositeLabels = $derived.by(() => {
    const opposite = new Set<number>();
    if (!staggerLabels) return opposite;
    let previousEnd = -Infinity;
    let previousOpposite = false;
    const sorted = normalizedSteps.map((s, i) => ({ s, i })).sort((a, b) => a.s.value - b.s.value);
    for (const { s, i } of sorted) {
      const position = getPercent(s.value) / 100 * trackLength;
      const size = labelSizes[i] ?? 0;
      const start = alignLabels !== 'center' && i === 0 ? position
        : alignLabels !== 'center' && i === normalizedSteps.length - 1 ? position - size
        : position - size / 2;
      const moveOpposite = start < previousEnd + 4 && !previousOpposite;
      if (moveOpposite) opposite.add(i);
      previousOpposite = moveOpposite;
      previousEnd = start + size;
    }
    return opposite;
  });

  // Icons and text need more room than a text-only row. Reserve the measured
  // cross-axis size on each occupied side so labels cannot overlap the rail.
  let labelSpace = $derived.by(() => {
    const space = { before: 0, after: 0 };
    if (!showStepsRow) return space;
    normalizedSteps.forEach((s, i) => {
      const side = primaryBefore !== oppositeLabels.has(i) ? 'before' : 'after';
      const fallback = vertical ? 44 : showStepIcons && s.icon ? 36 : 20;
      space[side] = Math.max(space[side], (labelCrossSizes[i] ?? fallback) + 4);
    });
    return space;
  });
  let useCompactLabels = $derived.by(() => {
    if (!compactLabels || containerWidth <= 0) return false;
    const positions = normalizedSteps.map(s => getPercent(s.value)).sort((a, b) => a - b);
    return positions.some((position, i) => i > 0 &&
      (position - positions[i - 1]) / 100 * Math.max(0, trackLength) <
        (Math.max(minTickGap, minimumTickGap) || 48));
  });
  let hasMeasured = $state(false);

  // Settle width-dependent tick spacing before enabling transitions on mount.
  $effect(() => {
    if (containerWidth <= 0 || hasMeasured) return;
    const frame = requestAnimationFrame(() => { hasMeasured = true; });
    return () => cancelAnimationFrame(frame);
  });
  let tooltipElement: HTMLDivElement | null = $state(null);
  let tooltipWidth = $state(44);

  // ==========================================
  // Visual Tick Layout & Gap Enforcement (matches WPF MinimumTickGap)
  // ==========================================
  function buildVisualTickPositions(tickValues: number[], trackLength: number, minGap: number): number[] {
    const count = tickValues.length;
    if (count <= 1) return [0];
    if (trackLength <= 0) {
      return tickValues.map((_, i) => (i / (count - 1)) * 100);
    }

    const minVal = tickValues[0];
    const maxVal = tickValues[count - 1];
    const range = maxVal - minVal;
    if (range <= 0) {
      return tickValues.map((_, i) => (i / (count - 1)) * 100);
    }

    const rawGaps: number[] = [];
    for (let i = 0; i < count - 1; i++) {
      const norm = (tickValues[i + 1] - tickValues[i]) / range;
      rawGaps.push(norm * trackLength);
    }

    const maxMinGap = trackLength / (count - 1);
    const effectiveGap = Math.max(0, Math.min(minGap, maxMinGap));
    const minRequired = effectiveGap * (count - 1);
    const slack = trackLength - minRequired;

    let weightSum = 0;
    const weights: number[] = [];
    for (let i = 0; i < count - 1; i++) {
      const w = Math.max(0, rawGaps[i] - effectiveGap);
      weights.push(w);
      weightSum += w;
    }

    const positions: number[] = [0];
    for (let i = 0; i < count - 1; i++) {
      const extra = weightSum <= 0 ? slack / (count - 1) : slack * (weights[i] / weightSum);
      positions.push(positions[i] + effectiveGap + extra);
    }

    return positions.map((p) => Math.max(0, Math.min(100, (p / trackLength) * 100)));
  }

  let visualTickPositions = $derived.by<number[] | null>(() => {
    if (proportionalSpacing || effectiveMinTickGap <= 0 || normalizedSteps.length <= 1) return null;
    const trackLen = Math.max(1, trackLength);
    const sorted = [...normalizedSteps].sort((a, b) => a.value - b.value).map((s) => s.value);
    return buildVisualTickPositions(sorted, trackLen, effectiveMinTickGap);
  });

  function mapActualToVisual(actual: number): number {
    if (!visualTickPositions || normalizedSteps.length <= 1) {
      return effectiveMax > effectiveMin ? Math.max(0, Math.min(100, ((actual - effectiveMin) / (effectiveMax - effectiveMin)) * 100)) : 0;
    }
    const tickVals = normalizedSteps.map((s) => s.value);
    if (actual <= tickVals[0]) return visualTickPositions[0];
    if (actual >= tickVals[tickVals.length - 1]) return visualTickPositions[visualTickPositions.length - 1];

    let lower = 0;
    for (let i = 0; i < tickVals.length - 1; i++) {
      if (actual <= tickVals[i + 1]) {
        lower = i;
        break;
      }
    }
    const upper = Math.min(lower + 1, tickVals.length - 1);
    const span = tickVals[upper] - tickVals[lower];
    const t = span <= 0 ? 0 : (actual - tickVals[lower]) / span;
    return visualTickPositions[lower] + (visualTickPositions[upper] - visualTickPositions[lower]) * t;
  }

  function mapVisualToActual(visualPercent: number): number {
    if (!visualTickPositions || normalizedSteps.length <= 1) {
      return effectiveMin + (visualPercent / 100) * (effectiveMax - effectiveMin);
    }
    const tickVals = normalizedSteps.map((s) => s.value);
    if (visualPercent <= visualTickPositions[0]) return tickVals[0];
    if (visualPercent >= visualTickPositions[visualTickPositions.length - 1]) return tickVals[tickVals.length - 1];

    let lower = 0;
    for (let i = 0; i < visualTickPositions.length - 1; i++) {
      if (visualPercent <= visualTickPositions[i + 1]) {
        lower = i;
        break;
      }
    }
    const upper = Math.min(lower + 1, visualTickPositions.length - 1);
    const span = visualTickPositions[upper] - visualTickPositions[lower];
    const t = span <= 0 ? 0 : (visualPercent - visualTickPositions[lower]) / span;
    return tickVals[lower] + (tickVals[upper] - tickVals[lower]) * t;
  }

  // Progress percentage calculation
  let progressPercent = $derived(mapActualToVisual(value));

  function getPercent(val: number): number {
    return mapActualToVisual(val);
  }

  function isStepActive(stepVal: number): boolean {
    return Math.abs(value - stepVal) < 0.0001;
  }

  // Area boundaries & visual segments
  let areaBoundaries = $derived.by<number[]>(() => {
    if (normalizedAreas.length <= 1) return [];
    const b: number[] = [];
    for (let i = 0; i < normalizedAreas.length - 1; i++) {
      const boundaryVal = normalizedAreas[i].to;
      if (boundaryVal > effectiveMin && boundaryVal < effectiveMax) {
        b.push(boundaryVal);
      }
    }
    return b;
  });

  let hasCustomAreaColors = $derived(
    normalizedAreas.some((a) => Boolean(a.color))
  );

  let areaSegments = $derived.by(() => {
    if (normalizedAreas.length <= 1 || !hasCustomAreaColors) return null;
    return normalizedAreas.map((area) => {
      const startPct = getPercent(area.from);
      const endPct = getPercent(area.to);
      const areaWidthPct = Math.max(0, endPct - startPct);

      let filledPct = 0;
      if (value > area.from) {
        const currentWithinArea = Math.min(value, area.to);
        const fillEndPct = getPercent(currentWithinArea);
        filledPct = Math.max(0, fillEndPct - startPct);
      }

      return {
        from: area.from,
        to: area.to,
        startPct,
        widthPct: areaWidthPct,
        filledPct,
        color: area.color || 'var(--accent-color, #1DB954)',
        className: area.className
      };
    });
  });

  let currentActiveArea = $derived.by(() => {
    if (normalizedAreas.length === 0) return null;
    return (
      normalizedAreas.find((a) => value >= a.from && value <= a.to) ??
      (value > normalizedAreas[normalizedAreas.length - 1].to
        ? normalizedAreas[normalizedAreas.length - 1]
        : normalizedAreas[0])
    );
  });

  let thumbDotColor = $derived(
    currentActiveArea?.color || 'var(--accent-color, #1DB954)'
  );

  let shouldShowAreaDividers = $derived(
    showAreaDividers !== undefined ? showAreaDividers : normalizedAreas.length > 1
  );

  // Snapping logic
  function snapValue(raw: number): number {
    const clamped = Math.max(effectiveMin, Math.min(effectiveMax, raw));

    // 1. Magnetic snap points (boundaries and explicit points)
    if (allMagneticSnapPoints.length > 0) {
      let closestPt: number | null = null;
      let minDistance = Infinity;

      for (const pt of allMagneticSnapPoints) {
        const dist = Math.abs(clamped - pt);
        const radius = snapRadius ?? effectiveSnapRadius;
        if (dist <= radius && dist < minDistance) {
          minDistance = dist;
          closestPt = pt;
        }
      }

      if (closestPt !== null) {
        return closestPt;
      }
    }

    // 2. Area-based snapping ('sliding' vs 'steps')
    if (normalizedAreas.length > 0) {
      let matched = normalizedAreas.find((a) => clamped >= a.from && clamped <= a.to);
      if (!matched) {
        if (clamped <= normalizedAreas[0].from) {
          matched = normalizedAreas[0];
        } else {
          matched = normalizedAreas[normalizedAreas.length - 1];
        }
      }

      if (matched.type === 'steps') {
        if (matched.steps && matched.steps.length > 0) {
          let closest = matched.steps[0].value;
          let minDiff = Math.abs(clamped - closest);
          for (let i = 1; i < matched.steps.length; i++) {
            const diff = Math.abs(clamped - matched.steps[i].value);
            if (diff < minDiff) {
              minDiff = diff;
              closest = matched.steps[i].value;
            }
          }
          return closest;
        }

        const areaStep = matched.step ?? step ?? 1;
        const count = Math.round((clamped - matched.from) / areaStep);
        const snapped = matched.from + count * areaStep;
        const decimals = (areaStep.toString().split('.')[1] || '').length;
        return Math.max(matched.from, Math.min(matched.to, parseFloat(snapped.toFixed(decimals))));
      } else {
        // 'sliding' continuous mode
        const fineStep = matched.step ?? step;
        if (fineStep && fineStep > 0) {
          const decimals = (fineStep.toString().split('.')[1] || '').length;
          if (decimals > 0) {
            return Math.max(matched.from, Math.min(matched.to, parseFloat(clamped.toFixed(decimals))));
          }
        }
        return clamped;
      }
    }

    // 3. Fallback when no areas defined
    if (!isSnapActive) {
      if (step && step > 0) {
        const decimals = (step.toString().split('.')[1] || '').length;
        if (decimals > 0) {
          return Math.max(effectiveMin, Math.min(effectiveMax, parseFloat(clamped.toFixed(decimals))));
        }
      }
      return clamped;
    }

    if (normalizedSteps.length > 0) {
      let closest = normalizedSteps[0].value;
      let minDiff = Math.abs(clamped - closest);
      for (let i = 1; i < normalizedSteps.length; i++) {
        const diff = Math.abs(clamped - normalizedSteps[i].value);
        if (diff < minDiff) {
          minDiff = diff;
          closest = normalizedSteps[i].value;
        }
      }
      return closest;
    }

    if (step && step > 0) {
      const stepsCount = Math.round((clamped - effectiveMin) / step);
      const snapped = effectiveMin + stepsCount * step;
      const decimals = (step.toString().split('.')[1] || '').length;
      return Math.max(effectiveMin, Math.min(effectiveMax, parseFloat(snapped.toFixed(decimals))));
    }

    return clamped;
  }

  // ==========================================
  // Pointer & Value Handlers
  // ==========================================
  function resolveValueFromClientX(clientX: number): number {
    if (!trackElement) return value;
    const rect = trackElement.getBoundingClientRect();
    const length = vertical ? rect.height : rect.width;
    const offset = vertical ? rect.bottom - clientX : clientX - rect.left;
    const ratio = length > 0 ? Math.max(0, Math.min(1, offset / length)) : 0;
    const raw = mapVisualToActual(ratio * 100);
    return snapValue(raw);
  }
  let currentStep = $derived(
    normalizedSteps.find((s) => Math.abs(s.value - value) < 0.0001)
  );

  let resolvedTooltipText = $derived.by(() => {
    if (formatTooltip) {
      return formatTooltip(value, currentStep);
    }
    if (currentStep?.label) {
      return currentStep.label;
    }
    if (resolvedOptions.length > 0) {
      const idx = Math.round(value - effectiveMin);
      if (idx >= 0 && idx < resolvedOptions.length) {
        return resolvedOptions[idx];
      }
    }
    if (thumbToolTipFallback) {
      return thumbToolTipFallback.replace('{0}', String(value));
    }
    if (formatValue) {
      return formatValue(value);
    }
    return String(value);
  });

  let isWheelActive = $derived(Boolean(enableWheel || scrollable || enableScroll));
  let isWheelScrubbing = $state(false);
  let wheelTimeout: any = null;

  let isTooltipVisible = $derived(
    showTooltip &&
    !disabled &&
    (tooltipOnDragOnly
      ? (isPointerDown || isDragging || isWheelScrubbing)
      : (isHovered || isPointerDown || isDragging || isWheelScrubbing))
  );

  // Clamped tooltip popup position so it never overflows edges
  let clampedTooltipLeft = $derived.by(() => {
    if (!containerWidth || containerWidth <= 0) return 0;
    const halfW = (tooltipWidth || 44) / 2;
    const trackInset = 10;
    const effectiveTrackWidth = Math.max(1, containerWidth - trackInset * 2);
    const thumbX = trackInset + (progressPercent / 100) * effectiveTrackWidth;
    return Math.max(halfW, Math.min(containerWidth - halfW, thumbX));
  });

  let sliderRootElement: HTMLDivElement | null = $state(null);
  let fixedTooltipTop = $state(0);
  let fixedTooltipLeft = $state(0);
  let isFlippedBelow = $state(false);

  function updateFixedTooltipCoords() {
    if (!sliderRootElement) return;
    const rect = sliderRootElement.getBoundingClientRect();
    const halfW = (tooltipWidth || 44) / 2;
    const trackInset = 10;
    const effectiveTrackWidth = Math.max(1, (containerWidth || rect.width) - trackInset * 2);
    const thumbX = trackInset + (progressPercent / 100) * effectiveTrackWidth;

    let targetLeft = vertical ? rect.right + halfW + 8 : rect.left + thumbX;
    let targetTop = vertical
      ? rect.bottom - 10 - (progressPercent / 100) * Math.max(1, rect.height - 20)
      : rect.top - 8;
    isFlippedBelow = false;

    // Flip below if approaching top of viewport
    if (targetTop < 36) {
      targetTop = rect.bottom + 8;
      isFlippedBelow = true;
    }

    // Clamp horizontally to stay inside viewport
    if (targetLeft - halfW < 8) {
      targetLeft = halfW + 8;
    } else if (targetLeft + halfW > window.innerWidth - 8) {
      targetLeft = window.innerWidth - halfW - 8;
    }

    fixedTooltipTop = targetTop;
    fixedTooltipLeft = targetLeft;
  }

  $effect(() => {
    if (isTooltipVisible) {
      progressPercent;
      orientation;
      updateFixedTooltipCoords();
      const onScroll = () => updateFixedTooltipCoords();
      const onResize = () => updateFixedTooltipCoords();
      window.addEventListener('scroll', onScroll, true);
      window.addEventListener('resize', onResize);
      return () => {
        window.removeEventListener('scroll', onScroll, true);
        window.removeEventListener('resize', onResize);
      };
    }
  });


  function commitValue(newVal: number, fireChange: boolean = false) {
    if (newVal === value) return;
    value = newVal;
    oninput?.(value);
    onseek?.(value);
    if (fireChange) {
      onchange?.(value);
    }
  }

  function handlePointerDown(e: PointerEvent) {
    if (disabled || e.button !== 0) return;
    (e.currentTarget as HTMLElement)?.setPointerCapture?.(e.pointerId);

    isPointerDown = true;
    isDragging = false;
    startPointerX = e.clientX;
    startPointerY = e.clientY;

    const targetValue = resolveValueFromClientX(vertical ? e.clientY : e.clientX);
    commitValue(targetValue, false);
  }

  function handlePointerMove(e: PointerEvent) {
    if (!isPointerDown) return;

    if (!isDragging) {
      const dist = Math.hypot(e.clientX - startPointerX, e.clientY - startPointerY);
      if (dist > 3) {
        isDragging = true;
      }
    }

    if (isDragging) {
      const targetValue = resolveValueFromClientX(vertical ? e.clientY : e.clientX);
      commitValue(targetValue, false);
    }
  }

  function handlePointerUp(e: PointerEvent) {
    if (!isPointerDown) return;
    try {
      (e.currentTarget as HTMLElement)?.releasePointerCapture?.(e.pointerId);
    } catch {}

    isPointerDown = false;
    isDragging = false;
    onchange?.(value);
  }

  function handleLostPointerCapture() {
    isPointerDown = false;
    isDragging = false;
  }

  function selectStep(stepVal: number) {
    if (disabled) return;
    commitValue(stepVal, true);
  }

  // Keyboard navigation
  function handleKeyDown(e: KeyboardEvent) {
    if (disabled) return;

    let delta = 0;
    if (e.key === 'ArrowRight' || e.key === 'ArrowUp') {
      delta = 1;
    } else if (e.key === 'ArrowLeft' || e.key === 'ArrowDown') {
      delta = -1;
    } else if (e.key === 'PageUp') {
      delta = 5;
    } else if (e.key === 'PageDown') {
      delta = -5;
    } else if (e.key === 'Home') {
      e.preventDefault();
      commitValue(effectiveMin, true);
      return;
    } else if (e.key === 'End') {
      e.preventDefault();
      commitValue(effectiveMax, true);
      return;
    } else {
      return;
    }

    e.preventDefault();

    if (normalizedSteps.length > 0) {
      const curIdx = normalizedSteps.findIndex((s) => Math.abs(s.value - value) < 0.001);
      const targetIdx =
        curIdx >= 0
          ? Math.max(0, Math.min(normalizedSteps.length - 1, curIdx + delta))
          : delta > 0
          ? 0
          : normalizedSteps.length - 1;
      commitValue(normalizedSteps[targetIdx].value, true);
    } else {
      let stepSize = step && step > 0 ? step : (effectiveMax - effectiveMin) / 100;
      if (normalizedAreas.length > 0) {
        const curArea = normalizedAreas.find((a) => value >= a.from && value <= a.to);
        if (curArea?.step && curArea.step > 0) {
          stepSize = curArea.step;
        }
      }
      const targetVal = snapValue(value + delta * stepSize);
      commitValue(targetVal, true);
    }
  }

  // Mouse wheel scrolling
  function handleWheel(e: WheelEvent) {
    if (disabled || !isWheelActive) return;

    // Prevent background page / container scrolling while hovering active scrollable slider
    e.preventDefault();
    e.stopPropagation();

    // Invert deltaY: scrolling up (deltaY < 0) increases value, scrolling down decreases value
    const direction = e.deltaY < 0 ? 1 : e.deltaY > 0 ? -1 : 0;
    if (direction === 0) return;

    isWheelScrubbing = true;
    if (wheelTimeout) clearTimeout(wheelTimeout);
    wheelTimeout = setTimeout(() => {
      isWheelScrubbing = false;
    }, 750);

    if (normalizedSteps.length > 0) {
      const curIdx = normalizedSteps.findIndex((s) => Math.abs(s.value - value) < 0.001);
      const targetIdx =
        curIdx >= 0
          ? Math.max(0, Math.min(normalizedSteps.length - 1, curIdx + direction))
          : direction > 0
          ? 0
          : normalizedSteps.length - 1;
      commitValue(normalizedSteps[targetIdx].value, true);
    } else {
      let increment = wheelStep;
      if (!increment || increment <= 0) {
        if (normalizedAreas.length > 0) {
          const curArea = normalizedAreas.find((a) => value >= a.from && value <= a.to);
          if (curArea?.step && curArea.step > 0 && curArea.type === 'steps') {
            increment = curArea.step;
          }
        }
        if (!increment || increment <= 0) {
          if (step && step > 0 && isSnapActive) {
            increment = step;
          } else {
            const range = effectiveMax - effectiveMin;
            const fivePercent = range * 0.05;
            increment = step && step > fivePercent ? step : fivePercent;
          }
        }
      }

      const targetVal = snapValue(value + direction * increment);
      commitValue(targetVal, true);
    }
  }
</script>

<div
  bind:this={sliderRootElement}
  class="amp-slider-root {className}"
  class:is-seekbar={variant === 'seekbar'}
  class:is-disabled={disabled}
  class:has-steps-row={showStepsRow}
  class:is-vertical={vertical}
  class:labels-before={primaryBefore}
  class:labels-staggered={staggerLabels && oppositeLabels.size > 0}
  {title}
  {id}
  style="--slider-duration: {(!hasMeasured || (!effectiveEaseOnDrag && isDragging)) ? 0 : activeAnimDurationMs}ms; --slider-label-before-space: {labelSpace.before}px; --slider-label-after-space: {labelSpace.after}px; {style}"
  bind:clientWidth={containerWidth}
  onwheel={handleWheel}
>
  <div class="slider-main-row">
    <!-- Interactive Track & Thumb Area -->
    <div
      class="slider-interactive-area"
      class:is-pointer-down={isPointerDown}
      class:is-dragging={isDragging}
      class:is-hovered={isHovered}
      role="slider"
      tabindex={disabled ? -1 : 0}
      aria-label={ariaLabel || title || undefined}
      aria-orientation={orientation}
      aria-valuemin={effectiveMin}
      aria-valuemax={effectiveMax}
      aria-valuenow={value}
      aria-valuetext={currentStep?.label || formatValue(value)}
      aria-disabled={disabled}
      onpointerenter={() => { if (!disabled) isHovered = true; }}
      onpointerleave={() => { isHovered = false; }}
      onpointerdown={handlePointerDown}
      onpointermove={handlePointerMove}
      onpointerup={handlePointerUp}
      onlostpointercapture={handleLostPointerCapture}
      onkeydown={handleKeyDown}
    >
      <!-- 20px invisible hit area for comfortable clicks matching WPF -->
      <div class="slider-hit-area"></div>

      <!-- Main Track Groove -->
      <div class="slider-track" style="--slider-thumb-position: {progressPercent}%;" bind:this={trackElement} use:measureTrack>
        <div class="slider-rail">
          <!-- Area Boundary Divider Notches -->
          {#if shouldShowAreaDividers && areaBoundaries.length > 0}
            <div class="area-dividers-layer">
              {#each areaBoundaries as boundary}
                <div
                  class="area-divider"
                  class:is-reached={value >= boundary}
                  style={vertical ? `bottom: ${getPercent(boundary)}%;` : `left: ${getPercent(boundary)}%;`}
                ></div>
              {/each}
            </div>
          {/if}

          <!-- Step Markers (Tick Pips) on Track -->
          {#if showTickPips}
            <div class="step-markers-layer">
              {#each normalizedSteps as s (s.value)}
                {@const pipPercent = getPercent(s.value)}
                {@const isFilled = s.value <= value}
                <div
                  class="step-pip"
                  class:is-filled={isFilled}
                  class:is-active={isStepActive(s.value)}
                  style={vertical ? `bottom: ${pipPercent}%;` : `left: ${pipPercent}%;`}
                ></div>
              {/each}
            </div>
          {/if}

          <div class="slider-groove">
            <!-- Filled Track Bar(s) (supports custom area colors and transitions) -->
            {#if areaSegments}
              {#each areaSegments as seg (seg.from)}
                <!-- Keep empty segments mounted so mute/unmute can animate through zero. -->
                <div
                  class="slider-fill {seg.className || ''}"
                  style={`${vertical ? `bottom: ${seg.startPct}%; height: ${seg.filledPct}%;` : `left: ${seg.startPct}%; width: ${seg.filledPct}%;`} background-color: ${seg.color};`}
                ></div>
              {/each}
            {:else}
              <div class="slider-fill" style={vertical ? `height: ${progressPercent}%;` : `width: ${progressPercent}%;`}></div>
            {/if}
          </div>
        </div>

        <!-- The rail has a transparent cutout around the accent dot. -->
        <div class="slider-thumb" style={vertical ? `bottom: ${progressPercent}%;` : `left: ${progressPercent}%;`}>
          <div class="slider-thumb-dot" style="background: {thumbDotColor};"></div>
        </div>
      </div>

      <!-- Floating Drag & Hover Tooltip Popup (portaled to document.body with max z-index to escape container overflow) -->
      {#if showTooltip && !disabled}
        <div
          use:portal
          class="slider-tooltip"
          class:is-visible={isTooltipVisible}
          class:is-flipped={isFlippedBelow}
          style="top: {fixedTooltipTop}px; left: {fixedTooltipLeft}px;"
          bind:this={tooltipElement}
          bind:clientWidth={tooltipWidth}
        >
          <span class="tooltip-text">{resolvedTooltipText}</span>
        </div>
      {/if}
    </div>

    <!-- Optional Value Display on the Right -->
    {#if showValue}
      <span class="value-display">{formatValue(value)}</span>
    {/if}
  </div>

  <!-- Step Labels & Icons Row below the Track -->
  {#if showStepsRow}
    <div class="slider-steps-track">
      {#each normalizedSteps as s, i (s.value)}
        {@const stepPercent = getPercent(s.value)}
        {@const isActive = isStepActive(s.value)}
        <button
          type="button"
          class="step-item"
          use:measureLabel={i}
          class:label-before={primaryBefore !== oppositeLabels.has(i)}
          class:is-active={isActive}
          class:is-first={i === 0}
          class:is-last={i === normalizedSteps.length - 1}
          class:align-edges={alignLabels !== 'center'}
          style={vertical ? `bottom: ${stepPercent}%;` : `left: ${stepPercent}%;`}
          onclick={() => selectStep(s.value)}
          title={s.title || s.label}
          disabled={disabled || s.disabled}
          tabindex="-1"
        >
          {#if showStepIcons && s.icon}
            <div class="step-icon">
              <FluentIcon name={s.icon} size={15} active={isActive} />
            </div>
          {/if}
          {#if showStepText && s.label}
            <span class="step-label">{useCompactLabels ? (compactLabels?.[i] ?? s.label) : s.label}</span>
          {/if}
        </button>
      {/each}
    </div>
  {/if}
</div>

<style>
  .amp-slider-root {
    display: flex;
    flex-direction: column;
    width: 100%;
    user-select: none;
    box-sizing: border-box;
    position: relative;
  }

  .amp-slider-root.is-disabled {
    opacity: 0.45;
    cursor: not-allowed;
    pointer-events: none;
  }

  .slider-main-row {
    display: flex;
    align-items: center;
    gap: 12px;
    width: 100%;
    position: relative;
  }

  /* Interactive Track Container */
  .slider-interactive-area {
    position: relative;
    width: 100%;
    height: 24px;
    min-height: 24px;
    display: flex;
    align-items: center;
    cursor: pointer;
    outline: none;
    touch-action: none;
  }

  .slider-interactive-area:focus-visible {
    outline: 2px solid var(--accent-color, #1DB954);
    outline-offset: 2px;
    border-radius: 4px;
  }

  /* 20px hit area to match WPF */
  .slider-hit-area {
    position: absolute;
    top: 0;
    bottom: 0;
    left: 0;
    right: 0;
    background: transparent;
    pointer-events: none;
  }

  @property --slider-thumb-position {
    syntax: '<percentage>';
    inherits: true;
    initial-value: 0%;
  }

  @property --slider-cutout-radius {
    syntax: '<length>';
    inherits: false;
    initial-value: 9px;
  }

  /* Keep the cutout and thumb on the same timing when the value changes. */
  .slider-track {
    position: relative;
    margin: 0 10px;
    width: calc(100% - 20px);
    height: 4px;
    border-radius: 2px;
    overflow: visible;
    pointer-events: none;
    transition: --slider-thumb-position var(--slider-duration, 200ms) cubic-bezier(0.1, 0.9, 0.2, 1);
  }

  /* Mask the rail and its markers, leaving the actual surface visible. */
  .slider-rail {
    position: absolute;
    inset: -10px 0;
    mask-image: radial-gradient(circle at var(--slider-thumb-position) 50%, transparent calc(var(--slider-cutout-radius) - 0.5px), #000 var(--slider-cutout-radius));
    transition: --slider-cutout-radius var(--transition-fast, 0.15s ease);
  }

  .slider-interactive-area:is(.is-pointer-down, .is-dragging) .slider-rail {
    --slider-cutout-radius: 9.72px;
  }

  .slider-groove {
    position: absolute;
    inset: 10px 0;
    border-radius: 2px;
    background: var(--track-groove-bg, rgba(255, 255, 255, 0.12));
  }

  :global(:root.light) .slider-groove,
  :global(:root[data-theme="light"]) .slider-groove {
    background: rgba(0, 0, 0, 0.12);
  }

  /* Step Markers (Tick Pips) */
  .step-markers-layer {
    position: absolute;
    top: 0;
    left: 0;
    right: 0;
    bottom: 0;
    pointer-events: none;
  }

  .step-pip {
    position: absolute;
    top: 50%;
    width: 4px;
    height: 4px;
    border-radius: 50%;
    transform: translate(-50%, -50%);
    background: rgba(255, 255, 255, 0.35);
    z-index: 1;
    transition: background 0.15s ease, transform 0.15s ease;
  }

  :global(:root.light) .step-pip,
  :global(:root[data-theme="light"]) .step-pip {
    background: rgba(0, 0, 0, 0.25);
  }

  .step-pip.is-filled {
    background: rgba(0, 0, 0, 0.45);
  }

  :global(:root.light) .step-pip.is-filled,
  :global(:root[data-theme="light"]) .step-pip.is-filled {
    background: rgba(255, 255, 255, 0.7);
  }

  /* Area Dividers / Notches */
  .area-dividers-layer {
    position: absolute;
    top: 0;
    left: 0;
    right: 0;
    bottom: 0;
    pointer-events: none;
    z-index: 2;
  }

  .area-divider {
    position: absolute;
    top: 50%;
    width: 2px;
    height: 8px;
    border-radius: 1px;
    transform: translate(-50%, -50%);
    background: rgba(255, 255, 255, 0.4);
    transition: background 0.15s ease, height 0.15s ease;
  }

  :global(:root.light) .area-divider,
  :global(:root[data-theme="light"]) .area-divider {
    background: rgba(0, 0, 0, 0.28);
  }

  .area-divider.is-reached {
    background: rgba(255, 255, 255, 0.85);
  }

  :global(:root.light) .area-divider.is-reached,
  :global(:root[data-theme="light"]) .area-divider.is-reached {
    background: rgba(0, 0, 0, 0.7);
  }

  /* Filled Progress Track */
  .slider-fill {
    position: absolute;
    left: 0;
    top: 0;
    bottom: 0;
    height: 4px;
    border-radius: 2px;
    background-color: var(--accent-color, #1DB954);
    pointer-events: none;
    z-index: 1;
    transition: width var(--slider-duration, 200ms) cubic-bezier(0.1, 0.9, 0.2, 1);
  }

  /* Transparent thumb wrapper; only the accent dot paints a surface. */
  .slider-thumb {
    position: absolute;
    top: 50%;
    width: 18px;
    height: 18px;
    border-radius: 50%;
    border: none;
    box-shadow: none;
    transform: translate(-50%, -50%);
    display: flex;
    align-items: center;
    justify-content: center;
    pointer-events: none;
    z-index: 3;
    transition:
      left var(--slider-duration, 200ms) cubic-bezier(0.1, 0.9, 0.2, 1),
      transform var(--transition-fast, 0.15s ease);
  }

  /* Inner Dot */
  .slider-thumb-dot {
    width: 10px;
    height: 10px;
    border-radius: 50%;
    background: var(--accent-color, #1DB954);
    transition: transform var(--transition-fast, 0.15s ease), filter var(--transition-fast, 0.15s ease);
  }

  /* Hover & Active Thumb Scaling */
  .slider-interactive-area:hover .slider-thumb-dot,
  .slider-interactive-area.is-hovered .slider-thumb-dot {
    transform: scale(1.15);
    filter: brightness(1.1);
  }

  .slider-interactive-area.is-pointer-down .slider-thumb,
  .slider-interactive-area.is-dragging .slider-thumb {
    transform: translate(-50%, -50%) scale(1.08);
  }

  .slider-interactive-area.is-pointer-down .slider-thumb-dot,
  .slider-interactive-area.is-dragging .slider-thumb-dot {
    transform: scale(1.25);
    filter: brightness(1.15);
  }

  /* Floating Drag / Hover Tooltip Popup - portaled directly to body to avoid container clipping */
  .slider-tooltip {
    position: fixed;
    z-index: 2147483647;
    padding: 4px 8px;
    border-radius: var(--radius-sm, 6px);
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
    transform: translate(-50%, -100%) scale(0.85);
    transform-origin: bottom center;
    opacity: 0;
    visibility: hidden;
    transition:
      opacity var(--anim-fast, 0.14s) cubic-bezier(0, 0, 0.2, 1),
      transform var(--anim-fast, 0.14s) cubic-bezier(0, 0, 0.2, 1),
      visibility var(--anim-fast, 0.14s);
  }

  .slider-tooltip.is-flipped {
    transform: translate(-50%, 0) scale(0.85);
    transform-origin: top center;
  }

  :global(:root.light) .slider-tooltip,
  :global(:root[data-theme="light"]) .slider-tooltip {
    background: rgba(255, 255, 255, 0.96);
    color: #18181b;
    border: none;
    box-shadow: 0 10px 25px -5px rgba(0, 0, 0, 0.15), 0 8px 10px -6px rgba(0, 0, 0, 0.08);
  }

  .slider-tooltip.is-visible {
    opacity: 1;
    visibility: visible;
    transform: translate(-50%, -100%) scale(1);
  }

  .slider-tooltip.is-visible.is-flipped {
    transform: translate(-50%, 0) scale(1);
  }

  .tooltip-text {
    display: block;
    line-height: 1;
  }

  /* Right-side Inline Value Display */
  .value-display {
    font-size: 12px;
    font-weight: 500;
    color: var(--text-secondary);
    min-width: 32px;
    text-align: right;
    font-variant-numeric: tabular-nums;
    flex-shrink: 0;
  }

  /* Step Labels & Icons Row below the Track */
  .slider-steps-track {
    position: absolute;
    inset: 0 10px;
    pointer-events: none;
  }

  .step-item {
    position: absolute;
    bottom: 0;
    pointer-events: auto;
    transform: translateX(-50%);
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 3px;
    background: none;
    border: none;
    padding: 2px 4px;
    margin: 0;
    cursor: pointer;
    color: var(--text-tertiary, rgba(255, 255, 255, 0.45));
    font-family: inherit;
    font-size: 11px;
    font-weight: 500;
    white-space: nowrap;
    border-radius: var(--radius-sm, 4px);
    transition: color var(--transition-fast, 0.15s ease), opacity var(--transition-fast, 0.15s ease);
    outline: none;
  }

  :global(:root.light) .step-item,
  :global(:root[data-theme="light"]) .step-item {
    color: rgba(0, 0, 0, 0.5);
  }

  .step-item:hover:not(:disabled) {
    color: var(--text-primary, #FFFFFF);
  }

  :global(:root.light) .step-item:hover:not(:disabled),
  :global(:root[data-theme="light"]) .step-item:hover:not(:disabled) {
    color: rgba(0, 0, 0, 0.9);
  }

  .step-item.is-active {
    color: var(--accent-color, #1DB954);
    font-weight: 600;
  }

  .step-item:focus-visible {
    box-shadow: 0 0 0 2px var(--accent-color, #1DB954);
  }

  .step-item.align-edges.is-first {
    transform: translateX(0);
    align-items: flex-start;
  }

  .step-item.align-edges.is-last {
    transform: translateX(-100%);
    align-items: flex-end;
  }

  .step-icon {
    display: inline-flex;
    align-items: center;
    justify-content: center;
    transition: transform 0.15s ease;
  }

  .step-item.is-active .step-icon {
    transform: scale(1.1);
  }

  .step-label {
    line-height: 1.2;
  }

  .is-seekbar {
    --slider-fill-color: var(--text-primary, #FFFFFF);
    --slider-boost-color: var(--text-primary, #FFFFFF);
  }

  .is-seekbar:has(.slider-interactive-area:is(:hover, :focus-visible, .is-pointer-down, .is-dragging)) {
    --slider-fill-color: var(--accent-color, #1DB954);
    --slider-boost-color: var(--warning-color, #F59E0B);
  }

  .is-seekbar .slider-thumb {
    width: 12px;
    height: 12px;
    background: var(--text-primary, #FFFFFF);
    box-shadow: 0 1px 4px rgba(0, 0, 0, 0.4);
    opacity: 0;
    transform: translate(-50%, -50%) scale(0);
    transition: left var(--slider-duration, 200ms) cubic-bezier(0.1, 0.9, 0.2, 1),
                opacity var(--transition-fast, 0.15s ease),
                transform var(--transition-fast, 0.15s ease);
  }

  .is-seekbar .slider-rail {
    mask-image: none;
  }

  .is-seekbar .slider-thumb-dot {
    display: none;
  }

  .is-seekbar .slider-interactive-area:is(:hover, :focus-visible, .is-pointer-down, .is-dragging) .slider-thumb {
    opacity: 1;
    transform: translate(-50%, -50%) scale(1);
  }

  .is-seekbar .slider-fill {
    background-color: var(--slider-fill-color);
    transition: width var(--slider-duration, 200ms) cubic-bezier(0.1, 0.9, 0.2, 1),
                background-color var(--transition-fast, 0.16s ease);
  }

  .has-steps-row { padding-top: var(--slider-label-before-space); padding-bottom: var(--slider-label-after-space); }
  .step-item.label-before { top: 0; bottom: auto; }
  .is-vertical { width: max-content; height: 200px; min-height: 80px; padding: 0 var(--slider-label-after-space) 0 var(--slider-label-before-space); }
  .is-vertical.labels-staggered .step-item { top: auto; }
  .is-vertical.labels-staggered .step-item.label-before { left: 0; }
  .is-vertical.labels-staggered .step-item:not(.label-before) { right: 0; }
  .is-vertical .slider-main-row { height: 100%; width: 24px; flex-direction: column; }
  .is-vertical .slider-interactive-area { height: 100%; width: 24px; justify-content: center; }
  .is-vertical .slider-track { height: calc(100% - 20px); width: 4px; margin: 10px 0; }
  .is-vertical .slider-rail { inset: 0 -10px; mask-image: radial-gradient(circle at 50% calc(100% - var(--slider-thumb-position)), transparent calc(var(--slider-cutout-radius) - 0.5px), #000 var(--slider-cutout-radius)); }
  .is-vertical.is-seekbar .slider-rail { mask-image: none; }
  .is-vertical .slider-groove { inset: 0 10px; }
  .is-vertical .slider-fill { top: auto; width: 4px; transition-property: height; }
  .is-vertical .slider-thumb { top: auto; left: 50%; transform: translate(-50%, 50%); transition-property: bottom, transform; }
  .is-vertical .step-pip { top: auto; left: 50%; transform: translate(-50%, 50%); }
  .is-vertical .area-divider { top: auto; left: 50%; width: 8px; height: 2px; transform: translate(-50%, 50%); }
  .is-vertical .slider-steps-track { inset: 10px 0; }
  .is-vertical .step-item { top: auto; left: auto; right: 0; transform: translateY(50%); }
  .is-vertical .step-item.label-before { left: 0; right: auto; }
  .is-vertical .step-item.align-edges.is-first { transform: translateY(0); }
  .is-vertical .step-item.align-edges.is-last { transform: translateY(100%); }
</style>
