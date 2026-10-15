import type { GameInfo, InstrumentConfig, InstrumentLayoutStyle, KeyboardLayoutConfig, KeyMatrix } from '$shared/types/games.js';
import { instrumentNotes, normalizeKeyboardLayout, type RawKeyboardLayout } from '../../../shared/utils/instrumentLayout.js';
import type { KeyChangeMarker, MidiFile } from '$shared/types/midi.js';
import { formatSignedOffset, getKeyNoteName } from '../utils/musicConstants.js';

export interface RawSongRecord {
  id: string;
  baseKey?: number | null;
  key: number;
  path: string;
  fileHash?: string;
  title?: string;
  artist?: string;
  album?: string;
  albumId?: string;
  dateAdded?: string;
  transpose: number;
  speed?: number;
  bpm?: number;
  disabledTracks?: string;
  mergeNotes?: boolean;
  mergeMilliseconds?: number;
  holdNotes?: boolean;
  tapThresholdMs?: number;
  imagePath?: string;
  cachedDurationMs?: number;
  cachedNativeBpm?: number;
  autoChord?: boolean;
  chordDetectionMilliseconds?: number;
  keyChanges?: string;
  detectChordPads?: boolean;
  enableKeyChangeMarkers?: boolean;
  isMissing: boolean;
}

/** The payload emitted by Rust when a MIDI file has been parsed for playback. */
export interface RawLoadedMidiFile {
  id: string;
  title: string;
  artist: string;
  album: string;
  durationMs: number;
  durationSeconds?: number;
  bpm: number;
  tempo?: number;
  filePath: string;
  tracks?: Array<Record<string, unknown>>;
  notes?: Array<Record<string, unknown>>;
}

export interface RawLoadedBundle {
  config: {
    id: string;
    displayName: string;
    shortName?: string;
    processNames: string[];
    windowNames?: string[];
    icon?: string;
    defaultInstrument?: string;
    defaultLayout?: string;
  };
  iconPath?: string;
  instruments: Array<{
    id: string;
    name: string;
    category?: string;
    type?: string;
    layoutStyle?: InstrumentLayoutStyle;
    keys?: KeyMatrix<number>;
    notes?: number[];
    supportedLayouts?: string[];
    chordPads?: Array<{ name: string; pitchClasses: number[] }>;
    supportsPedals?: boolean;
    supportsChords?: boolean;
    subfolder?: string;
    filePath?: string;
  }>;
  folderPath: string;
  configPath?: string;
}

function toTransposeMode(value: number): MidiFile['transposeMode'] {
  switch (value) {
    case 0: return 'Ignore';
    case 2: return 'Up';
    case 3: return 'Down';
    default: return 'Smart';
  }
}

function parseKeyMarkers(keyChanges?: string): KeyChangeMarker[] {
  if (!keyChanges) return [];

  try {
    const parsed: unknown = JSON.parse(keyChanges);
    if (!Array.isArray(parsed)) return [];

    return parsed.map((marker, index) => {
      const record = marker as {
        TimeMs?: number;
        KeyOffset?: number;
        Label?: string;
        Transpose?: number;
      };
      const keyOffset = record.KeyOffset ?? 0;
      const noteDisplay = record.Label || getKeyNoteName(keyOffset);

      return {
        id: `km-${index}-${record.TimeMs ?? 0}`,
        timeMs: record.TimeMs ?? 0,
        time: (record.TimeMs ?? 0) / 1000,
        key: noteDisplay,
        keyOffset,
        noteDisplay,
        offsetDisplay: formatSignedOffset(keyOffset),
        transpose: record.Transpose,
        label: record.Label,
      };
    });
  } catch {
    return [];
  }
}

export function songRecordToMidiFile(record: RawSongRecord): MidiFile {
  const durationMs = record.cachedDurationMs ?? 0;
  const bpm = record.bpm ?? record.cachedNativeBpm ?? 120;
  const fileName = record.path.split(/[/\\]/).pop()?.replace(/\.midi?$/i, '') || 'Unknown Song';
  const keyMarkers = parseKeyMarkers(record.keyChanges);

  return {
    id: record.id,
    title: record.title || fileName,
    artist: record.artist || 'Unknown Artist',
    album: record.album || 'Unknown Album',
    durationMs,
    durationSeconds: durationMs / 1000,
    bpm,
    nativeBpm: record.cachedNativeBpm ?? 120,
    bpmOverride: record.bpm ?? null,
    tempo: bpm,
    baseKeyOffset: record.baseKey ?? 0,
    baseKey: record.baseKey ?? null,
    keyOffset: record.key ?? 0,
    transposeMode: toTransposeMode(record.transpose),
    speed: record.speed ?? 1,
    mergeNotes: record.mergeNotes ?? false,
    mergeToleranceMs: record.mergeMilliseconds ?? 100,
    holdNotes: record.holdNotes ?? false,
    tapThresholdMs: record.tapThresholdMs ?? 0,
    disabledTracks: record.disabledTracks ?? null,
    autoChord: record.autoChord ?? false,
    enableKeyChangeMarkers: record.enableKeyChangeMarkers ?? true,
    dateAdded: record.dateAdded || new Date().toISOString(),
    filePath: record.path,
    isMissing: record.isMissing,
    isFavorite: false,
    tracks: [],
    notes: [],
    keyMarkers,
    keyChanges: keyMarkers.map(({ id, time, key }) => ({ id, time: time ?? 0, key: key ?? '' })),
  };
}

/**
 * Makes native playback payloads conform to the renderer model without
 * inventing notes, tracks, or metadata. It is deliberately a normalizer, not
 * a mock-data enricher.
 */
export function loadedMidiToMidiFile(raw: RawLoadedMidiFile | MidiFile): MidiFile {
  const source = raw as unknown as Record<string, unknown>;
  const durationMs = Number(source.durationMs ?? 0);
  const durationSeconds = Number(source.durationSeconds ?? durationMs / 1000);
  const bpm = Number(source.bpm ?? source.tempo ?? 120);
  const rawNotes = Array.isArray(source.notes) ? source.notes as Array<Record<string, unknown>> : [];
  const notes = rawNotes.map((note, index) => {
    const noteNumber = Number(note.noteNumber ?? note.pitch ?? 60);
    const startMs = Number(note.startMs ?? (Number(note.startTime ?? 0) * 1000));
    const lengthMs = Number(note.lengthMs ?? (Number(note.duration ?? 0) * 1000));
    const trackIndex = Number(note.trackIndex ?? 0);

    return {
      id: String(note.id ?? `note-${index}`),
      noteNumber,
      pitch: Number(note.pitch ?? noteNumber),
      rawNoteNumber: Number(note.rawNoteNumber ?? noteNumber),
      startMs,
      startTime: Number(note.startTime ?? startMs / 1000),
      lengthMs,
      duration: Number(note.duration ?? lengthMs / 1000),
      trackIndex,
      trackId: Number(note.trackId ?? trackIndex + 1),
      velocity: Number(note.velocity ?? 80),
    };
  });
  const rawTracks = Array.isArray(source.tracks) ? source.tracks as Array<Record<string, unknown>> : [];
  const tracks = rawTracks.map((track, index) => {
    const trackIndex = Number(track.index ?? index);
    const trackNotes = notes.filter((note) => note.trackIndex === trackIndex);
    const noteCount = Number(track.noteCount ?? trackNotes.length);
    const timeStartMs = Number(track.timeStartMs ?? track.startMs ?? 0);
    const timeDurationMs = Number(track.timeDurationMs ?? track.durationMs ?? durationMs);
    const minPitch = Number(track.minPitch ?? track.pitchMin ?? 0);
    const maxPitch = Number(track.maxPitch ?? track.pitchMax ?? 0);

    return {
      id: Number(track.id ?? trackIndex + 1),
      index: trackIndex,
      name: String(track.name ?? `Track ${index + 1}`),
      instrument: String(track.instrument ?? track.instrumentName ?? 'Unknown'),
      instrumentName: String(track.instrumentName ?? track.instrument ?? 'Unknown'),
      channel: Number(track.channel ?? 0),
      noteCount,
      playableNotes: Number(track.playableNotes ?? noteCount),
      playablePercentage: Number(track.playablePercentage ?? (noteCount > 0 ? 100 : 0)),
      blackKeyRatio: Number(track.blackKeyRatio ?? 0),
      avgDuration: Number(track.avgDuration ?? track.averageDurationMs ?? track.avgDurationMs ?? 0),
      avgDurationMs: Number(track.avgDurationMs ?? track.averageDurationMs ?? track.avgDuration ?? 0),
      frequentRatio: Number(track.frequentRatio ?? track.frequentNotesRatio ?? 0),
      frequentNotesRatio: Number(track.frequentNotesRatio ?? track.frequentRatio ?? 0),
      timeStartMs,
      timeDurationMs,
      timeEndMs: Number(track.timeEndMs ?? timeStartMs + timeDurationMs),
      minPitch,
      pitchMin: minPitch,
      maxPitch,
      pitchMax: maxPitch,
      avgPitch: Number(track.avgPitch ?? 0),
      muted: Boolean(track.muted ?? track.isMuted),
      isMuted: Boolean(track.isMuted ?? track.muted),
      color: typeof track.color === 'string' ? track.color : undefined,
      isActive: Boolean(track.isActive),
      isExpanded: Boolean(track.isExpanded),
      notes: trackNotes,
    };
  });
  const rawMarkers = Array.isArray(source.keyMarkers) ? source.keyMarkers as Array<Record<string, unknown>> : [];
  const keyMarkers = rawMarkers.map((marker, index) => {
    const timeMs = Number(marker.timeMs ?? (Number(marker.time ?? 0) * 1000));
    const keyOffset = Number(marker.keyOffset ?? 0);
    const key = String(marker.key ?? marker.noteDisplay ?? 'C');

    return {
      id: String(marker.id ?? `key-marker-${index}`),
      timeMs,
      time: Number(marker.time ?? timeMs / 1000),
      key,
      keyOffset,
      noteDisplay: String(marker.noteDisplay ?? key),
      offsetDisplay: String(marker.offsetDisplay ?? formatSignedOffset(keyOffset)),
      transpose: typeof marker.transpose === 'number' ? marker.transpose : null,
      label: typeof marker.label === 'string' ? marker.label : undefined,
    };
  });

  return {
    id: String(source.id ?? ''),
    title: String(source.title ?? 'Unknown Song'),
    artist: String(source.artist ?? 'Unknown Artist'),
    album: String(source.album ?? 'Unknown Album'),
    durationMs,
    durationSeconds,
    bpm,
    tempo: Number(source.tempo ?? bpm),
    nativeBpm: Number(source.nativeBpm ?? bpm),
    bpmOverride: typeof source.bpmOverride === 'number' ? source.bpmOverride : null,
    timeSignature: typeof source.timeSignature === 'string' ? source.timeSignature : undefined,
    overallPlayablePercentage: typeof source.overallPlayablePercentage === 'number'
      ? source.overallPlayablePercentage
      : undefined,
    baseKeyOffset: Number(source.baseKeyOffset ?? source.baseKey ?? 0),
    baseKey: source.baseKey === null ? null : Number(source.baseKey ?? source.baseKeyOffset ?? 0),
    keyOffset: Number(source.keyOffset ?? 0),
    transposeMode: source.transposeMode === 'Ignore' || source.transposeMode === 'Up' || source.transposeMode === 'Down'
      ? source.transposeMode
      : 'Smart',
    speed: Number(source.speed ?? 1),
    mergeNotes: Boolean(source.mergeNotes),
    mergeToleranceMs: Number(source.mergeToleranceMs ?? 25),
    holdNotes: Boolean(source.holdNotes),
    tapThresholdMs: Number(source.tapThresholdMs ?? 0),
    disabledTracks: typeof source.disabledTracks === 'string' ? source.disabledTracks : null,
    autoChord: Boolean(source.autoChord),
    enableKeyChangeMarkers: source.enableKeyChangeMarkers !== false,
    dateAdded: String(source.dateAdded ?? new Date().toISOString()),
    filePath: String(source.filePath ?? ''),
    gameId: typeof source.gameId === 'string' ? source.gameId : undefined,
    coverArtUrl: typeof source.coverArtUrl === 'string' ? source.coverArtUrl : undefined,
    tags: Array.isArray(source.tags) ? source.tags.filter((tag): tag is string => typeof tag === 'string') : [],
    isFavorite: Boolean(source.isFavorite),
    tracks,
    notes,
    keyMarkers,
    keyChanges: keyMarkers.map(({ id, time, key }) => ({ id, time: time ?? 0, key: key ?? '' })),
  };
}

export function getBundledGameImage(gameId?: string, shortName?: string): string {
  const normalized = `${gameId || ''} ${shortName || ''}`.toLowerCase().replace(/[^a-z0-9]/g, '');
  if (normalized.includes('genshin')) return 'Genshin_Impact.png';
  if (normalized.includes('sky')) return 'Sky.png';
  if (normalized.includes('wuwa') || normalized.includes('wuthering')) return 'Wuthering_Waves.png';
  if (normalized.includes('roblox')) return 'Roblox.png';
  if (normalized.includes('heartopia')) return 'Heartopia.png';
  if (normalized.includes('corekeeper') || normalized.includes('core')) return 'Core_Keeper.png';
  if (normalized.includes('hpma') || normalized.includes('potter')) return 'HPMA.png';
  if (normalized.includes('nte') || normalized.includes('neverness')) return 'NTE.png';
  if (normalized.includes('wwm') || normalized.includes('winds')) return 'WWM.png';
  if (normalized.includes('bpsr') || normalized.includes('blueprotocol') || normalized.includes('protocol')) return 'BPSR.png';
  return 'Genshin_Impact.png';
}

export function loadedBundleToGameInfo(
  bundle: RawLoadedBundle,
  allLayouts: Record<string, RawKeyboardLayout>,
  convertFileSrc?: (filePath: string) => string,
): GameInfo {
  const layoutSet = new Map<string, KeyboardLayoutConfig>();
  const normalizedLayouts = Object.fromEntries(Object.entries(allLayouts).map(([id, layout]) => [id, normalizeKeyboardLayout(layout)]));
  const layouts = Object.values(normalizedLayouts);

  for (const instrument of bundle.instruments || []) {
    for (const layoutId of instrument.supportedLayouts || []) {
      const layout = normalizedLayouts[layoutId.toLowerCase()] || layouts.find((candidate) =>
        candidate.id.toLowerCase() === layoutId.toLowerCase() ||
        candidate.name.toLowerCase() === layoutId.toLowerCase(),
      );
      if (layout) layoutSet.set(layout.id, layout);
    }
  }

  const instruments: InstrumentConfig[] = (bundle.instruments || []).map((instrument) => ({
    id: instrument.id,
    name: instrument.name,
    category: instrument.category || instrument.subfolder || 'General',
    type: instrument.type || 'diatonic21',
    layoutStyle: instrument.layoutStyle,
    keys: instrument.keys,
    notes: instrumentNotes(instrument),
    supportedLayouts: instrument.supportedLayouts || [],
    chordPads: instrument.chordPads || [],
    supportsPedals: instrument.supportsPedals ?? false,
    supportsChords: instrument.supportsChords ?? Boolean(instrument.chordPads?.length),
    supportsSustain: instrument.supportsPedals ?? false,
    subfolder: instrument.subfolder,
    filePath: instrument.filePath,
    octaves: 3,
    keyCount: instrumentNotes(instrument).length,
    minMidiNote: instrumentNotes(instrument).length ? Math.min(...instrumentNotes(instrument)) : 48,
    maxMidiNote: instrumentNotes(instrument).length ? Math.max(...instrumentNotes(instrument)) : 83,
    noteMap: instrumentNotes(instrument).map(String),
  }));

  return {
    id: bundle.config.id,
    displayName: bundle.config.displayName,
    shortName: bundle.config.shortName || bundle.config.displayName,
    name: bundle.config.displayName,
    iconUrl: bundle.iconPath && convertFileSrc ? convertFileSrc(bundle.iconPath) : undefined,
    imageFileName: getBundledGameImage(bundle.config.id, bundle.config.shortName),
    processNames: bundle.config.processNames || [],
    windowNames: bundle.config.windowNames || [],
    defaultInstrument: bundle.config.defaultInstrument,
    defaultLayout: bundle.config.defaultLayout,
    instruments,
    layouts: [...layoutSet.values()],
    folderPath: bundle.folderPath,
    filePath: bundle.configPath,
    isSelected: false,
    isRunning: false,
  };
}
