/** Match labels from the displayed layout, preserving paired bindings such as q/Q. */
export function keyboardPitches(key: string, bindings: readonly { label: string; pitch: number }[]): number[] {
  const exact = bindings.filter(binding => binding.label === key);
  if (exact.length) return exact.map(binding => binding.pitch);
  return bindings.filter(binding => binding.label.toLowerCase() === key.toLowerCase())
    .map(binding => binding.pitch);
}

/** Each physical source owns its notes, so releasing one cannot clear another. */
export class InstrumentInput {
  private sources = new Map<string, number[]>();

  constructor(private changed: (pitches: Set<number>) => void) {}

  press(source: string, pitches: number[]): boolean {
    if (this.sources.has(source) || !pitches.length) return false;
    this.sources.set(source, pitches);
    this.publish();
    return true;
  }

  release(source: string) {
    if (this.sources.delete(source)) this.publish();
  }

  clear(prefix = '') {
    for (const source of this.sources.keys()) {
      if (source.startsWith(prefix)) this.sources.delete(source);
    }
    this.publish();
  }

  midi(data: ArrayLike<number>): number | null {
    if (data.length < 3) return null;
    const [status, note, velocity] = [data[0], data[1], data[2]];
    const command = status >> 4;
    const channel = status & 0x0f;
    const source = `midi:${channel}:${note}`;
    if (command === 9 && velocity > 0) {
      return this.press(source, [note]) ? note : null;
    }
    if (command === 8 || (command === 9 && velocity === 0)) this.release(source);
    if (command === 11 && (note === 120 || note === 123)) this.clear(`midi:${channel}:`);
    return null;
  }

  private publish() {
    this.changed(new Set([...this.sources.values()].flat()));
  }
}
