import * as sharedDefaults from '../src/shared/defaults/settings.ts';
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { stripTypeScriptTypes } from 'node:module';
import { compileModule } from 'svelte/compiler';
import { dedupeIssuesByKind, groupDuplicateFiles } from '../src/renderer/lib/utils/fileIssueGroups.ts';
import { filterMidiFilePaths, normalizeTargetPlaylistId, resolveDropTargetPlaylistId } from '../src/renderer/lib/utils/midiImport.ts';
import { getMarkerNoteDisplay, formatBpm } from '../src/renderer/lib/utils/musicConstants.ts';
import { compile } from 'svelte/compiler';

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

test('dedupeIssuesByKind keeps one entry per path within a conflict category', () => {
  const issues = [
    { kind: 'missing', path: 'C:\\MIDIs\\Missing.mid', songId: 'song-1', relatedPath: null, title: 'Missing', details: null },
    { kind: 'missing', path: 'C:\\MIDIs\\Missing.mid', songId: 'song-2', relatedPath: null, title: 'Missing', details: null },
    { kind: 'invalid', path: 'C:\\MIDIs\\Missing.mid', songId: null, relatedPath: null, title: 'Missing', details: 'Bad header' },
    { kind: 'missing', path: 'C:\\MIDIs\\Other.mid', songId: 'song-3', relatedPath: null, title: 'Other', details: null },
  ];
  const missing = dedupeIssuesByKind(issues, 'missing');
  assert.deepEqual(missing.map(issue => issue.songId), ['song-1', 'song-3']);
  assert.equal(dedupeIssuesByKind(issues, 'invalid').length, 1);
});

let harnessId = 0;

async function nativeHarness() {
  const calls = [];
  const mocks = {
    '../../../shared/defaults/settings.js': sharedDefaults,
    '../infrastructure/logger.js': { logger: {} },
    '../infrastructure/telemetry.js': { telemetry: {} },
    '../utils/musicConstants.js': { getMarkerNoteDisplay },
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

test('saving a song strips legacy label offsets while preserving numeric marker metadata', async () => {
  const { api, calls } = await nativeHarness();
  await api.songs.saveSong({ id: 'song', keyOffset: 0, baseKeyOffset: 0, transposeMode: 'Smart',
    tracks: [], keyMarkers: [{ timeMs: 183013, keyOffset: -26, transpose: 2, label: 'A♯0 (-26)' }],
  }, { kind: 'instrument', gameId: 'a', instrumentId: 'lyre' });
  assert.deepEqual(JSON.parse(calls[0][1].settings.keyChanges), [
    { TimeMs: 183013, KeyOffset: -26, Transpose: 2, Label: 'A♯0' },
  ]);
});

test('song serialization preserves merge settings and note processing reaches the native command', async () => {
  const { api, calls } = await nativeHarness();
  await api.songs.saveSong({ id: 'song', keyOffset: 0, baseKeyOffset: 0, transposeMode: 'Smart',
    tracks: [], mergeNotes: true, mergeToleranceMs: 175, chordDetectionMilliseconds: 65,
  }, { kind: 'instrument', gameId: 'a', instrumentId: 'lyre' });
  assert.equal(calls[0][1].settings.mergeNotes, true);
  assert.equal(calls[0][1].settings.mergeMilliseconds, 175);
  assert.equal(calls[0][1].settings.chordDetectionMilliseconds, 65);
  await api.playback.setNoteProcessing(true, 175, 65);
  assert.deepEqual(calls.at(-1), ['set_note_processing', {
    mergeNotes: true, mergeToleranceMs: 175, chordDetectionMilliseconds: 65,
  }]);
});

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
  globalThis[key] = { ...sharedDefaults, api: { songs: { scanFolder: async () => { throw failure; } } }, isTauri: false };
  const source = stripTypeScriptTypes(readFileSync(new URL('../src/renderer/lib/state/libraryStore.svelte.ts', import.meta.url), 'utf8'))
    .replace("import { DEFAULT_UI_PREFERENCES } from '../../../shared/defaults/settings.js';", `const { DEFAULT_UI_PREFERENCES } = globalThis.${key};`)
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
    '../../../shared/defaults/settings.js': sharedDefaults,
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
  const {
    libraryStore: store,
    ALL_SONGS_PLAYLIST_ID,
    LIKED_SONGS_PLAYLIST_ID,
    isBuiltInPlaylist,
  } = await import(`data:text/javascript;base64,${Buffer.from(compiled).toString('base64')}`);
  delete globalThis[key];
  return { store, ALL_SONGS_PLAYLIST_ID, LIKED_SONGS_PLAYLIST_ID, isBuiltInPlaylist, api, calls, toasts };
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

test('playlist ordering goes through the playlist API and refreshes its stored membership order', async () => {
  const h = await storeHarness();
  const currentSongs = [{ id: 'a', title: 'A' }, { id: 'b', title: 'B' }];
  let playlistSongs = [...currentSongs];
  h.api.playlists.getPlaylists = async () => [{ id: 'liked-songs', title: 'Liked Songs' }];
  h.api.playlists.getPlaylistSongs = async () => playlistSongs;
  h.api.playlists.reorderPlaylist = async (playlistId, ids) => {
    h.calls.push(['reorder-playlist', playlistId, ids]);
    playlistSongs = ids.map(id => currentSongs.find(song => song.id === id));
    return true;
  };
  await h.store.init();
  await h.store.reorderPlaylist('liked-songs', ['b', 'a']);
  assert.deepEqual(h.calls, [['reorder-playlist', 'liked-songs', ['b', 'a']]]);
  assert.deepEqual(h.store.songsByPlaylist['liked-songs'].map(song => song.id), ['b', 'a']);
  h.api.playlists.reorderPlaylist = async () => false;
  await assert.rejects(h.store.reorderPlaylist('liked-songs', ['a', 'b']), /could not be saved/);
  assert.deepEqual(h.store.songsByPlaylist['liked-songs'].map(song => song.id), ['b', 'a']);
});

test('Liked Songs batch helpers deduplicate, skip duplicates, preserve order, and report failures', async () => {
  const h = await storeHarness();
  const catalog = [{ id: 'a', title: 'Song A' }, { id: 'b', title: 'Song B' }, { id: 'c', title: 'Song C' }];
  let liked = [catalog[0]];
  let reloadCount = 0;

  h.api.songs.getSongs = async () => catalog;
  h.api.playlists.getPlaylists = async () => [{ id: 'liked-songs', title: 'Liked Songs' }];
  h.api.playlists.getPlaylistSongs = async (playlistId) => {
    reloadCount++;
    return playlistId === 'liked-songs' ? [...liked] : [];
  };
  h.api.playlists.addToPlaylist = async (playlistId, songId) => {
    h.calls.push(['add', playlistId, songId]);
    const song = catalog.find(item => item.id === songId);
    if (song && !liked.some(item => item.id === songId)) liked = [...liked, song];
    return true;
  };
  h.api.playlists.removeFromPlaylist = async (playlistId, songId) => {
    h.calls.push(['remove', playlistId, songId]);
    liked = liked.filter(item => item.id !== songId);
    return true;
  };

  await h.store.init();
  reloadCount = 0;
  assert.equal(h.LIKED_SONGS_PLAYLIST_ID, 'liked-songs');
  assert.equal(h.store.isSongLiked('a'), true);
  assert.equal(h.store.isSongLiked('b'), false);

  assert.equal(await h.store.addSongsToPlaylist('liked-songs', ['a', 'c', 'b', 'c']), 2);
  assert.deepEqual(h.calls, [['add', 'liked-songs', 'c'], ['add', 'liked-songs', 'b']]);
  assert.equal(reloadCount, 1);
  assert.deepEqual([...h.store.likedSongIds], ['a', 'c', 'b']);
  assert.equal(h.store.isSongLiked('b'), true);

  h.calls.length = 0;
  assert.equal(await h.store.addSongsToPlaylist('liked-songs', ['a', 'b']), 0);
  assert.deepEqual(h.calls, []);
  assert.equal(reloadCount, 1);

  assert.equal(await h.store.removeSongsFromPlaylist('liked-songs', ['a', 'missing', 'a']), 1);
  assert.deepEqual(h.calls, [['remove', 'liked-songs', 'a']]);
  assert.equal(reloadCount, 2);
  assert.equal(h.store.isSongLiked('a'), false);
  assert.equal(h.store.isSongLiked('c'), true);

  h.api.playlists.addToPlaylist = async () => false;
  await assert.rejects(h.store.addSongsToPlaylist('liked-songs', ['a']), /could not be added/);

  h.api.playlists.removeFromPlaylist = async () => false;
  await assert.rejects(h.store.removeSongsFromPlaylist('liked-songs', ['c']), /could not be removed/);
});

test('custom playlists can be created, edited, deleted, and built-in playlists are protected', async () => {
  const h = await storeHarness();
  let playlists = [{
    id: 'liked-songs',
    title: 'Liked Songs',
    subtitle: 'Favorite songs',
    icon: 'Heart',
    gradient: 'linear-gradient(135deg, #450af5 0%, #8e8ee5 100%)',
    bannerColor: 'rgba(69, 10, 245, 0.45)',
    songCount: 0,
  }];
  const songsMap = { 'liked-songs': [] };

  h.api.playlists.getPlaylists = async () => playlists.map(p => ({ ...p }));
  h.api.playlists.getPlaylistSongs = async (id) => songsMap[id] ?? [];
  h.api.playlists.createPlaylist = async (title, icon, bannerColor, subtitle, gradient) => {
    h.calls.push(['create-playlist', title, icon, bannerColor, subtitle, gradient]);
    const created = {
      id: 'pl-1',
      title,
      subtitle: subtitle ?? null,
      icon,
      bannerColor,
      gradient,
      songCount: 0,
    };
    playlists.push(created);
    songsMap[created.id] = [];
    return created;
  };
  h.api.playlists.updatePlaylist = async (id, details) => {
    h.calls.push(['update-playlist', id, details]);
    const target = playlists.find(p => p.id === id);
    Object.assign(target, details);
    return { ...target };
  };
  h.api.playlists.deletePlaylist = async (id) => {
    h.calls.push(['delete-playlist', id]);
    playlists = playlists.filter(p => p.id !== id);
    delete songsMap[id];
    return true;
  };

  await h.store.init();
  assert.equal(h.isBuiltInPlaylist(h.ALL_SONGS_PLAYLIST_ID), true);
  assert.equal(h.isBuiltInPlaylist('liked-songs'), true);
  assert.equal(h.isBuiltInPlaylist('pl-1'), false);
  assert.deepEqual([...h.store.customPlaylists], []);

  const created = await h.store.createPlaylist(
    'Practice Mix',
    'Star',
    'rgba(37, 99, 235, 0.45)',
    'Warm-up tracks',
    'linear-gradient(135deg, #2563eb 0%, #06b6d4 100%)'
  );
  assert.equal(created.id, 'pl-1');
  assert.equal(h.store.customPlaylists.length, 1);
  assert.equal(h.store.customPlaylists[0].description, 'Warm-up tracks');
  assert.equal(h.store.customPlaylists[0].isBuiltIn, false);

  await h.store.updatePlaylist('pl-1', { title: 'Concert Prep', subtitle: '', icon: 'Headphones' });
  assert.equal(h.store.customPlaylists[0].title, 'Concert Prep');
  assert.equal(h.store.customPlaylists[0].description, undefined);
  assert.equal(h.store.customPlaylists[0].subtitle, 'Playlist • 0 songs');

  await assert.rejects(h.store.updatePlaylist('songs', { title: 'Nope' }), /Built-in playlists/);
  await assert.rejects(h.store.updatePlaylist('liked-songs', { title: 'Nope' }), /Built-in playlists/);
  await assert.rejects(h.store.deletePlaylist('songs'), /Built-in playlists/);
  await assert.rejects(h.store.deletePlaylist('liked-songs'), /Built-in playlists/);

  h.store.selectedPlaylistId = 'pl-1';
  await h.store.deletePlaylist('pl-1');
  assert.equal(h.store.selectedPlaylistId, 'songs');
  assert.deepEqual([...h.store.customPlaylists], []);
});

test('filterMidiFilePaths filters .mid/.midi case-insensitively, ignores non-MIDI files, and deduplicates', () => {
  assert.deepEqual(
    filterMidiFilePaths([
      'C:\\MIDIs\\One.MID',
      ' C:\\MIDIs\\One.MID ',
      'C:\\MIDIs\\Two.midi',
      'C:\\MIDIs\\readme.txt',
      'C:\\MIDIs\\cover.png',
      'C:\\MIDIs\\Three.MiDi',
      '',
    ]),
    ['C:\\MIDIs\\One.MID', 'C:\\MIDIs\\Two.midi', 'C:\\MIDIs\\Three.MiDi'],
  );
  assert.deepEqual(filterMidiFilePaths([]), []);
  assert.deepEqual(filterMidiFilePaths(null), []);
});

test('resolveDropTargetPlaylistId resolves All Songs, Liked Songs, custom playlists, and hovered sidebar targets', () => {
  assert.equal(normalizeTargetPlaylistId('songs'), null);
  assert.equal(normalizeTargetPlaylistId('all-songs'), null);
  assert.equal(normalizeTargetPlaylistId(null), null);
  assert.equal(normalizeTargetPlaylistId('liked-songs'), 'liked-songs');
  assert.equal(normalizeTargetPlaylistId('pl-1'), 'pl-1');

  assert.equal(
    resolveDropTargetPlaylistId({ activeTab: 'songs', selectedPlaylistId: 'songs' }),
    null,
  );
  assert.equal(
    resolveDropTargetPlaylistId({ activeTab: 'songs', selectedPlaylistId: 'liked-songs' }),
    'liked-songs',
  );
  assert.equal(
    resolveDropTargetPlaylistId({ activeTab: 'songs', selectedPlaylistId: 'pl-1' }),
    'pl-1',
  );
  assert.equal(
    resolveDropTargetPlaylistId({ activeTab: 'studio', selectedPlaylistId: 'liked-songs' }),
    null,
  );
  assert.equal(
    resolveDropTargetPlaylistId({
      activeTab: 'studio',
      selectedPlaylistId: 'songs',
      hoveredPlaylistId: 'liked-songs',
    }),
    'liked-songs',
  );
  assert.equal(
    resolveDropTargetPlaylistId({
      activeTab: 'songs',
      selectedPlaylistId: 'pl-1',
      hoveredPlaylistId: 'songs',
    }),
    null,
  );
});

test('libraryStore.importFiles adds imported songs to both All Songs and the target playlist (including Liked Songs)', async () => {
  const h = await storeHarness();
  let allSongs = [{ id: 'existing', title: 'Existing' }];
  const playlistSongsMap = {
    'liked-songs': [],
    'pl-custom': [],
  };

  h.api.songs.getSongs = async () => allSongs.map(song => ({ ...song }));
  h.api.playlists.getPlaylists = async () => [
    { id: 'liked-songs', title: 'Liked Songs', songCount: playlistSongsMap['liked-songs'].length },
    { id: 'pl-custom', title: 'Custom Mix', songCount: playlistSongsMap['pl-custom'].length },
  ];
  h.api.playlists.getPlaylistSongs = async (id) => (playlistSongsMap[id] ?? []).map(song => ({ ...song }));
  h.api.playlists.addToPlaylist = async (playlistId, songId) => {
    h.calls.push(['add', playlistId, songId]);
    const song = allSongs.find(item => item.id === songId);
    if (song && !playlistSongsMap[playlistId].some(item => item.id === songId)) {
      playlistSongsMap[playlistId].push(song);
    }
    return true;
  };
  h.api.songs.importFiles = async (paths = []) => {
    h.calls.push(['import', paths]);
    const created = paths.map((p, idx) => ({ id: `imp-${h.calls.length}-${idx}`, title: p }));
    allSongs = [...allSongs, ...created];
    return created;
  };

  await h.store.init();

  await h.store.importFiles(['C:\\all.mid'], 'songs');
  assert.deepEqual(h.calls, [['import', ['C:\\all.mid']]]);
  assert.equal(h.store.songs.some(song => song.title === 'C:\\all.mid'), true);

  h.calls.length = 0;
  const likedImported = await h.store.importFiles(['C:\\liked1.mid', 'C:\\liked2.midi'], 'liked-songs');
  assert.equal(likedImported.length, 2);
  assert.deepEqual(h.calls, [
    ['import', ['C:\\liked1.mid', 'C:\\liked2.midi']],
    ['add', 'liked-songs', likedImported[0].id],
    ['add', 'liked-songs', likedImported[1].id],
  ]);
  assert.equal(h.store.songs.some(song => song.id === likedImported[0].id), true);
  assert.equal(h.store.songsByPlaylist['liked-songs'].some(song => song.id === likedImported[0].id), true);
  assert.equal(h.store.songsByPlaylist['liked-songs'].some(song => song.id === likedImported[1].id), true);

  h.calls.length = 0;
  const customImported = await h.store.importFiles(['C:\\custom.mid'], 'pl-custom');
  assert.equal(customImported.length, 1);
  assert.deepEqual(h.calls, [
    ['import', ['C:\\custom.mid']],
    ['add', 'pl-custom', customImported[0].id],
  ]);
  assert.equal(h.store.songs.some(song => song.id === customImported[0].id), true);
  assert.equal(h.store.songsByPlaylist['pl-custom'].some(song => song.id === customImported[0].id), true);
});

test('formatBpm safely handles valid, null, undefined, and non-finite BPM values', () => {
  assert.equal(formatBpm(120), '120');
  assert.equal(formatBpm(128.5714), '128.57');
  assert.equal(formatBpm(120.00), '120');
  assert.equal(formatBpm(95.4), '95.4');

  assert.equal(formatBpm(null), '');
  assert.equal(formatBpm(undefined), '');
  assert.equal(formatBpm(Number.NaN), '');
  assert.equal(formatBpm(Number.POSITIVE_INFINITY), '');
  assert.equal(formatBpm(Number.NEGATIVE_INFINITY), '');
});

test('EditDialog handles cleared BPM input without throwing toFixed on null', () => {
  const editDialogSource = readFileSync(new URL('../src/renderer/lib/components/Dialogs/EditDialog.svelte', import.meta.url), 'utf8');

  // Verify the binding handles null and doesn't invoke toFixed on null
  assert.match(editDialogSource, /bind:value=\{\(\) => bpm == null \? null : Number\(formatBpm\(bpm\)\), value => bpm = value \?\? undefined\}/);
  assert.match(editDialogSource, /if \(bpm != null && \(!Number\.isFinite\(bpm\)/);
  assert.match(editDialogSource, /const bpmOverride = bpm == null \|\| Math\.abs\(bpm - nativeBpm\) < 0\.01 \? null : bpm;/);

  const compiled = compile(editDialogSource, { filename: 'EditDialog.svelte', generate: 'client' });
  assert.ok(compiled.js.code.length > 0);
});
