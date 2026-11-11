import type { KeyboardLayoutConfig } from '../types/games.js';
import type { PedalBindings } from '../types/settings.js';

export function resolvePedalBindings(
  layout: KeyboardLayoutConfig,
): PedalBindings {
  return {
    sustainKey: layout.sustainKey ?? null,
    sostenutoKey: layout.sostenutoKey ?? null,
    unaCordaKey: layout.unaCordaKey ?? null,
  };
}

/** Pedals hold one physical key, including a standalone modifier. */
export function pedalKeyFromEvent(event: Pick<KeyboardEvent, 'key' | 'code'>): string | null {
  const { code, key } = event;
  if (/^Key[A-Z]$/.test(code)) return code.slice(3).toLowerCase();
  if (/^Digit[0-9]$/.test(code)) return code.slice(5);
  if (/^Numpad[0-9]$/.test(code) || /^F([1-9]|1[0-9]|2[0-4])$/.test(code)) return code;
  const physicalKeys: Record<string, string> = {
    Minus: '-', Equal: '=', BracketLeft: '[', BracketRight: ']', Backslash: '\\',
    Semicolon: ';', Quote: "'", Comma: ',', Period: '.', Slash: '/', Backquote: '`',
    Space: 'Space', NumpadEnter: 'Enter', NumpadAdd: 'NumpadAdd',
    NumpadSubtract: 'NumpadSubtract', NumpadMultiply: 'NumpadMultiply',
    NumpadDivide: 'NumpadDivide', NumpadDecimal: 'NumpadDecimal',
  };
  if (physicalKeys[code]) return physicalKeys[code];
  if (['Control', 'Alt', 'Shift', 'Meta', 'Enter', 'Tab', 'Backspace', 'Delete',
    'Insert', 'Home', 'End', 'PageUp', 'PageDown', 'ArrowUp', 'ArrowDown',
    'ArrowLeft', 'ArrowRight', 'CapsLock', 'NumLock', 'ScrollLock', 'Pause', 'PrintScreen'].includes(key)) return key;
  return null;
}
