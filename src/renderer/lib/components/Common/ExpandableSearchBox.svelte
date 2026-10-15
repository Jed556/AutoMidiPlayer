<script lang="ts">
  import { tick } from 'svelte';
  import { searchOverflow } from '../../actions/searchOverflow';
  import { animationStore } from '../../state/animationStore.svelte';
  import { t } from '../../i18n';
  import FluentIcon from './FluentIcon.svelte';

  interface Props {
    value?: string;
    placeholder: string;
    expandedWidth?: string;
  }

  let {
    value = $bindable(''),
    placeholder,
    expandedWidth,
  }: Props = $props();

  const inputId = $props.id();
  let open = $state(false);
  let container = $state<HTMLDivElement>();
  let input = $state<HTMLInputElement>();
  let trigger = $state<HTMLButtonElement>();
  let expanded = $derived(open || value.length > 0);

  async function openSearch() {
    open = true;
    await tick();
    // Focus immediately so typing and tabbing never wait for the reveal.
    input?.focus({ preventScroll: true });
  }

  async function clearSearch() {
    value = '';
    open = false;
    await tick();
    trigger?.focus({ preventScroll: true });
  }

  function handleFocusOut(event: FocusEvent) {
    if (!container?.contains(event.relatedTarget as Node | null) && !value) {
      open = false;
    }
  }
</script>

<div
  class="expandable-search"
  class:expanded
  class:has-query={value.length > 0}
  class:no-motion={animationStore.isOff}
  data-direction={animationStore.searchExpansionDirection}
  style:--search-beat={`${animationStore.timing}s`}
  style:--search-expanded-width={expandedWidth}
  bind:this={container}
  onfocusout={handleFocusOut}
>
  <!-- A fractional grid track moves toolbar neighbors without scaling text or icons. -->
  <div class="search-size" aria-hidden="true"></div>
  <div class="search-surface" aria-hidden="true"></div>
  <svg class="search-outline" aria-hidden="true">
    <rect width="100%" height="100%" rx="15.5" pathLength="1" />
  </svg>

  <button
    type="button"
    class="search-trigger"
    bind:this={trigger}
    onclick={openSearch}
    title={placeholder}
    aria-label={placeholder}
    aria-expanded={expanded}
    aria-controls={inputId}
    tabindex={expanded ? -1 : 0}
  >
    <FluentIcon name="Search" size={14} />
  </button>

  <div class="search-field" inert={!expanded}>
    <input
      id={inputId}
      type="text"
      {placeholder}
      aria-label={placeholder}
      autocomplete="off"
      spellcheck="false"
      bind:value
      bind:this={input}
      use:searchOverflow={{ value, placeholder }}
      onkeydown={(event) => {
        if (event.key === 'Escape') {
          event.preventDefault();
          event.stopPropagation();
          void clearSearch();
        }
      }}
    />
    {#if value}
      <button
        type="button"
        class="clear-search"
        onclick={clearSearch}
        aria-label={t('common.clearSearch')}
        title={t('common.clearSearch')}
      >
        <FluentIcon name="Dismiss" size={12} />
      </button>
    {/if}
  </div>
</div>

<style>
  .expandable-search {
    --search-ease: cubic-bezier(0.22, 1, 0.36, 1);
    position: relative;
    display: grid;
    grid-template-columns: minmax(32px, 0fr);
    flex: 0 0 auto;
    height: 32px;
    border-radius: 16px;
    isolation: isolate;
    transition: grid-template-columns calc(var(--search-beat) * 1.25) var(--search-ease);
  }

  .expanded {
    grid-template-columns: minmax(32px, 1fr);
    transition-duration: calc(var(--search-beat) * 1.75);
    transition-delay: calc(var(--search-beat) * 1.25);
  }

  .search-size {
    width: var(--search-expanded-width, 240px);
    min-width: 0;
    max-width: var(--search-expanded-width, max(32px, calc(100cqi - 224px)));
    overflow: hidden;
  }

  .search-surface,
  .search-field {
    position: absolute;
    inset: 0;
    border-radius: inherit;
  }

  .search-surface {
    background: var(--control-fill, rgba(255, 255, 255, 0.1));
    opacity: 0;
    transition: opacity calc(var(--search-beat) * 1.25) ease;
    pointer-events: none;
  }

  .expanded .search-surface {
    opacity: 1;
  }

  .expanded:focus-within .search-surface {
    background: var(--control-fill-hover, rgba(255, 255, 255, 0.18));
  }

  .search-outline {
    position: absolute;
    inset: 0.5px;
    width: calc(100% - 1px);
    height: 31px;
    overflow: visible;
    fill: none;
    stroke: var(--text-secondary);
    stroke-width: 1;
    opacity: 0;
    pointer-events: none;
    transition: opacity calc(var(--search-beat) * 1.5) ease;
  }

  .search-outline rect {
    stroke-dasharray: 1;
    stroke-dashoffset: 1;
    transition: stroke-dashoffset calc(var(--search-beat) * 1.25) cubic-bezier(0.4, 0, 0.2, 1);
  }

  .expanded .search-outline { opacity: 0.55; }
  .expanded .search-outline rect { stroke-dashoffset: 0; }

  .expanded:has(input:focus-visible) .search-outline {
    stroke: var(--accent-color);
    opacity: 0.85;
  }

  .search-trigger {
    position: absolute;
    inset: 0 auto 0 0;
    display: grid;
    place-items: center;
    width: 32px;
    padding: 0;
    border: none;
    border-radius: 50%;
    background: transparent;
    color: var(--text-primary);
    opacity: 0.65;
    cursor: pointer;
    z-index: 1;
    transition: opacity var(--transition-fast);
  }

  .search-trigger:hover { opacity: 1; }
  .search-trigger:active { opacity: 0.75; }

  .search-trigger:focus-visible {
    outline: 2px solid var(--accent-color);
    outline-offset: 3px;
  }

  .search-field {
    overflow: hidden;
    pointer-events: none;
    opacity: 0;
    transition: opacity calc(var(--search-beat) * 1.25) ease;
  }

  .expanded .search-field {
    opacity: 1;
    pointer-events: auto;
  }

  input {
    position: absolute;
    inset: 0;
    box-sizing: border-box;
    width: 100%;
    height: 100%;
    min-width: 0;
    padding: 0 28px 0 31px;
    border: none;
    outline: none;
    border-radius: inherit;
    background: transparent;
    color: var(--text-primary);
    font: inherit;
    font-size: 12.5px;
  }

  input::placeholder {
    color: var(--text-tertiary);
    opacity: 1;
  }

  .clear-search {
    position: absolute;
    inset: 6px 5px 6px auto;
    display: grid;
    place-items: center;
    width: 20px;
    padding: 0;
    border: none;
    border-radius: 50%;
    background: transparent;
    color: var(--text-secondary);
    cursor: pointer;
  }

  .clear-search:hover {
    background: var(--bg-hover);
    color: var(--text-primary);
  }

  .clear-search:focus-visible {
    outline: 2px solid var(--accent-color);
    outline-offset: -2px;
  }

  /* Typed text must become readable even if the opening sequence is still running. */
  .has-query,
  .has-query *,
  .no-motion,
  .no-motion * {
    transition: none !important;
  }

  @media (prefers-reduced-motion: reduce) {
    .expandable-search,
    .expandable-search * {
      transition: none !important;
    }
  }
</style>
