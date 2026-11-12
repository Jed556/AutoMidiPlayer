import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { stripTypeScriptTypes } from 'node:module';
import { MAX_SCROLL_POSITIONS, manualViewportPosition, normalizeScrollPosition, ScrollPositionStore } from '../src/renderer/lib/utils/scrollPositions.ts';

const actionSource = stripTypeScriptTypes(readFileSync(new URL('../src/renderer/lib/actions/persistScroll.ts', import.meta.url), 'utf8'), { mode: 'transform' })
  .replace(/^import .*scrollPositionStore.*;$/m, 'const scrollPositionStore = undefined;');
const { attachScrollPersistence } = await import(`data:text/javascript;base64,${Buffer.from(actionSource).toString('base64')}`);

function storeWith(initial = {}) {
  const saves = [];
  const store = new ScrollPositionStore(async () => initial, value => saves.push(structuredClone(value)));
  return { store, saves };
}

function desktop(t) {
  const previous = Object.fromEntries(['requestAnimationFrame', 'cancelAnimationFrame', 'ResizeObserver', 'MutationObserver'].map(key => [key, globalThis[key]]));
  let id = 0;
  const frames = new Map();
  const observers = [];
  globalThis.requestAnimationFrame = callback => { frames.set(++id, callback); return id; };
  globalThis.cancelAnimationFrame = frame => frames.delete(frame);
  globalThis.ResizeObserver = class {
    constructor(callback) { this.callback = callback; observers.push(this); }
    observe() {}
    disconnect() { this.disconnected = true; }
  };
  globalThis.MutationObserver = class extends globalThis.ResizeObserver {};
  t.after(() => { for (const [key, value] of Object.entries(previous)) globalThis[key] = value; });
  return {
    resize() { for (const observer of observers) if (!observer.disconnected) observer.callback([]); },
    frame() { const pending = [...frames.values()]; frames.clear(); for (const callback of pending) callback(0); },
    get pending() { return frames.size; },
  };
}

class ScrollElement extends EventTarget {
  scrollLeft = 0;
  scrollTop = 0;
  scrollWidth = 100;
  scrollHeight = 100;
  clientWidth = 100;
  clientHeight = 100;
  children = [];
}

test('only finite nonnegative viewport coordinates are restored', async () => {
  for (const invalid of [null, {}, { x: NaN, y: 0 }, { x: 1, y: -3 }, { x: '4', y: 0 }, { x: 0, y: Infinity }]) {
    assert.equal(normalizeScrollPosition(invalid), undefined);
  }
  assert.deepEqual(normalizeScrollPosition({ x: 1e20, y: 24 }), { x: 1e9, y: 24 });
  const { store } = storeWith(JSON.parse('{"__proto__":{"x":2,"y":3},"valid":{"x":4,"y":5}}'));
  await store.ready();
  assert.equal(store.get('__proto__'), undefined);
  assert.deepEqual(store.get('valid'), { x: 4, y: 5 });
});

test('page, playlist and song positions survive store recreation independently', async () => {
  const { store, saves } = storeWith();
  await store.ready();
  store.set('settings:content', { x: 0, y: 260 });
  store.set('songs:favorites', { x: 0, y: 1100 });
  store.set('studio:song-a', { x: 300, y: 440 });
  store.set('studio:song-b', { x: 180, y: 32 });
  const restored = storeWith(saves.at(-1)).store;
  await restored.ready();
  for (const key of ['settings:content', 'songs:favorites', 'studio:song-a', 'studio:song-b']) assert.deepEqual(restored.get(key), store.get(key));
  const value = restored.get('studio:song-a');
  value.y = 999;
  assert.equal(restored.get('studio:song-a').y, 440, 'Callers cannot mutate the stored position');
});

test('viewport history is bounded and recently visited positions are retained', async () => {
  const { store, saves } = storeWith();
  await store.ready();
  for (let i = 0; i < MAX_SCROLL_POSITIONS; i++) store.set(`song:${i}`, { x: i, y: i });
  store.get('song:0');
  store.set('song:new', { x: 1, y: 2 });
  assert.equal(Object.keys(saves.at(-1)).length, MAX_SCROLL_POSITIONS);
  assert.equal(store.get('song:1'), undefined);
  assert.deepEqual(store.get('song:0'), { x: 0, y: 0 });
  const count = saves.length;
  store.set('song:new', { x: 1, y: 2 });
  assert.equal(saves.length, count, 'Unchanged viewports do not write settings');
});

test('playhead following does not schedule writes or overwrite the manual horizontal viewport', async () => {
  const { store, saves } = storeWith({ 'studio:song': { x: 400, y: 280 } });
  await store.ready();
  let remembered = store.get('studio:song');
  let changes = 0;
  const observe = (current, locked) => {
    const next = manualViewportPosition(remembered, current, locked);
    if (next === remembered) return;
    changes++;
    remembered = next;
    store.set('studio:song', next);
  };
  for (let frame = 0; frame < 600; frame++) observe({ x: 1000 + frame * 4, y: 280 }, true);
  assert.equal(changes, 0, 'Automatic locked movement never schedules persistence');
  assert.equal(saves.length, 0);
  observe({ x: 3400, y: 350 }, true);
  assert.deepEqual(store.get('studio:song'), { x: 400, y: 350 }, 'Vertical movement is still remembered while locked');
  observe({ x: 720, y: 350 }, false);
  assert.deepEqual(store.get('studio:song'), { x: 720, y: 350 }, 'Unlocked panning updates the manual horizontal position');
  for (let frame = 0; frame < 600; frame++) observe({ x: 4000 + frame * 4, y: 350 }, true);
  assert.equal(saves.length, 2, 'Relocking adds no writes while following playback');
});

test('restoration waits through empty content and notifies virtualized scroll listeners', async t => {
  const display = desktop(t);
  const { store, saves } = storeWith({ tracks: { x: 0, y: 900 } });
  const node = new ScrollElement();
  let scrollEvents = 0;
  node.addEventListener('scroll', () => scrollEvents++);
  const detach = attachScrollPersistence(node, 'tracks', store);
  await store.ready();
  await Promise.resolve();
  display.frame();
  assert.equal(node.scrollTop, 0);
  assert.equal(saves.length, 0, 'Loading content must not replace a saved viewport with zero');
  node.scrollHeight = 2000;
  display.resize();
  display.frame();
  assert.equal(node.scrollTop, 900);
  assert.ok(scrollEvents > 0);
  detach();
  assert.equal(saves.length, 0, 'Unchanged restoration needs no disk write');
});

test('user scrolling cancels pending restore and saves the current viewport', async t => {
  const display = desktop(t);
  const { store } = storeWith({ queue: { x: 0, y: 900 } });
  const node = new ScrollElement();
  const detach = attachScrollPersistence(node, 'queue', store);
  await store.ready();
  await Promise.resolve();
  display.frame();
  node.dispatchEvent(new Event('wheel'));
  node.scrollTop = 45;
  node.dispatchEvent(new Event('scroll'));
  node.scrollHeight = 2000;
  display.resize();
  display.frame();
  assert.equal(node.scrollTop, 45);
  assert.deepEqual(store.get('queue'), { x: 0, y: 45 });
  detach();
});

test('interaction before preferences load wins over the saved position', async t => {
  const display = desktop(t);
  let finishLoad;
  const store = new ScrollPositionStore(() => new Promise(resolve => { finishLoad = resolve; }), () => {});
  const node = new ScrollElement();
  node.scrollHeight = 2000;
  const detach = attachScrollPersistence(node, 'sheet:song', store);
  node.dispatchEvent(new Event('pointerdown'));
  node.scrollTop = 80;
  finishLoad({ 'sheet:song': { x: 0, y: 700 } });
  await store.ready();
  await Promise.resolve();
  display.frame();
  assert.equal(node.scrollTop, 80);
  assert.deepEqual(store.get('sheet:song'), { x: 0, y: 80 });
  detach();
});

test('destroying a viewport cancels delayed restore and preserves a clipped target', async t => {
  const display = desktop(t);
  const { store } = storeWith({ about: { x: 0, y: 600 } });
  const node = new ScrollElement();
  const detach = attachScrollPersistence(node, 'about', store);
  await store.ready();
  await Promise.resolve();
  display.frame();
  detach();
  node.scrollHeight = 2000;
  display.resize();
  display.frame();
  assert.equal(node.scrollTop, 0);
  assert.deepEqual(store.get('about'), { x: 0, y: 600 });
  assert.equal(display.pending, 0);
});

test('switching to a new viewport starts at zero without replacing the previous position', async t => {
  const display = desktop(t);
  const { store } = storeWith({ 'tracks:first-song': { x: 0, y: 300 } });
  const node = new ScrollElement();
  node.scrollHeight = 2000;
  const detachFirst = attachScrollPersistence(node, 'tracks:first-song', store);
  await store.ready();
  await Promise.resolve();
  display.frame();
  assert.equal(node.scrollTop, 300);
  // A content update can clamp the element before the action's old key is detached.
  node.scrollTop = 0;
  detachFirst();
  const detachSecond = attachScrollPersistence(node, 'tracks:second-song', store);
  node.scrollTop = 300;
  await store.ready();
  await Promise.resolve();
  display.frame();
  assert.equal(node.scrollTop, 0);
  assert.deepEqual(store.get('tracks:first-song'), { x: 0, y: 300 });
  detachSecond();
});
