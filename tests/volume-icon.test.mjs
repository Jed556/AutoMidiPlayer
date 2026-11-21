import test from 'node:test';
import assert from 'node:assert/strict';
import { crossStrokeProgress } from '../src/renderer/lib/components/Icons/windowControlUtils.ts';
import {
  sameVolumeMotion,
  stepVolumeMotion,
  volumeTargetFor,
} from '../src/renderer/lib/components/Icons/volumeIconUtils.ts';

function assertMotionInBounds(motion) {
  assert.ok(motion.primary >= 0 && motion.primary <= 1, `primary out of bounds: ${motion.primary}`);
  assert.ok(motion.primaryScale >= 1 && motion.primaryScale <= 2.5, `primaryScale out of bounds: ${motion.primaryScale}`);
  assert.ok(motion.inner >= 0 && motion.inner <= 1, `inner out of bounds: ${motion.inner}`);
  assert.ok(motion.first >= 0 && motion.first <= 1, `first out of bounds: ${motion.first}`);
  assert.ok(motion.second >= 0 && motion.second <= 1, `second out of bounds: ${motion.second}`);
}

test('crossStrokeProgress clamps negative and >1 progress into [0, 1]', () => {
  assert.deepEqual(crossStrokeProgress(-0.5), { first: 0, second: 0 });
  assert.deepEqual(crossStrokeProgress(0), { first: 0, second: 0 });
  assert.deepEqual(crossStrokeProgress(1), { first: 1, second: 1 });
  assert.deepEqual(crossStrokeProgress(1.5), { first: 1, second: 1 });
});

test('stepVolumeMotion clamps negative elapsedMs and never extrapolates past bounds when spam-toggled', () => {
  const muted = volumeTargetFor(0);
  const high = volumeTargetFor(1);

  const neg = stepVolumeMotion(muted, high, -12, 250);
  assert.equal(neg.done, false);
  assert.deepEqual(neg.motion, muted);
  assertMotionInBounds(neg.motion);

  let current = { ...high };
  for (let i = 0; i < 200; i++) {
    const target = i % 2 === 0 ? muted : high;
    const elapsed = (i % 5 === 0) ? -8 : (i % 7) * 16;
    const next = stepVolumeMotion(current, target, elapsed, 200);
    assertMotionInBounds(next.motion);
    current = next.motion;
  }
});

test('volumeTargetFor groups values by icon tier and sameVolumeMotion detects unchanged targets', () => {
  assert.equal(sameVolumeMotion(volumeTargetFor(0.05), volumeTargetFor(0.32)), true);
  assert.equal(sameVolumeMotion(volumeTargetFor(0.33), volumeTargetFor(0.65)), true);
  assert.equal(sameVolumeMotion(volumeTargetFor(0.66), volumeTargetFor(2.0)), true);
  assert.equal(sameVolumeMotion(volumeTargetFor(0), volumeTargetFor(0.01)), false);
  assert.equal(sameVolumeMotion(volumeTargetFor(0.32), volumeTargetFor(0.33)), false);
  assert.equal(sameVolumeMotion(volumeTargetFor(0.65), volumeTargetFor(0.66)), false);
});
