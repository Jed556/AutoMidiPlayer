import type { MidiFile, RollNote } from '../../../shared/types/midi';
import { createNoteIndex } from './noteIndex.ts';
import { projectNoteTiming, type NoteTimingSettings } from './noteTiming.ts';

export const DEFAULT_SHEET_DELIMITER = '·';
// Each delimiter represents a sixteenth note before shortening.
const STEPS_PER_BEAT = 4;

interface SheetOptions {
  delimiter: string;
  bars: number;
  beats: number;
  shorten: number;
  pitchToKey: ReadonlyMap<number, string>;
  displayPitch: (note: RollNote) => number;
  layoutName: string;
  timing?: NoteTimingSettings;
  rawPlayback?: boolean;
}

const positive = (value: number | undefined, fallback: number) =>
  value !== undefined && Number.isFinite(value) && value > 0 ? value : fallback;

export interface SheetPart {
  text: string;
  delimiter: boolean;
  notation?: boolean;
  presses?: SheetPress[];
}

interface SheetPress {
  startMs: number;
  endMs: number;
  pitch: number;
  trackIndex: number;
}

/** Retain delimiter identity so rendering can style rests without coloring note keys or metadata. */
export function generatePianoSheetParts(song: MidiFile | null, options: SheetOptions): SheetPart[] {
  if (!song) return [];
  const nativeBpm = positive(song.nativeBpm, positive(song.bpm, 120));
  const bpm = positive(song.bpm, nativeBpm);
  const duration = Math.max(0, song.durationSeconds ?? song.durationMs / 1000);
  const header = [
    `// ${song.title} - ${song.artist}`,
    `// BPM: ${Math.round(bpm)} | Time: ${Math.floor(duration / 60)}:${Math.floor(duration % 60).toString().padStart(2, '0')}`,
    `// Layout: ${options.layoutName}`,
    '',
  ];
  const parts: SheetPart[] = [{ text: header.join('\n'), delimiter: false }];
  const beatsPerLine = Math.max(1, Math.trunc(positive(options.bars, 1)))
    * Math.max(1, Math.trunc(positive(options.beats, 1)));
  const shorten = Math.max(1, Math.trunc(positive(options.shorten, 1)));
  const delimiter = options.delimiter;
  const muted = new Set(song.tracks.filter(track => track.muted || track.isMuted).map(track => track.index));
  // Track-local notes are an alias of song.notes in native payloads; read one source only.
  const source = song.notes.length ? song.notes : song.tracks.flatMap(track =>
    (track.notes ?? []).map(note => ({ ...note, trackIndex: track.index })));
  const events = source.flatMap(note => {
    const track = note.trackIndex ?? (note.trackId !== undefined ? note.trackId - 1 : 0);
    if (muted.has(track)) return [];
    const key = options.pitchToKey.get(options.displayPitch(note));
    if (!key) return [];
    // Native beat positions follow the complete tempo map. Older payloads use native BPM.
    const beat = note.startBeat ?? note.startMs * nativeBpm / 60_000;
    if (!Number.isFinite(beat) || beat < 0) return [];
    const timed = projectNoteTiming(note, options.timing);
    const press: SheetPress = {
      startMs: note.startMs,
      endMs: note.startMs + Math.max(0, timed.lengthMs),
      pitch: options.rawPlayback ? note.rawNoteNumber ?? note.noteNumber : options.displayPitch(note),
      trackIndex: track,
    };
    return [{ beat, key, press }];
  }).sort((a, b) => a.beat - b.beat);

  let lineIndex = 0;
  let previousBeat = 0;
  const append = (text: string, isDelimiter = false, notation = false, presses?: SheetPress[]) => {
    if (text) parts.push({ text, delimiter: isDelimiter, notation, presses });
  };
  const appendKey = (key: string, presses: SheetPress[]) => {
    // A leading caret modifies the following key; a standalone caret is a key itself.
    const modifier = key.match(/^(\^+)(.+)$/);
    if (modifier) {
      append(modifier[1], false, true, presses);
      append(modifier[2], false, false, presses);
    } else {
      append(key, false, false, presses);
    }
  };
  const spacing = (beats: number) => append(
    delimiter.repeat(Math.max(0, Math.floor(beats * STEPS_PER_BEAT / shorten + 1e-6))), true);
  // The header already ends in one newline; add the blank line only when notes exist.
  if (events.length) append('\n');
  for (let i = 0; i < events.length;) {
    const beat = events[i].beat;
    const keys = new Map<string, SheetPress[]>();
    do {
      const event = events[i++];
      const presses = keys.get(event.key) ?? [];
      presses.push(event.press);
      keys.set(event.key, presses);
    } while (i < events.length && Math.abs(events[i].beat - beat) < 1e-6);

    const nextLine = Math.floor((beat + 1e-6) / beatsPerLine);
    while (lineIndex < nextLine) {
      const boundary = (lineIndex + 1) * beatsPerLine;
      spacing(boundary - previousBeat);
      append('\n');
      previousBeat = boundary;
      lineIndex++;
    }
    spacing(beat - previousBeat);
    const chordPresses = [...keys.values()].flat();
    if (keys.size > 1) append('[', false, true, chordPresses);
    keys.forEach((presses, key) => appendKey(key, presses));
    if (keys.size > 1) append(']', false, true, chordPresses);
    previousBeat = beat;
  }
  return parts;
}

/** Plain text used for copying and exporting the sheet. */
export function generatePianoSheet(song: MidiFile | null, options: SheetOptions): string {
  return generatePianoSheetParts(song, options).map(part => part.text).join('');
}

/** Index once per sheet; playback ticks query only note intervals near the playhead. */
export function createSheetPlaybackIndex(parts: readonly SheetPart[]) {
  const owners = new Map<SheetPress, number[]>();
  parts.forEach((part, index) => part.presses?.forEach(press => {
    const indices = owners.get(press) ?? [];
    indices.push(index);
    owners.set(press, indices);
  }));
  const presses = [...owners.keys()];
  const index = createNoteIndex(presses.map((press, id) => ({
    id: String(id), noteNumber: press.pitch, startMs: press.startMs,
    lengthMs: press.endMs - press.startMs,
  })));
  return {
    startedParts(fromMs: number, toMs: number): Set<number> {
      const started = new Set<number>();
      if (!Number.isFinite(fromMs) || !Number.isFinite(toMs) || toMs <= fromMs) return started;
      for (const note of index.between(fromMs / 1000, toMs / 1000)) {
        const press = presses[Number(note.id)];
        if (press.startMs <= fromMs || press.startMs > toMs) continue;
        for (const owner of owners.get(press)!) started.add(owner);
      }
      return started;
    },
    activeParts(timeMs: number, activeNotes: readonly { pitch: number; trackId: number }[], playing: boolean): Set<number> {
      const active = new Set<number>();
      if (!playing || !Number.isFinite(timeMs) || !activeNotes.length) return active;
      const held = new Set(activeNotes.map(note => `${note.trackId}:${note.pitch}`));
      for (const note of index.between(timeMs / 1000, timeMs / 1000)) {
        const press = presses[Number(note.id)];
        if (timeMs >= press.endMs || !held.has(`${press.trackIndex}:${press.pitch}`)) continue;
        for (const owner of owners.get(press)!) active.add(owner);
      }
      return active;
    },
  };
}
