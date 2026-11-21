import type { InstrumentConfig } from '../../../shared/types/games';

import type { ArrangementState } from '../../../shared/types/ipc';

export interface GuitarPosition { string: number; fret: number; pitch: number }

/** Project exactly the physical attack reported by the player onto the six strings. */
export function dispatchedGuitarPositions(instrument: InstrumentConfig, action: ArrangementState | null): GuitarPosition[] {
  const guitar = instrument.guitar;
  if (!guitar || action?.slot == null || !action.mode) return [];
  const { mode, slot, strings } = action;
  const voicing = instrument.chords?.[slot]?.stringPitches;
  const sharedFret = slot < guitar.fretCount ? slot + 1 : 0;
  const strum = mode === 'chords' && strings === 0;
  return guitar.tuning.flatMap((open, string) => {
    if (!strum && !(strings & (1 << string))) return [];
    const pitch = mode === 'frets' ? open + sharedFret : voicing?.[string];
    if (typeof pitch !== 'number' || pitch < open || pitch > open + guitar.fretCount) return [];
    return [{ string, fret: pitch - open, pitch }];
  });
}

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
