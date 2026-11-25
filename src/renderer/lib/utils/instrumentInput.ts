/** Match labels from the displayed layout, preserving paired bindings such as q/Q. */
export function keyboardPitches(key: string, bindings: readonly { label: string; pitch: number }[]): number[] {
  const exact = bindings.filter(binding => binding.label === key);
  if (exact.length) return exact.map(binding => binding.pitch);
  return bindings.filter(binding => binding.label.toLowerCase() === key.toLowerCase())
    .map(binding => binding.pitch);
}

const MOUSE_BINDINGS = new Set([
  'mouseleft', 'mouse_left', 'mouse-left', 'lmb', 'leftclick', 'left_click',
  'mouseright', 'mouse_right', 'mouse-right', 'rmb', 'rightclick', 'right_click',
]);

/** Return whether a binding string refers to a mouse button rather than a keyboard key. */
export function isMouseBinding(binding: string | undefined): boolean {
  const normalized = binding?.trim().toLowerCase() ?? '';
  return Boolean(normalized) && MOUSE_BINDINGS.has(normalized);
}

/** Match a DOM KeyboardEvent.key against a configured key binding string. */
export function matchesKeyBinding(eventKey: string, binding: string | undefined): boolean {
  const normalized = binding?.trim() ?? '';
  if (!normalized || isMouseBinding(normalized)) return false;
  if (normalized === eventKey || normalized.toLowerCase() === eventKey.toLowerCase()) return true;
  return normalized.toLowerCase() === 'space' && eventKey === ' ';
}

/** Resolve MIDI pitches for a pressed physical key against chord pad bindings. */
export function chordPadPitches(
  key: string,
  chords: readonly { pitchClasses: readonly number[]; stringPitches?: readonly (number | 'muted' | null)[] }[],
  bindings: readonly string[],
): number[] {
  const exactIndex = bindings.findIndex((binding, index) => index < chords.length && !isMouseBinding(binding) && binding.trim() === key);
  const matchedIndex = exactIndex >= 0
    ? exactIndex
    : bindings.findIndex((binding, index) => index < chords.length && matchesKeyBinding(key, binding));
  if (matchedIndex < 0) return [];
  const chord = chords[matchedIndex];
  const stringNotes = chord.stringPitches?.filter((pitch): pitch is number => typeof pitch === 'number') ?? [];
  if (stringNotes.length) return [...new Set(stringNotes)];
  return chord.pitchClasses.map(pc => 60 + pc);
}

export type GuitarKeyAction =
  | { type: 'string'; index: number }
  | { type: 'strum'; direction: 'up' | 'down' }
  | { type: 'page'; delta: -1 | 1 }
  | { type: 'wedge'; delta: -1 | 1 }
  | { type: 'mode' }
  | { type: 'timbre' };

export interface GuitarKeyConfig {
  stringKeys?: readonly string[];
  strumButtons?: { up?: string; down?: string };
  pageKeys?: readonly string[];
  modeKey?: string;
  timbreKey?: string;
  allowArrows?: boolean;
  allowWedgeArrows?: boolean;
  allowPageKeys?: boolean;
  allowModeKey?: boolean;
  allowTimbreKey?: boolean;
}

/** Resolve a pressed physical key to a guitar preview action. */
export function resolveGuitarKeyAction(key: string, config: GuitarKeyConfig): GuitarKeyAction | null {
  const stringKeys = config.stringKeys ?? [];
  const exactString = stringKeys.findIndex(binding => !isMouseBinding(binding) && binding.trim() === key);
  const matchedString = exactString >= 0
    ? exactString
    : stringKeys.findIndex(binding => matchesKeyBinding(key, binding));
  if (matchedString >= 0) return { type: 'string', index: matchedString };

  if (matchesKeyBinding(key, config.strumButtons?.up)) return { type: 'strum', direction: 'up' };
  if (matchesKeyBinding(key, config.strumButtons?.down)) return { type: 'strum', direction: 'down' };

  if (config.allowPageKeys && config.pageKeys?.length) {
    if (matchesKeyBinding(key, config.pageKeys[0])) return { type: 'page', delta: -1 };
    if (config.pageKeys[1] && matchesKeyBinding(key, config.pageKeys[1])) return { type: 'page', delta: 1 };
  }

  if (config.allowModeKey && matchesKeyBinding(key, config.modeKey)) return { type: 'mode' };
  if (config.allowTimbreKey && matchesKeyBinding(key, config.timbreKey)) return { type: 'timbre' };

  if (config.allowArrows) {
    if (key === 'ArrowUp') return { type: 'strum', direction: 'up' };
    if (key === 'ArrowDown') return { type: 'strum', direction: 'down' };
  }

  if (config.allowWedgeArrows) {
    if (key === 'ArrowLeft') return { type: 'wedge', delta: -1 };
    if (key === 'ArrowRight') return { type: 'wedge', delta: 1 };
  }

  return null;
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
