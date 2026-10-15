<script lang="ts">
  import SongInfoControl from './SongInfoControl.svelte';
  import MainPlaybackControl from './MainPlaybackControl.svelte';
  import AudioSettingsControl from './AudioSettingsControl.svelte';
  import { uiStore } from '../../state/uiStore.svelte';
  import { tooltipManager } from '../../ui/tooltipManager.svelte';
</script>

<footer
  class="player-control-panel"
  data-tooltip-disabled={uiStore.isBottomPickerActive ? 'true' : undefined}
  onclickcapture={() => {
    tooltipManager.hide();
  }}
>
  <SongInfoControl />
  <MainPlaybackControl />
  <AudioSettingsControl />
</footer>

<style>
  .player-control-panel {
    height: var(--player-panel-height, 90px);
    background: var(--bg-app, #000000);
    border-top: none;
    display: grid;
    grid-template-columns: minmax(0, 1fr) auto minmax(0, 1fr);
    align-items: center;
    padding: 0 16px;
    gap: 16px;
    z-index: 60;
    user-select: none;
    flex-shrink: 0;
    box-sizing: border-box;
    container-type: inline-size;
  }
</style>
