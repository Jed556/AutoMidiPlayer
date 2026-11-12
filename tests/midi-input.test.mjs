import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { stripTypeScriptTypes } from 'node:module';
import { compileModule } from 'svelte/compiler';

let harness = 0;
async function store() {
  let listener;
  let calls = [];
  const state = { ports: [{ id: 'opaque-device-id', name: 'Keyboard' }], selectedPortId: null,
    connected: false, active: false, starting: false, listenMode: true,
    requiresSession: false, outputAvailable: true, status: 'idle', pitches: [], error: null };
  const notifications = [];
  let resolveRead;
  const midiInput = {
    onStateChanged(callback) { listener = callback; return () => { listener = undefined; }; },
    getState: () => new Promise(resolve => { resolveRead = resolve; }),
    async selectPort(id) { calls.push(['select', id]); Object.assign(state, { selectedPortId: id, active: false, connected: false }); return { ...state }; },
    async start() { calls.push(['start']); return { ...state, active: true, connected: true, status: 'listening' }; },
    async stop() { calls.push(['stop']); return { ...state, active: false, connected: false, pitches: [], status: 'idle' }; },
    async refreshPorts() { calls.push(['refresh']); return { ...state }; },
  };
  const key = `__midiInputHarness${++harness}`;
  globalThis[key] = {
    '../backend': { api: { midiInput } },
    '../ui/notificationCenter.svelte': { notificationCenter: { error: (...args) => notifications.push(args) } },
    '../i18n': { t: key => key },
  };
  const source = stripTypeScriptTypes(readFileSync(new URL('../src/renderer/lib/state/midiInputStore.svelte.ts', import.meta.url), 'utf8'), { mode: 'transform' });
  const compiled = compileModule(source.replace(/^import \{([^}]+)\} from '([^']+)';/gm,
    (_, names, path) => `const {${names}} = globalThis.${key}[${JSON.stringify(path)}];`),
    { filename: 'midiInputStore.svelte.js', generate: 'client' }).js.code
    .replace("'svelte/internal/client'", JSON.stringify(import.meta.resolve('svelte/internal/client')));
  const { MidiInputStore } = await import(`data:text/javascript;base64,${Buffer.from(compiled).toString('base64')}`);
  delete globalThis[key];
  return { store: new MidiInputStore(), state, calls, notifications, midiInput,
    read: value => resolveRead(value), emit: value => listener(value) };
}

test('device selection and active input live in the app store, not a view', async () => {
  const h = await store();
  const init = h.store.init(); h.read(h.state); await init;
  await h.store.select('opaque-device-id');
  assert.equal(h.store.state.active, true);
  h.emit({ ...h.store.state, pitches: [60, 64] });
  assert.equal(h.store.state.active, true);
  // A view remount may reinitialize the store, but cannot reconnect or stop it.
  await h.store.init();
  assert.deepEqual(h.calls, [['select', 'opaque-device-id'], ['start']]);
  assert.deepEqual(h.store.state.pitches, [60, 64]);
  await h.store.select(null);
  assert.equal(h.store.state.active, false);
  assert.equal(h.store.state.selectedPortId, null);
  assert.deepEqual(h.calls.at(-1), ['select', null]);
  assert.deepEqual(h.store.state.pitches, []);
});

test('restores an available selected device at startup but skips missing and already active devices', async () => {
  for (const mode of ['available', 'missing', 'active', 'starting']) {
    const h = await store();
    const init = h.store.init();
    h.read({ ...h.state, selectedPortId: mode === 'missing' ? 'missing-id' : 'opaque-device-id',
      active: mode === 'active', starting: mode === 'starting' });
    await init;
    assert.deepEqual(h.calls, mode === 'available' ? [['start']] : [], mode);
  }
});

test('refresh retries a failed selected device without restarting an active connection', async () => {
  const h = await store();
  h.midiInput.start = async () => { throw new Error('Device is busy'); };
  await h.store.select('opaque-device-id');
  assert.equal(h.store.state.selectedPortId, 'opaque-device-id');
  assert.equal(h.store.state.status, 'error');
  assert.match(h.notifications.at(-1)[1], /Device is busy/);
  h.midiInput.start = async () => {
    h.calls.push(['start']);
    Object.assign(h.state, { active: true, connected: true, status: 'listening' });
    return { ...h.state };
  };
  await h.store.refresh();
  assert.equal(h.store.state.active, true);
  await h.store.refresh();
  assert.deepEqual(h.calls, [['select', 'opaque-device-id'], ['refresh'], ['start'], ['refresh']]);
});

test('a late initial snapshot cannot overwrite a newer disconnect or active state', async () => {
  const h = await store();
  const init = h.store.init();
  h.emit({ ...h.state, selectedPortId: 'opaque-device-id', active: true, connected: true, status: 'listening' });
  h.read(h.state); await init;
  assert.equal(h.store.state.active, true);
  const disconnected = { ...h.store.state, connected: false, active: false, pitches: [], status: 'error', error: 'Device disconnected' };
  h.emit(disconnected); h.emit(disconnected);
  assert.equal(h.notifications.length, 1);
  assert.equal(h.store.state.selectedPortId, 'opaque-device-id');
});

test('a startup event with a saved device takes precedence over a stale initial read', async () => {
  const h = await store();
  const init = h.store.init();
  h.emit({ ...h.state, selectedPortId: 'opaque-device-id' });
  h.read(h.state);
  await init;
  assert.deepEqual(h.calls, [['start']]);
  assert.equal(h.store.state.active, true);
});

test('Wayland authorization remains stoppable and output failures are surfaced', async () => {
  const h = await store();
  const init = h.store.init(); h.read(h.state); await init;
  h.midiInput.start = async () => ({ ...h.state, starting: true, requiresSession: true, status: 'permission' });
  await h.store.select('opaque-device-id');
  assert.equal(h.store.busy, false);
  assert.equal(h.store.state.starting, true);
  await h.store.select(null);
  assert.deepEqual(h.calls.at(-1), ['select', null]);
  h.midiInput.start = async () => { throw new Error('Permission denied'); };
  await h.store.start();
  assert.equal(h.store.state.status, 'error');
  assert.equal(h.store.busy, false);
  assert.match(h.notifications.at(-1)[1], /Permission denied/);
});
