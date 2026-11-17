import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { stripTypeScriptTypes } from 'node:module';
import { compileModule } from 'svelte/compiler';
import { changedConfigFields, configReviewHeading, formatConfigJson } from '../src/renderer/lib/utils/configUpdateReview.ts';
import { prepareConfigurationStartup } from '../src/renderer/lib/backend/configurationStartup.ts';
import { fadeConfigurationSurface } from '../src/renderer/lib/utils/configurationReviewFade.ts';

const pending = () => ({ addedFiles: ['KeyboardLayouts/new.json'], backupDirectory: null, duplicates: [], issues: [], conflicts: [
  { id: 'KeyboardLayouts/keys.json', path: 'KeyboardLayouts/keys.json', kind: 'layout', name: 'QWERTY',
    currentJson: '{"keys":["a"]}', bundledJson: '{"keys":["q"]}', token: 'reviewed-layout' },
  { id: 'Games/Test/Instruments/Ukulele.json', path: 'Games/Test/Instruments/Ukulele.json', kind: 'instrument', name: 'Ukulele',
    currentJson: '{"keyboardLayouts":[]}', bundledJson: '{"chordLayouts":["keys"]}', token: 'reviewed-instrument' },
] });
let harnessId = 0;
async function harness({ read = async () => pending(), resolve } = {}) {
  const choices = [];
  const duplicateChoices = [];
  const api = { games: {
    getDefaultConfigUpdates: read,
    resolveDefaultConfigUpdates: async (batch, duplicates) => { choices.push(batch); duplicateChoices.push(duplicates); return resolve ? resolve(batch, duplicates) : { ...pending(), conflicts: [] }; },
  } };
  const key = `__defaultConfigHarness${++harnessId}`;
  globalThis[key] = { api };
  const source = stripTypeScriptTypes(readFileSync(new URL('../src/renderer/lib/state/defaultConfigStore.svelte.ts', import.meta.url), 'utf8'), { mode: 'transform' })
    .replace("import { api } from '../backend/index.js';", `const { api } = globalThis.${key};`);
  const compiled = compileModule(source, { filename: 'defaultConfigStore.svelte.js', generate: 'client' }).js.code
    .replace("'svelte/internal/client'", JSON.stringify(import.meta.resolve('svelte/internal/client')));
  const { defaultConfigStore: store } = await import(`data:text/javascript;base64,${Buffer.from(compiled).toString('base64')}`);
  delete globalThis[key];
  return { store, choices, duplicateChoices };
}

test('startup loads comparisons without sending overwrite choices', async () => {
  const { store, choices } = await harness();
  await store.load();
  assert.equal(store.report.conflicts.length, 2);
  assert.equal(store.error, '');
  assert.equal(store.loading, false);
  assert.deepEqual(choices, []);
});

test('applying choices only replaces explicitly selected files and binds consent to preview tokens', async () => {
  const { store, choices } = await harness();
  await store.load();
  assert.equal(await store.apply(new Set(['KeyboardLayouts/keys.json'])), true);
  assert.deepEqual(choices[0], [
    { id: 'KeyboardLayouts/keys.json', token: 'reviewed-layout', action: 'replace' },
    { id: 'Games/Test/Instruments/Ukulele.json', token: 'reviewed-instrument', action: 'keep' },
  ]);
  assert.equal(store.saving, false);
  assert.equal(store.report.conflicts.length, 0);
});

test('a failed application refreshes the preview and never retries replacements automatically', async () => {
  let reads = 0;
  const { store, choices } = await harness({
    read: async () => {
      const report = pending();
      if (++reads > 1) report.conflicts[0].token = 'changed-on-disk';
      return report;
    },
    resolve: async () => { throw new Error('File changed during review'); },
  });
  await store.load();
  assert.equal(await store.apply(new Set(['KeyboardLayouts/keys.json'])), false);
  assert.match(store.error, /File changed/);
  assert.equal(store.report.conflicts[0].token, 'changed-on-disk');
  assert.equal(choices.length, 1);
  assert.equal(store.saving, false);
});

test('unreadable updates surface an error and concurrent applications cannot duplicate a replacement', async () => {
  const failed = await harness({ read: async () => { throw new Error('Cannot read configuration'); } });
  await failed.store.load();
  assert.match(failed.store.error, /Cannot read/);
  assert.equal(await failed.store.apply(new Set()), false);
  let finish;
  const active = await harness({ resolve: () => new Promise(resolve => { finish = resolve; }) });
  await active.store.load();
  const saving = active.store.apply(new Set(['KeyboardLayouts/keys.json']));
  assert.equal(await active.store.apply(new Set()), false);
  assert.equal(active.choices.length, 1);
  finish({ ...pending(), conflicts: [] });
  assert.equal(await saving, true);
});

test('pending configuration review holds the runtime until the user continues', async () => {
  const calls = [];
  let finish;
  const reviewing = prepareConfigurationStartup({
    initialStatus: { configurationReviewRequired: null },
    readStatus: async () => ({ configurationReviewRequired: true }),
    onStatus: () => {}, wait: async () => {},
    review: () => { calls.push('review'); return new Promise(resolve => { finish = resolve; }); },
    getSettings: async () => { calls.push('settings'); return { onboardingCompleted: true }; },
    startRuntime: async () => { calls.push('runtime'); },
  });
  await new Promise(resolve => setImmediate(resolve));
  assert.deepEqual(calls, ['review']);
  finish();
  await reviewing;
  assert.deepEqual(calls, ['review', 'settings', 'runtime']);
});

test('unchanged defaults preserve normal startup and fresh installs still wait for onboarding', async () => {
  let starts = 0;
  const options = {
    initialStatus: { configurationReviewRequired: false },
    readStatus: async () => assert.fail('Preflight already completed'), onStatus: () => {},
    review: async () => assert.fail('No review needed'),
    getSettings: async () => ({ onboardingCompleted: false }),
    startRuntime: async () => { starts++; },
  };
  assert.equal((await prepareConfigurationStartup(options)).configurationReviewRequired, false);
  await prepareConfigurationStartup({ ...options, initialStatus: { configurationReviewRequired: true }, review: async () => {} });
  assert.equal(starts, 0);
});

test('duplicate deletion choices are explicit and include their reviewed tokens', async () => {
  const { store, duplicateChoices } = await harness({ read: async () => ({ ...pending(), duplicates: [
    { id: 'duplicate/layout/keys', token: 'duplicate-review', configId: 'keys', kind: 'layout',
      keep: { path: 'KeyboardLayouts/new.json', json: '{}', modifiedMs: 2000 },
      olderFiles: [{ path: 'KeyboardLayouts/old.json', json: '{}', modifiedMs: 1000 }] },
  ] }) });
  await store.load();
  await store.apply(new Set(['duplicate/layout/keys']));
  assert.deepEqual(duplicateChoices[0], [{ id: 'duplicate/layout/keys', token: 'duplicate-review', action: 'replace' }]);
});

test('comparison names actual changed fields and retains invalid JSON for review', () => {
  assert.deepEqual(changedConfigFields('{"keys":["a"],"custom":{"b":2,"a":1}}',
    '{"chordKeys":["q"],"custom":{"a":1,"b":2}}'), ['chordKeys', 'keys']);
  assert.equal(formatConfigJson('{invalid'), '{invalid');
  assert.deepEqual(changedConfigFields('{invalid', '{}'), []);
  assert.equal(formatConfigJson('{"keys":[]}'), '{\n  "keys": []\n}');
});

test('review heading shows matching name, falls back to shared id when name differs, and uses file name when both change', () => {
  assert.equal(configReviewHeading({
    path: 'KeyboardLayouts/Genshin_DVORAKLeft.json',
    currentJson: '{"id":"Genshin_DVORAKLeft","name":"DVORAKLeft","keys":["7"]}',
    bundledJson: '{"id":"Genshin_DVORAKLeft","name":"DVORAKLeft","keys":["6"]}',
  }), 'DVORAKLeft');
  assert.equal(configReviewHeading({
    path: 'Games/Genshin Impact/game.json',
    currentJson: '{"id":"genshin","displayName":"Genshin Old"}',
    bundledJson: '{"id":"genshin","displayName":"Genshin New"}',
  }), 'genshin');
  assert.equal(configReviewHeading({
    path: 'KeyboardLayouts/Genshin_DVORAKLeft.json',
    currentJson: '{"id":"old_id","name":"Old Name"}',
    bundledJson: '{"id":"new_id","name":"New Name"}',
  }), 'Genshin_DVORAKLeft.json');
  assert.equal(configReviewHeading({
    path: 'KeyboardLayouts/Broken.json',
    currentJson: '{invalid',
    bundledJson: '{"id":"broken","name":"Broken"}',
  }), 'Broken.json');
});

test('surface fades keep the loader mounted until completion and block interactions during handoff', async () => {
  const completions = [];
  function surface() {
    const animations = [];
    return { inert: false, animations, style: { display: '', opacity: '', removeProperty(key) { delete this[key]; } },
      animate(frames, options) {
        const animation = { frames, options, cancelled: false,
          finished: new Promise(resolve => completions.push(resolve)), cancel() { this.cancelled = true; } };
        animations.push(animation);
        return animation;
      },
    };
  }
  const loader = surface();
  const review = surface();
  const entering = fadeConfigurationSurface(loader, review, 250);
  assert.equal(review.inert, true);
  assert.notEqual(loader.style.display, 'none');
  assert.deepEqual(review.animations[0].frames, [{ opacity: 0 }, { opacity: 1 }]);
  completions.splice(0).forEach(resolve => resolve());
  await entering;
  assert.equal(loader.style.display, 'none');
  assert.equal(review.inert, false);
  const leaving = fadeConfigurationSurface(review, loader, 250);
  assert.equal(review.inert, true);
  assert.notEqual(loader.style.display, 'none');
  completions.splice(0).forEach(resolve => resolve());
  await leaving;
  assert.equal(loader.style.opacity, '1');
  assert.equal(loader.inert, false);
  assert.equal(review.style.display, 'none');
  assert.ok([...loader.animations, ...review.animations].every(animation => animation.cancelled));
});

test('disabled motion switches surfaces immediately without animation work', async () => {
  const surface = () => ({ inert: false, style: { display: 'none', opacity: '', removeProperty(key) { delete this[key]; } },
    animate() { assert.fail('Animations must respect zero duration'); } });
  const loader = surface();
  const review = surface();
  await fadeConfigurationSurface(loader, review, 0);
  assert.equal(loader.style.display, 'none');
  assert.equal(review.style.opacity, '1');
  assert.equal(review.inert, false);
});
