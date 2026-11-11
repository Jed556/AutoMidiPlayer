import test from 'node:test';
import assert from 'node:assert/strict';
import { resolvePedalBindings, pedalKeyFromEvent } from '../src/shared/utils/pedalBindings.ts';

test('pedal bindings use the selected layout fields and preserve cleared keys', () => {
  assert.deepEqual(resolvePedalBindings({ id: 'Piano61', sustainKey: null, sostenutoKey: 'Shift' }), {
    sustainKey: null, sostenutoKey: 'Shift', unaCordaKey: null,
  });
  assert.deepEqual(resolvePedalBindings({ id: 'Piano88', sustainKey: 'Space' }), {
    sustainKey: 'Space', sostenutoKey: null, unaCordaKey: null,
  });
});

test('pedal recording uses physical single keys, including modifiers, punctuation and numpad', () => {
  for (const [code, key, expected] of [
    ['KeyQ', 'Q', 'q'], ['Digit1', '!', '1'], ['Slash', '?', '/'],
    ['Space', ' ', 'Space'], ['ShiftLeft', 'Shift', 'Shift'],
    ['ControlRight', 'Control', 'Control'], ['AltLeft', 'Alt', 'Alt'],
    ['ArrowUp', 'ArrowUp', 'ArrowUp'], ['Numpad0', 'Insert', 'Numpad0'],
    ['NumpadAdd', '+', 'NumpadAdd'], ['F12', 'F12', 'F12'],
  ]) assert.equal(pedalKeyFromEvent({ code, key }), expected, code);
  assert.equal(pedalKeyFromEvent({ code: 'Escape', key: 'Escape' }), null);
  assert.equal(pedalKeyFromEvent({ code: 'Unidentified', key: 'Unidentified' }), null);
});
