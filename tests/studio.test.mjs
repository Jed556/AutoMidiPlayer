import test from 'node:test';
import assert from 'node:assert/strict';
import { createNoteIndex } from '../src/renderer/lib/utils/noteIndex.ts';
import { PlaybackPositionGate } from '../src/renderer/lib/utils/playbackPosition.ts';
import { getNoteTextColor } from '../src/renderer/lib/utils/noteAppearance.ts';
import { TRACK_COLORS } from '../src/renderer/lib/utils/musicConstants.ts';
import { createDisplayPitch, getDisplayTiming } from '../src/renderer/lib/components/Studio/studioDisplay.ts';
import { projectNoteTiming } from '../src/renderer/lib/utils/noteTiming.ts';

test('Studio projects threshold taps while retaining original duration for the tooltip', () => {
  const settings = { holdNotes: true, thresholdMs: 100, tapMs: 20, speed: 1 };
  const short = note('short', 0, 100);
  const tap = projectNoteTiming(short, settings);
  assert.equal(tap.duration, 0.02);
  assert.equal(tap.lengthMs, 20);
  assert.equal(tap.rawDuration, 0.1);
  assert.equal(tap.tapDurationMs, 20);
  assert.equal(short.lengthMs, 100);
  const held = note('held', 0, 330);
  assert.equal(projectNoteTiming(held, settings), held);
  const converted = projectNoteTiming(held, { ...settings, thresholdMs: 500 });
  assert.equal(converted.rawDuration, 0.33);
  assert.equal(converted.tapDurationMs, 20);
  assert.equal([...createNoteIndex([converted]).between(0.1, 0.2)].length, 0);
});

test('Studio tap widths preserve wall-clock delay at changed speed and in raw display', () => {
  const source = note('source', 0, 330);
  const settings = { holdNotes: true, thresholdMs: 100, tapMs: 35, speed: 4 };
  const tap = projectNoteTiming(source, settings);
  assert.equal(tap.lengthMs, 140);
  assert.equal(tap.tapDurationMs, 35);
  assert.equal(projectNoteTiming(source), source);
  assert.equal(projectNoteTiming(source, { ...settings, holdNotes: false, speed: 1 }).lengthMs, 35);
  assert.equal(projectNoteTiming(source, { ...settings, thresholdMs: 0, speed: 1 }), source);
  assert.equal(projectNoteTiming(source, { ...settings, holdNotes: false, tapMs: 0 }).lengthMs, 0);
});

const note = (id, startMs, lengthMs) => ({ id, startMs, lengthMs, noteNumber: 60 });
const displaySettings = (overrides = {}) => ({
  mode: 'Auto', keyOffset: 2, transpose: 'Ignore', markersEnabled: true,
  markers: [{ timeMs: 1000, keyOffset: 5 }], playableNotes: [], notes: [], ...overrides,
});

test('Studio modes distinguish raw MIDI, song transposition, and timeline markers', () => {
  const original = { ...note('original', 1500, 500), rawNoteNumber: 60, pitch: 80 };
  assert.equal(createDisplayPitch(displaySettings({ mode: 'SongDefault' }))(original), 60);
  assert.equal(createDisplayPitch(displaySettings({ mode: 'NoteModifiers' }))(original), 60);
  assert.equal(createDisplayPitch(displaySettings({ mode: 'Transpose' }))(original), 62);
  assert.equal(createDisplayPitch(displaySettings())(original), 65);
  assert.equal(original.pitch, 80);
  assert.equal(original.noteNumber, 60);
});

test('Note Modifiers previews note timing without song, marker, or instrument pitch adaptation', () => {
  const source = { ...note('source', 1500, 100), noteNumber: 36, rawNoteNumber: 36, pitch: 72 };
  const original = structuredClone(source);
  for (const transpose of ['Ignore', 'Up', 'Down', 'Smart']) {
    const pitch = createDisplayPitch(displaySettings({ mode: 'NoteModifiers', transpose,
      keyOffset: 12, playableNotes: [60, 62, 64], markers: [{ timeMs: 1000, keyOffset: 7, transpose: 1 }] }));
    const settings = { holdNotes: true, thresholdMs: 100, tapMs: 30, speed: 1 };
    // The explicit preview still works when Listen Mode uses the raw synthesizer.
    const timing = getDisplayTiming('NoteModifiers', settings, true);
    const rendered = projectNoteTiming({ ...source, pitch: pitch(source) }, timing);
    assert.equal(rendered.pitch, 36);
    assert.equal(rendered.lengthMs, 30);
    assert.equal(rendered.rawDuration, 0.1);
    assert.equal(rendered.tapDurationMs, 30);
    assert.equal(projectNoteTiming(source, { ...timing, thresholdMs: 99 }), source);
    assert.equal(projectNoteTiming(source, { ...timing, tapMs: 50 }).lengthMs, 50);
    assert.equal(projectNoteTiming(source, { ...timing, speed: 2 }).lengthMs, 60);
  }
  assert.deepEqual(source, original);
});

test('timing previews preserve the existing Auto, Transpose, and Song Default behavior', () => {
  const timing = { holdNotes: false, thresholdMs: 0, tapMs: 20, speed: 1 };
  for (const mode of ['Auto', 'Transpose']) {
    assert.equal(getDisplayTiming(mode, timing, false), timing);
    assert.equal(getDisplayTiming(mode, timing, true), undefined);
  }
  assert.equal(getDisplayTiming('SongDefault', timing, false), undefined);
  assert.equal(getDisplayTiming('SongDefault', timing, true), undefined);
  assert.equal(getDisplayTiming('NoteModifiers', timing, false), timing);
});

test('Auto uses the latest marker at note onset, with song settings before the first marker', () => {
  const pitch = createDisplayPitch(displaySettings({ markers: [
    { timeMs: 2000, keyOffset: -3 }, { timeMs: 1000, keyOffset: 5 },
  ] }));
  assert.equal(pitch(note('before', 999, 5000)), 62);
  assert.equal(pitch(note('boundary', 1000, 5000)), 65);
  assert.equal(pitch(note('between', 1999, 5000)), 65);
  assert.equal(pitch(note('after', 2000, 5000)), 57);
  assert.equal(createDisplayPitch(displaySettings({ markersEnabled: false }))(note('disabled', 2000, 500)), 62);
});

test('marker transpose overrides use native enum values and inherit when unset', () => {
  const source = { ...note('low', 1000, 500), noteNumber: 36 };
  const settings = displaySettings({ keyOffset: 0, playableNotes: [60, 62, 64, 65, 67, 69, 71, 72],
    markers: [{ timeMs: 1000, keyOffset: 0, transpose: 1 }] });
  assert.equal(createDisplayPitch(settings)(source), 60);
  assert.equal(createDisplayPitch({ ...settings, markers: [{ timeMs: 1000, keyOffset: 0, transpose: 0 }] })(source), 36);
  assert.equal(createDisplayPitch({ ...settings, markers: [{ timeMs: 1000, keyOffset: 0 }] })(source), 36);
  assert.equal(createDisplayPitch({ ...settings, mode: 'Transpose' })(source), 36);
});

test('transposed views fold octaves, snap Smart notes, and retain unmapped notes', () => {
  const settings = displaySettings({ mode: 'Transpose', keyOffset: 0,
    playableNotes: [60, 62, 64, 65, 67, 69, 71, 72] });
  for (const transpose of ['Up', 'Down', 'Smart']) {
    const pitch = createDisplayPitch({ ...settings, transpose });
    assert.equal(pitch({ ...note('low', 0, 500), noteNumber: 36 }), 60);
    assert.equal(pitch({ ...note('high', 0, 500), noteNumber: 96 }), 72);
  }
  assert.equal(createDisplayPitch({ ...settings, transpose: 'Smart' })({ ...note('sharp', 0, 500), noteNumber: 61 }), 60);
  assert.equal(createDisplayPitch({ ...settings, transpose: 'Ignore' })({ ...note('unmapped', 0, 500), noteNumber: 61 }), 61);
});

test('display pitches handle missing instruments, narrow ranges, and MIDI boundaries', () => {
  assert.equal(createDisplayPitch(displaySettings({ transpose: 'Smart' }))(note('no-instrument', 0, 500)), 62);
  assert.equal(createDisplayPitch(displaySettings({ transpose: 'Up', playableNotes: [65] }))(note('narrow', 0, 500)), 65);
  assert.equal(createDisplayPitch(displaySettings({ keyOffset: -100 }))(note('bottom', 0, 500)), 0);
  assert.equal(createDisplayPitch(displaySettings({ keyOffset: 100 }))(note('top', 0, 500)), 127);
});

test('bright notes use dark text, and dark notes use light text', () => {
  assert.equal(getNoteTextColor('#FFB703'), '#000000');
  assert.equal(getNoteTextColor('#06D6A0'), '#000000');
  assert.equal(getNoteTextColor('#8338EC'), '#ffffff');
  assert.equal(getNoteTextColor('#fff'), '#000000');
  assert.equal(getNoteTextColor('#000'), '#ffffff');
  assert.equal(getNoteTextColor('rgb(255, 183, 3)'), '#000000');
});

test('every track palette color has at least 4.5:1 text contrast', () => {
  for (const color of TRACK_COLORS) {
    const channels = [1, 3, 5].map(start => parseInt(color.slice(start, start + 2), 16) / 255)
      .map(value => value <= 0.04045 ? value / 12.92 : ((value + 0.055) / 1.055) ** 2.4);
    const luminance = channels[0] * 0.2126 + channels[1] * 0.7152 + channels[2] * 0.0722;
    const contrast = getNoteTextColor(color) === '#000000'
      ? (luminance + 0.05) / 0.05 : 1.05 / (luminance + 0.05);
    assert.ok(contrast >= 4.5, `${color} has insufficient text contrast: ${contrast}`);
  }
});

test('note queries retain sustained notes that start before the viewport', () => {
  const index = createNoteIndex([note('later', 9000, 100), note('sustain', 0, 10000), note('short', 500, 50)]);
  assert.deepEqual([...index.between(5, 6)].map(n => n.id), ['sustain']);
  assert.equal([...index.between(11, 12)].length, 0);
});

test('indexed queries match a full scan across a dense song', () => {
  const notes = Array.from({ length: 100000 }, (_, i) => note(String(i), i * 10, 20 + i % 500));
  notes.unshift(note('long-sustain', 0, 1000000));
  const index = createNoteIndex(notes);
  for (const start of [0, 10, 250.25, 900, 999.9]) {
    const expected = notes.filter(n => n.startMs / 1000 <= start + 1 && (n.startMs + n.lengthMs) / 1000 >= start);
    assert.deepEqual([...index.between(start, start + 1)], expected);
  }
});

test('seeks reject queued old playback positions and accept engine acknowledgement', () => {
  const gate = new PlaybackPositionGate();
  assert.equal(gate.accepts({ timeMs: 50, seekId: 0 }), true);
  const first = gate.requestSeek();
  const latest = gate.requestSeek();
  assert.equal(gate.accepts({ timeMs: 10000, seekId: first }), false);
  assert.equal(gate.accepts({ timeMs: 500, seekId: latest }), true);
  assert.equal(gate.accepts({ timeMs: 520, seekId: latest }), true);
  gate.cancelRequest(first);
  assert.equal(gate.accepts({ timeMs: 10000, seekId: first }), false);
  gate.reset();
  assert.equal(gate.accepts({ timeMs: 0, seekId: latest }), true);
});
