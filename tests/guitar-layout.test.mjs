import test from 'node:test';
import assert from 'node:assert/strict';
import { guitarVoicing, guitarChord } from '../src/renderer/lib/utils/guitarLayout.ts';
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
