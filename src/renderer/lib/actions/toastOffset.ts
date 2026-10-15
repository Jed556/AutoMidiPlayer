import type { Action } from 'svelte/action';
import { onDestroy } from 'svelte';
import { createFrameResizeObserver } from '../utils/frameResizeObserver';
import { notificationCenter, type ToastPositionOffset } from '../ui/notificationCenter.svelte';

export interface ToastOffsetActionOptions extends ToastPositionOffset {
  /**
   * If true (or if bottom is not explicitly set), automatically measures the node's
   * height and uses it as the bottom offset.
   * Default: true when `bottom` is undefined.
   */
  auto?: boolean;
  /**
   * Additional pixel margin to add on top of the measured height.
   * Default: 0.
   */
  extraMargin?: number;
}

/**
 * Svelte action to set a custom toast position offset based on a DOM element
 * (e.g. `<div class="inst-bottom-bar" use:toastOffset>`).
 * Automatically adjusts if the element resizes, and resets the offset when destroyed.
 */
export const toastOffset: Action<HTMLElement, ToastOffsetActionOptions | undefined> = (node, options) => {
  const id = `toast-action-${Math.random().toString(36).slice(2, 8)}`;
  let currentOptions = options;

  function computeAndApply() {
    const isAuto = currentOptions?.auto ?? (currentOptions?.bottom === undefined);
    if (isAuto) {
      const measuredHeight = node.offsetHeight ||
        (typeof window !== 'undefined' ? parseFloat(window.getComputedStyle(node).minHeight) || 0 : 0);
      const extra = currentOptions?.extraMargin || 0;
      const offset: ToastPositionOffset = {
        ...currentOptions,
        bottom: (measuredHeight > 0 ? measuredHeight : 52) + extra
      };
      notificationCenter.setToastOffset(offset, id);
    } else {
      notificationCenter.setToastOffset(currentOptions || {}, id);
    }
  }

  computeAndApply();

  let ro: ReturnType<typeof createFrameResizeObserver> | null = null;
  if (typeof ResizeObserver !== 'undefined') {
    ro = createFrameResizeObserver(() => {
      computeAndApply();
    });
    ro.observe(node);
  }

  return {
    update(newOptions) {
      currentOptions = newOptions;
      computeAndApply();
    },
    destroy() {
      ro?.disconnect();
      notificationCenter.resetToastOffset(id);
    }
  };
};

/**
 * Helper to set custom toast position offset for the lifetime of a component.
 * Automatically cleans up when the component is unmounted.
 */
export function useToastOffset(offset: ToastPositionOffset | (() => ToastPositionOffset)) {
  const id = `use-offset-${Math.random().toString(36).slice(2, 8)}`;

  if (typeof offset === 'function') {
    $effect(() => {
      const val = offset();
      notificationCenter.setToastOffset(val, id);
      return () => {
        notificationCenter.resetToastOffset(id);
      };
    });
  } else {
    notificationCenter.setToastOffset(offset, id);
    onDestroy(() => {
      notificationCenter.resetToastOffset(id);
    });
  }
}
