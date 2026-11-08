import test from 'node:test';
import assert from 'node:assert/strict';
import { DEFAULT_SHEET_DELIMITER, generatePianoSheet, generatePianoSheetParts, createSheetPlaybackIndex } from '../src/renderer/lib/utils/pianoSheet.ts';
import { createDisplayPitch } from '../src/renderer/lib/components/Studio/studioDisplay.ts';

const note = (pitch, startMs, trackIndex = 0, extra = {}) =>
  ({ noteNumber: pitch, startMs, lengthMs: 100, trackIndex, ...extra });
const song = (notes, extra = {}) => ({
  title: 'Test', artist: 'Artist', bpm: 120, nativeBpm: 120, durationMs: 61_000,
  notes, tracks: [], ...extra,
});
const options = (extra = {}) => ({
  delimiter: DEFAULT_SHEET_DELIMITER, bars: 4, beats: 4, shorten: 1,
  pitchToKey: new Map([[60, 'a'], [62, 's'], [64, 'd']]),
  displayPitch: n => n.noteNumber, layoutName: 'Test layout', ...extra,
});
const body = (value, extra) => generatePianoSheet(value, options(extra)).split('\n').slice(4).join('\n');

test('interleaves tracks chronologically and groups simultaneous distinct keys', () => {
  const value = song([note(62, 500), note(64, 0, 1), note(60, 0), note(60, 0, 2)]);
  const original = structuredClone(value);
  assert.equal(body(value), '[da]····s');
  assert.deepEqual(value, original);
});

test('muted tracks stay excluded even when every track is muted', () => {
  const value = song([note(60, 0), note(62, 0, 1)], {
    tracks: [{ index: 0, muted: false, isMuted: true }, { index: 1, muted: true, isMuted: false }],
  });
  assert.equal(body(value), '');
  value.tracks[1].muted = false;
  assert.equal(body(value), 's');
});

test('empty songs and unmapped pitches never produce demonstration notes', () => {
  assert.equal(generatePianoSheet(null, options()), '');
  assert.equal(body(song([])), '');
  assert.equal(body(song([note(61, 0)])), '');
  assert.equal(body(song([note(60, 0)]), { pitchToKey: new Map() }), '');
});

test('track-local fallback retains track ownership and excludes muted tracks', () => {
  assert.equal(body(song([], { tracks: [
    { index: 0, notes: [note(60, 500)] },
    { index: 1, isMuted: true, notes: [note(62, 0)] },
  ] })), '····a');
});

test('rests use middle dots and shortening divides elapsed-time spacing', () => {
  const value = song([note(60, 0), note(62, 500), note(64, 1500)]);
  assert.equal(body(value), 'a····s········d');
  assert.equal(body(value, { shorten: 2 }), 'a··s····d');
  assert.equal(body(value, { delimiter: '-' }), 'a----s--------d');
  assert.equal(body(value, { delimiter: '' }), 'asd');
});

test('line splits follow musical time and preserve wholly silent spans', () => {
  const value = song([note(60, 0), note(62, 2000), note(64, 6000)]);
  assert.equal(body(value, { bars: 1, beats: 4 }), 'a················\ns················\n················\nd');
  assert.equal(body(value, { bars: 2, beats: 4 }), 'a················s················\n················d');
});

test('native beat positions preserve rhythm through tempo changes and BPM overrides', () => {
  const value = song([note(60, 0, 0, { startBeat: 0 }), note(62, 500, 0, { startBeat: 1 }),
    note(64, 1500, 0, { startBeat: 2 })], { bpm: 180, bpmOverride: 180 });
  assert.equal(body(value), 'a····s····d');
  assert.equal(body(song([note(60, 0), note(62, 500)], { bpm: 180 })), 'a····s');
});

test('nearby onsets remain separate instead of becoming a false chord', () => {
  assert.equal(body(song([note(60, 0), note(62, 20)])), 'as');
});

test('header rounds BPM without changing source precision or note spacing', () => {
  const value = song([note(60, 0)], { bpm: 119.88011988011988 });
  assert.match(generatePianoSheet(value, options()), /BPM: 120 \| Time: 1:01/);
  assert.equal(value.bpm, 119.88011988011988);
});

test('sheet uses song transposition and marker pitch adaptation before mapping keys', () => {
  const displayPitch = createDisplayPitch({
    mode: 'Auto', keyOffset: 2, transpose: 'Ignore', markersEnabled: true,
    markers: [{ timeMs: 500, keyOffset: 4 }], playableNotes: [60, 62, 64],
  });
  assert.equal(body(song([note(60, 0), note(60, 500)]), { displayPitch }), 's····d');
});

test('modifier key labels and custom multi-character delimiters are preserved', () => {
  assert.equal(body(song([note(60, 0), note(62, 0), note(64, 500)]), {
    pitchToKey: new Map([[60, 'a'], [62, 'Shift+S'], [64, 'Ctrl+D']]), delimiter: ' | ',
  }), '[aShift+S] |  |  |  | Ctrl+D');
});

test('delimiter styling preserves plain text and does not color matching keys or metadata', () => {
  const value = song([note(60, 0), note(62, 500)], { title: 'a title' });
  const settings = options({ delimiter: 'a' });
  const parts = generatePianoSheetParts(value, settings);
  assert.equal(parts.map(part => part.text).join(''), generatePianoSheet(value, settings));
  assert.deepEqual(parts.filter(part => part.delimiter).map(part => part.text), ['aaaa']);
  assert.ok(parts.some(part => !part.delimiter && part.text === 'a'));
  assert.ok(parts.some(part => !part.delimiter && part.text.includes('a title')));
});

test('chord wrappers and control prefixes are notation while literal bracket and caret keys stay bright', () => {
  const value = song([note(60, 0), note(62, 0), note(64, 500), note(65, 1000)]);
  const settings = options({ pitchToKey: new Map([[60, '['], [62, '^a'], [64, ']'], [65, '^']]) });
  const parts = generatePianoSheetParts(value, settings);
  assert.equal(body(value, settings), '[[^a]····]····^');
  assert.deepEqual(parts.filter(part => part.notation).map(part => part.text), ['[', '^', ']']);
  assert.deepEqual(parts.filter(part => !part.notation && !part.delimiter).slice(2).map(part => part.text), ['[', 'a', ']', '^']);
  assert.equal(parts.map(part => part.text).join(''), generatePianoSheet(value, settings));
});

const playback = (value, extra) => {
  const parts = generatePianoSheetParts(value, options(extra));
  const index = createSheetPlaybackIndex(parts);
  return { parts, index, lit: (timeMs, notes, playing = true) =>
    [...index.activeParts(timeMs, notes, playing)].sort((a, b) => a - b).map(index => parts[index].text) };
};

test('playback highlights only the current occurrence, follows seeks, and clears on pause or release', () => {
  const { parts, index, lit } = playback(song([note(60, 0), note(60, 1000)]));
  const held = [{ pitch: 60, trackId: 0 }];
  const first = [...index.activeParts(50, held, true)];
  const second = [...index.activeParts(1050, held, true)];
  assert.equal(first.length, 1);
  assert.equal(second.length, 1);
  assert.notEqual(first[0], second[0]);
  assert.equal(parts[first[0]].text, 'a');
  assert.deepEqual([...index.activeParts(20, held, true)], first, 'A backward seek returns to the earlier occurrence');
  assert.deepEqual(lit(50, held, false), []);
  assert.deepEqual(lit(50, []), []);
  assert.deepEqual(lit(100, held), [], 'End times are exclusive');
  assert.deepEqual(lit(500, held), [], 'A stale held pitch cannot light later sheet occurrences');
});

test('chord keys release independently and brackets remain active while any member is held', () => {
  const { lit } = playback(song([note(60, 0, 0, { lengthMs: 500 }), note(62, 0, 1)]), {
    pitchToKey: new Map([[60, '^a'], [62, 's']]),
  });
  assert.deepEqual(lit(50, [{ pitch: 60, trackId: 0 }, { pitch: 62, trackId: 1 }]), ['[', '^', 'a', 's', ']']);
  assert.deepEqual(lit(200, [{ pitch: 60, trackId: 0 }]), ['[', '^', 'a', ']']);
  assert.deepEqual(lit(200, [{ pitch: 60, trackId: 1 }]), [], 'Track identity matters');
});

test('tap duration projection respects playback speed and held-note settings', () => {
  const value = song([note(60, 0, 0, { lengthMs: 800 })]);
  const held = [{ pitch: 60, trackId: 0 }];
  const tap = playback(value, { timing: { holdNotes: false, thresholdMs: 100, tapMs: 20, speed: 2 } });
  assert.deepEqual(tap.lit(30, held), ['a']);
  assert.deepEqual(tap.lit(40, held), []);
  const sustained = playback(value, { timing: { holdNotes: true, thresholdMs: 100, tapMs: 20, speed: 2 } });
  assert.deepEqual(sustained.lit(700, held), ['a']);
});

test('raw listen playback matches native raw pitches while retaining the displayed layout', () => {
  const { lit } = playback(song([note(60, 0)]), {
    displayPitch: n => n.noteNumber + 12,
    pitchToKey: new Map([[72, 'q']]), rawPlayback: true,
  });
  assert.deepEqual(lit(50, [{ pitch: 60, trackId: 0 }]), ['q']);
  assert.deepEqual(lit(50, [{ pitch: 72, trackId: 0 }]), []);
});

test('playback indexing never changes sheet text or highlights delimiters and headers', () => {
  const { parts, index } = playback(song([note(60, 500)]));
  const text = parts.map(part => part.text).join('');
  const active = index.activeParts(550, [{ pitch: 60, trackId: 0 }], true);
  assert.ok([...active].every(i => !parts[i].delimiter && parts[i].presses?.length));
  assert.equal(parts.map(part => part.text).join(''), text);
});
