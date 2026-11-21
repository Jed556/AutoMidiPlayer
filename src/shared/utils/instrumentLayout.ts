import type { InstrumentConfig, InstrumentInputMode, InstrumentLayoutStyle, KeyboardLayoutConfig, KeyMatrix } from '../types/games.js';

export function instrumentLayoutIds(instrument: InstrumentConfig | null, mode: InstrumentInputMode | 'all'): string[] {
  if (!instrument) return [];
  if (mode === 'mixed') return instrument.pluckStrings && instrument.chordMode === 'wheel' && instrument.supportsChords !== false
    && instrument.guitar?.tuning.length === 6 && instrument.chords?.length && instrument.chordLayouts?.length ? instrument.fretLayouts ?? [] : [];
  if (mode === 'all') return [...new Set([...(instrument.keyboardLayouts ?? []), ...(instrument.chordLayouts ?? []), ...(instrument.fretLayouts ?? [])])];
  return instrument[mode === 'keyboard' ? 'keyboardLayouts' : mode === 'chords' ? 'chordLayouts' : 'fretLayouts'] ?? [];
}

export function guitarNotes(instrument: InstrumentConfig | null): number[] {
  if (!instrument?.guitar) return instrumentNotes(instrument);
  return [...new Set(instrument.guitar.tuning.flatMap(open => Array.from({ length: instrument.guitar!.fretCount + 1 }, (_, fret) => open + fret)))].sort((a, b) => a - b);
}

export function instrumentNotes(instrument: InstrumentConfig | null): number[] {
  return instrument?.keys ? instrument.keys.flat().filter((pitch): pitch is number => pitch !== null) : instrument?.notes ?? [];
}

/** Detection is shared; only the physical chord controls differ by layout. */
export function instrumentChordMode(instrument: InstrumentConfig | null, layout: KeyboardLayoutConfig | null): 'pads' | 'wheel' | null {
  if (!instrument || instrument.supportsChords === false || !instrument.chords?.length || !layout) return null;
  if (instrument.chordMode === 'wheel') return layout.chordWheel?.positions.length ? 'wheel' : null;
  return layout.chordKeys?.some(key => key.trim()) ? 'pads' : null;
}

export function instrumentLayoutStyle(instrument: InstrumentConfig | null): InstrumentLayoutStyle {
  if (instrument?.layoutStyle) return instrument.layoutStyle;
  if (!instrument || /^piano\d*$/i.test(instrument.type ?? '')) return 'none';
  if (instrument.type === 'drum8' || instrument.category?.toLowerCase() === 'percussion'
    || instrument.name?.toLowerCase().includes('drum')) return 'drums';
  if (instrument.type === 'diatonic15' || instrumentNotes(instrument).length === 15) return 'sky';
  return 'genshin';
}

export interface InstrumentKey {
  pitch: number;
  label: string;
  note: string;
  solfege: string;
  sharp: boolean;
}

const names = ['C', 'C#', 'D', 'D#', 'E', 'F', 'F#', 'G', 'G#', 'A', 'A#', 'B'];

/** Explicit matrices preserve row boundaries and null spacers; flat configs keep their old mapping order. */
export function instrumentKeyRows(instrument: InstrumentConfig | null, layout: KeyboardLayoutConfig | null): Array<Array<InstrumentKey | null>> {
  if (!instrument) return [];
  const notes = instrumentNotes(instrument);
  const style = instrumentLayoutStyle(instrument);
  let rows: KeyMatrix<number>;
  if (instrument.keys) {
    rows = instrument.keys;
  } else if (layout?.keyRows) {
    let index = 0;
    rows = layout.keyRows.map(row => row.map(key => key === null ? null : notes[index++] ?? null));
  } else {
    const columns = style === 'sky' ? 5 : style === 'drums' ? 4 : 7;
    rows = Array.from({ length: Math.ceil(notes.length / columns) }, (_, index) => notes.slice(index * columns, (index + 1) * columns));
  }
  let index = 0;
  return rows.map((row, rowIndex) => row.map((pitch, columnIndex) => {
    if (pitch === null) return null;
    const label = layout?.keyRows ? layout.keyRows[rowIndex]?.[columnIndex] ?? '' : layout?.keys[index] ?? '';
    index++;
    const noteClass = pitch % 12;
    return { pitch, label, note: `${names[noteClass]}${Math.floor(pitch / 12) - 1}`,
      solfege: String([1, 1, 2, 2, 3, 4, 4, 5, 5, 6, 6, 7][noteClass]),
      sharp: [1, 3, 6, 8, 10].includes(noteClass) };
  }));
}

export function instrumentKeyPairs(instrument: InstrumentConfig | null, layout: KeyboardLayoutConfig | null): Array<[number, string]> {
  if (layout?.fretboard || layout?.chordWheel) return [];
  return instrumentKeyRows(instrument, layout).flat().flatMap(key => key?.label ? [[key.pitch, key.label] as [number, string]] : []);
}

export type RawKeyboardLayout = Omit<KeyboardLayoutConfig, 'keys'> & { keys: string[] | KeyMatrix<string> };

/** Native IPC exposes flat bindings plus the source matrix; browser imports can also supply raw JSON. */
export function normalizeKeyboardLayout(layout: RawKeyboardLayout): KeyboardLayoutConfig {
  if (layout.keys.length && Array.isArray(layout.keys[0])) {
    const keyRows = layout.keys as KeyMatrix<string>;
    return { ...layout, keys: keyRows.flat().filter((key): key is string => key !== null), keyRows };
  }
  return { ...layout, keys: layout.keys as string[] };
}
