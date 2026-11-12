import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { stripTypeScriptTypes } from 'node:module';

const turn = () => new Promise(resolve => setImmediate(resolve));

function resetHarness({ failure } = {}) {
  const app = readFileSync(new URL('../src/renderer/App.svelte', import.meta.url), 'utf8');
  const start = app.indexOf('  async function clearAppData(');
  const end = app.indexOf('  // Native media sessions', start);
  const body = stripTypeScriptTypes(app.slice(start, end));
  const calls = [];
  const errors = new Map();
  const make = new Function('settingsStore', 'updateStore', 'playbackStore', 'api', 'tick', 'window', 'sessionStorage', 'localStorage', 'scheduledPlaybackStore', `
    let clearingAppData = false, startupReady = true, showStartup = false, startupStatus, splashEntered;
    const startupProgress = { beginStartup() {} };
    ${body}
    return { clearAppData, enter: () => splashEntered(),
      state: () => ({ clearingAppData, startupReady, showStartup, startupStatus }) };
  `);
  return { ...make(
    { settings: {}, suspendHotkeys: () => calls.push('hotkeys') },
    { isApplyingUpdate: false, configureTimer: () => calls.push('timer') },
    { stop: async () => calls.push('stop'), flushPersistence: async () => calls.push('flush') },
    { settings: { clearAppData: async () => { calls.push('clear'); if (failure) throw new Error(failure); } } },
    async () => calls.push('unmount'),
    { location: { reload: () => calls.push('reload') } },
    { setItem: (key, value) => errors.set(key, value), clear: () => calls.push('sessionStorage') },
    { clear: () => calls.push('localStorage') },
    { cancelAndWait: async () => calls.push('scheduledTimer') },
  ), calls, errors };
}

test('Clear Data enters the splash, stops and drains writes, then clears and reloads the same window', async () => {
  const h = resetHarness();
  const reset = h.clearAppData();
  assert.equal(h.state().showStartup, true);
  assert.equal(h.state().startupStatus, 'startup.clearingData');
  assert.deepEqual(h.calls, [], 'Do not clear before the splash covers the interface');
  await h.clearAppData();
  assert.deepEqual(h.calls, [], 'Ignore duplicate requests');
  h.enter();
  await reset;
  assert.equal(h.state().startupReady, false);
  assert.deepEqual(h.calls, ['unmount', 'hotkeys', 'timer', 'scheduledTimer', 'stop', 'flush', 'clear', 'localStorage', 'sessionStorage', 'reload']);
  assert.equal(h.errors.size, 0);
});

test('a failed or partial reset reloads fresh stores and retains the error for display', async () => {
  const h = resetHarness({ failure: 'File is locked' });
  const reset = h.clearAppData();
  h.enter();
  await reset;
  assert.equal(h.calls.at(-1), 'reload');
  assert.match(h.errors.get('appDataResetError'), /File is locked/);
});

test('the native reset drains work and prevents stale requests and events from restoring cleared data', async () => {
  const calls = [];
  let finishOld;
  let deliver;
  const oldCommand = new Promise(resolve => { finishOld = resolve; });
  let source = stripTypeScriptTypes(readFileSync(new URL('../src/renderer/lib/backend/runtime.ts', import.meta.url), 'utf8'))
    .replace("import { waitForBackendReady } from './startupGate';", 'const waitForBackendReady = async () => {};')
    .replaceAll("await import('@tauri-apps/api/core')", '({ invoke: mockInvoke })')
    .replaceAll("import('@tauri-apps/api/event')", 'Promise.resolve({ listen: mockListen })')
    .replaceAll('export ', '');
  source += '\nreturn { invokeTauri, clearTauriAppData, listenTauri };';
  const runtime = new Function('window', 'mockInvoke', 'mockListen', source)(
    { __TAURI_INTERNALS__: {} },
    async command => { calls.push(command); if (command === 'old_write') await oldCommand; },
    async (_name, callback) => { deliver = callback; return () => {}; },
  );
  let delivered = 0;
  runtime.listenTauri('library:updated', () => delivered++);
  const old = runtime.invokeTauri('Settings', 'old_write');
  await turn();
  deliver({ payload: null });
  assert.equal(delivered, 1);
  const reset = runtime.clearTauriAppData();
  void runtime.invokeTauri('Settings', 'stale_write');
  await turn();
  assert.deepEqual(calls, ['old_write']);
  deliver({ payload: null });
  assert.equal(delivered, 1, 'Old event listeners stay frozen until reload');
  await runtime.invokeTauri('Window', 'minimize_window');
  finishOld();
  await old;
  await reset;
  assert.deepEqual(calls, ['old_write', 'minimize_window', 'clear_app_data']);
});
