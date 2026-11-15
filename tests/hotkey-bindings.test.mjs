import test from 'node:test';
import assert from 'node:assert/strict';
import { hotkeyBindingFromEvent } from '../src/shared/utils/hotkeyBindings.ts';

const event = (key, code, modifiers = {}) => ({ key, code, ctrlKey: false, altKey: false,
  shiftKey: false, metaKey: false, ...modifiers });

test('Command shortcuts retain Meta and physical codes across keyboard layouts', () => {
  assert.deepEqual(hotkeyBindingFromEvent(event('q', 'KeyA', { metaKey: true })),
    { key: 'KeyA', ctrl: false, alt: false, shift: false, meta: true });
});

test('Shift and Option punctuation never become ambiguous shortcut separators', () => {
  for (const [key, code] of [['+', 'Equal'], ['?', 'Slash'], ['å', 'KeyA'], ['!', 'Digit1']]) {
    assert.equal(hotkeyBindingFromEvent(event(key, code, { altKey: true, shiftKey: true })).key, code);
  }
});

test('modifiers and composition events do not commit a shortcut', () => {
  for (const key of ['Meta', 'Alt', 'Shift', 'Control', 'Dead', 'Process', 'Unidentified']) {
    assert.equal(hotkeyBindingFromEvent(event(key, 'AltRight')), null);
  }
  assert.equal(hotkeyBindingFromEvent(event(' ', '')).key, 'Space');
  assert.equal(hotkeyBindingFromEvent(event('Enter', 'NumpadEnter')).key, 'NumpadEnter');
});
