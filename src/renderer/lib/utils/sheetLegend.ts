export interface SheetLegendRow {
  pitch: number;
  key: string;
  note: string;
}

/** Preserve config order and binding spelling, including Ctrl and Shift syntax. */
export function createSheetLegendRows(pairs: readonly (readonly [number, string])[]): SheetLegendRow[] {
  const names = ['C', 'C#', 'D', 'D#', 'E', 'F', 'F#', 'G', 'G#', 'A', 'A#', 'B'];
  return pairs.filter(([pitch, key]) => Number.isInteger(pitch) && pitch >= 0 && pitch <= 127 && key.length > 0)
    .map(([pitch, key]) => ({ pitch, key, note: `${names[pitch % 12]}${Math.floor(pitch / 12) - 1}` }));
}
