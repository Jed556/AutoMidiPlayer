import test from 'node:test';
import assert from 'node:assert/strict';
import { canSkipOnboarding } from '../src/renderer/lib/components/Layout/onboardingPolicy.ts';

test('required first two steps cannot be skipped, with or without legacy data', () => {
  for (const count of [8, 9]) {
    assert.equal(canSkipOnboarding(0, count, false), false);
    assert.equal(canSkipOnboarding(1, count, false), false);
    assert.equal(canSkipOnboarding(2, count, false), true);
    assert.equal(canSkipOnboarding(count - 1, count, false), false);
    assert.equal(canSkipOnboarding(2, count, true), false);
  }
});
