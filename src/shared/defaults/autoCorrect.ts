import type { KeyboardLayoutConfig } from '../types/games.js';

/** Default to the largest keyboard layout strictly below this key count. */
export const DEFAULT_AUTO_CORRECT_BELOW = 36;
/** Set a layout key count to override the automatic default; null keeps it automatic. */
export const DEFAULT_AUTO_CORRECT_KEY_COUNT: number | null = null;

export function getAutoCorrectTicks(layouts: Record<string, KeyboardLayoutConfig>): number[] {
  return [0, ...new Set(Object.values(layouts).map(layout => layout.keys.length)
    .filter(count => count > 0))].sort((a, b) => a - b);
}

export function getDefaultAutoCorrectThreshold(
  ticks: number[],
  below = DEFAULT_AUTO_CORRECT_BELOW,
  preferred = DEFAULT_AUTO_CORRECT_KEY_COUNT,
): number {
  if (preferred !== null && ticks.includes(preferred)) return preferred;
  return Math.max(0, ...ticks.filter(count => count < below));
}

export function resolveAutoCorrectThreshold(value: number | undefined, ticks: number[]): number {
  if (value !== undefined && ticks.includes(value)) return value;
  // 4 was the old application default, rather than a layout key count.
  if (value === undefined || value === 4 || !Number.isFinite(value)) {
    return getDefaultAutoCorrectThreshold(ticks);
  }
  return Math.max(0, ...ticks.filter(count => count <= value));
}
