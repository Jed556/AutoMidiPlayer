import test from 'node:test';
import assert from 'node:assert/strict';
import {
  backtrackGuitarSlot,
  dispatchedGuitarPositions,
  guitarVoicing,
  guitarChord,
  fretRomanNumeral,
  resolveGuitarSlotFromPitches,
  wheelAngleDelta,
} from '../src/renderer/lib/utils/guitarLayout.ts';
import { instrumentLayoutIds, guitarNotes } from '../src/shared/utils/instrumentLayout.ts';

const guitar = { guitar: { tuning: [64, 59, 55, 50, 45, 40], fretCount: 24 },
  keyboardLayouts: ['keys'], chordLayouts: ['chords'], fretLayouts: ['frets'],
  chords: [{ name: 'G', pitchClasses: [2, 7, 11] }, { name: 'G/B', pitchClasses: [2, 7, 11], bassPitchClass: 11 }] };

test('guitar presentation keeps open strings, twelfth and twenty-fourth frets exact', () => {
  assert.deepEqual(guitarVoicing(guitar, [40, 45]), [{ string: 5, fret: 0, pitch: 40 }, { string: 4, fret: 0, pitch: 45 }]);
  assert.deepEqual(guitarVoicing(guitar, [71, 76]), [{ string: 1, fret: 12, pitch: 71 }, { string: 0, fret: 12, pitch: 76 }]);
  assert.deepEqual(guitarVoicing(guitar, [88]), [{ string: 0, fret: 24, pitch: 88 }]);
  assert.deepEqual(guitarVoicing(guitar, [60, 61]), []);
  assert.deepEqual(guitarVoicing(guitar, []), []);
  assert.deepEqual(guitarVoicing(null, [60]), []);
});

test('guitar layout groups and pitches stay independent of keyboard notes', () => {
  assert.deepEqual(instrumentLayoutIds(guitar, 'keyboard'), ['keys']);
  assert.deepEqual(instrumentLayoutIds(guitar, 'chords'), ['chords']);
  assert.deepEqual(instrumentLayoutIds(guitar, 'frets'), ['frets']);
  assert.deepEqual(instrumentLayoutIds(guitar, 'all'), ['keys', 'chords', 'frets']);
  assert.deepEqual(guitarNotes(guitar), Array.from({ length: 49 }, (_, index) => index + 40));
});

test('chord display distinguishes slash bass and does not invent a chord for single notes', () => {
  assert.equal(guitarChord(guitar, [55, 59, 62]), 'G');
  assert.equal(guitarChord(guitar, [59, 62, 67]), 'G/B');
  assert.equal(guitarChord(guitar, [55]), null);
  assert.equal(guitarChord(guitar, []), null);
});

test('guitar display uses the dispatched shape, string mask, and open selector', () => {
  const instrument = { ...guitar, chords: [
    { name: 'C', pitchClasses: [0, 4, 7], stringPitches: [64, 60, 55, null, 48, 'muted'] },
  ] };
  const attack = { mode: 'chords', slot: 0, strings: 0b000110, page: 0 };
  assert.deepEqual(dispatchedGuitarPositions(instrument, attack), [
    { string: 1, fret: 1, pitch: 60 }, { string: 2, fret: 0, pitch: 55 },
  ]);
  assert.deepEqual(dispatchedGuitarPositions(instrument, { ...attack, strings: 0 }), [
    { string: 0, fret: 0, pitch: 64 }, { string: 1, fret: 1, pitch: 60 },
    { string: 2, fret: 0, pitch: 55 }, { string: 4, fret: 3, pitch: 48 },
  ]);
  assert.deepEqual(dispatchedGuitarPositions(instrument, { ...attack, mode: 'frets', page: 4, slot: 24, strings: 0b100001 }), [
    { string: 0, fret: 0, pitch: 64 }, { string: 5, fret: 0, pitch: 40 },
  ]);
  assert.deepEqual(dispatchedGuitarPositions(instrument, { ...attack, mode: 'frets', page: 0, slot: 4, strings: 0, strum: 'down' }), [
    { string: 0, fret: 5, pitch: 69 },
    { string: 1, fret: 5, pitch: 64 },
    { string: 2, fret: 5, pitch: 60 },
    { string: 3, fret: 5, pitch: 55 },
    { string: 4, fret: 5, pitch: 50 },
    { string: 5, fret: 5, pitch: 45 },
  ]);
  assert.deepEqual(dispatchedGuitarPositions(instrument, { ...attack, slot: null }), []);
});

test('fret wheel labels use Roman numerals I through XXIV', () => {
  const expected = [
    'I', 'II', 'III', 'IV', 'V', 'VI',
    'VII', 'VIII', 'IX', 'X', 'XI', 'XII',
    'XIII', 'XIV', 'XV', 'XVI', 'XVII', 'XVIII',
    'XIX', 'XX', 'XXI', 'XXII', 'XXIII', 'XXIV',
  ];
  assert.deepEqual(Array.from({ length: 24 }, (_, idx) => fretRomanNumeral(idx + 1)), expected);
  assert.equal(fretRomanNumeral(0), '');
  assert.equal(fretRomanNumeral(-3), '');
});

test('wheel highlight rotor takes the shortest angular path around the ring', () => {
  // Stepping clockwise 0 -> 1 -> ... -> 5 -> 0 accumulates +60° per step without spinning backward
  let angle = 0;
  for (const slot of [1, 2, 3, 4, 5, 0, 1]) {
    angle += wheelAngleDelta(angle, slot, 6);
  }
  assert.equal(angle, 420);

  // Stepping counter-clockwise 0 -> 5 takes -60° instead of +300°
  assert.equal(wheelAngleDelta(0, 5, 6), -60);
  // Stepping clockwise 5 (300°) -> 0 takes +60° instead of -300°
  assert.equal(wheelAngleDelta(300, 0, 6), 60);
  // Opposite slot (180°) takes +180°
  assert.equal(wheelAngleDelta(0, 3, 6), 180);
  assert.equal(wheelAngleDelta(60, 1, 6), 0);
});

test('backtrackGuitarSlot resolves the active fret or chord at or before the playhead', () => {
  const notes = [
    { noteNumber: 55, startMs: 0, lengthMs: 200, trackIndex: 0 },
    { noteNumber: 59, startMs: 0, lengthMs: 200, trackIndex: 0 },
    { noteNumber: 62, startMs: 0, lengthMs: 200, trackIndex: 0 },
    { noteNumber: 59, startMs: 500, lengthMs: 200, trackIndex: 0 },
    { noteNumber: 62, startMs: 500, lengthMs: 200, trackIndex: 0 },
    { noteNumber: 67, startMs: 500, lengthMs: 200, trackIndex: 0 },
    { noteNumber: 88, startMs: 1000, lengthMs: 200, trackIndex: 0 },
  ];

  // At 300ms (after first G chord at 0ms, before G/B at 500ms), backtracks to G (slot 0)
  assert.equal(
    backtrackGuitarSlot({ instrument: guitar, fretMode: false, notes, currentTimeMs: 300 }),
    0,
  );

  // At 750ms (after G/B at 500ms), backtracks to G/B (slot 1)
  assert.equal(
    backtrackGuitarSlot({ instrument: guitar, fretMode: false, notes, currentTimeMs: 750 }),
    1,
  );

  // In fret mode at 1200ms (after pitch 88 = fret 24 = slot 23), backtracks to slot 23
  assert.equal(
    backtrackGuitarSlot({ instrument: guitar, fretMode: true, notes, currentTimeMs: 1200 }),
    23,
  );

  // Open string pitch 40 resolves to openSlot (24) in fret mode
  assert.equal(resolveGuitarSlotFromPitches(guitar, [40], true, 6), 24);
});
