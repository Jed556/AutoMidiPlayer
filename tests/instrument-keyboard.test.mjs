import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createPianoRows, createPianoSlots, hasInGameInstrumentLayout, pianoRange } from '../src/renderer/lib/utils/instrumentKeyboard.ts';
import { instrumentNotes, instrumentKeyRows, instrumentKeyPairs, instrumentLayoutStyle, normalizeKeyboardLayout } from '../src/shared/utils/instrumentLayout.ts';

const instrument = path => {
  const raw = JSON.parse(readFileSync(new URL(`../src-tauri/resources/defaults/Games/${path}`, import.meta.url), 'utf8'));
  return { ...raw, notes: instrumentNotes(raw) };
};
const displayedPitches = slots => slots.flatMap(slot => [slot.white?.pitch, slot.sharp?.pitch])
  .filter(pitch => pitch !== undefined).sort((a, b) => a - b);

test('88- and 61-key pianos retain every key and use their real range', () => {
  for (const game of ['Roblox', 'Blue Protocol Star Resonance']) {
    for (const [count, range, whiteCount] of [[88, 'A0 – C8', 52], [61, 'C2 – C7', 36]]) {
      const piano = instrument(`${game}/Instruments/Pianos/Piano${count}Key.json`);
      const slots = createPianoSlots(piano.notes);
      assert.equal(slots.length, whiteCount);
      assert.deepEqual(displayedPitches(slots), piano.notes);
      assert.equal(pianoRange(piano.notes), range);
      assert.equal(hasInGameInstrumentLayout(piano), false);
    }
  }
});

test('custom chromatic grids keep their in-game view and their exact piano notes', () => {
  const piano = instrument('Heartopia/Instruments/Pianos/Piano37Key.json');
  assert.equal(hasInGameInstrumentLayout(piano), true);
  assert.equal(pianoRange(piano.notes), 'C3 – C6');
  assert.deepEqual(displayedPitches(createPianoSlots(piano.notes)), [...piano.notes].sort((a, b) => a - b));
});

test('sparse scales retain isolated sharps and add no unsupported natural keys', () => {
  const lyre = instrument('Genshin Impact/Instruments/Lyres/VintageLyre.json');
  assert.equal(hasInGameInstrumentLayout(lyre), true);
  assert.equal(pianoRange(lyre.notes), 'C3 – A#5');
  const slots = createPianoSlots(lyre.notes);
  assert.deepEqual(displayedPitches(slots), [...lyre.notes].sort((a, b) => a - b));
  assert.ok(slots.some(slot => !slot.white && slot.sharp));
});

test('empty, repeated, unsorted, and black-only note sets remain well-defined', () => {
  assert.deepEqual(createPianoSlots([]), []);
  assert.equal(pianoRange([]), '');
  assert.equal(pianoRange([61]), 'C#4');
  assert.deepEqual(displayedPitches(createPianoSlots([63, 61, 61, 70])), [61, 63, 70]);
  assert.equal(hasInGameInstrumentLayout(null), false);
  assert.equal(hasInGameInstrumentLayout({ type: 'drum8' }), true);
});

test('piano rows wrap at C without dropping or repeating keys across viewport widths', () => {
  const piano = instrument('Roblox/Instruments/Pianos/Piano88Key.json');
  const slots = createPianoSlots(piano.notes);
  for (const width of [320, 640, 960, 1440]) {
    const rows = createPianoRows(slots, width);
    assert.deepEqual(rows.flat(), slots);
    assert.deepEqual(displayedPitches(rows.flat()), piano.notes);
    assert.ok(rows.every(row => row.length <= Math.max(7, Math.floor((width - 26) / 26))));
    assert.ok(rows.slice(1).every(row => row[0].white?.isC));
  }
  assert.equal(createPianoRows(slots, 1440).length, 1);
  assert.ok(createPianoRows(slots, 640).length > 1);
});

test('row wrapping supports sparse scales and keeps a closing C when it fits', () => {
  assert.deepEqual(createPianoRows([], 0), []);
  const octave = createPianoSlots(Array.from({ length: 13 }, (_, index) => 60 + index));
  assert.deepEqual(createPianoRows(octave, 260), [octave]);
  const sparse = createPianoSlots(Array.from({ length: 17 }, (_, index) => index * 3 + 1));
  const rows = createPianoRows(sparse, 0);
  assert.deepEqual(rows.flat(), sparse);
  assert.ok(rows.every(row => row.length > 0 && row.length <= 7));
});

const layout = name => normalizeKeyboardLayout(JSON.parse(readFileSync(new URL(`../src-tauri/resources/defaults/KeyboardLayouts/${name}.json`, import.meta.url), 'utf8')));

test('Heartopia matrices preserve the configured rows and QWERTY bindings', () => {
  for (const [file, binding, sizes] of [
    ['Piano2Row', 'Heartopia_QWERTY_2Row', [8, 7]],
    ['Piano3Row', 'Heartopia_QWERTY_3Row', [5, 5, 5]],
    ['Piano22Key', 'Heartopia_QWERTY_22Key', [8, 7, 7]],
    ['Piano37Key', 'Heartopia_QWERTY_37Key', [13, 12, 12]],
  ]) {
    const piano = instrument(`Heartopia/Instruments/Pianos/${file}.json`);
    const keyboard = layout(binding);
    const rows = instrumentKeyRows(piano, keyboard);
    assert.equal(instrumentLayoutStyle(piano), 'heartopia');
    assert.deepEqual(rows.map(row => row.length), sizes);
    assert.deepEqual(rows.flat().map(key => key.pitch), piano.notes);
    assert.deepEqual(rows.flat().map(key => key.label), keyboard.keys);
    assert.equal(rows.flat().filter(key => key.sharp).length, file === 'Piano37Key' ? 15 : 0);
  }
  const piano = instrument('Heartopia/Instruments/Pianos/Piano37Key.json');
  const rows = instrumentKeyRows(piano, layout('Heartopia_QWERTY_37Key'));
  assert.deepEqual(rows.map(row => [row[0].pitch, row[0].label]), [[72, 'q'], [60, 'z'], [48, ',']]);
  assert.equal(rows[0].at(-1).label, 'i');
});

test('matrix spacers never shift labels, and playback shares the displayed mapping', () => {
  const piano = { id: 'spaced', name: 'Spaced', layoutStyle: 'heartopia', keys: [[60, null, 61], [72, 73]] };
  const keyboard = normalizeKeyboardLayout({ id: 'qwerty', name: 'QWERTY', keys: [['q', null, 'Q'], ['w', 'W']] });
  const rows = instrumentKeyRows(piano, keyboard);
  assert.equal(rows[0][1], null);
  assert.equal(rows[0][2].label, 'Q');
  assert.deepEqual(instrumentKeyPairs(piano, keyboard), [[60, 'q'], [61, 'Q'], [72, 'w'], [73, 'W']]);
  assert.deepEqual(instrumentNotes(piano), [60, 61, 72, 73]);
});

test('visual style can be chosen independently of note count or instrument type', () => {
  const notes = Array.from({ length: 15 }, (_, index) => 60 + index);
  assert.equal(instrumentLayoutStyle({ name: 'Custom', type: 'diatonic15', layoutStyle: 'genshin', notes }), 'genshin');
  assert.equal(instrumentLayoutStyle({ name: 'Custom', type: 'chromatic37', layoutStyle: 'sky', notes }), 'sky');
  assert.equal(hasInGameInstrumentLayout({ layoutStyle: 'none' }), false);
  assert.equal(hasInGameInstrumentLayout({ layoutStyle: 'heartopia', type: 'piano88' }), true);
  const legacy = { name: 'Legacy', notes: [60, 61, 62] };
  assert.deepEqual(instrumentKeyPairs(legacy, { keys: ['q', 'Q', 'w'] }), [[60, 'q'], [61, 'Q'], [62, 'w']]);
});

test('short windows keep the final octave on the second row to make rendered keys larger', () => {
  const piano = instrument('Roblox/Instruments/Pianos/Piano88Key.json');
  const slots = createPianoSlots(piano.notes);
  const tallRows = createPianoRows(slots, 640, { height: 420, stageWidth: 720 });
  const shortRows = createPianoRows(slots, 640, { height: 224, stageWidth: 720 });
  assert.equal(tallRows.length, 3);
  assert.equal(shortRows.length, 2);
  assert.equal(shortRows[1][0].white.name, 'C4');
  assert.equal(shortRows[1].at(-1).white.name, 'C8');
  assert.deepEqual(shortRows.flat(), slots);
  const renderedWidth = rows => (720 - 26) / (Math.max(...rows.map(row => row.length)) + 0.28)
    * Math.min(640 / 720, 224 / (24 + rows.length * 116 + (rows.length - 1) * 10));
  assert.ok(renderedWidth(shortRows) > renderedWidth(tallRows) * 1.15);
});

test('height-aware wrapping preserves every note and C boundaries while resizing', () => {
  for (const count of [61, 88]) {
    const piano = instrument(`Roblox/Instruments/Pianos/Piano${count}Key.json`);
    const slots = createPianoSlots(piano.notes);
    for (const width of [320, 640, 960, 1440]) {
      for (const height of [160, 224, 420]) {
        const rows = createPianoRows(slots, width, { height, stageWidth: Math.max(720, width) });
        assert.deepEqual(rows.flat(), slots);
        assert.ok(rows.slice(1).every(row => row[0].white?.isC));
      }
    }
  }
});
