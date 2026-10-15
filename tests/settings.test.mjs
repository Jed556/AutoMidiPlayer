import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { stripTypeScriptTypes } from 'node:module';
import { compileModule } from 'svelte/compiler';
import { DEFAULT_APP_SETTINGS } from '../src/shared/defaults/settings.ts';
import * as autoCorrect from '../src/shared/defaults/autoCorrect.ts';
import { getAutoCorrectTicks, getDefaultAutoCorrectThreshold, resolveAutoCorrectThreshold } from '../src/shared/defaults/autoCorrect.ts';

const layouts = (...counts) => Object.fromEntries(counts.map((count, i) => [i, { keys: Array(count).fill('A') }]));

test('auto correct ticks use unique sorted layout key counts, including custom layouts', () => {
  assert.deepEqual(getAutoCorrectTicks(layouts(37, 21, 15, 21, 32, 0, 88)), [0, 15, 21, 32, 37, 88]);
  assert.deepEqual(getAutoCorrectTicks({}), [0]);
});

test('default uses the largest layout strictly below the configurable cutoff', () => {
  const ticks = getAutoCorrectTicks(layouts(15, 21, 32, 36, 37, 88));
  assert.equal(getDefaultAutoCorrectThreshold(ticks), 32);
  assert.equal(getDefaultAutoCorrectThreshold(ticks, 32), 21);
  assert.equal(getDefaultAutoCorrectThreshold(ticks, 36, 15), 15);
  assert.equal(getDefaultAutoCorrectThreshold(ticks, 36, 25), 32);
  assert.equal(getDefaultAutoCorrectThreshold([0, 37]), 0);
});

test('missing and legacy values migrate while Off and saved layout choices survive revisits', () => {
  const ticks = [0, 15, 21, 32, 37, 88];
  assert.equal(resolveAutoCorrectThreshold(undefined, ticks), 32);
  assert.equal(resolveAutoCorrectThreshold(4, ticks), 32);
  assert.equal(resolveAutoCorrectThreshold(NaN, ticks), 32);
  for (const value of ticks) assert.equal(resolveAutoCorrectThreshold(value, ticks), value);
  assert.equal(resolveAutoCorrectThreshold(35, ticks), 32);
  assert.equal(resolveAutoCorrectThreshold(61, ticks), 37);
  assert.equal(resolveAutoCorrectThreshold(4, [0, 4, 21]), 4);
});

let harnessId = 0;
async function harness(initial = {}) {
  let saved = structuredClone(initial);
  let keyboardLayouts = layouts(15, 21, 32, 36, 37, 88);
  const noop = () => {};
  const folderCalls = [];
  const errors = [];
  const api = {
    settings: {
      getDataDirectory: async () => '',
      getStorageUsage: async () => ({ appDataBytes: 11, onlineMidiBytes: 23, discoverCacheBytes: 37, totalBytes: 71 }),
      clearDiscoverCache: async () => 37,
      getSettings: async () => structuredClone(saved),
      updateSettings: async value => { saved = { ...saved, ...JSON.parse(JSON.stringify(value)) }; return true; },
      updateSongDefaults: async () => true,
      updateGlobalHotkeys: async () => true,
      resetSettings: async () => { saved = {}; return true; },
    },
    games: { getKeyboardLayouts: async () => keyboardLayouts },
    songs: {
      watchFolder: async path => { folderCalls.push(['watch', path]); return true; },
      scanFolder: async path => { folderCalls.push(['scan', path]); return []; },
    },
  };
  const mocks = {
    '$shared/defaults/settings': { DEFAULT_APP_SETTINGS },
    '$shared/defaults/autoCorrect': autoCorrect,
    '../backend/index.js': { api },
    '../ui/notificationCenter.svelte': { notificationCenter: { info: noop, success: noop, error: (...args) => errors.push(args) } },
    './themeStore.svelte.js': { themeStore: { setAccentColor: noop, setTheme: noop } },
    './updateStore.svelte.js': { updateStore: { configureTimer: noop } },
    '../infrastructure/logger.js': { logger: { setVerbosity: noop, setDebugMode: noop } },
    '../infrastructure/telemetry.js': { telemetry: { setEnabled: noop } },
    '../i18n': { t: key => key },
  };
  const source = stripTypeScriptTypes(readFileSync(new URL('../src/renderer/lib/state/settingsStore.svelte.ts', import.meta.url), 'utf8'));
  const key = `__settingsHarness${++harnessId}`;
  globalThis[key] = mocks;
  const compiled = compileModule(source.replace(/^import \{([^}]+)\} from '([^']+)';/gm,
    (_, names, path) => `const {${names}} = globalThis.${key}[${JSON.stringify(path)}];`),
  { filename: 'settingsStore.svelte.js', generate: 'client' }).js.code
    .replace("'svelte/internal/client'", JSON.stringify(import.meta.resolve('svelte/internal/client')));
  const { settingsStore: store } = await import(`data:text/javascript;base64,${Buffer.from(compiled).toString('base64')}`);
  delete globalThis[key];
  return { store, api, folderCalls, errors, saved: () => saved, setLayouts: counts => { keyboardLayouts = layouts(...counts); } };
}

test('settings initialization migrates and saves the old default, and reload preserves a chosen value', async () => {
  for (const initial of [{}, { autoCorrectThreshold: 4 }]) {
    const h = await harness(initial);
    await h.store.init();
    assert.equal(h.store.settings.autoCorrectThreshold, 32);
    assert.equal(h.saved().autoCorrectThreshold, 32);
    h.store.update('autoCorrectThreshold', 21);
    await h.store.init();
    assert.equal(h.store.settings.autoCorrectThreshold, 21);
    h.store.update('autoCorrectThreshold', 0);
    await h.store.init();
    assert.equal(h.store.settings.autoCorrectThreshold, 0);
  }
});

test('layout refresh updates slider stops and reset uses the current layout default', async () => {
  const h = await harness({ autoCorrectThreshold: 32 });
  await h.store.init();
  h.setLayouts([15, 21, 25, 36, 61]);
  await h.store.refreshAutoCorrectLayouts(h.store.settings.autoCorrectThreshold);
  assert.deepEqual([...h.store.autoCorrectThresholdTicks], [0, 15, 21, 25, 36, 61]);
  assert.equal(h.store.settings.autoCorrectThreshold, 25);
  assert.equal(h.saved().autoCorrectThreshold, 25);
  h.store.update('autoCorrectThreshold', 61);
  h.store.resetAllDefaults();
  await new Promise(resolve => setImmediate(resolve));
  assert.equal(h.store.settings.autoCorrectThreshold, 25);
  assert.equal(h.saved().autoCorrectThreshold, 25);
});

const settle = () => new Promise(resolve => setImmediate(resolve));

test('overlap behavior defaults to retrigger, preserves saved choices, and resets with player settings', async () => {
  const h = await harness();
  await h.store.init();
  assert.equal(h.store.settings.overlapBehavior, 'retrigger');
  h.store.update('overlapBehavior', 'hold');
  await settle();
  assert.equal(h.saved().overlapBehavior, 'hold');
  await h.store.init();
  assert.equal(h.store.settings.overlapBehavior, 'hold');
  h.store.resetSongDefaults();
  assert.equal(h.store.settings.overlapBehavior, 'hold');
  h.store.resetAllDefaults();
  await settle();
  assert.equal(h.store.settings.overlapBehavior, 'retrigger');
  assert.equal(h.saved().overlapBehavior, 'retrigger');
});

test('rapid overlap switches keep only the latest pending choice and avoid unrelated native work', async () => {
  const h = await harness({ overlapBehavior: 'hold' });
  await h.store.init();
  const writes = [];
  const finish = [];
  let defaultsUpdates = 0;
  h.api.settings.updateSettings = patch => {
    writes.push(patch);
    return new Promise(resolve => finish.push(resolve));
  };
  h.api.settings.updateSongDefaults = async () => { defaultsUpdates++; return true; };
  h.store.update('overlapBehavior', 'retrigger');
  for (let i = 0; i < 501; i++) {
    h.store.update('overlapBehavior', i % 2 === 0 ? 'hold' : 'retrigger');
  }
  assert.equal(h.store.settings.overlapBehavior, 'hold');
  assert.deepEqual(writes, [{ overlapBehavior: 'retrigger' }]);
  finish.shift()(true);
  await settle();
  assert.deepEqual(writes, [{ overlapBehavior: 'retrigger' }, { overlapBehavior: 'hold' }]);
  finish.shift()(true);
  await settle();
  h.store.update('overlapBehavior', 'hold');
  await settle();
  assert.equal(writes.length, 2);
  assert.equal(defaultsUpdates, 0);
});

test('overlap write failures still flush the latest switch and allow future saves', async () => {
  const h = await harness({ overlapBehavior: 'hold' });
  await h.store.init();
  const writes = [];
  let rejectWrite;
  h.api.settings.updateSettings = patch => {
    writes.push(patch);
    return writes.length === 1 ? new Promise((_, reject) => { rejectWrite = reject; }) : Promise.resolve(true);
  };
  h.store.update('overlapBehavior', 'retrigger');
  h.store.update('overlapBehavior', 'hold');
  const originalError = console.error;
  try {
    console.error = () => {};
    rejectWrite(new Error('temporary write failure'));
    await settle();
  } finally {
    console.error = originalError;
  }
  assert.deepEqual(writes, [{ overlapBehavior: 'retrigger' }, { overlapBehavior: 'hold' }]);
  h.store.update('overlapBehavior', 'retrigger');
  await settle();
  assert.deepEqual(writes.at(-1), { overlapBehavior: 'retrigger' });
});

test('reset discards pending overlap switches before saving defaults', async () => {
  const h = await harness({ overlapBehavior: 'hold' });
  await h.store.init();
  const writes = [];
  let finishWrite;
  h.api.settings.updateSettings = patch => {
    writes.push(patch);
    return writes.length === 1 ? new Promise(resolve => { finishWrite = resolve; }) : Promise.resolve(true);
  };
  h.store.update('overlapBehavior', 'retrigger');
  h.store.update('overlapBehavior', 'hold');
  h.store.update('overlapBehavior', 'retrigger');
  h.store.resetAllDefaults();
  await settle();
  finishWrite(true);
  await settle();
  assert.equal(writes.length, 2);
  assert.equal(writes.at(-1).overlapBehavior, 'retrigger');
  assert.equal(h.store.settings.overlapBehavior, 'retrigger');
});

test('startup watches the saved MIDI folder before scanning changes made while the app was closed', async () => {
  const h = await harness({ midiFolder: 'C:/Music', autoScanMidiFolder: true });
  await h.store.init();
  assert.deepEqual(h.folderCalls, [['watch', 'C:/Music'], ['scan', 'C:/Music']]);
  const disabled = await harness({ midiFolder: 'C:/Music', autoScanMidiFolder: false });
  await disabled.store.init();
  assert.deepEqual(disabled.folderCalls, [['watch', null]]);
});

test('folder changes, toggles, clearing, and reset keep the watcher in sync', async () => {
  const h = await harness({ midiFolder: 'C:/Music' });
  await h.store.init();
  h.folderCalls.length = 0;
  h.store.update('autoScanMidiFolder', false);
  await settle();
  assert.deepEqual(h.folderCalls, [['watch', null]]);
  h.folderCalls.length = 0;
  h.store.update('midiFolder', 'D:/Songs');
  await settle();
  assert.deepEqual(h.folderCalls, [['watch', null]]);
  h.folderCalls.length = 0;
  h.store.updateMultiple({ midiFolder: 'E:/MIDIs', autoScanMidiFolder: true });
  await settle();
  assert.deepEqual(h.folderCalls, [['watch', 'E:/MIDIs'], ['scan', 'E:/MIDIs']]);
  h.folderCalls.length = 0;
  h.store.update('midiFolder', '');
  await settle();
  assert.deepEqual(h.folderCalls, [['watch', null]]);
  h.folderCalls.length = 0;
  h.store.update('midiFolder', 'C:/Music');
  h.store.resetAllDefaults();
  await settle();
  assert.deepEqual(h.folderCalls.at(-1), ['watch', null]);
});

test('rapid folder switches serialize watch and scan operations', async () => {
  const h = await harness();
  let finishScan;
  h.api.songs.scanFolder = path => {
    h.folderCalls.push(['scan', path]);
    return new Promise(resolve => { finishScan = resolve; });
  };
  h.store.update('midiFolder', 'C:/First');
  await settle();
  h.store.update('midiFolder', 'D:/Second');
  h.store.update('autoScanMidiFolder', false);
  await settle();
  assert.deepEqual(h.folderCalls, [['watch', 'C:/First'], ['scan', 'C:/First']]);
  finishScan([]);
  await settle();
  assert.deepEqual(h.folderCalls.at(-1), ['scan', 'D:/Second']);
  finishScan([]);
  await settle();
  assert.deepEqual(h.folderCalls.at(-1), ['watch', null]);
});

test('watch failures report an error and do not prevent switching to a valid folder', async () => {
  const h = await harness();
  const watch = h.api.songs.watchFolder;
  h.api.songs.watchFolder = async path => {
    if (path === 'missing') throw new Error('Folder is missing');
    return watch(path);
  };
  h.store.update('midiFolder', 'missing');
  const originalError = console.error;
  try {
    console.error = () => {};
    await settle();
  } finally {
    console.error = originalError;
  }
  assert.match(h.errors[0][1], /Folder is missing/);
  h.store.update('midiFolder', 'C:/Music');
  await settle();
  assert.deepEqual(h.folderCalls, [['watch', 'C:/Music'], ['scan', 'C:/Music']]);
});


test('storage ignores stale scans and keeps previous measurements on a refresh failure', async () => {
  const h = await harness();
  let finish;
  h.api.settings.getStorageUsage = () => new Promise(resolve => { finish = resolve; });
  const stale = h.store.refreshStorage();
  h.api.settings.getStorageUsage = async () => ({ appDataBytes: 19, onlineMidiBytes: 0, discoverCacheBytes: 0, totalBytes: 19 });
  await h.store.refreshStorage();
  finish({ appDataBytes: 99, onlineMidiBytes: 0, discoverCacheBytes: 0, totalBytes: 99 });
  await stale;
  assert.equal(h.store.storageUsage.totalBytes, 19);
  h.api.settings.getStorageUsage = async () => { throw new Error('Storage offline'); };
  await h.store.refreshStorage();
  assert.equal(h.store.storageUsage.totalBytes, 19);
  assert.match(h.store.storageError, /Storage offline/);
  assert.equal(h.store.storageLoading, false);
});

test('cache cleanup prevents double clicks, refreshes usage, and recovers from recycle errors', async () => {
  const h = await harness();
  let finish;
  let calls = 0;
  h.api.settings.clearDiscoverCache = () => { calls++; return new Promise(resolve => { finish = resolve; }); };
  const clearing = h.store.clearCache();
  await h.store.clearCache();
  assert.equal(calls, 1);
  assert.equal(h.store.isClearingCache, true);
  finish(37);
  await clearing;
  assert.equal(h.store.isClearingCache, false);
  assert.equal(h.store.storageUsage.totalBytes, 71);
  h.api.settings.clearDiscoverCache = async () => { throw new Error('File is in use'); };
  await h.store.clearCache();
  assert.match(h.errors.at(-1)[1], /File is in use/);
  assert.equal(h.store.isClearingCache, false);
  assert.equal(h.store.storageLoading, false);
});
