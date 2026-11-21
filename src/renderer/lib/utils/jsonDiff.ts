export interface DiffPart {
  text: string;
  changed: boolean;
}

export interface DiffLine {
  text: string;
  lineNumber: number;
  parts: DiffPart[];
}

export interface JsonDiffRow {
  kind: 'equal' | 'added' | 'removed' | 'modified';
  left: DiffLine | null;
  right: DiffLine | null;
}

export interface JsonDiffResult {
  rows: JsonDiffRow[];
  /** Start row of each contiguous group of changed lines. */
  changeStarts: number[];
  added: number;
  removed: number;
  modified: number;
  invalidLeft: boolean;
  invalidRight: boolean;
}

interface SourceLines {
  lines: string[];
  invalid: boolean;
  requiresRaw: boolean;
}

type Edit = { kind: 'equal' | 'added' | 'removed'; text: string };

// The full dynamic-programming table never exceeds four megabytes. Larger
// comparisons use a bounded search rather than quadratic memory or CPU work.
const MAX_LCS_CELLS = 1_000_000;
const LOOKAHEAD = 48;

function hasUnsafeNumbers(source: string): boolean {
  const numberToken = /-?(?:0|[1-9]\d*)(?:\.\d+)?(?:[eE][+-]?\d+)?/y;
  for (let index = 0; index < source.length;) {
    if (source[index] === '"') {
      // JSON keys and string contents are text even if they look like numbers.
      index++;
      while (index < source.length) {
        if (source[index] === '\\') index += 2;
        else if (source[index++] === '"') break;
      }
    } else if (source[index] === '-' || /[0-9]/.test(source[index])) {
      numberToken.lastIndex = index;
      const token = numberToken.exec(source)?.[0];
      if (!token) { index++; continue; }
      const value = Number(token);
      const significant = token.split(/[eE]/)[0].replace(/[-.]/g, '').replace(/^0+|0+$/g, '');
      // JSON.parse/stringify can silently round distinct lexemes to the same
      // number, or turn overflowing exponents into null. Fifteen significant
      // decimal digits are a conservative bound for canonical formatting.
      if (!Number.isFinite(value) || Object.is(value, -0) || significant.length > 15
        || (Number.isInteger(value) && !Number.isSafeInteger(value))
        || (value === 0 && significant.length > 0)
        || (value !== 0 && Math.abs(value) < 2.2250738585072014e-308)) return true;
      index = numberToken.lastIndex;
    } else index++;
  }
  return false;
}

function sourceLines(source: string): SourceLines {
  let value: unknown;
  try {
    value = JSON.parse(source);
  } catch {
    return { lines: source.split('\n'), invalid: true, requiresRaw: false };
  }
  if (hasUnsafeNumbers(source)) return { lines: source.split('\n'), invalid: false, requiresRaw: true };
  try {
    const canonical = JSON.stringify(value, (_key, item: unknown) => {
      if (!item || typeof item !== 'object' || Array.isArray(item)) return item;
      const object = item as Record<string, unknown>;
      return Object.fromEntries(Object.keys(object).sort().map(key => [key, object[key]]));
    }, 2);
    return { lines: canonical.split('\n'), invalid: false, requiresRaw: false };
  } catch {
    // Valid deeply nested JSON may exceed stringify's recursion limit. Keep it
    // intact rather than incorrectly reporting a syntax error.
    return { lines: source.split('\n'), invalid: false, requiresRaw: true };
  }
}

function boundedEdits(left: string[], right: string[]): Edit[] {
  const edits: Edit[] = [];
  let i = 0;
  let j = 0;
  while (i < left.length && j < right.length) {
    if (left[i] === right[j]) {
      edits.push({ kind: 'equal', text: left[i++] });
      j++;
      continue;
    }
    let addedDistance = 0;
    let removedDistance = 0;
    for (let distance = 1; distance <= LOOKAHEAD; distance++) {
      if (!addedDistance && j + distance < right.length && left[i] === right[j + distance]) addedDistance = distance;
      if (!removedDistance && i + distance < left.length && left[i + distance] === right[j]) removedDistance = distance;
      if (addedDistance || removedDistance) break;
    }
    if (addedDistance && (!removedDistance || addedDistance < removedDistance)) {
      for (let count = 0; count < addedDistance; count++) edits.push({ kind: 'added', text: right[j++] });
    } else if (removedDistance) {
      for (let count = 0; count < removedDistance; count++) edits.push({ kind: 'removed', text: left[i++] });
    } else {
      edits.push({ kind: 'removed', text: left[i++] }, { kind: 'added', text: right[j++] });
    }
  }
  while (i < left.length) edits.push({ kind: 'removed', text: left[i++] });
  while (j < right.length) edits.push({ kind: 'added', text: right[j++] });
  return edits;
}

function middleEdits(left: string[], right: string[]): Edit[] {
  const width = right.length + 1;
  if ((left.length + 1) * width > MAX_LCS_CELLS) return boundedEdits(left, right);
  const lengths = new Uint32Array((left.length + 1) * width);
  for (let i = left.length - 1; i >= 0; i--) {
    for (let j = right.length - 1; j >= 0; j--) {
      lengths[i * width + j] = left[i] === right[j]
        ? lengths[(i + 1) * width + j + 1] + 1
        : Math.max(lengths[(i + 1) * width + j], lengths[i * width + j + 1]);
    }
  }
  const edits: Edit[] = [];
  let i = 0;
  let j = 0;
  while (i < left.length && j < right.length) {
    if (left[i] === right[j]) {
      edits.push({ kind: 'equal', text: left[i++] });
      j++;
    } else if (lengths[(i + 1) * width + j] >= lengths[i * width + j + 1]) {
      edits.push({ kind: 'removed', text: left[i++] });
    } else {
      edits.push({ kind: 'added', text: right[j++] });
    }
  }
  while (i < left.length) edits.push({ kind: 'removed', text: left[i++] });
  while (j < right.length) edits.push({ kind: 'added', text: right[j++] });
  return edits;
}

function lineEdits(left: string[], right: string[]): Edit[] {
  let prefix = 0;
  while (prefix < left.length && prefix < right.length && left[prefix] === right[prefix]) prefix++;
  let suffix = 0;
  while (suffix < left.length - prefix && suffix < right.length - prefix
    && left[left.length - 1 - suffix] === right[right.length - 1 - suffix]) suffix++;
  return [
    ...left.slice(0, prefix).map(text => ({ kind: 'equal' as const, text })),
    ...middleEdits(left.slice(prefix, left.length - suffix), right.slice(prefix, right.length - suffix)),
    ...left.slice(left.length - suffix).map(text => ({ kind: 'equal' as const, text })),
  ];
}

function changedParts(left: string, right: string): [DiffPart[], DiffPart[]] {
  // Compare code points so highlighting never splits a surrogate pair.
  const before = Array.from(left);
  const after = Array.from(right);
  let prefix = 0;
  while (prefix < before.length && prefix < after.length && before[prefix] === after[prefix]) prefix++;
  let suffix = 0;
  while (suffix < before.length - prefix && suffix < after.length - prefix
    && before[before.length - 1 - suffix] === after[after.length - 1 - suffix]) suffix++;
  const parts = (characters: string[]): DiffPart[] => [
    { text: characters.slice(0, prefix).join(''), changed: false },
    { text: characters.slice(prefix, characters.length - suffix).join(''), changed: true },
    { text: suffix ? characters.slice(-suffix).join('') : '', changed: false },
  ].filter(part => part.text.length > 0);
  return [parts(before), parts(after)];
}

/** Read-only aligned JSON diff; source text is always rendered as text, never HTML. */
export function createJsonDiff(leftSource: string, rightSource: string): JsonDiffResult {
  const leftSourceLines = sourceLines(leftSource);
  const rightSourceLines = sourceLines(rightSource);
  // Compare both originals if either cannot be canonicalized faithfully. Mixing
  // raw and formatted text would otherwise invent unrelated formatting edits.
  const raw = leftSourceLines.requiresRaw || rightSourceLines.requiresRaw;
  const edits = lineEdits(raw ? leftSource.split('\n') : leftSourceLines.lines,
    raw ? rightSource.split('\n') : rightSourceLines.lines);
  const rows: JsonDiffRow[] = [];
  let leftNumber = 1;
  let rightNumber = 1;
  let removed: string[] = [];
  let added: string[] = [];
  const line = (text: string, lineNumber: number, changed: boolean): DiffLine => ({ text, lineNumber, parts: [{ text, changed }] });
  function flushChanges() {
    for (let index = 0; index < Math.max(removed.length, added.length); index++) {
      const before = removed[index];
      const after = added[index];
      const left = before === undefined ? null : line(before, leftNumber++, true);
      const right = after === undefined ? null : line(after, rightNumber++, true);
      if (left && right) [left.parts, right.parts] = changedParts(left.text, right.text);
      rows.push({ kind: left && right ? 'modified' : left ? 'removed' : 'added', left, right });
    }
    removed = [];
    added = [];
  }
  for (const edit of edits) {
    if (edit.kind === 'removed') removed.push(edit.text);
    else if (edit.kind === 'added') added.push(edit.text);
    else {
      flushChanges();
      rows.push({ kind: 'equal', left: line(edit.text, leftNumber++, false), right: line(edit.text, rightNumber++, false) });
    }
  }
  flushChanges();
  const changeStarts: number[] = [];
  let addedCount = 0;
  let removedCount = 0;
  let modifiedCount = 0;
  rows.forEach((row, index) => {
    if (row.kind !== 'equal' && (index === 0 || rows[index - 1].kind === 'equal')) changeStarts.push(index);
    if (row.kind === 'added') addedCount++;
    else if (row.kind === 'removed') removedCount++;
    else if (row.kind === 'modified') modifiedCount++;
  });
  return {
    rows, changeStarts, added: addedCount, removed: removedCount, modified: modifiedCount,
    invalidLeft: leftSourceLines.invalid, invalidRight: rightSourceLines.invalid,
  };
}
