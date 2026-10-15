import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { stripTypeScriptTypes } from 'node:module';
import { compileModule } from 'svelte/compiler';
import * as svelte from 'svelte/internal/client';

let harnessId = 0;

async function harness(t) {
  const keys = ['window', 'document', 'requestAnimationFrame', 'MutationObserver'];
  const originals = new Map(keys.map(key => [key, Object.getOwnPropertyDescriptor(globalThis, key)]));
  const events = new Map();
  let frames = [];
  let onMutation;
  globalThis.MutationObserver = class {
    constructor(callback) { onMutation = callback; }
    observe() {}
  };
  globalThis.window = {
    innerWidth: 1024, innerHeight: 768,
    addEventListener(name, callback) {
      if (!events.has(name)) events.set(name, new Set());
      events.get(name).add(callback);
    },
    removeEventListener(name, callback) { events.get(name)?.delete(callback); },
  };
  globalThis.document = {
    documentElement: { classList: { contains: () => false } },
    body: {},
    querySelectorAll: () => [],
  };
  globalThis.requestAnimationFrame = callback => { frames.push(callback); return frames.length; };
  const key = `__tooltipHarness${++harnessId}`;
  globalThis[key] = { animationStore: { isOff: true, durationMs: 0 } };
  const source = stripTypeScriptTypes(readFileSync(new URL('../src/renderer/lib/ui/tooltipManager.svelte.ts', import.meta.url), 'utf8'))
    .replace("import { animationStore } from '../state/animationStore.svelte.js';", `const { animationStore } = globalThis.${key};`);
  const compiled = compileModule(source, { filename: 'tooltipManager.svelte.js', generate: 'client' }).js.code
    .replace("'svelte/internal/client'", JSON.stringify(import.meta.resolve('svelte/internal/client')));
  const { tooltipManager: manager } = await import(`data:text/javascript;base64,${Buffer.from(compiled).toString('base64')}`);
  globalThis[key] = { tooltipManager: manager };
  const action = stripTypeScriptTypes(readFileSync(new URL('../src/renderer/lib/actions/tooltip.ts', import.meta.url), 'utf8'))
    .replace(/import \{[^}]+\} from '..\/ui\/tooltipManager.svelte';/, `const { tooltipManager } = globalThis.${key};`);
  const { tooltip } = await import(`data:text/javascript;base64,${Buffer.from(action).toString('base64')}`);
  delete globalThis[key];
  manager.initGlobal();
  t.after(() => {
    manager.hide(true);
    for (const [name, descriptor] of originals) {
      if (descriptor) Object.defineProperty(globalThis, name, descriptor);
      else delete globalThis[name];
    }
  });
  return {
    manager, tooltip,
    dispatch(name, event = {}) { for (const callback of events.get(name) ?? []) callback(event); },
    mutate(target, attributeName) { onMutation([{ type: 'attributes', target, attributeName }]); },
    frame() { const pending = frames; frames = []; pending.forEach(callback => callback()); },
    activate(target, text = 'Tooltip', options = {}, mouseEvent) {
      manager.show(target, { text, delay: 10_000, exitDelay: 0, ...options }, mouseEvent);
      manager.isRendered = true;
      manager.isVisible = true;
    },
  };
}

function target(rect = { left: 100, right: 200, top: 100, bottom: 130, width: 100, height: 30 }, attributes = {}) {
  return {
    getAttribute: name => attributes[name] ?? null,
    hasAttribute: name => name in attributes,
    setAttribute(name, value) { attributes[name] = value; },
    removeAttribute(name) { delete attributes[name]; },
    closest(selector) {
      return selector.includes('[title]') && ['title', 'data-tooltip', 'data-amp-tooltip'].some(key => key in attributes) ? this : null;
    },
    contains(node) { return node === this; },
    addEventListener() {},
    removeEventListener() {},
    getBoundingClientRect: () => rect,
  };
}

function duringRender(callback) {
  // Svelte uses the same mutation guard for templates and derived expressions.
  return svelte.get(svelte.derived(() => { callback(); return true; }));
}

test('removing a focused control during rendering defers tooltip state changes', async t => {
  const h = await harness(t);
  h.activate(target());
  h.manager.isVisible = false;
  assert.throws(() => duringRender(() => { h.manager.isVisible = false; }), /state_unsafe_mutation/);
  assert.doesNotThrow(() => duringRender(() => h.dispatch('focusout')));
  assert.equal(h.manager.isRendered, true, 'The render pass cannot update global tooltip state');
  await Promise.resolve();
  assert.equal(h.manager.isVisible, false);
  assert.equal(h.manager.isRendered, false);
});

test('a delayed focusout cannot dismiss a newly shown tooltip, including the same target', async t => {
  const h = await harness(t);
  const first = target();
  const second = target();
  h.activate(first);
  h.dispatch('focusout');
  h.activate(second, 'Second');
  await Promise.resolve();
  assert.equal(h.manager.isVisible, true);
  assert.equal(h.manager.options.text, 'Second');
  h.dispatch('focusout');
  h.activate(second, 'Refocused');
  await Promise.resolve();
  assert.equal(h.manager.isVisible, true);
  assert.equal(h.manager.options.text, 'Refocused');
});

test('destroying a tooltip action is safe during rendering and only hides its own target', async t => {
  const h = await harness(t);
  const active = target();
  const unrelated = h.tooltip(target(), 'Unrelated');
  h.activate(active);
  assert.doesNotThrow(() => duringRender(() => unrelated.destroy()));
  await Promise.resolve();
  assert.equal(h.manager.isVisible, true);
  const action = h.tooltip(active, 'Active');
  assert.doesNotThrow(() => duringRender(() => action.destroy()));
  await Promise.resolve();
  assert.equal(h.manager.isVisible, false);
  assert.equal(h.manager.isRendered, false);
});

test('disabling an unrelated tooltip action preserves the current tooltip', async t => {
  const h = await harness(t);
  const action = h.tooltip(target(), 'Inactive');
  h.activate(target());
  action.update({ text: 'Inactive', disabled: true });
  await Promise.resolve();
  assert.equal(h.manager.isVisible, true);
});

test('follow-off anchors to the element center regardless of entry point or pointer movement', async t => {
  const h = await harness(t);
  const wide = target({ left: 100, right: 700, top: 200, bottom: 400, width: 600, height: 200 });
  for (const position of ['top', 'bottom', 'left', 'right']) {
    h.activate(wide, 'Anchored', { position, followCursor: false }, { clientX: 150, clientY: 220 });
    h.manager.updatePosition();
    const before = { ...h.manager.coords };
    if (position === 'top' || position === 'bottom') assert.equal(before.left, 400);
    else assert.equal(before.top, 300);
    h.manager.updateMousePosition({ clientX: 650, clientY: 380 });
    h.frame();
    h.manager.updatePosition(); // Resize/scroll must not re-anchor to the last pointer position either.
    assert.deepEqual(h.manager.coords, before);
  }
});

test('global follow toggles reposition the active tooltip and follow pointer movement', async t => {
  const h = await harness(t);
  h.activate(target(), 'Follow', {}, { clientX: 120, clientY: 115 });
  h.manager.updatePosition();
  assert.equal(h.manager.coords.left, 150);
  h.manager.setFollowCursor(true);
  assert.equal(h.manager.coords.left, 120);
  h.manager.updateMousePosition({ clientX: 180, clientY: 120 });
  h.frame();
  assert.equal(h.manager.coords.left, 180);
  h.manager.setFollowCursor(false);
  assert.equal(h.manager.coords.left, 150);
});

test('changing an active action follow/position option applies without changing its text', async t => {
  const h = await harness(t);
  const node = target();
  const action = h.tooltip(node, { text: 'Same', followCursor: false });
  h.activate(node, 'Same', { followCursor: false }, { clientX: 180, clientY: 115 });
  h.manager.updatePosition();
  assert.doesNotThrow(() => duringRender(() => action.update({ text: 'Same', followCursor: true, position: 'bottom' })));
  await Promise.resolve();
  h.frame();
  assert.equal(h.manager.coords.left, 180);
  assert.equal(h.manager.coords.actualPosition, 'bottom');
  action.update({ text: 'Same', followCursor: false });
  await Promise.resolve();
  assert.equal(h.manager.coords.left, 150);
});

test('per-tooltip follow overrides take precedence over the global default', async t => {
  const h = await harness(t);
  h.manager.setFollowCursor(true);
  h.activate(target(), 'Override', { followCursor: false }, { clientX: 180, clientY: 115 });
  h.manager.updatePosition();
  assert.equal(h.manager.coords.left, 150);
  h.manager.setFollowCursor(false);
  h.activate(target(), 'Override', { followCursor: true }, { clientX: 180, clientY: 115 });
  h.manager.updatePosition();
  assert.equal(h.manager.coords.left, 180);
});

test('keyboard focus anchors to the focused element even when follow is enabled', async t => {
  const h = await harness(t);
  h.manager.setFollowCursor(true);
  h.manager.updateMousePosition({ clientX: 800, clientY: 600 });
  h.activate(target());
  h.manager.updatePosition();
  assert.equal(h.manager.coords.left, 150);
  assert.equal(h.manager.coords.top, 94);
});

test('moving the pointer over a focused control resumes following after click focus', async t => {
  const h = await harness(t);
  const node = target();
  h.manager.setFollowCursor(true);
  h.activate(node);
  h.manager.updatePosition();
  assert.equal(h.manager.coords.left, 150);
  h.dispatch('mousemove', { target: node, clientX: 180, clientY: 115 });
  h.frame();
  assert.equal(h.manager.coords.left, 180);
});

test('tooltips flip at each viewport edge and stay inside the viewport in both modes', async t => {
  const h = await harness(t);
  h.manager.registerTooltipElement({ offsetWidth: 120, offsetHeight: 28 });
  const edges = [
    ['top', 'bottom', { left: 400, right: 500, top: 0, bottom: 30, width: 100, height: 30 }, 450, 5],
    ['bottom', 'top', { left: 400, right: 500, top: 738, bottom: 768, width: 100, height: 30 }, 450, 763],
    ['left', 'right', { left: 0, right: 100, top: 300, bottom: 330, width: 100, height: 30 }, 5, 315],
    ['right', 'left', { left: 924, right: 1024, top: 300, bottom: 330, width: 100, height: 30 }, 1019, 315],
  ];
  for (const followCursor of [false, true]) {
    for (const [position, expected, rect, clientX, clientY] of edges) {
      h.activate(target(rect), 'Edge', { position, followCursor }, { clientX, clientY });
      h.manager.updatePosition();
      const { top, left, actualPosition } = h.manager.coords;
      assert.equal(actualPosition, expected);
      const x = left - (actualPosition === 'left' ? 120 : actualPosition === 'right' ? 0 : 60);
      const y = top - (actualPosition === 'top' ? 28 : actualPosition === 'bottom' ? 0 : 14);
      assert.ok(x >= 8 && x + 120 <= 1016, `${position}: horizontal bounds`);
      assert.ok(y >= 8 && y + 28 <= 760, `${position}: vertical bounds`);
    }
  }
});

test('deferred action options cannot overwrite a replacement tooltip', async t => {
  const h = await harness(t);
  const node = target();
  const action = h.tooltip(node, 'First');
  h.activate(node, 'First');
  action.update({ text: 'Updated', followCursor: true });
  h.activate(node, 'Replacement', { followCursor: false });
  await Promise.resolve();
  h.frame();
  assert.equal(h.manager.options.text, 'Replacement');
  assert.equal(h.manager.options.followCursor, false);
});

test('global attribute tooltips respond to follow and position changes while hovered', async t => {
  const h = await harness(t);
  const node = target(undefined, { 'data-tooltip': 'Attribute tooltip', 'data-tooltip-follow': 'false' });
  h.dispatch('mouseover', { target: node, clientX: 180, clientY: 115 });
  h.manager.isRendered = true;
  h.manager.updatePosition();
  assert.equal(h.manager.coords.left, 150);
  node.setAttribute('data-tooltip-follow', 'true');
  h.mutate(node, 'data-tooltip-follow');
  assert.equal(h.manager.coords.left, 180);
  node.setAttribute('data-tooltip-position', 'bottom');
  h.mutate(node, 'data-tooltip-position');
  assert.equal(h.manager.coords.actualPosition, 'bottom');
  h.dispatch('mousemove', { clientX: 120, clientY: 120 });
  h.frame();
  assert.equal(h.manager.coords.left, 120);
  node.setAttribute('data-tooltip-follow', 'false');
  h.mutate(node, 'data-tooltip-follow');
  assert.equal(h.manager.coords.left, 150);
  node.removeAttribute('data-tooltip-position');
  h.mutate(node, 'data-tooltip-position');
  h.manager.setDefaultPosition('right');
  assert.equal(h.manager.coords.actualPosition, 'right');
});
