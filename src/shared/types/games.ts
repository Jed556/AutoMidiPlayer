export interface ChordDefinition {
  name: string;
  pitchClasses: number[];
  bassPitchClass?: number;
  stringPitches?: Array<number | null | 'muted'>;
}

export type InstrumentLayoutStyle = 'sky' | 'genshin' | 'heartopia' | 'drums' | 'guitar' | 'none';
export type InstrumentInputMode = 'keyboard' | 'chords' | 'frets' | 'mixed';
export type KeyMatrix<T> = Array<Array<T | null>>;

export interface InstrumentConfig {
  id: string;
  name: string;
  category?: string;
  type?: string;
  layoutStyle?: InstrumentLayoutStyle;
  keys?: KeyMatrix<number>;
  notes?: number[];
  keyboardLayouts?: string[];
  chordLayouts?: string[];
  fretLayouts?: string[];
  guitar?: { tuning: number[]; fretCount: number };
  chords?: ChordDefinition[];
  chordMode?: 'pads' | 'wheel';
  supportsChords?: boolean;
  pluckStrings?: boolean;
  pagedChords?: boolean;
  pagedFrets?: boolean;
  supportsPedals?: boolean;
  supportsSustain?: boolean;
  subfolder?: string;
  filePath?: string;
  octaves?: number;
  keyCount?: number;
  minMidiNote?: number;
  maxMidiNote?: number;
  noteMap?: string[]; // key names or notes
}

export interface KeyboardLayoutConfig {
  id: string;
  name: string;
  keys: string[];
  keyRows?: KeyMatrix<string>;
  filePath?: string;
  chordKeys?: string[];
  fretboard?: {
    stringKeys: string[];
    wheelLayout: string;
    modeKey: string;
    pageKeys?: [string, string];
  };
  chordWheel?: {
    positions: Array<[number, number]>;
    strumPosition: [number, number];
    moveDelayMs: number;
    selectHoldMs: number;
    afterSelectMs: number;
    pageCount: number;
    pageKeys: [string, string];
    pageHoldMs: number;
    pageWaitMs: number;
  };
  sustainKey?: string | null;
  sostenutoKey?: string | null;
  unaCordaKey?: string | null;
}

export interface GameDefinition {
  id: string;
  displayName: string;
  shortName?: string;
  name?: string;
  iconUrl?: string;
  defaultLayout?: string;
  defaultInstrument?: string;
  imageFileName?: string;
  processNames: string[];
  windowNames?: string[];
  instruments: InstrumentConfig[];
  layouts: KeyboardLayoutConfig[];
  folderPath?: string;
  filePath?: string;
  isMissing?: boolean;
}

export interface GameStatus {
  id: string;
  isRunning: boolean;
  isFocused: boolean;
  pid?: number | null;
  requiresManualFocus?: boolean;
}

export interface ConfigIssue {
  path: string;
  message: string;
}

export interface GameConfigState {
  games: GameInfo[];
  issues: ConfigIssue[];
}

export interface DefaultConfigConflict {
  id: string;
  path: string;
  kind: 'layout' | 'instrument' | 'game';
  name: string;
  currentJson: string;
  bundledJson: string;
  token: string;
}

export interface DefaultConfigUpdates {
  addedFiles: string[];
  conflicts: DefaultConfigConflict[];
  duplicates: DuplicateConfig[];
  issues: ConfigIssue[];
  backupDirectory: string | null;
}

export interface DuplicateConfigFile {
  path: string;
  json: string;
  modifiedMs: number;
}

export interface DuplicateConfig {
  id: string;
  configId: string;
  kind: 'layout' | 'instrument' | 'game';
  keep: DuplicateConfigFile;
  olderFiles: DuplicateConfigFile[];
  token: string;
}

export interface DefaultConfigChoice {
  id: string;
  token: string;
  action: 'keep' | 'replace';
}

export interface GameInfo extends GameDefinition {
  isSelected: boolean;
  isRunning: boolean;
  isFocused?: boolean;
  pid?: number;
  requiresManualFocus?: boolean;
  customExecutablePath?: string;
}
