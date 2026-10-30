import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { stripTypeScriptTypes } from 'node:module';
import { getMarkerNoteDisplay } from '../src/renderer/lib/utils/musicConstants.ts';

// Exercise the real storage/native mappers with their runtime dependencies.
const source = stripTypeScriptTypes(readFileSync(new URL('../src/renderer/lib/backend/mappers.ts', import.meta.url), 'utf8'))
  .replace('../../../shared/defaults/settings.js', new URL('../src/shared/defaults/settings.ts', import.meta.url).href)
  .replace('../../../shared/utils/instrumentLayout.js', new URL('../src/shared/utils/instrumentLayout.ts', import.meta.url).href)
  .replace('../utils/musicConstants.js', new URL('../src/renderer/lib/utils/musicConstants.ts', import.meta.url).href);
const { songRecordToMidiFile, loadedMidiToMidiFile } = await import(`data:text/javascript;base64,${Buffer.from(source).toString('base64')}`);

test('legacy marker labels render the numeric offset once and retain the saved metadata', () => {
  for (const [offset, label, note, signed] of [
    [-26, 'A♯0 (-26)', 'A♯0', '-26'],
    [14, 'D4 (+14)', 'D4', '+14'],
    [0, 'C3 (+0)', 'C3', '0'],
    [-26, 'A♯0 (-26) (-26)', 'A♯0', '-26'],
  ]) {
    const stored = { TimeMs: 183013, KeyOffset: offset, Transpose: 2, Label: label };
    const song = songRecordToMidiFile({ id: 'song', path: 'song.mid', key: 0, transpose: 1,
      keyChanges: JSON.stringify([stored]) });
    for (const marker of [song.keyMarkers[0], loadedMidiToMidiFile(song).keyMarkers[0]]) {
      assert.equal(marker.noteDisplay, note);
      assert.equal(marker.key, note);
      assert.equal(`${marker.noteDisplay} (${marker.offsetDisplay})`, `${note} (${signed})`);
      assert.equal(marker.label, label);
      assert.equal(marker.keyOffset, offset);
      assert.equal(marker.timeMs, stored.TimeMs);
      assert.equal(marker.transpose, stored.Transpose);
    }
  }
});

test('plain notes and custom labels survive normalization and repeated loading', () => {
  for (const label of ['A♯0', 'Chorus', 'D maj', 'Verse (2)', 'Chorus (-26)']) {
    const display = getMarkerNoteDisplay(-26, label);
    assert.equal(display, label === 'Chorus (-26)' ? 'Chorus' : label);
    assert.equal(getMarkerNoteDisplay(-26, display), display);
  }
  assert.equal(getMarkerNoteDisplay(-26), 'A#');
});
