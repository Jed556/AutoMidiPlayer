<script lang="ts">
  import type { KeyChangeMarker } from '$shared/types/midi';
  import Button from '../Common/Button.svelte';
  import { t } from '../../i18n';

  interface Props {
    marker: KeyChangeMarker;
    x: number;
    y: number;
    onDelete: (markerId: string) => void;
    onEdit?: (marker: KeyChangeMarker) => void;
    onClose?: () => void;
  }

  let { marker, x, y, onDelete, onEdit, onClose }: Props = $props();

  function formatTime(ms: number): string {
    const totalSeconds = ms / 1000;
    const minutes = Math.floor(totalSeconds / 60);
    const seconds = totalSeconds % 60;
    const padM = String(minutes).padStart(2, '0');
    const padS = seconds.toFixed(2).padStart(5, '0');
    return `${padM}:${padS}`;
  }
</script>

<!-- svelte-ignore a11y_no_static_element_interactions -->
<div
  class="marker-action-popup"
  style="left: {x}px; top: {y}px;"
  onpointerdown={(e) => e.stopPropagation()}
  onmousedown={(e) => e.stopPropagation()}
>
  <div class="popup-inner">
    <div class="marker-title" title="{marker.noteDisplay || marker.key} ({marker.offsetDisplay})">
      {marker.noteDisplay || marker.key} ({marker.offsetDisplay})
    </div>
    <div class="marker-time">
      {formatTime(marker.timeMs ?? Math.round((marker.time ?? 0) * 1000))}
    </div>

    <div class="popup-actions">
      <Button
        variant="ghost"
        colorMode="danger"
        icon="Delete"
        iconFilled={true}
        class="action-btn"
        title={t('studio.deleteMarkerTooltip') || 'Delete marker'}
        onclick={() => onDelete(marker.id)}
      />

      <Button
        variant="ghost"
        icon="Edit"
        iconFilled={true}
        class="action-btn"
        title={t('studio.editMarkerTooltip') || 'Edit marker key offset'}
        onclick={() => onEdit?.(marker)}
      />
    </div>
  </div>
</div>

<style>
  .marker-action-popup {
    position: absolute;
    transform: translate(-50%, 0);
    z-index: 100;
    pointer-events: auto;
    animation: popup-enter 160ms cubic-bezier(0.16, 1, 0.3, 1);
  }

  @keyframes popup-enter {
    from {
      opacity: 0;
      transform: translate(-50%, -4px) scale(0.96);
    }
    to {
      opacity: 1;
      transform: translate(-50%, 0) scale(1);
    }
  }

  .popup-inner {
    width: 90px;
    background: var(--bg-surface-elevated, #242428);
    border: 1px solid var(--border-default, rgba(255, 255, 255, 0.12));
    border-radius: 8px;
    padding: 7px 8px;
    display: flex;
    flex-direction: column;
    align-items: center;
    box-shadow: 0 8px 24px rgba(0, 0, 0, 0.45), 0 2px 6px rgba(0, 0, 0, 0.25);
    backdrop-filter: blur(12px);
  }

  .marker-title {
    font-size: 11.5px;
    font-weight: 600;
    color: var(--text-primary, #ffffff);
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
    max-width: 78px;
    text-align: center;
    line-height: 1.2;
  }

  .marker-time {
    font-size: 10.5px;
    color: var(--text-secondary, rgba(255, 255, 255, 0.65));
    margin-top: 2px;
    margin-bottom: 6px;
    font-variant-numeric: tabular-nums;
    text-align: center;
  }

  .popup-actions {
    display: flex;
    align-items: center;
    gap: 4px;
  }

  .popup-actions :global(.action-btn) {
    width: 26px !important;
    height: 26px !important;
    padding: 0 !important;
    display: flex !important;
    align-items: center !important;
    justify-content: center !important;
    border-radius: 5px !important;
  }
</style>
