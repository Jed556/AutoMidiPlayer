import type { InstrumentConfig } from '../../../shared/types/games';

export interface GuitarPosition { string: number; fret: number; pitch: number }

/** The game's fret selector is shared by all strings; preserve exact pitches. */
export function guitarVoicing(instrument: InstrumentConfig | null, pitches: Iterable<number>): GuitarPosition[] {
  const guitar = instrument?.guitar;
  const notes = [...new Set(pitches)].sort((a, b) => a - b);
  if (!guitar || !notes.length || notes.length > guitar.tuning.length) return [];
  for (let fret = 0; fret <= guitar.fretCount; fret++) {
    const used = new Set<number>();
    const positions: GuitarPosition[] = [];
    for (const pitch of notes) {
      const string = guitar.tuning.findIndex((open, index) => !used.has(index) && open + fret === pitch);
      if (string < 0) break;
      used.add(string);
      positions.push({ string, fret, pitch });
    }
    if (positions.length === notes.length) return positions;
  }
  return [];
}

export function guitarChord(instrument: InstrumentConfig | null, pitches: Iterable<number>): string | null {
  const notes = [...pitches].sort((a, b) => a - b);
  if (!notes.length) return null;
  const mask = notes.reduce((value, pitch) => value | (1 << (pitch % 12)), 0);
  return [...(instrument?.chords ?? [])].sort((a, b) => Number(b.bassPitchClass !== undefined) - Number(a.bassPitchClass !== undefined))
    .find(chord => chord.pitchClasses.reduce((value, pitch) => value | (1 << pitch), 0) === mask
      && (chord.bassPitchClass === undefined || chord.bassPitchClass === notes[0] % 12))?.name ?? null;
}
