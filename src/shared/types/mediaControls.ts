import type { PlaybackStatus } from './playback.js';

export type MediaControlAction =
  | { action: 'play' | 'pause' | 'toggle' | 'stop' | 'next' | 'previous' }
  | { action: 'seek'; seconds: number }
  | { action: 'seekBy'; seconds: number }
  | { action: 'volume'; volume: number };

export interface MediaControlsSnapshot {
  title: string;
  artist: string;
  album: string;
  durationSeconds: number;
  positionSeconds: number;
  status: PlaybackStatus;
  volume: number;
}
