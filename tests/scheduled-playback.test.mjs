import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { stripTypeScriptTypes } from 'node:module';
import { compileModule } from 'svelte/compiler';

let harnessId = 0;
async function harness({ playing = false, hasSong = true, fail = false, refusePlay = false, deferPlay = false } = {}) {
  let now = new Date(2026, 10, 12, 10, 0, 20).getTime();
  let callback;
  const calls = [];
  const notifications = [];
  let finishPlay;
  const waitingPlay = new Promise(resolve => { finishPlay = resolve; });
  const key = `__schedule${++harnessId}`;
  globalThis[key] = {
    playbackStore: { playbackQueue: [], isPlaying: false },
    notificationCenter: new Proxy({}, { get: (_, method) => (...args) => notifications.push([method, ...args]) }),
    t: key => key,
  };
  const source = stripTypeScriptTypes(readFileSync(new URL('../src/renderer/lib/state/scheduledPlaybackStore.svelte.ts', import.meta.url), 'utf8'), { mode: 'transform' })
    .replace(/^import \{([^}]+)\} from '[^']+';/gm, (_, names) => `const {${names}} = globalThis.${key};`);
  const compiled = compileModule(source, { filename: 'scheduledPlaybackStore.svelte.js', generate: 'client' }).js.code
    .replace("'svelte/internal/client'", JSON.stringify(import.meta.resolve('svelte/internal/client')));
  const { ScheduledPlaybackStore } = await import(`data:text/javascript;base64,${Buffer.from(compiled).toString('base64')}`);
  delete globalThis[key];
  const store = new ScheduledPlaybackStore({
    now: () => now,
    isPlaying: () => playing,
    hasSong: () => hasSong,
    play: async () => { calls.push('play'); if (deferPlay) await waitingPlay; if (fail) throw new Error('Output unavailable'); if (!refusePlay) playing = true; },
    pause: async () => { calls.push('pause'); if (fail) throw new Error('Output unavailable'); playing = false; },
    interval: fn => { callback = fn; return 1; },
    clearInterval: () => {},
  });
  store.hour = 10;
  store.minute = 1;
  return { store, calls, notifications, finishPlay, setPlaying: value => { playing = value; },
    setHasSong: value => { hasSong = value; },
    tick: async milliseconds => { now += milliseconds; callback?.(); await new Promise(resolve => setImmediate(resolve)); },
    callback: () => callback?.(),
  };
}

test('absolute deadline catches throttled callbacks and executes only once', async () => {
  const h = await harness();
  assert.equal(h.store.arm(), true);
  assert.equal(h.store.remainingSeconds, 40);
  await h.tick(35_000);
  assert.equal(h.store.remainingSeconds, 5);
  assert.deepEqual(h.calls, []);
  // A suspended background view can miss ticks; wall-clock deadline still wins.
  await h.tick(60_000);
  assert.deepEqual(h.calls, ['play']);
  assert.equal(h.store.active, false);
  await h.tick(60_000);
  assert.deepEqual(h.calls, ['play']);
  assert.equal(h.notifications.at(-1)[0], 'success');
});

test('captures intended pause rather than toggling playback at the deadline', async () => {
  const h = await harness({ playing: true });
  h.store.arm();
  assert.equal(h.store.action, 'pause');
  h.setPlaying(false);
  await h.tick(40_000);
  assert.deepEqual(h.calls, [], 'An already paused song must not start again');
  assert.equal(h.notifications.at(-1)[2], 'instrument.timerPausedMsg');
});

test('scheduled pause survives unrelated playback changes and calls pause explicitly', async () => {
  const h = await harness({ playing: true });
  h.store.arm();
  await h.tick(40_000);
  assert.deepEqual(h.calls, ['pause']);
});

test('cancel invalidates callbacks; rearming preserves the next deadline', async () => {
  const h = await harness();
  h.store.arm();
  h.store.cancel(false);
  await h.tick(60_000);
  assert.deepEqual(h.calls, []);
  h.store.minute = 2;
  h.store.arm();
  await h.tick(40_000);
  assert.deepEqual(h.calls, ['play']);
});

test('past local time schedules the following day and invalid times are rejected', async () => {
  const h = await harness();
  h.store.minute = 0;
  h.store.arm();
  assert.equal(h.store.remainingSeconds, 24 * 60 * 60 - 20);
  h.store.cancel(false);
  h.store.hour = 24;
  assert.equal(h.store.arm(), false);
  h.store.hour = 10;
  h.store.minute = NaN;
  assert.equal(h.store.arm(), false);
});

test('missing songs and failed or declined playback never report success', async () => {
  const empty = await harness({ hasSong: false });
  assert.equal(empty.store.arm(), false);
  assert.equal(empty.notifications.at(-1)[0], 'error');
  for (const options of [{ fail: true }, { refusePlay: true }]) {
    const h = await harness(options);
    h.store.arm();
    await h.tick(40_000);
    assert.equal(h.store.executing, false);
    assert.equal(h.notifications.at(-1)[0], 'error');
    assert.equal(h.notifications.some(([kind]) => kind === 'success'), false);
  }
  const removed = await harness();
  removed.store.arm();
  removed.setHasSong(false);
  await removed.tick(40_000);
  assert.deepEqual(removed.calls, []);
  assert.equal(removed.notifications.at(-1)[0], 'error');
});

test('reset cancellation waits for an in-flight play before the caller stops and clears data', async () => {
  const h = await harness({ deferPlay: true });
  h.store.arm();
  await h.tick(40_000);
  assert.equal(h.store.executing, true);
  let stopped = false;
  const reset = (async () => {
    await h.store.cancelAndWait();
    h.setPlaying(false);
    stopped = true;
  })();
  await new Promise(resolve => setImmediate(resolve));
  assert.equal(stopped, false, 'Stopping earlier would let the pending play restart playback');
  h.finishPlay();
  await reset;
  assert.equal(stopped, true);
  assert.equal(h.store.executing, false);
  assert.equal(h.store.active, false);
  assert.equal(h.notifications.some(([kind]) => kind === 'success'), false, 'Cancelled work must not show a completion toast');
});
