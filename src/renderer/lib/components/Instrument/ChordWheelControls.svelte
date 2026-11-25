<script lang="ts">
  import { onDestroy, onMount, untrack } from 'svelte';
  import type { ChordDefinition, GuitarTimbre, GuitarTimbreMode, InstrumentConfig, KeyboardLayoutConfig } from '../../../../shared/types/games';
  import type { ArrangementState, ChordWheelState } from '../../../../shared/types/ipc';
  import { api } from '../../backend';
  import { playbackStore } from '../../state/playbackStore.svelte';
  import { notificationCenter } from '../../ui/notificationCenter.svelte';
  import {
    backtrackGuitarSlot,
    dispatchedGuitarPositions,
    guitarVoicing,
    resolveGuitarSlotFromPitches,
    wheelAngleDelta,
    type GuitarPosition,
  } from '../../utils/guitarLayout';
  import { tooltip } from '../../actions/tooltip';
  import { t } from '../../i18n';
  import Button from '../Common/Button.svelte';
  import Dropdown from '../Common/Dropdown.svelte';
  import ChordIcon from '../Icons/ChordIcon.svelte';

  let {
    wheel,
    pads,
    instrument,
    fretMode,
    paged,
    mixed,
    needsModeSync,
    physicalMode,
    onsyncmode,
    performing,
    action,
    attacks,
    activePitches,
    activeSlots,
    stringKeys = [],
    onpreview,
    onpreviewpositions,
  }: {
    wheel: NonNullable<KeyboardLayoutConfig['chordWheel']>;
    pads: ChordDefinition[];
    instrument: InstrumentConfig;
    fretMode: boolean;
    paged: boolean;
    mixed: boolean;
    needsModeSync: boolean;
    physicalMode: 'frets' | 'chords' | null;
    onsyncmode: (mode: 'frets' | 'chords') => Promise<void>;
    performing: boolean;
    action: ArrangementState | null;
    attacks: ArrangementState[];
    activePitches: Set<number>;
    activeSlots: ReadonlySet<number>;
    stringKeys?: string[];
    onpreview: (pitches: number[]) => void;
    onpreviewpositions?: (positions: GuitarPosition[], chord: string | null) => void;
  } = $props();

  let wheelState = $state<ChordWheelState>({
    page: null,
    selected: null,
    busy: false,
    error: null,
    timbre: 'clean',
    timbreMode: 'auto',
  });
  let pending = $state(false);
  let loaded = $state(false);
  let revision = 0;
  let lastError: string | null = null;
  let selectedRadialSlot = $state(0);
  let manualRadialSlot = $state<number | null>(null);
  let highlightAngle = $state(0);

  const locked = $derived(!loaded || pending || wheelState.busy || playbackStore.isPlaying);
  const slotCount = $derived(Math.max(1, wheel.positions.length));
  const tuning = $derived(instrument.guitar?.tuning ?? []);
  const fretCount = $derived(instrument.guitar?.fretCount ?? 24);
  const currentMode = $derived<'frets' | 'chords' | 'mixed'>(
    mixed && physicalMode === null && !performing ? 'mixed' : fretMode ? 'frets' : 'chords',
  );
  const mutedTracks = $derived(new Set(
    (playbackStore.activeSong?.tracks ?? [])
      .filter(track => track.isMuted || track.muted)
      .map(track => track.index),
  ));

  const backtrackedGlobalSlot = $derived.by(() => {
    if (!playbackStore.activeSong && !activePitches.size) return null;
    return backtrackGuitarSlot({
      instrument,
      fretMode,
      slotCount,
      activePitches: activePitches.size > 0 ? activePitches : undefined,
      notes: playbackStore.activeSong?.notes,
      currentTimeMs: playbackStore.currentTimeSeconds * 1000,
      keyOffset: playbackStore.activeKeyAdjustment?.keyOffset ?? playbackStore.effectiveKeyOffset ?? 0,
      chordToleranceMs: playbackStore.chordDetectionMilliseconds,
      mutedTracks,
    });
  });

  const authoritativeGlobalSlot = $derived.by(() => {
    if (performing) {
      if (action?.slot != null && (currentMode === 'mixed' || action.mode === currentMode)) {
        return action.slot;
      }
      if (activeSlots.size > 0) {
        return [...activeSlots].at(-1)!;
      }
      if (wheelState.selected != null) {
        return wheelState.selected;
      }
      return backtrackedGlobalSlot;
    }
    if (playbackStore.isPlaying) {
      return backtrackedGlobalSlot;
    }
    if (activePitches.size > 0 && strummedStrings.size === 0 && heldStrings.size === 0 && manualRadialSlot === null) {
      return resolveGuitarSlotFromPitches(instrument, activePitches, fretMode, slotCount);
    }
    return null;
  });

  const authoritativePage = $derived(
    authoritativeGlobalSlot !== null
      ? Math.min(wheel.pageCount - 1, Math.max(0, Math.floor(authoritativeGlobalSlot / slotCount)))
      : null,
  );
  const rawPage = $derived(
    !paged
      ? 0
      : playbackStore.isPlaying
        ? action?.page ?? authoritativePage ?? wheelState.page
        : action?.page ?? wheelState.page,
  );
  const page = $derived(
    rawPage !== null && rawPage >= 0 && rawPage < wheel.pageCount ? rawPage : 0,
  );
  const pageStart = $derived(page * slotCount);
  const visiblePads = $derived(pads.slice(pageStart, pageStart + slotCount));

  let autoSyncKey = '';
  $effect(() => {
    if (!loaded || !paged || pending || wheelState.busy || playbackStore.isPlaying || needsModeSync) return;
    if (mixed && !physicalMode) return;
    const needsDefaultPage = wheelState.page === null || wheelState.page < 0 || wheelState.page >= wheel.pageCount;
    if (!needsDefaultPage) {
      autoSyncKey = '';
      return;
    }
    const key = `${instrument.id}:${fretMode}:${wheel.pageCount}:${wheelState.page}`;
    if (autoSyncKey === key) return;
    autoSyncKey = key;
    void (async () => {
      try {
        if (await api.playback.setChordWheelPage(0, false)) {
          const beforeRead = revision;
          const state = await api.playback.getChordWheelState();
          if (revision === beforeRead) receiveState(state);
        }
      } catch {
        // Ignore transient errors while instrument config is reloading.
      }
    })();
  });

  // Reset manual wedge selection when switching instrument or guitar mode.
  $effect(() => {
    instrument.id;
    fretMode;
    untrack(() => {
      selectedRadialSlot = 0;
      manualRadialSlot = null;
      clearStrumPreview();
      clearHeldStrings();
    });
  });

  // Backtrack correction: when playback starts/resumes or runs, discard any diverging manual wedge override.
  $effect(() => {
    if (playbackStore.isPlaying && manualRadialSlot !== null) {
      manualRadialSlot = null;
    }
  });

  const targetRadialSlot = $derived.by(() => {
    if (!visiblePads.length) return null;
    if (authoritativeGlobalSlot !== null) {
      const slotPage = Math.floor(authoritativeGlobalSlot / slotCount);
      if (!paged || slotPage === page || playbackStore.isPlaying) {
        const slot = ((authoritativeGlobalSlot % slotCount) + slotCount) % slotCount;
        if (visiblePads[slot]?.name) return slot;
      }
    }
    if (!playbackStore.isPlaying && manualRadialSlot !== null && visiblePads[manualRadialSlot]?.name) {
      return manualRadialSlot;
    }
    if (!playbackStore.isPlaying && backtrackedGlobalSlot !== null) {
      const slotPage = Math.floor(backtrackedGlobalSlot / slotCount);
      if (!paged || slotPage === page) {
        const slot = ((backtrackedGlobalSlot % slotCount) + slotCount) % slotCount;
        if (visiblePads[slot]?.name) return slot;
      }
    }
    if (visiblePads[selectedRadialSlot]?.name) return selectedRadialSlot;
    const firstNamed = visiblePads.findIndex(pad => Boolean(pad.name));
    return firstNamed >= 0 ? firstNamed : null;
  });

  $effect(() => {
    if (targetRadialSlot === null) return;
    selectedRadialSlot = targetRadialSlot;
    const delta = wheelAngleDelta(untrack(() => highlightAngle), targetRadialSlot, slotCount);
    if (delta !== 0) highlightAngle += delta;
  });

  const activeChord = $derived(!fretMode && targetRadialSlot !== null ? visiblePads[targetRadialSlot] ?? null : null);

  const activeStrings = $derived.by(() => {
    const active = new Set<number>();
    if (performing) {
      for (const attack of attacks) {
        if (currentMode !== 'mixed' && attack.mode !== currentMode) continue;
        for (const pos of dispatchedGuitarPositions(instrument, attack)) {
          active.add(pos.string);
        }
      }
      return active;
    }
    if (strummedStrings.size > 0 || heldStringIndices.size > 0) {
      return new Set([...strummedStrings.keys(), ...heldStringIndices]);
    }
    if (!activePitches.size) return active;
    if (fretMode && targetRadialSlot !== null) {
      const globalSlot = pageStart + targetRadialSlot;
      const activeFret = globalSlot < fretCount ? globalSlot + 1 : 0;
      tuning.forEach((open, index) => {
        if (activePitches.has(open + activeFret)) active.add(index);
      });
    } else if (!fretMode && activeChord?.stringPitches) {
      activeChord.stringPitches.forEach((pitch, index) => {
        if (typeof pitch === 'number' && activePitches.has(pitch)) active.add(index);
      });
    }
    if (active.size === 0) {
      for (const pos of guitarVoicing(instrument, activePitches)) {
        active.add(pos.string);
      }
    }
    return active;
  });

  // Wheel SVG Geometry in [-180..180] user space centered at (0, 0)
  const WHEEL_OUTER_R = 166;
  const WHEEL_INNER_R = 86;
  const WHEEL_LABEL_R = 127;
  const HUB_BEZEL_R = 81;
  const HUB_HOLE_R = 62;
  const TEXT_RING_R = 71;
  const sectorSpanDeg = $derived(360 / slotCount);
  // Constant linear gap width matching the outer 2.4° gap (2 * 166 * sin(1.2°) ≈ 6.95)
  const sectorGapWidth = $derived(slotCount > 1 ? 6.95 : 0);

  function polar(r: number, deg: number): { x: number; y: number } {
    const rad = ((deg - 90) * Math.PI) / 180;
    return { x: r * Math.cos(rad), y: r * Math.sin(rad) };
  }

  function halfGapAngleDeg(r: number, gapWidth: number): number {
    if (gapWidth <= 0 || r <= 0) return 0;
    return (Math.asin(Math.min(1, gapWidth / (2 * r))) * 180) / Math.PI;
  }

  function sectorPath(rIn: number, rOut: number, centerDeg: number, spanDeg: number, gapWidth: number): string {
    const halfOut = Math.max(0.5, spanDeg / 2 - halfGapAngleDeg(rOut, gapWidth));
    const halfIn = Math.max(0.5, spanDeg / 2 - halfGapAngleDeg(rIn, gapWidth));
    const o1 = polar(rOut, centerDeg - halfOut);
    const o2 = polar(rOut, centerDeg + halfOut);
    const i2 = polar(rIn, centerDeg + halfIn);
    const i1 = polar(rIn, centerDeg - halfIn);
    const largeArcOut = halfOut * 2 > 180 ? 1 : 0;
    const largeArcIn = halfIn * 2 > 180 ? 1 : 0;
    return [
      `M ${o1.x.toFixed(2)} ${o1.y.toFixed(2)}`,
      `A ${rOut} ${rOut} 0 ${largeArcOut} 1 ${o2.x.toFixed(2)} ${o2.y.toFixed(2)}`,
      `L ${i2.x.toFixed(2)} ${i2.y.toFixed(2)}`,
      `A ${rIn} ${rIn} 0 ${largeArcIn} 0 ${i1.x.toFixed(2)} ${i1.y.toFixed(2)}`,
      'Z',
    ].join(' ');
  }

  function innerArcPath(r: number, startDeg: number, endDeg: number): string {
    const p1 = polar(r, startDeg);
    const p2 = polar(r, endDeg);
    const largeArc = endDeg - startDeg > 180 ? 1 : 0;
    return `M ${p1.x.toFixed(2)} ${p1.y.toFixed(2)} A ${r} ${r} 0 ${largeArc} 1 ${p2.x.toFixed(2)} ${p2.y.toFixed(2)}`;
  }

  const wedges = $derived(Array.from({ length: slotCount }, (_, index) => {
    const centerDeg = index * sectorSpanDeg;
    const labelPos = polar(WHEEL_LABEL_R, centerDeg);
    const pad = visiblePads[index];
    return {
      index,
      name: pad?.name ?? '',
      pad,
      path: sectorPath(WHEEL_INNER_R, WHEEL_OUTER_R, centerDeg, sectorSpanDeg, sectorGapWidth),
      labelX: labelPos.x,
      labelY: labelPos.y,
    };
  }));

  const highlightArcR = WHEEL_INNER_R + 1.5;
  const highlightArcHalf = $derived(Math.max(1, sectorSpanDeg / 2 - halfGapAngleDeg(highlightArcR, sectorGapWidth) - 1));
  const highlightSectorPath = $derived(sectorPath(WHEEL_INNER_R, WHEEL_OUTER_R, 0, sectorSpanDeg, sectorGapWidth));
  const highlightInnerArc = $derived(innerArcPath(highlightArcR, -highlightArcHalf, highlightArcHalf));

  const svgId = $props.id();
  const bezelTextArcs = [0, 90, 180, 270].map((centerDeg, idx) => ({
    id: `${svgId}-bezel-arc-${idx}`,
    d: innerArcPath(TEXT_RING_R, centerDeg - 34, centerDeg + 34),
  }));

  const hubStrings = $derived.by(() => {
    const count = Math.max(1, tuning.length || 6);
    const spacing = count === 6 ? 15.5 : Math.min(18, 92 / count);
    const offset = (count - 1) / 2;
    return Array.from({ length: count }, (_, index) => {
      const y = (index - offset) * spacing;
      const chordVal = activeChord?.stringPitches?.[index];
      const muted = !fretMode && activeChord !== null && (chordVal === 'muted' || chordVal === null);
      return {
        index,
        stringNumber: index + 1,
        label: (stringKeys[index]?.trim() || String(index + 1)).toUpperCase(),
        key: stringKeys[index] ?? '',
        y,
        x1: -HUB_HOLE_R,
        x2: HUB_HOLE_R,
        active: activeStrings.has(index) || strummedStrings.has(index) || heldStringIndices.has(index),
        muted,
      };
    });
  });

  let strumTimers: ReturnType<typeof setTimeout>[] = [];
  let strummedStrings = $state<Map<number, GuitarPosition>>(new Map());
  let heldStrings = $state<Map<string, GuitarPosition>>(new Map());
  const heldStringIndices = $derived(new Set([...heldStrings.values()].map(pos => pos.string)));

  const manualPreviewPositions = $derived.by(() => {
    if (strummedStrings.size === 0 && heldStrings.size === 0) return [];
    const byString = new Map<number, GuitarPosition>();
    for (const pos of strummedStrings.values()) byString.set(pos.string, pos);
    for (const pos of heldStrings.values()) byString.set(pos.string, pos);
    return [...byString.values()].sort((a, b) => a.string - b.string);
  });
  const manualPreviewChord = $derived(
    !fretMode && manualPreviewPositions.length > 0 ? (activeChord?.name ?? null) : null,
  );
  $effect(() => {
    onpreviewpositions?.(manualPreviewPositions, manualPreviewChord);
  });

  function formatStrumBinding(binding: string | undefined, fallbackMouse: 'left' | 'right') {
    const normalized = (binding ?? (fallbackMouse === 'left' ? 'mouseLeft' : 'mouseRight')).trim();
    const lower = normalized.toLowerCase();
    if (['mouseleft', 'mouse_left', 'mouse-left', 'lmb', 'leftclick', 'left_click'].includes(lower)) {
      return t('instrument.mouseLeft');
    }
    if (['mouseright', 'mouse_right', 'mouse-right', 'rmb', 'rightclick', 'right_click'].includes(lower)) {
      return t('instrument.mouseRight');
    }
    return normalized.toUpperCase();
  }

  const strumUpLabel = $derived(
    t('instrument.guitarStrumUp', { key: formatStrumBinding(wheel.strumButtons?.up, 'left') }),
  );
  const strumDownLabel = $derived(
    t('instrument.guitarStrumDown', { key: formatStrumBinding(wheel.strumButtons?.down, 'right') }),
  );

  function clearStrumPreview() {
    for (const timer of strumTimers) clearTimeout(timer);
    strumTimers = [];
    if (strummedStrings.size) strummedStrings = new Map();
  }

  onDestroy(() => {
    clearStrumPreview();
    if (heldStrings.size) heldStrings = new Map();
    onpreviewpositions?.([], null);
  });

  function stringPreviewPosition(stringIndex: number): GuitarPosition | null {
    const open = tuning[stringIndex];
    if (open === undefined) return null;
    const radialSlot = targetRadialSlot ?? 0;
    const globalSlot = pageStart + radialSlot;
    if (fretMode) {
      const fret = globalSlot < fretCount ? globalSlot + 1 : 0;
      return { string: stringIndex, fret, pitch: open + fret };
    }
    const pad = visiblePads[radialSlot];
    const chordPitch = pad?.stringPitches?.[stringIndex];
    if (typeof chordPitch === 'number') {
      return { string: stringIndex, fret: Math.max(0, chordPitch - open), pitch: chordPitch };
    }
    if (chordPitch === 'muted' || chordPitch === null) return null;
    return { string: stringIndex, fret: 0, pitch: open };
  }

  function selectWedge(index: number) {
    const pad = visiblePads[index];
    if (!pad?.name) return;
    if (!playbackStore.isPlaying) {
      manualRadialSlot = index;
      selectedRadialSlot = index;
    }
    const globalSlot = pageStart + index;
    if (fretMode) {
      const fret = globalSlot < fretCount ? globalSlot + 1 : 0;
      const previewPitch = tuning[0] !== undefined ? tuning[0] + fret : undefined;
      if (previewPitch !== undefined) onpreview([previewPitch]);
      return;
    }
    const stringNotes = pad.stringPitches?.filter((pitch): pitch is number => typeof pitch === 'number') ?? [];
    const pitches = stringNotes.length ? [...new Set(stringNotes)] : pad.pitchClasses.map(pc => 60 + pc);
    if (pitches.length) onpreview(pitches);
  }

  function pluckString(stringIndex: number) {
    const pos = stringPreviewPosition(stringIndex);
    if (!pos) return;
    onpreview([pos.pitch]);
    const next = new Map(strummedStrings);
    next.set(pos.string, pos);
    strummedStrings = next;
    const endTimer = setTimeout(() => {
      const after = new Map(strummedStrings);
      if (after.get(pos.string) === pos) {
        after.delete(pos.string);
        strummedStrings = after;
      }
    }, 220);
    strumTimers.push(endTimer);
  }

  function strumStrings(direction: 'up' | 'down') {
    clearStrumPreview();
    const count = Math.max(1, tuning.length || 6);
    const order = Array.from({ length: count }, (_, idx) => idx);
    if (direction === 'down') order.reverse();
    const targets = order
      .map(index => stringPreviewPosition(index))
      .filter((pos): pos is GuitarPosition => pos !== null);
    targets.forEach((pos, step) => {
      const startTimer = setTimeout(() => {
        onpreview([pos.pitch]);
        const next = new Map(strummedStrings);
        next.set(pos.string, pos);
        strummedStrings = next;
        const endTimer = setTimeout(() => {
          const after = new Map(strummedStrings);
          if (after.get(pos.string) === pos) {
            after.delete(pos.string);
            strummedStrings = after;
          }
        }, 220);
        strumTimers.push(endTimer);
      }, step * 25);
      strumTimers.push(startTimer);
    });
  }

  export function pressString(source: string, stringIndex: number): number | null {
    if (heldStrings.has(source)) return null;
    const pos = stringPreviewPosition(stringIndex);
    if (!pos) return null;
    const next = new Map(heldStrings);
    next.set(source, pos);
    heldStrings = next;
    return pos.pitch;
  }

  export function releaseString(source: string): void {
    if (!heldStrings.has(source)) return;
    const next = new Map(heldStrings);
    next.delete(source);
    heldStrings = next;
  }

  export function clearHeldStrings(): void {
    untrack(() => {
      if (heldStrings.size) heldStrings = new Map();
    });
  }

  export function strum(direction: 'up' | 'down'): boolean {
    strumStrings(direction);
    return true;
  }

  export function stepWedge(delta: -1 | 1): boolean {
    if (playbackStore.isPlaying || !visiblePads.length) return false;
    const current = targetRadialSlot ?? 0;
    const len = visiblePads.length;
    for (let offset = 1; offset <= len; offset++) {
      const candidate = (current + delta * offset + len * 2) % len;
      if (visiblePads[candidate]?.name) {
        manualRadialSlot = candidate;
        selectedRadialSlot = candidate;
        return true;
      }
    }
    return false;
  }

  export function stepPage(delta: -1 | 1): boolean {
    if (locked || !paged || wheel.pageCount <= 1 || needsModeSync) return false;
    const nextPage = (page + delta + wheel.pageCount) % wheel.pageCount;
    void setPage(nextPage, false);
    return true;
  }

  export function toggleMixedMode(): boolean {
    if (locked || !mixed) return false;
    toggleMode();
    return true;
  }

  export function toggleTimbreMode(): boolean {
    if (locked || !wheel.timbreKey) return false;
    toggleTimbre();
    return true;
  }

  const timbreKeyLabel = $derived(wheel.timbreKey?.toUpperCase() ?? '');
  const timbreButtonTooltip = $derived(
    timbreKeyLabel ? t('instrument.guitarTimbreButtonTooltip', { key: timbreKeyLabel }) : '',
  );
  const timbreModeTooltip = $derived(t('instrument.guitarTimbreModeTooltip'));
  const timbreSyncTooltip = $derived(
    timbreKeyLabel ? t('instrument.guitarSyncTimbreTooltip', { key: timbreKeyLabel }) : '',
  );

  function toggleMode() {
    const next = physicalMode === 'frets' ? 'chords' : 'frets';
    void syncMode(next);
  }

  function toggleTimbre() {
    const current = wheelState.timbreMode === 'auto' ? wheelState.timbre : wheelState.timbreMode;
    const next: GuitarTimbre = current === 'distortion' ? 'clean' : 'distortion';
    void setTimbreMode(next);
  }

  async function setTimbreMode(mode: GuitarTimbreMode) {
    if (locked) return;
    pending = true;
    try {
      await api.playback.setGuitarTimbreMode(mode);
    } catch (error) {
      notificationCenter.error(t('instrument.chordWheelError'), String(error));
    } finally {
      try {
        const beforeRead = revision;
        const state = await api.playback.getChordWheelState();
        if (revision === beforeRead) receiveState(state);
      } catch {
        // Ignore transient read errors while state is updating.
      }
      pending = false;
    }
  }

  async function syncTimbre(timbre: GuitarTimbre) {
    if (locked) return;
    pending = true;
    try {
      await api.playback.syncGuitarTimbre(timbre);
      const beforeRead = revision;
      const state = await api.playback.getChordWheelState();
      if (revision === beforeRead) receiveState(state);
    } catch (error) {
      notificationCenter.error(t('instrument.chordWheelError'), String(error));
    } finally {
      pending = false;
    }
  }

  async function syncMode(mode: 'chords' | 'frets') {
    if (locked) return;
    pending = true;
    try {
      await onsyncmode(mode);
      const beforeRead = revision;
      const state = await api.playback.getChordWheelState();
      if (revision === beforeRead) receiveState(state);
    } catch (error) {
      notificationCenter.error(t('instrument.chordWheelError'), String(error));
    } finally {
      pending = false;
    }
  }

  function receiveState(state: ChordWheelState) {
    wheelState = state;
    loaded = true;
    if (state.error && state.error !== lastError) {
      notificationCenter.error(t('instrument.chordWheelError'), state.error);
    }
    lastError = state.error;
  }

  onMount(() => {
    let disposed = false;
    const unsubscribe = api.playback.onChordWheelState(state => {
      revision++;
      if (!disposed) receiveState(state);
    });
    const beforeRead = revision;
    void api.playback.getChordWheelState().then(state => {
      if (!disposed && revision === beforeRead) receiveState(state);
    }).catch(error => {
      if (!disposed) notificationCenter.error(t('instrument.chordWheelError'), String(error));
    });
    return () => {
      disposed = true;
      unsubscribe();
    };
  });

  async function setPage(nextPage: number, send: boolean) {
    if (locked || !Number.isInteger(nextPage) || nextPage < 0 || nextPage >= wheel.pageCount) return;
    pending = true;
    try {
      if (!await api.playback.setChordWheelPage(nextPage, send)) throw new Error(t('instrument.chordWheelPageFailed'));
      const beforeRead = revision;
      const state = await api.playback.getChordWheelState();
      if (revision === beforeRead) receiveState(state);
    } catch (error) {
      notificationCenter.error(t('instrument.chordWheelError'), String(error));
    } finally {
      pending = false;
    }
  }
</script>

<section class="nte-wheel-stage" aria-label={t('instrument.chordWheel')}>
  <div class="wheel-canvas-wrap">
    <svg
      class="nte-wheel-svg"
      viewBox="-180 -180 360 360"
      role="group"
      aria-label={t(fretMode ? 'instrument.fretboard' : 'instrument.chordWheelAvailableChords')}
    >
      <defs>
        {#each bezelTextArcs as arc}
          <path id={arc.id} d={arc.d} fill="none" />
        {/each}
        <clipPath id={`${svgId}-soundhole-clip`}>
          <circle cx="0" cy="0" r={HUB_HOLE_R} />
        </clipPath>
      </defs>

      <!-- 6 Background Wedges -->
      {#each wedges as wedge (wedge.index)}
        <path
          d={wedge.path}
          class="wheel-wedge-bg"
          class:empty={!wedge.name}
        />
      {/each}

      <!-- Smoothly Gliding Flat Accent Highlight Wedge -->
      <g
        class="wheel-highlight-rotor"
        class:hidden={targetRadialSlot === null}
        style:transform="rotate({highlightAngle}deg)"
      >
        <path
          d={highlightSectorPath}
          class="wheel-highlight-sector"
        />
        <path
          d={highlightInnerArc}
          class="wheel-highlight-arc"
        />
      </g>

      <!-- Interactive Wedge Hit Targets & Labels (stay upright above rotor) -->
      {#each wedges as wedge (wedge.index)}
        <g
          class="wheel-wedge-interactive"
          class:active={targetRadialSlot === wedge.index}
          class:disabled={!wedge.name}
          role="button"
          tabindex={wedge.name ? 0 : -1}
          aria-disabled={!wedge.name}
          aria-label={wedge.name || t('instrument.chordWheel')}
          aria-pressed={wedge.name ? targetRadialSlot === wedge.index : undefined}
          onclick={() => selectWedge(wedge.index)}
          onkeydown={(e) => {
            if ((e.key === 'Enter' || e.key === ' ') && wedge.name) {
              e.preventDefault();
              selectWedge(wedge.index);
            }
          }}
        >
          <path d={wedge.path} class="wheel-wedge-hit" fill="transparent" />
          {#if wedge.name}
            <text
              x={wedge.labelX}
              y={wedge.labelY}
              class="wheel-wedge-label"
              class:long={wedge.name.length >= 4}
              class:active={targetRadialSlot === wedge.index}
              text-anchor="middle"
              dominant-baseline="central"
            >
              {wedge.name}
            </text>
          {/if}
        </g>
      {/each}

      <!-- Center Hub Bezel & GUITAR Engraving -->
      <circle cx="0" cy="0" r={HUB_BEZEL_R} class="wheel-hub-bezel" />
      {#each bezelTextArcs as arc}
        <text class="wheel-bezel-text" text-anchor="middle">
          <textPath href="#{arc.id}" startOffset="50%">GUITAR</textPath>
        </text>
      {/each}

      <!-- Soundhole & 6 Strummable Center Strings -->
      <g
        class="wheel-soundhole"
        role="presentation"
        oncontextmenu={(e) => {
          e.preventDefault();
          strumStrings('down');
        }}
      >
        <circle
          cx="0"
          cy="0"
          r={HUB_HOLE_R}
          class="wheel-hub-hole"
          role="button"
          tabindex="0"
          aria-label={strumUpLabel}
          onclick={() => strumStrings('up')}
          onkeydown={(e) => {
            if (e.key === 'Enter' || e.key === ' ') {
              e.preventDefault();
              strumStrings('up');
            }
          }}
        />
        <g clip-path={`url(#${svgId}-soundhole-clip)`}>
          {#each hubStrings as str (str.index)}
            <g
              class="wheel-string-group"
              class:active={str.active}
              class:muted={str.muted}
              role="button"
              tabindex={str.muted ? -1 : 0}
              aria-disabled={str.muted}
              aria-label={`${t('instrument.guitarString')} ${str.stringNumber}${str.key ? ` (${str.key.toUpperCase()})` : ''}`}
              onclick={(e) => {
                e.stopPropagation();
                pluckString(str.index);
              }}
              onkeydown={(e) => {
                if ((e.key === 'Enter' || e.key === ' ') && !str.muted) {
                  e.preventDefault();
                  pluckString(str.index);
                }
              }}
            >
            <!-- Invisible generous hit area for clicking/strumming the string -->
            <rect x={str.x1} y={str.y - 6.5} width={str.x2 - str.x1} height="13" fill="transparent" />
            <line
              x1={str.x1}
              y1={str.y}
              x2={str.x2}
              y2={str.y}
              class="wheel-string-line"
              class:active={str.active}
              class:muted={str.muted}
            />
            <circle
              cx="0"
              cy={str.y}
              r="5.6"
              class="wheel-string-badge"
              class:active={str.active}
              class:muted={str.muted}
            />
            <text
              x="0"
              y={str.y}
              dy="0.35em"
              class="wheel-string-num"
              class:active={str.active}
              class:muted={str.muted}
              text-anchor="middle"
            >
              {str.label}
            </text>
          </g>
          {/each}
        </g>
        <circle cx="0" cy="0" r={HUB_HOLE_R} class="wheel-hub-hole-rim" aria-hidden="true" />
      </g>
    </svg>
  </div>

  <!-- NTE Bottom Pagination Bar: [R] Previous  ◉ ○ ○ ○  Next [T] -->
  {#if paged && wheel.pageCount > 1 && !needsModeSync}
    <div class="wheel-pagination-bar" role="group" aria-label={t('instrument.chordWheelPages')}>
      <button
        type="button"
        class="wheel-page-btn prev theme-ghost-button"
        disabled={locked}
        use:tooltip={t('instrument.chordWheelPreviousKey', { key: wheel.pageKeys[0].toUpperCase() })}
        onclick={() => setPage((page - 1 + wheel.pageCount) % wheel.pageCount, false)}
      >
        <kbd class="wheel-keycap">{wheel.pageKeys[0].toUpperCase()}</kbd>
        <span>{t('instrument.chordWheelPrevious')}</span>
      </button>

      <div class="wheel-page-dots" role="tablist" aria-label={t('instrument.chordWheelPages')}>
        {#each Array.from({ length: wheel.pageCount }, (_, idx) => idx) as dotIndex}
          <button
            type="button"
            role="tab"
            class="wheel-page-dot"
            class:active={page === dotIndex}
            aria-selected={page === dotIndex}
            aria-label={t('instrument.chordWheelPage', { page: dotIndex + 1, count: wheel.pageCount })}
            disabled={locked}
            use:tooltip={t('instrument.chordWheelPage', { page: dotIndex + 1, count: wheel.pageCount })}
            onclick={() => setPage(dotIndex, false)}
          >
            <span class="dot-core"></span>
          </button>
        {/each}
      </div>

      <button
        type="button"
        class="wheel-page-btn next theme-ghost-button"
        disabled={locked}
        use:tooltip={t('instrument.chordWheelNextKey', { key: wheel.pageKeys[1].toUpperCase() })}
        onclick={() => setPage((page + 1) % wheel.pageCount, false)}
      >
        <span>{t('instrument.chordWheelNext')}</span>
        <kbd class="wheel-keycap">{wheel.pageKeys[1].toUpperCase()}</kbd>
      </button>
    </div>
  {/if}

  <!-- Mode Sync, Timbre & Strum Row -->
  <div class="wheel-meta-row">
    {#if mixed}
      <div class="wheel-sync">
        <button
          type="button"
          class="wheel-page-btn wheel-timbre-btn theme-ghost-button"
          disabled={locked}
          use:tooltip={t('instrument.guitarSyncModeHint')}
          onclick={toggleMode}
        >
          {#if wheel.modeKey}
            <kbd class="wheel-keycap">{wheel.modeKey.toUpperCase()}</kbd>
          {:else}
            <ChordIcon size={13} />
          {/if}
          <span>{t('instrument.guitarSyncMode')}</span>
        </button>
        <Dropdown
          variant="ghost"
          animateWidth
          options={(['chords', 'frets'] as const).map(value => ({ value, label: t(`instrument.guitarMode_${value}`) }))}
          value={physicalMode}
          placeholder={t('instrument.guitarModeUnknown')}
          ariaLabel={t('instrument.guitarSyncMode')}
          tooltipText={t('instrument.guitarSyncModeHint')}
          disabled={locked}
          showTooltips={false}
          onchange={syncMode}
        />
      </div>
    {:else if needsModeSync && physicalMode}
      <Button variant="ghost" size="sm" disabled={locked} onclick={() => syncMode(physicalMode!)}>
        {t('instrument.guitarConfirmMode', { mode: t(`instrument.guitarMode_${physicalMode}`) })}
      </Button>
    {/if}

    {#if wheel.timbreKey}
      <div class="wheel-sync">
        <button
          type="button"
          class="wheel-page-btn wheel-timbre-btn theme-ghost-button"
          disabled={locked}
          use:tooltip={timbreButtonTooltip}
          onclick={toggleTimbre}
        >
          <kbd class="wheel-keycap">{timbreKeyLabel}</kbd>
          <span>{t('instrument.guitarTimbre')}</span>
        </button>
        <Dropdown
          variant="ghost"
          animateWidth
          options={(['auto', 'clean', 'distortion'] as const).map(value => ({ value, label: t(`instrument.guitarTimbre_${value}`) }))}
          value={wheelState.timbreMode ?? 'auto'}
          ariaLabel={t('instrument.guitarTimbre')}
          tooltipText={timbreModeTooltip}
          disabled={locked}
          showTooltips={false}
          onchange={setTimbreMode}
        />
      </div>
      <div class="wheel-sync">
        <span use:tooltip={timbreSyncTooltip}>{t('instrument.guitarSyncTimbre')}</span>
        <Dropdown
          variant="ghost"
          animateWidth
          options={(['clean', 'distortion'] as const).map(value => ({ value, label: t(`instrument.guitarTimbre_${value}`) }))}
          value={wheelState.timbre ?? null}
          placeholder={t('instrument.guitarTimbreUnknown')}
          ariaLabel={t('instrument.guitarSyncTimbre')}
          tooltipText={timbreSyncTooltip}
          disabled={locked}
          showTooltips={false}
          onchange={syncTimbre}
        />
      </div>
    {/if}

    <div class="wheel-strum-actions" role="group" aria-label={t('instrument.guitarStrumGroup')}>
      <button
        type="button"
        class="wheel-strum-btn theme-ghost-button"
        aria-label={strumUpLabel}
        use:tooltip={strumUpLabel}
        onclick={() => strumStrings('up')}
      >
        <span class="wheel-keycap wheel-strum-cap" aria-hidden="true">
          <svg class="wheel-strum-icon up" viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
            <path d="M8 12.2V4.4" />
            <path d="M4.6 7.5Q6.4 6 8 4.1Q9.6 6 11.4 7.5" />
          </svg>
        </span>
      </button>
      <button
        type="button"
        class="wheel-strum-btn theme-ghost-button"
        aria-label={strumDownLabel}
        use:tooltip={strumDownLabel}
        onclick={() => strumStrings('down')}
        oncontextmenu={(e) => {
          e.preventDefault();
          strumStrings('down');
        }}
      >
        <span class="wheel-keycap wheel-strum-cap" aria-hidden="true">
          <svg class="wheel-strum-icon down" viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
            <path d="M8 3.8V11.6" />
            <path d="M4.6 8.5Q6.4 10 8 11.9Q9.6 10 11.4 8.5" />
          </svg>
        </span>
      </button>
    </div>
  </div>

  {#if (mixed && !physicalMode) || (paged && wheel.pageCount > 1 && !needsModeSync) || fretMode}
    <div class="wheel-hints">
      {#if paged && wheel.pageCount > 1 && !needsModeSync}
        <p class="wheel-hint">{t('instrument.chordWheelSyncHint')}</p>
      {/if}
      {#if mixed && !physicalMode}
        <p class="wheel-hint">{t('instrument.guitarSyncModeHint')}</p>
      {:else if fretMode || (paged && wheel.pageCount > 1)}
        <p class="wheel-hint">{t(fretMode ? 'instrument.guitarSharedFret' : 'instrument.chordWheelHint')}</p>
      {/if}
    </div>
  {/if}
</section>

<style>
  .nte-wheel-stage {
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 12px;
    width: 100%;
    padding: 4px 0 8px;
  }

  .wheel-canvas-wrap {
    position: relative;
    width: var(--wheel-size, 316px);
    height: var(--wheel-size, 316px);
    max-width: 100%;
    aspect-ratio: 1;
    flex-shrink: 0;
  }

  .nte-wheel-svg {
    width: 100%;
    height: 100%;
    overflow: visible;
    user-select: none;
  }

  .wheel-wedge-bg {
    fill: var(--bg-secondary);
    stroke: var(--border-subtle);
    stroke-width: 1;
    transition: opacity var(--transition-fast), fill var(--transition-fast);
  }

  .wheel-wedge-bg.empty {
    opacity: 0.45;
  }

  .wheel-highlight-rotor {
    transform-origin: 0px 0px;
    transition: transform var(--transition-slow), opacity var(--transition-fast);
    pointer-events: none;
  }

  .wheel-highlight-rotor.hidden {
    opacity: 0;
  }

  .wheel-highlight-sector {
    fill: var(--accent);
    stroke: var(--accent);
    stroke-width: 1;
  }

  .wheel-highlight-arc {
    fill: none;
    stroke: rgba(255, 255, 255, 0.85);
    stroke-width: 3;
    stroke-linecap: round;
  }

  .wheel-wedge-interactive {
    cursor: pointer;
    outline: none;
  }

  .wheel-wedge-interactive.disabled {
    cursor: default;
  }

  .wheel-wedge-interactive:not(.disabled):hover .wheel-wedge-hit {
    fill: var(--bg-hover);
  }

  .wheel-wedge-interactive:focus-visible .wheel-wedge-hit {
    stroke: var(--accent);
    stroke-width: 2;
  }

  .wheel-wedge-label {
    fill: var(--text-primary);
    font-size: 20px;
    font-weight: 600;
    letter-spacing: 0.01em;
    pointer-events: none;
    transition: fill var(--transition-fast);
  }

  .wheel-wedge-label.long {
    font-size: 16px;
  }

  .wheel-wedge-label.active {
    fill: #ffffff;
    font-weight: 700;
  }

  .wheel-hub-bezel {
    fill: var(--bg-tertiary);
    stroke: var(--border-subtle);
    stroke-width: 1.5;
  }

  .wheel-hub-hole {
    fill: var(--bg-primary);
    stroke: none;
    outline: none;
    cursor: pointer;
  }

  .wheel-hub-hole:focus-visible {
    stroke: var(--accent);
    stroke-width: 2;
  }

  .wheel-hub-hole-rim {
    fill: none;
    stroke: var(--border-subtle);
    stroke-width: 1.5;
    pointer-events: none;
  }

  .wheel-strum-actions {
    display: inline-flex;
    align-items: center;
    gap: 4px;
  }

  .wheel-strum-btn {
    display: inline-flex;
    align-items: center;
    justify-content: center;
    height: 24px;
    padding: 0 2px;
    border-radius: 999px;
    line-height: 1;
  }

  .wheel-strum-cap {
    width: 20px;
    min-width: 20px;
    height: 20px;
    padding: 0;
    font-size: 11px;
  }

  .wheel-strum-icon {
    width: 13px;
    height: 13px;
    display: block;
    transition: transform var(--transition-fast);
  }

  .wheel-strum-btn:hover:not(:disabled) .wheel-strum-icon.up {
    transform: translateY(-1.2px);
  }

  .wheel-strum-btn:hover:not(:disabled) .wheel-strum-icon.down {
    transform: translateY(1.2px);
  }

  .wheel-strum-btn:active:not(:disabled) .wheel-strum-icon.up {
    transform: translateY(-2px) scale(0.95);
  }

  .wheel-strum-btn:active:not(:disabled) .wheel-strum-icon.down {
    transform: translateY(2px) scale(0.95);
  }

  .wheel-bezel-text {
    fill: var(--text-tertiary);
    opacity: 0.55;
    font-size: 7px;
    font-weight: 700;
    letter-spacing: 0.24em;
    pointer-events: none;
  }

  .wheel-string-group {
    cursor: pointer;
    outline: none;
    transform-origin: 0px 0px;
  }

  .wheel-string-group.muted {
    cursor: default;
    opacity: 0.32;
  }

  .wheel-string-line {
    stroke: var(--accent);
    opacity: 0.6;
    stroke-width: 1.9;
    stroke-linecap: round;
    transition: stroke var(--transition-fast), stroke-width var(--transition-fast), opacity var(--transition-fast);
  }

  .wheel-string-group:not(.muted):hover .wheel-string-line {
    opacity: 0.9;
    stroke-width: 2.3;
  }

  .wheel-string-line.active {
    stroke: var(--accent);
    opacity: 1;
    stroke-width: 2.9;
    animation: wheelStringStrum 0.18s ease-out;
  }

  .wheel-string-badge {
    fill: var(--bg-secondary);
    stroke: var(--accent);
    stroke-width: 1;
    transition: fill var(--transition-fast), stroke var(--transition-fast);
  }

  .wheel-string-badge.active {
    fill: var(--accent);
    stroke: #ffffff;
  }

  .wheel-string-num {
    fill: var(--text-primary);
    font-size: 7.5px;
    font-weight: 700;
    pointer-events: none;
  }

  .wheel-string-num.active {
    fill: #ffffff;
  }

  @keyframes wheelStringStrum {
    0% { transform: translateY(0px); }
    25% { transform: translateY(-1.8px); }
    55% { transform: translateY(1.5px); }
    80% { transform: translateY(-0.7px); }
    100% { transform: translateY(0px); }
  }

  .wheel-pagination-bar {
    display: flex;
    align-items: center;
    justify-content: center;
    gap: 18px;
    min-height: 28px;
  }

  .wheel-page-btn {
    display: inline-flex;
    align-items: center;
    gap: 7px;
    padding: 4px 6px;
    border-radius: var(--radius-sm);
    font: inherit;
    font-size: 12.5px;
    font-weight: 500;
  }

  .wheel-timbre-btn {
    height: 24px;
    padding: 0 4px;
    font-size: 12px;
    line-height: 1;
  }

  .wheel-keycap {
    display: inline-flex;
    align-items: center;
    justify-content: center;
    min-width: 20px;
    height: 20px;
    padding: 0 5px;
    border-radius: 999px;
    background: var(--control-fill-hover);
    color: var(--text-primary);
    border: 1px solid var(--border-medium);
    font-family: inherit;
    font-size: 10.5px;
    font-weight: 700;
    line-height: 1;
  }

  .wheel-page-dots {
    display: inline-flex;
    align-items: center;
    gap: 10px;
  }

  .wheel-page-dot {
    position: relative;
    display: grid;
    place-items: center;
    width: 16px;
    height: 16px;
    padding: 0;
    border: none;
    background: transparent;
    cursor: pointer;
  }

  .wheel-page-dot:disabled {
    cursor: default;
    opacity: 0.65;
  }

  .wheel-page-dot:focus-visible {
    outline: 2px solid var(--accent);
    outline-offset: 2px;
    border-radius: 50%;
  }

  .wheel-page-dot .dot-core {
    width: 9px;
    height: 9px;
    border-radius: 50%;
    border: 1.5px solid var(--text-tertiary);
    background: transparent;
    transition: border-color var(--transition-fast), transform var(--transition-fast);
  }

  .wheel-page-dot:hover:not(:disabled) .dot-core {
    border-color: var(--text-primary);
    transform: scale(1.1);
  }

  .wheel-page-dot.active .dot-core {
    width: 12px;
    height: 12px;
    border-color: var(--accent);
    position: relative;
  }

  .wheel-page-dot.active .dot-core::after {
    content: '';
    position: absolute;
    inset: 2px;
    border-radius: 50%;
    background: var(--accent);
  }

  .wheel-meta-row {
    display: flex;
    align-items: center;
    justify-content: center;
    flex-wrap: wrap;
    gap: 16px;
  }

  .wheel-sync {
    display: inline-flex;
    align-items: center;
    min-height: 24px;
    gap: 6px;
    color: var(--text-tertiary);
    font-size: 12px;
    line-height: 1;
  }

  .wheel-sync > span {
    display: inline-flex;
    align-items: center;
    height: 24px;
    line-height: 1;
  }

  .wheel-sync :global(.fluent-dropdown.variant-ghost) {
    align-self: center;
    align-items: center;
    height: 24px;
  }

  .wheel-sync :global(.fluent-dropdown.variant-ghost .dropdown-trigger) {
    height: 24px;
    padding: 0;
    font-size: 12px;
    line-height: 1;
    align-items: center;
  }

  .wheel-sync :global(.fluent-dropdown.variant-ghost .trigger-content) {
    height: 100%;
    align-items: center;
    line-height: 1;
  }

  .wheel-sync :global(.fluent-dropdown.variant-ghost .trigger-label),
  .wheel-sync :global(.fluent-dropdown.variant-ghost .trigger-placeholder) {
    font-size: 12px;
    font-weight: 500;
    line-height: 1;
  }

  .wheel-sync :global(.fluent-dropdown.variant-ghost .chevron-icon) {
    margin-top: 0;
  }

  .wheel-hints {
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 4px;
  }

  .wheel-hint {
    margin: 0;
    color: var(--text-tertiary);
    font-size: 11px;
    line-height: 1.45;
    text-align: center;
    max-width: 560px;
  }

  @media (prefers-reduced-motion: reduce) {
    .wheel-highlight-rotor,
    .wheel-wedge-bg,
    .wheel-wedge-label,
    .wheel-string-line,
    .wheel-string-badge,
    .wheel-page-btn,
    .wheel-strum-icon,
    .wheel-page-dot .dot-core {
      transition: none;
    }
    .wheel-strum-btn:hover:not(:disabled) .wheel-strum-icon,
    .wheel-strum-btn:active:not(:disabled) .wheel-strum-icon {
      transform: none;
    }
    .wheel-string-line.active {
      animation: none;
    }
  }
</style>
