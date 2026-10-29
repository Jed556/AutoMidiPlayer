<script lang="ts">
  import { onMount } from 'svelte';
  import { animationStore } from '../../state/animationStore.svelte';
  import type { Snippet } from 'svelte';

  interface Props {
    visible?: boolean;
    direction?: 'horizontal' | 'vertical';
    /** Anchor side for horizontal collapse: 'left' (sweeps at right edge) or 'right' (sweeps at left edge) */
    anchor?: 'left' | 'right';
    class?: string;
    style?: string;
    gradientLength?: string;
    collapseMargin?: 'left' | 'right' | 'top' | 'bottom' | 'none';
    marginAmount?: string;
    overflowVisibleOnComplete?: boolean;
    reducedMotion?: boolean;
    children?: Snippet;
  }

  let {
    visible = true,
    direction = 'horizontal',
    anchor,
    class: className = '',
    style = '',
    gradientLength = '16px',
    collapseMargin = 'none',
    marginAmount = '16px',
    overflowVisibleOnComplete = true,
    reducedMotion = false,
    children
  }: Props = $props();

  let containerEl = $state<HTMLElement | null>(null);
  let innerEl = $state<HTMLElement | null>(null);
  let isMounted = $state(false);
  let lastVisible: boolean | null = null;

  const sizeProp = $derived(direction === 'horizontal' ? 'width' : 'height');
  const effectiveAnchor = $derived(anchor || 'left');
  const marginProp = $derived(
    collapseMargin === 'left' ? 'marginLeft' :
    collapseMargin === 'right' ? 'marginRight' :
    collapseMargin === 'top' ? 'marginTop' :
    collapseMargin === 'bottom' ? 'marginBottom' : null
  );
  const cssMarginProp = $derived(
    collapseMargin === 'left' ? 'margin-left' :
    collapseMargin === 'right' ? 'margin-right' :
    collapseMargin === 'top' ? 'margin-top' :
    collapseMargin === 'bottom' ? 'margin-bottom' : null
  );

  onMount(() => {
    isMounted = true;
    lastVisible = visible;
    if (!containerEl || !innerEl) return;

    if (visible) {
      containerEl.style[sizeProp] = 'auto';
      containerEl.style.opacity = '1';
      containerEl.style.overflow = overflowVisibleOnComplete ? 'visible' : 'hidden';
      containerEl.style.setProperty('--gradient-sweep-mask', '#000000');
      if (marginProp) containerEl.style[marginProp] = marginAmount;
    } else {
      containerEl.style[sizeProp] = '0px';
      containerEl.style.opacity = '0';
      containerEl.style.display = 'none';
      containerEl.style.overflow = 'hidden';
      containerEl.style.setProperty('--gradient-sweep-mask', 'transparent');
      if (marginProp) containerEl.style[marginProp] = '0px';
    }
  });

  $effect(() => {
    const isNowVisible = visible;
    if (!isMounted || !containerEl || !innerEl) {
      lastVisible = isNowVisible;
      return;
    }
    if (lastVisible === null) {
      lastVisible = isNowVisible;
      return;
    }
    if (isNowVisible === lastVisible) return;
    lastVisible = isNowVisible;

    animateTransition(isNowVisible);
  });

  function animateTransition(targetVisible: boolean) {
    if (!containerEl || !innerEl) return;

    const isOff = reducedMotion || animationStore.isOff || (typeof document !== 'undefined' && document.documentElement.classList.contains('no-animations'));
    const durationSec = Math.max(0.12, animationStore.timing);
    const easeCurve = 'cubic-bezier(0.4, 0, 0.2, 1)';

    if (targetVisible) {
      // Reveal
      containerEl.style.display = '';
      containerEl.style.overflow = 'hidden';

      // Measure target natural size
      let targetSize: number;
      if (direction === 'horizontal') {
        targetSize = Math.ceil(innerEl.getBoundingClientRect().width || innerEl.scrollWidth || innerEl.offsetWidth);
      } else {
        targetSize = Math.ceil(innerEl.getBoundingClientRect().height || innerEl.scrollHeight || innerEl.offsetHeight);
      }

      if (isOff || targetSize <= 0) {
        containerEl.style.transition = 'none';
        containerEl.style[sizeProp] = 'auto';
        containerEl.style.opacity = '1';
        containerEl.style.setProperty('--gradient-sweep-mask', '#000000');
        if (marginProp) containerEl.style[marginProp] = marginAmount;
        if (overflowVisibleOnComplete) containerEl.style.overflow = 'visible';
        return;
      }

      const currentSize = Math.max(0, containerEl.getBoundingClientRect()[sizeProp] || 0);
      containerEl.style.transition = 'none';
      containerEl.style[sizeProp] = `${currentSize}px`;
      containerEl.style.opacity = '0';
      containerEl.style.setProperty('--gradient-sweep-mask', 'transparent');
      if (marginProp) containerEl.style[marginProp] = '0px';

      void containerEl.offsetHeight; // Force synchronous reflow

      const opacityTransition = `opacity ${(durationSec * 0.85).toFixed(2)}s ${easeCurve} 0s`;
      const maskTransition = `--gradient-sweep-mask ${durationSec}s ${easeCurve}`;
      const sizeTransition = `${sizeProp} ${durationSec}s ${easeCurve}`;
      const marginTransition = (marginProp && cssMarginProp) ? `, ${cssMarginProp} ${durationSec}s ${easeCurve}` : '';

      containerEl.style.transition = `${sizeTransition}, ${maskTransition}, ${opacityTransition}${marginTransition}`;
      containerEl.style[sizeProp] = `${targetSize}px`;
      containerEl.style.opacity = '1';
      containerEl.style.setProperty('--gradient-sweep-mask', '#000000');
      if (marginProp) containerEl.style[marginProp] = marginAmount;

      const onEnd = (e: TransitionEvent) => {
        if (e.target !== containerEl || e.propertyName !== sizeProp) return;
        containerEl?.removeEventListener('transitionend', onEnd);
        if (lastVisible && containerEl) {
          containerEl.style[sizeProp] = 'auto';
          containerEl.style.setProperty('--gradient-sweep-mask', '#000000');
          if (overflowVisibleOnComplete) {
            containerEl.style.overflow = 'visible';
          }
        }
      };
      containerEl.addEventListener('transitionend', onEnd);
    } else {
      // Collapse
      containerEl.style.overflow = 'hidden';

      let currentSize: number;
      if (direction === 'horizontal') {
        currentSize = Math.ceil(containerEl.getBoundingClientRect().width);
      } else {
        currentSize = Math.ceil(containerEl.getBoundingClientRect().height);
      }

      if (isOff || currentSize <= 0) {
        containerEl.style.transition = 'none';
        containerEl.style[sizeProp] = '0px';
        containerEl.style.opacity = '0';
        containerEl.style.display = 'none';
        containerEl.style.setProperty('--gradient-sweep-mask', 'transparent');
        if (marginProp) containerEl.style[marginProp] = '0px';
        return;
      }

      containerEl.style.transition = 'none';
      containerEl.style[sizeProp] = `${currentSize}px`;
      containerEl.style.opacity = '1';
      containerEl.style.setProperty('--gradient-sweep-mask', 'transparent');
      if (marginProp) containerEl.style[marginProp] = marginAmount;

      void containerEl.offsetHeight; // Force synchronous reflow

      const fadeDuration = (durationSec * 0.45).toFixed(2);
      const fadeDelay = (durationSec * 0.55).toFixed(2);
      const opacityTransition = `opacity ${fadeDuration}s ${easeCurve} ${fadeDelay}s`;
      const sizeTransition = `${sizeProp} ${durationSec}s ${easeCurve}`;
      const marginTransition = (marginProp && cssMarginProp) ? `, ${cssMarginProp} ${durationSec}s ${easeCurve}` : '';

      containerEl.style.transition = `${sizeTransition}, ${opacityTransition}${marginTransition}`;
      containerEl.style[sizeProp] = '0px';
      containerEl.style.opacity = '0';
      if (marginProp) containerEl.style[marginProp] = '0px';

      const onEnd = (e: TransitionEvent) => {
        if (e.target !== containerEl || e.propertyName !== sizeProp) return;
        containerEl?.removeEventListener('transitionend', onEnd);
        if (!lastVisible && containerEl) {
          containerEl.style.display = 'none';
        }
      };
      containerEl.addEventListener('transitionend', onEnd);
    }
  }
</script>

<div
  bind:this={containerEl}
  class="gradient-sweep {direction} anchor-{effectiveAnchor} {className}"
  style="--sweep-len: {gradientLength}; {style}"
>
  <div bind:this={innerEl} class="gradient-sweep-inner">
    {@render children?.()}
  </div>
</div>

<style>
  .gradient-sweep {
    box-sizing: border-box;
    position: relative;
    --gradient-sweep-mask: #000000;
    will-change: width, height, opacity;
  }

  .gradient-sweep.horizontal {
    display: inline-flex;
    flex-shrink: 0;
    -webkit-mask-image: linear-gradient(
      to right,
      #000000 0%,
      #000000 calc(100% - var(--sweep-len, 16px)),
      var(--gradient-sweep-mask) 100%
    );
    mask-image: linear-gradient(
      to right,
      #000000 0%,
      #000000 calc(100% - var(--sweep-len, 16px)),
      var(--gradient-sweep-mask) 100%
    );
    -webkit-mask-size: 100% 100%;
    mask-size: 100% 100%;
    -webkit-mask-repeat: no-repeat;
    mask-repeat: no-repeat;
  }

  .gradient-sweep.horizontal.anchor-right {
    justify-content: flex-end;
    -webkit-mask-image: linear-gradient(
      to right,
      var(--gradient-sweep-mask) 0%,
      #000000 var(--sweep-len, 16px),
      #000000 100%
    );
    mask-image: linear-gradient(
      to right,
      var(--gradient-sweep-mask) 0%,
      #000000 var(--sweep-len, 16px),
      #000000 100%
    );
  }

  .gradient-sweep.horizontal.anchor-right .gradient-sweep-inner {
    justify-content: flex-end;
    text-align: right;
  }

  .gradient-sweep.horizontal .gradient-sweep-inner {
    display: inline-flex;
    align-items: center;
    width: max-content;
    white-space: nowrap;
    box-sizing: border-box;
  }

  .gradient-sweep.vertical {
    display: flex;
    flex-direction: column;
    width: 100%;
    -webkit-mask-image: linear-gradient(
      to bottom,
      #000000 0%,
      #000000 calc(100% - var(--sweep-len, 16px)),
      var(--gradient-sweep-mask) 100%
    );
    mask-image: linear-gradient(
      to bottom,
      #000000 0%,
      #000000 calc(100% - var(--sweep-len, 16px)),
      var(--gradient-sweep-mask) 100%
    );
    -webkit-mask-size: 100% 100%;
    mask-size: 100% 100%;
    -webkit-mask-repeat: no-repeat;
    mask-repeat: no-repeat;
  }

  .gradient-sweep.vertical .gradient-sweep-inner {
    width: 100%;
    display: flex;
    flex-direction: column;
    box-sizing: border-box;
  }
</style>
