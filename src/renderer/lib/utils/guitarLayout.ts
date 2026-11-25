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
  const strum = (mode === 'chords' || mode === 'frets') && strings === 0;
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

const ROMAN_NUMERALS: Array<[number, string]> = [
  [10, 'X'],
  [9, 'IX'],
  [5, 'V'],
  [4, 'IV'],
  [1, 'I'],
];

/** Format a 1-based fret number as an uppercase Roman numeral (I–XXIV). */
export function fretRomanNumeral(fret: number): string {
  if (!Number.isInteger(fret) || fret <= 0) return '';
  let remaining = fret;
  let result = '';
  for (const [value, symbol] of ROMAN_NUMERALS) {
    while (remaining >= value) {
      result += symbol;
      remaining -= value;
    }
  }
  return result;
}

/** Return the shortest signed angular step in (-180, 180] from currentAngle to targetSlot. */
export function wheelAngleDelta(currentAngle: number, targetSlot: number, slotCount = 6): number {
  if (!Number.isFinite(currentAngle) || !Number.isInteger(targetSlot) || slotCount <= 0) return 0;
  const normalizedSlot = ((targetSlot % slotCount) + slotCount) % slotCount;
  const targetAngle = normalizedSlot * (360 / slotCount);
  const delta = ((((targetAngle - currentAngle) % 360) + 540) % 360) - 180;
  return delta === -180 ? 180 : delta;
}

export interface BacktrackNoteLike {
  noteNumber?: number;
  pitch?: number;
  startMs?: number;
  startTime?: number;
  lengthMs?: number;
  duration?: number;
  trackIndex?: number;
  trackId?: number;
}

/** Resolve a set of sounding MIDI pitches to a global wheel slot index (0-based across all pages). */
export function resolveGuitarSlotFromPitches(
  instrument: InstrumentConfig | null,
  pitches: Iterable<number>,
  fretMode: boolean,
  slotCount = 6,
): number | null {
  const notes = [...new Set(pitches)].filter(n => Number.isFinite(n)).sort((a, b) => a - b);
  if (!instrument || !notes.length || slotCount <= 0) return null;

  if (fretMode) {
    const guitar = instrument.guitar;
    if (!guitar) return null;
    const openSlot = Math.ceil(guitar.fretCount / slotCount) * slotCount;
    const voicing = guitarVoicing(instrument, notes);
    if (voicing.length > 0) {
      const fret = voicing[0].fret;
      return fret === 0 ? openSlot : fret - 1;
    }
    const leadVoicing = guitarVoicing(instrument, [notes[notes.length - 1]]);
    if (leadVoicing.length > 0) {
      const fret = leadVoicing[0].fret;
      return fret === 0 ? openSlot : fret - 1;
    }
    return null;
  }

  const chords = instrument.chords ?? [];
  if (!chords.length) return null;
  const exactName = guitarChord(instrument, notes);
  if (exactName) {
    const idx = chords.findIndex(c => c.name === exactName);
    if (idx >= 0) return idx;
  }

  if (instrument.pluckStrings) {
    const lead = notes[notes.length - 1];
    let bestSlot: number | null = null;
    let bestKept = 0;
    for (let i = 0; i < chords.length; i++) {
      const sp = chords[i].stringPitches;
      if (!sp || !sp.includes(lead)) continue;
      const kept = notes.filter(n => sp.includes(n)).length;
      if (kept > bestKept) {
        bestKept = kept;
        bestSlot = i;
      }
    }
    if (bestSlot !== null) return bestSlot;
  }

  const bassPc = ((notes[0] % 12) + 12) % 12;
  const leadPc = ((notes[notes.length - 1] % 12) + 12) % 12;
  const pitchClasses = new Set(notes.map(n => ((n % 12) + 12) % 12));
  let bestIdx: number | null = null;
  let bestScore = -1;
  for (let i = 0; i < chords.length; i++) {
    const chord = chords[i];
    if (!chord.name || !chord.pitchClasses?.length) continue;
    if (chord.bassPitchClass !== undefined && chord.bassPitchClass !== bassPc) continue;
    let covered = 0;
    for (const pc of pitchClasses) {
      if (chord.pitchClasses.includes(pc)) covered++;
    }
    if (covered === 0) continue;
    const hasBass = chord.pitchClasses[0] === bassPc ? 1 : 0;
    const hasLead = chord.pitchClasses.includes(leadPc) ? 1 : 0;
    const score = covered * 8 + hasBass * 3 + hasLead * 2;
    if (score > bestScore) {
      bestScore = score;
      bestIdx = i;
    }
  }
  return bestIdx;
}

/**
 * Backtrack from `currentTimeMs` through song notes (and any currently active pitches)
 * to determine the chord/fret wheel slot that should be selected in the ring at the playhead.
 */
export function backtrackGuitarSlot(options: {
  instrument: InstrumentConfig | null;
  fretMode: boolean;
  slotCount?: number;
  activePitches?: Iterable<number>;
  notes?: ReadonlyArray<BacktrackNoteLike>;
  currentTimeMs?: number;
  keyOffset?: number;
  chordToleranceMs?: number;
  mutedTracks?: ReadonlySet<number>;
}): number | null {
  const {
    instrument,
    fretMode,
    slotCount = 6,
    activePitches,
    notes,
    currentTimeMs = 0,
    keyOffset = 0,
    chordToleranceMs = 35,
    mutedTracks,
  } = options;
  if (!instrument || slotCount <= 0) return null;

  if (activePitches) {
    const activeSlot = resolveGuitarSlotFromPitches(instrument, activePitches, fretMode, slotCount);
    if (activeSlot !== null) return activeSlot;
  }

  if (!notes?.length) return null;

  const targetMs = currentTimeMs + 5;
  let lo = 0;
  let hi = notes.length;
  while (lo < hi) {
    const mid = (lo + hi) >>> 1;
    const start = notes[mid].startMs ?? (notes[mid].startTime ?? 0) * 1000;
    if (start <= targetMs) lo = mid + 1;
    else hi = mid;
  }

  let idx = lo - 1;
  let attempts = 0;
  while (idx >= 0 && attempts < 24) {
    const note = notes[idx];
    const track = note.trackIndex ?? note.trackId;
    if (track !== undefined && mutedTracks?.has(track)) {
      idx--;
      continue;
    }
    const clusterEndMs = note.startMs ?? (note.startTime ?? 0) * 1000;
    const clusterStartMs = clusterEndMs - Math.max(1, chordToleranceMs);
    const clusterPitches: number[] = [];
    let cursor = idx;
    while (cursor >= 0) {
      const candidate = notes[cursor];
      const start = candidate.startMs ?? (candidate.startTime ?? 0) * 1000;
      if (start < clusterStartMs) break;
      const candidateTrack = candidate.trackIndex ?? candidate.trackId;
      if (candidateTrack === undefined || !mutedTracks?.has(candidateTrack)) {
        const rawPitch = candidate.noteNumber ?? candidate.pitch;
        if (typeof rawPitch === 'number') {
          clusterPitches.push(rawPitch + keyOffset);
        }
      }
      cursor--;
    }
    if (clusterPitches.length > 0) {
      const slot = resolveGuitarSlotFromPitches(instrument, clusterPitches, fretMode, slotCount);
      if (slot !== null) return slot;
      attempts++;
    }
    idx = Math.min(idx - 1, cursor);
  }
  return null;
}
