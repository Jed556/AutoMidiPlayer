import test from 'node:test';
import assert from 'node:assert/strict';
import { createJsonDiff } from '../src/renderer/lib/utils/jsonDiff.ts';

const reconstruct = (diff, side) => diff.rows.flatMap(row => row[side] ? [row[side].text] : []).join('\n');
function assertLineNumbers(diff) {
  for (const side of ['left', 'right']) {
    assert.deepEqual(diff.rows.flatMap(row => row[side] ? [row[side].lineNumber] : []),
      Array.from({ length: diff.rows.filter(row => row[side]).length }, (_, index) => index + 1));
    for (const row of diff.rows) {
      if (row[side]) assert.equal(row[side].parts.map(part => part.text).join(''), row[side].text);
    }
  }
}

test('JSON formatting and nested object property order produce no changes', () => {
  const diff = createJsonDiff('{"z":2,"a":{"y":1,"x":0}}', '{\n"a":{"x":0,"y":1},"z":2\n}');
  assert.deepEqual(diff.changeStarts, []);
  assert.equal(diff.added + diff.removed + diff.modified, 0);
  assert.equal(diff.invalidLeft, false);
  assert.equal(diff.invalidRight, false);
  assert.equal(reconstruct(diff, 'left'), reconstruct(diff, 'right'));
  assert.ok(diff.rows.every(row => row.kind === 'equal'));
  assertLineNumbers(diff);
});

test('nested field updates align changed values and highlight only their differing characters', () => {
  const diff = createJsonDiff('{"nested":{"binding":"KeyA"},"untouched":true}', '{"nested":{"binding":"KeyB"},"untouched":true}');
  assert.equal(diff.modified, 1);
  assert.equal(diff.added, 0);
  assert.equal(diff.removed, 0);
  assert.equal(diff.changeStarts.length, 1);
  const row = diff.rows.find(row => row.kind === 'modified');
  assert.deepEqual(row.left.parts.filter(part => part.changed), [{ text: 'A', changed: true }]);
  assert.deepEqual(row.right.parts.filter(part => part.changed), [{ text: 'B', changed: true }]);
  assertLineNumbers(diff);
});

test('added and removed object fields keep genuine empty cells and reconstruct each complete JSON', () => {
  const before = '{"a":1,"b":2,"c":3,"z":9}';
  const after = '{"a":1,"c":3,"d":4,"e":5,"z":9}';
  const diff = createJsonDiff(before, after);
  assert.deepEqual(JSON.parse(reconstruct(diff, 'left')), JSON.parse(before));
  assert.deepEqual(JSON.parse(reconstruct(diff, 'right')), JSON.parse(after));
  assert.ok(diff.rows.some(row => row.kind === 'removed' && row.right === null));
  assert.ok(diff.rows.some(row => row.kind === 'added' && row.left === null));
  assertLineNumbers(diff);
});

test('array insertions, removals, and reordered values remain visible rather than being sorted away', () => {
  for (const [before, after] of [
    ['[1,2,3]', '[1,9,2,3]'], ['[1,9,2,3]', '[1,2,3]'], ['[1,2,3]', '[3,2,1]'],
  ]) {
    const diff = createJsonDiff(before, after);
    assert.ok(diff.changeStarts.length > 0);
    assert.deepEqual(JSON.parse(reconstruct(diff, 'left')), JSON.parse(before));
    assert.deepEqual(JSON.parse(reconstruct(diff, 'right')), JSON.parse(after));
    assertLineNumbers(diff);
  }
});

test('separated edits expose separate navigation groups and unchanged closing lines remain aligned', () => {
  const diff = createJsonDiff('{"a":1,"b":2,"c":3,"d":4,"e":5}', '{"a":9,"b":2,"c":3,"d":4,"e":8}');
  assert.equal(diff.changeStarts.length, 2);
  assert.equal(diff.modified, 2);
  assert.equal(diff.rows.at(-1).kind, 'equal');
  for (const index of diff.changeStarts) {
    assert.notEqual(diff.rows[index].kind, 'equal');
    if (index > 0) assert.equal(diff.rows[index - 1].kind, 'equal');
  }
});

test('malformed JSON preserves original text including empty lines and flags only the invalid side', () => {
  const before = '{invalid\n\n  "keys": [\n';
  const after = '{"keys": []}';
  const diff = createJsonDiff(before, after);
  assert.equal(diff.invalidLeft, true);
  assert.equal(diff.invalidRight, false);
  assert.equal(reconstruct(diff, 'left'), before);
  assert.deepEqual(JSON.parse(reconstruct(diff, 'right')), JSON.parse(after));
  assertLineNumbers(diff);
});

test('markup and Unicode remain plain text and highlighting does not split surrogate pairs', () => {
  const before = JSON.stringify({ html: '<script>alert("x")</script>', symbol: 'a😀b' });
  const after = JSON.stringify({ html: '<img onerror="alert(1)">', symbol: 'a😃b' });
  const diff = createJsonDiff(before, after);
  assert.deepEqual(JSON.parse(reconstruct(diff, 'left')), JSON.parse(before));
  assert.deepEqual(JSON.parse(reconstruct(diff, 'right')), JSON.parse(after));
  const row = diff.rows.find(row => row.left?.text.includes('symbol'));
  assert.deepEqual(row.left.parts.filter(part => part.changed), [{ text: '😀', changed: true }]);
  assert.deepEqual(row.right.parts.filter(part => part.changed), [{ text: '😃', changed: true }]);
  assert.ok(diff.rows.some(row => row.right?.text.includes('<img onerror=')));
  assertLineNumbers(diff);
});

test('large comparisons use bounded work while preserving all source lines and nearby insertion anchors', () => {
  const left = Array.from({ length: 6000 }, (_, index) => `before ${index}`).join('\n');
  const right = Array.from({ length: 6000 }, (_, index) => `after ${index}`).join('\n');
  const diff = createJsonDiff(left, right);
  assert.equal(diff.modified, 6000);
  assert.equal(reconstruct(diff, 'left'), left);
  assert.equal(reconstruct(diff, 'right'), right);
  assertLineNumbers(diff);
  const anchorLeft = Array.from({ length: 2500 }, (_, index) => `${index}: unchanged`).join('\n');
  const anchorRight = `new first line\n${anchorLeft}\nnew last line`;
  const anchored = createJsonDiff(anchorLeft, anchorRight);
  assert.equal(anchored.added, 2);
  assert.equal(anchored.modified, 0);
  assert.equal(reconstruct(anchored, 'left'), anchorLeft);
  assert.equal(reconstruct(anchored, 'right'), anchorRight);
  assertLineNumbers(anchored);
});

test('empty sources and JSON primitives remain valid, numbered comparisons', () => {
  for (const [before, after] of [['', ''], ['null', 'null'], ['1', '2'], ['"a"', '"b"']]) {
    const diff = createJsonDiff(before, after);
    assert.ok(diff.rows.length > 0);
    assertLineNumbers(diff);
  }
});

test('unsafe integer and decimal differences retain their exact numeric lexemes', () => {
  for (const [left, right] of [
    ['9007199254740992', '9007199254740993'],
    ['0.123456789012345678901', '0.123456789012345678902'],
    ['1.0000000000000001', '1.0000000000000002'],
    ['9999999999999999', '10000000000000000'],
  ]) {
    const before = `{"number":${left}}`;
    const after = `{"number":${right}}`;
    const diff = createJsonDiff(before, after);
    assert.equal(reconstruct(diff, 'left'), before);
    assert.equal(reconstruct(diff, 'right'), after);
    assert.ok(diff.changeStarts.length > 0);
    assert.equal(diff.invalidLeft, false);
    assert.equal(diff.invalidRight, false);
    assertLineNumbers(diff);
  }
});

test('overflowing and underflowing exponents remain valid JSON text rather than null or zero', () => {
  for (const [before, after] of [
    ['{"number":1e999}', '{"number":2e999}'],
    ['{"number":-1e999}', '{"number":-2e999}'],
    ['{"number":1e-999}', '{"number":2e-999}'],
    ['{"number":3e-324}', '{"number":4e-324}'],
  ]) {
    const diff = createJsonDiff(before, after);
    assert.equal(reconstruct(diff, 'left'), before);
    assert.equal(reconstruct(diff, 'right'), after);
    assert.ok(diff.changeStarts.length > 0);
    assert.equal(diff.invalidLeft, false);
    assert.equal(diff.invalidRight, false);
  }
});

test('negative zero remains distinguishable from positive zero', () => {
  for (const before of ['{"number":-0}', '{"number":-0.0}', '{"number":-0e2}']) {
    const after = '{"number":0}';
    const diff = createJsonDiff(before, after);
    assert.equal(reconstruct(diff, 'left'), before);
    assert.equal(reconstruct(diff, 'right'), after);
    assert.ok(diff.changeStarts.length > 0);
    assert.equal(diff.invalidLeft, false);
    assert.equal(diff.invalidRight, false);
  }
});

test('unsafe numbers force original formatting on both sides, including an otherwise safe opposite side', () => {
  const unsafe = '{ "z": 9007199254740993, "a": 1 }';
  const ordinary = '{ "z": 12, "a": 1 }';
  for (const [before, after] of [[unsafe, ordinary], [ordinary, unsafe]]) {
    const diff = createJsonDiff(before, after);
    assert.equal(reconstruct(diff, 'left'), before);
    assert.equal(reconstruct(diff, 'right'), after);
    assert.equal(diff.invalidLeft, false);
    assert.equal(diff.invalidRight, false);
  }
  const malformed = '{ "number": 9007199254740993';
  const diff = createJsonDiff(unsafe, malformed);
  assert.equal(reconstruct(diff, 'left'), unsafe);
  assert.equal(reconstruct(diff, 'right'), malformed);
  assert.equal(diff.invalidLeft, false);
  assert.equal(diff.invalidRight, true);
});

test('numeric-looking strings and escaped quotes do not disable canonical JSON formatting', () => {
  const value = { z: '1e999, 9007199254740993, 0.123456789012345678901',
    a: '\\" a quoted \\"number\\": 1e999', b: 0.125 };
  const before = JSON.stringify(value);
  const after = JSON.stringify({ b: 0.125, a: value.a, z: value.z }, null, 2);
  const diff = createJsonDiff(before, after);
  assert.deepEqual(diff.changeStarts, []);
  assert.equal(reconstruct(diff, 'left'), reconstruct(diff, 'right'));
  assert.equal(diff.invalidLeft, false);
  assert.equal(diff.invalidRight, false);
});

test('ordinary numeric formatting and insignificant trailing zeroes remain canonical', () => {
  const diff = createJsonDiff('{"z":1.230000000000000000,"a":2e2,"b":0.000125}',
    '{"b":1.25e-4,"a":200,"z":1.23}');
  assert.deepEqual(diff.changeStarts, []);
  assertLineNumbers(diff);
});

test('valid JSON exceeding formatting recursion limits falls back without a syntax warning', () => {
  const before = '['.repeat(6000) + '0' + ']'.repeat(6000);
  const after = '['.repeat(6000) + '1' + ']'.repeat(6000);
  const diff = createJsonDiff(before, after);
  assert.equal(reconstruct(diff, 'left'), before);
  assert.equal(reconstruct(diff, 'right'), after);
  assert.equal(diff.invalidLeft, false);
  assert.equal(diff.invalidRight, false);
  assert.ok(diff.changeStarts.length > 0);
});
