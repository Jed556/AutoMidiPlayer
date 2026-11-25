<script lang="ts">
  import { onDestroy, onMount, untrack } from 'svelte';
  import { midiInputStore } from '../state/midiInputStore.svelte';
  import { playbackStore } from '../state/playbackStore.svelte';
  import { scheduledPlaybackStore } from '../state/scheduledPlaybackStore.svelte';
  import { gameStore } from '../state/gameStore.svelte';
  import { uiStore } from '../state/uiStore.svelte';
  import { animationStore } from '../state/animationStore.svelte';
  import { api } from '../backend/index.js';
  import { notificationCenter } from '../ui/notificationCenter.svelte';
  import { t } from '../i18n';
  import FluentIcon from '../components/Icons/FluentIcon.svelte';
  import AdaptiveText from '../components/Common/AdaptiveText.svelte';
  import Button from '../components/Common/Button.svelte';
  import NumberBox from '../components/Common/NumberBox.svelte';
  import Dropdown from '../components/Common/Dropdown.svelte';
  import RefreshIcon from '../components/Icons/RefreshIcon.svelte';
  import PedalIcon from '../components/Icons/PedalIcon.svelte';
  import ChordIcon from '../components/Icons/ChordIcon.svelte';
  import GradientSweep from '../components/Common/GradientSweep.svelte';
  import SlidingTabs from '../components/Common/SlidingTabs.svelte';
  import { createFrameResizeObserver } from '../utils/frameResizeObserver';
  import { tooltip } from '../actions/tooltip';
  import GameIcon from '../components/Common/GameIcon.svelte';
  import { toastOffset } from '../actions/toastOffset';
  import { createDropdownState } from '../ui/dropdownState.svelte';
  import { chordPadPitches, InstrumentInput, keyboardPitches, resolveGuitarKeyAction } from '../utils/instrumentInput';
  import { PressHighlights } from '../utils/pressHighlights';
  import type { ArrangementState } from '../../../shared/types/ipc';
  import { createPianoRows, createPianoSlots, hasInGameInstrumentLayout, pianoRange, type PianoSlot } from '../utils/instrumentKeyboard';
  import { guitarNotes, instrumentChordMode, instrumentKeyRows, instrumentLayoutStyle, instrumentNotes as getInstrumentNotes } from '../../../shared/utils/instrumentLayout';
  import InstrumentLayout from '../components/Instrument/InstrumentLayout.svelte';
  import GuitarLayout from '../components/Instrument/GuitarLayout.svelte';
  import ChordPadsLayout from '../components/Instrument/ChordPadsLayout.svelte';
  import type { InstrumentInputMode } from '../../../shared/types/games';
  import ChordWheelControls from '../components/Instrument/ChordWheelControls.svelte';
  import { fretRomanNumeral, type GuitarPosition } from '../utils/guitarLayout';

  let topBarWidth = $state(0);
  const showTopLabels = $derived(topBarWidth >= 700);
  let bottomBarWidth = $state(0);
  const showBottomLabels = $derived(bottomBarWidth >= 700);

  let topBar = $state<HTMLDivElement | null>(null);

  function toggleGameSelector() {
    uiStore.gameSelectorAnchor = topBar;
    uiStore.gameSelectorOpen = !uiStore.gameSelectorOpen;
  }

  onDestroy(() => {
    if (uiStore.gameSelectorAnchor === topBar) {
      uiStore.gameSelectorOpen = false;
      uiStore.gameSelectorAnchor = null;
    }
  });

  let selectedInstrumentId = $state<string>(gameStore.activeInstrument?.id ?? '');
  let selectedInstrument = $derived(
    gameStore.activeGame?.instruments?.find(i => i.id === selectedInstrumentId) || null
  );
  const inputModes = $derived((['keyboard', 'chords', 'frets', 'mixed'] as const).filter(mode =>
    mode === 'mixed' ? gameStore.getInstrumentLayouts(selectedInstrument, gameStore.activeGame, mode).length > 0 :
    (mode === 'keyboard' ? selectedInstrument?.keyboardLayouts : mode === 'chords' ? selectedInstrument?.chordLayouts : selectedInstrument?.fretLayouts)?.length));
  const showInputModes = $derived(inputModes.length > 1);
  let lastInputModes = $state<InstrumentInputMode[]>([]);
  let lastInputMode = $state<InstrumentInputMode>('keyboard');
  $effect(() => {
    if (showInputModes) {
      lastInputModes = inputModes;
      lastInputMode = gameStore.activeInputMode;
    }
  });
  const displayInputModes = $derived(showInputModes ? inputModes : lastInputModes);
  const displayInputMode = $derived(showInputModes ? gameStore.activeInputMode : lastInputMode);

  let compactStage = $state(uiStore.instrumentCompactStage);
  const initialWidth = typeof window !== 'undefined' ? Math.max(720, Math.min(1600, window.innerWidth - 40)) : 720;
  const initialHeight = typeof window !== 'undefined' ? Math.max(300, window.innerHeight - 100) : 500;
  let pianoAvailableWidth = $state(initialWidth);
  let pianoAvailableHeight = $state(initialHeight);
  let pianoStageWidth = $state(initialWidth);
  let selectedStage = $derived(uiStore.instrumentStage);
  let gameSelected = $derived(uiStore.instrumentGameSelected);
  let pianoSelected = $derived(uiStore.instrumentPianoSelected);
  const hasInGameLayout = $derived(hasInGameInstrumentLayout(selectedInstrument));
  const usePreferredStage = $derived(
    (compactStage && gameSelected && pianoSelected) || (!gameSelected && !pianoSelected)
  );
  const showGame = $derived(hasInGameLayout && (usePreferredStage ? selectedStage === 'game' : gameSelected));
  const showPiano = $derived(!hasInGameLayout || (usePreferredStage ? selectedStage === 'piano' : pianoSelected));

  function selectStage(view: 'game' | 'piano') {
    const other = view === 'game' ? 'piano' : 'game';
    const next = { game: showGame, piano: showPiano };
    next[view] = compactStage || !next[view];
    next[other] = !compactStage && (next[other] || !next[view]);
    uiStore.setInstrumentStage(next[view] ? view : other);
    uiStore.setInstrumentToggles(next.game, next.piano);
  }

  function fitInstrumentStage(
    viewport: HTMLDivElement,
    options: {
      stage: 'game' | 'piano';
      gameSelected: boolean;
      pianoSelected: boolean;
      hasGame: boolean;
      duration: number;
      instrumentId?: string;
      layoutId?: string;
      inputMode?: string;
    },
  ) {
    const content = viewport.querySelector('.inst-stage') as HTMLDivElement;
    const game = content.querySelector('.game-stage') as HTMLDivElement;
    const piano = content.querySelector('.piano-strip-section') as HTMLDivElement;
    const gameSlot = game.parentElement!;
    const pianoSlot = piano.parentElement!;
    const reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)');
    const BASE_WHEEL_SIZE = 260;
    const MAX_WHEEL_SIZE = 430;
    const MAX_GAME_SCALE = 1.45;
    type StageReveal = [game: number, piano: number, gap: number];
    let availableWidth = 0;
    let availableHeight = 0;
    let stageWidth = 0;
    let naturalGameWidth = 0;
    let naturalGameHeight = 0;
    let naturalPianoHeight = 0;
    let wheelOverhead = 0;
    let hasWheel = false;
    let current: StageReveal | null = null;
    let target: StageReveal = [0, 0, 0];
    let frame: number | null = null;

    function syncGameDomMetrics() {
      const wrap = game.querySelector('.wheel-canvas-wrap') as HTMLElement | null;
      const inner = game.firstElementChild as HTMLElement | null;
      hasWheel = Boolean(wrap);
      if (wrap && game.offsetHeight > 0) {
        wheelOverhead = Math.max(0, game.offsetHeight - wrap.offsetHeight);
      }
      if (inner && inner.offsetWidth > 0) {
        naturalGameWidth = inner.offsetWidth;
      }
      if (!hasWheel && game.offsetHeight > 0) {
        naturalGameHeight = game.offsetHeight;
      }
    }

    function resolveGameMetrics(pianoReveal: number, gapReveal: number): { height: number; scale: number } {
      const reservedBelow = pianoReveal * naturalPianoHeight + gapReveal * 24;
      if (hasWheel) {
        const verticalPad = pianoReveal > 0.01 ? 28 : 70;
        const freeHeight = (availableHeight - reservedBelow - wheelOverhead - verticalPad) * 0.88;
        const freeWidth = (availableWidth - 70) * 0.88;
        const wheelSize = Math.round(
          Math.max(BASE_WHEEL_SIZE, Math.min(freeWidth, freeHeight, MAX_WHEEL_SIZE)),
        );
        content.style.setProperty('--wheel-size', `${wheelSize}px`);
        return { height: wheelSize + wheelOverhead, scale: 1 };
      }
      content.style.removeProperty('--wheel-size');
      if (naturalGameHeight <= 0 || naturalGameWidth <= 0) {
        return { height: naturalGameHeight, scale: 1 };
      }
      const verticalPad = pianoReveal > 0.01 ? 18 : 52;
      const freeHeight = Math.max(naturalGameHeight, (availableHeight - reservedBelow - verticalPad) * 0.92);
      const freeWidth = Math.max(naturalGameWidth, (availableWidth - 56) * 0.93);
      const gameScale = Math.max(
        1,
        Math.min(freeWidth / naturalGameWidth, freeHeight / naturalGameHeight, MAX_GAME_SCALE),
      );
      return { height: naturalGameHeight * gameScale, scale: gameScale };
    }

    function draw(reveal: StageReveal) {
      if (!current) content.style.visibility = 'visible';
      current = reveal;
      const [gameReveal, pianoReveal, gapReveal] = reveal;
      const { height: effectiveGameHeight, scale: gameScale } = resolveGameMetrics(pianoReveal, gapReveal);
      const gameHeight = gameReveal * effectiveGameHeight;
      const pianoHeight = pianoReveal * naturalPianoHeight;
      const gap = gapReveal * 24;
      const height = Math.max(1, gameHeight + pianoHeight + gap);
      const scale = Math.min(1, availableWidth / stageWidth, availableHeight / height);
      gameSlot.style.height = `${gameHeight}px`;
      pianoSlot.style.height = `${pianoHeight}px`;
      content.style.gap = `${gap}px`;
      content.style.transform = `scale(${scale})`;
      // The slots push the other view while the panels slide out through the stage mask.
      const travel = (availableHeight + 40) / Math.max(scale, 0.001);
      game.style.visibility = gameReveal === 0 ? 'hidden' : '';
      piano.style.visibility = pianoReveal === 0 ? 'hidden' : '';
      const scaleTransform = gameScale > 1.001 ? `scale(${gameScale})` : '';
      game.style.transform = gameReveal > 0 && gameReveal < 1
        ? `translateY(${-travel * (1 - gameReveal)}px)${scaleTransform ? ` ${scaleTransform}` : ''}`
        : scaleTransform;
      piano.style.transform = pianoReveal > 0 && pianoReveal < 1
        ? `translateY(${travel * (1 - pianoReveal)}px)` : '';
    }

    function stopAnimation() {
      if (frame !== null) cancelAnimationFrame(frame);
      frame = null;
    }

    function fit() {
      if (stageWidth === 0) {
        stopAnimation();
        current = null;
        return;
      }

      syncGameDomMetrics();
      pianoAvailableWidth = Math.min(stageWidth, availableWidth);
      pianoAvailableHeight = availableHeight;
      pianoStageWidth = stageWidth;
      const widthScale = Math.min(1, availableWidth / stageWidth);
      const baseGameHeight = hasWheel ? BASE_WHEEL_SIZE + wheelOverhead : naturalGameHeight;
      compactStage = options.hasGame && availableHeight < (baseGameHeight + naturalPianoHeight + 24) * widthScale * 0.85;
      uiStore.instrumentCompactStage = compactStage;
      const next: StageReveal = [showGame ? 1 : 0, showPiano ? 1 : 0, showGame && showPiano ? 1 : 0];
      const duration = reducedMotion.matches ? 0 : options.duration;
      const start = current;
      if (!start || duration <= 0) {
        stopAnimation();
        target = next;
        draw(next);
        return;
      }
      // Resize the current frame of the slide using the latest natural sizes.
      draw(start);
      if (next[0] === target[0] && next[1] === target[1]) return;

      stopAnimation();
      target = next;
      const [startGame, startPiano, startGap] = start;
      const startedAt = performance.now();
      function advance(now: number) {
        const progress = Math.min(1, Math.max(0, (now - startedAt) / duration));
        if (progress === 1) {
          frame = null;
          draw(next);
          return;
        }
        const eased = 1 - Math.pow(1 - progress, 3);
        draw([
          startGame + (next[0] - startGame) * eased,
          startPiano + (next[1] - startPiano) * eased,
          startGap + (next[2] - startGap) * eased,
        ]);
        frame = requestAnimationFrame(advance);
      }
      frame = requestAnimationFrame(advance);
    }

    const resize = createFrameResizeObserver(entries => {
      let changed = false;
      for (const { target, contentRect: { width, height } } of entries) {
        if (target === viewport && (width !== availableWidth || height !== availableHeight)) {
          availableWidth = width;
          availableHeight = height;
          changed = true;
        } else if (target === game) {
          const wrap = game.querySelector('.wheel-canvas-wrap') as HTMLElement | null;
          const inner = game.firstElementChild as HTMLElement | null;
          const nextGameWidth = inner ? inner.offsetWidth : width;
          const nextHasWheel = Boolean(wrap);
          const nextOverhead = wrap ? Math.max(0, height - wrap.offsetHeight) : 0;
          if (
            nextHasWheel !== hasWheel ||
            Math.abs(nextGameWidth - naturalGameWidth) > 0.5 ||
            (nextHasWheel ? Math.abs(nextOverhead - wheelOverhead) > 0.5 : height !== naturalGameHeight)
          ) {
            hasWheel = nextHasWheel;
            wheelOverhead = nextOverhead;
            naturalGameWidth = nextGameWidth;
            naturalGameHeight = height;
            changed = true;
          } else {
            naturalGameHeight = height;
          }
        } else if (target === piano && (width !== stageWidth || height !== naturalPianoHeight)) {
          stageWidth = width;
          naturalPianoHeight = height;
          changed = true;
        }
      }
      if (changed) fit();
    });
    resize.observe(viewport);
    resize.observe(game);
    resize.observe(piano);
    reducedMotion.addEventListener('change', fit);
    return {
      update(next: typeof options) {
        options = next;
        fit();
      },
      destroy() {
        resize.disconnect();
        reducedMotion.removeEventListener('change', fit);
        stopAnimation();
      },
    };
  }

  // ==========================================
  // Instrument & Keyboard Layout Selection
  // ==========================================
  let selectedLayoutId = $state<string>('');

  let instrumentOptions = $derived.by(() => {
    const list = gameStore.activeGame?.instruments;
    if (!list || list.length === 0) {
      return [];
    }

    const categorized = new Map<string, Array<{ value: string; label: string }>>();
    const rootItems: Array<{ value: string; label: string }> = [];

    for (const inst of list) {
      const item = { value: inst.id, label: inst.name };
      const cat = inst.category || inst.subfolder;
      if (cat && cat.trim() && cat.toLowerCase() !== 'general' && cat.toLowerCase() !== 'instruments') {
        const group = categorized.get(cat) || [];
        group.push(item);
        categorized.set(cat, group);
      } else {
        rootItems.push(item);
      }
    }

    const result: any[] = [];
    for (const [catName, items] of categorized.entries()) {
      result.push({
        isSubmenu: true,
        label: catName,
        items
      });
    }
    result.push(...rootItems);
    return result;
  });

  let layoutOptions = $derived(gameStore.getInstrumentLayouts(selectedInstrument).map(l => ({
    value: l.id,
    label: l.name
  })));

  $effect(() => {
    selectedInstrumentId = gameStore.activeInstrument?.id ?? '';
  });

  $effect(() => {
    selectedLayoutId = gameStore.activeLayout?.id ?? '';
  });

  const chordMode = $derived(instrumentChordMode(selectedInstrument, gameStore.activeLayout));
  let modePending = $state(false);
  async function selectInputMode(mode: InstrumentInputMode) {
    if (modePending || playbackStore.isPlaying) return;
    modePending = true;
    try { await gameStore.setInputMode(mode); }
    catch (error) { notificationCenter.error(t('common.error'), String(error)); }
    finally { modePending = false; }
  }
  const mixed = $derived(gameStore.activeInputMode === 'mixed');
  const supportsChords = $derived(chordMode !== null || mixed);
  const performing = $derived(playbackStore.isPlaying && !playbackStore.isListenMode);
  type PhysicalMode = 'chords' | 'frets';
  let syncedMode = $state<PhysicalMode | null>(null);
  let modeRead = $state(false);
  let syncRevision = 0;

  let lastModeContext = '';

  $effect(() => {
    if (!wheel || gameStore.activeInputMode === 'keyboard') {
      lastModeContext = '';
      modeRead = false;
      syncedMode = null;
      return;
    }
    if (modePending) return;
    // Re-read whenever playback toggles so returning to this view or pausing keeps mode in sync.
    playbackStore.isPlaying;
    const contextKey = `${gameStore.activeGame?.id ?? ''}:${selectedInstrumentId}:${gameStore.activeLayout?.id ?? ''}:${gameStore.activeInputMode}`;
    if (contextKey !== lastModeContext) {
      lastModeContext = contextKey;
      modeRead = false;
      syncedMode = null;
    }
    const revision = ++syncRevision;
    void gameStore.hasPlaybackMapping().then(async ready => {
      if (!ready || revision !== syncRevision) return;
      const mode = await api.playback.getGuitarMode();
      if (revision === syncRevision) {
        syncedMode = mode;
        modeRead = true;
      }
    }).catch(error => {
      if (revision === syncRevision) notificationCenter.error(t('instrument.chordWheelError'), String(error));
    });
    return () => { ++syncRevision; };
  });
  async function syncMode(mode: PhysicalMode) {
    const revision = ++syncRevision;
    await api.playback.syncGuitarMode(mode);
    if (revision === syncRevision) syncedMode = mode;
  }
  let highlightedActions = $state.raw<ArrangementState[]>([]);
  const displayingDispatch = $derived(performing || (!playbackStore.isListenMode && highlightedActions.length > 0));
  const displayedAction = $derived(highlightedActions.at(-1) ?? playbackStore.arrangement);
  const physicalMode = $derived<PhysicalMode | null>(mixed
    ? ((displayingDispatch && (displayedAction?.mode === 'frets' || displayedAction?.mode === 'chords') ? displayedAction.mode : null) ?? syncedMode)
    : (gameStore.activeInputMode === 'frets' || gameStore.activeInputMode === 'chords' ? gameStore.activeInputMode : null));
  const guitarMode = $derived(mixed
    ? (displayingDispatch ? displayedAction?.mode : null) ?? syncedMode ?? (playbackStore.isPlaying && playbackStore.isListenMode ? 'chords' : 'mixed')
    : gameStore.activeInputMode);
  const fretMode = $derived(guitarMode === 'frets');
  const wheelLayout = $derived(gameStore.activeLayout?.chordWheel ? gameStore.activeLayout :
    gameStore.activeGame?.layouts.find(layout => layout.id.toLowerCase() === gameStore.activeLayout?.fretboard?.wheelLayout.toLowerCase()));
  const wheel = $derived(wheelLayout?.chordWheel?.positions.length ? wheelLayout.chordWheel : undefined);
  const fretCount = $derived(selectedInstrument?.guitar?.fretCount ?? 24);
  const openSlot = $derived(wheel ? Math.ceil(fretCount / wheel.positions.length) * wheel.positions.length : 0);
  const paged = $derived(guitarMode !== 'mixed' && (fretMode ? selectedInstrument?.pagedFrets : selectedInstrument?.pagedChords) !== false);
  const displayWheel = $derived(wheel ? {
    ...wheel,
    pageKeys: (fretMode ? gameStore.activeLayout?.fretboard?.pageKeys : undefined) ?? wheel.pageKeys,
    strumButtons: (fretMode ? gameStore.activeLayout?.fretboard?.strumButtons : undefined) ?? wheel.strumButtons,
    modeKey: gameStore.activeLayout?.fretboard?.modeKey ?? wheel.modeKey,
    timbreKey: gameStore.activeLayout?.fretboard?.timbreKey ?? wheel.timbreKey,
    pageCount: !paged ? 1 : fretMode ? openSlot / wheel.positions.length + 1 : wheel.pageCount,
  } : null);
  const fretLayout = $derived(
    gameStore.activeLayout?.fretboard?.stringKeys?.length
      ? gameStore.activeLayout
      : selectedInstrument?.pluckStrings && wheelLayout
        ? gameStore.getLinkedFretLayout(selectedInstrument, wheelLayout.id)
        : null,
  );
  const guitarStringKeys = $derived(fretLayout?.fretboard?.stringKeys ?? []);
  const needsModeSync = $derived(modeRead && syncedMode === null);
  let chordWheelRef = $state<{
    pressString: (source: string, stringIndex: number) => number | null;
    releaseString: (source: string) => void;
    clearHeldStrings: () => void;
    strum: (direction: 'up' | 'down') => boolean;
    stepWedge: (delta: -1 | 1) => boolean;
    stepPage: (delta: -1 | 1) => boolean;
    toggleMixedMode: () => boolean;
    toggleTimbreMode: () => boolean;
  }>();
  let guitarPreviewPositions = $state.raw<GuitarPosition[]>([]);
  let guitarPreviewChord = $state<string | null>(null);
  const wheelItems = $derived(fretMode ? [
    ...Array.from({ length: openSlot }, (_, index) => ({ name: index < fretCount ? fretRomanNumeral(index + 1) : '', pitchClasses: [] })),
    { name: t('instrument.guitarOpen'), pitchClasses: [] },
  ] : guitarMode === 'chords' ? selectedInstrument?.chords ?? [] : []);

  let supportsPedals = $derived(
    selectedInstrument ? selectedInstrument.supportsSustain : true
  );
  const pedalsActive = $derived(supportsPedals && playbackStore.pedals.enabled);

  // ==========================================
  // Autoplay / Scheduled Playback (Lower Right)
  // ==========================================
  let scheduleTrigger: HTMLButtonElement;
  let schedulePopup: HTMLDivElement;
  let scheduleOpen = $state(false);
  const schedulePopupId = $props.id();
  const scheduleDropdown = createDropdownState();
  const scheduledAction = $derived(scheduledPlaybackStore.active || scheduledPlaybackStore.executing
    ? scheduledPlaybackStore.action : playbackStore.isPlaying ? 'pause' : 'play');

  function positionSchedulePopup() {
    if (!scheduleOpen) return;
    const rect = scheduleTrigger.getBoundingClientRect();
    const row = scheduleTrigger.closest('.inst-bottom-bar')!.getBoundingClientRect();
    schedulePopup.style.bottom = `${window.innerHeight - row.top + 8}px`;
    const popupWidth = parseFloat(getComputedStyle(schedulePopup).width) || 0;
    const maxLeft = Math.max(8, window.innerWidth - popupWidth - 8);
    schedulePopup.style.left = `${Math.min(maxLeft, Math.max(8, rect.left + (rect.width - popupWidth) / 2))}px`;
    schedulePopup.style.maxHeight = `${Math.max(0, row.top - 16)}px`;
  }

  function trackScheduleAnchor(node: HTMLElement) {
    const resize = createFrameResizeObserver(positionSchedulePopup);
    resize.observe(node);
    return { destroy: () => resize.disconnect() };
  }

  function handleScheduleToggle(event: ToggleEvent) {
    scheduleOpen = event.newState === 'open';
    scheduleDropdown.isOpen = scheduleOpen;
    positionSchedulePopup();
  }

  function handleScheduleKeyDown(event: KeyboardEvent) {
    // Keep Space on these controls from reaching the app's playback shortcut.
    if (event.code === 'Space') event.stopPropagation();
  }

  $effect(() => {
    if (scheduleOpen && (!scheduleDropdown.isOpen || uiStore.activeTab !== 'instrument'
      || uiStore.activeDialog || uiStore.gameSelectorOpen)) schedulePopup.hidePopover();
  });

  function startStopScheduledTimer() {
    if (scheduledPlaybackStore.active) scheduledPlaybackStore.cancel();
    else scheduledPlaybackStore.arm();
  }

  function formatCountdown(sec: number): string {
    const h = Math.floor(sec / 3600);
    const m = Math.floor((sec % 3600) / 60);
    const s = sec % 60;
    if (h > 0) {
      return `${h}h ${m}m ${s.toString().padStart(2, '0')}s`;
    }
    return `${m}:${s.toString().padStart(2, '0')}`;
  }

  // ==========================================
  // Chords and Pedals (Lower Left)
  // ==========================================

  // ==========================================
  // Physical MIDI Input (Lower Right)
  // ==========================================
  const midiInputs = $derived([
    { value: '', label: t('instrument.noMidiDevice') },
    ...midiInputStore.state.ports.map(port => ({ value: port.id, label: port.name })),
    ...(midiInputStore.state.selectedPortId && !midiInputStore.state.ports.some(port => port.id === midiInputStore.state.selectedPortId)
      ? [{ value: midiInputStore.state.selectedPortId, label: t('instrument.midiDeviceUnavailable') }] : []),
  ]);
  const midiInputLabel = $derived(midiInputs.find(option => option.value === (midiInputStore.state.selectedPortId ?? ''))?.label ?? t('instrument.noMidiDevice'));
  const midiInputActive = $derived(Boolean(midiInputStore.state.selectedPortId)
    && midiInputStore.state.connected && midiInputStore.state.active);
  let midiDropdownWidth = $state(0);
  let inputPitches = $state(new Set<number>());
  const instrumentInput = new InstrumentInput(pitches => {
    inputPitches = pitches;
    updateHighlights();
  });
  const inputEnabled = $derived(uiStore.activeTab === 'instrument'
    && !uiStore.activeDialog && !uiStore.gameSelectorOpen && !scheduleOpen);

  onDestroy(() => {
    instrumentInput.clear();
    chordWheelRef?.clearHeldStrings();
    pitchesToShow.clear();
    actionsToShow.clear();
    for (const timer of previewTimers.values()) clearTimeout(timer);
  });

  // ==========================================
  // Layout Definitions & Active Note Glow
  // ==========================================
  let activePitches = $derived(new Set([...playbackStore.activeNotes.map(n => n.pitch), ...inputPitches, ...midiInputStore.state.pitches]));

  const selectedGameLayout = $derived(
    gameStore.activeGame?.layouts?.find(layout => layout.id === selectedLayoutId) ?? null
  );
  const instrumentNotes = $derived(gameStore.activeInputMode === 'frets' || mixed || (gameStore.activeInputMode === 'chords' && selectedInstrument?.pluckStrings)
    ? guitarNotes(selectedInstrument) : getInstrumentNotes(selectedInstrument));
  const layoutStyle = $derived(instrumentLayoutStyle(selectedInstrument));
  const isGuitarStage = $derived(layoutStyle === 'guitar' && Boolean(selectedInstrument) && gameStore.activeInputMode !== 'keyboard');
  const mappedRows = $derived(instrumentKeyRows(selectedInstrument, selectedGameLayout));
  const mappedKeys = $derived(mappedRows.flat().filter(key => key !== null));

  // ==========================================
  // Piano strip follows the selected instrument's playable notes.
  // ==========================================

  const pianoSlots = $derived(createPianoSlots(instrumentNotes));
  const pianoRows = $derived(createPianoRows(pianoSlots, pianoAvailableWidth, {
    height: pianoAvailableHeight, stageWidth: pianoStageWidth,
  }));
  const pianoRowColumns = $derived(Math.max(1, ...pianoRows.map(row => row.length)));
  const pianoRangeLabel = $derived(pianoRange(instrumentNotes));
  const stageMaxWidth = $derived(isGuitarStage ? 1440 : Math.max(1280, Math.min(1680, pianoSlots.length * 56 + 26)));
  const gameBindings = $derived(new Map(mappedKeys.map(key => [key.pitch, key.label])));

  let manuallyActivePitches = $state(new Set<number>());
  const litPitches = $derived(new Set([...activePitches, ...manuallyActivePitches]));
  const previewTimers = new Map<number, ReturnType<typeof setTimeout>>();
  let highlightedPitches = $state<ReadonlySet<number>>(new Set());
  const pitchesToShow = new PressHighlights<number>(pitch => pitch, values => { highlightedPitches = new Set(values); });
  const actionsToShow = new PressHighlights<ArrangementState>(
    action => `${action.mode}/${action.page}/${action.slot}/${action.strings}`,
    values => { highlightedActions = values; },
  );
  const activeSlots = $derived(new Set(highlightedActions
    .filter(action => action.mode === guitarMode && action.slot !== null)
    .map(action => action.slot!)));

  function updateHighlights() {
    if (uiStore.activeTab !== 'instrument' || playbackStore.isScrubbing) return;
    pitchesToShow.update(litPitches);
    const action = playbackStore.arrangement;
    actionsToShow.update(performing && action?.slot != null ? [action] : []);
  }

  // Capture backend snapshots even when a press and release arrive before Svelte paints.
  onMount(() => playbackStore.onInstrumentVisualUpdate(updateHighlights));
  $effect(() => {
    uiStore.activeTab; gameStore.activeGame?.id; selectedInstrumentId; selectedLayoutId;
    gameStore.activeInputMode; playbackStore.activeSong?.filePath; playbackStore.isListenMode; playbackStore.isScrubbing;
    return () => {
      pitchesToShow.clear();
      actionsToShow.clear();
    };
  });
  $effect(updateHighlights);

  // Clear held input when navigation, an overlay, or the displayed mapping changes.
  $effect(() => {
    inputEnabled; gameStore.activeGame?.id; selectedInstrumentId; selectedLayoutId; gameStore.activeInputMode;
    untrack(() => {
      instrumentInput.clear();
      chordWheelRef?.clearHeldStrings();
    });
  });

  function handleKeyDown(event: KeyboardEvent) {
    if (!inputEnabled || event.defaultPrevented || event.isComposing
      || event.ctrlKey || event.altKey || event.metaKey) return;
    const target = event.target;
    if (target instanceof HTMLElement && target.closest(
      'input, textarea, select, [contenteditable]:not([contenteditable="false"]), [role="dialog"], [role="menu"], [role="listbox"], [role="slider"]',
    )) return;
    if ((event.key === 'Enter' || event.key === ' ') && target instanceof HTMLElement
      && target.closest('button, a')) return;
    const source = `keyboard:${event.code || event.key}`;

    if (isGuitarStage && selectedInstrument && displayWheel && chordWheelRef) {
      const inDropdown = target instanceof HTMLElement && Boolean(target.closest('.fluent-dropdown'));
      const inHorizontalNav = target instanceof HTMLElement
        && Boolean(target.closest('.fluent-dropdown, [role="tablist"], [role="tab"], .fret-scroll'));
      const guitarAction = resolveGuitarKeyAction(event.key, {
        stringKeys: guitarStringKeys,
        strumButtons: displayWheel.strumButtons,
        pageKeys: displayWheel.pageKeys,
        modeKey: displayWheel.modeKey,
        timbreKey: displayWheel.timbreKey,
        allowPageKeys: paged && displayWheel.pageCount > 1 && !needsModeSync,
        allowModeKey: mixed,
        allowTimbreKey: Boolean(displayWheel.timbreKey),
        allowArrows: !inDropdown,
        allowWedgeArrows: !inHorizontalNav,
      });
      if (!guitarAction) return;
      event.preventDefault();
      if (event.repeat) return;
      switch (guitarAction.type) {
        case 'string': {
          const pitch = chordWheelRef.pressString(source, guitarAction.index);
          if (pitch !== null) {
            if (instrumentInput.press(source, [pitch])) previewNote(pitch);
            else chordWheelRef.releaseString(source);
          }
          break;
        }
        case 'strum':
          chordWheelRef.strum(guitarAction.direction);
          break;
        case 'page':
          chordWheelRef.stepPage(guitarAction.delta);
          break;
        case 'wedge':
          chordWheelRef.stepWedge(guitarAction.delta);
          break;
        case 'mode':
          chordWheelRef.toggleMixedMode();
          break;
        case 'timbre':
          chordWheelRef.toggleTimbreMode();
          break;
      }
      return;
    }

    if (gameStore.activeInputMode === 'chords' && chordMode === 'pads' && selectedInstrument) {
      const pitches = chordPadPitches(event.key, selectedInstrument.chords ?? [], gameStore.activeLayout?.chordKeys ?? []);
      if (!pitches.length) return;
      event.preventDefault();
      if (!event.repeat && instrumentInput.press(source, pitches)) {
        pitches.forEach(previewNote);
      }
      return;
    }

    const bindings = mappedKeys;
    const pitches = keyboardPitches(event.key, bindings);
    if (!pitches.length) return;
    event.preventDefault();
    if (!event.repeat && instrumentInput.press(source, pitches)) {
      pitches.forEach(previewNote);
    }
  }

  function handleKeyUp(event: KeyboardEvent) {
    const source = `keyboard:${event.code || event.key}`;
    instrumentInput.release(source);
    chordWheelRef?.releaseString(source);
  }

  function clearInput() {
    instrumentInput.clear();
    chordWheelRef?.clearHeldStrings();
  }

  function previewNote(pitch: number) {
    // Manual interaction previews audio even while playback is in game mode.
    // Sending simulated keystrokes here would feed keyboard input back into this view.
    api.games.tapNotePreview(pitch, true).catch(err => {
      console.warn('[InstrumentView] triggerNote failed:', err);
    });
  }

  function triggerKey(pitch: number) {
    clearTimeout(previewTimers.get(pitch));
    manuallyActivePitches.add(pitch);
    manuallyActivePitches = new Set(manuallyActivePitches);
    previewTimers.set(pitch, setTimeout(() => {
      manuallyActivePitches.delete(pitch);
      manuallyActivePitches = new Set(manuallyActivePitches);
      previewTimers.delete(pitch);
    }, 180));
    previewNote(pitch);
  }

  function isInteractiveStageTarget(target: EventTarget | null): boolean {
    return target instanceof Element && Boolean(target.closest(
      'button, a, input, select, textarea, [role="button"], [role="tab"], [role="menuitem"], [role="option"], [role="listbox"], .fluent-dropdown',
    ));
  }

  function handleStageClick(event: MouseEvent) {
    if (!inputEnabled || !isGuitarStage || !chordWheelRef || event.defaultPrevented || event.button !== 0) return;
    if (isInteractiveStageTarget(event.target) || document.querySelector('[role="listbox"]')) return;
    if (window.getSelection()?.toString()) return;
    chordWheelRef.strum('up');
  }

  function handleStageContextMenu(event: MouseEvent) {
    if (!inputEnabled || !isGuitarStage || !chordWheelRef || event.defaultPrevented) return;
    if (isInteractiveStageTarget(event.target) || document.querySelector('[role="listbox"]')) return;
    event.preventDefault();
    chordWheelRef.strum('down');
  }
</script>

<svelte:window onkeydown={handleKeyDown} onkeyup={handleKeyUp} onblur={clearInput} onresize={positionSchedulePopup} />
<svelte:document onvisibilitychange={clearInput} />

{#snippet topLabel(label: string)}
  <span class="top-control-label" aria-hidden="true">
    <!-- Mount with the measured visibility so entering the view does not trigger a reveal. -->
    {#if topBarWidth > 0}
      <GradientSweep visible={showTopLabels} anchor="right" overflowVisibleOnComplete={false}>
        <span class="top-label-text">{label}</span>
      </GradientSweep>
    {/if}
  </span>
{/snippet}

{#snippet bottomLabel(label: string)}
  <span class="bottom-control-label" aria-hidden="true">
    <!-- Mount with the measured visibility so entering the view does not trigger a reveal. -->
    {#if bottomBarWidth > 0}
      <GradientSweep visible={showBottomLabels} collapseMargin="left" marginAmount="6px" overflowVisibleOnComplete={false}>
        {label}
      </GradientSweep>
    {/if}
  </span>
{/snippet}

{#snippet pianoKeyboard(slots: PianoSlot[], showBindings = false)}
  <div class="piano-keyboard-strip" style:--white-key-count={pianoRowColumns} style:--row-key-count={slots.length}>
    {#each slots as slot}
      <div class="piano-key-slot" class:sharp-only={!slot.white}>
        {#if slot.white}
        {@const white = slot.white}
        {@const isWhiteLit = highlightedPitches.has(white.pitch)}
        <button
          type="button"
          class="piano-key-white"
          class:active={isWhiteLit}
          onclick={() => triggerKey(white.pitch)}
          title={white.name}
        >
          {#if showBindings}<span class="piano-key-binding">{gameBindings.get(white.pitch)}</span>{/if}
          <span class="piano-key-label" class:c-label={white.isC}>{white.name}</span>
        </button>
        {/if}

        {#if slot.sharp}
          {@const isBlackLit = highlightedPitches.has(slot.sharp.pitch)}
          <button
            type="button"
            class="piano-key-black"
            class:active={isBlackLit}
            onclick={(e) => { e.stopPropagation(); triggerKey(slot.sharp!.pitch); }}
            title={slot.sharp.name}
          >
            {#if showBindings}<span class="piano-key-binding">{gameBindings.get(slot.sharp.pitch)}</span>{/if}
            <span class="piano-black-label">{slot.sharp.name}</span>
          </button>
        {/if}
      </div>
    {/each}
  </div>
{/snippet}

<div class="instrument-view">
  <!-- Top Bar -->
  <div class="inst-top-bar" bind:this={topBar} bind:clientWidth={topBarWidth}>
    <!-- Top Left: Game Info Badge with Game Name & Ghost Dropdowns -->
    <div class="game-info-badge">
      <button type="button" class="inst-game-avatar-wrap game-selector-trigger" onclick={toggleGameSelector} aria-label={t('gameSelector.selectTargetGame')} aria-haspopup="dialog" aria-expanded={uiStore.gameSelectorOpen && uiStore.gameSelectorAnchor === topBar} use:tooltip={gameStore.gameDeletionTooltip} data-tooltip-follow="false">
        <GameIcon size={62} borderRadius="var(--radius-md)" placeholderSize={24} objectFit="contain" GrayscaleInactiveGame={false} />
      </button>

      <div class="inst-game-meta">
        <div class="inst-game-title-row">
          <button type="button" class="inst-game-name game-selector-trigger" onclick={toggleGameSelector} aria-label={t('gameSelector.selectTargetGame')} aria-haspopup="dialog" aria-expanded={uiStore.gameSelectorOpen && uiStore.gameSelectorAnchor === topBar} use:tooltip={gameStore.gameDeletionTooltip} data-tooltip-follow="false"><AdaptiveText text={gameStore.activeGame?.displayName || gameStore.activeGame?.name || 'Game'} compactText={gameStore.activeGame?.shortName} /></button>
        </div>
        <div class="inst-ghost-pickers">
          <Dropdown
            variant="ghost"
            options={instrumentOptions}
            placeholder={gameStore.isInstrumentMissing ? 'Missing Instrument' : 'No Instruments'}
            tooltipText={gameStore.instrumentDeletionTooltip}
            disabled={!gameStore.activeGame?.instruments.length}
            showTooltips={false}
            bind:value={selectedInstrumentId}
            onchange={(id) => gameStore.syncActiveInstrumentMapping(id)}
            class="ghost-instrument-dropdown"
          />
          <Dropdown
            variant="ghost"
            options={layoutOptions}
            placeholder={gameStore.isLayoutMissing ? 'Missing Layout' : 'No Layouts'}
            tooltipText={gameStore.layoutDeletionTooltip}
            disabled={layoutOptions.length === 0}
            showTooltips={false}
            bind:value={selectedLayoutId}
            onchange={(id) => gameStore.syncActiveInstrumentMapping(undefined, id)}
            class="ghost-layout-dropdown"
          />
        </div>
      </div>
    </div>

    <!-- Top Right: Stage Selector (top) & Input Mode Selector (below) -->
    <div class="inst-top-controls">
      <div class="top-control-row">
        {@render topLabel(t('instrument.view'))}
        <div class="stage-selector" role="group" aria-label="Instrument views">
          {#if hasInGameLayout}
            <button type="button" aria-pressed={showGame} onclick={() => selectStage('game')}>
              <FluentIcon name="Keyboard" size={13} />
              <span>{t('instrument.inGameView')}</span>
            </button>
            <button type="button" aria-pressed={showPiano} onclick={() => selectStage('piano')}>
              <FluentIcon name={isGuitarStage ? 'Guitar' : 'MusicNote2'} size={13} />
              <span>{isGuitarStage ? t('instrument.guitarView') : t('instrument.pianoView')}</span>
            </button>
          {:else}
            <span class="stage-label">
              <FluentIcon name="MusicNote2" size={13} />
              <span>{t('instrument.pianoView')}</span>
            </span>
          {/if}
        </div>
      </div>

      <GradientSweep visible={showInputModes} direction="vertical" collapseMargin="top" marginAmount="6px" overflowVisibleOnComplete={false}>
        <div class="top-control-row input-modes-sweep" inert={!showInputModes}>
          {#if displayInputModes.length > 1}
            {@render topLabel(t('instrument.mode'))}
            <SlidingTabs
              variant="pill"
              label={t('instrument.inputMode')}
              value={displayInputMode}
              disabled={!showInputModes || modePending || playbackStore.isPlaying}
              onselect={id => selectInputMode(id as InstrumentInputMode)}
              tabs={displayInputModes.map(mode => ({
                id: mode,
                label: t(`instrument.guitarMode_${mode}`),
              }))}
            />
          {/if}
        </div>
      </GradientSweep>
    </div>
  </div>

  <!-- Main Instrument Display Area -->
  <div
    class="inst-stage-viewer"
    role="presentation"
    onclick={handleStageClick}
    oncontextmenu={handleStageContextMenu}
    use:fitInstrumentStage={{
      stage: selectedStage,
      gameSelected,
      pianoSelected,
      hasGame: hasInGameLayout,
      duration: animationStore.durationMs,
      instrumentId: selectedInstrumentId,
      layoutId: selectedLayoutId,
      inputMode: gameStore.activeInputMode,
    }}
  >
    <div class="inst-stage" style:--stage-max-width={`${stageMaxWidth}px`}>
    <div class="stage-slot game-slot" inert={!showGame}>
    <div class="game-stage">
    {#if hasInGameLayout}
      {#if gameStore.activeInputMode === 'chords' && chordMode === 'pads' && selectedInstrument}
        <ChordPadsLayout chords={selectedInstrument.chords ?? []} bindings={gameStore.activeLayout?.chordKeys ?? []} activePitches={litPitches} dispatchedPads={displayingDispatch ? activeSlots : undefined} onpreview={pitches => pitches.forEach(triggerKey)} />
      {:else if isGuitarStage && selectedInstrument && displayWheel}
        <ChordWheelControls
          bind:this={chordWheelRef}
          wheel={displayWheel}
          pads={wheelItems}
          instrument={selectedInstrument}
          {fretMode}
          {paged}
          {mixed}
          {needsModeSync}
          {physicalMode}
          onsyncmode={syncMode}
          performing={displayingDispatch}
          action={displayingDispatch ? displayedAction : null}
          attacks={highlightedActions}
          activePitches={litPitches}
          {activeSlots}
          stringKeys={guitarStringKeys}
          onpreview={pitches => pitches.forEach(triggerKey)}
          onpreviewpositions={(positions, chord) => {
            guitarPreviewPositions = positions;
            guitarPreviewChord = chord;
          }}
        />
      {:else if !isGuitarStage}
        <InstrumentLayout {layoutStyle} rows={mappedRows} activePitches={highlightedPitches} onplay={triggerKey} />
      {/if}
    {/if}

    </div>
    </div>
    <!-- Bottom Instrument Strip (Guitar Fretboard in guitar mode, Chromatic Piano Strip otherwise) -->
    <div class="stage-slot piano-slot" inert={!showPiano}>
    <div class="piano-strip-section">
      {#if isGuitarStage && selectedInstrument}
        <GuitarLayout
          instrument={selectedInstrument}
          mode={guitarMode}
          performing={displayingDispatch}
          action={playbackStore.arrangement}
          attacks={highlightedActions}
          activePitches={litPitches}
          accessibleChords={selectedInstrument.pagedChords === false ? wheel?.positions.length : undefined}
          previewPositions={guitarPreviewPositions}
          previewChord={guitarPreviewChord}
        />
      {:else}
        <div class="piano-strip-header">
          <div class="piano-strip-title-row">
            <FluentIcon name="MusicNote2" size={13} class="piano-strip-icon" />
            <span class="piano-strip-title">{t('instrument.pianoStrip')}</span>
            {#if pianoRangeLabel}<span class="piano-strip-range-badge">{pianoRangeLabel}</span>{/if}
          </div>

        </div>

        <div class="piano-rows">
          {#each pianoRows as row}
            {@render pianoKeyboard(row, !hasInGameLayout)}
          {/each}
        </div>
      {/if}
    </div>
    </div>
    </div>
  </div>

  <!-- Bottom Bar: Chords & Pedals (Lower Left) | Scheduling & MIDI Input (Lower Right) -->
  <div class="inst-bottom-bar" bind:clientWidth={bottomBarWidth} use:toastOffset>
    <!-- Lower Left: Chords and Pedals Toggles -->
    <div class="bottom-left-controls">
      <!-- Chord Strum Tolerance -->
      <GradientSweep visible={supportsChords} collapseMargin="left" marginAmount="8px">
        <div
          class="control-unit chord-tolerance-unit"
          inert={!supportsChords}
          use:tooltip={t('instrument.chordTolerance')}
        >
          <span class="chord-tolerance-label">
            <ChordIcon size={16} filled={supportsChords} isActive={supportsChords} />
            {@render bottomLabel(t('instrument.chordTolerance'))}
          </span>
          <div class="tolerance-box">
            <NumberBox
              value={playbackStore.chordDetectionMilliseconds}
              onchange={(value) => playbackStore.setChordDetectionMilliseconds(value)}
              min={0}
              max={500}
              step={5}
              unit="ms"
              label={t('instrument.chordTolerance')}
            />
          </div>
        </div>
      </GradientSweep>

      <!-- Pedals toggle and bindings -->
      <div class="control-unit">
        <button
          type="button"
          class="instrument-toggle-btn pedal-toggle-btn"
          class:enabled={pedalsActive}
          disabled={!supportsPedals}
          aria-label={t('instrument.pedals')}
          aria-pressed={pedalsActive}
          onclick={() => playbackStore.togglePedals()}
          use:tooltip={supportsPedals ? t('instrument.pedalBindings') : t('instrument.noPedalSupport')}
        >
          <PedalIcon
            size={15}
            isActive={pedalsActive}
            isSoftActive={playbackStore.pedals.unaCorda}
            isSostenutoActive={playbackStore.pedals.sostenuto}
            isSustainActive={playbackStore.pedals.sustain}
            pressedColor="#1db954"
          />
          {@render bottomLabel(t('instrument.pedals'))}
        </button>

        <button
          type="button"
          class="pedal-settings-icon-btn theme-ghost-button"
          disabled={!supportsPedals || !gameStore.activeLayout}
          onclick={() => uiStore.openDialog('pedalSettings')}
          aria-label={t('instrument.pedalSettings')}
          use:tooltip={t('instrument.pedalSettings')}
        >
          <FluentIcon name="Settings" size={13} />
        </button>
      </div>
    </div>

    <!-- Lower Right: Scheduled Playback & MIDI Input -->
    <div class="bottom-right-controls" use:trackScheduleAnchor>
      <!-- Scheduled Playback -->
      <button
        type="button"
        class="instrument-toggle-btn scheduled-playback-btn"
        class:enabled={scheduleOpen || scheduledPlaybackStore.active || scheduledPlaybackStore.executing}
        bind:this={scheduleTrigger}
        popovertarget={schedulePopupId}
        aria-label={t('instrument.scheduledPlayback')}
        aria-haspopup="dialog"
        aria-expanded={scheduleOpen}
        aria-controls={schedulePopupId}
        onkeydown={handleScheduleKeyDown}
        use:tooltip={t('instrument.scheduledPlayback')}
      >
        <FluentIcon name="Clock" size={16} filled={scheduledPlaybackStore.active || scheduledPlaybackStore.executing} />
        {@render bottomLabel(t('instrument.schedule'))}
      </button>
      <div
        class="scheduled-playback-popup"
        bind:this={schedulePopup}
        id={schedulePopupId}
        popover="auto"
        role="dialog"
        tabindex="-1"
        aria-label={t('instrument.scheduledPlayback')}
        onbeforetoggle={handleScheduleToggle}
        ontoggle={event => {
          if (event.newState === 'open') {
            positionSchedulePopup();
            schedulePopup.querySelector<HTMLElement>('input:not(:disabled), button:not(:disabled)')?.focus();
          }
        }}
        onkeydown={handleScheduleKeyDown}
        use:scheduleDropdown.layer
        use:trackScheduleAnchor
      >
        <div class="sched-header">
          <FluentIcon name="Clock" size={13} class="sched-icon" />
          <span class="sched-title">{t('instrument.scheduledPlayback')}</span>
          {#if !scheduledPlaybackStore.active}
            <span class="sched-info">
              {t(scheduledAction === 'play' ? 'instrument.schedulePlay' : 'instrument.schedulePause')} {scheduledPlaybackStore.hour.toString().padStart(2, '0')}:{scheduledPlaybackStore.minute.toString().padStart(2, '0')}
            </span>
          {/if}
        </div>

        <div class="sched-controls-row">
          <div class="time-inputs-group">
            <div class="time-box">
              <NumberBox
                bind:value={scheduledPlaybackStore.hour}
                min={0}
                max={23}
                step={1}
                disabled={scheduledPlaybackStore.active || scheduledPlaybackStore.executing}
              />
            </div>
            <span class="time-colon">:</span>
            <div class="time-box">
              <NumberBox
                bind:value={scheduledPlaybackStore.minute}
                min={0}
                max={59}
                step={1}
                disabled={scheduledPlaybackStore.active || scheduledPlaybackStore.executing}
              />
            </div>
          </div>

          <Button
            variant="secondary"
            compact
            disabled={scheduledPlaybackStore.active || scheduledPlaybackStore.executing}
            onclick={() => scheduledPlaybackStore.setToNow()}
            title={t('instrument.setToNow')}
          >
            {t('instrument.now')}
          </Button>

          <Button
            variant={scheduledPlaybackStore.active ? 'danger' : 'accent'}
            compact
            onclick={startStopScheduledTimer}
            disabled={scheduledPlaybackStore.executing}
          >
            {scheduledPlaybackStore.active ? t('instrument.cancelTimer') : t('instrument.startTimer')}
          </Button>
        </div>

        {#if scheduledPlaybackStore.active}
          <div class="sched-status-row">
            <span class="sched-countdown">
              <span class="sched-pulse-dot"></span>
              {t(scheduledAction === 'play' ? 'instrument.playsIn' : 'instrument.pausesIn')}: {formatCountdown(scheduledPlaybackStore.remainingSeconds)}
            </span>
          </div>
        {/if}
      </div>

      <div class="midi-input-group">
        <div class="midi-icon-label" class:connected={midiInputActive} use:tooltip={t('instrument.physicalMidiDevice')}>
          <FluentIcon name="Keyboard" size={15} class="midi-plug-icon" />
          {@render bottomLabel(t('instrument.midiInput'))}
        </div>

        <div class="midi-dropdown-wrap" style:width={midiDropdownWidth ? `${midiDropdownWidth}px` : 'auto'}>
          <span class="midi-dropdown-sizer" aria-hidden="true">
            <span class="midi-dropdown-measure" bind:clientWidth={midiDropdownWidth}>
              <span>{midiInputLabel}</span>
              <FluentIcon name="ChevronDown" size={11} />
            </span>
          </span>
          <Dropdown
            options={midiInputs}
            variant="ghost"
            showTooltips={false}
            value={midiInputStore.state.selectedPortId ?? ''}
            onchange={(value) => midiInputStore.select(String(value) || null)}
            disabled={midiInputStore.busy}
            ariaLabel={t('instrument.midiInput')}
            placeholder={t('instrument.noMidiDevice')}
          />
        </div>

        <button
          type="button"
          class="midi-refresh-btn theme-ghost-button"
          disabled={midiInputStore.busy}
          aria-label={t('instrument.refreshDevices')}
          onclick={() => midiInputStore.refresh()}
          use:tooltip={t('instrument.refreshDevices')}
        >
          <RefreshIcon loading={midiInputStore.refreshing} size={15} />
        </button>
      </div>
    </div>
  </div>
</div>

<style>
  .instrument-view {
    display: flex;
    flex-direction: column;
    height: 100%;
    width: 100%;
    overflow: hidden;
    background: transparent;
  }

  /* ==========================================
     Top Bar
     ========================================== */
  .inst-top-bar {
    display: flex;
    align-items: center;
    justify-content: space-between;
    flex-wrap: wrap;
    padding: 10px 20px;
    background: var(--bg-secondary);
    border: none;
    gap: 16px;
    flex-shrink: 0;
  }

  .inst-top-controls {
    display: flex;
    flex-direction: column;
    align-items: flex-end;
    justify-content: center;
    margin-left: auto;
    flex-shrink: 0;
    min-width: 0;
  }

  .top-control-row {
    display: flex;
    align-items: center;
    justify-content: flex-end;
  }

  .top-control-label {
    display: inline-flex;
    align-items: center;
    white-space: nowrap;
    color: var(--text-secondary);
    font-size: 11px;
    font-weight: 500;
    flex-shrink: 0;
  }

  .input-modes-sweep {
    width: 100%;
  }

  .top-label-text {
    padding-right: 8px;
  }

  .input-modes-sweep :global(.sliding-tabs.pill .tab-btn) {
    padding: 4px 10px;
    font-size: 11px;
    line-height: 14px;
  }

  .game-info-badge {
    display: flex;
    align-items: center;
    gap: 12px;
    min-width: 0;
  }

  .inst-game-avatar-wrap {
    width: 62px;
    height: 62px;
    border-radius: var(--radius-md);
    overflow: hidden;
    flex-shrink: 0;
    display: flex;
    align-items: center;
    justify-content: center;
  }



  .inst-game-meta {
    min-width: 0;
    display: flex;
    flex-direction: column;
    justify-content: center;
    gap: 3px;
  }

  .inst-game-title-row {
    display: flex;
    align-items: center;
    gap: 8px;
  }

  .inst-game-name {
    min-width: 0;
    max-width: 100%;
    font-size: 15px;
    font-weight: 700;
    color: var(--text-primary);
    line-height: 1.25;
    padding: 0;
    margin: 0;
    letter-spacing: -0.01em;
  }

  .game-selector-trigger {
    border: none;
    padding: 0;
    background: transparent;
    font-family: inherit;
    text-align: left;
    cursor: pointer;
  }

  .game-selector-trigger:focus-visible {
    outline: 2px solid var(--accent);
    outline-offset: 4px;
  }

  .inst-ghost-pickers {
    display: flex;
    flex-direction: column;
    align-items: flex-start;
    gap: 1.5px;
  }

  .inst-ghost-pickers :global(.fluent-dropdown) {
    margin-left: 0;
  }

  :global(.ghost-instrument-dropdown .trigger-label) {
    color: var(--text-secondary);
    font-weight: 500;
    font-size: 11.5px;
    line-height: 1.25;
    opacity: 0.85;
    transition: opacity var(--transition-fast, 0.15s ease), color var(--transition-fast, 0.15s ease);
  }

  :global(.ghost-instrument-dropdown .chevron-icon) {
    color: var(--text-secondary);
    opacity: 0.7;
    transition: transform var(--transition-normal), opacity var(--transition-fast, 0.15s ease), color var(--transition-fast, 0.15s ease);
  }

  :global(.ghost-instrument-dropdown:hover .trigger-label),
  :global(.ghost-instrument-dropdown:hover .chevron-icon) {
    opacity: 1;
    color: var(--text-primary);
  }

  :global(.ghost-layout-dropdown .trigger-label) {
    color: var(--text-secondary);
    font-size: 11px;
    line-height: 1.25;
    opacity: 0.7;
    transition: opacity var(--transition-fast, 0.15s ease), color var(--transition-fast, 0.15s ease);
  }

  :global(.ghost-layout-dropdown .chevron-icon) {
    color: var(--text-secondary);
    opacity: 0.7;
    transition: transform var(--transition-normal), opacity var(--transition-fast, 0.15s ease), color var(--transition-fast, 0.15s ease);
  }

  :global(.ghost-layout-dropdown:hover .trigger-label),
  :global(.ghost-layout-dropdown:hover .chevron-icon) {
    opacity: 1;
    color: var(--text-primary);
  }

  /* Scheduled Playback Popup */
  .scheduled-playback-btn {
    gap: 0;
  }

  .scheduled-playback-popup {
    position: fixed;
    inset: auto;
    margin: 0;
    width: max-content;
    max-width: calc(100vw - 16px);
    box-sizing: border-box;
    flex-direction: column;
    gap: 8px;
    background: var(--bg-dialog, var(--bg-card));
    color: var(--text-primary);
    border-radius: var(--radius-md);
    padding: 12px;
    border: none;
    box-shadow: var(--popup-shadow);
    overflow: auto;
    opacity: 0;
    transform: translateY(4px) scale(0.96);
    transform-origin: bottom center;
    transition: opacity var(--transition-fast), transform var(--transition-fast),
      display var(--anim-fast, 0.15s) allow-discrete, overlay var(--anim-fast, 0.15s) allow-discrete;
  }

  .scheduled-playback-popup:popover-open {
    display: flex;
    opacity: 1;
    transform: translateY(0) scale(1);
  }

  @starting-style {
    .scheduled-playback-popup:popover-open {
      opacity: 0;
      transform: translateY(4px) scale(0.96);
    }
  }

  @media (prefers-reduced-motion: reduce) {
    .scheduled-playback-popup { transition: none; }
  }

  .sched-header {
    display: flex;
    align-items: center;
    gap: 6px;
  }

  :global(.sched-icon) {
    color: var(--accent);
  }

  .sched-title {
    font-size: 11px;
    font-weight: 600;
    text-transform: uppercase;
    letter-spacing: 0.04em;
    color: var(--text-tertiary);
  }

  .sched-controls-row {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: 8px;
  }

  .time-inputs-group {
    display: flex;
    align-items: center;
    gap: 4px;
  }

  .time-box {
    width: 52px;
  }

  .time-box :global(.number-box) {
    width: 100%;
    padding: 0 2px 0 6px;
    height: 28px;
  }

  .time-box :global(.num-input) {
    width: 24px;
    font-size: 12px;
    font-weight: 600;
    text-align: center;
  }

  .time-colon {
    font-size: 13px;
    font-weight: 700;
    color: var(--text-secondary);
  }

  .sched-status-row {
    display: flex;
    align-items: center;
  }

  .sched-info {
    margin-left: auto;
    text-align: right;
    white-space: nowrap;
    font-size: 10.5px;
    color: var(--text-tertiary);
  }

  .sched-countdown {
    display: flex;
    align-items: center;
    gap: 6px;
    font-size: 11px;
    font-weight: 600;
    color: var(--status-warning, #f59e0b);
  }

  .sched-pulse-dot {
    width: 6px;
    height: 6px;
    border-radius: 50%;
    background: var(--status-warning, #f59e0b);
    animation: pulseDot 1s infinite alternate ease-in-out;
  }

  @keyframes pulseDot {
    0% { transform: scale(0.8); opacity: 0.5; }
    100% { transform: scale(1.2); opacity: 1; }
  }

  /* ==========================================
     Main Instrument Display Stage
     ========================================== */
  .inst-stage-viewer {
    flex: 1;
    min-height: 0;
    min-width: 0;
    width: 100%;
    position: relative;
    overflow: hidden;
    box-sizing: border-box;
    padding: 20px;
    mask-image: linear-gradient(to bottom,
      transparent, black 28px, black calc(100% - 28px), transparent);
    display: flex;
    align-items: center;
    justify-content: center;
  }

  .inst-stage {
    display: flex;
    flex-direction: column;
    align-items: center;
    width: clamp(720px, 100%, var(--stage-max-width));
    flex-shrink: 0;
    transform-origin: center;
    position: relative;
    visibility: hidden;
  }

  .stage-slot {
    width: 100%;
    flex-shrink: 0;
    overflow: visible;
    display: flex;
    flex-direction: column;
  }
  .game-slot { justify-content: center; }
  .game-stage, .piano-strip-section { flex-shrink: 0; }
  .game-stage { width: 100%; display: flex; justify-content: center; transform-origin: center center; }
  .stage-selector {
    display: inline-flex;
    align-items: center;
    gap: 2px;
    padding: 2px;
    border-radius: var(--radius-sm);
    background: var(--control-fill);
    flex-shrink: 0;
  }
  .stage-selector button, .stage-selector .stage-label {
    display: inline-flex;
    align-items: center;
    justify-content: center;
    gap: 5px;
    border: none;
    border-radius: 3px;
    padding: 4px 10px;
    background: transparent;
    color: var(--text-secondary);
    font: inherit;
    font-size: 11px;
    font-weight: 500;
    line-height: 14px;
    white-space: nowrap;
    transition: color var(--transition-fast), background var(--transition-fast);
  }
  .stage-selector button { cursor: pointer; }
  .stage-selector button:hover:not([aria-pressed="true"]) {
    color: var(--text-primary);
    background: var(--bg-hover);
  }
  .stage-selector button[aria-pressed="true"] {
    background: var(--bg-tertiary);
    color: var(--accent);
    font-weight: 600;
  }
  .stage-selector button:focus-visible {
    outline: 2px solid var(--accent);
    outline-offset: -2px;
  }
  @media (prefers-reduced-motion: reduce) {
    .stage-selector button { transition: none; }
  }
  .piano-key-white:focus, .piano-key-white:focus-visible,
  .piano-key-black:focus, .piano-key-black:focus-visible {
    outline: none;
  }

  /* ==========================================
     Piano Strip Section
     ========================================== */
  .piano-strip-section {
    margin-top: 0;
    display: flex;
    flex-direction: column;
    gap: 8px;
    width: 100%;
    margin-left: auto;
    margin-right: auto;
  }

  .piano-strip-header {
    display: flex;
    align-items: center;
    justify-content: space-between;
    padding: 0 4px;
  }

  .piano-strip-title-row {
    display: flex;
    align-items: center;
    gap: 8px;
  }

  :global(.piano-strip-icon) {
    color: var(--accent);
  }

  .piano-strip-title {
    font-size: 13px;
    font-weight: 600;
    color: var(--text-primary);
  }

  .piano-strip-range-badge {
    font-size: 10.5px;
    font-weight: 500;
    color: var(--text-tertiary);
    background: var(--bg-tertiary);
    padding: 1px 6px;
    border-radius: var(--radius-sm, 4px);
  }

  .piano-rows {
    display: flex;
    flex-direction: column;
    gap: 10px;
    container-type: inline-size;
  }

  .piano-keyboard-strip {
    display: flex;
    align-items: stretch;
    height: 116px;
    background: rgba(15, 17, 23, 0.85);
    border: 1px solid var(--border-subtle);
    border-radius: var(--radius-lg, 8px);
    padding: 10px 12px 10px 12px;
    box-shadow: inset 0 2px 10px rgba(0, 0, 0, 0.5), 0 4px 16px rgba(0, 0, 0, 0.3);
    width: 100%;
    box-sizing: border-box;
  }

  .piano-key-slot {
    position: relative;
    flex: 1 1 0;
    min-width: 0;
    height: 100%;
    display: flex;
  }

  .piano-keyboard-strip::after {
    content: '';
    /* Keep short rows aligned, with room for a final overhanging sharp. */
    flex: calc(var(--white-key-count) - var(--row-key-count) + 0.28) 1 0;
  }

  .piano-key-white {
    width: 100%;
    height: 100%;
    background: linear-gradient(180deg, #f1f2f6 0%, #dcdee4 100%);
    border: 1px solid rgba(0, 0, 0, 0.35);
    border-radius: 0 0 4px 4px;
    box-shadow: 0 2px 4px rgba(0, 0, 0, 0.35), inset 0 1px 0 rgba(255, 255, 255, 0.8);
    display: flex;
    flex-direction: column;
    justify-content: flex-end;
    align-items: center;
    padding-bottom: 6px;
    cursor: pointer;
    transition: transform 0.06s ease, background 0.08s ease, box-shadow 0.08s ease;
    user-select: none;
    outline: none;
    box-sizing: border-box;
    padding-left: 0;
    padding-right: 0;
    gap: 7px;
  }

  .piano-key-white:hover {
    background: linear-gradient(180deg, #ffffff 0%, #e5e7eb 100%);
  }

  .piano-key-white.active {
    background: linear-gradient(180deg, #38bdf8 0%, #0284c7 100%) !important;
    border-color: rgba(255, 255, 255, 0.5) !important;
    box-shadow: 0 0 16px rgba(56, 189, 248, 0.85), inset 0 -3px 0 rgba(255, 255, 255, 0.7) !important;
    transform: translateY(2px);
  }

  .piano-key-white.active .piano-key-label {
    color: #ffffff !important;
    text-shadow: 0 1px 3px rgba(0, 0, 0, 0.6);
  }

  .piano-key-label {
    font-size: clamp(7px, calc(60cqw / var(--white-key-count)), 9.5px);
    font-weight: 600;
    color: #475569;
    pointer-events: none;
    line-height: 1;
  }

  .piano-key-label.c-label {
    color: var(--accent);
    font-weight: 800;
  }

  .piano-key-black {
    position: absolute;
    right: -28%;
    top: 0;
    width: 56%;
    height: 64%;
    z-index: 3;
    background: linear-gradient(180deg, #2d2f38 0%, #17181c 100%);
    border: 1px solid rgba(255, 255, 255, 0.15);
    border-radius: 0 0 3px 3px;
    box-shadow: 0 3px 6px rgba(0, 0, 0, 0.65), inset 0 1px 0 rgba(255, 255, 255, 0.2);
    display: flex;
    flex-direction: column;
    justify-content: flex-end;
    align-items: center;
    padding-bottom: 3px;
    cursor: pointer;
    transition: transform 0.06s ease, background 0.08s ease, box-shadow 0.08s ease;
    user-select: none;
    outline: none;
    box-sizing: border-box;
    padding-left: 0;
    padding-right: 0;
    gap: 6px;
  }

  .piano-key-slot.sharp-only .piano-key-black {
    left: 36%;
    right: auto;
  }

  .piano-key-binding {
    font-size: clamp(7px, calc(60cqw / var(--white-key-count)), 10px);
    color: #475569;
    line-height: 1;
    pointer-events: none;
  }

  .piano-key-black .piano-key-binding {
    font-size: clamp(5px, calc(42cqw / var(--white-key-count)), 8px);
    color: #dcdee4;
  }

  .piano-key-white.active .piano-key-binding,
  .piano-key-black.active .piano-key-binding { color: white; }

  .piano-key-black:hover {
    background: linear-gradient(180deg, #3f424e 0%, #22242a 100%);
  }

  .piano-key-black.active {
    background: linear-gradient(180deg, #a855f7 0%, #6b21a8 100%) !important;
    border-color: rgba(255, 255, 255, 0.6) !important;
    box-shadow: 0 0 18px rgba(168, 85, 247, 0.95), inset 0 -2px 0 rgba(255, 255, 255, 0.6) !important;
    transform: translateY(2px);
  }

  .piano-black-label {
    font-size: clamp(5px, calc(42cqw / var(--white-key-count)), 7.5px);
    font-weight: 600;
    color: #94a3b8;
    pointer-events: none;
    line-height: 1;
    transform: scale(0.9);
  }

  .piano-key-black.active .piano-black-label {
    color: #ffffff;
    text-shadow: 0 1px 2px rgba(0, 0, 0, 0.8);
  }

  /* ==========================================
     Bottom Bar
     ========================================== */
  .inst-bottom-bar {
    min-height: 44px;
    box-sizing: border-box;
    background: var(--bg-secondary);
    border: none;
    display: flex;
    align-items: center;
    justify-content: space-between;
    padding: 8px 20px;
    gap: 16px;
    flex-shrink: 0;
  }

  .bottom-left-controls,
  .bottom-right-controls {
    display: flex;
    align-items: center;
    gap: 18px;
  }

  .bottom-right-controls { min-width: 0; max-width: 60%; }

  .control-unit {
    display: flex;
    align-items: center;
    gap: 8px;
  }

  .chord-tolerance-unit {
    gap: 8px;
  }

  .chord-tolerance-label {
    display: inline-flex;
    align-items: center;
    gap: 6px;
    color: var(--text-primary);
    opacity: 0.85;
    font-size: 12px;
    font-weight: 500;
    white-space: nowrap;
  }

  .instrument-toggle-btn {
    display: inline-flex;
    align-items: center;
    flex-shrink: 0;
    height: 24px;
    white-space: nowrap;
    font-family: inherit;
    font-size: 12px;
    font-weight: 500;
    line-height: 1;
    color: var(--text-primary);
    opacity: 0.35;
    padding: 0;
    border: none;
    background: transparent;
    cursor: pointer;
    transition: opacity var(--transition-normal), color var(--transition-fast);
  }

  .bottom-control-label {
    display: inline-flex;
    align-items: center;
    white-space: nowrap;
    line-height: 1;
  }

  .instrument-toggle-btn.enabled:not(:disabled) {
    color: var(--accent);
    opacity: 0.85;
  }

  .instrument-toggle-btn:is(:hover, :focus-visible):not(:disabled) { opacity: 1; }
  .instrument-toggle-btn:active:not(:disabled) { opacity: 0.5; }
  .instrument-toggle-btn:disabled {
    color: var(--text-primary);
    opacity: 0.22;
    cursor: not-allowed;
  }
  .instrument-toggle-btn:focus-visible:not(:disabled) {
    outline: 2px solid var(--accent);
    outline-offset: 3px;
  }

  @media (prefers-reduced-motion: reduce) {
    .instrument-toggle-btn { transition: none; }
  }

  .tolerance-box {
    display: inline-flex;
  }

  .tolerance-box :global(.number-box) {
    height: 26px;
    padding: 0 4px 0 6px;
  }

  .tolerance-box :global(.num-input) {
    width: 32px;
    padding: 0;
    font-size: 11.5px;
    font-variant-numeric: tabular-nums;
  }

  .pedal-settings-icon-btn,
  .midi-refresh-btn {
    display: inline-flex;
    align-items: center;
    justify-content: center;
    width: 24px;
    height: 24px;
    padding: 0;
    flex-shrink: 0;
  }

  /* MIDI Input Group */
  .midi-input-group {
    display: flex;
    align-items: center;
    gap: 8px;
    min-width: 0;
    height: 24px;
  }

  .midi-icon-label {
    display: flex;
    align-items: center;
    gap: 0;
    height: 24px;
    color: var(--text-secondary);
    font-size: 12px;
    font-weight: 500;
    line-height: 1;
    flex-shrink: 0;
  }

  .midi-icon-label :global(.midi-plug-icon) {
    color: inherit;
    transition: color var(--transition-fast);
  }

  .midi-icon-label.connected :global(.midi-plug-icon) {
    color: var(--accent);
  }

  .midi-dropdown-wrap {
    position: relative;
    display: flex;
    align-items: center;
    height: 24px;
    min-width: 0;
    max-width: 320px;
    transition: width var(--transition-normal);
  }

  .midi-dropdown-sizer {
    position: absolute;
    visibility: hidden;
    pointer-events: none;
    width: 0;
    height: 0;
    overflow: hidden;
  }

  .midi-dropdown-measure {
    display: inline-flex;
    align-items: center;
    gap: 4px;
    width: max-content;
    white-space: nowrap;
    font-size: 12px;
  }

  .midi-dropdown-wrap :global(.fluent-dropdown.variant-ghost) {
    align-self: center;
    align-items: center;
    height: 24px;
  }

  .midi-dropdown-wrap :global(.fluent-dropdown.variant-ghost .dropdown-trigger) {
    height: 24px;
    padding: 0;
    font-size: 12px;
    line-height: 1;
    align-items: center;
    border: none;
  }

  .midi-dropdown-wrap :global(.fluent-dropdown.variant-ghost .trigger-content) {
    height: 100%;
    align-items: center;
    line-height: 1;
  }

  .midi-dropdown-wrap :global(.fluent-dropdown.variant-ghost .trigger-label),
  .midi-dropdown-wrap :global(.fluent-dropdown.variant-ghost .trigger-placeholder) {
    font-size: 12px;
    line-height: 1;
  }

  .midi-dropdown-wrap :global(.fluent-dropdown.variant-ghost .chevron-icon) {
    margin-top: 0;
  }

  @media (prefers-reduced-motion: reduce) {
    .midi-dropdown-wrap { transition: none; }
  }
</style>
