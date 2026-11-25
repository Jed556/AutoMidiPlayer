import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { stripTypeScriptTypes } from 'node:module';

const source = stripTypeScriptTypes(readFileSync(new URL('../src/renderer/lib/utils/instrumentInput.ts', import.meta.url), 'utf8'), { mode: 'transform' });
const {
  InstrumentInput,
  chordPadPitches,
  isMouseBinding,
  keyboardPitches,
  matchesKeyBinding,
  resolveGuitarKeyAction,
} = await import(`data:text/javascript;base64,${Buffer.from(source).toString('base64')}`);

test('keyboard follows the selected bindings, including shifted notes and named keys', () => {
  const bindings = [{ label: 'q', pitch: 60 }, { label: 'Q', pitch: 61 },
    { label: '!', pitch: 62 }, { label: 'Enter', pitch: 63 }];
  assert.deepEqual(keyboardPitches('q', bindings), [60]);
  assert.deepEqual(keyboardPitches('Q', bindings), [61]);
  assert.deepEqual(keyboardPitches('!', bindings), [62]);
  assert.deepEqual(keyboardPitches('Enter', bindings), [63]);
  assert.deepEqual(keyboardPitches('a', [{ label: 'A', pitch: 72 }]), [72]);
  assert.deepEqual(keyboardPitches('z', bindings), []);
});

test('held notes survive repeat and overlapping keyboard/MIDI sources until each releases', () => {
  let pitches;
  const input = new InstrumentInput(next => { pitches = next; });
  input.press('keyboard:KeyQ', [60]);
  assert.equal(input.press('keyboard:KeyQ', [61]), false);
  assert.equal(input.midi([0x90, 60, 100]), 60);
  input.release('keyboard:KeyQ');
  assert.deepEqual([...pitches], [60]);
  input.midi([0x80, 60, 64]);
  assert.equal(pitches.size, 0);
});

test('MIDI handles zero velocity note-off, channels, panic messages, and malformed data', () => {
  let pitches;
  const input = new InstrumentInput(next => { pitches = next; });
  input.midi([0x90, 60, 100]);
  input.midi([0x91, 60, 100]);
  input.midi([0x90, 60, 0]);
  assert.deepEqual([...pitches], [60]);
  input.midi([0xb1, 123, 0]);
  assert.equal(pitches.size, 0);
  input.midi([0x90, 62, 100]);
  input.midi([0xb0, 120, 0]);
  assert.equal(pitches.size, 0);
  assert.equal(input.midi([0xf8]), null);
});

test('device changes clear MIDI only; blur or navigation clears all held input', () => {
  let pitches;
  const input = new InstrumentInput(next => { pitches = next; });
  input.press('keyboard:KeyQ', [60]);
  input.midi([0x90, 62, 100]);
  input.clear('midi:');
  assert.deepEqual([...pitches], [60]);
  input.clear();
  assert.equal(pitches.size, 0);
});

test('guitar keyboard resolver maps strings, keyboard strums, pages, mode key, and arrow guards', () => {
  const nteConfig = {
    stringKeys: ['6', '5', '4', '3', '2', '1'],
    strumButtons: { up: 'mouseLeft', down: 'mouseRight' },
    pageKeys: ['r', 't'],
    modeKey: 'z',
    timbreKey: 'v',
    allowPageKeys: true,
    allowModeKey: true,
    allowTimbreKey: true,
    allowArrows: true,
    allowWedgeArrows: true,
  };
  assert.equal(isMouseBinding('mouseLeft'), true);
  assert.equal(isMouseBinding('RMB'), true);
  assert.equal(isMouseBinding('r'), false);
  assert.equal(matchesKeyBinding(' ', 'space'), true);
  assert.equal(matchesKeyBinding('R', 'r'), true);
  assert.equal(matchesKeyBinding('mouseLeft', 'mouseLeft'), false);

  assert.deepEqual(resolveGuitarKeyAction('6', nteConfig), { type: 'string', index: 0 });
  assert.deepEqual(resolveGuitarKeyAction('1', nteConfig), { type: 'string', index: 5 });
  assert.deepEqual(resolveGuitarKeyAction('R', nteConfig), { type: 'page', delta: -1 });
  assert.deepEqual(resolveGuitarKeyAction('t', nteConfig), { type: 'page', delta: 1 });
  assert.deepEqual(resolveGuitarKeyAction('Z', nteConfig), { type: 'mode' });
  assert.deepEqual(resolveGuitarKeyAction('v', nteConfig), { type: 'timbre' });
  assert.deepEqual(resolveGuitarKeyAction('V', nteConfig), { type: 'timbre' });
  assert.deepEqual(resolveGuitarKeyAction('ArrowUp', nteConfig), { type: 'strum', direction: 'up' });
  assert.deepEqual(resolveGuitarKeyAction('ArrowDown', nteConfig), { type: 'strum', direction: 'down' });
  assert.deepEqual(resolveGuitarKeyAction('ArrowLeft', nteConfig), { type: 'wedge', delta: -1 });
  assert.deepEqual(resolveGuitarKeyAction('ArrowRight', nteConfig), { type: 'wedge', delta: 1 });

  assert.deepEqual(
    resolveGuitarKeyAction('u', { ...nteConfig, strumButtons: { up: 'u', down: 'j' } }),
    { type: 'strum', direction: 'up' },
  );
  assert.deepEqual(
    resolveGuitarKeyAction(' ', { ...nteConfig, strumButtons: { up: 'space', down: 'j' } }),
    { type: 'strum', direction: 'up' },
  );
  assert.equal(resolveGuitarKeyAction('r', { ...nteConfig, allowPageKeys: false }), null);
  assert.equal(resolveGuitarKeyAction('z', { ...nteConfig, allowModeKey: false }), null);
  assert.equal(resolveGuitarKeyAction('v', { ...nteConfig, allowTimbreKey: false }), null);
  assert.equal(resolveGuitarKeyAction('ArrowUp', { ...nteConfig, allowArrows: false }), null);
  assert.equal(resolveGuitarKeyAction('ArrowLeft', { ...nteConfig, allowWedgeArrows: false }), null);
});

test('chord pad keyboard lookup resolves string voicings or falls back to pitch classes', () => {
  const chords = [
    { name: 'C', pitchClasses: [0, 4, 7], stringPitches: [64, 60, 55, 52, 48, 'muted'] },
    { name: 'Dm', pitchClasses: [2, 5, 9] },
  ];
  assert.deepEqual(chordPadPitches('Q', chords, ['q', 'w']), [64, 60, 55, 52, 48]);
  assert.deepEqual(chordPadPitches('w', chords, ['q', 'w']), [62, 65, 69]);
  assert.deepEqual(chordPadPitches('e', chords, ['q', 'w']), []);
});
