import type { SheetPart } from './pianoSheet';

export interface SheetFragment {
  partIndex: number;
  text: string;
  column: number;
  width: number;
}

export interface SheetRow {
  fragments: SheetFragment[];
  hardBreak: boolean;
}

/** Wrap once when the text or viewport width changes, never on playback ticks. */
export function layoutSheetRows(parts: readonly SheetPart[], columns: number,
  characterWidth: (character: string) => number = () => 1): SheetRow[] {
  const limit = Math.max(1, columns);
  const rows: SheetRow[] = [];
  let row: SheetRow = { fragments: [], hardBreak: false };
  let column = 0;
  const finish = (hardBreak: boolean) => {
    row.hardBreak = hardBreak;
    rows.push(row);
    row = { fragments: [], hardBreak: false };
    column = 0;
  };
  parts.forEach((part, partIndex) => {
    for (const character of part.text) {
      if (character === '\n') { finish(true); continue; }
      let width = character === '\t' ? 8 - column % 8 : characterWidth(character);
      if (column > 0 && column + width > limit + 1e-6) {
        finish(false);
        if (character === '\t') width = 8;
      }
      const previous = row.fragments[row.fragments.length - 1];
      const text = character === '\t' ? ' '.repeat(Math.ceil(width)) : character;
      if (previous?.partIndex === partIndex) {
        previous.text += text;
        previous.width += width;
      } else {
        row.fragments.push({ partIndex, text, column, width });
      }
      column += width;
    }
  });
  rows.push(row);
  return rows;
}

export function sheetVisibleRange(rowCount: number, scrollTop: number, height: number, lineHeight: number) {
  return {
    start: Math.max(0, Math.floor(scrollTop / lineHeight) - 1),
    end: Math.min(rowCount, Math.ceil((scrollTop + height) / lineHeight) + 1),
  };
}

export function sheetAttackMs(part: SheetPart, speed: number): number {
  let attack = 60;
  for (const press of part.presses ?? []) attack = Math.min(attack, (press.endMs - press.startMs) / Math.max(0.1, speed) * 0.2);
  return Math.max(12, attack);
}
