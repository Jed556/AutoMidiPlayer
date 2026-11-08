import test from 'node:test';
import assert from 'node:assert/strict';
import { layoutSheetRows, sheetVisibleRange, sheetAttackMs } from '../src/renderer/lib/utils/sheetViewport.ts';

const part = text => ({ text, delimiter: false });
const rowText = row => row.fragments.map(fragment => fragment.text).join('');

test('visual wrapping preserves hard breaks and source token identities', () => {
  const parts = [part('Header\n\n'), part('[abc]'), part('··'), part('d')];
  const rows = layoutSheetRows(parts, 4);
  assert.deepEqual(rows.map(rowText), ['Head', 'er', '', '[abc', ']··d']);
  assert.equal(rows.map(row => rowText(row) + (row.hardBreak ? '\n' : '')).join(''), parts.map(part => part.text).join(''));
  assert.deepEqual(rows[4].fragments.map(fragment => [fragment.partIndex, fragment.column, fragment.width]), [[1, 0, 1], [2, 1, 2], [3, 3, 1]]);
});

test('wide characters and surrogate pairs wrap using measured glyph widths', () => {
  const rows = layoutSheetRows([part('A黑喂B🙂C')], 4, character => /[黑喂🙂]/u.test(character) ? 2 : 1);
  assert.deepEqual(rows.map(rowText), ['A黑', '喂B', '🙂C']);
  assert.equal(rows.map(rowText).join(''), 'A黑喂B🙂C');
});

test('trailing hard breaks retain the native textarea final empty line', () => {
  assert.deepEqual(layoutSheetRows([part('abcd\n')], 4).map(rowText), ['abcd', '']);
  assert.deepEqual(layoutSheetRows([], 4).map(rowText), ['']);
});

test('tabs are expanded at the start of their wrapped row', () => {
  const rows = layoutSheetRows([part('123456789\tx')], 10);
  assert.deepEqual(rows.map(rowText), ['123456789', '        x']);
});

test('the drawn range stays bounded by viewport height, regardless of total sheet size', () => {
  const range = sheetVisibleRange(1_000_000, 20.8 * 500_000, 416, 20.8);
  assert.equal(range.start, 499_999);
  assert.ok(range.end - range.start <= 23);
  assert.deepEqual(sheetVisibleRange(3, 0, 416, 20.8), { start: 0, end: 3 });
});

test('attack easing follows wall-clock note duration and caps very short or held notes', () => {
  const press = endMs => ({ startMs: 0, endMs, pitch: 60, trackIndex: 0 });
  assert.equal(sheetAttackMs({ ...part('a'), presses: [press(300)] }, 2), 30);
  assert.equal(sheetAttackMs({ ...part('a'), presses: [press(20)] }, 1), 12);
  assert.equal(sheetAttackMs({ ...part('a'), presses: [press(2000)] }, 1), 60);
});
