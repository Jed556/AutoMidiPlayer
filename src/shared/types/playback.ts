export type PlaybackStatus = 'Stopped' | 'Playing' | 'Paused';

export type LoopMode = 'Off' | 'RepeatOne' | 'RepeatAll';

export interface PedalState {
  isSustainHeld: boolean;
  isSostenutoHeld: boolean;
  isUnaCordaHeld: boolean;
}

export interface PlaybackControlsState {
  status: PlaybackStatus;
  currentTimeMs: number;
  durationMs: number;
  speed: number;
  volume: number; // 0 - 200 (overshoot up to 200%)
  isMuted: boolean;
  isListenMode: boolean; // output to speakers vs keystrokes
  playUnplayableOnIgnore: boolean;
  shuffle: boolean;
  loopMode: LoopMode;
  pedals: PedalState;
  activeNotes: number[]; // currently sounding MIDI note numbers
}
