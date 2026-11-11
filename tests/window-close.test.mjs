import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { stripTypeScriptTypes } from 'node:module';
import { createWindowCloseHandler } from '../src/renderer/lib/backend/windowClose.ts';

test('window controls remain usable while backend startup is pending or failed', async () => {
  const calls = [];
  let source = stripTypeScriptTypes(readFileSync(new URL('../src/renderer/lib/backend/runtime.ts', import.meta.url), 'utf8'))
    .replace("import { waitForBackendReady } from './startupGate';", 'const waitForBackendReady = mockReady;')
    .replaceAll("await import('@tauri-apps/api/core')", '({ invoke: mockInvoke })')
    .replaceAll('export ', '');
  source += '\nreturn { invokeTauri };';
  for (const ready of [() => new Promise(() => {}), () => Promise.reject(new Error('Startup failed'))]) {
    const runtime = new Function('window', 'mockReady', 'mockInvoke', source)(
      { __TAURI_INTERNALS__: {} }, ready, async command => calls.push(command));
    await runtime.invokeTauri('Window', 'minimize_window');
    await runtime.invokeTauri('Window', 'complete_window_close', { cancel: false });
  }
  assert.deepEqual(calls, ['minimize_window', 'complete_window_close', 'minimize_window', 'complete_window_close']);
});

test('closing to tray can save and close repeatedly without destroying the listener', async () => {
  const calls = [];
  const close = createWindowCloseHandler({ completeClose: async cancel => calls.push(['close', cancel]) },
    async () => { calls.push(['save']); }, assert.fail);
  const event = { preventDefault: () => calls.push(['prevent']) };
  await close(event);
  await close(event);
  assert.deepEqual(calls, [
    ['prevent'], ['save'], ['close', undefined],
    ['prevent'], ['save'], ['close', undefined],
  ]);
});

test('a duplicate close request cannot destroy the window before saving finishes', async () => {
  const calls = [];
  let finish;
  const close = createWindowCloseHandler({ completeClose: async () => calls.push('close') },
    () => new Promise(resolve => { finish = resolve; }), assert.fail);
  const event = { preventDefault: () => calls.push('prevent') };
  const pending = close(event);
  await close(event);
  assert.deepEqual(calls, ['prevent', 'prevent']);
  finish();
  await pending;
  assert.deepEqual(calls, ['prevent', 'prevent', 'close']);
});

test('save failure cancels native Exit and permits a later retry', async () => {
  const calls = [];
  const errors = [];
  const failed = new Error('Disk unavailable');
  let attempts = 0;
  const close = createWindowCloseHandler({ completeClose: async cancel => calls.push(cancel) },
    async () => { if (++attempts === 1) throw failed; }, error => errors.push(error));
  const event = { preventDefault() {} };
  await close(event);
  assert.deepEqual(calls, [true]);
  assert.deepEqual(errors, [failed]);
  await close(event);
  assert.deepEqual(calls, [true, undefined]);
});
