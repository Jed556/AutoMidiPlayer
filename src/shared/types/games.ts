export interface ChordPadDefinition {
  name: string;
  pitchClasses: number[];
}

export type InstrumentLayoutStyle = 'sky' | 'genshin' | 'heartopia' | 'drums' | 'none';
export type KeyMatrix<T> = Array<Array<T | null>>;

export interface InstrumentConfig {
  id: string;
  name: string;
  category?: string;
  type?: string;
  layoutStyle?: InstrumentLayoutStyle;
  keys?: KeyMatrix<number>;
  notes?: number[];
  supportedLayouts?: string[];
  chordPads?: ChordPadDefinition[];
  supportsChords?: boolean;
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
}

export interface ConfigIssue {
  path: string;
  message: string;
}

export interface GameConfigState {
  games: GameInfo[];
  issues: ConfigIssue[];
}

export interface GameInfo extends GameDefinition {
  isSelected: boolean;
  isRunning: boolean;
  isFocused?: boolean;
  pid?: number;
  customExecutablePath?: string;
}
