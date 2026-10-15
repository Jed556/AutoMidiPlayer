<script lang="ts">
  import { onMount, tick, type Snippet } from 'svelte';
  import { createFrameResizeObserver } from '../../utils/frameResizeObserver';

  interface Props {
    children?: Snippet;
    class?: string;
    contentClass?: string;
    style?: string;
    contentStyle?: string;
    padding?: string;          // Padding for the scroll content container
    gap?: string;              // Gap between scroll items
    fadeTop?: boolean;         // Enable top edge fade effect (default true)
    fadeBottom?: boolean;      // Enable bottom edge fade effect (default true)
    fadeHeight?: number;       // Height of the fade zone in px (default 36)
    fadeRampDistance?: number; // Scroll distance in px to reach maximum fade (default 32)
    showScrollbar?: boolean;   // Whether to show the scrollbar (default true)
    scrollContainer?: HTMLDivElement; // Bindable ref to the scrollable element
    onscroll?: (e: Event) => void;
    onwheel?: (e: WheelEvent) => void;
    onpointerdown?: (e: PointerEvent) => void;
    onclick?: (e: MouseEvent) => void;
    onkeydown?: (e: KeyboardEvent) => void;
    ondragover?: (e: DragEvent) => void;
    ondragleave?: (e: DragEvent) => void;
    ondrop?: (e: DragEvent) => void;
  }

  let {
    children,
    class: customClass = '',
    contentClass = '',
    style = '',
    contentStyle = '',
    padding = '',
    gap = '',
    fadeTop = true,
    fadeBottom = true,
    fadeHeight = 36,
    fadeRampDistance = 32,
    showScrollbar = true,
    scrollContainer = $bindable(),
    onscroll: externalOnScroll,
    onwheel: externalOnWheel,
    onpointerdown: externalOnPointerDown,
    onclick: externalOnClick,
    onkeydown: externalOnKeyDown,
    ondragover: externalOnDragOver,
    ondragleave: externalOnDragLeave,
    ondrop: externalOnDrop
  }: Props = $props();

  let wrapperEl = $state<HTMLDivElement>();
  let internalScrollEl = $state<HTMLDivElement>();

  let topFadeOpacity = $state(0);
  let bottomFadeOpacity = $state(0);
  let scrollbarWidth = $state(0);

  // Update bindable prop
  $effect(() => {
    scrollContainer = internalScrollEl;
  });

  export function updateScrollFade() {
    if (!internalScrollEl) {
      topFadeOpacity = 0;
      bottomFadeOpacity = 0;
      scrollbarWidth = 0;
      return;
    }

    const { scrollTop, scrollHeight, clientHeight, offsetWidth, clientWidth } = internalScrollEl;
    const maxScroll = scrollHeight - clientHeight;
    const measuredBar = offsetWidth - clientWidth;
    scrollbarWidth = showScrollbar ? (measuredBar > 0 ? measuredBar : (maxScroll > 1 ? 8 : 0)) : 0;

    if (maxScroll <= 1) {
      topFadeOpacity = 0;
      bottomFadeOpacity = 0;
      return;
    }

    const rampDist = Math.max(1, fadeRampDistance);
    const topFactor = Math.min(Math.max(scrollTop / rampDist, 0), 1);
    const remainingToBottom = maxScroll - scrollTop;
    const bottomFactor = Math.min(Math.max(remainingToBottom / rampDist, 0), 1);

    topFadeOpacity = Math.pow(topFactor, 0.55);
    bottomFadeOpacity = Math.pow(bottomFactor, 0.55);
  }

  function handleScroll(e: Event) {
    updateScrollFade();
    externalOnScroll?.(e);
  }

  export function scrollTo(options: ScrollToOptions): void;
  export function scrollTo(x: number, y: number): void;
  export function scrollTo(xOrOptions: number | ScrollToOptions, y?: number): void {
    if (!internalScrollEl) return;
    if (typeof xOrOptions === 'object') {
      internalScrollEl.scrollTo(xOrOptions);
    } else {
      internalScrollEl.scrollTo(xOrOptions, y ?? 0);
    }
  }

  export function scrollBy(options: ScrollToOptions): void;
  export function scrollBy(x: number, y: number): void;
  export function scrollBy(xOrOptions: number | ScrollToOptions, y?: number): void {
    if (!internalScrollEl) return;
    if (typeof xOrOptions === 'object') {
      internalScrollEl.scrollBy(xOrOptions);
    } else {
      internalScrollEl.scrollBy(xOrOptions, y ?? 0);
    }
  }

  export function getScrollElement(): HTMLDivElement | undefined {
    return internalScrollEl;
  }

  onMount(() => {
    tick().then(() => {
      updateScrollFade();
    });
  });

  // Re-calculate when children or wrapper resizes
  $effect(() => {
    if (!internalScrollEl) return;
    const observer = createFrameResizeObserver(() => {
      updateScrollFade();
    });
    observer.observe(internalScrollEl);
    return () => observer.disconnect();
  });

  // Mask stop values for pure alpha clipping:
  // At 0px, alpha is (1 - topFadeOpacity). Content dissolves directly into whatever background is behind the viewer.
  // Because it's a pure alpha mask (color-agnostic), any theme switch or acrylic blur transitions seamlessly in real time!
  let topMaskAlpha = $derived(fadeTop ? Math.max(0, 1 - topFadeOpacity) : 1);
  let bottomMaskAlpha = $derived(fadeBottom ? Math.max(0, 1 - bottomFadeOpacity) : 1);

  let hasActiveFade = $derived(topMaskAlpha < 0.999 || bottomMaskAlpha < 0.999);

  let maskStyle = $derived.by(() => {
    if (!hasActiveFade) {
      return '';
    }
    const maskGrad = `linear-gradient(
      to bottom,
      rgba(0, 0, 0, ${topMaskAlpha}) 0px,
      #000 ${fadeHeight}px,
      #000 calc(100% - ${fadeHeight}px),
      rgba(0, 0, 0, ${bottomMaskAlpha}) 100%
    )`;
    const solidBar = `linear-gradient(to bottom, #000, #000)`;
    const maskSize = `calc(100% - ${scrollbarWidth}px) 100%, ${scrollbarWidth}px 100%`;
    const maskPos = `left top, right top`;

    return `
      -webkit-mask-image: ${maskGrad}, ${solidBar};
      -webkit-mask-size: ${maskSize};
      -webkit-mask-position: ${maskPos};
      -webkit-mask-repeat: no-repeat;
      mask-image: ${maskGrad}, ${solidBar};
      mask-size: ${maskSize};
      mask-position: ${maskPos};
      mask-repeat: no-repeat;
    `;
  });
</script>

<div
  class="custom-scroll-viewer {customClass}"
  bind:this={wrapperEl}
  {style}
>
  <!-- Scrollable Viewport with Pure Alpha Gradient Fade -->
  <!-- svelte-ignore a11y_no_noninteractive_element_interactions -->
  <div
    class="scroll-content {contentClass}"
    class:hide-scrollbar={!showScrollbar}
    bind:this={internalScrollEl}
    role="region"
    tabindex="-1"
    onscroll={handleScroll}
    onwheel={externalOnWheel}
    onpointerdown={externalOnPointerDown}
    onclick={externalOnClick}
    onkeydown={externalOnKeyDown}
    ondragover={externalOnDragOver}
    ondragleave={externalOnDragLeave}
    ondrop={externalOnDrop}
    style="
      {padding ? `padding: ${padding};` : ''}
      {gap ? `gap: ${gap};` : ''}
      {contentStyle}
      {maskStyle}
    "
  >
    {#if children}
      {@render children()}
    {/if}
  </div>
</div>

<style>
  .custom-scroll-viewer {
    position: relative;
    flex: 1;
    min-height: 0;
    width: 100%;
    display: flex;
    flex-direction: column;
    overflow: hidden;
  }

  .scroll-content {
    flex: 1;
    min-height: 0;
    width: 100%;
    overflow-y: auto;
    overflow-x: hidden;
    display: flex;
    flex-direction: column;
    box-sizing: border-box;
  }

  .scroll-content.hide-scrollbar {
    scrollbar-width: none;
    -ms-overflow-style: none;
  }

  .scroll-content.hide-scrollbar::-webkit-scrollbar {
    display: none;
  }
</style>
