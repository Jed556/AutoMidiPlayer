import { crossStrokeProgress } from './windowControlUtils.ts';

export interface VolumeMotion {
  primary: number;
  primaryScale: number;
  inner: number;
  first: number;
  second: number;
}

export function volumeTargetFor(value: number): VolumeMotion {
  const safe = Number.isFinite(value) ? value : 1;
  const muted = safe <= 0;
  const high = safe >= 0.66;
  return {
    primary: muted ? 0 : 1,
    primaryScale: high ? 2.5 : safe >= 0.33 ? 1.45 : 1,
    inner: high ? 1 : 0,
    first: muted ? 1 : 0,
    second: muted ? 1 : 0,
  };
}

export function sameVolumeMotion(a: VolumeMotion, b: VolumeMotion): boolean {
  return (
    a.primary === b.primary &&
    a.primaryScale === b.primaryScale &&
    a.inner === b.inner &&
    a.first === b.first &&
    a.second === b.second
  );
}

export function stepVolumeMotion(
  start: VolumeMotion,
  target: VolumeMotion,
  elapsedMs: number,
  durationMs: number
): { done: boolean; motion: VolumeMotion } {
  if (!(durationMs > 0) || !Number.isFinite(durationMs)) {
    return { done: true, motion: { ...target } };
  }
  const p = Number.isFinite(elapsedMs) ? Math.max(0, Math.min(1, elapsedMs / durationMs)) : 1;
  if (p >= 1) {
    return { done: true, motion: { ...target } };
  }
  const ease = (t: number) => 1 - Math.pow(1 - t, 3);
  const mix = (a: number, b: number, t: number) => a + (b - a) * t;
  const e = ease(p);
  // The upper-right stroke leads; the upper-left stroke starts 20% later.
  const { first, second } = crossStrokeProgress(p);
  return {
    done: false,
    motion: {
      primary: mix(start.primary, target.primary, e),
      primaryScale: mix(start.primaryScale, target.primaryScale, e),
      inner: mix(start.inner, target.inner, ease(Math.max(0, (p - 0.12) / 0.88))),
      first: mix(start.first, target.first, first),
      second: mix(start.second, target.second, second),
    },
  };
}
