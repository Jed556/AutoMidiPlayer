import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { stripTypeScriptTypes } from 'node:module';
import { compileModule } from 'svelte/compiler';
import { normalizeKeyboardLayout, instrumentNotes } from '../src/shared/utils/instrumentLayout.ts';

const source = stripTypeScriptTypes(readFileSync(new URL('../src/renderer/lib/state/gameStore.svelte.ts', import.meta.url), 'utf8'), { mode: 'transform' })
  .replace('../../../shared/utils/instrumentLayout.js', new URL('../src/shared/utils/instrumentLayout.ts', import.meta.url).href);
let harnessId = 0;

async function createGameStore(selectedInstrumentIds = {}, configureGames = () => {}, initialStatuses = [], selectedLayoutIds = {}) {
  const contexts = [];
  const settingsWrites = [];
  const mappings = [];
  const notifications = [];
  const stopped = [];
  const mappingSources = [];
  const mixedMappings = [];
  const pedalWrites = [];
  const modeChanges = [];
  let applyMapping = async () => true;
  let pedalWriteError;
  let onConfigsChanged;
  let onStatusChanged;
  let readStatuses = async () => initialStatuses;
  const games = ['a', 'b'].map(id => ({ id, displayName: id, defaultInstrument: 'piano',
    instruments: [{ id: 'lyre', name: 'Lyre', notes: [60, 62], keyboardLayouts: ['qwerty', 'azerty'] },
      { id: 'piano', name: 'Piano', notes: [60, 61], keyboardLayouts: ['qwerty', 'azerty'] }],
    layouts: [{ id: 'qwerty', name: 'QWERTY', keys: ['A', 'S'] }, { id: 'azerty', name: 'AZERTY', keys: ['Q', 'S'] }],
  }));
  configureGames(games);
  let snapshot = { games, issues: [] };
  let readConfigs = async () => snapshot;
  const api = {
    games: {
      getConfigs: () => readConfigs(),
      getGameStatuses: () => {
        assert.equal(typeof onStatusChanged, 'function', 'Subscribe before reading statuses');
        return readStatuses();
      },
      onGameStatusChanged: async callback => {
        await Promise.resolve();
        onStatusChanged = callback;
        return () => {};
      },
      reloadConfigs: () => readConfigs(),
      saveLayoutPedalBindings: async (id, bindings) => {
        if (pedalWriteError) throw new Error(pedalWriteError);
        pedalWrites.push([id, bindings]);
        for (const game of snapshot.games) {
          const layout = game.layouts.find(layout => layout.id === id);
          if (layout) Object.assign(layout, bindings ?? { sustainKey: 'Space', sostenutoKey: null, unaCordaKey: null });
        }
        return true;
      },
      onConfigsChanged: async callback => { onConfigsChanged = callback; return () => {}; },
      setActiveGame: async () => true,
      setGameInstrumentMapping: async (layout, pairs, paths, mixed) => {
        mappings.push([layout, pairs]);
        mappingSources.push(paths);
        mixedMappings.push(mixed);
        return applyMapping(layout);
      },
    },
    playback: { setGuitarMode: async mode => { modeChanges.push(mode); return true; }, stop: async () => { stopped.push(true); return true; } },
    songs: { setSettingsContext: context => contexts.push(context) },
    settings: {
      getSettings: async () => ({ selectedInstrumentIds, selectedLayoutIds }),
      updateSettings: async settings => { settingsWrites.push(settings); return true; },
    },
  };
  const key = `__gameSettingsHarness${++harnessId}`;
  globalThis[key] = { api, notificationCenter: { show: toast => notifications.push(toast) } };
  const compiled = compileModule(source.replace("import { api } from '../backend/index.js';", `const { api, notificationCenter } = globalThis.${key};`)
    .replace("import { notificationCenter } from '../ui/notificationCenter.svelte';", ''),
    { filename: 'gameStore.svelte.js', generate: 'client' }).js.code
    .replace("'svelte/internal/client'", JSON.stringify(import.meta.resolve('svelte/internal/client')));
  const { gameStore } = await import(`data:text/javascript;base64,${Buffer.from(compiled).toString('base64')}`);
  delete globalThis[key];
  await gameStore.init();
  return { gameStore, contexts, settingsWrites, mappings, notifications, stopped, mappingSources, mixedMappings, pedalWrites, modeChanges,
    setMappingHandler(handler) { applyMapping = handler; },
    failPedalWrite(error) { pedalWriteError = error; },
    setSnapshot(value) { snapshot = value; },
    setReader(reader) { readConfigs = reader; },
    setStatusReader(reader) { readStatuses = reader; },
    emitStatuses(statuses) { onStatusChanged(statuses); },
    emitChange() { onConfigsChanged(); return gameStore.refreshConfigs(); },
  };
}

test('pedal edits use the layout-file API, refresh playback, and leave app settings alone', async () => {
  const h = await createGameStore();
  const beforeSettings = h.settingsWrites.length;
  const beforeMappings = h.mappings.length;
  await h.gameStore.saveLayoutPedalBindings('qwerty', { sustainKey: null, sostenutoKey: 'Shift' });
  assert.deepEqual(h.pedalWrites, [['qwerty', { sustainKey: null, sostenutoKey: 'Shift' }]]);
  assert.equal(h.gameStore.activeLayout.sustainKey, null);
  assert.equal(h.gameStore.activeLayout.sostenutoKey, 'Shift');
  assert.equal(h.settingsWrites.length, beforeSettings);
  assert.ok(h.mappings.length > beforeMappings);
  await h.gameStore.saveLayoutPedalBindings('qwerty', null);
  assert.equal(h.gameStore.activeLayout.sustainKey, 'Space');
  assert.equal(h.gameStore.activeLayout.sostenutoKey, null);
  h.failPedalWrite('File is read-only');
  const afterReset = h.mappings.length;
  await assert.rejects(h.gameStore.saveLayoutPedalBindings('qwerty', { sustainKey: 'Tab' }), /read-only/);
  assert.equal(h.gameStore.activeLayout.sustainKey, 'Space');
  assert.equal(h.mappings.length, afterReset);
});

test('manual-focus desktop capability follows native status and clears when the session changes', async () => {
  const h = await createGameStore();
  h.emitStatuses([{ id: 'a', isRunning: false, isFocused: false, requiresManualFocus: true }]);
  assert.equal(h.gameStore.activeGame.requiresManualFocus, true);
  assert.equal(h.gameStore.games[0].requiresManualFocus, true);
  h.emitStatuses([{ id: 'a', isRunning: true, isFocused: true }]);
  assert.equal(h.gameStore.activeGame.requiresManualFocus, false);
});

test('layout choices survive restart for each game and instrument', async () => {
  const first = await createGameStore({ a: 'lyre' });
  first.gameStore.syncActiveInstrumentMapping('lyre', 'azerty');
  first.gameStore.syncActiveInstrumentMapping('piano', 'qwerty');
  first.gameStore.setActiveGame('b');
  first.gameStore.syncActiveInstrumentMapping('piano', 'azerty');
  const savedLayouts = first.settingsWrites.filter(write => write.selectedLayoutIds).at(-1).selectedLayoutIds;
  const second = await createGameStore({ a: 'lyre', b: 'piano' }, () => {}, [], savedLayouts);
  assert.equal(second.gameStore.activeLayout.id, 'azerty');
  second.gameStore.syncActiveInstrumentMapping('piano');
  assert.equal(second.gameStore.activeLayout.id, 'qwerty');
  second.gameStore.setActiveGame('b');
  assert.equal(second.gameStore.activeLayout.id, 'azerty');
});

test('native mappings use the instrument matrix and preserve null positions and shifted bindings', async () => {
  const { mappings } = await createGameStore({}, games => {
    games[0].instruments[1] = { id: 'piano', name: 'Piano', layoutStyle: 'heartopia',
      keys: [[72, null, 73], [60, 61]], keyboardLayouts: ['qwerty'] };
    games[0].layouts[0] = { id: 'qwerty', name: 'QWERTY', keys: ['q', 'Q', 'a', 'A'],
      keyRows: [['q', null, 'Q'], ['a', 'A']] };
  });
  assert.deepEqual(mappings[0], ['qwerty', [[72, 'q'], [73, 'Q'], [60, 'a'], [61, 'A']]]);
});

test('initial detection and background launch, focus and exit snapshots update exact game IDs', async () => {
  const { gameStore, emitStatuses } = await createGameStore({}, games => {
    games[1].displayName = 'a'; // Display-name collisions must not apply another game status.
  }, [{ id: 'a', isRunning: true, isFocused: false, pid: 10 }]);
  assert.equal(gameStore.activeGame.isRunning, true);
  assert.equal(gameStore.activeGame.pid, 10);
  assert.equal(gameStore.games[1].isRunning, false);
  emitStatuses([{ id: 'b', isRunning: true, isFocused: true, pid: 20 }]);
  assert.equal(gameStore.games[1].isRunning, true);
  assert.equal(gameStore.games[1].isFocused, true);
  assert.equal(gameStore.activeGame.id, 'a', 'Detection preserves the manually selected game');
  assert.equal(gameStore.activeGame.isRunning, false);
  assert.equal(gameStore.activeGame.pid, undefined);
  emitStatuses([]);
  assert.equal(gameStore.games[1].isRunning, false);
  assert.equal(gameStore.games[1].isFocused, false);
  assert.equal(gameStore.games[1].pid, undefined);
});

test('a late status response cannot overwrite a newer process event during config reload', async () => {
  const harness = await createGameStore();
  let resolveRead;
  let started;
  const reading = new Promise(resolve => { started = resolve; });
  harness.setStatusReader(() => { started(); return new Promise(resolve => { resolveRead = resolve; }); });
  const refreshing = harness.gameStore.refreshConfigs();
  await reading;
  harness.emitStatuses([{ id: 'a', isRunning: true, isFocused: true, pid: 22 }]);
  resolveRead([{ id: 'a', isRunning: false, isFocused: false, pid: null }]);
  await refreshing;
  assert.equal(harness.gameStore.activeGame.isRunning, true);
  assert.equal(harness.gameStore.activeGame.pid, 22);
});

test('config refresh retains running state until a fresh detection snapshot arrives', async () => {
  const harness = await createGameStore({}, () => {}, [{ id: 'a', isRunning: true, isFocused: true, pid: 33 }]);
  const transitions = [];
  harness.gameStore.onGameStatusChange(game => transitions.push([game.isRunning, game.isFocused]));
  harness.setSnapshot({ games: harness.gameStore.games.map(game => ({ ...game,
    isRunning: false, isFocused: false, pid: undefined })), issues: [] });
  await harness.gameStore.refreshConfigs();
  assert.ok(transitions.length > 1);
  assert.ok(transitions.every(([running, focused]) => running && focused),
    'A profile edit must not briefly pause playback by reporting the game as stopped');
});

test('game defaults and saved instrument choices set the song settings context before loading songs', async () => {
  const first = await createGameStore();
  assert.equal(first.gameStore.activeInstrument.id, 'piano');
  assert.deepEqual(first.contexts[0], { gameId: 'a', instrumentId: 'piano' });
  const saved = await createGameStore({ a: 'lyre' });
  assert.equal(saved.gameStore.activeInstrument.id, 'lyre');
  assert.deepEqual(saved.contexts[0], { gameId: 'a', instrumentId: 'lyre' });
});

test('game summaries keep each game instrument and layout choice when another game is active', async () => {
  const { gameStore } = await createGameStore({ a: 'lyre' });
  const [first, second] = gameStore.games;
  second.layouts[0].name = 'Second game QWERTY';
  gameStore.syncActiveInstrumentMapping('lyre', 'azerty');
  gameStore.setActiveGame('b');
  assert.equal(gameStore.getGameInstrument(first).name, 'Lyre');
  assert.equal(gameStore.getGameLayout(first).name, 'AZERTY');
  assert.equal(gameStore.getGameInstrument(second), gameStore.activeInstrument);
  assert.equal(gameStore.getGameLayout(second), gameStore.activeLayout);
  assert.equal(gameStore.getGameLayout(second).name, 'Second game QWERTY');
  gameStore.setActiveGame('a');
  assert.equal(gameStore.activeInstrument.id, 'lyre');
  assert.equal(gameStore.activeLayout.id, 'azerty');
});

test('game summaries respect instrument compatibility, game defaults and deleted choices', async () => {
  const { gameStore } = await createGameStore();
  const second = gameStore.games[1];
  second.defaultInstrument = 'Lyre';
  second.defaultLayout = 'AZERTY';
  assert.equal(gameStore.getGameInstrument(second).id, 'lyre');
  assert.equal(gameStore.getGameLayout(second).id, 'azerty');
  second.instruments[0].keyboardLayouts = ['qwerty'];
  assert.equal(gameStore.getGameLayout(second).id, 'qwerty');
  gameStore.setActiveGame('b');
  gameStore.setActiveGame('a');
  second.layouts = second.layouts.filter(layout => layout.id !== 'qwerty');
  assert.equal(gameStore.getGameLayout(second), null, 'A deleted saved layout must not show another layout');
  second.instruments = second.instruments.filter(instrument => instrument.id !== 'lyre');
  assert.equal(gameStore.getGameInstrument(second), null, 'A deleted saved instrument must not show another instrument');
  assert.equal(gameStore.getGameLayout(second), null);
});

test('switching instruments persists each game choice and notifies settings once per context', async () => {
  const { gameStore, contexts, settingsWrites, mappings } = await createGameStore();
  const changes = [];
  gameStore.onSettingsContextChange(() => changes.push(gameStore.songSettingsContext));
  gameStore.syncActiveInstrumentMapping('lyre');
  await gameStore.syncActiveInstrumentMapping('lyre', 'azerty');
  assert.equal(changes.length, 1, 'Keyboard layout changes do not change the song settings scope');
  assert.equal(mappings.at(-1)[0], 'azerty');
  gameStore.setActiveGame('b');
  assert.equal(gameStore.activeInstrument.id, 'piano');
  gameStore.setActiveGame('a');
  assert.equal(gameStore.activeInstrument.id, 'lyre');
  assert.deepEqual(contexts.at(-1), { gameId: 'a', instrumentId: 'lyre' });
  assert.deepEqual(settingsWrites.at(-1).selectedInstrumentIds, { a: 'lyre', b: 'piano' });
});

test('startup recovers stale saved instruments to configured defaults, including inactive games', async () => {
  const { gameStore, settingsWrites } = await createGameStore({ a: 'removed', b: 'renamed' });
  assert.equal(gameStore.activeInstrument.id, 'piano');
  assert.equal(gameStore.getGameInstrument(gameStore.games[1]).id, 'piano');
  assert.deepEqual(gameStore.selectedInstrumentIds, { a: 'piano', b: 'piano' });
  assert.deepEqual(settingsWrites.at(-1).selectedInstrumentIds, { a: 'piano', b: 'piano' });
  assert.equal(gameStore.isInstrumentMissing, false);
});

test('a missing saved instrument without a valid default keeps its placeholder and an empty game clears the old context', async () => {
  const { gameStore, contexts, mappings } = await createGameStore({ a: 'removed' }, games => {
    games[0].defaultInstrument = 'also-removed';
  });
  assert.equal(gameStore.activeInstrument, null);
  assert.equal(gameStore.isInstrumentMissing, true);
  gameStore.activeGame = { id: 'empty', instruments: [] };
  gameStore.syncActiveInstrumentMapping();
  assert.equal(contexts.at(-1), null);
  assert.equal(gameStore.songSettingsContext, null);
  assert.deepEqual(mappings.at(-1), ['', []]);
});

test('the old Harry Potter instrument selection recovers to the bundled Piano default', async () => {
  const readConfig = path => JSON.parse(readFileSync(new URL(`../src-tauri/resources/defaults/${path}`, import.meta.url), 'utf8'));
  const { gameStore } = await createGameStore({ HPMA: 'hpma-great-hall-piano' }, games => {
    games[1] = {
      ...readConfig('Games/Harry Potter Magic Awakened/game.json'),
      instruments: ['Piano14Key', 'Piano36Key'].map(name =>
        readConfig(`Games/Harry Potter Magic Awakened/Instruments/Pianos/${name}.json`)),
      layouts: ['HPMA_QWERTY_14Key', 'HPMA_QWERTY_36Key'].map(name => readConfig(`KeyboardLayouts/${name}.json`)),
    };
  });
  const game = gameStore.games[1];
  assert.equal(gameStore.getGameInstrument(game).name, 'Piano');
  assert.equal(gameStore.getGameLayout(game).id, 'HPMA_QWERTY_14Key');
  assert.equal(gameStore.selectedInstrumentIds.HPMA, 'hpma-piano-14k');
});

test('live updates preserve selections, refresh notes and keys, and handle removal of every game', async () => {
  const harness = await createGameStore({ a: 'lyre' });
  const { gameStore, mappings, contexts } = harness;
  gameStore.setActiveGame('b');
  const refreshed = JSON.parse(JSON.stringify(gameStore.games));
  refreshed[1].instruments[1].notes = [70, 71];
  refreshed[1].layouts[0].keys = ['Z', 'X'];
  harness.setSnapshot({ games: refreshed, issues: [] });
  await harness.emitChange();
  assert.equal(gameStore.activeGame.id, 'b');
  assert.equal(gameStore.activeInstrument.id, 'piano');
  assert.deepEqual(mappings.at(-1), ['qwerty', [[70, 'Z'], [71, 'X']]]);
  harness.setSnapshot({ games: [], issues: [] });
  await harness.emitChange();
  assert.equal(gameStore.activeGame.isMissing, true);
  assert.equal(gameStore.activeGame.displayName, 'Missing Game');
  assert.equal(contexts.at(-1), null);
  assert.deepEqual(mappings.at(-1), ['', []]);
});

test('instruments without supported layouts never use another instrument layout', async () => {
  const { gameStore, mappings } = await createGameStore();
  gameStore.activeInstrument.keyboardLayouts = [];
  await gameStore.syncActiveInstrumentMapping();
  assert.deepEqual(gameStore.getInstrumentLayouts(gameStore.activeInstrument), []);
  assert.deepEqual(mappings.at(-1), ['', []]);
  assert.equal(gameStore.isLayoutMissing, false, 'An unassigned layout uses No Layouts, not a deletion warning');
  gameStore.activeInstrument.keyboardLayouts = ['missing'];
  await gameStore.syncActiveInstrumentMapping();
  assert.deepEqual(mappings.at(-1), ['', []]);
});

test('deleting the selected instrument stops playback without selecting another instrument', async () => {
  const harness = await createGameStore();
  const { gameStore, stopped, mappings, contexts } = harness;
  const original = JSON.parse(JSON.stringify(gameStore.games));
  const changed = JSON.parse(JSON.stringify(original));
  changed[0].instruments = changed[0].instruments.filter(i => i.id !== 'piano');
  harness.setSnapshot({ games: changed, issues: [] });
  await harness.emitChange();
  assert.equal(stopped.length, 1);
  assert.equal(gameStore.activeInstrument, null);
  assert.equal(gameStore.selectedInstrumentIds.a, 'piano');
  assert.equal(gameStore.isInstrumentMissing, true);
  assert.match(gameStore.instrumentDeletionTooltip, /instrument.*deleted/);
  assert.equal(contexts.at(-1), null);
  assert.deepEqual(mappings.at(-1), ['', []]);
  await harness.emitChange();
  assert.equal(stopped.length, 1, 'Repeated scans do not repeatedly stop playback');
  harness.setSnapshot({ games: original, issues: [] });
  await harness.emitChange();
  assert.equal(gameStore.activeInstrument.id, 'piano');
  assert.equal(gameStore.isInstrumentMissing, false);
  assert.equal(stopped.length, 1);
});

test('a missing saved layout can be replaced while a previous mapping is still being cleared', async () => {
  const h = await createGameStore({}, () => {}, [], { 'a/piano/keyboard': 'removed-layout' });
  assert.equal(h.gameStore.activeLayout, null);
  assert.equal(await h.gameStore.hasPlaybackMapping(), false);
  let finishClear;
  h.setMappingHandler(layout => layout ? Promise.resolve(true) : new Promise(resolve => { finishClear = resolve; }));
  const clear = h.gameStore.syncActiveInstrumentMapping();
  const ready = h.gameStore.hasPlaybackMapping();
  const replacement = h.gameStore.syncActiveInstrumentMapping('lyre', 'azerty');
  await new Promise(resolve => setImmediate(resolve));
  assert.equal(h.mappings.at(-1)[0], '');
  finishClear(false);
  assert.equal(await clear, false);
  assert.equal(await ready, true);
  assert.equal(await replacement, true);
  assert.deepEqual(h.mappings.at(-1), ['azerty', [[60, 'Q'], [62, 'S']]]);
  h.setMappingHandler(async () => false);
  await h.gameStore.syncActiveInstrumentMapping();
  assert.equal(await h.gameStore.hasPlaybackMapping(), false, 'Native rejection overrides a valid-looking selection');
});

test('deleting the selected layout leaves a missing placeholder even when another layout is available', async () => {
  const harness = await createGameStore();
  const { gameStore, stopped, mappings } = harness;
  const changed = JSON.parse(JSON.stringify(gameStore.games));
  changed[0].layouts = changed[0].layouts.filter(l => l.id !== 'qwerty');
  harness.setSnapshot({ games: changed, issues: [] });
  await harness.emitChange();
  assert.equal(stopped.length, 1);
  assert.equal(gameStore.activeLayout, null);
  assert.equal(gameStore.isLayoutMissing, true);
  assert.match(gameStore.layoutDeletionTooltip, /keyboard layout.*deleted/);
  assert.deepEqual(mappings.at(-1), ['', []]);
  gameStore.syncActiveInstrumentMapping(undefined, 'azerty');
  assert.equal(gameStore.activeLayout.id, 'azerty');
  assert.equal(gameStore.isLayoutMissing, false);
});

test('deleting the active game keeps Missing Game until it is restored or explicitly replaced', async () => {
  const harness = await createGameStore();
  const { gameStore, stopped, mappings } = harness;
  const original = JSON.parse(JSON.stringify(gameStore.games));
  harness.setSnapshot({ games: original.filter(g => g.id !== 'a'), issues: [] });
  await harness.emitChange();
  assert.equal(stopped.length, 1);
  assert.equal(gameStore.activeGame.id, 'a');
  assert.equal(gameStore.activeGame.displayName, 'Missing Game');
  assert.equal(gameStore.activeGame.iconUrl, undefined);
  assert.equal(gameStore.activeGame.imageFileName, undefined);
  assert.equal(gameStore.activeGame.isRunning, false);
  assert.equal(gameStore.hasDeletedSelection, true);
  assert.match(gameStore.gameDeletionTooltip, /game.*deleted/);
  assert.deepEqual(mappings.at(-1), ['', []]);
  harness.setSnapshot({ games: original, issues: [] });
  await harness.emitChange();
  assert.equal(gameStore.activeGame.displayName, 'a');
  assert.equal(gameStore.hasDeletedSelection, false);
  harness.setSnapshot({ games: original.filter(g => g.id !== 'a'), issues: [] });
  await harness.emitChange();
  gameStore.setActiveGame('b');
  assert.equal(gameStore.activeGame.id, 'b');
  assert.equal(gameStore.hasDeletedSelection, false);
});

test('the native deletion guard receives the files for the selected game, instrument and layout', async () => {
  const { gameStore, mappingSources } = await createGameStore();
  gameStore.activeGame.filePath = 'Games/a/game.json';
  gameStore.activeInstrument.filePath = 'Games/a/Instruments/Piano.json';
  gameStore.activeLayout.filePath = 'KeyboardLayouts/qwerty.json';
  await gameStore.syncActiveInstrumentMapping();
  assert.deepEqual(mappingSources.at(-1), ['Games/a/game.json', 'Games/a/Instruments/Piano.json', 'KeyboardLayouts/qwerty.json']);
});

test('deleting an unselected game, instrument or layout does not stop playback', async () => {
  const harness = await createGameStore();
  const changed = JSON.parse(JSON.stringify(harness.gameStore.games)).slice(0, 1);
  changed[0].instruments = changed[0].instruments.filter(i => i.id === 'piano');
  changed[0].layouts = changed[0].layouts.filter(l => l.id === 'qwerty');
  harness.setSnapshot({ games: changed, issues: [] });
  await harness.emitChange();
  assert.equal(harness.stopped.length, 0);
  assert.equal(harness.gameStore.activeInstrument.id, 'piano');
  assert.equal(harness.gameStore.activeLayout.id, 'qwerty');
});

test('config errors are reported once until corrected, then can be reported again', async () => {
  const harness = await createGameStore();
  const { gameStore, notifications } = harness;
  const issue = { path: 'Games/NTE/game.json', message: 'Malformed JSON: expected value at line 2 column 3' };
  harness.setSnapshot({ games: JSON.parse(JSON.stringify(gameStore.games)), issues: [issue] });
  await harness.emitChange();
  await harness.emitChange();
  assert.equal(notifications.length, 1);
  assert.match(notifications[0].message, /game.json.*Malformed JSON/);
  harness.setSnapshot({ games: JSON.parse(JSON.stringify(gameStore.games)), issues: [] });
  await harness.emitChange();
  harness.setSnapshot({ games: JSON.parse(JSON.stringify(gameStore.games)), issues: [issue] });
  await harness.emitChange();
  assert.equal(notifications.length, 2);
});

test('a config event during an outstanding read discards the stale snapshot', async () => {
  const harness = await createGameStore();
  let resolveRead;
  harness.setReader(() => new Promise(resolve => { resolveRead = resolve; }));
  const pending = harness.gameStore.refreshConfigs();
  harness.setReader(async () => ({ games: [], issues: [] }));
  const event = harness.emitChange();
  resolveRead({ games: JSON.parse(JSON.stringify(harness.gameStore.games)), issues: [] });
  await Promise.all([pending, event]);
  assert.equal(harness.gameStore.activeGame.isMissing, true);
  assert.deepEqual(harness.gameStore.games, []);
});

test('manual reload reports unsuccessful validation instead of claiming success', async () => {
  const harness = await createGameStore();
  harness.setSnapshot({ games: JSON.parse(JSON.stringify(harness.gameStore.games)),
    issues: [{ path: 'Games/NTE/Instruments/Copy.json', message: 'Duplicate instrument ID' }] });
  assert.equal(await harness.gameStore.reloadConfigs(), false);
  harness.setSnapshot({ games: JSON.parse(JSON.stringify(harness.gameStore.games)), issues: [] });
  assert.equal(await harness.gameStore.reloadConfigs(), true);
});

test('guitar input groups retain separate layouts and only explicit chord/fret changes toggle the game', async () => {
  const configure = games => {
    games[0].isRunning = true;
    Object.assign(games[0].instruments[1], { chordLayouts: ['wheel-a', 'wheel-b'], fretLayouts: ['frets'],
      guitar: { tuning: [64, 59, 55, 50, 45, 40], fretCount: 24 } });
    games[0].layouts.push(...['wheel-a', 'wheel-b'].map(id => ({ id, name: id, keys: [], chordWheel: { positions: [[693, 255]] } })),
      { id: 'frets', name: 'Frets', keys: [], fretboard: { stringKeys: ['6', '5', '4', '3', '2', '1'], wheelLayout: 'wheel-a', modeKey: 'z' } });
  };
  const h = await createGameStore({}, configure, [{ id: 'a', isRunning: true, isFocused: false }]);
  await h.gameStore.setInputMode('chords');
  await h.gameStore.syncActiveInstrumentMapping(undefined, 'wheel-b');
  await h.gameStore.setInputMode('frets');
  assert.equal(h.gameStore.activeLayout.id, 'frets');
  await h.gameStore.setInputMode('chords');
  assert.equal(h.gameStore.activeLayout.id, 'wheel-b');
  assert.deepEqual(h.modeChanges, ['frets', 'chords']);
  const saved = h.settingsWrites.findLast(write => write.selectedLayoutIds).selectedLayoutIds;
  const restored = await createGameStore({}, configure, [], saved);
  assert.equal(restored.gameStore.activeInputMode, 'chords');
  assert.equal(restored.gameStore.activeLayout.id, 'wheel-b');
  assert.deepEqual(restored.modeChanges, []);
});

test('Mixed requires opt-in and a linked wheel, restores its layout, and leaves physical mode alone on entry', async () => {
  const configure = games => {
    Object.assign(games[0].instruments[1], { chordMode: 'wheel', pluckStrings: true,
      chords: [{ name: 'C', pitchClasses: [0, 4, 7] }],
      chordLayouts: ['wheel'], fretLayouts: ['frets', 'unlinked'],
      guitar: { tuning: [64, 59, 55, 50, 45, 40], fretCount: 24 } });
    const fretboard = { stringKeys: ['6', '5', '4', '3', '2', '1'], wheelLayout: 'WHEEL', modeKey: 'z' };
    games[0].layouts.push({ id: 'wheel', name: 'Wheel', keys: [], chordWheel: { positions: [[500, 200]] } },
      { id: 'frets', name: 'Frets', keys: [], fretboard },
      { id: 'unlinked', name: 'Unlinked', keys: [], fretboard: { ...fretboard, wheelLayout: 'missing' } });
  };
  const h = await createGameStore({}, configure, [{ id: 'a', isRunning: true, isFocused: false }]);
  await h.gameStore.setInputMode('chords');
  await h.gameStore.setInputMode('mixed');
  assert.equal(h.gameStore.activeInputMode, 'mixed');
  assert.deepEqual(h.gameStore.getInstrumentLayouts(h.gameStore.activeInstrument).map(layout => layout.id), ['frets']);
  assert.deepEqual(h.mappings.at(-1), ['frets', []]);
  assert.equal(h.mixedMappings.at(-1), true);
  assert.deepEqual(h.modeChanges, []);
  const saved = h.settingsWrites.findLast(write => write.selectedLayoutIds).selectedLayoutIds;
  const restored = await createGameStore({}, configure, [], saved);
  assert.equal(restored.gameStore.activeInputMode, 'mixed');
  assert.deepEqual(restored.mappings.at(-1), ['frets', []]);
  assert.equal(restored.mixedMappings.at(-1), true);
  assert.deepEqual(restored.modeChanges, []);
  await h.gameStore.setInputMode('frets');
  assert.deepEqual(h.modeChanges, ['frets']);
  assert.equal(h.mixedMappings.at(-1), false);
  const legacy = await createGameStore({}, games => {
    configure(games);
    delete games[0].instruments[1].pluckStrings;
  }, [], saved);
  assert.equal(legacy.gameStore.activeInputMode, 'frets');
  assert.equal(legacy.mixedMappings.at(-1), false);
});

test('removing the active Mixed layout allows recovery through the Chords layout', async () => {
  const h = await createGameStore({}, games => {
    Object.assign(games[0].instruments[1], { chordMode: 'wheel', pluckStrings: true,
      chords: [{ name: 'C', pitchClasses: [0, 4, 7] }], chordLayouts: ['wheel'], fretLayouts: ['frets'],
      guitar: { tuning: [64, 59, 55, 50, 45, 40], fretCount: 24 } });
    games[0].layouts.push({ id: 'wheel', name: 'Wheel', keys: [], chordWheel: { positions: [[500, 200]] } },
      { id: 'frets', name: 'Frets', keys: [], fretboard: {
        stringKeys: ['6', '5', '4', '3', '2', '1'], wheelLayout: 'wheel', modeKey: 'z',
      } });
  }, [{ id: 'a', isRunning: true, isFocused: false }]);
  await h.gameStore.setInputMode('mixed');
  const games = h.gameStore.games.map(game => ({ ...game, layouts: game.layouts.filter(layout => layout.id !== 'frets') }));
  h.setSnapshot({ games, issues: [] });
  await h.gameStore.refreshConfigs();
  assert.equal(await h.gameStore.hasPlaybackMapping(), false);
  await h.gameStore.setInputMode('chords');
  assert.equal(await h.gameStore.hasPlaybackMapping(), true);
  assert.equal(h.gameStore.activeLayout.id, 'wheel');
  assert.deepEqual(h.modeChanges, []);
});

test('mouse-only chord layouts remain selectable and synchronize without chromatic key pairs', async () => {
  const h = await createGameStore({}, games => {
    games[0].instruments.push({ id: 'chords', name: 'Guitar (Chords)', notes: [60, 64, 67], keyboardLayouts: ['nte-wheel'] });
    games[0].layouts.push({ id: 'nte-wheel', name: 'Chord Wheel', keys: [], chordWheel: { positions: [[693, 255]] } });
  });
  await h.gameStore.syncActiveInstrumentMapping('chords');
  assert.equal(h.gameStore.activeLayout.id, 'nte-wheel');
  assert.deepEqual(h.mappings.at(-1), ['nte-wheel', []]);
});

test('Genshin stringed instruments support Keyboard and Chords modes under a single instrument entry', async () => {
  const readDefault = path => JSON.parse(readFileSync(new URL(`../src-tauri/resources/defaults/${path}`, import.meta.url), 'utf8'));
  const h = await createGameStore({}, games => {
    games[0].instruments = ['Ukulele', 'LingeringEuphonia'].map(file => {
      const instrument = readDefault(`Games/Genshin Impact/Instruments/Strings/${file}.json`);
      return { ...instrument, notes: instrumentNotes(instrument) };
    });
    games[0].layouts = ['Genshin_QWERTY_Melody', 'Genshin_QWERTY_Chords'].map(id =>
      normalizeKeyboardLayout(readDefault(`KeyboardLayouts/${id}.json`)));
  });
  for (const id of ['ukulele', 'lingering-euphonia']) {
    await h.gameStore.syncActiveInstrumentMapping(id);
    assert.equal(h.gameStore.activeInstrument.id, id);
    assert.equal(h.gameStore.activeInputMode, 'keyboard');
    assert.equal(h.gameStore.activeLayout.id, 'Genshin_QWERTY_Melody');
    assert.equal(h.mappings.at(-1)[1].length, 14);
    assert.equal(h.contexts.at(-1).instrumentId, id);

    await h.gameStore.setInputMode('chords');
    assert.equal(h.gameStore.activeInputMode, 'chords');
    assert.equal(h.gameStore.activeLayout.id, 'Genshin_QWERTY_Chords');
    assert.equal(h.mappings.at(-1)[1].length, 0);
    assert.equal(h.contexts.at(-1).instrumentId, id);

    await h.gameStore.setInputMode('keyboard');
    assert.equal(h.gameStore.activeInputMode, 'keyboard');
    assert.equal(h.gameStore.activeLayout.id, 'Genshin_QWERTY_Melody');
    assert.equal(h.mappings.at(-1)[1].length, 14);
  }
  assert.deepEqual(h.modeChanges, []);
});
