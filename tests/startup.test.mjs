import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { stripTypeScriptTypes } from 'node:module';
import { compile } from 'svelte/compiler';

const source = stripTypeScriptTypes(readFileSync(new URL('../src/renderer/lib/backend/startupProgress.ts', import.meta.url), 'utf8'));
const { createStartupProgress } = await import(`data:text/javascript;base64,${Buffer.from(source).toString('base64')}`);

test('scoped splash animations complete startup and update transitions without handling child animations', () => {
  const component = readFileSync(new URL('../src/renderer/lib/components/Layout/StartupScreen.svelte', import.meta.url), 'utf8');
  const { css } = compile(component, { filename: 'StartupScreen.svelte', generate: 'client' });
  const names = [...css.code.matchAll(/@keyframes\s+([^\s{]+)/g)].map(match => match[1]);
  const handler = component.match(/onanimationend=\{\(event\) => \{([\s\S]*?)\n  \}\}/)[1];
  const dispatch = new Function('event', 'finished', 'entering', 'onentered', 'onfinished', handler);
  const screen = {};
  let entered = 0;
  let finished = 0;
  for (const [suffix, isFinished, isEntering] of [['startup-out', true, false], ['startup-in', false, true]]) {
    const animationName = names.find(name => name.endsWith(suffix));
    assert.ok(animationName);
    assert.notEqual(animationName, suffix, 'Exercise the actual Svelte-scoped animation name');
    dispatch({ target: screen, currentTarget: screen, animationName }, isFinished, isEntering,
      () => entered++, () => finished++);
  }
  assert.equal(entered, 1);
  assert.equal(finished, 1);
  dispatch({ target: {}, currentTarget: screen, animationName: names[0] }, true, false,
    () => entered++, () => finished++);
  assert.equal(finished, 1, 'Logo and text animations must not dismiss the screen');
});

function harness() {
  let now = 0;
  let nextFrame = 0;
  const frames = new Map();
  const progress = createStartupProgress({
    now: () => now,
    frame: callback => { frames.set(++nextFrame, callback); return nextFrame; },
    cancel: id => frames.delete(id),
  });
  return { progress, step(ms) {
    now += ms;
    const pending = [...frames.values()];
    frames.clear();
    for (const frame of pending) frame(now);
  } };
}

test('progress eases between milestones and never retreats on stale or repeated stages', () => {
  const { progress, step } = harness();
  progress.advance(40);
  step(30);
  assert.ok(progress.current > 10 && progress.current < 40);
  step(30);
  assert.equal(progress.current, 35);
  progress.advance(48);
  step(20);
  const beforeStaleStage = progress.current;
  progress.advance(8);
  progress.advance(48);
  step(60);
  assert.equal(progress.current, 48);
  assert.ok(progress.current > beforeStaleStage);
});

test('HTML and Svelte subscribers inherit the same in-flight value without resetting', () => {
  const { progress, step } = harness();
  const html = [];
  const svelte = [];
  const stopHtml = progress.subscribe(value => html.push(value));
  progress.advance(40);
  step(60);
  progress.subscribe(value => svelte.push(value));
  assert.equal(svelte[0], html.at(-1));
  step(30);
  assert.equal(svelte.at(-1), html.at(-1));
  stopHtml();
  progress.advance(94);
  step(220);
  assert.equal(svelte.at(-1), 94);
  progress.advance(100);
  step(40);
  assert.ok(svelte.at(-1) > 94 && svelte.at(-1) < 100);
  step(40);
  assert.equal(svelte.at(-1), 100);
  assert.ok(svelte.every((value, index) => index === 0 || value >= svelte[index - 1]));
});

test('rapid loading reports move on the next frame and settle without a long final wait', () => {
  const { progress, step } = harness();
  for (const milestone of [8, 40, 48, 62, 94, 100]) progress.advance(milestone);
  step(16);
  assert.ok(progress.current > 10 && progress.current < 100);
  step(104);
  assert.equal(progress.current, 100);
});

test('a new destination cancels the old movement without restarting the waiting period', () => {
  const { progress, step } = harness();
  const samples = [];
  progress.subscribe(value => samples.push(value));
  progress.advance(40);
  step(60);
  assert.equal(progress.current, 35);
  progress.advance(76);
  progress.advance(100);
  step(16);
  assert.ok(progress.current > 40);
  const sampleCount = samples.length;
  step(44);
  assert.equal(progress.current, 100);
  assert.equal(samples.length, sampleCount + 1);
});

test('updating starts a new bar after launch and startup continues in its reserved range', () => {
  const { progress, step } = harness();
  progress.advanceStartup(100);
  step(120);
  assert.equal(progress.current, 100);
  progress.beginUpdate();
  assert.equal(progress.current, 0);
  const samples = [];
  progress.subscribe(value => samples.push(value));
  for (const milestone of [4, 8, 35, 55, 58, 63, 70, 75]) {
    progress.advance(milestone);
    step(120);
  }
  progress.advanceStartup(2);
  step(120);
  assert.equal(progress.current, 75.5);
  progress.advanceStartup(48);
  step(120);
  assert.equal(progress.current, 87);
  progress.advanceStartup(100);
  step(120);
  assert.equal(progress.current, 100);
  assert.ok(samples.every((value, index) => index === 0 || value >= samples[index - 1]));
});

test('a restarted process restores the update bar before its first startup frame', () => {
  const { progress, step } = harness();
  progress.advance(2); // A boot frame may already be pending when IPC resolves.
  progress.resumeUpdate(75);
  const samples = [];
  progress.subscribe(value => samples.push(value));
  assert.equal(samples[0], 75);
  step(16);
  assert.equal(progress.current, 75);
  for (const stage of [8, 27, 24, 40, 48, 62, 97, 100]) {
    progress.advanceStartup(stage);
    step(120);
  }
  assert.equal(progress.current, 100);
  assert.ok(samples.every((value, index) => index === 0 || value >= samples[index - 1]));
});
