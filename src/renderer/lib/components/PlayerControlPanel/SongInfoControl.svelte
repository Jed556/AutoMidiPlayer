<script lang="ts">
  import { playbackStore } from '../../state/playbackStore.svelte';
  import { t } from '../../i18n';
  import FluentIcon from '../Common/FluentIcon.svelte';

  import OverflowText from '../Common/OverflowText.svelte';
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
    <div class="title-text">
      <OverflowText
        text={playbackStore.activeSong?.title || t('player.noSongPlaying')}
        hoverBehavior="loop"
        hoverTarget=".song-info-control"
      />
    </div>
    <div class="artist-text">
      <OverflowText
        text={playbackStore.activeSong?.artist || t('player.unknownArtist')}
        hoverBehavior="loop"
        hoverTarget=".song-info-control"
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
