import type { KeyChangeMarker, RollNote, TransposeMode } from '$shared/types/midi';
import type { StudioPreferences } from '$shared/types/settings';
import type { NoteTimingSettings } from '../../utils/noteTiming';

export type StudioDisplayMode = StudioPreferences['displayMode'];

/** Explicit timing previews apply even when the synthesizer plays raw MIDI. */
export function getDisplayTiming(mode: StudioDisplayMode, timing: NoteTimingSettings, rawSynth: boolean) {
  return mode === 'NoteModifiers' || (mode !== 'SongDefault' && !rawSynth) ? timing : undefined;
}

interface DisplaySettings {
  mode: StudioDisplayMode;
  keyOffset: number;
  transpose: TransposeMode;
  markersEnabled: boolean;
  markers: readonly KeyChangeMarker[];
  playableNotes: readonly number[];
}

const MODES = [
  [0, 2, 4, 5, 7, 9, 11], [0, 2, 3, 5, 7, 9, 10],
  [0, 1, 3, 5, 7, 8, 10], [0, 2, 4, 6, 7, 9, 11],
  [0, 2, 4, 5, 7, 9, 10], [0, 2, 3, 5, 7, 8, 10],
  [0, 1, 3, 5, 6, 8, 10],
];
const rawPitch = (note: RollNote) => note.rawNoteNumber ?? note.noteNumber ?? note.pitch!;
const clampPitch = (pitch: number) => Math.max(0, Math.min(127, pitch));

/** Match legacy Smart's instrument scale, weighted around the folded target. */
function smartCandidates(playable: readonly number[], target: number): readonly number[] {
  const histogram = Array<number>(12).fill(0);
  for (const pitch of playable) {
    histogram[pitch % 12] += 1 / (1 + Math.abs(pitch - target));
  }
  let scale = MODES[0];
  let bestScore = -Infinity;
  for (let tonic = 0; tonic < 12; tonic++) {
    for (const mode of MODES) {
      const pitches = mode.map(interval => (tonic + interval) % 12);
      const score = pitches.reduce((sum, pitch) => sum + histogram[pitch], 0);
      if (score > bestScore) {
        bestScore = score;
        scale = pitches;
      }
    }
  }
  const candidates = playable.filter(pitch => scale.includes(pitch % 12));
  return candidates.length ? candidates : playable;
}

/** Match amp-core's pitch adaptation without changing source MIDI. */
export function createDisplayPitch(settings: DisplaySettings): (note: RollNote) => number {
  if (settings.mode === 'SongDefault' || settings.mode === 'NoteModifiers') return rawPitch;

  const playable = [...new Set(settings.playableNotes)].sort((a, b) => a - b);
  const playableSet = new Set(playable);
  const min = playable[0];
  const max = playable[playable.length - 1];
  const markers = settings.mode === 'Auto' && settings.markersEnabled
    ? [...settings.markers].sort((a, b) => a.timeMs - b.timeMs) : [];
  const transposeModes: TransposeMode[] = ['Ignore', 'Smart', 'Up', 'Down'];
  const cache = new Map<string, number>();

  return note => {
    const timeMs = note.startTime !== undefined ? note.startTime * 1000 : note.startMs;
    let lo = 0;
    let hi = markers.length;
    while (lo < hi) {
      const mid = (lo + hi) >>> 1;
      if (markers[mid].timeMs <= timeMs) lo = mid + 1;
      else hi = mid;
    }
    const marker = markers[lo - 1];
    const shifted = rawPitch(note) + (marker?.keyOffset ?? settings.keyOffset);
    const transpose = marker?.transpose != null
      ? transposeModes[marker.transpose] ?? settings.transpose : settings.transpose;
    const key = `${shifted}:${transpose}`;
    const cached = cache.get(key);
    if (cached !== undefined) return cached;

    let pitch = shifted;
    if (playable.length && transpose !== 'Ignore') {
      while (pitch < min) pitch += 12;
      while (pitch > max) pitch -= 12;
      if (pitch < min) pitch = Math.abs(pitch - min) <= Math.abs(pitch - max) ? min : max;
      if (transpose === 'Smart' && !playableSet.has(pitch)) {
        const candidates = smartCandidates(playable, pitch);
        let score = Infinity;
        let best = candidates[0];
        for (const candidate of candidates) {
          const distance = Math.abs(candidate - pitch) + Math.abs(Math.floor(candidate / 12) - Math.floor(pitch / 12)) * 0.15;
          if (distance < score || (Math.abs(distance - score) < 0.000001
            && Math.abs(candidate - pitch) < Math.abs(best - pitch))) {
            score = distance;
            best = candidate;
          }
        }
        pitch = best;
      }
    }
    // Keep unmapped notes visible for editing, as in the original Studio.
    pitch = clampPitch(pitch);
    cache.set(key, pitch);
    return pitch;
  };
}
