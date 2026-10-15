export type TransposeMode = 'Ignore' | 'Up' | 'Down' | 'Smart';

export type NoteDisplayMode = 'MidiPitch' | 'GameKey' | 'Degree';

export interface RollNote {
  id: string;
  noteNumber: number; // 0 - 127
  pitch?: number; // alias for noteNumber
  rawNoteNumber?: number;
  startMs: number;
  startTime?: number; // startMs / 1000
  lengthMs: number;
  duration?: number; // lengthMs / 1000
  /** Studio timing projection retains the source length for hover details. */
  rawDuration?: number;
  tapDurationMs?: number;
  trackIndex?: number;
  trackId?: number;
  velocity?: number;
}

export interface KeyChangeMarker {
  id: string;
  timeMs: number;
  time?: number; // timeMs / 1000
  key?: string; // e.g. "C", "D#", "F"
  keyOffset: number; // -12 to +12
  noteDisplay: string; // e.g. "C", "D#", "F"
  offsetDisplay: string; // e.g. "0", "+2", "-5"
  /** Optional native marker metadata retained when round-tripping song settings. */
  transpose?: number | null;
  label?: string;
}

export interface MidiTrack {
  id?: number;
  index: number;
  name: string;
  instrument?: string;
  instrumentName: string;
  channel: number;
  noteCount: number;
  playableNotes: number;
  playablePercentage?: number;
  blackKeyRatio: number; // percentage, 0 - 100 (native parser units)
  avgDuration?: number;
  avgDurationMs: number;
  frequentRatio?: number; // percentage, 0 - 100; alias for frequentNotesRatio
  frequentNotesRatio: number; // percentage, 0 - 100 (native parser units)
  timeStartMs: number;
  timeDurationMs: number;
  timeEndMs: number;
  minPitch: number;
  pitchMin?: number;
  maxPitch: number;
  pitchMax?: number;
  avgPitch: number;
  muted?: boolean;
  isMuted: boolean;
  color?: string;
  isActive: boolean; // glowing when note plays
  isExpanded?: boolean;
  notes?: RollNote[];
}

export interface MidiFile {
  isMissing?: boolean;
  id: string;
  title: string;
  artist: string;
  album: string;
  durationMs: number;
  durationSeconds?: number;
  bpm: number;
  nativeBpm?: number;
  bpmOverride?: number | null;
  tempo?: number;
  timeSignature?: string;
  overallPlayablePercentage?: number;
  baseKeyOffset: number;
  baseKey?: number | null;
  keyOffset: number;
  transposeMode: TransposeMode;
  speed: number;
  mergeNotes: boolean;
  mergeToleranceMs: number;
  holdNotes: boolean;
  tapThresholdMs: number;
  disabledTracks?: string | null;
  autoChord?: boolean;
  enableKeyChangeMarkers?: boolean;
  dateAdded: string;
  filePath: string;
  gameId?: string;
  coverArtUrl?: string;
  tags?: string[];
  isFavorite?: boolean;
  tracks: MidiTrack[];
  notes: RollNote[];
  keyMarkers: KeyChangeMarker[];
  keyChanges?: { id: string; time: number; key: string }[];
}
