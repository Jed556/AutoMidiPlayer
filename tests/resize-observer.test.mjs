import test from 'node:test';
import assert from 'node:assert/strict';
import { createFrameResizeObserver } from '../src/renderer/lib/utils/frameResizeObserver.ts';

function harness(t) {
  const originals = new Map(['ResizeObserver', 'requestAnimationFrame', 'cancelAnimationFrame']
    .map(key => [key, Object.getOwnPropertyDescriptor(globalThis, key)]));
  t.after(() => {
    for (const [key, descriptor] of originals) {
      if (descriptor) Object.defineProperty(globalThis, key, descriptor);
      else delete globalThis[key];
    }
  });
  const frames = new Map();
  const observed = new Map();
  let notify;
  let nextFrame = 0;
  globalThis.ResizeObserver = class {
    constructor(callback) { notify = callback; }
    observe(target, options) { observed.set(target, options); }
    unobserve(target) { observed.delete(target); }
    disconnect() { observed.clear(); }
  };
  globalThis.requestAnimationFrame = callback => {
    const id = nextFrame++;
    frames.set(id, callback);
    return id;
  };
  globalThis.cancelAnimationFrame = id => frames.delete(id);
  return {
    frames, observed,
    notify: entries => notify(entries),
    step() {
      const callbacks = [...frames.values()];
      frames.clear();
      for (const callback of callbacks) callback();
    },
  };
}

test('resize UI updates run outside observer delivery and retain the latest entry for every target', t => {
  const h = harness(t);
  const calls = [];
  const observer = createFrameResizeObserver(entries => calls.push(entries));
  const panel = {};
  const popup = {};
  const panelEntry = { target: panel, contentRect: { width: 900 } };
  const popupEntry = { target: popup, contentRect: { width: 44 } };
  const latestPanelEntry = { target: panel, contentRect: { width: 880 } };
  observer.observe(panel, { box: 'border-box' });
  observer.observe(popup);
  assert.deepEqual(h.observed.get(panel), { box: 'border-box' });
  h.notify([panelEntry, popupEntry]);
  h.notify([latestPanelEntry]);
  assert.equal(calls.length, 0, 'Do not change layout during ResizeObserver delivery');
  assert.equal(h.frames.size, 1, 'Coalesce notifications, including animation frame ID zero');
  h.step();
  assert.deepEqual(calls, [[latestPanelEntry, popupEntry]]);
  assert.equal(h.frames.size, 0);
});

test('a layout change during an update schedules a separate frame', t => {
  const h = harness(t);
  const target = {};
  let calls = 0;
  createFrameResizeObserver(() => {
    calls++;
    if (calls === 1) h.notify([{ target }]);
  });
  h.notify([{ target }]);
  h.step();
  assert.equal(calls, 1);
  assert.equal(h.frames.size, 1);
  h.step();
  assert.equal(calls, 2);
  assert.equal(h.frames.size, 0);
});

test('disconnect cancels queued updates so unmounted components are never updated', t => {
  const h = harness(t);
  let calls = 0;
  const observer = createFrameResizeObserver(() => calls++);
  const target = {};
  observer.observe(target);
  h.notify([{ target }]);
  observer.disconnect();
  assert.equal(h.observed.size, 0);
  assert.equal(h.frames.size, 0);
  h.step();
  assert.equal(calls, 0);

  observer.observe(target);
  h.notify([{ target }]);
  h.step();
  assert.equal(calls, 1, 'Re-observing after disconnect starts a fresh batch');
});

test('unobserve removes stale queued entries without dropping other targets', t => {
  const h = harness(t);
  const calls = [];
  const observer = createFrameResizeObserver(entries => calls.push(entries));
  const removed = {};
  const retained = {};
  observer.observe(removed);
  observer.observe(retained);
  h.notify([{ target: removed }, { target: retained }]);
  observer.unobserve(removed);
  h.step();
  assert.deepEqual(calls, [[{ target: retained }]]);
  assert.equal(h.observed.has(removed), false);
  h.notify([{ target: retained }]);
  observer.unobserve(retained);
  h.step();
  assert.equal(calls.length, 1, 'Do not deliver an empty batch');
});
