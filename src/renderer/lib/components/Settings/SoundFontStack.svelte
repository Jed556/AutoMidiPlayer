<script lang="ts">
  import { onMount, onDestroy, tick } from 'svelte';
  import { portal } from '../../actions/portal';
  import { animationStore } from '../../state/animationStore.svelte';
  import { playbackStore } from '../../state/playbackStore.svelte';
  import { api } from '../../backend/index';
  import { t } from '../../i18n';
  import Button from '../Common/Button.svelte';
  import Dropdown from '../Common/Dropdown.svelte';
  import ToggleSwitch from '../Common/ToggleSwitch.svelte';

  let reducedMotion = $state(false);
  onMount(() => {
    const query = window.matchMedia('(prefers-reduced-motion: reduce)');
    const update = () => { reducedMotion = query.matches; };
    update(); query.addEventListener('change', update);
    return () => query.removeEventListener('change', update);
  });
  let motionMs = $derived(reducedMotion ? 0 : animationStore.durationMs);

  let order = $state<string[]>([]);
  let dragging = $state<string | null>(null);
  let targetSlot = $state(0);
  let dropping = $state(false);
  let preview = $state({ width: 0, height: 0, offsetX: 0, offsetY: 0, step: 0 });
  let dragWidth = $state(0);
  let dropTimer: ReturnType<typeof setTimeout> | undefined;
  let disposed = false;
  onDestroy(() => { disposed = true; clearTimeout(dropTimer); });
  let list = $state<HTMLOListElement>();
  let pointer = $state({ x: 0, y: 0 });
  let pending: { id: string; x: number; y: number } | null = null;
  let error = $state('');
  let busy = $state(false);
  let gapFilling = $state(playbackStore.soundFontGapFilling);
  $effect(() => { gapFilling = playbackStore.soundFontGapFilling; });
  $effect(() => { if (!dragging && !dropping && !busy) order = [...playbackStore.soundFontOrder]; });
  $effect(() => {
    if (order.some(id => !playbackStore.installedSoundFonts.includes(id))) {
      clearTimeout(dropTimer);
      pending = null; dragging = null; dropping = false;
      order = [...playbackStore.soundFontOrder];
    }
  });
  let options = $derived(playbackStore.soundFontOrder.map(id => ({ value: id, label: playbackStore.soundFontLabel(id), description: fontDescription(id) || undefined, ...(!['default', 'legacy'].includes(id) ? { actionIcon: 'Delete', actionTitle: t('settings.deleteSoundFont'), onaction: () => run(() => playbackStore.deleteSoundFont(id)) } : {}) })));

  function fontDescription(id: string) {
    return id === 'default' ? t('settings.soundFontDefaultDescription')
      : id === 'legacy' ? t('settings.soundFontLegacyDescription') : '';
  }

  async function run(action: () => Promise<unknown>) {
    if (busy) return;
    busy = true; error = '';
    try { await action(); } catch (e) { error = String(e); }
    finally { gapFilling = playbackStore.soundFontGapFilling; busy = false; order = [...playbackStore.soundFontOrder]; }
  }
  function move(id: string, target: number) {
    const next = order.filter(v => v !== id);
    next.splice(Math.max(0, Math.min(target, next.length)), 0, id);
    order = next;
  }
  function rowShift(index: number) {
    if (!dragging) return 0;
    const from = order.indexOf(dragging);
    if (index === from) return (targetSlot - from) * preview.step;
    if (from < targetSlot && index > from && index <= targetSlot) return -preview.step;
    if (from > targetSlot && index >= targetSlot && index < from) return preview.step;
    return 0;
  }
  function displayRank(index: number) {
    return index + 1 + Math.round(rowShift(index) / (preview.step || 1));
  }
  function sizePreview(node: HTMLElement) {
    // Measure glyphs rather than the stretched flex item, preserving text size.
    const frame = requestAnimationFrame(() => {
      if (dropping) return;
      const widths = Array.from(node.querySelectorAll('.font-name, .font-description')).map(label => {
        const range = document.createRange();
        range.selectNodeContents(label);
        return range.getBoundingClientRect().width;
      });
      const handleWidth = node.querySelector('.preview-handle')!.getBoundingClientRect().width;
      const style = getComputedStyle(node);
      const spacing = parseFloat(style.paddingLeft) + parseFloat(style.paddingRight)
        + parseFloat(style.borderLeftWidth) + parseFloat(style.borderRightWidth)
        + parseFloat(style.columnGap);
      dragWidth = Math.min(preview.width, Math.ceil(Math.max(0, ...widths) + handleWidth + spacing));
    });
    return { destroy() { cancelAnimationFrame(frame); } };
  }
  function pointerMove(event: PointerEvent) {
    if (!playbackStore.soundFontGapFilling || dropping) return;
    if (!dragging && pending && Math.hypot(event.clientX - pending.x, event.clientY - pending.y) >= 5) {
      const origin = Array.from(list?.querySelectorAll<HTMLElement>('[data-font-id]') ?? []).find(row => row.dataset.fontId === pending?.id)?.querySelector<HTMLElement>('.font-card');
      if (!origin) return;
      const rect = origin.getBoundingClientRect();
      preview = { width: rect.width, height: origin.offsetHeight, offsetX: pending.x - rect.left,
        offsetY: pending.y - rect.top, step: origin.offsetHeight + parseFloat(getComputedStyle(origin).marginBottom) };
      dragWidth = rect.width;
      targetSlot = order.indexOf(pending.id);
      dragging = pending.id;
    }
    if (!dragging || !list) return;
    pointer = { x: event.clientX - preview.offsetX, y: event.clientY - preview.offsetY };
    const bounds = list.getBoundingClientRect();
    // Slot positions come from the unchanged list, never from animated cards.
    const y = event.clientY - bounds.top;
    const candidate = Math.max(0, Math.min(order.length - 1, Math.floor(y / preview.step)));
    if (candidate > targetSlot && y > candidate * preview.step + 3) targetSlot = candidate;
    if (candidate < targetSlot && y < (candidate + 1) * preview.step - 3) targetSlot = candidate;
    const scroller = list.closest('.custom-scroll-viewer')?.querySelector<HTMLElement>('.scroll-content');
    if (scroller) {
      const rect = scroller.getBoundingClientRect();
      if (event.clientY < rect.top + 35) scroller.scrollBy(0, -12);
      if (event.clientY > rect.bottom - 35) scroller.scrollBy(0, 12);
    }
  }
  function finish(cancel = false) {
    pending = null;
    if (!dragging || dropping) return;
    const id = dragging;
    const from = order.indexOf(id);
    if (cancel) { dragging = null; return; }
    const bounds = list!.getBoundingClientRect();
    pointer = { x: bounds.left, y: bounds.top + targetSlot * preview.step };
    dragWidth = bounds.width;
    dropping = true;
    dropTimer = setTimeout(async () => {
      if (disposed) return;
      if (from !== targetSlot) move(id, targetSlot);
      dragging = null;
      await tick();
      if (!disposed && from !== targetSlot) await run(() => playbackStore.applySoundFontConfiguration([...order]));
      dropping = false;
    }, motionMs);
  }
  async function add() {
    await run(async () => {
      const path = await api.settings.browseSoundFont();
      if (path) await playbackStore.addSoundFont(path);
    });
  }
</script>

<svelte:window onblur={() => finish(true)} onkeydown={event => { if (event.key === 'Escape') finish(true); }} onpointermove={pointerMove} onpointerup={() => finish()} onpointercancel={() => finish(true)} />

<div class="font-section" style:--font-motion={`${motionMs}ms`}>
  <div class="setting-header">
    <span class="setting-label">{t('settings.soundFont')}</span>
    <p class="setting-description">{t(playbackStore.soundFontGapFilling ? 'settings.soundFontStackDesc' : 'settings.soundFontSingleDesc')}</p>
  </div>
  <ToggleSwitch label={t('settings.soundFontGapFilling')} bind:checked={gapFilling}
    disabled={busy || playbackStore.soundFontBusy || !!dragging || dropping}
    onchange={value => run(() => playbackStore.applySoundFontConfiguration(undefined, value))} />
  <div class="font-deck" class:collapsed={!playbackStore.soundFontGapFilling}>
    <ol bind:this={list} class="font-list" class:is-dragging={!!dragging} class:is-dropping={dropping} aria-label={t('settings.soundFontPriority')}>
      {#each order as id, index (id)}
        {@const selected = id === playbackStore.soundFontId}
        <li data-font-id={id} class:folded={!playbackStore.soundFontGapFilling && !selected}
          class:dragging={dragging === id} style:transform={dragging ? `translate3d(0, ${rowShift(index)}px, 0)` : undefined}
          inert={!playbackStore.soundFontGapFilling && !selected}>
          <div class="row-clip">
          <div class="font-card">
          <div class="handle-slot" inert={!playbackStore.soundFontGapFilling}>
          <div class="handle-content">
          <button class="drag-handle" type="button" disabled={busy || dropping || !playbackStore.soundFontGapFilling} aria-label={`${t('settings.soundFontReorder')}: ${playbackStore.soundFontLabel(id)}`}
            onpointerdown={event => { if (event.button !== 0) return; event.preventDefault(); if (dropping) return; pending = { id, x: event.clientX, y: event.clientY }; }}
            onkeydown={event => {
              if (event.key === 'Escape') finish(true);
              if (event.key === 'ArrowUp' || event.key === 'ArrowDown') {
                event.preventDefault(); move(id, index + (event.key === 'ArrowUp' ? -1 : 1));
                void run(() => playbackStore.applySoundFontConfiguration([...order]));
              }
            }}><svg width="20" height="20" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
              <circle cx="5" cy="9" r="1.4" /><circle cx="12" cy="9" r="1.4" /><circle cx="19" cy="9" r="1.4" />
              <circle cx="5" cy="15" r="1.4" /><circle cx="12" cy="15" r="1.4" /><circle cx="19" cy="15" r="1.4" />
            </svg></button>
          <span class="rank">{String(displayRank(index)).padStart(2, '0')}</span>
          </div>
          </div>
          <span class="font-info">
            <span class="font-name">{playbackStore.soundFontLabel(id)}{#if fontDescription(id)}<span class="font-description">{' '}({fontDescription(id)})</span>{/if}</span>
          </span>
          {#if playbackStore.soundFontGapFilling && !['default', 'legacy'].includes(id)}
            <Button variant="ghost" size="icon" icon="Delete" disabled={busy || !!dragging || dropping} title={t('settings.deleteSoundFont')}
              onclick={() => run(() => playbackStore.deleteSoundFont(id))} />
          {/if}
          {#if selected}
            <div class="dropdown-overlay" inert={playbackStore.soundFontGapFilling} aria-hidden={playbackStore.soundFontGapFilling}>
              <Dropdown variant="ghost" width="100%" value={playbackStore.soundFontId} {options} portalMenu
                ariaLabel={t('settings.soundFont')} disabled={busy || playbackStore.soundFontGapFilling}
                onchange={id => run(() => playbackStore.loadSoundFont(String(id)))} />
            </div>
          {/if}
          </div>
          </div>
        </li>
      {/each}
    </ol>
    {#if dragging}
      <div use:portal use:sizePreview class="drag-preview" class:is-dropping={dropping} aria-hidden="true"
        style:--font-motion={`${motionMs}ms`} style:width={`${dragWidth}px`} style:height={`${preview.height}px`}
        style:transform={`translate3d(${pointer.x}px, ${pointer.y}px, 0)`}>
        <span class="preview-handle"><span class="preview-grip"><svg width="20" height="20" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
              <circle cx="5" cy="9" r="1.4" /><circle cx="12" cy="9" r="1.4" /><circle cx="19" cy="9" r="1.4" />
              <circle cx="5" cy="15" r="1.4" /><circle cx="12" cy="15" r="1.4" /><circle cx="19" cy="15" r="1.4" />
            </svg></span><span class="rank">{String(targetSlot + 1).padStart(2, '0')}</span></span>
        <span class="font-info"><span class="font-name">{playbackStore.soundFontLabel(dragging)}{#if fontDescription(dragging)}<span class="font-description">{' '}({fontDescription(dragging)})</span>{/if}</span></span>
      </div>
    {/if}
  </div>
  <Button variant="ghost" compact icon="Add" disabled={busy || !!dragging || dropping} onclick={add}>{t('settings.addSoundFont')}</Button>
  {#if error}<p class="font-error" role="alert">{error}</p>{/if}
</div>

<style>
  .font-section { display: flex; flex-direction: column; align-items: stretch; gap: 14px; min-width: 0; }
  .setting-header { display: flex; flex-direction: column; gap: 6px; }
  .setting-label { font-size: 14px; font-weight: 600; }
  .setting-description { color: var(--text-secondary); font-size: 12px; line-height: 1.5; margin: 0; }
  .font-deck { min-width: 0; --font-ease: cubic-bezier(.22, 1, .36, 1); }
  .font-list { list-style: none; padding: 0; margin: 0 0 -6px; }
  li { display: grid; grid-template-rows: 1fr; opacity: 1; transition: grid-template-rows var(--font-motion) var(--font-ease), opacity var(--font-motion) var(--font-ease); }
  li.folded { grid-template-rows: 0fr; opacity: 0; pointer-events: none; }
  .row-clip { min-height: 0; overflow: hidden; }
  .font-card { position: relative; display: flex; align-items: center; gap: 0; min-height: 56px; margin-bottom: 6px; padding: 8px 12px; box-sizing: border-box; border-radius: 8px; background: var(--bg-tertiary); transition: background var(--font-motion) var(--font-ease), transform var(--font-motion) var(--font-ease); }
  li.folded .font-card { transform: translateY(-8px) scale(.98); }
  .font-list.is-dragging li { grid-template-rows: 1fr; opacity: 1; transition: transform var(--font-motion) var(--font-ease); }
  .font-list.is-dropping li { transition: none; }
  li.dragging .font-card { visibility: hidden; }
  .font-list.is-dragging { user-select: none; }
  .handle-slot { display: grid; grid-template-columns: 1fr; transition: grid-template-columns var(--font-motion) cubic-bezier(.4, 0, .2, 1); }
  .handle-content { min-width: 0; overflow: hidden; display: flex; align-items: center; gap: 8px; padding-right: 12px; mask-image: linear-gradient(90deg, #000 0%, #000 40%, transparent 65%); mask-size: 250% 100%; mask-position: 0% 0; transition: mask-position var(--font-motion) var(--font-ease), opacity var(--font-motion) var(--font-ease), transform var(--font-motion) var(--font-ease), padding-right var(--font-motion) var(--font-ease); }
  .collapsed .handle-slot { grid-template-columns: 0fr; }
  .collapsed .handle-content { mask-position: 100% 0; opacity: 0; transform: translateX(-8px); padding-right: 0; }
  .drag-handle { flex: none; cursor: grab; touch-action: none; display: grid; place-items: center; border: 0; border-radius: 4px; background: transparent; color: var(--text-secondary); padding: 6px 0; opacity: 1; transition: opacity var(--font-motion) cubic-bezier(.4, 0, .2, 1), color var(--font-motion) var(--font-ease); }
  /* Disabled during folding/loading; let the mode transition control fading. */
  .drag-handle:disabled { opacity: 1; cursor: default; }
  .collapsed .drag-handle, .collapsed .drag-handle:disabled { opacity: 0; }
  .drag-handle svg { display: block; }
  .drag-handle:hover { color: var(--text-primary); }
  .drag-handle:focus-visible { outline: 2px solid var(--accent-color); outline-offset: 2px; }
  .drag-handle:active { cursor: grabbing; }
  .rank { font-size: 16px; font-weight: 700; color: var(--text-primary); opacity: .3; min-width: 24px; flex: none; font-variant-numeric: tabular-nums; line-height: 20px; }
  .font-info { flex: 1; min-width: 0; display: flex; align-items: center; padding-right: 24px; }
  .font-name { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-size: 13px; line-height: 20px; }
  .font-description { color: var(--text-secondary); font-size: inherit; line-height: inherit; }
  .dropdown-overlay { position: absolute; inset: 0; opacity: 0; pointer-events: none; transition: opacity var(--font-motion) var(--font-ease); }
  .collapsed .dropdown-overlay { opacity: 1; pointer-events: auto; }
  .dropdown-overlay :global(.fluent-dropdown) { height: 100%; }
  .dropdown-overlay :global(.dropdown-trigger) { width: 100% !important; height: 100% !important; padding: 8px 14px !important; border-radius: 8px; background: transparent !important; }
  .dropdown-overlay :global(.trigger-content) { opacity: 0; }
  .dropdown-overlay :global(.chevron-icon) { position: absolute; right: 14px; top: 50%; margin: 0 !important; transform: translateY(-50%) translateX(-6px); transition: transform var(--font-motion) var(--font-ease); }
  .collapsed .dropdown-overlay :global(.chevron-icon) { transform: translateY(-50%) translateX(0); }
  .collapsed .dropdown-overlay :global(.dropdown-trigger.is-open .chevron-icon) { transform: translateY(-50%) rotate(180deg); }
  .collapsed .font-card:focus-within { outline: 1px solid var(--accent-color); outline-offset: -1px; }
  .collapsed .font-card:hover { background: var(--control-fill-hover); }
  .drag-preview { position: fixed; top: 0; left: 0; z-index: 100; pointer-events: none; display: flex; align-items: center; gap: 0; box-sizing: border-box; padding: 8px 15px 8px 12px; border-radius: 8px; background: var(--bg-dialog); color: var(--text-primary); border: 0; box-shadow: inset 0 0 0 1px var(--accent-color), 0 12px 32px rgba(0,0,0,.24); font-size: 13px; transition: width var(--font-motion) cubic-bezier(.22, 1, .36, 1); }
  .drag-preview.is-dropping { transition: transform var(--font-motion) cubic-bezier(.22, 1, .36, 1), width var(--font-motion) cubic-bezier(.22, 1, .36, 1); }
  .drag-preview .font-info { padding-right: 0; }
  .preview-handle { display: flex; align-items: center; flex: none; gap: 8px; padding-right: 12px; }
  .preview-grip { display: grid; place-items: center; flex: none; width: 20px; height: 20px; color: var(--text-secondary); }
  .font-error { color: var(--error-color, #d64c4c); font-size: 12px; overflow-wrap: anywhere; }
  @media (prefers-reduced-motion: reduce) { .font-section { --font-motion: 0ms !important; } }
</style>
