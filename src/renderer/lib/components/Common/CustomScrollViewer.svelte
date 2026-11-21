<script lang="ts">
  import { onMount, tick, type Snippet } from 'svelte';
  import { createFrameResizeObserver } from '../../utils/frameResizeObserver';
  import { attachScrollPersistence } from '../../actions/persistScroll';

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
    overlayScrollbar?: boolean; // Draw the scrollbar over content without reserving a gutter.
    scrollContainer?: HTMLDivElement; // Bindable ref to the scrollable element
    persistenceKey?: string;
    tabIndex?: number;
    ariaLabel?: string;
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
    overlayScrollbar = false,
    scrollContainer = $bindable(),
    persistenceKey,
    tabIndex = -1,
    ariaLabel,
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
  const viewportId = $props.id();
  let viewportHeight = $state(0);
  let contentHeight = $state(0);
  let scrollPosition = $state(0);
  let dragOffset: number | null = null;
  let scrollRange = $derived(Math.max(0, contentHeight - viewportHeight));
  let thumbHeight = $derived(Math.min(viewportHeight, Math.max(24, viewportHeight * viewportHeight / Math.max(1, contentHeight))));
  let thumbTop = $derived(scrollRange > 0 ? scrollPosition / scrollRange * (viewportHeight - thumbHeight) : 0);

  function moveOverlayThumb(event: PointerEvent) {
    if (dragOffset === null || !internalScrollEl) return;
    const rail = event.currentTarget as HTMLElement;
    const travel = viewportHeight - thumbHeight;
    internalScrollEl.scrollTop = travel > 0
      ? (event.clientY - rail.getBoundingClientRect().top - dragOffset) / travel * scrollRange : 0;
  }

  function startOverlayDrag(event: PointerEvent) {
    if (event.button !== 0) return;
    event.preventDefault();
    event.stopPropagation();
    const rail = event.currentTarget as HTMLElement;
    dragOffset = event.target === rail ? thumbHeight / 2
      : event.clientY - rail.getBoundingClientRect().top - thumbTop;
    rail.setPointerCapture(event.pointerId);
    moveOverlayThumb(event);
  }

  function handleOverlayKey(event: KeyboardEvent) {
    if (!internalScrollEl) return;
    const targets: Record<string, number> = {
      ArrowUp: scrollPosition - 40, ArrowDown: scrollPosition + 40,
      PageUp: scrollPosition - viewportHeight, PageDown: scrollPosition + viewportHeight,
      Home: 0, End: scrollRange,
    };
    if (event.key in targets) {
      event.preventDefault();
      event.stopPropagation();
      internalScrollEl.scrollTop = targets[event.key];
    }
  }

  // Update bindable prop
  $effect(() => {
    scrollContainer = internalScrollEl;
  });

  $effect(() => {
    if (internalScrollEl && persistenceKey) return attachScrollPersistence(internalScrollEl, persistenceKey);
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
    viewportHeight = clientHeight;
    contentHeight = scrollHeight;
    scrollPosition = scrollTop;
    const measuredBar = offsetWidth - clientWidth;
    scrollbarWidth = showScrollbar && !overlayScrollbar ? (measuredBar > 0 ? measuredBar : (maxScroll > 1 ? 8 : 0)) : 0;

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
    // Content can grow without resizing the fixed-height viewport.
    function observeChildren() {
      if (!internalScrollEl) return;
      for (const child of internalScrollEl.children) observer.observe(child);
      updateScrollFade();
    }
    if (!overlayScrollbar) return () => observer.disconnect();
    observeChildren();
    const mutations = new MutationObserver(() => {
      observer.disconnect();
      if (internalScrollEl) observer.observe(internalScrollEl);
      observeChildren();
    });
    mutations.observe(internalScrollEl, { childList: true, subtree: true });
    return () => { observer.disconnect(); mutations.disconnect(); };
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
  <!-- svelte-ignore a11y_no_noninteractive_element_interactions, a11y_no_noninteractive_tabindex (Scrollable regions may opt into keyboard focus.) -->
  <div
    class="scroll-content {contentClass}"
    class:hide-scrollbar={!showScrollbar || overlayScrollbar}
    id={viewportId}
    bind:this={internalScrollEl}
    role="region"
    tabindex={tabIndex}
    aria-label={ariaLabel}
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
  {#if overlayScrollbar && showScrollbar && scrollRange > 1}
    <div
      class="overlay-scrollbar"
      role="scrollbar"
      tabindex="0"
      aria-label="Scroll content"
      aria-controls={viewportId}
      aria-orientation="vertical"
      aria-valuemin={0}
      aria-valuemax={scrollRange}
      aria-valuenow={scrollPosition}
      onpointerdown={startOverlayDrag}
      onpointermove={moveOverlayThumb}
      onpointerup={(event) => { dragOffset = null; (event.currentTarget as HTMLElement).releasePointerCapture(event.pointerId); }}
      onlostpointercapture={() => { dragOffset = null; }}
      onkeydown={handleOverlayKey}
      onwheel={(event) => {
        if (!internalScrollEl) return;
        event.preventDefault();
        const unit = event.deltaMode === 1 ? 16 : event.deltaMode === 2 ? viewportHeight : 1;
        internalScrollEl.scrollTop += event.deltaY * unit;
        externalOnWheel?.(event);
      }}
    >
      <div class="overlay-scrollbar-thumb" style:height={`${thumbHeight}px`} style:transform={`translateY(${thumbTop}px)`}></div>
    </div>
  {/if}
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

  .overlay-scrollbar {
    position: absolute;
    inset: 0 0 0 auto;
    width: 8px;
    z-index: 20;
    touch-action: none;
    user-select: none;
  }

  .overlay-scrollbar-thumb {
    width: 8px;
    border-radius: 4px;
    background: color-mix(in srgb, var(--text-primary) 25%, transparent);
  }

  .overlay-scrollbar:hover .overlay-scrollbar-thumb,
  .overlay-scrollbar:focus-visible .overlay-scrollbar-thumb {
    background: color-mix(in srgb, var(--text-primary) 45%, transparent);
  }

  .overlay-scrollbar:focus-visible {
    outline: 1px solid var(--accent-color);
    outline-offset: -1px;
    border-radius: 4px;
  }
</style>
