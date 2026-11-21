import test from 'node:test';
import assert from 'node:assert/strict';
import { MIN_PRESS_HIGHLIGHT_MS, PressHighlights } from '../src/renderer/lib/utils/pressHighlights.ts';

test('short presses stay visible independently and a long hold clears on release', ({ mock }) => {
  mock.timers.enable({ apis: ['setTimeout'] });
  let time = 0;
  mock.method(performance, 'now', () => time);
  const advance = ms => { time += ms; mock.timers.tick(ms); };
  let lit = [];
  const highlights = new PressHighlights(value => value, values => { lit = values; });

  highlights.update([60]);
  highlights.update([]);
  advance(20);
  highlights.update([64]);
  assert.deepEqual(lit, [60, 64]);
  advance(MIN_PRESS_HIGHLIGHT_MS - 21);
  assert.deepEqual(lit, [60, 64]);
  advance(1);
  assert.deepEqual(lit, [64]);
  advance(MIN_PRESS_HIGHLIGHT_MS);
  assert.deepEqual(lit, [64]);
  highlights.update([]);
  assert.deepEqual(lit, []);
});

test('retriggering a released press restarts its minimum and cleanup cancels pending highlights', ({ mock }) => {
  mock.timers.enable({ apis: ['setTimeout'] });
  let time = 0;
  mock.method(performance, 'now', () => time);
  const advance = ms => { time += ms; mock.timers.tick(ms); };
  const frames = [];
  const highlights = new PressHighlights(action => action.slot, values => frames.push(values));

  highlights.update([{ slot: 1 }]);
  highlights.update([]);
  advance(20);
  const second = { slot: 1, strings: 2 };
  highlights.update([second]);
  highlights.update([]);
  advance(MIN_PRESS_HIGHLIGHT_MS - 1);
  assert.deepEqual(frames.at(-1), [second]);
  highlights.clear();
  const count = frames.length;
  advance(MIN_PRESS_HIGHLIGHT_MS);
  assert.deepEqual(frames.at(-1), []);
  assert.equal(frames.length, count);
});
