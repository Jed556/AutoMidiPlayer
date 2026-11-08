import test from 'node:test';
import assert from 'node:assert/strict';
import { MIN_SHEET_GLOW_MS, startSheetGlow, releaseSheetGlow, sheetGlowValue, sheetGlowAnimating, SheetStrikeTracker } from '../src/renderer/lib/utils/sheetGlow.ts';
import { generatePianoSheetParts, createSheetPlaybackIndex } from '../src/renderer/lib/utils/pianoSheet.ts';

test('a tap released before the first frame still reaches full accent and holds for 120ms', () => {
  const pulse = startSheetGlow(1000, 12);
  releaseSheetGlow(pulse, 1001);
  assert.equal(pulse.releaseAt, 1000 + MIN_SHEET_GLOW_MS);
  assert.equal(sheetGlowValue(pulse, 1016), 1);
  assert.equal(sheetGlowValue(pulse, 1119), 1);
  assert.equal(sheetGlowAnimating(pulse, 1119), true);
  assert.ok(sheetGlowValue(pulse, 1160) > 0 && sheetGlowValue(pulse, 1160) < 1);
  assert.equal(sheetGlowValue(pulse, 1300), 0);
  assert.equal(sheetGlowAnimating(pulse, 1300), false);
});

test('a held note glows through its real release and then fades', () => {
  const pulse = startSheetGlow(0, 60);
  assert.equal(sheetGlowValue(pulse, 900), 1);
  releaseSheetGlow(pulse, 1000);
  assert.equal(pulse.releaseAt, 1000);
  assert.equal(sheetGlowValue(pulse, 1000), 1);
  assert.ok(sheetGlowValue(pulse, 1090) > 0);
  assert.equal(sheetGlowValue(pulse, 1180), 0);
});

test('reduced motion retains minimum visibility without color fades', () => {
  const pulse = startSheetGlow(0, 60);
  releaseSheetGlow(pulse, 2);
  assert.equal(sheetGlowValue(pulse, 1, true), 1);
  assert.equal(sheetGlowValue(pulse, 119, true), 1);
  assert.equal(sheetGlowAnimating(pulse, 119, true), true);
  assert.equal(sheetGlowValue(pulse, 120, true), 0);
  assert.equal(sheetGlowAnimating(pulse, 120, true), false);
});

const song = {
  title: 'Quick taps', artist: '', bpm: 120, nativeBpm: 120, durationMs: 10000, tracks: [],
  notes: [10, 30, 1000].map((startMs, i) => ({ id: String(i), noteNumber: 60 + i * 2, startMs, lengthMs: 1, trackIndex: 0 })),
};
const parts = generatePianoSheetParts(song, {
  delimiter: '·', bars: 4, beats: 4, shorten: 1, layoutName: 'Test',
  pitchToKey: new Map([[60, 'a'], [62, 's'], [64, 'd']]), displayPitch: n => n.noteNumber,
});
const index = createSheetPlaybackIndex(parts);
const textFor = set => [...set].map(i => parts[i].text).join('');

test('onsets between snapshots are captured even when their native active snapshot is already empty', () => {
  const tracker = new SheetStrikeTracker();
  tracker.update(index, 0, true, 1);
  const strikes = tracker.update(index, 50, true, 1);
  assert.equal(strikes.reset, false);
  assert.equal(textFor(strikes.parts), 'as');
  assert.equal(index.activeParts(50, [], true).size, 0);
  assert.equal(tracker.update(index, 50, true, 1), null, 'A repeated timestamp must not retrigger the flash');
  assert.equal(tracker.update(index, 60, true, 1).parts.size, 0);
});

test('seeks, pause, resume and a rebuilt sheet clear history rather than replaying skipped notes', () => {
  const tracker = new SheetStrikeTracker();
  tracker.update(index, 0, true, 1);
  assert.equal(tracker.update(index, 50, true, 1).parts.size, 2);
  const forward = tracker.update(index, 2000, true, 1);
  assert.equal(forward.reset, true);
  assert.equal(forward.parts.size, 0);
  assert.equal(tracker.update(index, 0, true, 1).reset, true);
  assert.equal(tracker.update(index, 0, false, 1).parts.size, 0);
  assert.equal(tracker.update(index, 1000, true, 1).parts.size, 0);
  assert.equal(tracker.update(createSheetPlaybackIndex(parts), 1000, true, 1).reset, true);
});

test('normal accelerated playback is not mistaken for a seek', () => {
  const tracker = new SheetStrikeTracker();
  tracker.update(index, 0, true, 5);
  assert.equal(tracker.update(index, 150, true, 5).reset, false);
});
