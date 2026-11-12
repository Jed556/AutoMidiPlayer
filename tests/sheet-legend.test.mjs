import test from 'node:test';
import assert from 'node:assert/strict';
import { createSheetLegendRows } from '../src/renderer/lib/utils/sheetLegend.ts';
import { instrumentKeyPairs } from '../src/shared/utils/instrumentLayout.ts';

test('legend follows the selected instrument/layout matrix including gaps and modifiers', () => {
  const instrument = { id: 'test', name: 'Test', keys: [[60, null, 61], [72, 127]] };
  const layout = { id: 'layout', name: 'Layout', keys: ['a', 'A', '^1', '!'], keyRows: [['a', null, 'A'], ['^1', '!']] };
  assert.deepEqual(createSheetLegendRows(instrumentKeyPairs(instrument, layout)), [
    { pitch: 60, key: 'a', note: 'C4' },
    { pitch: 61, key: 'A', note: 'C#4' },
    { pitch: 72, key: '^1', note: 'C5' },
    { pitch: 127, key: '!', note: 'G9' },
  ]);
});

test('flat instrument mapping preserves order, named keys and unassigned entries', () => {
  const instrument = { id: 'test', name: 'Test', notes: [0, 60, 62, 64] };
  const layout = { id: 'layout', name: 'Layout', keys: ['Space', ' ', '', 'Enter'] };
  assert.deepEqual(createSheetLegendRows(instrumentKeyPairs(instrument, layout)), [
    { pitch: 0, key: 'Space', note: 'C-1' },
    { pitch: 60, key: ' ', note: 'C4' },
    { pitch: 64, key: 'Enter', note: 'E4' },
  ]);
  assert.deepEqual(createSheetLegendRows(instrumentKeyPairs(null, layout)), []);
  assert.deepEqual(createSheetLegendRows([[-1, 'x'], [128, 'y'], [60.5, 'z'], [60, '']]), []);
});
