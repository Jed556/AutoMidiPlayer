import type { Action } from 'svelte/action';
import { createFrameResizeObserver } from '../utils/frameResizeObserver';

interface SearchContent {
  value: string;
  placeholder: string;
}

/** Fade only the edges that have hidden text, following the input's native scroll. */
export const searchOverflow: Action<HTMLInputElement, SearchContent> = (input) => {
  const context = document.createElement('canvas').getContext('2d');
  let frame: number | null = null;
  let destroyed = false;
  const previousMask = input.style.maskImage;

  function measure() {
    frame = null;
    const styles = getComputedStyle(input);
    const left = parseFloat(styles.paddingLeft) || 0;
    const right = parseFloat(styles.paddingRight) || 0;
    const width = input.clientWidth - left - right;
    let hiddenLeft = false;
    let hiddenRight = false;

    if (width > 0) {
      if (input.value) {
        const overflow = Math.max(0, input.scrollWidth - input.clientWidth);
        hiddenLeft = input.scrollLeft > 1;
        hiddenRight = overflow - input.scrollLeft > 1;
      } else if (context) {
        // Native placeholders do not contribute to an input's scrollWidth.
        context.font = `${styles.fontWeight} ${styles.fontSize} ${styles.fontFamily}`;
        const spacing = parseFloat(styles.letterSpacing) || 0;
        const textWidth = context.measureText(input.placeholder).width
          + Math.max(0, Array.from(input.placeholder).length - 1) * spacing;
        hiddenRight = textWidth - width > 1;
      }
    }

    input.style.maskImage = hiddenLeft || hiddenRight
      ? `linear-gradient(to right, ${hiddenLeft ? 'transparent' : '#000'} ${left}px, #000 ${left + Math.min(16, width / 2)}px, #000 calc(100% - ${right + Math.min(16, width / 2)}px), ${hiddenRight ? 'transparent' : '#000'} calc(100% - ${right}px))`
      : 'none';
  }

  function schedule() {
    if (!destroyed && frame === null) frame = requestAnimationFrame(measure);
  }

  // Measure after the browser scrolls the caret into view, including arrow keys,
  // selection, paste, and value changes made by the parent component.
  const events = ['input', 'scroll', 'keyup', 'pointerup', 'select', 'focus', 'blur'] as const;
  for (const event of events) input.addEventListener(event, schedule);
  const observer = createFrameResizeObserver(schedule);
  observer.observe(input);
  document.fonts.addEventListener('loadingdone', schedule);
  void document.fonts.ready.then(schedule);
  schedule();

  return {
    update: schedule,
    destroy() {
      destroyed = true;
      if (frame !== null) cancelAnimationFrame(frame);
      observer.disconnect();
      for (const event of events) input.removeEventListener(event, schedule);
      document.fonts.removeEventListener('loadingdone', schedule);
      input.style.maskImage = previousMask;
    },
  };
};
