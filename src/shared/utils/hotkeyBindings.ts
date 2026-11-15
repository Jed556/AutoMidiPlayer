import type { KeyBinding } from '../types/settings.js';

/** Global shortcuts use physical key codes so Shift/Option do not change the key name. */
export function hotkeyBindingFromEvent(
  event: Pick<KeyboardEvent, 'key' | 'code' | 'ctrlKey' | 'altKey' | 'shiftKey' | 'metaKey'>,
): KeyBinding | null {
  if (['Control', 'Alt', 'Shift', 'Meta', 'Dead', 'Process', 'Unidentified'].includes(event.key)) return null;
  const key = event.code || (event.key === ' ' ? 'Space' : event.key);
  if (!key) return null;
  return { key, ctrl: event.ctrlKey, alt: event.altKey, shift: event.shiftKey, meta: event.metaKey };
}
