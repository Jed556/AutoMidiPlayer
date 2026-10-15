import type { InstrumentConfig } from '../../../shared/types/games.js';

export interface PianoKey {
  pitch: number;
  name: string;
  isC: boolean;
}

export interface PianoSlot {
  white?: PianoKey;
  sharp?: PianoKey;
}

const noteNames = ['C', 'C#', 'D', 'D#', 'E', 'F', 'F#', 'G', 'G#', 'A', 'A#', 'B'];
const blackNotes = new Set([1, 3, 6, 8, 10]);

export function noteName(pitch: number): string {
  return `${noteNames[pitch % 12]}${Math.floor(pitch / 12) - 1}`;
}

/** Keep the exact playable notes, including accidentals whose natural key is absent. */
export function createPianoSlots(notes: readonly number[]): PianoSlot[] {
  const slots = new Map<number, PianoSlot>();
  const pitches = [...new Set(notes)].filter(pitch => Number.isInteger(pitch) && pitch >= 0 && pitch <= 127)
    .sort((a, b) => a - b);
  for (const pitch of pitches) {
    const black = blackNotes.has(pitch % 12);
    const anchor = black ? pitch - 1 : pitch;
    const slot = slots.get(anchor) ?? {};
    slot[black ? 'sharp' : 'white'] = { pitch, name: noteName(pitch), isC: pitch % 12 === 0 };
    slots.set(anchor, slot);
  }
  return [...slots.values()];
}

function splitPianoRows(slots: readonly PianoSlot[], capacity: number): PianoSlot[][] {
  const rows: PianoSlot[][] = [];
  let start = 0;
  while (start < slots.length) {
    let end = Math.min(start + capacity, slots.length);
    if (end < slots.length) {
      for (let boundary = end; boundary > start; boundary--) {
        if (slots[boundary].white?.isC) {
          end = boundary;
          break;
        }
      }
    }
    rows.push(slots.slice(start, end));
    start = end;
  }
  return rows;
}

/** Prefer readable key widths, but avoid extra rows that force the whole stage to shrink. */
export function createPianoRows(
  slots: readonly PianoSlot[],
  availableWidth: number,
  fit?: { height: number; stageWidth: number },
): PianoSlot[][] {
  // Match the strip's border and horizontal padding; allow a complete octave on narrow stages.
  const capacity = Math.max(7, Math.floor((availableWidth - 26) / 26));
  let best = splitPianoRows(slots, capacity);
  if (!fit || best.length <= 1) return best;
  const { height: availableHeight, stageWidth } = fit;

  function visibleKeyWidth(rows: PianoSlot[][]): number {
    // InstrumentView: 24px header, 116px strips, 10px between rows.
    const height = 24 + rows.length * 116 + (rows.length - 1) * 10;
    const scale = Math.min(1, availableWidth / stageWidth, availableHeight / height);
    const columns = Math.max(...rows.map(row => row.length));
    return (stageWidth - 26) / (columns + 0.28) * scale;
  }

  let bestWidth = visibleKeyWidth(best);
  for (let candidateCapacity = capacity + 1; candidateCapacity <= slots.length; candidateCapacity++) {
    const rows = splitPianoRows(slots, candidateCapacity);
    const keyWidth = visibleKeyWidth(rows);
    if (keyWidth > bestWidth + 0.01) {
      best = rows;
      bestWidth = keyWidth;
    }
  }
  return best;
}

/** Piano instruments already use the piano strip; chromatic grid instruments still have a game view. */
export function hasInGameInstrumentLayout(instrument: InstrumentConfig | null): boolean {
  if (instrument?.layoutStyle) return instrument.layoutStyle !== 'none';
  return !!instrument && !/^piano\d*$/i.test(instrument.type ?? '');
}

export function pianoRange(notes: readonly number[]): string {
  if (!notes.length) return '';
  const low = Math.min(...notes);
  const high = Math.max(...notes);
  return low === high ? noteName(low) : `${noteName(low)} – ${noteName(high)}`;
}
