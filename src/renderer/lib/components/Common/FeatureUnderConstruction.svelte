<script lang="ts">
  import type { Snippet } from 'svelte';
  import { t } from '../../i18n';

  interface Props {
    featureName?: string;
    title?: string;
    description?: string;
    actions?: Snippet;
  }

  let { featureName, title, description, actions }: Props = $props();
</script>

<section class="construction-view" aria-label={featureName ?? title ?? t('construction.title')}>
  <div class="construction-content">
    <div class="construction-art" aria-hidden="true">
      <svg viewBox="0 0 120 120" fill="none" focusable="false">
        <path class="cone" d="M54 22h12l22 68H32l22-68Z" />
        <path class="cone-stripes" d="M48 40h24l4 12H44l4-12Zm-9 28h42l4 12H35l4-12Z" />
        <rect class="cone-base" x="22" y="90" width="76" height="10" rx="3" />
      </svg>
    </div>

    <div class="construction-copy">
      {#if featureName}
        <p class="feature-name">{featureName}</p>
      {/if}
      <h1>{title ?? t('construction.title')}</h1>
      <p class="description">{description ?? t('construction.description')}</p>
      {#if actions}
        <div class="construction-actions">{@render actions()}</div>
      {/if}
    </div>
  </div>
</section>

<style>
  .construction-view {
    display: grid;
    place-items: center;
    flex: 1;
    min-height: 0;
    overflow: auto;
    padding: clamp(24px, 6vw, 72px);
  }

  .construction-content {
    display: grid;
    grid-template-columns: 120px minmax(0, 1fr);
    align-items: center;
    gap: 36px;
    width: 100%;
    max-width: 620px;
    margin-block: auto;
  }

  .construction-art {
    color: var(--accent-color);
  }

  svg {
    display: block;
    width: 120px;
    height: 120px;
    stroke-linecap: round;
    stroke-linejoin: round;
  }

  .cone,
  .cone-base {
    fill: var(--accent-subtle);
    stroke: currentColor;
    stroke-width: 2;
  }

  .cone-stripes {
    fill: currentColor;
  }

  .feature-name {
    margin: 0 0 10px;
    color: var(--accent-color);
    font-size: 12px;
    font-weight: 600;
    letter-spacing: 0.08em;
    text-transform: uppercase;
  }

  h1 {
    margin: 0;
    color: var(--text-primary);
    font-size: clamp(22px, 2.5vw, 28px);
    font-weight: 650;
    line-height: 1.25;
    letter-spacing: -0.025em;
    overflow-wrap: anywhere;
  }

  .description {
    margin: 14px 0 0;
    max-width: 46ch;
    color: var(--text-secondary);
    font-size: 14px;
    line-height: 1.7;
    overflow-wrap: anywhere;
  }

  .construction-actions {
    display: flex;
    flex-wrap: wrap;
    gap: 12px;
    margin-top: 24px;
  }

  @media (max-width: 767px) {
    .construction-content {
      grid-template-columns: minmax(0, 1fr);
      gap: 24px;
      max-width: 440px;
    }
  }
</style>
