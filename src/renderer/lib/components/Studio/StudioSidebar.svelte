<script lang="ts">
  import { playbackStore } from '../../state/playbackStore.svelte';
  import { uiStore } from '../../state/uiStore.svelte';
  import { gameStore } from '../../state/gameStore.svelte';
  import { t } from '../../i18n';
  import { getTrackColor, generateKeyOptions } from '../../utils/musicConstants';
  import FluentIcon from '../Common/FluentIcon.svelte';
  import ToggleSwitch from '../Common/ToggleSwitch.svelte';
  import HoldNotesControl from '../Common/HoldNotesControl.svelte';
  import Dropdown from '../Common/Dropdown.svelte';
  import Button from '../Common/Button.svelte';
  import CustomScrollViewer from '../Common/CustomScrollViewer.svelte';
  import SlidingTabs from '../Common/SlidingTabs.svelte';

  let {
    selectedTrackIndex = $bindable<number | null>(null),
    hoveredTrackIndex = $bindable<number | null>(null),
  } = $props();

  $effect(() => {
    playbackStore.activeSong?.id;
    selectedTrackIndex = null;
    hoveredTrackIndex = null;
  });

  let isCollapsed = $derived(uiStore.studioSidebarCollapsed);

  const keyOffsetOptions = $derived(
    generateKeyOptions(playbackStore.keyOffsetBase).map(opt => ({
      value: opt.value,
      label: `${opt.offsetDisplay} ${opt.noteDisplay}`
    }))
  );

  const speedOptions = [
    { value: 0.25, label: '0.25x' },
    { value: 0.5, label: '0.5x' },
    { value: 0.75, label: '0.75x' },
    { value: 1.0, label: '1.0x' },
    { value: 1.25, label: '1.25x' },
    { value: 1.5, label: '1.5x' },
    { value: 1.75, label: '1.75x' },
    { value: 2.0, label: '2.0x' },
    { value: 2.5, label: '2.5x' },
    { value: 3.0, label: '3.0x' }
  ];

  const transposeOptions = $derived([
    { value: 'Smart', label: t('player.transposeSmart') || 'Smart' },
    { value: 'Ignore', label: t('player.transposeIgnore') || 'Ignore' },
    { value: 'Up', label: t('player.transposeUp') || 'Up' },
    { value: 'Down', label: t('player.transposeDown') || 'Down' }
  ]);

  function toggleTrackMute(trackId: number) {
    const song = playbackStore.activeSong;
    if (!song) return;
    const track = song.tracks.find(t => t.index === trackId);
    if (track) {
      const trackIndex = (track.index ?? (track.id ? track.id - 1 : 0)) as number;
      const currentlyDisabled = (track.muted ?? track.isMuted ?? false) || playbackStore.getTrackVolume(trackIndex) === 0;
      const nextMuted = !currentlyDisabled;
      track.muted = nextMuted;
      track.isMuted = nextMuted;
      playbackStore.setTrackMuted(trackIndex, nextMuted);
    }
  }

  function soloTrack(trackId: number) {
    const song = playbackStore.activeSong;
    if (!song) return;
    const isCurrentlySolo = song.tracks.every(t => {
      const tIdx = (t.index ?? (t.id ? t.id - 1 : 0)) as number;
      const isM = (t.muted ?? t.isMuted ?? false) || playbackStore.getTrackVolume(tIdx) === 0;
      return t.index === trackId ? !isM : isM;
    });
    if (isCurrentlySolo) {
      // Unsolo all
      song.tracks.forEach(t => {
        t.muted = false;
        t.isMuted = false;
        playbackStore.setTrackMuted(t.index ?? (t.id ? t.id - 1 : 0), false);
      });
    } else {
      song.tracks.forEach(t => {
        const isTarget = t.index === trackId;
        t.muted = !isTarget;
        t.isMuted = !isTarget;
        playbackStore.setTrackMuted(t.index ?? (t.id ? t.id - 1 : 0), !isTarget);
      });
    }
  }

  // Active track IDs set
  let activeTrackIds = $derived(new Set(playbackStore.activeNotes.map(n => n.trackId)));

  function toggleSidebar() {
    uiStore.toggleStudioSidebar();
  }
</script>

<aside class="studio-sidebar" class:collapsed={isCollapsed}>
  <!-- Gutter space with embedded chevron arrow like the 3 dots on the left nav menu -->
  <div
    class="sidebar-gutter"
    data-tooltip-position="right"
    role="button"
    tabindex="0"
    onclick={toggleSidebar}
    onkeydown={(e) => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); toggleSidebar(); } }}
    title={isCollapsed ? (t('studio.expandSidebar') || 'Expand sidebar') : (t('studio.collapseSidebar') || 'Collapse sidebar')}
    aria-label={isCollapsed ? 'Expand sidebar' : 'Collapse sidebar'}
  >
    <div class="gutter-line top"></div>
    <div class="gutter-chevron" class:collapsed={isCollapsed} aria-hidden="true">
      <FluentIcon name="ChevronRight" size={13} />
    </div>
    <div class="gutter-line bottom"></div>
  </div>

  <div class="sidebar-container">
    <div class="sidebar-inner" inert={isCollapsed || playbackStore.isChangingSongContext ? true : undefined}>
      <SlidingTabs
        label="Studio panels"
        value={uiStore.studio.sidebarTab}
        onselect={id => uiStore.studio.sidebarTab = id as typeof uiStore.studio.sidebarTab}
        tabs={[
          { id: 'tracks', label: t('studio.tracks'), icon: 'Layer' },
          { id: 'settings', label: t('studio.songSettings'), icon: 'Settings' },
          { id: 'markers', label: t('studio.timelineMarkers'), icon: 'Pin' },
        ]}
      />

      <!-- Tab Contents -->
      <CustomScrollViewer class="sidebar-content-viewer" contentClass="sidebar-content" padding="10px">
        {#if uiStore.studio.sidebarTab === 'tracks'}
          <div class="tracks-tab">
            <div class="tracks-list">
              {#if playbackStore.activeSong}
                {@const totalTracks = playbackStore.activeSong.tracks?.length || 1}
                {#each (playbackStore.activeSong.tracks || []) as track, idx (track.id ?? track.index ?? idx)}
                  {@const trackIndex = (track.index ?? (track.id ? track.id - 1 : idx)) as number}
                  {@const trackColor = track.color || getTrackColor(trackIndex, totalTracks)}
                  {@const isMuted = (track.muted ?? track.isMuted ?? false) || playbackStore.getTrackVolume(trackIndex) === 0}
                  {@const isPlaying = activeTrackIds.has(trackIndex) && !isMuted}
                  <div
                    class="track-item"
                    class:muted={isMuted}
                    class:playing={isPlaying}
                    class:highlighted={(hoveredTrackIndex ?? selectedTrackIndex) === trackIndex}
                    style="--track-color: {trackColor}"
                    role="group"
                    aria-label={track.name || `Track ${idx + 1}`}
                    onmouseenter={() => hoveredTrackIndex = trackIndex}
                    onmouseleave={() => hoveredTrackIndex = null}
                  >
                    <div class="track-color-indicator"></div>

                    <button
                      class="track-details"
                      aria-pressed={selectedTrackIndex === trackIndex}
                      aria-label={`${track.name || `Track ${idx + 1}`}: ${t('studio.highlightTrack')}`}
                      title={t('studio.highlightTrack')}
                      onclick={() => selectedTrackIndex = selectedTrackIndex === trackIndex ? null : trackIndex}
                      onfocus={() => hoveredTrackIndex = trackIndex}
                      onblur={() => hoveredTrackIndex = null}
                    >
                      <div class="track-name">{track.name || `Track ${idx + 1}`}</div>
                      <div class="track-instrument">{track.instrument || track.instrumentName || 'Instrument'}</div>
                    </button>

                    <div class="track-controls">
                      <button
                        class="track-action-btn"
                        class:active={!isMuted}
                        aria-label={isMuted ? t('studio.unmute') : t('studio.mute')}
                        aria-pressed={isMuted}
                        onclick={() => toggleTrackMute(trackIndex)}
                        title={isMuted ? t('studio.unmute') : t('studio.mute')}
                      >
                        {#if isMuted}
                          <FluentIcon name="Volume0" size={13} />
                        {:else}
                          <FluentIcon name="Volume3" size={13} />
                        {/if}
                      </button>

                      <button
                        class="track-action-btn solo-btn"
                        onclick={() => soloTrack(trackIndex)}
                        title={t('studio.solo')}
                      >
                        S
                      </button>
                    </div>
                  </div>
                {/each}
              {:else}
                <div class="empty-hint">{t('studio.noTracksLoaded')}</div>
              {/if}
            </div>
          </div>
        {:else if uiStore.studio.sidebarTab === 'settings'}
          <!-- Song Settings Tab (Faithful to original WPF AutoMidiPlayer) -->
          <div class="settings-tab">
            <p class="settings-scope">{t('studio.settingsScope', {
              game: gameStore.activeGame?.displayName ?? '', instrument: gameStore.activeInstrument?.name ?? '',
            })}</p>
            {#if playbackStore.activeSong}
              <!-- Key Offset and Playback Speed (Same Row) -->
              <div class="settings-row-2">
                <div class="setting-group-col">
                  <span class="group-label">{t('studio.keyOffsetLabel') || 'Key Offset'}</span>
                  <Dropdown
                    value={playbackStore.keyOffset}
                    options={keyOffsetOptions}
                    onchange={(val) => playbackStore.setKeyOffset(Number(val))}
                  />
                </div>

                <div class="setting-group-col">
                  <span class="group-label">{t('studio.playbackSpeedLabel') || 'Playback Speed'}</span>
                  <Dropdown
                    bind:value={playbackStore.playbackSpeed}
                    options={speedOptions}
                    onchange={(val) => playbackStore.setPlaybackSpeed(Number(val))}
                  />
                </div>
              </div>

              <!-- Transpose Mode -->
              <div class="setting-group">
                <span class="group-label">{t('studio.transposeModeLabel') || 'Transpose Mode'}</span>
                <Dropdown
                  bind:value={playbackStore.transposeMode}
                  options={transposeOptions}
                  onchange={(val) => playbackStore.setTransposeMode(val)}
                />
              </div>

              <!-- Hold Notes Toggle -->
              <div class="setting-group">
                <HoldNotesControl
                  checked={playbackStore.holdNotes}
                  thresholdMs={playbackStore.tapThresholdMs}
                  onchange={(val) => playbackStore.setHoldNotes(val)}
                  onthresholdchange={(val) => playbackStore.setTapThresholdMs(val)}
                />
              </div>

              <!-- Auto Chord Detection Toggle -->
              <div class="setting-group switch-row-card">
                <div class="switch-card-text">
                  <div class="switch-card-title">{t('studio.autoChord') || 'Auto Chord Detection'}</div>
                  <div class="switch-card-desc">{t('studio.autoChordDesc') || 'Play chords as game chord-pads'}</div>
                </div>
                <ToggleSwitch
                  bind:checked={playbackStore.autoChord}
                  onchange={(val) => playbackStore.setAutoChord(val)}
                />
              </div>

              <!-- Key Change Markers Toggle -->
              <div class="setting-group switch-row-card">
                <div class="switch-card-text">
                  <div class="switch-card-title">{t('studio.keyMarkers') || 'Key Change Markers'}</div>
                  <div class="switch-card-desc">{t('studio.keyMarkersDesc') || 'Apply timeline key offset changes'}</div>
                </div>
                <ToggleSwitch
                  bind:checked={playbackStore.enableKeyChangeMarkers}
                  onchange={(val) => playbackStore.setKeyChangeMarkersEnabled(val)}
                />
              </div>


            {:else}
              <div class="empty-hint">{t('studio.loadSongPrompt')}</div>
            {/if}
          </div>
        {:else}
                <div class="markers-sidebar-section">
                  <div class="markers-sidebar-header">
                    <span class="group-label">{t('studio.timelineMarkers')} ({playbackStore.activeSong?.keyMarkers?.length || 0})</span>
                    <Button
                      variant="ghost"
                      size="sm"
                      icon="ScanDash"
                      onclick={() => playbackStore.autoDetectKeyChanges()}
                      title={t('studio.scanKeysTooltip') || 'Scan the MIDI file for embedded key signatures'}
                    >
                      {t('studio.scan') || 'Scan'}
                    </Button>
                  </div>

                  <div class="marker-navigation">
                    <Button variant="ghost" icon="ChevronLeft" onclick={() => playbackStore.navigateMarker('prev')} title={t('studio.prevMarkerTooltip')} />
                    <Button variant="ghost" icon="ChevronRight" onclick={() => playbackStore.navigateMarker('next')} title={t('studio.nextMarkerTooltip')} />
                    <Button variant="ghost" icon="Delete" colorMode="danger" disabled={!playbackStore.selectedMarkerId} onclick={() => playbackStore.deleteSelectedMarker()} title={t('studio.deleteMarkerTooltip')} />
                  </div>
                  {#if playbackStore.activeSong?.keyMarkers && playbackStore.activeSong.keyMarkers.length > 0}
                    <div class="markers-sidebar-list">
                      {#each playbackStore.activeSong.keyMarkers as marker (marker.id)}
                        <div
                          class="marker-sidebar-item"
                          class:is-active={marker.id === playbackStore.selectedMarkerId}
                        >
                          <button class="marker-item-info" onclick={() => {
                            playbackStore.selectMarker(marker);
                            playbackStore.seek(marker.timeMs / 1000);
                          }}>
                            <span class="marker-item-key">{marker.noteDisplay || marker.key}</span>
                            <span class="marker-item-offset">({marker.offsetDisplay})</span>
                            <span class="marker-item-time">{(marker.time ?? (marker.timeMs / 1000)).toFixed(2)}s</span>
                          </button>
                          <button
                            class="marker-item-delete"
                            onclick={(e) => {
                              e.stopPropagation();
                              playbackStore.deleteKeyMarker(marker.id);
                            }}
                            title={t('studio.deleteMarkerTooltip') || 'Delete marker'}
                          >
                            <svg width="12" height="12" viewBox="0 0 16 16" fill="currentColor">
                              <path d="M5.5 5.5A.5.5 0 0 1 6 6v6a.5.5 0 0 1-1 0V6a.5.5 0 0 1 .5-.5zm2.5 0a.5.5 0 0 1 .5.5v6a.5.5 0 0 1-1 0V6a.5.5 0 0 1 .5-.5zm3 .5a.5.5 0 0 0-1 0v6a.5.5 0 0 0 1 0V6z"/>
                              <path fill-rule="evenodd" d="M14.5 3a1 1 0 0 1-1 1H13v9a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V4h-.5a1 1 0 0 1-1-1V2a1 1 0 0 1 1-1H6a1 1 0 0 1 1-1h2a1 1 0 0 1 1 1h3.5a1 1 0 0 1 1 1v1zM4.118 4 4 4.059V13a1 1 0 0 0 1 1h6a1 1 0 0 0 1-1V4.059L11.882 4H4.118zM2.5 3V2h11v1h-11z"/>
                            </svg>
                          </button>
                        </div>
                      {/each}
                    </div>
                  {:else}
                    <div class="sidebar-no-markers">
                      {t('studio.noKeyChangesShort') || 'No key markers on timeline.'}
                    </div>
                  {/if}
                </div>
        {/if}
      </CustomScrollViewer>
  </div>
</div>
</aside>

<style>
  .settings-scope { margin: 0 0 12px; font-size: 11px; color: var(--text-secondary); }
  .studio-sidebar {
    width: 296px;
    height: 100%;
    display: flex;
    position: relative;
    user-select: none;
    transition: width var(--transition-slow, 260ms cubic-bezier(0.215, 0.61, 0.355, 1));
    flex-shrink: 0;
    overflow: hidden;
    isolation: isolate;
  }

  .studio-sidebar.collapsed {
    width: 16px;
  }

  .sidebar-gutter {
    width: 16px;
    height: 100%;
    position: relative;
    flex-shrink: 0;
    display: flex;
    align-items: center;
    justify-content: center;
    cursor: pointer;
    user-select: none;
    z-index: 20;
    background: transparent;
  }

  .gutter-line {
    position: absolute;
    left: 50%;
    width: 2px;
    background: rgba(255, 255, 255, 0.25);
    border-radius: 1px;
    opacity: 0;
    transition: transform 0.22s cubic-bezier(0.4, 0, 0.2, 1),
                opacity 0.18s ease-in 0.04s;
    pointer-events: none;
  }

  .gutter-line.top {
    top: 8px;
    bottom: calc(50% + 12px);
    transform: translateX(-50%) scaleY(0);
    transform-origin: bottom center;
  }

  .gutter-line.bottom {
    top: calc(50% + 12px);
    bottom: 8px;
    transform: translateX(-50%) scaleY(0);
    transform-origin: top center;
  }

  .sidebar-gutter:hover .gutter-line {
    opacity: 1;
    transition: transform 0.22s cubic-bezier(0.16, 1, 0.3, 1),
                opacity 0.12s ease-out;
  }

  .sidebar-gutter:hover .gutter-line.top {
    transform: translateX(-50%) scaleY(1);
  }

  .sidebar-gutter:hover .gutter-line.bottom {
    transform: translateX(-50%) scaleY(1);
  }

  .gutter-chevron {
    position: absolute;
    top: 50%;
    left: 50%;
    transform: translate(-50%, -50%) rotate(0deg);
    display: flex;
    align-items: center;
    justify-content: center;
    background: transparent;
    color: var(--text-tertiary, rgba(255, 255, 255, 0.4));
    pointer-events: none;
    z-index: 2;
    transition: color 0.15s ease, transform var(--transition-normal, 0.22s cubic-bezier(0, 0, 0.2, 1));
  }

  .sidebar-gutter:hover .gutter-chevron {
    color: var(--text-primary, #ffffff);
  }

  .gutter-chevron.collapsed {
    transform: translate(-50%, -50%) rotate(180deg);
  }

  .sidebar-container {
    flex: 1;
    min-width: 0;
    height: 100%;
    overflow: hidden;
    background: var(--surface-color, #121212);
    border-radius: 8px 0 0 8px;
    display: flex;
    flex-direction: column;
    box-sizing: border-box;
    transition: opacity var(--transition-fast, 180ms ease);
  }

  .studio-sidebar.collapsed .sidebar-container {
    opacity: 0;
    pointer-events: none;
  }

  .sidebar-inner {
    width: 280px;
    min-width: 280px;
    height: 100%;
    display: flex;
    flex-direction: column;
    overflow: hidden;
  }

  :global(.sidebar-content-viewer) {
    flex: 1;
    min-height: 0;
    width: 100%;
  }

  .tracks-list {
    display: flex;
    flex-direction: column;
    gap: 6px;
  }

  .track-item {
    display: flex;
    align-items: center;
    gap: 8px;
    padding: 8px 10px;
    border-radius: 6px;
    background: var(--bg-card);
    border: none;
    opacity: 1;
    transition: opacity var(--transition-normal, 0.22s cubic-bezier(0, 0, 0.2, 1)),
                background var(--transition-fast),
                box-shadow var(--transition-fast);
  }

  .track-item:hover {
    background: var(--card-highlight, #2A2A2A);
  }

  .track-item.muted {
    opacity: 0.45;
  }

  .track-item.playing {
    background: var(--card-highlight, #2A2A2A);
    box-shadow: inset 0 0 0 1.5px var(--track-color);
  }

  .track-item.highlighted {
    box-shadow: inset 0 0 0 2px var(--track-color);
  }

  .track-color-indicator {
    width: 4px;
    height: 28px;
    border-radius: 2px;
    background: var(--track-color);
    flex-shrink: 0;
  }

  .track-details {
    flex: 1;
    min-width: 0;
    padding: 0;
    border: 0;
    background: transparent;
    text-align: left;
    font: inherit;
    cursor: pointer;
  }

  .track-details:focus-visible {
    outline: 2px solid var(--track-color);
    outline-offset: 3px;
  }

  .track-name {
    font-size: 12px;
    font-weight: 500;
    color: var(--text-primary);
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
  }

  .track-instrument {
    font-size: 10.5px;
    color: var(--text-tertiary);
    margin-top: 1px;
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
  }

  .track-controls {
    display: flex;
    align-items: center;
    gap: 3px;
  }

  .track-action-btn {
    width: 24px;
    height: 24px;
    border-radius: 3px;
    background: transparent;
    border: none;
    color: var(--text-primary);
    opacity: 0.4;
    cursor: pointer;
    display: flex;
    align-items: center;
    justify-content: center;
    transition: opacity var(--transition-normal, 0.22s cubic-bezier(0, 0, 0.2, 1));
  }

  .track-action-btn:hover {
    background: transparent;
    opacity: 1;
  }

  .track-action-btn.active {
    color: var(--accent-color, var(--accent, #1DB954));
    opacity: 0.9;
  }

  .track-action-btn.active:hover {
    color: var(--accent-color, var(--accent, #1DB954));
    opacity: 1;
  }

  .track-action-btn:active {
    opacity: 0.75;
  }

  .solo-btn {
    font-size: 10px;
    font-weight: 700;
  }

  /* Song Settings Tab */
  .settings-tab {
    display: flex;
    flex-direction: column;
    gap: 12px;
  }

  .settings-row-2 {
    display: flex;
    gap: 10px;
  }

  .setting-group-col {
    flex: 1;
    min-width: 0;
    display: flex;
    flex-direction: column;
    background: var(--bg-card);
    border-radius: 6px;
    padding: 10px 12px;
  }

  .setting-group {
    background: var(--bg-card);
    border: none;
    border-radius: 6px;
    padding: 10px 12px;
  }

  .group-label {
    display: block;
    font-size: 11.5px;
    font-weight: 500;
    color: var(--text-secondary);
    margin-bottom: 6px;
  }

  .switch-row-card {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 12px;
  }

  .switch-card-text {
    flex: 1;
    min-width: 0;
  }

  .switch-card-title {
    font-size: 12px;
    font-weight: 500;
    color: var(--text-primary);
  }

  .switch-card-desc {
    font-size: 10.5px;
    color: var(--text-tertiary);
    margin-top: 2px;
  }

  .empty-hint {
    padding: 24px 0;
    text-align: center;
    font-size: 12px;
    color: var(--text-tertiary);
    font-style: italic;
  }

  .markers-sidebar-section {
    display: flex;
    flex-direction: column;
    gap: 8px;
    margin-top: 4px;
    padding: 10px;
    background: var(--bg-card);
    border: 1px solid var(--border-default);
    border-radius: var(--radius-md, 8px);
  }

  .markers-sidebar-header {
    display: flex;
    align-items: center;
    justify-content: space-between;
  }

  .marker-navigation {
    display: flex;
    gap: 4px;
  }

  .markers-sidebar-list {
    display: flex;
    flex-direction: column;
    gap: 4px;

  }

  .marker-sidebar-item {
    display: flex;
    align-items: center;
    justify-content: space-between;
    padding: 5px 8px;
    border-radius: var(--radius-sm, 6px);
    background: var(--bg-surface-elevated, rgba(255, 255, 255, 0.04));
    border: 1px solid transparent;
    cursor: pointer;
    transition: all 0.15s ease;
  }

  .marker-sidebar-item:hover {
    background: var(--bg-surface-hover, rgba(255, 255, 255, 0.08));
    border-color: var(--border-default);
  }

  .marker-sidebar-item.is-active {
    background: var(--accent-subtle, rgba(29, 185, 84, 0.15));
    border-color: var(--accent-color, var(--accent, #1DB954));
  }

  .marker-item-info {
    flex: 1;
    border: 0;
    background: transparent;
    cursor: pointer;
    padding: 4px 0;
    display: flex;
    align-items: center;
    gap: 6px;
    font-size: 11px;
  }

  .marker-item-key {
    font-weight: 600;
    color: var(--text-primary);
  }

  .marker-item-offset {
    color: var(--accent-color, #1db954);
    font-size: 10px;
  }

  .marker-item-time {
    color: var(--text-tertiary);
    font-size: 10.5px;
    font-variant-numeric: tabular-nums;
  }

  .marker-item-delete {
    opacity: 0;
    background: transparent;
    border: none;
    color: var(--color-danger, #ef4444);
    cursor: pointer;
    padding: 2px;
    border-radius: 4px;
    display: flex;
    align-items: center;
    justify-content: center;
    transition: opacity 0.15s ease;
  }

  .marker-sidebar-item:hover .marker-item-delete,
  .marker-sidebar-item:focus-within .marker-item-delete {
    opacity: 0.7;
  }

  .marker-item-delete:hover {
    opacity: 1 !important;
  }

  .sidebar-no-markers {
    font-size: 11px;
    color: var(--text-tertiary);
    font-style: italic;
    padding: 4px 0;
  }
</style>
