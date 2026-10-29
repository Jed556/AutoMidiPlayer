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
  let saved = structuredClone({ onboardingCompleted: true, ...initial });
  let keyboardLayouts = layouts(15, 21, 32, 36, 37, 88);
  const noop = () => {};
  const folderCalls = [];
  const errors = [];
  const telemetryCalls = [];
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
    '../infrastructure/telemetry.js': { telemetry: { setEnabled: async enabled => { telemetryCalls.push(enabled); return true; } } },
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
  return { store, api, folderCalls, errors, telemetryCalls, saved: () => saved, setLayouts: counts => { keyboardLayouts = layouts(...counts); } };
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

test('re-press gap defaults to 20ms and survives save, reload, and overlap mode switches', async () => {
  const h = await harness();
  await h.store.init();
  assert.equal(h.store.settings.repressGapMs, 20);
  h.store.update('repressGapMs', 65);
  await settle();
  assert.equal(h.saved().repressGapMs, 65);
  h.store.update('overlapBehavior', 'hold');
  await settle();
  await h.store.init();
  assert.equal(h.store.settings.repressGapMs, 65);
  h.store.update('overlapBehavior', 'retrigger');
  await settle();
  assert.equal(h.store.settings.repressGapMs, 65);
  h.store.resetSongDefaults();
  assert.equal(h.store.settings.repressGapMs, 65);
  h.store.resetAllDefaults();
  await settle();
  assert.equal(h.store.settings.repressGapMs, 20);
  assert.equal(h.saved().repressGapMs, 20);
});

test('re-press gap stores whole milliseconds within 0..1000 and handles invalid input', async () => {
  const h = await harness();
  await h.store.init();
  for (const [input, expected] of [[0, 0], [-10, 0], [65.7, 66], [2000, 1000], [NaN, 20], [Infinity, 20]]) {
    h.store.update('repressGapMs', input);
    await settle();
    assert.equal(h.store.settings.repressGapMs, expected);
    assert.equal(h.saved().repressGapMs, expected);
  }
});

test('rapid gap edits and mode switches serialize only their latest pending values', async () => {
  const h = await harness();
  await h.store.init();
  const writes = [], finish = [];
  let defaultsUpdates = 0;
  h.api.settings.updateSettings = patch => {
    writes.push(patch);
    return new Promise(resolve => finish.push(resolve));
  };
  h.api.settings.updateSongDefaults = async () => { defaultsUpdates++; return true; };
  h.store.update('repressGapMs', 25);
  for (let value = 26; value <= 100; value++) h.store.update('repressGapMs', value);
  h.store.update('overlapBehavior', 'hold');
  assert.deepEqual(writes, [{ repressGapMs: 25 }]);
  finish.shift()(true);
  await settle();
  assert.deepEqual(writes, [{ repressGapMs: 25 }, { repressGapMs: 100, overlapBehavior: 'hold' }]);
  finish.shift()(true);
  await settle();
  assert.equal(defaultsUpdates, 0);
});

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
  h.store.update('repressGapMs', 80);
  h.store.resetAllDefaults();
  await settle();
  finishWrite(true);
  await settle();
  assert.equal(writes.length, 2);
  assert.equal(writes.at(-1).overlapBehavior, 'retrigger');
  assert.equal(h.store.settings.overlapBehavior, 'retrigger');
  assert.equal(writes.at(-1).repressGapMs, 20);
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

test('unfinished onboarding defers folder monitoring and scans, even with a saved folder', async () => {
  const h = await harness({ onboardingCompleted: false, midiFolder: 'C:/Music', autoScanMidiFolder: true });
  const hotkeys = [];
  h.api.settings.updateGlobalHotkeys = async enabled => { hotkeys.push(enabled); return true; };
  await h.store.init();
  assert.deepEqual(h.folderCalls, []);
  assert.ok(hotkeys.length > 0 && hotkeys.every(enabled => !enabled));
  h.store.resetAllDefaults();
  await settle();
  assert.equal(h.saved().onboardingCompleted, false);
});

test('Finish awaits saved preferences and native song defaults before importing, then records All Songs', async () => {
  const h = await harness({ onboardingCompleted: false });
  await h.store.init();
  const calls = [];
  let finishDefaults;
  let finishPreferences;
  h.api.settings.updateSettings = async patch => {
    calls.push(['settings', JSON.parse(JSON.stringify(patch))]);
    if (!patch.onboardingCompleted) await new Promise(resolve => { finishPreferences = resolve; });
    return true;
  };
  h.api.settings.updateSongDefaults = defaults => {
    calls.push(['defaults', defaults]);
    return new Promise(resolve => { finishDefaults = resolve; });
  };
  h.api.songs.watchFolder = async path => { calls.push(['watch', path]); return true; };
  h.api.songs.scanFolder = async path => {
    calls.push(['scan', path]);
    return [
      { filePath: 'C:\\MIDIs\\One.mid' },
      { filePath: 'c:/midis/subfolder/Two.midi' },
      { filePath: 'C:/MIDIs-other/Unrelated.mid' },
    ];
  };
  const finishing = h.store.finishOnboarding(
    { midiFolder: 'C:/MIDIs/', defaultHoldNotes: true, defaultSpeed: 0.75, theme: 'System', accentColor: '#0078D4' },
    { locale: 'zh-CN', animationTiming: 0.1 },
  );
  await settle();
  assert.deepEqual(calls.map(call => call[0]), ['watch', 'settings']);
  finishPreferences();
  await settle();
  assert.deepEqual(calls.map(call => call[0]), ['watch', 'settings', 'defaults']);
  assert.equal(calls[2][1].defaultHoldNotes, true);
  assert.equal(calls[2][1].defaultSpeed, 0.75);
  assert.equal(calls[1][1].theme, 'System');
  assert.equal(calls[1][1].accentColor, '#0078D4');
  assert.equal(h.store.settings.onboardingCompleted, false);
  finishDefaults(true);
  assert.equal(await finishing, 2, 'Count only songs in the selected folder, including subfolders');
  assert.deepEqual(calls.slice(3), [
    ['scan', 'C:/MIDIs/'], ['watch', 'C:/MIDIs/'],
    ['settings', { onboardingCompleted: true, telemetryOptIn: false, ui: { locale: 'zh-CN', animationTiming: 0.1, lastViewedPage: 'songs', selectedPlaylistId: 'songs' } }],
  ]);
  assert.equal(h.store.settings.onboardingCompleted, true);
});

test('Finish imports once with monitoring disabled, and supports setup without a folder', async () => {
  const h = await harness({ onboardingCompleted: false });
  await h.store.init();
  await h.store.finishOnboarding({ midiFolder: 'C:/Music', autoScanMidiFolder: false });
  assert.deepEqual(h.folderCalls, [['watch', null], ['scan', 'C:/Music']]);
  assert.equal(h.saved().onboardingCompleted, true);
  const empty = await harness({ onboardingCompleted: false });
  await empty.store.init();
  assert.equal(await empty.store.finishOnboarding({ midiFolder: '' }), 0);
  assert.deepEqual(empty.folderCalls, [['watch', null]]);
  await empty.store.init();
  assert.equal(empty.store.settings.onboardingCompleted, true);
  empty.store.resetAllDefaults();
  await settle();
  assert.equal(empty.saved().onboardingCompleted, true, 'Ordinary settings reset does not restart onboarding');
});

test('failed preference/default saves cannot scan; failed imports can be retried without completing setup', async () => {
  for (const failure of ['preferences', 'defaults', 'scan', 'watch', 'completion']) {
    const h = await harness({ onboardingCompleted: false });
    await h.store.init();
    const update = h.api.settings.updateSettings;
    const defaults = h.api.settings.updateSongDefaults;
    const scan = h.api.songs.scanFolder;
    const watch = h.api.songs.watchFolder;
    if (failure === 'preferences') h.api.settings.updateSettings = async () => false;
    if (failure === 'defaults') h.api.settings.updateSongDefaults = async () => false;
    if (failure === 'scan') h.api.songs.scanFolder = async () => { throw new Error('Cannot read folder'); };
    if (failure === 'watch') h.api.songs.watchFolder = async path => { if (path) throw new Error('Cannot watch folder'); return watch(path); };
    if (failure === 'completion') h.api.settings.updateSettings = async patch => patch.onboardingCompleted ? false : update(patch);
    await assert.rejects(h.store.finishOnboarding({ midiFolder: 'C:/Music' }));
    assert.equal(h.store.settings.onboardingCompleted, false);
    assert.notEqual(h.saved().onboardingCompleted, true);
    if (failure === 'preferences' || failure === 'defaults') assert.deepEqual(h.folderCalls, [['watch', null]]);
    else assert.deepEqual(h.folderCalls.at(-1), ['watch', null]);
    h.api.settings.updateSettings = update;
    h.api.settings.updateSongDefaults = defaults;
    h.api.songs.scanFolder = scan;
    h.api.songs.watchFolder = watch;
    await h.store.finishOnboarding({ midiFolder: 'C:/Music' });
    assert.equal(h.store.settings.onboardingCompleted, true);
  }
});

test('telemetry stays off throughout unfinished setup and commits the draft choice only on success', async () => {
  for (const optedIn of [true, false]) {
    const h = await harness({ onboardingCompleted: false, telemetryOptIn: true });
    await h.store.init();
    assert.deepEqual(h.telemetryCalls, [false], 'Even an interrupted saved opt-in cannot enable telemetry');
    let finishScan;
    h.api.songs.scanFolder = () => new Promise(resolve => { finishScan = resolve; });
    const finishing = h.store.finishOnboarding({ midiFolder: 'C:/Music', telemetryOptIn: optedIn });
    await settle();
    assert.equal(h.saved().telemetryOptIn, false);
    assert.deepEqual(h.telemetryCalls, [false]);
    finishScan([]);
    await finishing;
    assert.equal(h.saved().telemetryOptIn, optedIn);
    assert.equal(h.saved().onboardingCompleted, true);
    assert.deepEqual(h.telemetryCalls, [false, optedIn]);
  }
});

test('a failed onboarding scan never activates the selected telemetry opt-in', async () => {
  const h = await harness({ onboardingCompleted: false });
  await h.store.init();
  h.api.songs.scanFolder = async () => { throw new Error('Folder unavailable'); };
  await assert.rejects(h.store.finishOnboarding({ midiFolder: 'C:/Music', telemetryOptIn: true }));
  assert.deepEqual(h.telemetryCalls, [false]);
  assert.equal(h.saved().telemetryOptIn, false);
  assert.equal(h.saved().onboardingCompleted, false);
});
