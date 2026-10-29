<script lang="ts">
  import { onMount, tick } from 'svelte';
  import type { OverlapBehavior } from '$shared/types/settings';
  import { DEFAULT_APP_SETTINGS } from '$shared/defaults/settings';
  import { animationStore } from '../../state/animationStore.svelte';
  import { t } from '../../i18n';
  import FluentIcon from '../Common/FluentIcon.svelte';
  import NumberBox from '../Common/NumberBox.svelte';
  import GradientSweep from '../Common/GradientSweep.svelte';
  import Button from '../Common/Button.svelte';
  import RefreshIcon from '../Common/RefreshIcon.svelte';

  let { value, onchange, repressGapMs, onrepressgapchange }: {
    value: OverlapBehavior;
    onchange: (value: OverlapBehavior) => void;
    repressGapMs: number;
    onrepressgapchange: (value: number) => void;
  } = $props();

  const id = $props.id();
  const options = ['hold', 'retrigger'] as const;
  let container: HTMLDivElement;
  let visible = $state(false);
  let documentVisible = $state(true);
  let reducedMotion = $state(false);
  let mounted = $state(false);
  let resetTrigger = $state<number | undefined>(undefined);
  let notes = $state([
    { x: 107, width: 204 },
    { x: 167, width: 84 },
    { x: 227, width: 84 },
  ]);
  let animations: Animation[] = [];
  let cycle = 0;

  function stopCycle() {
    ++cycle;
    animations.forEach(animation => animation.cancel());
    animations = [];
  }

  async function startCycle() {
    const version = ++cycle;
    const random = (min: number, max: number) => min + Math.random() * (max - min);
    const second = random(48, 76);
    const third = second + random(48, 76);
    const length = random(Math.max(190, third + 52), 236);
    notes = [
      { x: 107, width: length },
      { x: 107 + second, width: random(48, Math.min(104, length - second)) },
      { x: 107 + third, width: random(44, Math.min(90, length - third)) },
    ];
    // Keep both cards on the same randomized phrase. The first note starts
    // seven units beyond the right edge; reset ten units after the last exit.
    const distance = 280 + length + 10;
    const duration = distance / random(105, 120) * 1000;
    const hits = [280, 280 + second, 280 + third].map(position => position / distance);
    const release = (280 + length) / distance;
    // Flush the new geometry before creating animations for this phrase.
    await tick();
    if (version !== cycle || !mounted) return;
    const animate = (target: Element, frames: Keyframe[]) => {
      const animation = target.animate(frames, { duration, fill: 'both' });
      animations.push(animation);
      return animation;
    };
    for (const card of container.querySelectorAll<HTMLButtonElement>('.option')) {
      const presses = card.dataset.option === 'retrigger' ? hits : hits.slice(0, 1);
      animate(card.querySelector('.notes')!, [
        { transform: 'translateX(220px)' },
        { transform: `translateX(${220 - distance}px)` },
      ]);
      const keyFrames: Keyframe[] = [{ offset: 0, opacity: 0 }];
      const fingerFrames: Keyframe[] = [{ offset: 0, transform: 'translateY(0)' }];
      presses.forEach((hit, index) => {
        const end = presses[index + 1] ? presses[index + 1] - 0.05 : release;
        keyFrames.push(
          { offset: hit - 0.00001, opacity: 0 }, { offset: hit, opacity: 0.65 },
          { offset: end - 0.00001, opacity: 0.65 }, { offset: end, opacity: 0 },
        );
        fingerFrames.push(
          { offset: hit - 0.02, transform: 'translateY(0)', easing: 'ease-in-out' },
          { offset: hit, transform: 'translateY(4px)' },
          { offset: end, transform: 'translateY(4px)', easing: 'ease-in-out' },
          { offset: end + 0.015, transform: 'translateY(0)' },
        );
        animate(card.querySelectorAll('.ripple')[index], [
          { offset: 0, opacity: 0, transform: 'scale(0.5)' },
          { offset: hit - 0.00001, opacity: 0, transform: 'scale(0.5)' },
          { offset: hit, opacity: 0.8, transform: 'scale(0.5)' },
          { offset: hit + 0.065, opacity: 0, transform: 'scale(1.8)' },
          { offset: 1, opacity: 0, transform: 'scale(1.8)' },
        ]);
      });
      keyFrames.push({ offset: 1, opacity: 0 });
      fingerFrames.push({ offset: 1, transform: 'translateY(0)' });
      animate(card.querySelector('.key-down')!, keyFrames);
      animate(card.querySelector('.finger')!, fingerFrames);
    }
    const start = document.timeline.currentTime;
    for (const animation of animations) {
      animation.startTime = start;
      if (!visible || !documentVisible) animation.pause();
    }
    animations[0].finished.then(() => {
      if (version !== cycle) return;
      stopCycle();
      void startCycle();
    }).catch(() => {}); // Cancellation is expected on unmount or reduced motion.
  }

  $effect(() => {
    if (!mounted) return;
    if (animationStore.isOff || reducedMotion) {
      stopCycle();
      return;
    }
    const playing = visible && documentVisible;
    if (!animations.length) void startCycle();
    for (const animation of animations) {
      if (playing) animation.play();
      else animation.pause();
    }
  });

  onMount(() => {
    const motionPreference = window.matchMedia('(prefers-reduced-motion: reduce)');
    const onMotionPreference = () => { reducedMotion = motionPreference.matches; };
    onMotionPreference();
    motionPreference.addEventListener('change', onMotionPreference);
    mounted = true;
    const observer = new IntersectionObserver(([entry]) => { visible = entry.isIntersecting; });
    observer.observe(container);
    const onVisibility = () => { documentVisible = !document.hidden; };
    onVisibility();
    document.addEventListener('visibilitychange', onVisibility);
    return () => {
      mounted = false;
      stopCycle();
      motionPreference.removeEventListener('change', onMotionPreference);
      observer.disconnect();
      document.removeEventListener('visibilitychange', onVisibility);
    };
  });

  function onKeydown(event: KeyboardEvent, option: OverlapBehavior) {
    if (!['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown', 'Home', 'End'].includes(event.key)) return;
    event.preventDefault();
    const next = event.key === 'Home' ? 'hold' : event.key === 'End' ? 'retrigger'
      : option === 'hold' ? 'retrigger' : 'hold';
    if (next !== value) onchange(next);
    container.querySelector<HTMLButtonElement>(`[data-option="${next}"]`)?.focus();
  }
</script>

<div class="overlap-setting">
  <div class="heading">
    <span id={`${id}-label`} class="label">{t('settings.overlapBehavior')}</span>
  </div>
  <p class="description">{t('settings.overlapBehaviorDesc')}</p>
  <div bind:this={container} class="options" class:paused={!visible || !documentVisible}
    class:static={animationStore.isOff} role="radiogroup" aria-labelledby={`${id}-label`}>
    {#each options as option}
      {@const retrigger = option === 'retrigger'}
      <button type="button" role="radio" aria-checked={value === option}
        aria-labelledby={`${id}-${option}-title`} aria-describedby={`${id}-${option}-desc`}
        tabindex={value === option ? 0 : -1} data-option={option}
        class="option" class:selected={value === option} class:retrigger
        onclick={() => { if (option !== value) onchange(option); }} onkeydown={(event) => onKeydown(event, option)}>
        <span class="preview" aria-hidden="true">
          <svg class="roll" viewBox="0 0 320 140" fill="none">
            <defs>
              <clipPath id={`${id}-${option}-clip`}><rect x="47" y="12" width="273" height="106" /></clipPath>
              <linearGradient id={`${id}-${option}-fade`} x1="47" x2="320" gradientUnits="userSpaceOnUse">
                <stop offset="0" stop-color="white" />
                <stop offset="0.88" stop-color="white" />
                <stop offset="1" stop-color="white" stop-opacity="0" />
              </linearGradient>
              <mask id={`${id}-${option}-mask`} maskUnits="userSpaceOnUse" x="47" y="12" width="273" height="106">
                <rect x="47" y="12" width="273" height="106" fill={`url(#${id}-${option}-fade)`} />
              </mask>
            </defs>
            <g mask={`url(#${id}-${option}-mask)`}>
              <path class="grid" d="M47 12H320M47 47H320M47 83H320M47 118H320M106 12V118M166 12V118M226 12V118M286 12V118" />
              <g clip-path={`url(#${id}-${option}-clip)`}>
              <g class="notes">
                {#each notes as note, index}
                  <rect class="note" class:long-note={index === 0} class:overlap-note={index > 0}
                    x={note.x} y={51 + index * 11} width={note.width} height="9" rx="3" />
                {/each}
              </g>
              </g>
            </g>
            <rect class="white-key" x="2" y="13" width="43" height="33" rx="3" />
            <rect class="white-key" x="2" y="49" width="43" height="33" rx="3" />
            <rect class="key-down" x="2" y="49" width="43" height="33" rx="3" />
            <rect class="white-key" x="2" y="85" width="43" height="33" rx="3" />
            <rect class="black-key" x="2" y="39" width="26" height="16" rx="2" />
            <rect class="black-key" x="2" y="75" width="26" height="16" rx="2" />
            <path class="strike-line" d="M47 12V118" />
            <circle class="ripple first" cx="35" cy="66" r="10" />
            {#if retrigger}
              <circle class="ripple second" cx="35" cy="66" r="10" />
              <circle class="ripple third" cx="35" cy="66" r="10" />
            {/if}
            <g class="finger">
              <!-- A soft glove silhouette; the rounded fingertip meets the held key. -->
              <path class="hand" d="M29.5 94V69.5c0-7.3 11-7.3 11 0V84c0-6 9-6 9 0v3c0-5.8 8.5-5.5 8.5 0v5c0-5.4 8-5.1 8 .5v9c0 13-8 21.5-20 21.5-9 0-14.5-4-19.5-11l-8-12c-4.5-6.7 3.5-12 8-6l2.5 3.5Z" />
              <path class="hand-detail" d="M40.5 87v6m9-3v5m8.5-1v4m-18 15c3 2 7 2.5 10.5 1" />
            </g>
          </svg>
          <span class="preview-status">
            <span class="active-label" class:visible={value === option}>
              <FluentIcon name="Checkmark" size={12} />{t('settings.overlapActive')}
            </span>
            <span class="press-count">{t(retrigger ? 'settings.overlapThreePresses' : 'settings.overlapOnePress')}</span>
          </span>
        </span>
        <span class="option-copy">
          <span class="option-heading">
            <span id={`${id}-${option}-title`} class="option-title">{t(retrigger ? 'settings.overlapRetrigger' : 'settings.overlapHold')}</span>
          </span>
          <span id={`${id}-${option}-desc`} class="option-description">{t(retrigger ? 'settings.overlapRetriggerDesc' : 'settings.overlapHoldDesc')}</span>
        </span>
      </button>
    {/each}
  </div>
  <!-- Keep the sweep's mask outside the input's focus/change glow without moving the field. -->
  <GradientSweep visible={value === 'retrigger'} direction="vertical" gradientLength="24px" {reducedMotion}
    style="width: calc(100% + 32px); margin-inline: -16px;">
    <div class="repress-gap" inert={value !== 'retrigger'} aria-hidden={value !== 'retrigger'}>
      <span class="label">{t('settings.repressGap')}</span>
      <div class="repress-gap-controls">
        <NumberBox value={repressGapMs} min={0} max={1000} step={1} unit="ms"
          label={t('settings.repressGap')} onchange={onrepressgapchange} />
        <Button variant="ghost" size="icon" colorMode="danger"
          title={t('settings.repressGapReset')} ariaLabel={t('settings.repressGapReset')}
          disabled={repressGapMs === DEFAULT_APP_SETTINGS.repressGapMs}
          onclick={() => {
            if (!animationStore.isOff && !reducedMotion) resetTrigger = (resetTrigger ?? 0) + 1;
            onrepressgapchange(DEFAULT_APP_SETTINGS.repressGapMs);
          }}>
          <RefreshIcon size={15} trigger={resetTrigger} />
        </Button>
      </div>
      <p class="description">{t('settings.repressGapDesc')}</p>
    </div>
  </GradientSweep>
</div>

<style>
  .overlap-setting { display: flex; flex-direction: column; gap: 6px; min-width: 0; container-type: inline-size; }
  .repress-gap { display: flex; flex-direction: column; align-items: flex-start; gap: 6px; padding: 8px 16px 0; box-sizing: border-box; }
  .repress-gap-controls { display: flex; align-items: center; gap: 6px; }
  .repress-gap .description { margin-bottom: 0; }
  .heading, .option-heading { display: flex; justify-content: space-between; align-items: center; gap: 12px; }
  .label { font-size: 12.5px; font-weight: 600; color: var(--text-primary); }
  .description { margin: 0 0 6px; font-size: 11.5px; line-height: 1.5; color: var(--text-secondary); }
  .options { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 12px; }
  .option { display: flex; flex-direction: column; min-width: 0; padding: 0; overflow: hidden; border: 0; border-radius: 8px; background: var(--control-fill); color: var(--text-primary); text-align: left; font: inherit; cursor: pointer; transition: background var(--transition-fast), transform var(--transition-fast); }
  .option:hover { background: var(--control-fill-hover); }
  .option:active { transform: scale(0.985); }
  .option.selected { background: color-mix(in srgb, var(--text-primary) 9%, transparent); }
  .option:focus-visible { outline: 2px solid var(--text-secondary); outline-offset: 3px; }
  .preview { display: block; align-self: stretch; position: relative; padding: 6px 14px 0; background: color-mix(in srgb, var(--text-primary) 3%, transparent); }
  .roll { display: block; width: 100%; height: auto; aspect-ratio: 320 / 140; overflow: hidden; }
  .grid { stroke: var(--text-primary); stroke-opacity: 0.07; stroke-width: 1; }
  .note { fill: var(--accent); }
  .long-note { opacity: 0.65; }
  .overlap-note { opacity: 0.35; }
  .retrigger .overlap-note { opacity: 0.8; }
  .white-key { fill: color-mix(in srgb, var(--text-primary) 16%, var(--card-bg)); }
  .black-key { fill: var(--bg-primary); stroke: var(--text-primary); stroke-opacity: 0.12; }
  .key-down { fill: var(--accent); opacity: 0; }
  .strike-line { stroke: var(--accent); stroke-opacity: 0.5; }
  .notes { transform: translateX(220px); }
  .hand { fill: var(--bg-primary); stroke: var(--text-primary); stroke-width: 2.2; stroke-linecap: round; stroke-linejoin: round; }
  .hand-detail { stroke: var(--text-primary); stroke-opacity: 0.35; stroke-width: 1.6; stroke-linecap: round; stroke-linejoin: round; }
  .ripple { stroke: var(--accent); stroke-width: 1.5; opacity: 0; transform-box: fill-box; transform-origin: center; }
  .preview-status { display: flex; align-items: center; justify-content: space-between; gap: 8px; min-height: 14px; padding: 0 0 8px; font-size: 10px; }
  .active-label { display: inline-flex; align-items: center; gap: 4px; color: var(--accent); font-weight: 600; visibility: hidden; }
  .active-label.visible { visibility: visible; }
  .press-count { color: var(--text-tertiary); text-align: right; }
  .option-copy { display: flex; flex-direction: column; align-self: stretch; flex: 1; position: relative; isolation: isolate; gap: 6px; padding: 12px 14px; }
  .option-copy::before { content: ''; position: absolute; inset: 0; z-index: -1; pointer-events: none; background: linear-gradient(to top, color-mix(in srgb, var(--accent) 6%, transparent), transparent 48px); opacity: 0; transition: opacity var(--transition-fast); }
  .selected .option-copy::before { opacity: 1; }
  .option-title { font-size: 12px; font-weight: 600; transition: color var(--transition-fast); }
  .option-description { font-size: 11px; color: var(--text-secondary); line-height: 1.5; transition: color var(--transition-fast); }
  .selected .option-title { color: color-mix(in srgb, var(--accent) 20%, var(--text-primary)); }
  .selected .option-description { color: color-mix(in srgb, var(--accent) 12%, var(--text-secondary)); }
  .static .notes { animation: none; transform: translateX(-144px); }
  .static .finger { animation: none; transform: translateY(4px); }
  .static .key-down { animation: none; opacity: 0.65; }
  .static .ripple { animation: none; opacity: 0; }
  @media (prefers-reduced-motion: reduce) {
    .notes { animation: none; transform: translateX(-144px); }
    .finger { animation: none; transform: translateY(4px); }
    .key-down { animation: none; opacity: 0.65; }
    .ripple { animation: none; opacity: 0; }
    .option { transition: none; }
  }
  @media (max-width: 600px) { .options { grid-template-columns: minmax(0, 1fr); } }
  @container (max-width: 440px) { .options { grid-template-columns: minmax(0, 1fr); } }
</style>
