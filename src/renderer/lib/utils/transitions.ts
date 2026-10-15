import { cubicOut, cubicIn } from 'svelte/easing';
import type { TransitionConfig } from 'svelte/transition';
import { animationStore } from '../state/animationStore.svelte';

export interface ScaleFadeOptions {
  delay?: number;
  duration?: number;
  start?: number;
  opacity?: number;
  easing?: (t: number) => number;
}

/**
 * Universal Scale + Fade transition for dialogs, modals, and popup boxes.
 * Smoothly scales between `start` and 1.0 while cross-fading opacity between `opacity` and 1.0.
 * Preserves any existing CSS transform on the element (e.g. centering translateX(-50%)).
 */
export function scaleFade(
  node: Element,
  {
    delay = 0,
    duration,
    start = 0.82,
    opacity = 0,
    easing = cubicOut
  }: ScaleFadeOptions = {}
): TransitionConfig {
  const currentDuration = duration !== undefined ? duration : animationStore.durationMs;
  if (currentDuration <= 0) {
    return { duration: 0 };
  }

  const style = getComputedStyle(node);
  const targetOpacity = +style.opacity || 1;
  const existingTransform = style.transform === 'none' ? '' : style.transform;
  const opacityDelta = targetOpacity * (1 - opacity);
  const scaleDelta = 1 - start;

  return {
    delay,
    duration: currentDuration,
    easing,
    css: (_t, u) => `
      opacity: ${targetOpacity - opacityDelta * u};
      transform: ${existingTransform ? `${existingTransform} ` : ''}scale(${1 - scaleDelta * u});
    `
  };
}

/**
 * Modal Dialog Entrance: scale up from 0.82 to 1.0 with smooth deceleration
 */
export function dialogScaleIn(node: Element, options: ScaleFadeOptions = {}): TransitionConfig {
  if (animationStore.isOff) {
    return { duration: 0 };
  }
  return scaleFade(node, {
    duration: animationStore.durationMs,
    start: 0.82,
    opacity: 0,
    easing: cubicOut,
    ...options
  });
}

/**
 * Modal Dialog Exit: scale down from 1.0 to 0.82 with acceleration
 */
export function dialogScaleOut(node: Element, options: ScaleFadeOptions = {}): TransitionConfig {
  if (animationStore.isOff) {
    return { duration: 0 };
  }
  return scaleFade(node, {
    duration: Math.round(animationStore.durationMs * 0.75),
    start: 0.82,
    opacity: 0,
    easing: cubicIn,
    ...options
  });
}

/**
 * Dim backdrop fade in/out for modal dialogs
 */
export function dialogBackdrop(
  node: Element,
  { duration, delay = 0 }: { duration?: number; delay?: number } = {}
): TransitionConfig {
  if (animationStore.isOff) {
    return { duration: 0 };
  }
  const currentDuration = duration !== undefined ? duration : animationStore.durationMs;
  return {
    delay,
    duration: currentDuration,
    easing: cubicOut,
    css: (t) => `opacity: ${t};`
  };
}

/**
 * Popup / Flyout Entrance: scale up from 0.88 to 1.0
 */
export function popupScaleIn(node: Element, options: ScaleFadeOptions = {}): TransitionConfig {
  if (animationStore.isOff) {
    return { duration: 0 };
  }
  return scaleFade(node, {
    duration: Math.round(animationStore.durationMs * 0.9),
    start: 0.88,
    opacity: 0,
    easing: cubicOut,
    ...options
  });
}

/**
 * Popup / Flyout Exit: scale down from 1.0 to 0.88
 */
export function popupScaleOut(node: Element, options: ScaleFadeOptions = {}): TransitionConfig {
  if (animationStore.isOff) {
    return { duration: 0 };
  }
  return scaleFade(node, {
    duration: Math.round(animationStore.durationMs * 0.7),
    start: 0.88,
    opacity: 0,
    easing: cubicIn,
    ...options
  });
}

/**
 * View / Page Entrance transition matching WPF EntranceTransition:
 * Subtly slides up from translateY(16px) to translateY(0) and fades in opacity (0 -> 1).
 * Decelerates with smooth cubic curve over animationStore.durationMs.
 */
export function viewEntrance(
  node: Element,
  {
    delay = 0,
    duration,
    y = 16,
    easing = cubicOut
  }: { delay?: number; duration?: number; y?: number; easing?: (t: number) => number } = {}
): TransitionConfig {
  if (animationStore.isOff) {
    return { duration: 0 };
  }
  const currentDuration = duration !== undefined ? duration : animationStore.durationMs;
  if (currentDuration <= 0) {
    return { duration: 0 };
  }

  return {
    delay,
    duration: currentDuration,
    easing,
    css: (t, u) => `
      opacity: ${t};
      transform: translateY(${u * y}px);
    `
  };
}

/**
 * Exact cubic bezier solver matching CSS cubic-bezier(x1, y1, x2, y2).
 * Uses Newton-Raphson to solve for t given progress x.
 */
export function solveCubicBezier(p1x: number, p1y: number, p2x: number, p2y: number): (x: number) => number {
  const ax = 1 - 3 * p2x + 3 * p1x;
  const bx = 3 * p2x - 6 * p1x;
  const cx = 3 * p1x;

  const ay = 1 - 3 * p2y + 3 * p1y;
  const by = 3 * p2y - 6 * p1y;
  const cy = 3 * p1y;

  function sampleCurveX(t: number): number {
    return ((ax * t + bx) * t + cx) * t;
  }

  function sampleCurveDerivativeX(t: number): number {
    return (3 * ax * t + 2 * bx) * t + cx;
  }

  function sampleCurveY(t: number): number {
    return ((ay * t + by) * t + cy) * t;
  }

  return function (x: number): number {
    if (x <= 0) return 0;
    if (x >= 1) return 1;

    let t = x;
    for (let i = 0; i < 8; i++) {
      const currentX = sampleCurveX(t) - x;
      if (Math.abs(currentX) < 1e-6) break;
      const dX = sampleCurveDerivativeX(t);
      if (Math.abs(dX) < 1e-6) break;
      t -= currentX / dX;
      t = Math.max(0, Math.min(1, t));
    }
    return sampleCurveY(t);
  };
}

/** Pre-computed easing function for CSS cubic-bezier(0.4, 0, 0.2, 1) matching Seekbar */
export const easeSeekbar = solveCubicBezier(0.4, 0, 0.2, 1);

