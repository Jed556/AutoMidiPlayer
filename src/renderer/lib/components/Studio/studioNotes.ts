import type { MidiFile, RollNote } from '$shared/types/midi';
import { createNoteIndex } from '../../utils/noteIndex';
import { getTrackColor } from '../../utils/musicConstants';
import { getNoteTextColor } from '../../utils/noteAppearance';
import { projectNoteTiming, type NoteTimingSettings } from '../../utils/noteTiming';

export function indexSongTracks(song: MidiFile | null, accent?: string, displayPitch?: (note: RollNote) => number, timing?: NoteTimingSettings) {
  if (!song) return [];
  const grouped = new Map<number, RollNote[]>();
  for (const note of song.notes ?? []) {
    const index = note.trackIndex ?? (note.trackId ? note.trackId - 1 : 0);
    let notes = grouped.get(index);
    if (!notes) grouped.set(index, notes = []);
    notes.push(note);
  }
  const tracks = song.tracks.length ? song.tracks : [...grouped.keys()].map(index => ({
    index, notes: grouped.get(index), color: undefined, isMuted: false, muted: false,
  }));
  return tracks.map(track => {
    const color = track.color || getTrackColor(track.index, tracks.length, accent);
    return {
      index: track.index,
      color,
      labelColor: getNoteTextColor(color),
      muted: track.isMuted || track.muted,
      ...createNoteIndex((track.notes?.length ? track.notes : grouped.get(track.index) ?? []).map(note =>
        projectNoteTiming(displayPitch ? { ...note, rawNoteNumber: note.rawNoteNumber ?? note.noteNumber, pitch: displayPitch(note) } : note, timing))),
    };
  });
}

export type StudioRenderTrack = ReturnType<typeof indexSongTracks>[number];
