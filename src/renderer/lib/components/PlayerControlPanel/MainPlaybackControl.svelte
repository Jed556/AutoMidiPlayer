<script lang="ts">
  import { playbackStore } from '../../state/playbackStore.svelte';
  import { t } from '../../i18n';
  import FluentIcon from '../Common/FluentIcon.svelte';
  import PlayPauseIcon from '../Common/PlayPauseIcon.svelte';
  import PedalIcon from '../Common/PedalIcon.svelte';
  import LoopIcon from '../Common/LoopIcon.svelte';
  import PedalStatusPopup from './PedalStatusPopup.svelte';
  import Seekbar from './Seekbar.svelte';

  let showPedals = $state(false);
  let previewValue = $state(0);
  let isPreviewActive = $state(false);
  let pedalHoverTimer: ReturnType<typeof setTimeout> | null = null;

  function handlePedalMouseEnter() {
    if (pedalHoverTimer) {
      clearTimeout(pedalHoverTimer);
      pedalHoverTimer = null;
    }
    showPedals = true;
  }

  function handlePedalMouseLeave() {
    if (pedalHoverTimer) {
      clearTimeout(pedalHoverTimer);
    }
    pedalHoverTimer = setTimeout(() => {
      showPedals = false;
      pedalHoverTimer = null;
    }, 120);
  }

  function handlePedalClick() {
    playbackStore.togglePedals();
  }

  function togglePlay() {
    playbackStore.togglePlay();
  }

  function handlePrev() {
    playbackStore.prevTrack();
  }

  function handleNext() {
    playbackStore.nextTrack();
  }

  function toggleShuffle() {
    playbackStore.toggleShuffle();
  }

  function cycleLoopMode() {
    playbackStore.cycleLoopMode();
  }

  function toggleListenMode() {
    playbackStore.toggleListenMode();
  }

  function formatTime(seconds?: number): string {
    if (seconds === undefined || seconds === null || isNaN(seconds)) return '0:00';
    const sVal = Math.max(0, seconds);
    const mins = Math.floor(sVal / 60);
    const secs = Math.floor(sVal % 60);
    return `${mins}:${secs < 10 ? '0' : ''}${secs}`;
  }
</script>

<div class="main-playback-control">
  <!-- Control Buttons matching WPF MainPlaybackControl -->
  <div class="controls-row">
    <!-- Left transport side: Pedal, Shuffle, Previous (spacing: 12px) -->
    <div class="transport-group">
      <!-- Pedal Status Button -->
      <div
        class="pedal-anchor"
        role="group"
        onmouseenter={handlePedalMouseEnter}
        onmouseleave={handlePedalMouseLeave}
        onfocusin={handlePedalMouseEnter}
        onfocusout={handlePedalMouseLeave}
      >
        <button
          type="button"
          class="ctrl-btn icon-only pedal-btn"
          class:pedals-on={playbackStore.pedals.enabled}
          class:pedals-off={!playbackStore.pedals.enabled}
          onclick={handlePedalClick}
          aria-label={t('player.pedalsTooltip')}
          aria-pressed={playbackStore.pedals.enabled}
          disabled={!playbackStore.supportsPedals}
        >
          <PedalIcon
            isSoftActive={playbackStore.pedals.enabled && playbackStore.pedals.unaCorda}
            isSostenutoActive={playbackStore.pedals.enabled && playbackStore.pedals.sostenuto}
            isSustainActive={playbackStore.pedals.enabled && playbackStore.pedals.sustain}
            isActive={playbackStore.pedals.enabled}
            size={15}
          />
        </button>
        <PedalStatusPopup open={showPedals} />
      </div>

      <!-- Shuffle -->
      <button
        class="ctrl-btn icon-only"
        class:active={playbackStore.isShuffled}
        onclick={toggleShuffle}
        disabled={playbackStore.queue.length <= 1}
        title={playbackStore.isShuffled ? t('player.shuffleOn') : t('player.shuffleOff')}
      >
        <FluentIcon name="Shuffle" size={14} active={playbackStore.isShuffled} />
      </button>

      <!-- Previous -->
      <button
        class="ctrl-btn icon-only"
        onclick={handlePrev}
        disabled={!playbackStore.activeSong && playbackStore.queue.length === 0}
        title={t('player.previous')}
      >
        <FluentIcon name="Previous" size={14} />
      </button>
    </div>

    <!-- Center Play/Pause Circle Button -->
    <button
      class="play-main-btn"
      class:playing={playbackStore.isPlaying}
      onclick={togglePlay}
      disabled={!playbackStore.activeSong && playbackStore.queue.length === 0}
      title={playbackStore.isPlaying ? t('player.pause') : t('player.play')}
    >
      <PlayPauseIcon playing={playbackStore.isPlaying} size={16} />
    </button>

    <!-- Right transport side: Next, Loop, Listen Mode (Headphones) -->
    <div class="transport-group">
      <!-- Next -->
      <button
        class="ctrl-btn icon-only"
        onclick={handleNext}
        disabled={playbackStore.queue.length === 0 || (playbackStore.queue.length === 1 && playbackStore.loopMode === 'off')}
        title={t('player.next')}
      >
        <FluentIcon name="Next" size={14} />
      </button>

      <!-- Loop Mode -->
      <button
        class="ctrl-btn icon-only"
        class:active={playbackStore.loopMode !== 'off'}
        onclick={cycleLoopMode}
        title={t('player.loopMode', { mode: t('player.loop' + (playbackStore.loopMode === 'single' ? 'Single' : playbackStore.loopMode === 'all' ? 'All' : 'Off')) })}
      >
        <LoopIcon mode={playbackStore.loopMode} size={14} active={playbackStore.loopMode !== 'off'} />
      </button>

      <!-- Listen Mode toggle using Headphones icon -->
      <button
        class="ctrl-btn icon-only listen-btn"
        class:active={playbackStore.isListenMode}
        onclick={toggleListenMode}
        title={playbackStore.isListenMode ? t('player.listenModeActive') : t('player.listenModeInactive')}
      >
        <FluentIcon name="Headphones" size={16} filled={playbackStore.isListenMode} />
      </button>
    </div>
  </div>

  <!-- Progress Bar (Grid MinWidth 400 with current, seekbar, total) -->
  <div class="seekbar-row">
    <span class="time-label current">
      {formatTime(isPreviewActive ? previewValue : playbackStore.currentTimeSeconds)}
    </span>

    <div class="seek-slider-wrap">
      <Seekbar
        songId={playbackStore.activeSong?.id}
        value={playbackStore.currentTimeSeconds}
        max={playbackStore.durationSeconds || 100}
        disabled={!playbackStore.activeSong}
        bind:previewValue
        bind:isPreviewActive
        onseek={(val, opts) => playbackStore.seek(val, opts)}
      />
    </div>

    <span class="time-label total">{formatTime(playbackStore.durationSeconds)}</span>
  </div>
</div>

<style>
  .main-playback-control {
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    gap: 8px;
    box-sizing: border-box;
    width: 100%;
    max-width: 500px;
    min-width: 260px;
    user-select: none;
  }

  .controls-row {
    display: flex;
    align-items: center;
    gap: 12px;
  }

  .transport-group {
    display: flex;
    align-items: center;
    gap: 12px;
  }

  .pedal-anchor {
    position: relative;
    display: flex;
    align-items: center;
  }

  .ctrl-btn {
    width: 32px;
    height: 32px;
    border-radius: 4px;
    background: transparent;
    border: none;
    color: var(--text-primary);
    opacity: 0.35;
    display: flex;
    align-items: center;
    justify-content: center;
    cursor: pointer;
    transition: opacity var(--transition-normal, 0.22s cubic-bezier(0, 0, 0.2, 1)), color var(--transition-fast, 0.16s ease);
    padding: 0;
  }

  .ctrl-btn:hover {
    background: transparent;
    opacity: 1;
    color: var(--text-primary);
  }

  .ctrl-btn.active {
    color: var(--accent-color, var(--accent, #1DB954));
    opacity: 0.85;
  }

  .ctrl-btn.active:hover {
    color: var(--accent-color, var(--accent, #1DB954));
    opacity: 1;
  }

  .pedal-btn {
    color: var(--text-primary);
    opacity: 0.35;
  }

  .pedal-btn:hover {
    background: transparent;
    opacity: 1;
    color: var(--text-primary);
  }

  .pedal-btn.pedals-on {
    opacity: 0.4;
  }

  .pedal-btn.pedals-on:hover {
    opacity: 1;
  }

  .pedal-btn.pedals-off {
    opacity: 0.5;
  }

  .pedal-btn.pedals-off:hover {
    opacity: 0.85;
  }

  .pedal-btn:disabled {
    opacity: 0.15;
    cursor: not-allowed;
  }

  .listen-btn.active {
    color: var(--accent-color, var(--accent, #1DB954));
    opacity: 0.85;
  }

  .listen-btn.active:hover {
    color: var(--accent-color, var(--accent, #1DB954));
    opacity: 1;
  }

  /* Spotify-style PlayPauseButton: 32x32 circular button */
  .play-main-btn {
    width: 32px;
    height: 32px;
    border-radius: 50%;
    background: var(--text-primary);
    color: var(--bg-primary);
    border: none;
    display: flex;
    align-items: center;
    justify-content: center;
    cursor: pointer;
    margin: 0 4px;
    transition: transform var(--transition-fast, 0.14s ease), background var(--transition-fast, 0.14s ease);
  }

  .play-main-btn:hover {
    transform: scale(1.06);
    background: #ffffff;
  }

  .play-main-btn:active {
    transform: scale(0.96);
  }

  .seekbar-row {
    display: flex;
    align-items: center;
    gap: 8px;
    width: 100%;
    min-width: 220px;
    max-width: 500px;
  }

  .time-label {
    font-size: 11px;
    font-variant-numeric: tabular-nums;
    color: var(--text-primary);
    opacity: 0.7;
    min-width: 32px;
    height: 20px;
    line-height: 20px;
    text-align: center;
  }

  .seek-slider-wrap {
    flex: 1;
    display: flex;
    align-items: center;
  }
</style>
