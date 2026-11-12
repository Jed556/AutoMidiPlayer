export interface ScrollPosition { x: number; y: number }
export type ScrollPositions = Record<string, ScrollPosition>;

const MAX_POSITION = 1_000_000_000;
export const MAX_SCROLL_POSITIONS = 128;

function validKey(key: string): boolean {
  return key.length > 0 && key.length <= 256 && !['__proto__', 'constructor', 'prototype'].includes(key);
}

export function normalizeScrollPosition(value: unknown): ScrollPosition | undefined {
  if (!value || typeof value !== 'object') return;
  const { x, y } = value as ScrollPosition;
  if (![x, y].every(number => typeof number === 'number' && Number.isFinite(number) && number >= 0)) return;
  return { x: Math.min(x, MAX_POSITION), y: Math.min(y, MAX_POSITION) };
}

/** Playhead following moves the rendered viewport, not the user's remembered horizontal position. */
export function manualViewportPosition(previous: ScrollPosition, current: ScrollPosition, horizontalLocked: boolean): ScrollPosition {
  const x = horizontalLocked ? previous.x : current.x;
  return x === previous.x && current.y === previous.y ? previous : { x, y: current.y };
}

/** Retain recent viewports without allowing a large song library to grow settings forever. */
export class ScrollPositionStore {
  private positions = new Map<string, ScrollPosition>();
  private loading: Promise<void> | undefined;
  private load: () => Promise<unknown>;
  private save: (positions: ScrollPositions) => void;

  constructor(load: () => Promise<unknown>, save: (positions: ScrollPositions) => void) {
    this.load = load;
    this.save = save;
  }

  ready(): Promise<void> {
    return this.loading ??= this.load().then(value => {
      if (!value || typeof value !== 'object' || Array.isArray(value)) return;
      for (const [key, raw] of Object.entries(value)) {
        const position = normalizeScrollPosition(raw);
        if (validKey(key) && position && !this.positions.has(key)) {
          this.positions.set(key, position);
          this.trim();
        }
      }
      this.trim();
    }).catch(error => { console.warn('[Scroll positions] Could not load viewports:', error); });
  }

  get(key: string): ScrollPosition | undefined {
    const value = this.positions.get(key);
    if (!value) return;
    // Visiting a viewport keeps it in the bounded history, even if it is not scrolled.
    this.positions.delete(key);
    this.positions.set(key, value);
    return { ...value };
  }

  set(key: string, raw: ScrollPosition): void {
    const position = normalizeScrollPosition(raw);
    if (!validKey(key) || !position) return;
    const previous = this.positions.get(key);
    if (previous?.x === position.x && previous.y === position.y) return;
    this.positions.delete(key);
    this.positions.set(key, position);
    this.trim();
    this.save(Object.fromEntries([...this.positions].map(([id, value]) => [id, { ...value }])));
  }

  private trim() {
    while (this.positions.size > MAX_SCROLL_POSITIONS) this.positions.delete(this.positions.keys().next().value!);
  }
}
