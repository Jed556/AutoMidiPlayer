/** Keep short presses readable in Instrument View. This clock only drives rendering. */
export const MIN_PRESS_HIGHLIGHT_MS = 120;

export class PressHighlights<T> {
  private entries = new Map<string | number, { value: T; until: number }>();
  private pressed = new Set<string | number>();
  private timer: ReturnType<typeof setTimeout> | undefined;
  private nextExpiry = Infinity;

  constructor(
    private keyFor: (value: T) => string | number,
    private show: (values: T[]) => void,
  ) {}

  update(values: Iterable<T>) {
    const now = performance.now();
    const pressed = new Set<string | number>();
    let changed = false;
    for (const value of values) {
      const key = this.keyFor(value);
      if (pressed.has(key)) continue;
      pressed.add(key);
      if (this.pressed.has(key)) continue;
      // Move a retrigger to the end so the latest attack can drive the readout.
      this.entries.delete(key);
      this.entries.set(key, { value, until: now + MIN_PRESS_HIGHLIGHT_MS });
      changed = true;
    }
    this.pressed = pressed;
    this.refresh(now, changed);
  }

  clear() {
    clearTimeout(this.timer);
    this.timer = undefined;
    this.nextExpiry = Infinity;
    this.pressed.clear();
    if (this.entries.size) {
      this.entries.clear();
      this.show([]);
    }
  }

  private refresh(now: number, changed = false) {
    let nextExpiry = Infinity;
    for (const [key, { until }] of this.entries) {
      if (this.pressed.has(key)) continue;
      if (until > now) {
        nextExpiry = Math.min(nextExpiry, until);
      } else {
        this.entries.delete(key);
        changed = true;
      }
    }
    if (changed) this.show(Array.from(this.entries.values(), entry => entry.value));
    if (nextExpiry === this.nextExpiry) return;
    clearTimeout(this.timer);
    this.nextExpiry = nextExpiry;
    this.timer = Number.isFinite(nextExpiry) ? setTimeout(() => {
      this.nextExpiry = Infinity;
      this.refresh(performance.now());
    }, Math.ceil(nextExpiry - now)) : undefined;
  }
}
