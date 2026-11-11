import test from 'node:test';
import assert from 'node:assert/strict';
import { systemInfoRows, pollSystemSnapshot } from '../src/renderer/lib/components/Settings/systemInfo.ts';
import { enUS } from '../src/renderer/lib/i18n/locales/en-US.ts';
import { zhCN } from '../src/renderer/lib/i18n/locales/zh-CN.ts';

const translate = locale => key => key.split('.').reduce((value, part) => value?.[part], locale) ?? key;
const t = translate(enUS);
const observation = state => ({ state, detail: null });
const snapshot = (session = 'windows') => ({
  environment: { osName: 'Windows', osVersion: 'Windows 11', systemArchitecture: 'x86_64', appArchitecture: 'aarch64', desktopSession: session },
  keyboard: { backend: { windows: 'enigoWindows', macos: 'enigoMacos', x11: 'enigoX11', wayland: 'remoteDesktopPortal' }[session] ?? 'unknown', ...observation('notChecked') },
  audio: observation('ready'), tray: observation('ready'), mediaControls: observation('notChecked'),
});
const midi = { ports: [{ id: 'opaque', name: 'Stage keyboard' }], selectedPortId: null, connected: false, active: false,
  starting: false, listenMode: true, outputAvailable: false, requiresSession: false, status: 'idle', pitches: [], error: null };
const game = { displayName: 'Roblox', isRunning: true, isFocused: true };
const rows = (value = snapshot(), input = midi, target = game, failed = false, locale = enUS) => {
  const result = systemInfoRows(value, failed, target, input, translate(locale));
  return Object.fromEntries([...result.environment, ...result.services].map(row => [row.id, row]));
};

test('all desktop sessions retain a passive uninitialized keyboard status', () => {
  for (const session of ['windows', 'macos', 'x11', 'wayland', 'unknown']) {
    const result = rows(snapshot(session));
    assert.equal(result.keyboard.status, 'Not checked');
    assert.equal(result.keyboard.tone, 'muted');
    assert.ok(!result.keyboard.value.startsWith('systemInfo.'));
    assert.equal(result.systemArchitecture.value, 'x64');
    assert.equal(result.appArchitecture.value, 'ARM64');
    assert.equal(result.mediaControls.value, 'Not initialized');
    if (session === 'wayland') assert.match(result.game.status, /Focus unavailable on Wayland/);
    else assert.match(result.game.status, /Focused/);
  }
});

test('service states distinguish inactive, unchecked, permission and errors', () => {
  for (const state of ['ready', 'inactive', 'notChecked', 'checking', 'permissionRequired', 'unavailable', 'unknown']) {
    const value = snapshot(); value.keyboard = { ...value.keyboard, state, detail: 'Observed reason' };
    const result = rows(value).keyboard;
    assert.equal(result.status, t(`systemInfo.${state}`));
    assert.equal(result.detail, 'Observed reason');
  }
  const value = snapshot();
  value.audio = observation('reconnecting');
  assert.equal(rows(value).audio.value, 'Reconnecting');
  value.audio = observation('ready');
  assert.equal(rows(value).audio.value, 'Ready');
  value.tray = observation('unavailable');
  value.mediaControls = { state: 'unavailable', detail: 'D-Bus session unavailable' };
  assert.equal(rows(value).tray.value, 'Unavailable');
  assert.equal(rows(value).mediaControls.detail, 'D-Bus session unavailable');
});

test('game focus and MIDI disconnects use their live stores independently of native refresh', () => {
  assert.match(rows(snapshot(), midi, { ...game, isFocused: false }).game.status, /Not focused/);
  assert.match(rows(snapshot(), midi, { ...game, isRunning: false, isFocused: false }).game.status, /Not detected/);
  assert.equal(rows(snapshot(), midi, null).game.value, 'None selected');
  const active = { ...midi, selectedPortId: 'opaque', active: true, connected: true, status: 'listening' };
  assert.equal(rows(snapshot(), active).midi.value, 'Stage keyboard');
  assert.equal(rows(snapshot(), active).midi.status, 'Connected · Listening through speakers');
  const disconnected = { ...active, ports: [], active: false, connected: false, error: 'Device disconnected' };
  assert.equal(rows(snapshot(), disconnected).midi.value, 'Selected device unavailable');
  assert.equal(rows(snapshot(), disconnected).midi.status, 'Disconnected · Unavailable');
  assert.equal(rows(snapshot(), disconnected).midi.detail, 'Device disconnected');
  assert.match(rows(snapshot(), { ...active, listenMode: false, status: 'suspended' }).midi.status, /Game output suspended/);
});

test('refresh failure retains environment, invalidates native checks and preserves live store rows', () => {
  const result = rows(snapshot(), midi, game, true);
  assert.equal(result.os.value, 'Windows 11');
  assert.equal(result.keyboard.status, 'Unknown');
  for (const key of ['audio', 'tray', 'mediaControls']) assert.equal(result[key].value, 'Unknown');
  assert.match(result.game.status, /Detected/);
  assert.match(result.midi.status, /Inactive/);
  assert.equal(rows(null, midi, null, true).os.value, 'Unknown');
});

test('both languages localize every displayed label and state', () => {
  for (const locale of [enUS, zhCN]) {
    for (const session of ['windows', 'macos', 'x11', 'wayland', 'unknown']) {
      for (const state of ['ready', 'inactive', 'notChecked', 'checking', 'permissionRequired', 'reconnecting', 'unavailable', 'unknown']) {
        const value = snapshot(session);
        for (const key of ['keyboard', 'audio', 'tray', 'mediaControls']) value[key] = { ...value[key], state };
        assert.ok(!JSON.stringify(rows(value, midi, game, false, locale)).includes('systemInfo.'));
      }
    }
  }
  assert.equal(rows(snapshot(), midi, game, false, zhCN).keyboard.status, '尚未检查');
});

const tick = () => new Promise(resolve => setImmediate(resolve));
function pollingHarness(hidden = false) {
  const document = new EventTarget(); document.hidden = hidden;
  const window = new EventTarget();
  const timers = new Map(); let id = 0, reads = 0;
  let resolve, reject;
  const received = [], errors = [];
  const poller = pollSystemSnapshot(() => {
    reads++;
    return new Promise((ok, fail) => { resolve = ok; reject = fail; });
  }, value => received.push(value), () => errors.push('refresh failed'), {
    document, window,
    setInterval(callback, ms) { assert.equal(ms, 1000); timers.set(++id, callback); return id; },
    clearInterval(timer) { timers.delete(timer); },
  });
  return { document, window, timers, received, errors, poller, get reads() { return reads; },
    resolve: value => resolve(value), reject: () => reject(new Error('offline')),
    visibility(hidden) { document.hidden = hidden; document.dispatchEvent(new Event('visibilitychange')); },
    focus() { window.dispatchEvent(new Event('focus')); },
    interval() { for (const callback of timers.values()) callback(); },
  };
}

test('only reads snapshots, polls once per second and refreshes immediately on return', async () => {
  const h = pollingHarness(); await tick();
  assert.equal(h.reads, 1); assert.equal(h.timers.size, 1);
  h.resolve(snapshot()); await tick(); h.interval(); await tick();
  assert.equal(h.reads, 2);
  h.visibility(true); assert.equal(h.timers.size, 0);
  h.resolve(snapshot('macos')); await tick();
  assert.equal(h.received.length, 1); // hidden response is stale
  h.interval(); h.focus(); await tick(); assert.equal(h.reads, 2);
  h.visibility(false); await tick(); assert.equal(h.reads, 3);
  h.resolve(snapshot('wayland')); await tick();
  h.focus(); await tick(); assert.equal(h.reads, 4);
  h.poller.stop(); h.resolve(snapshot()); await tick();
  h.visibility(false); h.focus(); h.interval(); await tick();
  assert.equal(h.reads, 4); assert.equal(h.timers.size, 0);
  assert.equal(h.received.length, 2);
});

test('hidden mount is passive, requests never overlap, stale results cannot replace newer states', async () => {
  const h = pollingHarness(true); await tick(); assert.equal(h.reads, 0);
  h.visibility(false); await tick(); assert.equal(h.reads, 1);
  h.interval(); h.interval(); await tick(); assert.equal(h.reads, 1);
  h.visibility(true); h.visibility(false); await tick(); assert.equal(h.reads, 1);
  h.resolve(snapshot()); await tick(); assert.equal(h.reads, 2); assert.equal(h.received.length, 0);
  h.reject(); await tick(); assert.equal(h.errors.length, 1);
  h.poller.refresh(); await tick(); assert.equal(h.reads, 3);
  h.resolve(snapshot('x11')); await tick(); assert.equal(h.received.length, 1);
  h.poller.stop();
});

test('unmount before the initial microtask prevents even the queued native read', async () => {
  const h = pollingHarness(); h.poller.stop(); await tick();
  assert.equal(h.reads, 0); assert.equal(h.timers.size, 0);
});
