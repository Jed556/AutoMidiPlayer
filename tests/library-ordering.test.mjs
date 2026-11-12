import test from 'node:test';
import assert from 'node:assert/strict';
import { replaceVisibleSongOrder, sortLibrarySongs } from '../src/renderer/lib/utils/libraryOrdering.ts';

const songs = [
  { id: 'a', title: 'Alpha', artist: 'Zed', durationSeconds: 20, dateAdded: '2026-01-02' },
  { id: 'b', title: 'Bravo', artist: 'Amy', durationMs: 30000, dateAdded: '2026-01-01' },
  { id: 'c', title: 'Charlie', artist: 'Bob', durationSeconds: 10, dateAdded: '2026-01-03' },
];
const ids = songs => songs.map(song => song.id);

test('library fields sort in both directions without changing backend arrays', () => {
  for (const [mode, ascending] of [
    ['title', ['a', 'b', 'c']], ['artist', ['b', 'c', 'a']],
    ['duration', ['c', 'a', 'b']], ['dateAdded', ['b', 'a', 'c']],
  ]) {
    assert.deepEqual(ids(sortLibrarySongs(songs, mode, 'asc')), ascending);
    assert.deepEqual(ids(sortLibrarySongs(songs, mode, 'desc')), [...ascending].reverse());
  }
  assert.deepEqual(ids(songs), ['a', 'b', 'c']);
});

test('custom order retains saved ranks, appends new songs and ignores sort direction', () => {
  for (const direction of ['asc', 'desc']) {
    assert.deepEqual(ids(sortLibrarySongs(songs, 'custom', direction, ['gone', 'c', 'a'])), ['c', 'a', 'b']);
    assert.deepEqual(ids(sortLibrarySongs(songs, 'custom', direction)), ['a', 'b', 'c']);
  }
});

test('missing metadata and equal sort values preserve source order', () => {
  const equal = [{ id: 'a', title: 'Same', dateAdded: 'bad-date' }, { id: 'b', title: 'Same' }];
  for (const mode of ['title', 'duration', 'dateAdded']) {
    assert.deepEqual(ids(sortLibrarySongs(equal, mode, 'asc')), ['a', 'b']);
  }
});

test('reordering preserves unavailable IDs and appends new visible songs', () => {
  const next = replaceVisibleSongOrder(['a', 'missing', 'b'], ['a', 'b', 'c'], ['c', 'b', 'a']);
  assert.deepEqual(next, ['c', 'missing', 'b', 'a']);
  assert.deepEqual(ids(sortLibrarySongs(songs, 'custom', 'asc', next)), ['c', 'b', 'a']);
});

test('invalid reorders cannot duplicate, omit or introduce songs', () => {
  for (const invalid of [['a', 'a', 'c'], ['a', 'b'], ['a', 'b', 'foreign']]) {
    assert.throws(() => replaceVisibleSongOrder([], ['a', 'b', 'c'], invalid), /must match/);
  }
});
