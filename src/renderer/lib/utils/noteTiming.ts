import type { RollNote } from '$shared/types/midi';

export interface NoteTimingSettings {
  holdNotes: boolean;
  thresholdMs: number;
  tapMs: number;
  speed: number;
}

/** Durations on the timeline are song time; threshold and keypress delay are wall time. */
export function projectNoteTiming(note: RollNote, settings?: NoteTimingSettings): RollNote {
  if (!settings) return note;
  const sourceDuration = note.rawDuration ?? note.duration ?? note.lengthMs / 1000;
  const speed = Math.max(0.1, Math.min(5, settings.speed));
  // Match native playback: round wall-clock duration to whole milliseconds.
  const durationMs = Math.round(sourceDuration * 1000 / speed);
  const isTap = !settings.holdNotes || durationMs <= settings.thresholdMs;
  if (!isTap) return note;
  const tapMs = Math.max(0, Math.min(1000, settings.tapMs));
  return { ...note, rawDuration: sourceDuration, tapDurationMs: tapMs,
    duration: tapMs * speed / 1000, lengthMs: tapMs * speed };
}
