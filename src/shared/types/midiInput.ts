export interface MidiInputPort { id: string; name: string }

export interface MidiInputState {
  ports: MidiInputPort[];
  selectedPortId: string | null;
  connected: boolean;
  active: boolean;
  starting: boolean;
  listenMode: boolean;
  outputAvailable: boolean;
  requiresSession: boolean;
  status: 'idle' | 'permission' | 'listening' | 'playing' | 'suspended' | 'error';
  pitches: number[];
  error: string | null;
}

export interface MidiInputApi {
  getState(): Promise<MidiInputState>;
  refreshPorts(): Promise<MidiInputState>;
  selectPort(portId: string | null): Promise<MidiInputState>;
  start(): Promise<MidiInputState>;
  stop(): Promise<MidiInputState>;
  onStateChanged(callback: (state: MidiInputState) => void): () => void;
}
