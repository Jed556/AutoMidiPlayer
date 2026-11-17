<script lang="ts">
  import { onMount, tick } from 'svelte';
  import { createJsonDiff, type DiffLine } from '../../utils/jsonDiff';
  import { animationStore } from '../../state/animationStore.svelte';
  import { createFrameResizeObserver } from '../../utils/frameResizeObserver';
  import { t } from '../../i18n';
  import { tooltip } from '../../actions/tooltip';
  import Button from './Button.svelte';
  import CustomScrollViewer from './CustomScrollViewer.svelte';
  import SlidingTabs from './SlidingTabs.svelte';

  type DiffChoice = 'keep' | 'replace';

  let {
    before,
    after,
    beforeLabel,
    afterLabel,
    height = '360px',
    showChoiceTabs = false,
    choice = $bindable<DiffChoice>('keep'),
    keepLabel = t('jsonDiff.keep'),
    replaceLabel = t('jsonDiff.replace'),
    keepTitle,
    replaceTitle,
    choiceLabel = t('jsonDiff.choice'),
    choiceDisabled = false,
    onchoicechange,
  }: {
    before: string;
    after: string;
    beforeLabel: string;
    afterLabel: string;
    height?: string;
    showChoiceTabs?: boolean;
    choice?: DiffChoice;
    keepLabel?: string;
    replaceLabel?: string;
    keepTitle?: string;
    replaceTitle?: string;
    choiceLabel?: string;
    choiceDisabled?: boolean;
    onchoicechange?: (choice: DiffChoice) => void;
  } = $props();
  const diff = $derived(createJsonDiff(before, after));
  const fillHeight = $derived(height === '100%');
  let viewport = $state<HTMLDivElement>();
  let container: HTMLElement;
  let narrow = $state(false);
  let currentChange = $state(0);

  function selectChoice(next: DiffChoice) {
    if (choiceDisabled || next === choice) return;
    choice = next;
    onchoicechange?.(next);
  }

  onMount(() => {
    const update = () => { narrow = container.clientWidth < 560; };
    const observer = createFrameResizeObserver(update);
    observer.observe(container);
    update();
    return () => observer.disconnect();
  });

  function scrollToChange(index: number, smooth: boolean) {
    const row = viewport?.querySelector<HTMLElement>(`[data-diff-row="${diff.changeStarts[index]}"]`);
    if (!row || !viewport) return;
    const reduceMotion = animationStore.isOff || window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    viewport.scrollTo({ top: Math.max(0, viewport.scrollTop + row.getBoundingClientRect().top - viewport.getBoundingClientRect().top - 24),
      behavior: smooth && !reduceMotion ? 'smooth' : 'instant' });
  }

  function navigate(direction: number) {
    const count = diff.changeStarts.length;
    if (!count) return;
    currentChange = (currentChange + direction + count) % count;
    scrollToChange(currentChange, true);
  }

  $effect(() => {
    before; after;
    currentChange = 0;
    let cancelled = false;
    void tick().then(() => {
      if (cancelled) return;
      if (diff.changeStarts.length) scrollToChange(0, false);
      else viewport?.scrollTo({ top: 0, left: 0 });
    });
    return () => { cancelled = true; };
  });
</script>

{#snippet line(content: DiffLine | null, tone: 'equal' | 'removed' | 'added', sourceLabel: string)}
  <div class="diff-cell" class:removed={tone === 'removed' && content !== null}
    class:added={tone === 'added' && content !== null} class:empty={content === null}>
    <span class="line-number" aria-hidden="true">{content?.lineNumber ?? ''}</span>
    <span class="line-sign" aria-hidden="true">{content && tone !== 'equal' ? tone === 'added' ? '+' : '−' : ''}</span>
    {#if content}<span class="sr-only">{sourceLabel}, {t(`jsonDiff.${tone}Line`, { line: content.lineNumber })}: </span>{/if}
    <code>{#if content}{#each content.parts as part}<span class:inline-change={part.changed}>{part.text}</span>{/each}{/if}</code>
  </div>
{/snippet}

<section class="json-diff" class:fill={fillHeight} bind:this={container} aria-label={t('jsonDiff.comparison')}>
  <div class="diff-toolbar">
    <div class="diff-toolbar-left">
      {#if showChoiceTabs}
        <SlidingTabs
          variant="pill"
          label={choiceLabel}
          value={choice}
          disabled={choiceDisabled}
          onselect={id => selectChoice(id as DiffChoice)}
          tabs={[
            { id: 'keep', label: keepLabel, title: keepTitle },
            { id: 'replace', label: replaceLabel, title: replaceTitle },
          ]}
        />
      {/if}
      <div class="diff-counts">
        {#if diff.changeStarts.length}
          <span class="added-count" use:tooltip={t('jsonDiff.addedLines', { count: diff.added })}>+{diff.added}</span>
          <span class="removed-count" use:tooltip={t('jsonDiff.removedLines', { count: diff.removed })}>−{diff.removed}</span>
          <span use:tooltip={t('jsonDiff.modifiedLines', { count: diff.modified })}>~{diff.modified}</span>
        {:else}<span>{t('jsonDiff.noChanges')}</span>{/if}
      </div>
    </div>
    <div class="diff-navigation">
      {#if diff.changeStarts.length}<span class="change-position" role="status">{t('jsonDiff.position', { current: currentChange + 1, total: diff.changeStarts.length })}</span>{/if}
      <div class="diff-nav-buttons">
        <Button variant="ghost" size="icon" icon="ChevronUp" iconSize={14} disabled={!diff.changeStarts.length}
          title={t('jsonDiff.previous')} ariaLabel={t('jsonDiff.previous')} onclick={() => navigate(-1)} />
        <Button variant="ghost" size="icon" icon="ChevronDown" iconSize={14} disabled={!diff.changeStarts.length}
          title={t('jsonDiff.next')} ariaLabel={t('jsonDiff.next')} onclick={() => navigate(1)} />
      </div>
    </div>
  </div>
  {#if diff.invalidLeft || diff.invalidRight}<p class="invalid-json" role="status">{t('jsonDiff.invalidJson')}</p>{/if}
  <div class="diff-labels" class:unified={narrow} use:tooltip={t('jsonDiff.formattedLines')}>
    {#if narrow}<span>{beforeLabel} / {afterLabel}</span>
    {:else}<span>{beforeLabel}</span><span>{afterLabel}</span>{/if}
  </div>
  <CustomScrollViewer class="json-diff-viewport" style={fillHeight ? undefined : `height: ${height}; flex: none;`}
    overlayScrollbar={true} bind:scrollContainer={viewport} tabIndex={0} ariaLabel={t('jsonDiff.comparison')} padding="6px 0" fadeHeight={12}>
    <div class="diff-rows" class:unified={narrow}>
      {#each diff.rows as row, index}
        <div class="diff-row" data-diff-row={index}>
          {#if narrow}
            {#if row.kind === 'equal'}{@render line(row.right ?? row.left, 'equal', afterLabel)}
            {:else}
              {#if row.left}{@render line(row.left, 'removed', beforeLabel)}{/if}
              {#if row.right}{@render line(row.right, 'added', afterLabel)}{/if}
            {/if}
          {:else}
            {@render line(row.left, row.kind === 'equal' ? 'equal' : 'removed', beforeLabel)}
            {@render line(row.right, row.kind === 'equal' ? 'equal' : 'added', afterLabel)}
          {/if}
        </div>
      {/each}
    </div>
  </CustomScrollViewer>
</section>

<style>
  .json-diff { min-width: 0; margin-top: 14px; }
  .json-diff.fill { flex: 1; min-height: 0; display: flex; flex-direction: column; }
  .diff-toolbar { display: flex; align-items: center; flex-wrap: wrap; justify-content: space-between; gap: 8px 12px; margin-bottom: 8px; flex-shrink: 0; }
  .diff-toolbar-left { display: flex; align-items: center; flex-wrap: wrap; gap: 12px; min-width: 0; }
  .diff-counts, .diff-navigation { display: flex; align-items: center; gap: 10px; font-size: 11px; color: var(--text-secondary); }
  .added-count { color: var(--success, #49bd80); }
  .removed-count { color: var(--danger, #ec7984); }
  .change-position { font-variant-numeric: tabular-nums; }
  .diff-navigation { gap: 6px; }
  .diff-nav-buttons { display: flex; align-items: center; margin-right: -4px; }
  .diff-navigation :global(.fluent-btn) { border: none; width: 24px; height: 24px; min-width: 24px; }
  .diff-labels { flex-shrink: 0; display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); background: var(--control-fill); border-radius: var(--radius-sm) var(--radius-sm) 0 0; }
  .diff-labels span { padding: 10px 12px; color: var(--text-secondary); font-size: var(--json-diff-label-size, 12px); font-weight: 600; overflow-wrap: anywhere; }
  .diff-labels:not(.unified) span + span { border-left: 1px solid var(--border-medium); }
  :global(.json-diff-viewport) { background: var(--bg-secondary); border-radius: 0 0 var(--radius-sm) var(--radius-sm); }
  :global(.json-diff-viewport .scroll-content:focus-visible) { outline: 2px solid var(--accent); outline-offset: -2px; }
  .diff-rows { font: var(--json-diff-font-size, 12px)/1.65 var(--font-mono, monospace); color: var(--text-primary); }
  .diff-row { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); }
  .diff-cell { position: relative; display: grid; grid-template-columns: 4.5ch 2ch minmax(0, 1fr); padding-right: 14px; min-height: 1.65em; }
  .diff-rows:not(.unified) .diff-cell + .diff-cell { border-left: 1px solid var(--border-medium); }
  .line-number, .line-sign { user-select: none; color: var(--text-tertiary); font-variant-numeric: tabular-nums; text-align: right; padding-right: 1ch; }
  .line-sign { padding-right: .5ch; }
  code { font: inherit; white-space: pre-wrap; overflow-wrap: anywhere; user-select: text; }
  .removed { background: rgba(232, 72, 86, .12); }
  .added { background: rgba(54, 179, 117, .12); }
  .removed .line-sign { color: var(--danger, #ec7984); }
  .added .line-sign { color: var(--success, #49bd80); }
  .removed .inline-change { background: rgba(232, 72, 86, .25); }
  .added .inline-change { background: rgba(54, 179, 117, .25); }
  .empty { background: var(--control-fill); }
  .unified.diff-labels { grid-template-columns: minmax(0, 1fr); }
  .unified .diff-row { display: block; }
  .invalid-json { flex-shrink: 0; margin: 8px 0; font-size: 12px; line-height: 1.5; color: var(--danger, #ec7984); }
  .sr-only { position: absolute; width: 1px; height: 1px; padding: 0; overflow: hidden; clip-path: inset(50%); white-space: nowrap; user-select: none; }
</style>
