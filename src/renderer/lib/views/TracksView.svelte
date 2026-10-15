<script lang="ts">
  import { onMount } from 'svelte';
  import { createFrameResizeObserver } from '../utils/frameResizeObserver';
  import { playbackStore } from '../state/playbackStore.svelte';
  import { uiStore } from '../state/uiStore.svelte';
  import { t } from '../i18n';
  import FluentIcon from '../components/Common/FluentIcon.svelte';
  import Button from '../components/Common/Button.svelte';
  import Slider from '../components/Common/Slider.svelte';
  import GradientSweep from '../components/Common/GradientSweep.svelte';
  import CustomScrollViewer from '../components/Common/CustomScrollViewer.svelte';

  let showEmptyTracks = $state(false);
  let expandedTrackIds = $state<Set<number>>(new Set());

  // Derive total notes and playable stats directly from reactive playbackStore
  let totalNotes = $derived(playbackStore.totalNotes);
  let accessibleNotes = $derived(playbackStore.accessibleNotes);
  let playablePercentage = $derived(playbackStore.playablePercentage);

  let tracks = $derived.by(() => {
    if (!playbackStore.activeSong) return [];
    let list = playbackStore.activeSong.tracks;
    if (!showEmptyTracks) {
      list = list.filter(t => (t.noteCount || t.notes?.length || 0) > 0);
    }
    return list;
  });

  let isAnyExpanded = $derived(expandedTrackIds.size > 0);

  function toggleAllExpanded() {
    if (isAnyExpanded) {
      expandedTrackIds = new Set();
    } else {
      expandedTrackIds = new Set(tracks.map((t, idx) => (t.id ?? t.index ?? idx) as number));
    }
  }

  function toggleTrackExpand(id: number) {
    const next = new Set(expandedTrackIds);
    if (next.has(id)) {
      next.delete(id);
    } else {
      next.add(id);
    }
    expandedTrackIds = next;
  }

  let scrollHostEl = $state<HTMLElement | null>(null);
  let cardWidth = $state(typeof window !== 'undefined' ? window.innerWidth : 1000);

  onMount(() => {
    if (!scrollHostEl || typeof ResizeObserver === 'undefined') return;
    cardWidth = scrollHostEl.getBoundingClientRect().width || cardWidth;
    const observer = createFrameResizeObserver((entries) => {
      for (const entry of entries) {
        if (entry.contentBoxSize?.[0]) {
          cardWidth = entry.contentBoxSize[0].inlineSize;
        } else {
          cardWidth = entry.contentRect.width;
        }
      }
    });
    observer.observe(scrollHostEl);
    return () => observer.disconnect();
  });

  let isDrawerExpanded = $derived(uiStore.libraryWidth > 85);

  // Sequential visibility in summary card:
  // 1. Volume slider is the first to hide when drawer is expanded (unless >= 860px) or cardWidth <= 830px
  let showSummaryVolume = $derived(isDrawerExpanded ? cardWidth >= 860 : cardWidth > 830);

  // 2. Frequent is second to hide (<= 710px, or drawer expanded < 860px)
  let showSummaryFrequent = $derived(isDrawerExpanded ? cardWidth >= 860 : cardWidth > 710);

  // 3. Duration is third to hide (<= 610px, or drawer expanded <= 630px)
  let showSummaryDuration = $derived(isDrawerExpanded ? cardWidth > 630 : cardWidth > 610);

  // 4. Black Keys is fourth to hide (<= 500px, or drawer expanded <= 520px)
  let showSummaryBlackKeys = $derived(isDrawerExpanded ? cardWidth > 520 : cardWidth > 500);

  // Overflow visibility:
  // Only put items in overflow when not visible in summary card
  let showOverflowVolume = $derived(!showSummaryVolume);
  let showOverflowFrequent = $derived(!showSummaryFrequent);
  let showOverflowDuration = $derived(!showSummaryDuration);
  let showOverflowBlackKeys = $derived(!showSummaryBlackKeys);

  let hasOverflowStats = $derived(showOverflowFrequent || showOverflowDuration || showOverflowBlackKeys);
  let hasAnyOverflow = $derived(showOverflowVolume || hasOverflowStats);

  function handleCardClick(e: MouseEvent, trackId: number) {
    const target = e.target as HTMLElement | null;
    if (target?.closest('.fluent-switch') || target?.closest('button')) {
      return;
    }
    toggleTrackExpand(trackId);
  }

  function handleCardKeyDown(e: KeyboardEvent, trackId: number) {
    if (e.key === 'Enter' || e.key === ' ') {
      const target = e.target as HTMLElement | null;
      if (target?.closest('.fluent-switch') || target?.closest('button')) {
        return;
      }
      e.preventDefault();
      toggleTrackExpand(trackId);
    }
  }

  let interactedTrackIds = $state<Set<number>>(new Set());

  function toggleTrackMute(track: any) {
    const trackId = (track.id ?? track.index) as number;
    const trackIndex = (track.index ?? (track.id ? track.id - 1 : 0)) as number;
    const next = new Set(interactedTrackIds);
    next.add(trackId);
    interactedTrackIds = next;

    const currentlyDisabled = (track.muted ?? track.isMuted ?? false) || playbackStore.getTrackVolume(trackIndex) === 0;
    const nextMuted = !currentlyDisabled;
    track.muted = nextMuted;
    track.isMuted = nextMuted;
    playbackStore.setTrackMuted(trackIndex, nextMuted);
  }

  function formatTime(seconds: number): string {
    const s = Math.max(0, Math.floor(seconds));
    const m = Math.floor(s / 60);
    const rem = s % 60;
    return `${m}:${rem.toString().padStart(2, '0')}`;
  }

  function formatNoteName(noteNumber: number): string {
    const names = ['C', 'C#', 'D', 'D#', 'E', 'F', 'F#', 'G', 'G#', 'A', 'A#', 'B'];
    const normalized = Math.max(0, Math.min(127, Math.round(noteNumber)));
    const pitch = names[normalized % 12];
    const octave = Math.floor(normalized / 12) - 1;
    return `${pitch}${octave}`;
  }

  function getTrackTiming(track: any, totalDurSec: number) {
    let startTime = 0;
    let endTime = totalDurSec;

    if (track.startMs !== undefined && track.durationMs !== undefined && track.durationMs > 0) {
      startTime = track.startMs / 1000;
      endTime = (track.startMs + track.durationMs) / 1000;
    } else if (track.timeStartMs !== undefined && track.timeDurationMs !== undefined) {
      startTime = track.timeStartMs / 1000;
      endTime = (track.timeStartMs + track.timeDurationMs) / 1000;
    } else if (track.notes && track.notes.length > 0) {
      const sTimes = track.notes.map((n: any) => n.startTime ?? (n.startMs ? n.startMs / 1000 : 0));
      const eTimes = track.notes.map((n: any) => (n.startTime ?? (n.startMs ? n.startMs / 1000 : 0)) + (n.duration ?? (n.lengthMs ? n.lengthMs / 1000 : 0.25)));
      startTime = Math.min(...sTimes);
      endTime = Math.max(...eTimes);
    } else if (track.timeStartRatio !== undefined && track.timeDurationRatio !== undefined) {
      startTime = track.timeStartRatio * totalDurSec;
      endTime = (track.timeStartRatio + track.timeDurationRatio) * totalDurSec;
    }

    const effectiveTotal = totalDurSec > 0 ? totalDurSec : Math.max(1, endTime);
    const startRatio = Math.max(0, Math.min(1, startTime / effectiveTotal));
    const durationRatio = Math.max(0.015, Math.min(1 - startRatio, (endTime - startTime) / effectiveTotal));

    return {
      startTime,
      endTime,
      startPct: +(startRatio * 100).toFixed(2),
      widthPct: +(durationRatio * 100).toFixed(2),
      display: `${formatTime(startTime)} - ${formatTime(endTime)}`
    };
  }

  function getTrackPitch(track: any) {
    let minNote = track.minPitch ?? track.pitchMin;
    let maxNote = track.maxPitch ?? track.pitchMax;
    let avgNote = track.avgPitch;

    if ((minNote === undefined || maxNote === undefined) && track.notes && track.notes.length > 0) {
      const pitches = track.notes.map((n: any) => n.pitch ?? n.noteNumber ?? 60);
      minNote = Math.min(...pitches);
      maxNote = Math.max(...pitches);
      avgNote = Math.round(pitches.reduce((a: number, b: number) => a + b, 0) / pitches.length);
    }

    minNote = minNote ?? 36;
    maxNote = maxNote ?? 72;
    avgNote = avgNote ?? Math.round((minNote + maxNote) / 2);

    const startRatio = Math.max(0, Math.min(1, minNote / 127));
    const rangeRatio = Math.max(0.015, Math.min(1 - startRatio, (maxNote - minNote) / 127));

    return {
      minNote,
      maxNote,
      avgNote,
      startPct: +(startRatio * 100).toFixed(2),
      widthPct: +(rangeRatio * 100).toFixed(2),
      display: `${formatNoteName(minNote)} - ${formatNoteName(maxNote)}`,
      avgDisplay: formatNoteName(avgNote)
    };
  }
</script>

<div class="tracks-view" class:drawer-expanded={isDrawerExpanded}>
  {#if playbackStore.activeSong}
    <!-- Top Bar Summary matching WPF TrackView.xaml -->
    <div class="top-summary-box">
      <!-- Leftmost: Playable percentage -->
      <div class="stat-group">
        <span class="stat-caption">{t('tracks.playable')}</span>
        <span class="stat-value">{playablePercentage.toFixed(1)}%</span>
      </div>

      <!-- Middle: Notes fraction -->
      <div class="stat-group middle">
        <span class="stat-caption">{t('tracks.playableNotes')}</span>
        <span class="stat-value">
          {accessibleNotes.toLocaleString()}
          <span class="subtle-slash">/</span>
          <span class="subtle-total">{totalNotes.toLocaleString()}</span>
        </span>
      </div>

      <!-- Right: Action Buttons (Matches WPF TrackView.xaml line 56-68 Button Variant=Ghost) -->
      <div class="header-actions">
        <!-- Show/hide empty tracks button -->
        <Button
          variant="ghost"
          size="icon"
          active={showEmptyTracks}
          onclick={() => showEmptyTracks = !showEmptyTracks}
          title={t('tracks.showEmptyTracks')}
          aria-label={t('tracks.showEmptyTracks')}
        >
          <FluentIcon name="Eye" size={18} />
        </Button>

        <!-- Expand / collapse all -->
        <Button
          variant="ghost"
          size="icon"
          onclick={toggleAllExpanded}
          title={t('tracks.expandAll')}
          aria-label={t('tracks.expandAll')}
        >
          <span class="chevron-rotator" class:rotated={isAnyExpanded}>
            <FluentIcon name="ChevronDown" size={16} />
          </span>
        </Button>
      </div>
    </div>

    <!-- Track Cards List -->
    <CustomScrollViewer
      class="tracks-scroll-viewer"
      contentClass="tracks-scroll-host"
      bind:scrollContainer={scrollHostEl}
      gap="8px"
      padding="0 0 16px 0"
      fadeHeight={36}
    >
      {#each tracks as track, idx (track.id ?? track.index ?? idx)}
        {@const trackId = (track.id ?? track.index ?? idx) as number}
        {@const trackIndex = (track.index ?? (track.id ? track.id - 1 : idx)) as number}
        {@const isExpanded = expandedTrackIds.has(trackId)}
        {@const trackVol = playbackStore.getTrackVolume(trackIndex)}
        {@const isMuted = (track.muted ?? track.isMuted ?? false) || trackVol === 0}
        {@const isTrackSounding = playbackStore.isPlaying && !isMuted && playbackStore.activeNotes.some(
          n => n.trackId === trackId || n.trackId === trackIndex || n.trackId === (idx + 1)
        )}
        {@const trackNum = (idx + 1).toString().padStart(2, '0')}
        {@const songDur = playbackStore.durationSeconds ?? (playbackStore.activeSong?.durationMs ? playbackStore.activeSong.durationMs / 1000 : 0)}
        {@const timing = getTrackTiming(track, songDur)}
        {@const pitchTiming = getTrackPitch(track)}

        <div class="track-item" class:muted={isMuted}>
          <!-- Main Card Header -->
          <div
            class="track-card"
            class:active-glow={isTrackSounding}
            role="button"
            tabindex="0"
            aria-expanded={isExpanded}
            onclick={(e) => handleCardClick(e, trackId)}
            onkeydown={(e) => handleCardKeyDown(e, trackId)}
          >
            <div class="track-card-row">
              <!-- Track Index (01, 02) -->
              <div class="track-num">{trackNum}</div>

              <!-- Green Toggle Switch & Track Name / Instrument -->
              <div class="track-identity">
                <button
                  class="fluent-switch"
                  class:checked={!isMuted}
                  class:interacted={interactedTrackIds.has(trackId)}
                  onclick={(e) => {
                    e.stopPropagation();
                    toggleTrackMute(track);
                  }}
                  title={t('tracks.trackMuteTooltip')}
                  aria-label={t('tracks.trackMuteTooltip')}
                  role="switch"
                  aria-checked={!isMuted}
                >
                  <div class="switch-thumb"></div>
                </button>

                <div class="name-instrument-stack">
                  <span class="track-title">{track.name || t('tracks.trackNum', { num: idx + 1 })}</span>
                  {#if track.instrument || track.instrumentName}
                    <span class="track-instrument">{track.instrument || track.instrumentName}</span>
                  {/if}
                </div>
              </div>

              <!-- Track Volume Slider Control (slowly animates to shrink when hidden) -->
              <GradientSweep visible={showSummaryVolume} direction="horizontal">
                <div
                  class="track-volume-wrap"
                  role="presentation"
                  onclick={(e) => e.stopPropagation()}
                  onkeydown={(e) => e.stopPropagation()}
                  title={t('tracks.trackVolumeTooltip')}
                >
                  <button
                    type="button"
                    class="track-vol-btn"
                    onclick={() => {
                      const current = playbackStore.getTrackVolume(trackIndex);
                      playbackStore.setTrackVolume(trackIndex, current > 0 ? 0 : 1.0);
                    }}
                    title={trackVol === 0 ? t('tracks.unmuteTrackAudio') : t('tracks.muteTrackAudio')}
                  >
                    <FluentIcon
                      name={trackVol === 0 ? 'Volume0' : trackVol < 0.5 ? 'Volume1' : 'Volume2'}
                      size={14}
                    />
                  </button>
                  <div class="track-slider-container">
                    <Slider
                      min={0}
                      max={1}
                      step={0.01}
                      smooth={true}
                      value={trackVol}
                      oninput={(val) => playbackStore.setTrackVolume(trackIndex, val)}
                      onchange={(val) => playbackStore.setTrackVolume(trackIndex, val)}
                      formatTooltip={(v) => `${Math.round(v * 100)}%`}
                      tooltipOnDragOnly={true}
                    />
                  </div>
                  <span class="track-vol-pct">{Math.round(trackVol * 100)}%</span>
                </div>
              </GradientSweep>

              <!-- Statistics -->
              <div class="stats-group">
                <div class="track-stat stat-playable" title={t('tracks.notesStatTooltip')}>
                  <span class="stat-label">{t('tracks.playable')}</span>
                  <span class="stat-num">{playbackStore.getTrackPlayablePercentage(track)}%</span>
                </div>

                <div class="track-stat stat-notes" title={t('tracks.totalNotesTooltip')}>
                  <span class="stat-label">{t('tracks.notes')}</span>
                  <span class="stat-num">{track.noteCount || track.notes?.length || 0}</span>
                </div>

                <GradientSweep visible={showSummaryBlackKeys} direction="horizontal" collapseMargin="left" marginAmount="16px">
                  <div class="track-stat stat-black-keys" title={t('tracks.blackKeysTooltip')}>
                    <span class="stat-label">{t('tracks.blackKeys')}</span>
                    <span class="stat-num">{Math.round(track.blackKeyRatio ?? 0)}%</span>
                  </div>
                </GradientSweep>

                <GradientSweep visible={showSummaryDuration} direction="horizontal" collapseMargin="left" marginAmount="16px">
                  <div class="track-stat stat-duration" title={t('tracks.avgDurationTooltip')}>
                    <span class="stat-label">{t('tracks.avgDuration')}</span>
                    <span class="stat-num">{Math.round(track.averageDurationMs || track.avgDurationMs || track.avgDuration || 0)}ms</span>
                  </div>
                </GradientSweep>

                <GradientSweep visible={showSummaryFrequent} direction="horizontal" collapseMargin="left" marginAmount="16px">
                  <div class="track-stat stat-frequent" title={t('tracks.frequentTooltip')}>
                    <span class="stat-label">{t('tracks.frequent')}</span>
                    <span class="stat-num">{Math.round(track.frequentNotesRatio ?? track.frequentRatio ?? 0)}%</span>
                  </div>
                </GradientSweep>
              </div>

              <!-- Chevron expand button (Matches WPF TrackView.xaml line 246 RotateToggleButton Variant=Ghost) -->
              <Button
                variant="ghost"
                size="icon"
                onclick={(e) => {
                  e.stopPropagation();
                  toggleTrackExpand(trackId);
                }}
                title={t('tracks.toggleDetails')}
                aria-label={t('tracks.toggleDetails')}
              >
                <span class="chevron-rotator" class:rotated={isExpanded}>
                  <FluentIcon name="ChevronDown" size={14} />
                </span>
              </Button>
            </div>
          </div>

          <!-- Expanded Details Panel (Accordion Slide-Down) -->
          <div class="accordion-track" class:expanded={isExpanded}>
            <div class="accordion-inner">
              <div class="expanded-panel">
                <div class="range-row">
                  <span class="range-title">{t('tracks.timeRange')}</span>
                  <div class="range-bar-track">
                    <div
                      class="range-bar-fill"
                      style="--target-left: {timing.startPct}%; --target-width: {timing.widthPct}%;"
                    ></div>
                  </div>
                  <span class="range-time">{timing.display}</span>
                </div>

                <div class="range-row">
                  <span class="range-title">{t('tracks.pitchRange')}</span>
                  <div class="range-bar-track">
                    <div
                      class="range-bar-fill pitch"
                      style="--target-left: {pitchTiming.startPct}%; --target-width: {pitchTiming.widthPct}%;"
                    ></div>
                  </div>
                  <span class="range-time">
                    {pitchTiming.display}
                    <span class="range-avg">({pitchTiming.avgDisplay})</span>
                  </span>
                </div>

                <!-- Overflow Section (reveals with sweep down gradient, smoothly adjusting accordion height) -->
                <GradientSweep visible={hasAnyOverflow} direction="vertical" gradientLength="24px">
                  <div class="overflow-row">
                    <!-- Volume Slider (only when not visible in summary card) -->
                    {#if showOverflowVolume}
                      <div class="overflow-vol-block">
                        <div
                          class="range-vol-expanded"
                          role="presentation"
                          onclick={(e) => e.stopPropagation()}
                          onkeydown={(e) => e.stopPropagation()}
                        >
                          <button
                            type="button"
                            class="track-vol-btn"
                            onclick={() => {
                              const current = playbackStore.getTrackVolume(trackIndex);
                              playbackStore.setTrackVolume(trackIndex, current > 0 ? 0 : 1.0);
                            }}
                            title={trackVol === 0 ? t('tracks.unmuteTrackAudio') : t('tracks.muteTrackAudio')}
                          >
                            <FluentIcon
                              name={trackVol === 0 ? 'Volume0' : trackVol < 0.5 ? 'Volume1' : 'Volume2'}
                              size={14}
                            />
                          </button>
                          <div class="track-slider-container">
                            <Slider
                              min={0}
                              max={1}
                              step={0.01}
                              smooth={true}
                              value={trackVol}
                              oninput={(val) => playbackStore.setTrackVolume(trackIndex, val)}
                              onchange={(val) => playbackStore.setTrackVolume(trackIndex, val)}
                              formatTooltip={(v) => `${Math.round(v * 100)}%`}
                            />
                          </div>
                          <span class="track-vol-pct">{Math.round(trackVol * 100)}%</span>
                        </div>
                      </div>
                    {:else}
                      <div class="overflow-title-spacer">
                        <span class="range-title">{t('tracks.details') || 'Details'}</span>
                      </div>
                    {/if}

                    <!-- Overflow Stats (only when hidden from summary card) -->
                    <div class="overflow-stats-block">
                      <GradientSweep visible={showOverflowBlackKeys} direction="horizontal" collapseMargin="left" marginAmount="16px">
                        <div class="track-stat stat-black-keys" title={t('tracks.blackKeysTooltip')}>
                          <span class="stat-label">{t('tracks.blackKeys')}</span>
                          <span class="stat-num">{Math.round(track.blackKeyRatio ?? 0)}%</span>
                        </div>
                      </GradientSweep>

                      <GradientSweep visible={showOverflowDuration} direction="horizontal" collapseMargin="left" marginAmount="16px">
                        <div class="track-stat stat-duration" title={t('tracks.avgDurationTooltip')}>
                          <span class="stat-label">{t('tracks.avgDuration')}</span>
                          <span class="stat-num">{Math.round(track.averageDurationMs || track.avgDurationMs || track.avgDuration || 0)}ms</span>
                        </div>
                      </GradientSweep>

                      <GradientSweep visible={showOverflowFrequent} direction="horizontal" collapseMargin="left" marginAmount="16px">
                        <div class="track-stat stat-frequent" title={t('tracks.frequentTooltip')}>
                          <span class="stat-label">{t('tracks.frequent')}</span>
                          <span class="stat-num">{Math.round(track.frequentNotesRatio ?? track.frequentRatio ?? 0)}%</span>
                        </div>
                      </GradientSweep>
                    </div>
                  </div>
                </GradientSweep>
              </div>
            </div>
          </div>
        </div>
      {/each}
    </CustomScrollViewer>
  {:else}
    <div class="no-song-panel">
      <FluentIcon name="MusicNote2" size={48} class="empty-icon" />
      <span class="empty-title">{t('tracks.noSongLoaded')}</span>
      <span class="empty-sub">{t('tracks.noSongLoadedSub')}</span>
    </div>
  {/if}
</div>

<style>
  .tracks-view {
    display: flex;
    flex-direction: column;
    height: 100%;
    width: 100%;
    padding: 12px 18px;
    overflow: hidden;
    user-select: none;
  }

  /* Top Bar Summary matching WPF TrackView.xaml */
  .top-summary-box {
    display: flex;
    align-items: center;
    justify-content: space-between;
    padding: 12px 16px;
    margin-bottom: 10px;
    border: 1px solid var(--card-stroke);
    border-radius: 6px;
    background: var(--card-bg);
    box-shadow: var(--card-shadow);
    flex-shrink: 0;
  }

  .stat-group {
    display: flex;
    flex-direction: column;
  }

  .stat-group.middle {
    flex: 1;
    margin-left: 32px;
  }

  .stat-caption {
    font-size: 11px;
    color: var(--text-primary);
    opacity: 0.6;
    line-height: 1.2;
  }

  .stat-value {
    font-size: 14px;
    font-weight: 600;
    color: var(--text-primary);
    line-height: 1.3;
    margin-top: 2px;
  }

  .subtle-slash {
    color: var(--text-secondary);
    margin: 0 2px;
    opacity: 0.6;
  }

  .subtle-total {
    color: var(--text-secondary);
    opacity: 0.8;
  }

  .header-actions {
    display: flex;
    align-items: center;
    gap: 8px;
  }


  /* Chevron Rotator (180deg animated rotation) */
  .chevron-rotator {
    display: inline-flex;
    align-items: center;
    justify-content: center;
    transition: transform var(--transition-normal, 0.22s cubic-bezier(0.1, 0.9, 0.2, 1));
    transform-origin: center center;
    will-change: transform;
  }

  .chevron-rotator.rotated {
    transform: rotate(180deg);
  }

  /* Tracks Scroll Host */
  :global(.tracks-scroll-viewer) {
    flex: 1;
    min-height: 0;
    width: 100%;
  }

  .track-item {
    display: flex;
    flex-direction: column;
    position: relative;
    width: 100%;
    opacity: 1;
    transition: opacity var(--transition-normal, 0.22s cubic-bezier(0, 0, 0.2, 1));
    will-change: opacity;
  }

  .track-item.muted {
    opacity: 0.55;
  }

  /* Card matching WPF Border */
  .track-card {
    position: relative;
    border: 1px solid var(--card-stroke);
    border-radius: 6px;
    background: var(--card-bg);
    box-shadow: var(--card-shadow);
    display: flex;
    flex-direction: column;
    overflow: hidden;
    z-index: 2;
    cursor: pointer;
    width: 100%;
    box-sizing: border-box;
    container-type: inline-size;
    container-name: track-card;
    transition: background var(--transition-fast, 0.14s ease),
                border-color var(--transition-fast, 0.14s ease),
                box-shadow var(--transition-fast, 0.14s ease);
  }

  .track-card:hover {
    background: var(--card-hover-bg);
    border-color: var(--card-stroke-hover);
    box-shadow: var(--card-shadow-hover);
  }

  .track-card:focus-visible {
    outline: 2px solid var(--accent);
    outline-offset: -1px;
  }

  /* Note Played Active Glow matching WPF DropShadowEffect */
  .track-card.active-glow {
    border-color: var(--accent);
    box-shadow: 0 0 16px var(--accent-glow);
  }

  .track-card-row {
    display: flex;
    align-items: center;
    padding: 12px 14px;
    gap: 16px;
    width: 100%;
    box-sizing: border-box;
    min-width: 0;
  }

  .track-num {
    font-size: 16px;
    font-weight: 700;
    color: var(--text-primary);
    opacity: 0.3;
    min-width: 24px;
  }

  /* Green Toggle Switch matching WPF ToggleSwitch */
  .track-identity {
    display: flex;
    align-items: center;
    gap: 12px;
    min-width: 110px;
    flex: 1 1 auto;
    overflow: hidden;
  }

  .fluent-switch {
    width: 40px;
    height: 20px;
    border-radius: 10px;
    background: var(--switch-bg);
    border: 1px solid var(--switch-border);
    position: relative;
    cursor: pointer;
    transition: background var(--transition-fast, 0.18s cubic-bezier(0.1, 0.9, 0.2, 1)),
                border-color var(--transition-fast, 0.18s cubic-bezier(0.1, 0.9, 0.2, 1));
    padding: 0;
    outline: none;
    flex-shrink: 0;
  }

  .fluent-switch:hover {
    background: var(--switch-hover-bg);
    border-color: var(--switch-hover-border);
  }

  .fluent-switch.checked {
    background: var(--accent-color, var(--accent, #1DB954));
    border-color: var(--accent-color, var(--accent, #1DB954));
  }

  .fluent-switch.checked:hover {
    background: var(--accent-color, var(--accent, #1DB954));
    border-color: var(--accent-color, var(--accent, #1DB954));
    filter: brightness(1.06);
  }

  .switch-thumb {
    width: 14px;
    height: 14px;
    position: absolute;
    top: 2px;
    left: 2px;
    transform: translateX(0);
    transition: transform var(--transition-normal, 0.2s cubic-bezier(0.2, 0.9, 0.3, 1));
    will-change: transform;
    pointer-events: none;
  }

  .fluent-switch.checked .switch-thumb {
    transform: translateX(20px);
  }

  /* Inner visual circle that handles the stretch/smear without interrupting translation */
  .switch-thumb::before {
    content: '';
    display: block;
    width: 100%;
    height: 100%;
    border-radius: 7px;
    background: var(--switch-thumb-off, #CECECE);
    box-shadow: 0 1px 3px rgba(0, 0, 0, 0.35);
    transform-origin: center center;
    transform: scale(1, 1);
    will-change: transform;
    transition: background-color var(--transition-fast, 0.15s ease);
  }

  .fluent-switch:hover:not(.checked) .switch-thumb::before {
    background: var(--switch-thumb-off-hover, #FFFFFF);
  }

  .fluent-switch.checked .switch-thumb::before {
    background: var(--switch-thumb-checked, #000000);
  }

  /* Pure scale smear keyframes — independent of translation for silky 60/120fps glide */
  @keyframes thumb-smear-on {
    0% {
      transform: scale(1, 1);
    }
    40% {
      transform: scale(1.36, 0.9);
    }
    100% {
      transform: scale(1, 1);
    }
  }

  @keyframes thumb-smear-off {
    0% {
      transform: scale(1, 1);
    }
    40% {
      transform: scale(1.36, 0.9);
    }
    100% {
      transform: scale(1, 1);
    }
  }

  /* Only run the smear stretch animation on user toggle */
  .fluent-switch.interacted.checked .switch-thumb::before {
    animation: thumb-smear-on var(--anim-duration, 0.2s) cubic-bezier(0.2, 0.9, 0.3, 1);
  }

  .fluent-switch.interacted:not(.checked) .switch-thumb::before {
    animation: thumb-smear-off var(--anim-duration, 0.2s) cubic-bezier(0.2, 0.9, 0.3, 1);
  }


  .name-instrument-stack {
    display: flex;
    flex-direction: column;
    min-width: 0;
    overflow: hidden;
  }

  .track-title {
    font-size: 13.5px;
    font-weight: 600;
    color: var(--text-primary);
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
  }

  .track-instrument {
    font-size: 11px;
    color: var(--text-secondary);
    opacity: 0.7;
    margin-top: 1px;
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
  }

  /* Statistics matching WPF SimpleStackPanel */
  .stats-group {
    display: flex;
    align-items: center;
    gap: 0;
    margin-left: auto;
    flex-shrink: 0;
  }

  .stats-group .stat-notes {
    margin-left: 16px;
  }

  .track-stat {
    display: flex;
    flex-direction: column;
    align-items: center;
    min-width: 50px;
  }

  .stat-label {
    font-size: 10px;
    color: var(--text-primary);
    opacity: 0.6;
    white-space: nowrap;
  }

  .stat-num {
    font-size: 12.5px;
    font-weight: 600;
    color: var(--text-primary);
    margin-top: 2px;
    white-space: nowrap;
  }

  /* Overflow section in accordion details */
  .overflow-row {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    justify-content: space-between;
    gap: 16px;
    width: 100%;
    min-height: 36px;
    box-sizing: border-box;
  }

  .overflow-vol-block {
    display: flex;
    align-items: center;
    flex: 1;
    min-width: 160px;
    max-width: 520px;
  }

  .overflow-title-spacer {
    display: flex;
    align-items: center;
  }

  .overflow-stats-block {
    display: flex;
    align-items: center;
    margin-left: auto;
    flex-shrink: 0;
  }

  .overflow-stats-block .track-stat {
    display: flex;
    flex-direction: column;
    align-items: center;
    min-width: 50px;
  }

  .overflow-stats-block .stat-label {
    font-size: 10px;
    color: var(--text-primary);
    opacity: 0.6;
    white-space: nowrap;
  }

  .overflow-stats-block .stat-num {
    font-size: 12.5px;
    font-weight: 600;
    color: var(--text-primary);
    margin-top: 2px;
    white-space: nowrap;
  }

  @container track-card (max-width: 560px) {
    .overflow-row {
      gap: 12px;
    }
  }

  @container track-card (max-width: 480px) {
    .track-card-row {
      padding: 10px 10px;
      gap: 10px;
    }
    .track-identity {
      gap: 8px;
    }
  }


  /* Accordion Slide-Down matching WPF ScaleY + Opacity Storyboards */
  .accordion-track {
    display: grid;
    grid-template-rows: 0fr;
    transition: grid-template-rows var(--transition-slow, 0.25s cubic-bezier(0.1, 0.9, 0.2, 1));
    position: relative;
    margin-top: -4px;
    z-index: 1;
  }

  .accordion-track.expanded {
    grid-template-rows: 1fr;
  }

  .accordion-inner {
    min-height: 0;
    overflow: hidden;
    --accordion-mask: transparent;
    -webkit-mask-image: linear-gradient(
      to bottom,
      #000000 0%,
      #000000 calc(100% - 24px),
      var(--accordion-mask, #000000) 100%
    );
    mask-image: linear-gradient(
      to bottom,
      #000000 0%,
      #000000 calc(100% - 24px),
      var(--accordion-mask, #000000) 100%
    );
    -webkit-mask-size: 100% 100%;
    mask-size: 100% 100%;
    -webkit-mask-repeat: no-repeat;
    mask-repeat: no-repeat;
    transition: --accordion-mask var(--transition-slow, 0.25s cubic-bezier(0.1, 0.9, 0.2, 1));
  }

  .accordion-track.expanded .accordion-inner {
    --accordion-mask: #000000;
  }

  .expanded-panel {
    border: 1px solid var(--card-inset-stroke);
    border-top: none;
    border-radius: 0 0 6px 6px;
    padding: 12px 16px 14px 16px;
    background: var(--card-inset-bg);
    box-shadow: var(--card-inset-shadow);
    display: flex;
    flex-direction: column;
    gap: 10px;
    opacity: 0;
    transform: translateY(-8px);
    transition: opacity var(--transition-normal, 0.2s cubic-bezier(0.1, 0.9, 0.2, 1)),
                transform var(--transition-slow, 0.25s cubic-bezier(0.1, 0.9, 0.2, 1));
  }

  .accordion-track.expanded .expanded-panel {
    opacity: 1;
    transform: translateY(0);
  }

  .range-row {
    display: flex;
    align-items: center;
    gap: 12px;
    font-size: 11.5px;
  }

  .range-title {
    width: 90px;
    color: var(--text-secondary);
    flex-shrink: 0;
  }

  .range-bar-track {
    flex: 1;
    height: 5px;
    border-radius: 3px;
    background: var(--track-groove-bg);
    position: relative;
    overflow: hidden;
  }

  .range-bar-fill {
    position: absolute;
    top: 0;
    bottom: 0;
    background: var(--accent);
    border-radius: 2px;
    left: 0%;
    width: 0%;
    opacity: 0;
    transition: left var(--transition-fast, 0.15s ease),
                width var(--transition-fast, 0.15s ease),
                opacity var(--transition-fast, 0.1s ease);
  }

  .range-bar-fill.pitch {
    background: #00cc99; /* WPF color: #00cc99 mint/teal */
  }

  /* Linear bars animate to move from the left along with the offset */
  .accordion-track.expanded .range-bar-fill {
    left: var(--target-left);
    width: var(--target-width);
    opacity: 1;
    transition: left var(--transition-slow, 0.45s cubic-bezier(0.16, 1, 0.3, 1)),
                width var(--transition-slow, 0.45s cubic-bezier(0.16, 1, 0.3, 1)),
                opacity var(--transition-normal, 0.2s ease);
  }

  .range-time {
    font-size: 11px;
    font-variant-numeric: tabular-nums;
    color: var(--text-secondary);
    min-width: 90px;
    text-align: right;
    flex-shrink: 0;
  }

  .range-avg {
    font-size: 10px;
    opacity: 0.65;
    font-weight: 400;
    margin-left: 2px;
  }

  /* Track Volume Mixer Controls */
  .track-volume-wrap {
    display: flex;
    align-items: center;
    gap: 6px;
    min-width: 120px;
    max-width: 160px;
    flex-shrink: 0;
  }

  .track-slider-container {
    flex: 1;
    min-width: 60px;
  }

  .range-vol-expanded {
    flex: 1;
    display: flex;
    align-items: center;
    gap: 8px;
    width: 100%;
    max-width: 100%;
  }

  .track-vol-btn {
    background: transparent;
    border: none;
    color: var(--text-primary);
    cursor: pointer;
    display: flex;
    align-items: center;
    justify-content: center;
    padding: 2px;
    opacity: 0.6;
    border-radius: 4px;
    flex-shrink: 0;
    transition: opacity var(--transition-fast), color var(--transition-fast);
  }

  .track-vol-btn:hover {
    opacity: 1;
    color: var(--accent-color, var(--accent, #1DB954));
  }

  .track-vol-pct {
    font-size: 10.5px;
    font-variant-numeric: tabular-nums;
    color: var(--text-secondary);
    min-width: 28px;
    text-align: right;
    flex-shrink: 0;
  }

  /* No song */
  .no-song-panel {
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    height: 100%;
    color: var(--text-tertiary);
    gap: 8px;
  }

  .empty-icon {
    opacity: 0.3;
  }

  .empty-title {
    font-size: 16px;
    font-weight: 600;
  }

  .empty-sub {
    font-size: 13px;
  }
</style>
