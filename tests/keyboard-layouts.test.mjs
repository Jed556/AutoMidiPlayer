import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, readdirSync } from 'node:fs';
import { stripTypeScriptTypes } from 'node:module';
import { normalizeKeyboardLayout, instrumentKeyPairs } from '../src/shared/utils/instrumentLayout.ts';

const sharedLayoutIds = ['QWERTY_15Keys', 'QWERTY_21Keys', 'QWERTY_36Keys', 'QWERTY_61Keys', 'QWERTY_88Keys'];

const defaults = new URL('../src-tauri/resources/defaults/', import.meta.url);
const read = path => JSON.parse(readFileSync(new URL(path, defaults), 'utf8'));
const layoutFiles = readdirSync(new URL('KeyboardLayouts/', defaults));
const layouts = layoutFiles.map(file => normalizeKeyboardLayout(read(`KeyboardLayouts/${file}`)));
const source = stripTypeScriptTypes(readFileSync(new URL('../src/renderer/lib/backend/mappers.ts', import.meta.url), 'utf8'))
  .replace('../../../shared/defaults/settings.js', new URL('../src/shared/defaults/settings.ts', import.meta.url).href)
  .replace('../../../shared/utils/instrumentLayout.js', new URL('../src/shared/utils/instrumentLayout.ts', import.meta.url).href)
  .replace('../utils/musicConstants.js', new URL('../src/renderer/lib/utils/musicConstants.ts', import.meta.url).href);
const { loadedBundleToGameInfo } = await import(`data:text/javascript;base64,${Buffer.from(source).toString('base64')}`);

test('shared layouts preserve the original rows, modifiers and pedal defaults', () => {
  const piano61 = ['1', '!', '2', '@', '3', '4', '$', '5', '%', '6', '^', '7',
    '8', '*', '9', '(', '0', 'q', 'Q', 'w', 'W', 'e', 'E', 'r',
    't', 'T', 'y', 'Y', 'u', 'i', 'I', 'o', 'O', 'p', 'P', 'a',
    's', 'S', 'd', 'D', 'f', 'g', 'G', 'h', 'H', 'j', 'J', 'k',
    'l', 'L', 'z', 'Z', 'x', 'c', 'C', 'v', 'V', 'b', 'B', 'n', 'm'];
  const expected = {
    QWERTY_21Keys: [['q', 'w', 'e', 'r', 't', 'y', 'u'], ['a', 's', 'd', 'f', 'g', 'h', 'j'], ['z', 'x', 'c', 'v', 'b', 'n', 'm']],
    QWERTY_15Keys: [['y', 'u', 'i', 'o', 'p'], ['h', 'j', 'k', 'l', ';'], ['n', 'm', ',', '.', '/']],
    QWERTY_36Keys: [['q', 'Q', 'w', '^e', 'e', 'r', 'R', 't', 'T', 'y', '^u', 'u'],
      ['a', 'A', 's', '^d', 'd', 'f', 'F', 'g', 'G', 'h', '^j', 'j'],
      ['z', 'Z', 'x', '^c', 'c', 'v', 'V', 'b', 'B', 'n', '^m', 'm']],
    QWERTY_61Keys: [piano61],
    QWERTY_88Keys: [[...['^1', '^2', '^3', '^4', '^5', '^6', '^7', '^8', '^9', '^0', '^q', '^w', '^e', '^r', '^t'],
      ...piano61, ...['^y', '^u', '^i', '^o', '^p', '^a', '^s', '^d', '^f', '^g', '^h', '^j']]],
  };
  assert.equal(layoutFiles.length, 23);
  assert.equal(new Set(layouts.map(layout => layout.id)).size, 23);
  for (const [id, keys] of Object.entries(expected)) {
    const piano = id === 'QWERTY_61Keys' || id === 'QWERTY_88Keys';
    assert.deepEqual(read(`KeyboardLayouts/${id}.json`), {
      id, name: 'QWERTY', keys, ...(piano ? { sustainKey: 'space' } : {}),
    });
  }
});

test('every bundled reference resolves and every instrument retains its configured pitch-to-key pairing', () => {
  const allLayouts = Object.fromEntries(layouts.map(layout => [layout.id.toLowerCase(), layout]));
  const gameFolders = readdirSync(new URL('Games/', defaults));
  const covered = new Set();
  for (const folder of gameFolders) {
    const config = read(`Games/${folder}/game.json`);
    const instrumentFolder = new URL(`Games/${folder}/Instruments/`, defaults);
    const instruments = readdirSync(instrumentFolder, { recursive: true })
      .filter(path => path.endsWith('.json'))
      .map(path => JSON.parse(readFileSync(new URL(path.replaceAll('\\', '/'), instrumentFolder), 'utf8')));
    assert.ok(allLayouts[config.defaultLayout.toLowerCase()], config.defaultLayout);
    const game = loadedBundleToGameInfo({ config, instruments }, allLayouts);
    for (const instrument of game.instruments) {
      for (const id of instrument.supportedLayouts) {
        const layout = game.layouts.find(layout => layout.id === id);
        assert.equal(layout?.id, id);
        const pitches = instrument.keys?.flat().filter(pitch => pitch !== null) ?? instrument.notes;
        const expected = pitches.map((pitch, index) => [pitch, layout.keys[index]]);
        assert.deepEqual(instrumentKeyPairs(instrument, layout), expected, `${folder}/${instrument.id}/${id}`);
        if (sharedLayoutIds.includes(id)) covered.add(id);
      }
    }
  }
  assert.equal(covered.size, 5);
});

