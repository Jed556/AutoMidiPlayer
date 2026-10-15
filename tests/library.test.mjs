import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { stripTypeScriptTypes } from 'node:module';
import { compileModule } from 'svelte/compiler';
import { groupDuplicateFiles } from '../src/renderer/lib/utils/fileIssueGroups.ts';

const duplicateIssue = (path, songId, relatedPath) => ({
  kind: 'duplicate', path, songId, relatedPath, title: 'Same title', details: null,
});

test('duplicate cards group all copies by song and include the current file once', () => {
  const songs = [{ id: 'one', title: 'Same title', filePath: 'C:\\MIDIs\\Song.mid' },
    { id: 'two', title: 'Same title', filePath: 'D:\\MIDIs\\Other.mid' }];
  const issues = [duplicateIssue('C:\\MIDIs\\Song (1).mid', 'one', songs[0].filePath),
    duplicateIssue('D:\\Copies\\Song.mid', 'one', songs[0].filePath),
    duplicateIssue('D:\\MIDIs\\Other (1).mid', 'two', songs[1].filePath),
    duplicateIssue('C:\\MIDIs\\Song (1).mid', 'one', songs[0].filePath),
    { ...duplicateIssue('broken.mid', null, null), kind: 'invalid' }];
  const groups = groupDuplicateFiles(issues, songs);
  assert.equal(groups.length, 2, 'Identical titles must not merge different songs');
  assert.deepEqual(groups[0].copies.map(copy => copy.path), [songs[0].filePath, issues[0].path, issues[1].path]);
  assert.deepEqual(groups[0].copies.map(copy => copy.isCurrent), [true, false, false]);
  assert.equal(groups[0].copies[1].name, 'Song (1).mid');

  const replacement = { ...songs[0], filePath: issues[1].path };
  const switched = groupDuplicateFiles([
    duplicateIssue(songs[0].filePath, 'one', replacement.filePath),
    duplicateIssue(issues[0].path, 'one', replacement.filePath),
  ], [replacement]);
  assert.equal(switched[0].id, groups[0].id);
  assert.deepEqual(switched[0].copies.map(copy => copy.path), [replacement.filePath, songs[0].filePath, issues[0].path]);
  assert.equal(switched[0].copies[0].isCurrent, true);
});

test('duplicate cards use the related path when a library record is unavailable', () => {
  const groups = groupDuplicateFiles([
    duplicateIssue('/music/one-copy.mid', null, '/music/one.mid'),
    duplicateIssue('/backup/one-copy.mid', null, '/music/one.mid'),
    duplicateIssue('/music/two-copy.mid', null, '/music/two.mid'),
  ], []);
  assert.equal(groups.length, 2);
  assert.equal(groups[0].title, 'one');
  assert.equal(groups[0].copies.length, 3);
  assert.deepEqual(groups[0].copies[0], { path: '/music/one.mid', name: 'one.mid', isCurrent: true });
});

let harnessId = 0;

async function nativeHarness() {
  const calls = [];
  const mocks = {
    '../infrastructure/logger.js': { logger: {} },
    '../infrastructure/telemetry.js': { telemetry: {} },
    './mappers.js': { songRecordToMidiFile: song => song },
    './runtime.js': {
      invokeTauri: async (_scope, command, args) => {
        calls.push([command, args]);
        if (command === 'get_library_songs') return [{ id: 'new-song' }];
        return true;
      },
      isTauriEnvironment: true,
    },
  };
  const key = `__nativeLibraryHarness${++harnessId}`;
  globalThis[key] = mocks;
  const source = stripTypeScriptTypes(readFileSync(new URL('../src/renderer/lib/backend/nativeApi.ts', import.meta.url), 'utf8'))
    .replace(/^import \{([\s\S]*?)\} from '([^']+)';/gm,
      (_, names, path) => `const {${names}} = globalThis.${key}[${JSON.stringify(path)}];`);
  const { createAppApi } = await import(`data:text/javascript;base64,${Buffer.from(source).toString('base64')}`);
  delete globalThis[key];
  return { api: createAppApi(), calls };
}

test('manual refresh scans the folder before reading songs, even with auto scan disabled', async () => {
  const { api, calls } = await nativeHarness();
  const songs = await api.songs.scanFolder('C:/MIDIs');
  assert.deepEqual(calls, [
    ['scan_library_folder', { folderPath: 'C:/MIDIs' }],
    ['get_library_songs', { context: null }],
  ]);
  assert.deepEqual(songs, [{ id: 'new-song' }]);
});

test('refresh with an unset folder reports the problem instead of returning cached songs', async () => {
  const { api, calls } = await nativeHarness();
  for (const folder of [undefined, '', '   ']) {
    await assert.rejects(api.songs.scanFolder(folder), /Choose a MIDI library folder in Settings/);
  }
  assert.deepEqual(calls, []);
  await api.songs.watchFolder(null);
  assert.deepEqual(calls, [['watch_library_folder', { folderPath: null }]]);
});

test('failed refresh reaches the caller and resets scanning state without replacing the library', async () => {
  const failure = new Error('Folder cannot be read');
  const key = `__libraryHarness${++harnessId}`;
  globalThis[key] = { api: { songs: { scanFolder: async () => { throw failure; } } }, isTauri: false };
  const source = stripTypeScriptTypes(readFileSync(new URL('../src/renderer/lib/state/libraryStore.svelte.ts', import.meta.url), 'utf8'))
    .replace("import { api, isTauri } from '../backend/index.js';", `const { api, isTauri } = globalThis.${key};`)
    .replace("import { notificationCenter } from '../ui/notificationCenter.svelte';", 'const notificationCenter = {};')
    .replace("import { t } from '../i18n';", 'const t = key => key;');
  const compiled = compileModule(source, { filename: 'libraryStore.svelte.js', generate: 'client' }).js.code
    .replace("'svelte/internal/client'", JSON.stringify(import.meta.resolve('svelte/internal/client')));
  const { libraryStore } = await import(`data:text/javascript;base64,${Buffer.from(compiled).toString('base64')}`);
  delete globalThis[key];
  libraryStore.songs = [{ id: 'existing' }];
  const originalError = console.error;
  try {
    console.error = () => {};
    await assert.rejects(libraryStore.scanFolder('C:/MIDIs'), error => error === failure);
  } finally {
    console.error = originalError;
  }
  assert.equal(libraryStore.isScanning, false);
  assert.equal(libraryStore.songs[0].id, 'existing');
});

async function storeHarness() {
  let songs = [{ id: 'a', title: 'Song A' }, { id: 'b', title: 'Song B' }];
  let issues = [];
  const calls = [];
  const toasts = [];
  const api = {
    songs: {
      getSongs: async () => songs,
      getFileIssues: async () => issues,
      excludeSongs: async ids => {
        calls.push(['exclude', ids]);
        for (const song of songs.filter(song => ids.includes(song.id))) {
          issues.push({ kind: 'excluded', path: `${song.id}.mid`, title: song.title });
        }
        songs = songs.filter(song => !ids.includes(song.id));
        return true;
      },
      resolveFileIssue: async (path, action) => {
        calls.push(['resolve', path, action]);
        issues = issues.filter(issue => issue.path !== path);
        if (action === 'restore') songs = [...songs, { id: path.slice(0, 1), title: 'Restored' }];
        return true;
      },
    },
    playlists: { getPlaylists: async () => [], getPlaylistSongs: async () => [] },
  };
  const key = `__libraryStoreHarness${++harnessId}`;
  globalThis[key] = {
    '../backend/index.js': { api, isTauri: false },
    '../ui/notificationCenter.svelte': { notificationCenter: {
      success: (...args) => toasts.push(['success', ...args]),
      warning: (...args) => toasts.push(['warning', ...args]),
    } },
    '../i18n': { t: (key, params) => `${key}:${JSON.stringify(params ?? {})}` },
  };
  const source = stripTypeScriptTypes(readFileSync(new URL('../src/renderer/lib/state/libraryStore.svelte.ts', import.meta.url), 'utf8'))
    .replace(/^import \{([^}]+)\} from '([^']+)';/gm,
      (_, names, path) => `const {${names}} = globalThis.${key}[${JSON.stringify(path)}];`);
  const compiled = compileModule(source, { filename: 'libraryStore.svelte.js', generate: 'client' }).js.code
    .replace("'svelte/internal/client'", JSON.stringify(import.meta.resolve('svelte/internal/client')));
  const { libraryStore: store } = await import(`data:text/javascript;base64,${Buffer.from(compiled).toString('base64')}`);
  delete globalThis[key];
  return { store, api, calls, toasts };
}

test('library changes show batched add/remove toasts and quiet scans show none', async () => {
  const h = await storeHarness();
  await h.store.init();
  assert.equal(h.toasts.length, 0, 'Loading the existing library is quiet');
  h.store.notifyChanges({ addedFiles: ['One', 'Two'], removedFiles: ['Missing'] });
  assert.equal(h.toasts.length, 2);
  assert.equal(h.toasts[0][0], 'success');
  assert.match(h.toasts[0][2], /"count":2/);
  assert.equal(h.toasts[1][0], 'warning');
  assert.match(h.toasts[1][2], /Missing/);
  h.store.notifyChanges({ addedFiles: [], removedFiles: [] });
  assert.equal(h.toasts.length, 2);
  h.store.notifyChanges({ addedFiles: [], removedFiles: [], newConflicts: 2 });
  assert.equal(h.toasts.length, 3);
  assert.match(h.toasts[2][2], /"count":2/);
});

test('deleting library selection exposes file conflicts and restoring clears the warning', async () => {
  const h = await storeHarness();
  const unavailable = [];
  h.store.onSongsUnavailable = async ids => unavailable.push(ids);
  await h.store.init();
  assert.equal(h.store.hasFileIssues, false);
  await h.store.deleteSongs(['a', 'a']);
  assert.deepEqual(h.calls[0], ['exclude', ['a']]);
  assert.deepEqual([...h.store.songs].map(song => song.id), ['b']);
  assert.equal(h.store.hasFileIssues, true);
  assert.equal(h.store.fileIssues[0].kind, 'excluded');
  assert.deepEqual(unavailable, [['a']]);
  await h.store.resolveFileIssue('a.mid', 'restore');
  assert.equal(h.store.hasFileIssues, false);
  assert.equal(h.store.songs.length, 2);
});

test('native file conflict actions keep disk deletion separate from library removal', async () => {
  const { api, calls } = await nativeHarness();
  await api.songs.excludeSongs(['a', 'b']);
  await api.songs.getFileIssues();
  await api.songs.resolveFileIssue('a.mid', 'restore');
  await api.songs.resolveFileIssue('a.mid', 'deleteFile');
  assert.deepEqual(calls, [
    ['exclude_library_songs', { ids: ['a', 'b'] }],
    ['get_library_file_issues', undefined],
    ['resolve_library_file_issue', { path: 'a.mid', action: 'restore' }],
    ['resolve_library_file_issue', { path: 'a.mid', action: 'deleteFile' }],
  ]);
});
