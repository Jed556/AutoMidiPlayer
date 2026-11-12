<script lang="ts">
  import { onDestroy } from 'svelte';
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
  import { createFrameResizeObserver } from '../utils/frameResizeObserver';
  import { tooltip } from '../actions/tooltip';
  import { persistScroll } from '../actions/persistScroll';
  import GameIcon from '../components/Common/GameIcon.svelte';
  import { toastOffset } from '../actions/toastOffset';
  import { InstrumentInput, keyboardPitches } from '../utils/instrumentInput';
  import { createPianoRows, createPianoSlots, hasInGameInstrumentLayout, pianoRange, type PianoSlot } from '../utils/instrumentKeyboard';
  import { instrumentKeyRows, instrumentLayoutStyle, instrumentNotes as getInstrumentNotes } from '../../../shared/utils/instrumentLayout';
  import InstrumentLayout from '../components/Instrument/InstrumentLayout.svelte';

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

  let compactStage = $state(uiStore.instrumentCompactStage);
  const initialWidth = typeof window !== 'undefined' ? Math.max(720, Math.min(1600, window.innerWidth - 88)) : 720;
  const initialHeight = typeof window !== 'undefined' ? Math.max(300, window.innerHeight - 100) : 500;
  let pianoAvailableWidth = $state(initialWidth);
  let pianoAvailableHeight = $state(initialHeight);
  let pianoStageWidth = $state(initialWidth);
  let selectedStage = $derived(uiStore.instrumentStage);
  let gameSelected = $derived(uiStore.instrumentGameSelected);
  let pianoSelected = $derived(uiStore.instrumentPianoSelected);
  const hasInGameLayout = $derived(hasInGameInstrumentLayout(selectedInstrument));
  const showGame = $derived(hasInGameLayout && (compactStage ? selectedStage === 'game' : gameSelected));
  const showPiano = $derived(!hasInGameLayout || (compactStage ? selectedStage === 'piano' : pianoSelected));

  function selectStage(view: 'game' | 'piano') {
    uiStore.setInstrumentStage(view);
    if (compactStage) return;
    let nextGame = uiStore.instrumentGameSelected;
    let nextPiano = uiStore.instrumentPianoSelected;
    if (view === 'game') {
      nextGame = !nextGame;
      if (!nextGame) nextPiano = true;
    } else {
      nextPiano = !nextPiano;
      if (!nextPiano) nextGame = true;
    }
    uiStore.setInstrumentToggles(nextGame, nextPiano);
  }

  function fitInstrumentStage(
    viewport: HTMLDivElement,
    options: { game: boolean; piano: boolean; hasGame: boolean; maxWidth: number; duration: number },
  ) {
    const content = viewport.querySelector('.inst-stage') as HTMLDivElement;
    const game = content.querySelector('.game-stage') as HTMLDivElement;
    const piano = content.querySelector('.piano-strip-section') as HTMLDivElement;
    const gameSlot = game.parentElement!;
    const pianoSlot = piano.parentElement!;
    const reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)');
    // Width, occupied heights, gap, and available space share one eased timeline.
    let current: number[] | null = null;
    let target: number[] = [];
    let frame: number | null = null;
    let isSettled = false;
    let settleFrame: number | null = null;

    settleFrame = requestAnimationFrame(() => {
      settleFrame = requestAnimationFrame(() => {
        isSettled = true;
        settleFrame = null;
      });
    });

    function draw(values: number[]) {
      current = values;
      const [width, gameHeight, pianoHeight, gap, availableWidth, availableHeight,
        naturalGameHeight, naturalPianoHeight] = values;
      const height = Math.max(1, gameHeight + pianoHeight + gap);
      const scale = Math.min(1, availableWidth / width, availableHeight / height);
      content.style.width = `${width}px`;
      gameSlot.style.height = `${gameHeight}px`;
      pianoSlot.style.height = `${pianoHeight}px`;
      content.style.gap = `${gap}px`;
      content.style.transform = `scale(${scale})`;
      // Keep hidden views beyond the viewport, rather than clipping them at their slots.
      // The same eased occupied heights drive their travel and push the other view.
      const travel = (availableHeight + 40) / Math.max(scale, 0.001);
      const gameReveal = Math.min(1, gameHeight / Math.max(1, naturalGameHeight));
      const pianoReveal = Math.min(1, pianoHeight / Math.max(1, naturalPianoHeight));
      game.style.transform = `translateY(${-travel * (1 - gameReveal)}px)`;
      piano.style.transform = `translateY(${travel * (1 - pianoReveal)}px)`;
    }

    function measure() {
      const width = Math.max(0, viewport.clientWidth - 88);
      const height = Math.max(0, viewport.clientHeight - 40);
      const naturalWidth = Math.max(720, Math.min(options.maxWidth, width));
      // Use the visible width, since the stage itself may be scaled down.
      pianoAvailableWidth = Math.min(naturalWidth, width);
      pianoAvailableHeight = height;
      pianoStageWidth = naturalWidth;
      // Measure full inner views even while their occupied heights are zero.
      const previousWidth = content.style.width;
      content.style.width = `${naturalWidth}px`;
      const naturalGameHeight = game.offsetHeight;
      const naturalPianoHeight = piano.offsetHeight;
      content.style.width = previousWidth;
      const widthScale = Math.min(1, width / naturalWidth);
      compactStage = options.hasGame && height < (naturalGameHeight + naturalPianoHeight + 24) * widthScale * 0.85;
      uiStore.instrumentCompactStage = compactStage;
      // Read the derived selection after updating the compact layout mode.
      const next = [naturalWidth, showGame ? naturalGameHeight : 0,
        showPiano ? naturalPianoHeight : 0, showGame && showPiano ? 24 : 0, width, height,
        naturalGameHeight, naturalPianoHeight];
      const duration = (!isSettled || reducedMotion.matches) ? 0 : options.duration;
      if (current && duration > 0 && next.every((value, index) => Math.abs(value - target[index]) < 0.01)) return;
      target = next;
      if (frame !== null) cancelAnimationFrame(frame);
      frame = null;
      if (!current || duration <= 0) {
        draw(next);
        return;
      }
      // Reversals start at the currently rendered layout, including mid-resize.
      const start = [...current];
      const startedAt = performance.now();
      function advance(now: number) {
        const progress = Math.min(1, Math.max(0, (now - startedAt) / duration));
        const eased = 1 - Math.pow(1 - progress, 3);
        draw(start.map((value, index) => value + (next[index] - value) * eased));
        frame = progress < 1 ? requestAnimationFrame(advance) : null;
      }
      frame = requestAnimationFrame(advance);
    }

    const resize = createFrameResizeObserver(measure);
    resize.observe(viewport);
    resize.observe(game);
    resize.observe(piano);
    reducedMotion.addEventListener('change', measure);
    measure();
    return {
      update(next: typeof options) {
        options = next;
        measure();
      },
      destroy() {
        resize.disconnect();
        reducedMotion.removeEventListener('change', measure);
        if (frame !== null) cancelAnimationFrame(frame);
        if (settleFrame !== null) cancelAnimationFrame(settleFrame);
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
    label: l.name.toLowerCase().includes('layout') ? l.name : `${l.name} Layout`
  })));

  $effect(() => {
    selectedInstrumentId = gameStore.activeInstrument?.id ?? '';
  });

  $effect(() => {
    selectedLayoutId = gameStore.activeLayout?.id ?? '';
  });

  let supportsChords = $derived(
    selectedInstrument ? selectedInstrument.supportsChords : false
  );

  let supportsPedals = $derived(
    selectedInstrument ? selectedInstrument.supportsSustain : true
  );

  // ==========================================
  // Autoplay / Scheduled Playback (Upper Right)
  // ==========================================
  const scheduledAction = $derived(scheduledPlaybackStore.active || scheduledPlaybackStore.executing
    ? scheduledPlaybackStore.action : playbackStore.isPlaying ? 'pause' : 'play');

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
  let midiDropdownWidth = $state(0);
  let inputPitches = $state(new Set<number>());
  const instrumentInput = new InstrumentInput(pitches => { inputPitches = pitches; });
  const inputEnabled = $derived(uiStore.activeTab === 'instrument'
    && !uiStore.activeDialog && !uiStore.gameSelectorOpen);

  onDestroy(() => {
    instrumentInput.clear();
    for (const timer of previewTimers.values()) clearTimeout(timer);
  });

  // ==========================================
  // Layout Definitions & Active Note Glow
  // ==========================================
  let activePitches = $derived(new Set([...playbackStore.activeNotes.map(n => n.pitch), ...inputPitches, ...midiInputStore.state.pitches]));

  const selectedGameLayout = $derived(
    gameStore.activeGame?.layouts?.find(layout => layout.id === selectedLayoutId) ?? null
  );
  const instrumentNotes = $derived(getInstrumentNotes(selectedInstrument));
  const layoutStyle = $derived(instrumentLayoutStyle(selectedInstrument));
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
  const stageMaxWidth = $derived(Math.max(960, Math.min(1600, pianoSlots.length * 32 + 26)));
  const gameBindings = $derived(new Map(mappedKeys.map(key => [key.pitch, key.label])));

  let manuallyActivePitches = $state(new Set<number>());
  const litPitches = $derived(new Set([...activePitches, ...manuallyActivePitches]));
  const previewTimers = new Map<number, ReturnType<typeof setTimeout>>();

  // Clear held input when navigation, an overlay, or the displayed mapping changes.
  $effect(() => {
    inputEnabled; gameStore.activeGame?.id; selectedInstrumentId; selectedLayoutId;
    instrumentInput.clear();
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
    const bindings = mappedKeys;
    const pitches = keyboardPitches(event.key, bindings);
    if (!pitches.length) return;
    event.preventDefault();
    if (!event.repeat && instrumentInput.press(`keyboard:${event.code || event.key}`, pitches)) {
      pitches.forEach(previewNote);
    }
  }

  function handleKeyUp(event: KeyboardEvent) {
    instrumentInput.release(`keyboard:${event.code || event.key}`);
  }

  function clearInput() { instrumentInput.clear(); }

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
</script>

<svelte:window onkeydown={handleKeyDown} onkeyup={handleKeyUp} onblur={clearInput} />
<svelte:document onvisibilitychange={clearInput} />

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
<div class="piano-keys-scroll" use:persistScroll={'instrument:piano'}>
  <div class="piano-keyboard-strip" style:--white-key-count={pianoRowColumns} style:--row-key-count={slots.length}>
    {#each slots as slot}
      <div class="piano-key-slot" class:sharp-only={!slot.white}>
        {#if slot.white}
        {@const white = slot.white}
        {@const isWhiteLit = activePitches.has(white.pitch) || manuallyActivePitches.has(white.pitch)}
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
          {@const isBlackLit = activePitches.has(slot.sharp.pitch) || manuallyActivePitches.has(slot.sharp.pitch)}
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
</div>
{/snippet}

<div class="instrument-view">
  <!-- Top Bar -->
  <div class="inst-top-bar" bind:this={topBar}>
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

    <!-- Top Center: Nothing (Clean open space as requested) -->
    <div class="top-center-spacer"></div>

    <!-- Upper Right: Autoplay / Scheduling -->
    <div class="scheduled-playback-card">
      <div class="sched-header">
        <FluentIcon name="Clock" size={13} class="sched-icon" />
        <span class="sched-title">{t('instrument.scheduledPlayback')}</span>
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

      <div class="sched-status-row">
        {#if scheduledPlaybackStore.active}
          <span class="sched-countdown">
            <span class="sched-pulse-dot"></span>
            {t(scheduledAction === 'play' ? 'instrument.playsIn' : 'instrument.pausesIn')}: {formatCountdown(scheduledPlaybackStore.remainingSeconds)}
          </span>
        {:else}
          <span class="sched-info">
            {t(scheduledAction === 'play' ? 'instrument.schedulePlay' : 'instrument.schedulePause')} {scheduledPlaybackStore.hour.toString().padStart(2, '0')}:{scheduledPlaybackStore.minute.toString().padStart(2, '0')}
          </span>
        {/if}
      </div>
    </div>
  </div>

  <!-- Main Instrument Display Area -->
  <div class="inst-stage-viewer" use:fitInstrumentStage={{ game: showGame, piano: showPiano, hasGame: hasInGameLayout, maxWidth: stageMaxWidth, duration: animationStore.durationMs }}>
    <div class="stage-selector" role="group" aria-label="Instrument views">
      {#if hasInGameLayout}
      <button type="button" aria-pressed={showGame} onclick={() => selectStage('game')}>
        <FluentIcon name="Keyboard" size={14} />
        <span>In-game</span>
      </button>
      <button type="button" aria-pressed={showPiano} onclick={() => selectStage('piano')}>
        <FluentIcon name="MusicNote2" size={14} />
        <span>Piano</span>
      </button>
      {:else}
        <span class="stage-label">
          <FluentIcon name="MusicNote2" size={14} />
          <span>Piano</span>
        </span>
      {/if}
    </div>
    <div class="inst-stage">
    <div class="stage-slot game-slot" inert={!showGame}>
    <div class="game-stage">
    {#if hasInGameLayout}
      <InstrumentLayout {layoutStyle} rows={mappedRows} activePitches={litPitches} onplay={triggerKey} />
    {/if}

    </div>
    </div>
    <!-- Chromatic Piano Strip -->
    <div class="stage-slot piano-slot" inert={!showPiano}>
    <div class="piano-strip-section">
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
    </div>
    </div>
    </div>
  </div>

  <!-- Bottom Bar: Chords & Pedals (Lower Left) | MIDI Input (Lower Right) -->
  <div class="inst-bottom-bar" bind:clientWidth={bottomBarWidth} use:toastOffset>
    <!-- Lower Left: Chords and Pedals Toggles -->
    <div class="bottom-left-controls">
      <!-- Auto Chord Toggle -->
      <div class="control-unit" class:disabled={!supportsChords}>
        <button
          type="button"
          class="instrument-toggle-btn auto-chord-control"
          class:enabled={playbackStore.autoChord}
          disabled={!supportsChords}
          aria-label={t('instrument.autoChord')}
          aria-pressed={playbackStore.autoChord}
          onclick={() => playbackStore.setAutoChord(!playbackStore.autoChord)}
          use:tooltip={supportsChords ? t('instrument.detectPlayChords') : t('instrument.noChordSupport')}
        >
          <ChordIcon size={16} filled={playbackStore.autoChord} isActive={playbackStore.autoChord} />
          {@render bottomLabel(t('instrument.autoChord'))}
        </button>
        {#if supportsChords && playbackStore.autoChord}
          <div class="tolerance-box" use:tooltip={t('instrument.chordTolerance')}>
            <NumberBox
              value={playbackStore.chordDetectionMilliseconds}
              onchange={(value) => playbackStore.setChordDetectionMilliseconds(value)}
              min={0}
              max={500}
              step={5}
              unit="ms"
            />
          </div>
        {/if}
      </div>

      <!-- Pedals toggle and bindings -->
      <div class="control-unit" class:disabled={!supportsPedals}>
        <button
          type="button"
          class="instrument-toggle-btn pedal-toggle-btn"
          class:enabled={playbackStore.pedals.enabled}
          disabled={!supportsPedals}
          aria-label={t('instrument.pedals')}
          aria-pressed={playbackStore.pedals.enabled}
          onclick={() => playbackStore.togglePedals()}
          use:tooltip={supportsPedals ? t('instrument.pedalBindings') : t('instrument.noPedalSupport')}
        >
          <PedalIcon
            size={15}
            isActive={playbackStore.pedals.enabled}
            isSoftActive={playbackStore.pedals.enabled && playbackStore.pedals.unaCorda}
            isSostenutoActive={playbackStore.pedals.enabled && playbackStore.pedals.sostenuto}
            isSustainActive={playbackStore.pedals.enabled && playbackStore.pedals.sustain}
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

    <!-- Lower Right: MIDI Input Selector (Ghost Refresh) -->
    <div class="bottom-right-controls">
      <div class="midi-input-group">
        <div class="midi-icon-label" use:tooltip={t('instrument.physicalMidiDevice')}>
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
    padding: 10px 20px;
    background: var(--bg-secondary);
    border: none;
    gap: 16px;
    flex-shrink: 0;
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

  .top-center-spacer {
    flex: 1;
  }

  /* Scheduled Playback Card */
  .scheduled-playback-card {
    flex-shrink: 0;
    display: flex;
    flex-direction: column;
    gap: 5px;
    background: var(--bg-card);
    border-radius: var(--radius-md);
    padding: 8px 12px;
    border: none;
    box-shadow: 0 4px 12px rgba(0, 0, 0, 0.2);
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
    padding: 20px 20px 20px 68px;
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
    width: 960px;
    flex-shrink: 0;
    transform-origin: center;
    position: relative;
  }

  .stage-slot {
    width: 100%;
    flex-shrink: 0;
    overflow: visible;
    display: flex;
    flex-direction: column;
  }
  .game-slot { justify-content: flex-end; }
  .game-stage, .piano-strip-section { flex-shrink: 0; }
  .game-stage { width: 100%; display: flex; justify-content: center; }
  .stage-selector {
    position: absolute;
    top: 50%;
    left: 12px;
    transform: translateY(-50%);
    display: flex;
    flex-direction: column;
    gap: 3px;
    padding: 3px;
    border-radius: 999px;
    background: var(--bg-secondary);
    z-index: 1;
  }
  .stage-selector button, .stage-selector .stage-label {
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 6px;
    border: none;
    border-radius: 999px;
    padding: 12px 7px;
    background: transparent;
    color: var(--text-secondary);
    font: inherit;
    font-size: 12px;
  }
  .stage-selector button { cursor: pointer; }
  .stage-selector button span, .stage-label > span {
    writing-mode: vertical-rl;
    transform: rotate(180deg);
  }
  .stage-selector button[aria-pressed="true"] {
    background: var(--bg-tertiary);
    color: var(--accent);
  }
  .stage-selector button:focus-visible {
    outline: 2px solid var(--accent);
    outline-offset: 2px;
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
  }

  .piano-keys-scroll {
    width: 100%;
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

  .control-unit.disabled {
    opacity: 0.5;
  }

  .instrument-toggle-btn {
    display: inline-flex;
    align-items: center;
    flex-shrink: 0;
    white-space: nowrap;
    font-size: 12px;
    font-weight: 500;
    color: var(--text-primary);
    opacity: 0.35;
    padding: 4px 0;
    border: none;
    background: transparent;
    cursor: pointer;
    transition: opacity var(--transition-normal), color var(--transition-fast);
  }

  .bottom-control-label {
    display: inline-flex;
    white-space: nowrap;
  }

  .instrument-toggle-btn.enabled {
    color: var(--accent);
    opacity: 0.85;
  }

  .instrument-toggle-btn:is(:hover, :focus-visible):not(:disabled) { opacity: 1; }
  .instrument-toggle-btn:active:not(:disabled) { opacity: 0.5; }
  .instrument-toggle-btn:disabled {
    opacity: 0.22;
    cursor: not-allowed;
  }
  .instrument-toggle-btn:focus-visible {
    outline: 2px solid var(--accent);
    outline-offset: 3px;
  }

  @media (prefers-reduced-motion: reduce) {
    .instrument-toggle-btn { transition: none; }
  }

  .tolerance-box {
    width: 76px;
  }

  .tolerance-box :global(.number-box) {
    height: 26px;
    padding: 0 4px 0 6px;
    border: none;
  }

  .tolerance-box :global(.num-input) {
    width: 32px;
    font-size: 11.5px;
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
  }

  .midi-icon-label {
    display: flex;
    align-items: center;
    gap: 0;
    color: var(--text-secondary);
    font-size: 12px;
    font-weight: 500;
    flex-shrink: 0;
  }

  :global(.midi-plug-icon) {
    color: var(--accent);
  }

  .midi-dropdown-wrap {
    position: relative;
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
    gap: 8px;
    padding: 0 10px;
    width: max-content;
    white-space: nowrap;
    font-size: 12px;
  }

  .midi-dropdown-wrap :global(.trigger-label) {
    font-size: 12px;
  }

  @media (prefers-reduced-motion: reduce) {
    .midi-dropdown-wrap { transition: none; }
  }

  .midi-dropdown-wrap :global(.dropdown-trigger) {
    height: 28px;
    font-size: 12px;
    border: none;
  }
</style>
