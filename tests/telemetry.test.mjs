import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { stripTypeScriptTypes } from 'node:module';

function harness({ desktop = true, invoke = async () => {} } = {}) {
  const listeners = new Map();
  const window = {
    ...(desktop ? { __TAURI_INTERNALS__: {} } : {}),
    addEventListener: (name, handler) => listeners.set(name, handler),
  };
  const source = stripTypeScriptTypes(
    readFileSync(new URL('../src/renderer/lib/infrastructure/telemetry.ts', import.meta.url), 'utf8'),
  )
    .replace("import { logger } from './logger.js';", 'const logger = { error: async () => {} };')
    .replaceAll("await import('@tauri-apps/api/core')", '({ invoke: mockInvoke })')
    .replace('export const telemetry = new Telemetry();', 'return new Telemetry();');
  return { telemetry: new Function('window', 'mockInvoke', source)(window, invoke), listeners };
}

test('installing error handlers preserves the native saved telemetry preference', async () => {
  const calls = [];
  const { telemetry, listeners } = harness({ invoke: async (...args) => calls.push(args) });
  telemetry.init();
  telemetry.init();
  await new Promise(resolve => setImmediate(resolve));
  assert.deepEqual(calls, []);
  assert.deepEqual([...listeners.keys()], ['error', 'unhandledrejection']);
  await telemetry.setEnabled(true);
  assert.deepEqual(calls, [['set_telemetry_enabled', { enabled: true }]]);
});

test('the test button returns the native event ID and surfaces native failures', async () => {
  let fail = false;
  const { telemetry } = harness({
    invoke: async (command) => {
      assert.equal(command, 'send_sentry_test');
      if (fail) throw 'Sentry did not capture the test event.';
      return 'Sentry test queued and flushed. Event ID: diagnostic-id. Environment: debug.';
    },
  });
  const result = await telemetry.sendTest();
  assert.equal(result.success, true);
  assert.match(result.message, /Event ID: diagnostic-id/);
  fail = true;
  assert.deepEqual(await telemetry.sendTest(), {
    success: false,
    message: 'Sentry did not capture the test event.',
  });
});

test('browser previews explain that the Sentry test needs the desktop app', async () => {
  const { telemetry } = harness({ desktop: false, invoke: () => assert.fail('unexpected IPC') });
  assert.deepEqual(await telemetry.sendTest(), {
    success: false,
    message: 'Telemetry requires the desktop application.',
  });
});
