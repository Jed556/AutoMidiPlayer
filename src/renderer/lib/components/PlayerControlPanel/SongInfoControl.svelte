<script lang="ts">
  import { playbackStore } from '../../state/playbackStore.svelte';
  import { t } from '../../i18n';
  import FluentIcon from '../Icons/FluentIcon.svelte';

  import OverflowText from '../Common/OverflowText.svelte';

  let currentTitle = $derived(playbackStore.activeSong?.title || t('player.noSongPlaying'));
  let currentArtist = $derived(playbackStore.activeSong?.artist || t('player.unknownArtist'));
</script>

<div class="song-info-control">
  <div class="cover-box">
    {#if playbackStore.activeSong?.coverArtUrl}
      <img
        src={playbackStore.activeSong.coverArtUrl}
        alt={playbackStore.activeSong.title}
        class="cover-img"
      />
    {:else}
      <div class="note-icon-wrap">
        <FluentIcon name="MusicNote2" size={24} filled={true} />
      </div>
    {/if}
  </div>

  <div class="info-stack">
    <div
      class="title-text"
      title={currentTitle}
      data-tooltip-follow="false"
    >
      <OverflowText
        text={currentTitle}
        hoverBehavior="loop"
        hoverTarget=".song-info-control"
        showTooltip={false}
      />
    </div>
    <div
      class="artist-text"
      title={currentArtist}
      data-tooltip-follow="false"
    >
      <OverflowText
        text={currentArtist}
        hoverBehavior="loop"
        hoverTarget=".song-info-control"
        showTooltip={false}
      />
    </div>
  </div>
</div>

<style>
  .song-info-control {
    display: flex;
    align-items: center;
    width: 100%;
    min-width: 0;
    overflow: hidden;
    user-select: none;
  }

  .cover-box {
    width: 56px;
    height: 56px;
    min-width: 56px;
    min-height: 56px;
    aspect-ratio: 1 / 1;
    border-radius: 4px;
    background: var(--control-fill);
    border: none;
    display: flex;
    align-items: center;
    justify-content: center;
    overflow: hidden;
    flex-shrink: 0;
  }

  .cover-img {
    width: 100%;
    height: 100%;
    object-fit: cover;
  }

  .note-icon-wrap {
    color: var(--text-primary);
    opacity: 0.4;
    display: flex;
    align-items: center;
    justify-content: center;
  }

  .info-stack {
    display: flex;
    flex-direction: column;
    justify-content: center;
    margin-left: 14px;
    min-width: 0;
    flex: 1;
    overflow: hidden;
    position: relative;
    padding-right: 2px;
  }

  /* Anchor song info tooltips to the visible text, not the container */
  .title-text,
  .artist-text {
    width: fit-content;
    max-width: 100%;
  }

  .info-stack :global(.overflow-text) {
    width: fit-content;
  }

  .title-text {
    font-size: 14px;
    font-weight: 600;
    color: var(--text-primary);
    line-height: 1.25;
  }

  .artist-text {
    font-size: 12px;
    opacity: 0.6;
    color: var(--text-primary);
    line-height: 1.25;
    margin-top: 2px;
  }
</style>
