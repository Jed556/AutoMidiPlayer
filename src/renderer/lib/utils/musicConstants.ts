export const NOTE_NAMES = ['C', 'C#', 'D', 'D#', 'E', 'F', 'F#', 'G', 'G#', 'A', 'A#', 'B'] as const;

export interface KeyOptionItem {
  value: number;
  label: string;
  noteDisplay: string;
  offsetDisplay: string;
}

export function getKeyNoteName(offset: number): string {
  const normalized = ((offset % 12) + 12) % 12;
  return NOTE_NAMES[normalized];
}

export function getKeyNoteNameWithOctave(offset: number): string {
  return `${getKeyNoteName(offset).replace('#', '\u266f')}${3 + Math.floor(offset / 12)}`;
}

export function formatSignedOffset(offset: number): string {
  if (offset > 0) return `+${offset}`;
  return `${offset}`;
}

/** Legacy labels already include the offset that marker badges render separately. */
export function getMarkerNoteDisplay(offset: number, label?: string): string {
  let note = label?.trim() || getKeyNoteName(offset);
  let suffix: RegExpMatchArray | null;
  while ((suffix = note.match(/\s*\(([+-]?\d+)\)\s*$/)) && Number(suffix[1]) === offset) {
    const name = note.slice(0, suffix.index).trimEnd();
    if (!name) break;
    note = name;
  }
  return note;
}

export function formatKeyDisplay(offset: number, noteName?: string): string {
  const note = noteName ?? getKeyNoteName(offset);
  const signed = formatSignedOffset(offset);
  return `${note} (${signed})`;
}

/**
 * Generate standard key offset options (-12 to +12) matching WPF MusicConstants.GenerateKeyOptions()
 */
export function generateKeyOptions(baseKeyOffset: number = 0): KeyOptionItem[] {
  const options: KeyOptionItem[] = [];
  for (let offset = -12; offset <= 12; offset++) {
    const effectiveOffset = offset + baseKeyOffset;
    const noteDisplay = getKeyNoteName(effectiveOffset);
    const offsetDisplay = formatSignedOffset(offset);
    options.push({
      value: offset,
      noteDisplay,
      offsetDisplay,
      label: `${noteDisplay} (${offsetDisplay})`
    });
  }
  return options;
}

export const TRACK_COLORS = [
  '#00B4D8', // 0: Cyan
  '#06D6A0', // 1: Emerald
  '#FFB703', // 2: Amber
  '#FB8500', // 3: Coral Orange
  '#8338EC', // 4: Purple
  '#FF006E', // 5: Pink
  '#3A86FF', // 6: Blue
  '#70E000', // 7: Lime
  '#F72585', // 8: Magenta
  '#4CC9F0', // 9: Sky Blue
  '#FEE440', // 10: Bright Yellow
  '#F3722C'  // 11: Tangerine
] as const;

/**
 * Returns distinct color for a given track index from palette, or fallback accent if single track
 */
export function getTrackColor(trackIndex: number, totalTracks = 0, fallbackAccent = '#1DB954'): string {
  if (totalTracks === 1) {
    return fallbackAccent;
  }
  return TRACK_COLORS[Math.abs(trackIndex) % TRACK_COLORS.length];
}
