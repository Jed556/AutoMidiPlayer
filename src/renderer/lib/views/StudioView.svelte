<script lang="ts">
  import { uiStore } from '../state/uiStore.svelte';
  import { playbackStore } from '../state/playbackStore.svelte';
  import { t } from '../i18n';
  import PianoRollCanvas from '../components/Studio/PianoRollCanvas.svelte';
  import TimelineScrubber from '../components/Studio/TimelineScrubber.svelte';
  import StudioSidebar from '../components/Studio/StudioSidebar.svelte';
  import KeyPicker from '../components/Common/KeyPicker.svelte';
  import Button from '../components/Common/Button.svelte';
  import GradientSweep from '../components/Common/GradientSweep.svelte';
  import FluentIcon from '../components/Common/FluentIcon.svelte';
  import Dropdown from '../components/Common/Dropdown.svelte';
  import { gameStore } from '../state/gameStore.svelte';
  import { themeStore } from '../state/themeStore.svelte';
  import { settingsStore } from '../state/settingsStore.svelte';
  import { indexSongTracks } from '../components/Studio/studioNotes';
  import { createDisplayPitch, getDisplayTiming } from '../components/Studio/studioDisplay';
  import OverflowText from '../components/Common/OverflowText.svelte';
  import { getKeyNoteName, formatSignedOffset } from '../utils/musicConstants';
  import type { KeyChangeMarker } from '$shared/types/midi';

  const displayModes = $derived([
    { value: 'Auto', label: t('studio.viewAuto'), description: t('studio.viewAutoDesc') },
    { value: 'Transpose', label: t('studio.viewTranspose'), description: t('studio.viewTransposeDesc') },
    { value: 'NoteModifiers', label: t('studio.viewNoteModifiers'), description: t('studio.viewNoteModifiersDesc') },
    { value: 'SongDefault', label: t('studio.viewSongDefault'), description: t('studio.viewSongDefaultDesc') },
  ]);
  const displayModeLabel = $derived.by(() => {
    const label = displayModes.find(option => option.value === uiStore.studio.displayMode)!.label;
    const song = playbackStore.activeSong;
    if (uiStore.studio.displayMode !== 'Auto' || !song) return label;

    let marker: KeyChangeMarker | undefined;
    if (playbackStore.enableKeyChangeMarkers) {
      const timeMs = playbackStore.currentTimeSeconds * 1000;
      for (const candidate of song.keyMarkers ?? []) {
        if (candidate.timeMs <= timeMs && (!marker || candidate.timeMs >= marker.timeMs)) marker = candidate;
      }
    }
    const offset = marker?.keyOffset ?? playbackStore.effectiveKeyOffset;
    const followsTranspose = offset !== 0 || playbackStore.transposeMode !== 'Ignore';
    const source = marker
      ? t('studio.viewMarkerSource', { time: formatMarkerTime(marker.timeMs) })
      : t(followsTranspose ? 'studio.viewTransposeSource' : 'studio.viewSongDefault');
    return t('studio.viewAutoStatus', {
      key: getKeyNoteName(offset), offset: offset === 0 ? '+0' : formatSignedOffset(offset), source,
    });
  });

  function formatMarkerTime(timeMs: number): string {
    const milliseconds = Math.max(0, Math.round(timeMs));
    const minutes = Math.floor(milliseconds / 60000);
    const seconds = Math.floor(milliseconds / 1000) % 60;
    return `${String(minutes).padStart(2, '0')}:${String(seconds).padStart(2, '0')}.${String(milliseconds % 1000).padStart(3, '0')}`;
  }
  const displayPitch = $derived(createDisplayPitch({
    mode: uiStore.studio.displayMode,
    keyOffset: playbackStore.effectiveKeyOffset,
    transpose: playbackStore.transposeMode,
    markersEnabled: playbackStore.enableKeyChangeMarkers,
    markers: playbackStore.activeSong?.keyMarkers ?? [],
    playableNotes: gameStore.activeInstrument?.notes ?? [],
    notes: playbackStore.activeSong?.notes?.length ? playbackStore.activeSong.notes
      : playbackStore.activeSong?.tracks.flatMap(track => track.notes ?? []) ?? [],
  }));
  const renderTracks = $derived(indexSongTracks(playbackStore.activeSong, themeStore.accentColor, displayPitch,
    getDisplayTiming(uiStore.studio.displayMode, {
      holdNotes: playbackStore.holdNotes,
      thresholdMs: playbackStore.tapThresholdMs,
      tapMs: settingsStore.settings.keyPressDelayMs,
      speed: playbackStore.enginePlaybackSpeed,
    }, playbackStore.isListenMode && playbackStore.synthMode === 'raw')));

  function getInitialRollAreaWidth(): number {
    if (typeof window === 'undefined') return 1200;
    const navWidth = uiStore.libraryWidth ?? 80;
    const sidebarWidth = uiStore.studioSidebarCollapsed ? 16 : 296;
    return Math.max(0, window.innerWidth - navWidth - sidebarWidth);
  }

  let scrollX = $state(0);
  let scrollY = $state(0);
  let rollAreaWidth = $state(getInitialRollAreaWidth());
  const showToolbarLabels = $derived(rollAreaWidth >= 900);
  let hoverTime = $state<number | null>(null);
  let pianoRoll = $state<{ fitKeyHeight: () => void }>();
  let selectedTrackIndex = $state<number | null>(null);
  let hoveredTrackIndex = $state<number | null>(null);

  function handleScrollChange(newX: number, newY: number) {
    scrollX = newX;
    scrollY = newY;
  }

  function handleScrubberScroll(newX: number) {
    scrollX = newX;
  }

  function handleZoomChange(newZoom: number) {
    uiStore.studio.zoomX = newZoom;
  }

  function handleAddOrSetOffset() {
    if (playbackStore.selectedMarker) {
      playbackStore.updateKeyMarker(playbackStore.selectedMarker.id, playbackStore.selectedKeyOffset);
    } else {
      playbackStore.addKeyMarker(playbackStore.currentTimeSeconds, playbackStore.selectedKeyOffset);
    }
  }
</script>

{#snippet toolbarLabel(label: string)}
  <span class="accessible-toolbar-label">{label}</span>
  <span aria-hidden="true" class="toolbar-label">
    <GradientSweep visible={showToolbarLabels} collapseMargin="left" marginAmount="6px" overflowVisibleOnComplete={false}>
      {label}
    </GradientSweep>
  </span>
{/snippet}

<div class="studio-view">
  <div class="studio-body">
    <!-- Center Piano Roll Viewport -->
    <div class="roll-area" bind:clientWidth={rollAreaWidth}>
      <div class="studio-toolbar">
        <Dropdown
          options={displayModes}
          bind:value={uiStore.studio.displayMode}
          variant="ghost"
          title={t('studio.viewMode')}
          align="right"
        >
          <span class="view-mode-label">
            <OverflowText text={displayModeLabel} hoverTarget="button" showTooltip={false} />
          </span>
        </Dropdown>
      </div>
      <PianoRollCanvas
        {renderTracks}
        bind:this={pianoRoll}
        bind:zoomX={uiStore.studio.zoomX}
        bind:zoomY={uiStore.studio.zoomY}
        bind:scrollX
        bind:scrollY
        onScrollChange={handleScrollChange}
        bind:hoverTime
        bind:isLockedToPlayhead={uiStore.studio.isLockedToPlayhead}
        highlightedTrackIndex={hoveredTrackIndex ?? selectedTrackIndex}
      />

      <!-- Timeline Scrubber Bar (At the bottom, 1:1 with WPF StudioView) -->
      <TimelineScrubber
        {renderTracks}
        {scrollX}
        zoomX={uiStore.studio.zoomX}
        bind:zoomY={uiStore.studio.zoomY}
        viewportWidth={Math.max(200, rollAreaWidth - 58)}
        onScroll={handleScrubberScroll}
        onZoomChange={handleZoomChange}
        onFitNotes={() => pianoRoll?.fitKeyHeight()}
        bind:hoverTime
        bind:isLockedToPlayhead={uiStore.studio.isLockedToPlayhead}
      />

      <!-- Bottom Key Modulation & Markers Card (1:1 with WPF StudioView) -->
      <div class="modulation-card">
        <div class="card-left-group">
          <span class="card-label">{t('studio.keyOffset') || 'Key Offset:'}</span>

          <div class="dropdown-wrapper">
            <KeyPicker
              bind:value={playbackStore.selectedKeyOffset}
              placement="top"
              variant="ghost"
              width="96px"
              onchange={(newOffset) => {
                if (playbackStore.selectedMarker) {
                  playbackStore.updateKeyMarker(playbackStore.selectedMarker.id, newOffset);
                }
              }}
            />
          </div>

          <Button
            variant="ghost"
            class="responsive-toolbar-button"
            onclick={handleAddOrSetOffset}
            title={playbackStore.selectedMarker
              ? (t('studio.setMarkerTooltip') || "Update selected marker's key offset")
              : (t('studio.addMarkerTooltip') || `Add key offset marker at current playhead (${playbackStore.currentTimeSeconds.toFixed(1)}s)`)}
          >
            <FluentIcon name={playbackStore.selectedMarker ? 'Edit' : 'Add'} size={14} />
            {@render toolbarLabel(playbackStore.selectedMarker ? (t('studio.setMarker') || 'Set') : (t('studio.addMarker') || 'Add'))}
          </Button>


        </div>

        <div class="vertical-divider"></div>

        <div class="marker-actions">
          <Button
            variant="ghost"
            size="icon"
            icon="ChevronLeft"
            disabled={!playbackStore.activeSong?.keyMarkers?.length}
            onclick={() => playbackStore.navigateMarker('prev')}
            title={t('studio.prevMarkerTooltip')}
          />
          <Button
            variant="ghost"
            size="icon"
            icon="ChevronRight"
            disabled={!playbackStore.activeSong?.keyMarkers?.length}
            onclick={() => playbackStore.navigateMarker('next')}
            title={t('studio.nextMarkerTooltip')}
          />
          <Button
            variant="ghost"
            class="responsive-toolbar-button"
            colorMode="danger"
            disabled={!playbackStore.selectedMarkerId}
            onclick={() => playbackStore.deleteSelectedMarker()}
            title={t('studio.deleteMarkerTooltip')}
          >
            <FluentIcon name="Delete" size={14} />
            {@render toolbarLabel(t('studio.deleteMarker'))}
          </Button>
          <Button
            variant="ghost"
            class="responsive-toolbar-button"
            disabled={!playbackStore.activeSong}
            onclick={() => playbackStore.autoDetectKeyChanges()}
            title={t('studio.scanKeysTooltip')}
          >
            <FluentIcon name="ScanDash" size={14} />
            {@render toolbarLabel(t('studio.scanKeys'))}
          </Button>
        </div>

        <div class="follow-markers">
          <Button
            variant="ghost"
            class="responsive-toolbar-button"
            active={playbackStore.enableKeyChangeMarkers}
            pressed={playbackStore.enableKeyChangeMarkers}
            disabled={!playbackStore.activeSong}
            title={t('studio.followKeyMarkers')}
            onclick={() => playbackStore.setKeyChangeMarkersEnabled(!playbackStore.enableKeyChangeMarkers)}
          >
            <FluentIcon name="Location" size={14} />
            {@render toolbarLabel(t('studio.followKeyMarkers'))}
          </Button>
        </div>
      </div>
    </div>

    <!-- Right Collapsible Studio Sidebar -->
    <StudioSidebar bind:selectedTrackIndex bind:hoveredTrackIndex />
  </div>
</div>

<style>
  .studio-toolbar {
    display: flex;
    align-items: center;
    justify-content: flex-end;
    padding: 2px 10px;
    background: var(--bg-secondary);
    flex-shrink: 0;
  }

  .view-mode-label {
    min-width: 0;
    font-variant-numeric: tabular-nums;
  }

  .modulation-card :global(.responsive-toolbar-button) {
    gap: 0;
    padding: 0 8px;
    min-width: 32px;
  }

  .toolbar-label {
    display: inline-flex;
  }

  .accessible-toolbar-label {
    position: absolute;
    width: 1px;
    height: 1px;
    padding: 0;
    overflow: hidden;
    clip-path: inset(50%);
    white-space: nowrap;
  }

  .studio-view {
    display: flex;
    flex-direction: column;
    height: 100%;
    width: 100%;
    overflow: hidden;
    background: transparent;
  }

  .studio-body {
    display: flex;
    flex: 1;
    min-height: 0;
    height: 100%;
    overflow: hidden;
  }

  .roll-area {
    display: flex;
    flex-direction: column;
    flex: 1;
    min-width: 0;
    min-height: 0;
    height: 100%;
    position: relative;
    overflow: hidden;
    background: var(--surface-color, #121212);
    border-radius: var(--radius-md, 8px);
  }

  .modulation-card {
    height: 44px;
    box-sizing: border-box;
    flex-wrap: nowrap;
    background: var(--bg-secondary);
    border-top: none;
    display: flex;
    align-items: center;
    padding: 6px 10px;
    gap: 10px;
    user-select: none;
    z-index: 10;
    flex-shrink: 0;
    white-space: nowrap;
  }

  .card-left-group {
    display: flex;
    align-items: center;
    gap: 8px;
    flex-wrap: nowrap;
    flex-shrink: 0;
  }

  .card-label {
    font-size: 12px;
    font-weight: 600;
    color: var(--text-primary);
    white-space: nowrap;
  }

  .dropdown-wrapper {
    flex-shrink: 0;
  }

  .vertical-divider {
    width: 1px;
    height: 16px;
    background: var(--divider-color, rgba(255, 255, 255, 0.08));
    margin: 0 2px;
    flex-shrink: 0;
  }

  .follow-markers {
    margin-left: auto;
    flex-shrink: 0;
  }

  .marker-actions {
    display: flex;
    align-items: center;
    gap: 4px;
    flex-shrink: 0;
  }
</style>
