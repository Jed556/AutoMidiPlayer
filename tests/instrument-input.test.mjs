import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { stripTypeScriptTypes } from 'node:module';

const source = stripTypeScriptTypes(readFileSync(new URL('../src/renderer/lib/utils/instrumentInput.ts', import.meta.url), 'utf8'), { mode: 'transform' });
const { InstrumentInput, keyboardPitches } = await import(`data:text/javascript;base64,${Buffer.from(source).toString('base64')}`);

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
