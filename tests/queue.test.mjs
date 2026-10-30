import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { stripTypeScriptTypes } from 'node:module';
import { compileModule } from 'svelte/compiler';
import { PlaybackPositionGate } from '../src/renderer/lib/utils/playbackPosition.ts';
import * as music from '../src/renderer/lib/utils/musicConstants.ts';
import * as sessionPreferences from '../src/renderer/lib/utils/sessionPreferences.ts';

// Run the actual rune store with the Svelte runtime and an in-memory native boundary.
const source = stripTypeScriptTypes(readFileSync(new URL('../src/renderer/lib/state/playbackStore.svelte.ts', import.meta.url), 'utf8'), { mode: 'transform' });
let harnessId = 0;
async function createPlayer({ writeQueue, readQueue, writeSongSettings, readSong, readMidi, playback = {}, native = false, preferences = {} } = {}) {
  const writes = [];
  const playbackCalls = [];
  const songSettingsWrites = [];
  const preferenceWrites = [];
  let persisted = [];
  const songs = ['a', 'b', 'c', 'd', 'e'].map(id => ({
    id, title: id, artist: '', album: '', filePath: `${id}.mid`, durationMs: 60000,
    bpm: 120, nativeBpm: 120, bpmOverride: null, baseKey: 0, baseKeyOffset: 0, keyOffset: 0,
    transposeMode: 'Smart', speed: 1, holdNotes: false, mergeNotes: false, mergeToleranceMs: 100,
    tracks: [], notes: [], keyMarkers: [], dateAdded: '2026-01-01T00:00:00Z',
  }));
  const libraryStore = { songs, playlists: [{ id: 'songs' }, { id: 'favorites' }], songsByPlaylist: { favorites: songs.slice(1, 3) }, init: async () => {} };
  libraryStore.reload = async () => {};
  const gameStore = { activeGame: null, songSettingsContext: { gameId: 'game-a', instrumentId: 'lyre' },
    init: async () => {}, onSettingsContextChange() {}, onGameStatusChange() {} };
  const settingsStore = { settings: { autoCorrectThreshold: 21 } };
  const api = {
    songs: { getSongById: async id => readSong ? readSong(id) : songs.find(song => song.id === id),
    readMidiFile: async path => readMidi(path),
    saveSongSettings: async (id, settings, scope) => {
      songSettingsWrites.push([id, settings, scope]);
      if (writeSongSettings) await writeSongSettings(id, settings, scope);
      return true;
    } },
    queue: {
      getQueue: async () => readQueue ? readQueue() : persisted.map(id => songs.find(s => s.id === id)),
      setQueue: async ids => {
        writes.push([...ids]);
        if (writeQueue) await writeQueue(ids);
        persisted = [...ids];
        return true;
      },
    },
    playback: new Proxy({}, { get: (_, method) => async (...args) => {
      playbackCalls.push([method, ...args]);
      return playback[method] ? playback[method](...args) : true;
    } }),
  };
  const mocks = {
    '../backend/preferences.js': { loadUiPreferences: async () => preferences,
      saveUiPreferences: value => preferenceWrites.push(structuredClone(value)), flushUiPreferences: async () => {} },
    '../utils/sessionPreferences.js': sessionPreferences,
    '../backend/index.js': { api, isTauri: native, loadedMidiToMidiFile: song => ({ ...song }) },
    '../utils/musicConstants.js': music,
    '../ui/notificationCenter.svelte': { notificationCenter: new Proxy({}, { get: () => () => {} }) },
    '../utils/playbackPosition.js': { PlaybackPositionGate },
    './uiStore.svelte.js': { uiStore: { init: async () => {} } },
    './gameStore.svelte.js': { gameStore },
    './libraryStore.svelte.js': { libraryStore },
    './settingsStore.svelte.js': { settingsStore },
  };
  const key = `__queueHarness${++harnessId}`;
  globalThis[key] = mocks;
  const compiled = compileModule(source.replace(/^import \{([^}]+)\} from '([^']+)';/gm,
    (_, names, path) => `const {${names}} = globalThis.${key}[${JSON.stringify(path)}];`),
  { filename: 'playbackStore.svelte.js', generate: 'client' }).js.code
    .replace("'svelte/internal/client'", JSON.stringify(import.meta.resolve('svelte/internal/client')));
  const { playbackStore: player } = await import(`data:text/javascript;base64,${Buffer.from(compiled).toString('base64')}`);
  delete globalThis[key];
  player.isListenMode = true;
  return { player, songs, writes, playbackCalls, libraryStore, songSettingsWrites, gameStore, settingsStore, preferenceWrites };
}
const ids = songs => songs.map(s => s.id);

test('startup restores song, playhead, shuffle order and audio choices without starting playback', async () => {
  const { player, playbackCalls, libraryStore } = await createPlayer({
    readQueue: async () => [],
    preferences: {
      selectedPlaylistId: 'favorites', pedalsEnabled: false,
      playback: { activeSongId: 'c', positionSeconds: 23.5, queuePlaylistId: 'favorites',
        isShuffled: true, shuffledSongIds: ['c', 'a', 'b'], loopMode: 'all',
        volume: 1.2, isListenMode: false, synthMode: 'raw' },
    },
  });
  // Queue records come from SQLite, while session preferences reference stable IDs.
  await player.setQueueList(libraryStore.songs.slice(0, 3));
  await player.restoreSession({
    selectedPlaylistId: 'favorites', pedalsEnabled: false,
    playback: { activeSongId: 'c', positionSeconds: 23.5, queuePlaylistId: 'favorites',
      isShuffled: true, shuffledSongIds: ['c', 'a', 'b'], loopMode: 'all',
      volume: 1.2, isListenMode: false, synthMode: 'raw' },
  });
  assert.equal(player.activeSong.id, 'c');
  assert.equal(player.activeQueueIndex, 0);
  assert.equal(player.currentTimeSeconds, 23.5);
  assert.equal(player.status, 'Stopped');
  assert.deepEqual(ids(player.shuffledQueue), ['c', 'a', 'b']);
  assert.equal(player.loopMode, 'all');
  assert.equal(player.queuePlaylistId, 'favorites');
  assert.equal(libraryStore.selectedPlaylistId, 'favorites');
  assert.equal(player.pedals.enabled, false);
  assert.ok(playbackCalls.some(call => call[0] === 'setListenMode' && call[1] === false));
  assert.ok(playbackCalls.some(call => call[0] === 'setSynthMode' && call[1] === 'raw'));
  assert.ok(playbackCalls.some(call => call[0] === 'setVolume' && call[1] === 120));
  assert.ok(!playbackCalls.some(call => call[0] === 'play'));
  await player.setListenMode(true);
  await player.nextTrack();
  assert.equal(player.activeSong.id, 'a');
});

test('initialize restores the saved queue selection instead of picking its first song', async () => {
  const { player, songs, playbackCalls } = await createPlayer({
    preferences: { playback: { activeSongId: 'c', positionSeconds: 14, isListenMode: true } },
  });
  await player.setQueueList(songs.slice(0, 4));
  await player.initialize();
  assert.equal(player.activeSong.id, 'c');
  assert.equal(player.activeQueueIndex, 2);
  assert.equal(player.currentTimeSeconds, 14);
  assert.ok(!playbackCalls.some(call => call[0] === 'play'));
});

test('a session survives recreation and clamps a restored position to song duration', async () => {
  const first = await createPlayer();
  await first.player.setQueueList(first.songs.slice(0, 3), 0, 'favorites');
  first.player.loadSong(first.songs[1]);
  first.player.currentTimeSeconds = 200;
  first.player.isShuffled = true;
  first.player.shuffledQueue = [first.songs[2], first.songs[1], first.songs[0]];
  first.player.loopMode = 'single';
  await first.player.flushPersistence();
  const saved = first.preferenceWrites.at(-1);
  const second = await createPlayer({ preferences: saved });
  await second.player.setQueueList(second.songs.slice(0, 3));
  await second.player.initialize();
  assert.equal(second.player.activeSong.id, 'b');
  assert.equal(second.player.activeQueueIndex, 1);
  assert.equal(second.player.currentTimeSeconds, 60);
  assert.deepEqual(ids(second.player.shuffledQueue), ['c', 'b', 'a']);
  assert.equal(second.player.loopMode, 'single');
});

test('checkpoints skip unchanged state and the final flush always drains settings writes', async () => {
  const { player, songs, preferenceWrites } = await createPlayer();
  player.loadSong(songs[0]);
  player.persistSession(0);
  player.persistSession(0);
  assert.equal(preferenceWrites.length, 1);
  player.currentTimeSeconds = 8;
  player.persistSession(8);
  assert.equal(preferenceWrites.length, 2);
  await player.flushPersistence();
  assert.equal(preferenceWrites.length, 3);
  assert.equal(preferenceWrites.at(-1).playback.positionSeconds, 8);
});

test('deleted session references fall back safely and reconcile saved shuffle IDs', async () => {
  const { player, songs, libraryStore } = await createPlayer();
  await player.setQueueList(songs.slice(0, 3));
  await player.restoreSession({ selectedPlaylistId: 'deleted', playback: {
    activeSongId: 'deleted', queuePlaylistId: 'deleted', positionSeconds: 22,
    isShuffled: true, shuffledSongIds: ['deleted', 'b', 'b', 'a'],
  } });
  assert.equal(player.activeSong.id, 'a');
  assert.equal(player.currentTimeSeconds, 0);
  assert.equal(player.activeQueueIndex, 1);
  assert.deepEqual(ids(player.shuffledQueue), ['b', 'a', 'c']);
  assert.equal(player.queuePlaylistId, null);
  assert.equal(libraryStore.selectedPlaylistId, 'songs');
});

test('Play after restoration loads the engine and seeks to the saved position', async () => {
  const { player, songs, playbackCalls } = await createPlayer({ native: true });
  songs[1].notes = [{ pitch: 60, startTime: 0, duration: 1 }];
  await player.setQueueList(songs.slice(0, 3));
  await player.restoreSession({ playback: { activeSongId: 'b', positionSeconds: 12.5, isListenMode: true } });
  await player.play();
  assert.ok(playbackCalls.some(call => call[0] === 'loadMidiFile' && call[1] === 'b.mid'));
  assert.ok(playbackCalls.some(call => call[0] === 'seek' && call[1] === 12500));
  assert.equal(player.status, 'Playing');
});

test('manual key offsets stay relative to the detected base and save to the selected instrument', async () => {
  const { player, songs, gameStore, playbackCalls, songSettingsWrites } = await createPlayer();
  gameStore.activeInstrument = { id: 'lyre', notes: Array.from({ length: 21 }, (_, i) => 48 + i) };
  player.loadSong({ ...songs[0], baseKey: 3, baseKeyOffset: 3 });
  assert.equal(player.keyOffset, 0);
  assert.equal(player.keyOffsetBase, 3);
  assert.equal(player.effectiveKeyOffset, 3);
  const zero = music.generateKeyOptions(player.keyOffsetBase).find(option => option.value === 0);
  assert.equal(zero.noteDisplay, 'D#');
  assert.equal(music.getKeyNoteNameWithOctave(player.activeKeyAdjustment.offset), 'D\u266f3');
  player.setKeyOffset(2);
  assert.equal(player.keyOffset, 2);
  assert.equal(player.baseKeyOffset, 3);
  assert.equal(player.activeSong.baseKey, 3);
  assert.deepEqual(playbackCalls.at(-1), ['setKeyOffset', 5]);
  await player.flushPendingSongSettings();
  assert.equal(songSettingsWrites[0][1].key, 2);
  assert.equal(songSettingsWrites[0][1].baseKey, 3);
  assert.equal(songSettingsWrites[0][2].instrumentId, 'lyre');
  player.setTransposeMode('Ignore');
  assert.equal(player.keyOffsetBase, 0);
  assert.equal(player.effectiveKeyOffset, 2);
  assert.deepEqual(playbackCalls.at(-1), ['setKeyOffset', 2]);
  await player.flushPendingSongSettings();
});

test('Smart uses the saved base as zero regardless of scale-correction threshold or instrument size', async () => {
  const { player, songs, gameStore, settingsStore } = await createPlayer();
  player.loadSong({ ...songs[0], baseKey: -2, baseKeyOffset: -2, keyOffset: 1 });
  assert.equal(player.keyOffsetBase, -2);
  assert.equal(player.effectiveKeyOffset, -1);
  gameStore.activeInstrument = { id: 'lyre', keyCount: 21 };
  assert.equal(player.effectiveKeyOffset, -1);
  settingsStore.settings.autoCorrectThreshold = 0;
  assert.equal(player.effectiveKeyOffset, -1);
  settingsStore.settings.autoCorrectThreshold = 21;
  gameStore.activeInstrument = { id: 'piano', keyCount: 88 };
  assert.equal(player.keyOffsetBase, -2);
  gameStore.activeInstrument = { id: 'lyre', keyCount: 21 };
  for (const mode of ['Ignore', 'Up', 'Down']) {
    player.transposeMode = mode;
    assert.equal(player.effectiveKeyOffset, 1);
  }
  player.transposeMode = 'Smart';
  player.baseKeyOffset = 0;
  assert.equal(player.keyOffsetBase, 0);
});

test('loading leaves markers empty until an explicit Studio scan, then saves them for reopening', async () => {
  const detected = [
    { timeMs: 0, keyOffset: -2, label: 'D maj', transpose: null },
    { timeMs: 2500, keyOffset: -4, label: 'E maj', transpose: 1 },
  ];
  const { player, songs, playbackCalls, songSettingsWrites } = await createPlayer({
    native: true, readMidi: async () => parsedMidi(songs[0]),
    playback: { detectKeySignatures: async () => detected },
  });
  player.loadSong(songs[0]);
  await new Promise(resolve => setImmediate(resolve));
  assert.deepEqual(player.activeSong.keyMarkers, []);
  assert.equal(playbackCalls.some(([method]) => method === 'detectKeySignatures'), false);
  assert.deepEqual(playbackCalls.find(([method]) => method === 'setKeyMarkers'), ['setKeyMarkers', []]);
  assert.equal(songSettingsWrites.length, 0);

  await player.autoDetectKeyChanges();
  assert.deepEqual(playbackCalls.filter(([method]) => method === 'detectKeySignatures'),
    [['detectKeySignatures', songs[0].filePath]]);
  const markers = JSON.parse(JSON.stringify(player.activeSong.keyMarkers));
  assert.deepEqual(markers.map(({ timeMs, keyOffset, noteDisplay, transpose }) =>
    ({ timeMs, keyOffset, label: noteDisplay, transpose })), detected);
  assert.deepEqual(playbackCalls.filter(([method]) => method === 'setKeyMarkers').at(-1),
    ['setKeyMarkers', markers]);
  await player.flushPendingSongSettings();
  assert.equal(songSettingsWrites.length, 1);
  assert.equal(songSettingsWrites[0][0], songs[0].id);
  assert.deepEqual(JSON.parse(songSettingsWrites[0][1].keyChanges), detected.map(marker => ({
    TimeMs: marker.timeMs, KeyOffset: marker.keyOffset, Transpose: marker.transpose, Label: marker.label,
  })));

  player.loadSong(songs[1]);
  player.loadSong(songs[0]);
  assert.deepEqual(player.activeSong.keyMarkers, markers);
  assert.equal(playbackCalls.filter(([method]) => method === 'detectKeySignatures').length, 1);
});

test('the key indicator follows the latest enabled marker across seeks and song changes', async () => {
  const { player, songs } = await createPlayer();
  const early = { id: 'early', timeMs: 1000, keyOffset: 0 };
  const late = { id: 'late', timeMs: 2500, keyOffset: -3 };
  player.loadSong({ ...songs[0], keyMarkers: [late, early] });
  player.currentTimeSeconds = 0.999;
  assert.equal(player.activeKeyMarker, null);
  player.currentTimeSeconds = 1;
  assert.equal(player.activeKeyMarker.id, 'early');
  player.currentTimeSeconds = 2.5;
  assert.equal(player.activeKeyMarker.id, 'late');
  player.activeSong.keyMarkers[0].keyOffset = 7;
  assert.equal(player.activeKeyMarker.keyOffset, 7);
  player.currentTimeSeconds = 1.5;
  assert.equal(player.activeKeyMarker.id, 'early');
  player.enableKeyChangeMarkers = false;
  assert.equal(player.activeKeyMarker, null);
  player.enableKeyChangeMarkers = true;
  player.loadSong(songs[1]);
  assert.equal(player.activeKeyMarker, null);
});

test('selecting Smart shows its indicator even at zero offset or without detected-key correction', async () => {
  const { player, songs, gameStore, settingsStore } = await createPlayer();
  assert.equal(player.activeKeyAdjustment, null, 'No song has no key indicator');
  gameStore.activeInstrument = { id: 'lyre', keyCount: 21 };
  for (const [baseKey, threshold, keyCount, expected] of [
    [0, 21, 21, 0], [5, 0, 21, 5], [5, 21, 88, 5], [5, 21, 21, 5],
  ]) {
    settingsStore.settings.autoCorrectThreshold = threshold;
    gameStore.activeInstrument.keyCount = keyCount;
    player.loadSong({ ...songs[0], baseKey, baseKeyOffset: baseKey, transposeMode: 'Ignore' });
    assert.equal(player.activeKeyAdjustment, null);
    player.setTransposeMode('Smart');
    assert.deepEqual(player.activeKeyAdjustment, { marker: null, offset: expected });
    for (const mode of ['Ignore', 'Up', 'Down']) {
      player.setTransposeMode(mode);
      assert.equal(player.activeKeyAdjustment, null);
    }
  }
  await player.flushPendingSongSettings();
});

test('marker indicators override Smart and disabling markers restores its check at zero', async () => {
  const { player, songs } = await createPlayer();
  const marker = { id: 'marker', timeMs: 1000, keyOffset: 7 };
  player.loadSong({ ...songs[0], keyMarkers: [marker] });
  assert.deepEqual(player.activeKeyAdjustment, { marker: null, offset: 0 });
  player.currentTimeSeconds = 1;
  assert.equal(player.activeKeyAdjustment.marker.id, 'marker');
  assert.equal(player.activeKeyAdjustment.offset, 7);
  player.enableKeyChangeMarkers = false;
  assert.deepEqual(player.activeKeyAdjustment, { marker: null, offset: 0 });
  player.enableKeyChangeMarkers = true;
  player.currentTimeSeconds = 0;
  assert.deepEqual(player.activeKeyAdjustment, { marker: null, offset: 0 });
});

const parsedMidi = (song, pitch = 60) => ({
  ...song, title: 'File title', durationMs: 45000, durationSeconds: 45, bpm: 95, nativeBpm: 95,
  notes: [{ id: `${song.id}-note`, noteNumber: pitch, trackIndex: 2, startMs: 0, lengthMs: 1000 }],
  tracks: [{ index: 2, name: 'Piano', noteCount: 1, isMuted: false, muted: false }],
});

test('selecting library metadata loads Studio notes while stopped and preserves scoped settings', async () => {
  let complete;
  const { player, songs, playbackCalls } = await createPlayer({
    native: true, readMidi: () => new Promise(resolve => { complete = resolve; }),
  });
  const marker = { id: 'marker', timeMs: 1000, keyOffset: 5 };
  player.loadSong({ ...songs[0], title: 'Saved title', disabledTracks: '2', bpmOverride: 140,
    keyOffset: 3, speed: 1.5, keyMarkers: [marker] });
  assert.equal(player.activeSong.notes.length, 0);
  complete(parsedMidi(songs[0]));
  await new Promise(resolve => setImmediate(resolve));
  assert.equal(player.activeSong.notes[0].noteNumber, 60);
  assert.equal(player.activeSong.tracks[0].index, 2);
  assert.equal(player.activeSong.tracks[0].isMuted, true);
  assert.equal(player.activeSong.title, 'Saved title');
  assert.equal(player.activeSong.bpm, 140);
  assert.equal(player.activeSong.nativeBpm, 95);
  assert.equal(player.activeSong.durationSeconds, 45);
  assert.equal(player.activeSong.keyOffset, 3);
  assert.equal(player.activeSong.speed, 1.5);
  assert.deepEqual(player.activeSong.keyMarkers, [marker]);
  assert.equal(player.status, 'Stopped');
  assert.equal(player._currentLoadedPath, null, 'Reading notes must not mark the playback engine as loaded');
  assert.equal(playbackCalls.some(([method]) => ['play', 'loadMidiFile', 'seek'].includes(method)), false);
});

test('Studio can scrub the startup preview without seeking an empty engine', async () => {
  const { player, songs, playbackCalls } = await createPlayer({
    native: true, readMidi: async () => parsedMidi(songs[0]),
  });
  player.loadSong(songs[0]);
  await new Promise(resolve => setImmediate(resolve));
  player.beginScrub();
  await player.seek(12);
  await player.seek(18.5);
  player.endScrub();
  await new Promise(resolve => setTimeout(resolve, 25));
  assert.equal(player.currentTimeSeconds, 18.5);
  assert.equal(player.isScrubbing, false);
  assert.equal(player.status, 'Stopped');
  assert.equal(player.positionGate.accepts({ timeMs: 0, seekId: 0 }), false);
  assert.equal(playbackCalls.some(([method]) => ['seek', 'loadMidiFile', 'play'].includes(method)), false);
  await player.seek(100);
  assert.equal(player.currentTimeSeconds, 45, 'Preview seeks respect the parsed duration');
  await player.seek(-1);
  assert.equal(player.currentTimeSeconds, 0);
});

test('first Play preserves the Studio preview position and seeks the loaded engine before playing', async () => {
  const { player, songs, playbackCalls } = await createPlayer({ native: true });
  const song = parsedMidi(songs[0]);
  player.loadSong(song);
  await player.seek(18.5);
  await player.play();
  const methods = playbackCalls.map(([method]) => method);
  assert.ok(methods.indexOf('loadMidiFile') < methods.indexOf('seek'));
  assert.ok(methods.indexOf('seek') < methods.indexOf('play'));
  assert.equal(playbackCalls.find(([method]) => method === 'seek')[1], 18500);
  assert.equal(player.currentTimeSeconds, 18.5);
  assert.equal(player.activeSong.notes.length, 1);
  assert.equal(player.status, 'Playing');

  await player.pause();
  const firstSeek = player.seek(20);
  const lastSeek = player.seek(22);
  await Promise.all([firstSeek, lastSeek]);
  const seeks = playbackCalls.filter(([method]) => method === 'seek');
  assert.equal(seeks.length, 2, 'Loaded-engine scrubs still coalesce into one seek per frame');
  assert.equal(seeks.at(-1)[1], 22000);
  assert.equal(player.positionGate.accepts({ timeMs: 22000, seekId: seeks.at(-1)[2] }), true);
  await player.play();
  assert.equal(player.currentTimeSeconds, 22);
  assert.equal(playbackCalls.filter(([method]) => method === 'loadMidiFile').length, 1);
  assert.equal(playbackCalls.filter(([method]) => method === 'seek').length, 2);

  await player.play(song);
  assert.equal(player.currentTimeSeconds, 0, 'Explicitly playing the song again still restarts it');
  assert.equal(playbackCalls.filter(([method]) => method === 'seek').at(-1)[1], 0);
});

test('a different song preview never seeks the previously loaded engine song', async () => {
  const { player, songs, playbackCalls } = await createPlayer({ native: true });
  await player.play(parsedMidi(songs[0]));
  await player.stop();
  player.loadSong(parsedMidi(songs[1]));
  await player.seek(15);
  assert.equal(playbackCalls.some(([method]) => method === 'seek'), false);
  assert.equal(player._currentLoadedPath, songs[0].filePath);
  await player.play();
  assert.equal(player._currentLoadedPath, songs[1].filePath);
  assert.equal(playbackCalls.filter(([method]) => method === 'seek').at(-1)[1], 15000);
  assert.equal(player.currentTimeSeconds, 15);
});

test('seeking while the first engine load is pending starts playback at the latest preview position', async () => {
  let complete;
  const { player, songs, playbackCalls } = await createPlayer({
    native: true,
    playback: { loadMidiFile: () => new Promise(resolve => { complete = resolve; }) },
  });
  player.loadSong(parsedMidi(songs[0]));
  await player.seek(5);
  const playing = player.play();
  await new Promise(resolve => setImmediate(resolve));
  await player.seek(27);
  assert.equal(playbackCalls.some(([method]) => method === 'seek'), false);
  complete(true);
  await playing;
  assert.equal(playbackCalls.filter(([method]) => method === 'seek').at(-1)[1], 27000);
  assert.equal(player.currentTimeSeconds, 27);
});

test('a late Studio note read cannot overwrite a newer song selection', async () => {
  const pending = new Map();
  const { player, songs } = await createPlayer({
    native: true, readMidi: path => new Promise(resolve => pending.set(path, resolve)),
  });
  player.loadSong(songs[0]);
  player.loadSong(songs[1]);
  pending.get('b.mid')(parsedMidi(songs[1], 72));
  await new Promise(resolve => setImmediate(resolve));
  pending.get('a.mid')(parsedMidi(songs[0], 60));
  await new Promise(resolve => setImmediate(resolve));
  assert.equal(player.activeSong.id, 'b');
  assert.equal(player.activeSong.notes[0].noteNumber, 72);
});

test('reselecting metadata for the same song retains parsed Studio notes', async () => {
  let reads = 0;
  const { player, songs } = await createPlayer({
    native: true, readMidi: async () => { reads++; return parsedMidi(songs[0]); },
  });
  player.loadSong(songs[0]);
  await new Promise(resolve => setImmediate(resolve));
  player.loadSong({ ...songs[0], disabledTracks: '2' });
  assert.equal(player.activeSong.notes.length, 1);
  assert.equal(player.activeSong.tracks[0].isMuted, true);
  assert.equal(reads, 1);
});

test('songs with parsed notes or missing files do not trigger another Studio file read', async () => {
  let reads = 0;
  const { player, songs } = await createPlayer({ native: true, readMidi: async () => { reads++; } });
  player.loadSong(parsedMidi(songs[0]));
  player.loadSong({ ...songs[1], isMissing: true });
  await new Promise(resolve => setImmediate(resolve));
  assert.equal(reads, 0);
});

test('hold-note timing reaches the engine and persists in the selected song scope', async () => {
  const { player, songs, playbackCalls, songSettingsWrites } = await createPlayer();
  player.loadSong({ ...songs[0], holdNotes: true, tapThresholdMs: 125 });
  assert.deepEqual(playbackCalls.find(([method]) => method === 'setNoteTiming'), ['setNoteTiming', true, 125]);
  player.setTapThresholdMs(240);
  player.setHoldNotes(false);
  assert.deepEqual(playbackCalls.filter(([method]) => method === 'setNoteTiming').at(-1), ['setNoteTiming', false, 240]);
  for (const invalid of [NaN, -1, 0.5, 10001]) player.setTapThresholdMs(invalid);
  assert.equal(player.tapThresholdMs, 240);
  await player.flushPendingSongSettings();
  assert.equal(songSettingsWrites[0][1].tapThresholdMs, 240);
  assert.equal(songSettingsWrites[0][1].holdNotes, false);
  assert.deepEqual(songSettingsWrites[0][2], { kind: 'instrument', gameId: 'game-a', instrumentId: 'lyre' });
  await player.applyEditedSong({ ...songs[0], holdNotes: true, tapThresholdMs: 80 });
  assert.deepEqual(playbackCalls.filter(([method]) => method === 'setNoteTiming').at(-1), ['setNoteTiming', true, 80]);
  player.loadSong(songs[1]);
  assert.equal(player.tapThresholdMs, 0, 'An older song must not inherit the previous threshold');
});

test('a deleted game configuration blocks playback until the selection is replaced', async () => {
  const { player, songs, playbackCalls, gameStore } = await createPlayer();
  gameStore.hasDeletedSelection = true;
  gameStore.layoutDeletionTooltip = 'The entry for this keyboard layout has been deleted.';
  await player.play(songs[0]);
  assert.equal(playbackCalls.some(([method]) => method === 'play'), false);
  gameStore.hasDeletedSelection = false;
  await player.play(songs[0]);
  assert.equal(playbackCalls.some(([method]) => method === 'play'), true);
});

test('library removal flushes settings and queued writes before closing only affected playback', async () => {
  let finishQueueWrite;
  const h = await createPlayer({ writeQueue: () => new Promise(resolve => { finishQueueWrite = resolve; }) });
  h.player.loadSong(h.songs[0]);
  await h.player.setPlaybackSpeed(1.25);
  const queued = h.player.addToQueue(h.songs.slice(0, 2));
  await new Promise(resolve => setImmediate(resolve));
  let finished = false;
  const preparing = h.player.prepareSongRemoval(['a']).then(() => { finished = true; });
  await new Promise(resolve => setImmediate(resolve));
  assert.equal(finished, false);
  finishQueueWrite();
  await queued;
  await preparing;
  assert.equal(h.player.activeSong, null);
  assert.equal(h.songSettingsWrites[0][0], 'a');
  assert.equal(h.songSettingsWrites[0][1].speed, 1.25);
  assert.ok(h.playbackCalls.some(([method]) => method === 'stop'));
  h.player.loadSong(h.songs[1]);
  await h.player.prepareSongRemoval(['a']);
  assert.equal(h.player.activeSong.id, 'b');
});

test('a debounced Studio save captures the original song, instrument, and settings', async () => {
  const { player, songs, gameStore, songSettingsWrites } = await createPlayer();
  player.loadSong(songs[0]);
  await player.setPlaybackSpeed(1.5);
  gameStore.songSettingsContext = { gameId: 'game-b', instrumentId: 'piano' };
  player.loadSong(songs[1]);
  await player.flushPendingSongSettings();
  assert.equal(songSettingsWrites.length, 1);
  assert.equal(songSettingsWrites[0][0], 'a');
  assert.equal(songSettingsWrites[0][1].speed, 1.5);
  assert.deepEqual(songSettingsWrites[0][2], { kind: 'instrument', gameId: 'game-a', instrumentId: 'lyre' });
  assert.equal(songs[0].speed, 1.5, 'Queue and library copies retain the saved setting');
  assert.equal(player.playbackSpeed, 1);
});

test('Studio never falls back to a global save without a selected game instrument', async () => {
  const { player, songs, gameStore, songSettingsWrites } = await createPlayer();
  gameStore.songSettingsContext = null;
  player.loadSong(songs[0]);
  await player.setPlaybackSpeed(1.5);
  await player.flushPendingSongSettings();
  assert.equal(songSettingsWrites.length, 0);
});

test('switching instruments flushes old edits before loading the new instrument settings', async () => {
  let release;
  const gate = new Promise(resolve => { release = resolve; });
  const { player, songs, gameStore, songSettingsWrites, playbackCalls } = await createPlayer({
    writeSongSettings: () => gate,
    readSong: id => ({ ...songs.find(song => song.id === id), speed: 0.75, keyOffset: -3,
      disabledTracks: '1', keyMarkers: [], autoChord: true, enableKeyChangeMarkers: false }),
  });
  songs[0].tracks = [{ index: 0, isMuted: true }, { index: 1, isMuted: false }];
  player.loadSong(songs[0]);
  player.trackVolumes[0] = 0;
  player.setBaseKeyOffset(4);
  gameStore.songSettingsContext = { gameId: 'game-a', instrumentId: 'piano' };
  const refreshing = player.refreshSongSettingsContext();
  assert.equal(player.isChangingSongContext, true);
  await new Promise(resolve => setImmediate(resolve));
  assert.equal(songSettingsWrites[0][2].instrumentId, 'lyre');
  release();
  await refreshing;
  assert.equal(player.playbackSpeed, 0.75);
  assert.equal(player.activeSong.keyOffset, -3);
  assert.equal(player.activeSong.tracks[0].isMuted, false);
  assert.equal(player.activeSong.tracks[1].isMuted, true);
  assert.equal(player.autoChord, true);
  assert.equal(player.enableKeyChangeMarkers, false);
  assert.equal(player.getTrackVolume(0), 1);
  assert.ok(playbackCalls.some(([method, track, volume]) => method === 'setTrackVolume' && track === 0 && volume === 1));
  assert.ok(playbackCalls.some(([method, track, muted]) => method === 'setTrackMuted' && track === 1 && muted));
  await player.setPlaybackSpeed(1.25);
  await player.flushPendingSongSettings();
  assert.equal(songSettingsWrites[1][2].instrumentId, 'piano');
});

test('update preparation stops playback and flushes debounced settings and queued writes', async () => {
  let releaseQueue;
  const queueGate = new Promise(resolve => { releaseQueue = resolve; });
  const { player, songs, playbackCalls, songSettingsWrites } = await createPlayer({ writeQueue: () => queueGate });
  const queued = player.setQueueList(songs.slice(0, 2));
  player.activeSong = songs[0];
  player.playbackSpeed = 1.5;
  player.scheduleAutoSaveSongSettings();
  let finished = false;
  const preparing = player.prepareForUpdate().then(() => { finished = true; });
  await new Promise(resolve => setImmediate(resolve));
  assert.equal(songSettingsWrites.length, 1);
  assert.equal(songSettingsWrites[0][1].speed, 1.5);
  assert.ok(playbackCalls.some(([method]) => method === 'stop'));
  assert.equal(finished, false);
  releaseQueue();
  await Promise.all([queued, preparing]);
  assert.equal(finished, true);
});

test('a failed song settings save prevents update preparation from succeeding', async () => {
  let fail = true;
  const { player, songs } = await createPlayer({ writeSongSettings: async () => {
    if (fail) throw Object.assign(new Error('Database is busy'), { stack: 'Error: Database is busy' });
  } });
  player.activeSong = songs[0];
  player.scheduleAutoSaveSongSettings();
  await assert.rejects(player.prepareForUpdate(), /Database is busy/);
  fail = false;
  await player.prepareForUpdate();
});

test('an unsaved queue blocks updating until it can be saved', async () => {
  let fail = true;
  const { player, songs } = await createPlayer({ writeQueue: async () => {
    if (fail) throw Object.assign(new Error('Queue write failed'), { stack: 'Error: Queue write failed' });
  } });
  await player.setQueueList(songs.slice(0, 2));
  await assert.rejects(player.prepareForUpdate(), /Queue write failed/);
  fail = false;
  await player.prepareForUpdate();
});

test('playing one library song queues only that song, including the initially selected song', async () => {
  const { player, songs } = await createPlayer();
  player.activeSong = songs[0];
  await player.play();
  assert.deepEqual(ids(player.queue), ['a']);
  await player.play(songs[2]);
  assert.deepEqual(ids(player.queue), ['a', 'c']);
  assert.equal(player.activeQueueIndex, 1);
});

test('playlist play replaces an existing queue even when its first song is already active', async () => {
  const { player, songs, writes } = await createPlayer();
  await player.play(songs[1]);
  await player.addToQueue(songs[4]);
  await player.playPlaylist(songs.slice(1, 4), 'favorites');
  assert.deepEqual(ids(player.queue), ['b', 'c', 'd']);
  assert.deepEqual(writes.at(-1), ['b', 'c', 'd']);
  assert.equal(player.activeSong.id, 'b');
  assert.equal(player.queuePlaylistId, 'favorites');
  await player.playPlaylist(songs.slice(1, 4), 'favorites');
  assert.equal(player.status, 'Paused');
  await player.playPlaylist(songs.slice(1, 4), 'favorites');
  assert.equal(player.status, 'Playing');
});

test('play next moves songs before the current song without corrupting its index and preserves selection order', async () => {
  const { player, songs } = await createPlayer();
  await player.setQueueList(songs);
  await player.play(songs[2]);
  await player.playNext([songs[0], songs[1], songs[2]]);
  assert.deepEqual(ids(player.queue), ['c', 'a', 'b', 'd', 'e']);
  assert.equal(player.activeQueueIndex, 0);
  await player.nextTrack();
  assert.equal(player.activeSong.id, 'a');
});

test('add to queue appends in order, ignores duplicates, and does not interrupt playback', async () => {
  const { player, songs } = await createPlayer();
  await player.play(songs[0]);
  await player.addToQueue([songs[1], songs[2], songs[1]]);
  await player.addToQueue(songs[1]);
  assert.deepEqual(ids(player.queue), ['a', 'b', 'c']);
  assert.equal(player.activeSong.id, 'a');
  assert.equal(player.status, 'Playing');
});

test('empty queue edits stay idle and play next starts at the front', async () => {
  const { player, songs } = await createPlayer();
  await player.addToQueue(songs[2]);
  await player.playNext(songs.slice(0, 2));
  assert.deepEqual(ids(player.queue), ['a', 'b', 'c']);
  assert.equal(player.activeSong, null);
  assert.equal(player.status, 'Stopped');
  await player.play();
  assert.equal(player.activeSong.id, 'a');
});

test('play next can queue the initially selected library song before playback starts', async () => {
  const { player, songs } = await createPlayer();
  player.activeSong = songs[0];
  await player.playNext(songs[0]);
  assert.deepEqual(ids(player.queue), ['a']);
  assert.equal(player.status, 'Stopped');
});

test('playing a song outside the current playlist lets its Play button reload that playlist', async () => {
  const { player, songs } = await createPlayer();
  await player.playPlaylist(songs.slice(0, 2), 'favorites');
  await player.play(songs[4]);
  assert.equal(player.queuePlaylistId, null);
  await player.playPlaylist(songs.slice(0, 2), 'favorites');
  assert.deepEqual(ids(player.queue), ['a', 'b']);
  assert.equal(player.activeSong.id, 'a');
  assert.equal(player.status, 'Playing');
});

test('queue navigation honors loop modes and Previous history', async () => {
  const { player, songs } = await createPlayer();
  await player.setQueueList(songs.slice(0, 2));
  await player.play(songs[0]);
  await player.nextTrack();
  await player.prevTrack();
  assert.equal(player.activeSong.id, 'a');
  player.loopMode = 'single';
  await player.handleSongFinished();
  assert.equal(player.activeSong.id, 'a');
  await player.nextTrack();
  assert.equal(player.activeSong.id, 'b');
  player.loopMode = 'all';
  await player.handleSongFinished();
  assert.equal(player.activeSong.id, 'a');
  player.loopMode = 'off';
  await player.nextTrack();
  await player.nextTrack();
  assert.equal(player.status, 'Stopped');
});

test('shuffle advances through its stable order and honors play next', async () => {
  const { player, songs } = await createPlayer();
  await player.setQueueList(songs.slice(0, 4));
  await player.play(songs[0]);
  player.toggleShuffle();
  await player.playNext(songs[4]);
  const expected = ids(player.shuffledQueue);
  assert.equal(expected[1], 'e');
  for (const id of expected.slice(1)) {
    await player.nextTrack(false);
    assert.equal(player.activeSong.id, id);
    assert.deepEqual(ids(player.shuffledQueue), expected);
  }
  await player.handleSongFinished();
  assert.equal(player.status, 'Stopped');
});

test('removing the active song keeps its successor after native queue reloads', async () => {
  const { player, songs } = await createPlayer();
  await player.setQueueList(songs);
  await player.play(songs[2]);
  await player.removeMultipleFromQueue(['a', 'c']);
  await player.loadQueueFromDb();
  assert.equal(player.activeSong.id, 'c');
  await player.nextTrack(false);
  assert.equal(player.activeSong.id, 'd');
  await player.removeMultipleFromQueue(['d']);
  await player.removeMultipleFromQueue(['e']);
  await player.loadQueueFromDb();
  await player.nextTrack();
  assert.equal(player.status, 'Stopped');
});

test('rapid edits are persisted sequentially and a stale read cannot resurrect cleared songs', async () => {
  const resolvers = [];
  let staleRead;
  const { player, songs, writes } = await createPlayer({
    writeQueue: () => new Promise(resolve => resolvers.push(resolve)),
    readQueue: () => new Promise(resolve => { staleRead = resolve; }),
  });
  const read = player.loadQueueFromDb();
  await Promise.resolve();
  const first = player.addToQueue(songs[0]);
  const second = player.addToQueue(songs[1]);
  const clear = player.clearQueue();
  await Promise.resolve();
  assert.deepEqual(writes, [['a']]);
  staleRead([songs[4]]);
  await read;
  assert.deepEqual(ids(player.queue), []);
  resolvers.shift()();
  await first;
  await Promise.resolve();
  resolvers.shift()();
  await second;
  await Promise.resolve();
  resolvers.shift()();
  await clear;
  assert.deepEqual(writes, [['a'], ['a', 'b'], []]);
});

test('play next still takes priority when the playing song has been removed', async () => {
  const { player, songs } = await createPlayer();
  await player.setQueueList(songs.slice(0, 3));
  await player.play(songs[1]);
  await player.removeMultipleFromQueue(['b']);
  await player.playNext(songs[3]);
  await player.loadQueueFromDb();
  await player.nextTrack();
  assert.equal(player.activeSong.id, 'd');
  await player.nextTrack();
  assert.equal(player.activeSong.id, 'c');
});

test('editing a song synchronizes every displayed copy and keeps parsed notes and tracks', async () => {
  const { player, songs, playbackCalls, libraryStore, gameStore } = await createPlayer();
  gameStore.activeInstrument = { id: 'lyre', keyCount: 21 };
  const song = songs[1];
  libraryStore.songsByPlaylist.favorites = [{ ...song }];
  await player.play(song);
  player.activeSong.tracks = [{ index: 7 }];
  await player.applyEditedSong({ ...song, title: 'Updated', baseKey: -2, baseKeyOffset: -2,
    keyOffset: 5, bpmOverride: 180, speed: 1.5, mergeNotes: true, mergeToleranceMs: 250, holdNotes: true });
  assert.equal(player.activeSong.title, 'Updated');
  assert.equal(player.queue[0].title, 'Updated');
  assert.equal(libraryStore.songsByPlaylist.favorites[0].mergeToleranceMs, 250);
  assert.equal(player.activeSong.tracks[0].index, 7);
  assert.deepEqual(playbackCalls.filter(call => call[0] === 'setSpeed').at(-1), ['setSpeed', 2.25]);
  assert.deepEqual(playbackCalls.filter(call => call[0] === 'setKeyOffset').at(-1), ['setKeyOffset', 3]);
});

test('switching songs while playing resets currentTimeSeconds and ignores stale time updates from previous song', async () => {
  let timeCallback;
  const { player, songs, playbackCalls } = await createPlayer({
    native: true,
    playback: {
      onTimeUpdate: (cb) => { timeCallback = cb; return () => {}; },
      loadMidiFile: async (path) => {
        // Simulate in-flight time update from the old song arriving before load completes
        if (timeCallback) {
          timeCallback({ timeMs: 45000, seekId: 0 });
        }
        return parsedMidi(songs.find(s => s.filePath === path));
      },
    },
  });
  await player.play(parsedMidi(songs[0]));
  player.currentTimeSeconds = 40;
  // Now switch directly to song 1 while playing
  await player.play(parsedMidi(songs[1]));
  assert.equal(player.activeSong.id, 'b');
  assert.equal(player.currentTimeSeconds, 0, 'New song must start at 0:00 without carrying over old song position');
  const songBSeeks = playbackCalls.filter(([method, ms]) => method === 'seek' && ms > 0);
  assert.equal(songBSeeks.length, 0, 'New song must not seek to old song position');
});
