import type { createSheetPlaybackIndex } from './pianoSheet';

export const MIN_SHEET_GLOW_MS = 120;
export const SHEET_GLOW_RELEASE_MS = 180;

export interface SheetGlowPulse {
  from: number;
  at: number;
  attackMs: number;
  releaseAt: number | null;
}

export function sheetGlowValue(pulse: SheetGlowPulse, now: number, reducedMotion = false): number {
  if (pulse.releaseAt !== null && now >= pulse.releaseAt) {
    if (reducedMotion) return 0;
    return (1 - Math.min(1, (now - pulse.releaseAt) / SHEET_GLOW_RELEASE_MS)) ** 3;
  }
  if (reducedMotion) return 1;
  const progress = Math.min(1, Math.max(0, (now - pulse.at) / pulse.attackMs));
  return pulse.from + (1 - pulse.from) * (1 - (1 - progress) ** 3);
}

export function startSheetGlow(now: number, attackMs: number, from = 0): SheetGlowPulse {
  return { from, at: now, attackMs, releaseAt: null };
}

export function releaseSheetGlow(pulse: SheetGlowPulse, now: number): void {
  pulse.releaseAt = Math.max(now, pulse.at + pulse.attackMs, pulse.at + MIN_SHEET_GLOW_MS);
}

export function sheetGlowAnimating(pulse: SheetGlowPulse, now: number, reducedMotion = false): boolean {
  if (pulse.releaseAt !== null) return now < pulse.releaseAt + (reducedMotion ? 0 : SHEET_GLOW_RELEASE_MS);
  return !reducedMotion && now < pulse.at + pulse.attackMs;
}

type PlaybackIndex = ReturnType<typeof createSheetPlaybackIndex>;

/** Catch note onsets between UI snapshots, without replaying history after seeks or pauses. */
export class SheetStrikeTracker {
  private index: PlaybackIndex | null = null;
  private timeMs = 0;
  private playing = false;

  update(index: PlaybackIndex, timeMs: number, playing: boolean, speed: number) {
    if (this.index === index && this.timeMs === timeMs && this.playing === playing) return null;
    const delta = timeMs - this.timeMs;
    const reset = this.index !== index || !playing || !this.playing || delta < 0
      || !Number.isFinite(timeMs) || !Number.isFinite(this.timeMs)
      || delta > 200 * Math.max(0.1, speed);
    const parts = playing && Number.isFinite(timeMs)
      ? index.startedParts(reset ? (timeMs === 0 ? -1 : timeMs) : this.timeMs, timeMs)
      : new Set<number>();
    this.index = index;
    this.timeMs = timeMs;
    this.playing = playing;
    return { parts, reset };
  }
}
