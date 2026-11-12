import * as sharedDefaults from '../src/shared/defaults/settings.ts';
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { stripTypeScriptTypes } from 'node:module';
import { compileModule } from 'svelte/compiler';
import * as normalization from '../src/renderer/lib/utils/sessionPreferences.ts';

let harnessId = 0;
async function loadModule(path, mocks, runes = false) {
  const key = `__persistenceHarness${++harnessId}`;
  globalThis[key] = mocks;
  let source = stripTypeScriptTypes(readFileSync(new URL(path, import.meta.url), 'utf8'), { mode: 'transform' })
    .replace(/^import \{([^}]+)\} from '([^']+)';/gm,
      (_, names, path) => `const {${names}} = globalThis.${key}[${JSON.stringify(path)}];`);
  if (runes) source = compileModule(source, { filename: 'uiStore.svelte.js', generate: 'client' }).js.code
    .replace("'svelte/internal/client'", JSON.stringify(import.meta.resolve('svelte/internal/client')));
  const module = await import(`data:text/javascript;base64,${Buffer.from(source).toString('base64')}`);
  delete globalThis[key];
  return module;
}

async function createUi(preferences = {}) {
  const saved = [];
  const { uiStore } = await loadModule('../src/renderer/lib/state/uiStore.svelte.ts', {
    '../../../shared/defaults/settings.js': sharedDefaults,
    '../backend/preferences.js': { loadUiPreferences: async () => preferences, saveUiPreferences: value => saved.push(structuredClone(value)) },
    '../ui/notificationCenter.svelte': { notificationCenter: {} },
    '../infrastructure/logger.js': { logger: { pageVisit() {} } },
    '../utils/sessionPreferences.js': normalization,
  }, true);
  await uiStore.init();
  return { ui: uiStore, saved };
}

test('navigation and Studio preferences survive store recreation including history navigation', async () => {
  const { ui, saved } = await createUi();
  ui.activeTab = 'studio';
  ui.studio.zoomX = 175;
  ui.studio.zoomY = 24;
  ui.studio.isLockedToPlayhead = false;
  ui.studio.displayMode = 'SongDefault';
  ui.studio.sidebarTab = 'markers';
  ui.studioSidebarCollapsed = true;
  ui.songsSortMode = 'dateAdded';
  ui.activeTab = 'queue';
  ui.goBack();
  ui.persistPreferences('favorites');
  ui.persistPreferences('favorites');
  assert.equal(saved.length, 1, 'Unchanged preferences do not produce idle writes');
  const restored = (await createUi(saved.at(-1))).ui;
  assert.equal(restored.activeTab, 'studio');
  assert.equal(restored.tabHistory.length, 1);
  assert.deepEqual({ ...restored.studio }, { zoomX: 175, zoomY: 24, isLockedToPlayhead: false,
    displayMode: 'SongDefault', sidebarTab: 'markers' });
  assert.equal(restored.studioSidebarCollapsed, true);
  assert.equal(restored.songsSortMode, 'dateAdded');
  ui.goForward();
  ui.persistPreferences('favorites');
  assert.equal(saved.at(-1).lastViewedPage, 'queue');
});

test('transient pages and invalid saved values use usable defaults', async () => {
  const { ui } = await createUi({ lastViewedPage: 'new-playlist', libraryWidth: NaN, libraryExpandedWidth: 'bad',
    studio: { zoomX: Infinity, zoomY: -12, displayMode: 'bad', isLockedToPlayhead: 'false' }, songsSortMode: 'bad' });
  assert.equal(ui.activeTab, 'songs');
  assert.equal(ui.libraryWidth, 80);
  assert.equal(ui.lastExpandedWidth, 340);
  assert.equal(ui.studio.zoomX, 80);
  assert.equal(ui.studio.zoomY, 2);
  assert.equal(ui.studio.displayMode, 'Auto');
  assert.equal(ui.studio.isLockedToPlayhead, true);
  assert.equal(ui.songsSortMode, 'title');
  assert.equal(normalization.normalizePage('Studio'), 'studio');
});

test('the Note Modifiers display selection survives restart', async () => {
  const { ui, saved } = await createUi();
  ui.activeTab = 'studio';
  ui.studio.displayMode = 'NoteModifiers';
  ui.persistPreferences('songs');
  const restored = (await createUi(saved.at(-1))).ui;
  assert.equal(restored.studio.displayMode, 'NoteModifiers');
});

test('custom library order and sort direction survive store recreation', async () => {
  const { ui, saved } = await createUi();
  ui.songsSortMode = 'custom';
  ui.songsSortDirection = 'desc';
  ui.librarySongOrder = ['song-c', 'unavailable-song', 'song-a', 'song-b'];
  ui.persistPreferences('songs');
  ui.persistPreferences('songs');
  assert.equal(saved.length, 1, 'An unchanged library order does not produce idle writes');
  assert.deepEqual(saved[0].librarySongOrder, ['song-c', 'unavailable-song', 'song-a', 'song-b']);
  const restored = (await createUi(saved[0])).ui;
  assert.equal(restored.songsSortMode, 'custom');
  assert.equal(restored.songsSortDirection, 'desc');
  assert.deepEqual([...restored.librarySongOrder], ['song-c', 'unavailable-song', 'song-a', 'song-b']);
});

test('malformed ordering preferences normalize without discarding usable song IDs', async () => {
  const { ui } = await createUi({ songsSortMode: 'invalid', songsSortDirection: 'sideways',
    librarySongOrder: ['song-b', null, '', '   ', 7, 'song-b', 'song-a', false] });
  assert.equal(ui.songsSortMode, 'title');
  assert.equal(ui.songsSortDirection, 'asc');
  assert.deepEqual([...ui.librarySongOrder], ['song-b', 'song-a']);
  const invalidContainer = (await createUi({ librarySongOrder: { id: 'song-a' }, songsSortDirection: false })).ui;
  assert.deepEqual([...invalidContainer.librarySongOrder], []);
  assert.equal(invalidContainer.songsSortDirection, 'asc');
});

test('opening Settings restores the preferred drawer width when leaving it', async () => {
  const { ui } = await createUi({ lastViewedPage: 'settings', libraryWidth: 340 });
  assert.equal(ui.activeTab, 'settings');
  assert.equal(ui.libraryWidth, 80);
  ui.activeTab = 'songs';
  assert.equal(ui.libraryWidth, 340);
});

test('rapid independent preference changes merge and flush before closing', async () => {
  const writes = [];
  const prefs = await loadModule('../src/renderer/lib/backend/preferences.ts', {
    './index.js': { api: { settings: { updateSettings: async value => { writes.push(value); return true; } } } },
  });
  prefs.saveUiPreferences({ studio: { zoomX: 160 }, playback: { isListenMode: true } });
  prefs.saveUiPreferences({ studio: { zoomY: 20 }, playback: { positionSeconds: 42 } });
  prefs.saveUiPreferences({ lastViewedPage: 'studio', pedalsEnabled: false });
  await prefs.flushUiPreferences();
  assert.equal(writes.length, 1);
  assert.deepEqual(writes[0], { ui: { studio: { zoomX: 160, zoomY: 20 },
    playback: { isListenMode: true, positionSeconds: 42 }, lastViewedPage: 'studio', pedalsEnabled: false } });
});

test('viewport snapshots replace evicted history while coalescing with unrelated preferences', async () => {
  const writes = [];
  const prefs = await loadModule('../src/renderer/lib/backend/preferences.ts', {
    './index.js': { api: { settings: { updateSettings: async value => { writes.push(value); return true; } } } },
  });
  prefs.saveUiPreferences({ scrollPositions: { 'studio:old-song': { x: 120, y: 300 } } });
  prefs.saveUiPreferences({ studio: { zoomX: 200 } });
  prefs.saveUiPreferences({ scrollPositions: { 'studio:new-song': { x: 400, y: 500 } } });
  await prefs.flushUiPreferences();
  assert.deepEqual(writes, [{ ui: { studio: { zoomX: 200 }, scrollPositions: { 'studio:new-song': { x: 400, y: 500 } } } }]);
});

test('failed writes retain changes and a retry keeps the newest preference', async () => {
  let fail = true;
  let release;
  const blocked = new Promise(resolve => release = resolve);
  const writes = [];
  const prefs = await loadModule('../src/renderer/lib/backend/preferences.ts', {
    './index.js': { api: { settings: { updateSettings: async value => {
      writes.push(value);
      if (fail) { await blocked; throw new Error('disk busy'); }
      return true;
    } } } },
  });
  prefs.saveUiPreferences({ playback: { positionSeconds: 2, isListenMode: true } });
  const flushing = prefs.flushUiPreferences();
  await Promise.resolve();
  prefs.saveUiPreferences({ playback: { positionSeconds: 10 } });
  release();
  await assert.rejects(flushing, /disk busy/);
  fail = false;
  await prefs.flushUiPreferences();
  assert.deepEqual(writes.at(-1).ui.playback, { positionSeconds: 10, isListenMode: true });
});

test('instrument stage preferences survive store recreation', async () => {
  const { ui, saved } = await createUi();
  ui.setInstrumentStage('piano');
  ui.setInstrumentToggles(false, true);
  ui.persistPreferences('songs');
  assert.equal(saved.length, 3);
  const restored = (await createUi(saved.at(-1))).ui;
  assert.equal(restored.instrumentStage, 'piano');
  assert.equal(restored.instrumentGameSelected, false);
  assert.equal(restored.instrumentPianoSelected, true);
});
