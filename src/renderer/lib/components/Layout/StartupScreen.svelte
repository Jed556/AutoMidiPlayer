<script lang="ts">
  import { onDestroy } from 'svelte';
  import { startupProgress, frontendStartupProgress } from '../../backend/startupProgress';
  import { t } from '../../i18n';

  let { status, finished, entering = false, progress, onentered, onfinished }: {
    status: string;
    finished: boolean;
    entering?: boolean;
    progress?: number;
    onentered?: () => void;
    onfinished: () => void;
  } = $props();

  let displayedProgress = $state(startupProgress.current);
  const unsubscribe = startupProgress.subscribe((value) => { displayedProgress = value; });
  onDestroy(unsubscribe);

  $effect(() => {
    if (progress !== undefined) startupProgress.advance(progress);
    else startupProgress.advanceStartup(finished ? 100 : frontendStartupProgress[status] ?? 48);
  });
</script>

<div
  class="startup-screen"
  class:finished
  class:entering
  data-tauri-drag-region
  onanimationend={(event) => {
    if (event.target !== event.currentTarget) return;
    // Svelte scopes keyframe names, so use the screen's current phase.
    if (finished) onfinished();
    else if (entering) onentered?.();
  }}
>
  <img class="startup-logo" src="./logo-fill.svg" alt={t('titleBar.appName')} draggable="false" />
  <div class="startup-status" role="status" aria-live="polite" aria-atomic="true">
    {#key status}
      <p class="startup-text">{t(status)}</p>
    {/key}
    <div
      class="startup-progress"
      role="progressbar"
      aria-label={t(status)}
      aria-valuemin={0}
      aria-valuemax={100}
      aria-valuenow={Math.round(displayedProgress)}
    >
      <div class="startup-progress-fill" style:transform="scaleX({displayedProgress / 100})"></div>
    </div>
  </div>
</div>

<style>
  .startup-screen {
    position: fixed;
    inset: 0;
    z-index: 1000;
    display: grid;
    place-items: center;
    background: var(--card-bg, #1f1f1f);
    color: var(--text-tertiary, rgba(255, 255, 255, 0.45));
  }

  .startup-logo,
  :global(:root.no-animations) .startup-logo {
    width: 88px;
    height: 88px;
    object-fit: contain;
    filter: grayscale(1);
    opacity: 0.65;
    animation: startup-pulse 1800ms cubic-bezier(0.45, 0, 0.55, 1) infinite;
    /* Startup timings also override the global "animations off" rule. */
    animation-duration: 1800ms !important;
  }

  .startup-status {
    position: absolute;
    top: calc(50% + 108px);
    left: 24px;
    right: 24px;
    text-align: center;
  }

  .startup-text,
  :global(:root.no-animations) .startup-text {
    font-size: 13px;
    line-height: 1.5;
    animation: startup-text-in 240ms ease-out both;
    animation-duration: 240ms !important;
  }

  .startup-screen.finished {
    animation: startup-out 320ms ease-in-out forwards;
    animation-duration: 320ms !important;
  }

  .startup-screen.entering:not(.finished) {
    animation: startup-in 320ms ease-in-out both;
    animation-duration: 320ms !important;
  }

  .startup-progress {
    width: min(200px, 100%);
    height: 2px;
    margin: 18px auto 0;
    overflow: hidden;
    border-radius: 2px;
    background: var(--border-subtle, rgba(255, 255, 255, 0.08));
  }

  .startup-progress-fill {
    height: 100%;
    background: currentColor;
    transform-origin: left;
  }

  @keyframes startup-pulse {
    0%, 100% { opacity: 0.65; }
    50% { opacity: 0.9; }
  }

  @keyframes startup-text-in {
    from { opacity: 0; }
    to { opacity: 1; }
  }

  @keyframes startup-out {
    from { opacity: 1; }
    to { opacity: 0; }
  }

  @keyframes startup-in {
    from { opacity: 0; }
    to { opacity: 1; }
  }

  @media (prefers-reduced-motion: reduce) {
    .startup-logo { animation: none; }
  }
</style>
