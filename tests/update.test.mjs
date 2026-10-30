import * as sharedDefaults from '../src/shared/defaults/settings.ts';
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { stripTypeScriptTypes } from 'node:module';
import { compileModule } from 'svelte/compiler';
import * as versionUtils from '../src/renderer/lib/utils/versionUtils.ts';

const source = stripTypeScriptTypes(readFileSync(new URL('../src/renderer/lib/state/updateStore.svelte.ts', import.meta.url), 'utf8'));
let harnessId = 0;
const release = { id: 1, tagName: 'v8.1.0', version: '8.1.0', assets: [], body: '' };
const until = async predicate => {
  for (let attempt = 0; attempt < 100; ++attempt) {
    if (predicate()) return;
    await new Promise(resolve => setImmediate(resolve));
  }
  assert.fail('The update did not reach the expected phase');
};

async function harness({ downloaded = false, download, install, prepare } = {}) {
  const calls = [];
  const notifications = [];
  const api = {
    settings: { getAppVersion: async () => '8.0.0' },
    updates: {
      prepare: async tag => { calls.push(['prepare', tag]); return prepare ? prepare(tag) : { version: '8.1.0', downloaded }; },
      download: async progress => {
        calls.push(['download']);
        progress({ phase: 'downloading', progress: 20 });
        if (download) await download(progress);
        progress({ phase: 'verifying', progress: 58 });
      },
      install: async progress => {
        calls.push(['install']);
        if (install) await install(progress);
      },
    },
  };
  const mocks = {
    '../../../shared/defaults/settings.js': sharedDefaults,
    '../utils/versionUtils': versionUtils,
    '../ui/notificationCenter.svelte': { notificationCenter: new Proxy({}, { get: (_, kind) => (...args) => notifications.push([kind, ...args]) }) },
    '../backend/index.js': { api, isTauri: false },
    '../i18n': { t: key => key },
    '../backend/startupProgress': { startupProgress: { beginUpdate: () => calls.push(['beginUpdate']) } },
  };
  const key = `__updateHarness${++harnessId}`;
  globalThis[key] = mocks;
  const compiled = compileModule(source.replace(/^import \{([^}]+)\} from '([^']+)';/gm,
    (_, names, path) => `const {${names}} = globalThis.${key}[${JSON.stringify(path)}];`),
  { filename: 'updateStore.svelte.js', generate: 'client' }).js.code
    .replace("'svelte/internal/client'", JSON.stringify(import.meta.resolve('svelte/internal/client')));
  const { updateStore: store } = await import(`data:text/javascript;base64,${Buffer.from(compiled).toString('base64')}`);
  delete globalThis[key];
  store.latestRelease = release;
  return { store, calls, notifications };
}

test('installation waits for the splash fade and saves state before downloading', async () => {
  const { store, calls } = await harness();
  const applying = store.downloadUpdate(async () => { calls.push(['save']); });
  await until(() => store.isApplyingUpdate);
  assert.deepEqual(calls.map(([name]) => name), ['beginUpdate']);
  store.onSplashEntered();
  await applying;
  assert.deepEqual(calls.map(([name]) => name), ['beginUpdate', 'prepare', 'save', 'download', 'install']);
  assert.equal(store.updateDownloaded, true);
});

test('migrated weekly and on-launch update preferences retain their schedules', async (t) => {
  const { store } = await harness();
  const intervals = [];
  let checks = 0;
  store.checkForUpdates = async () => { checks++; };
  t.mock.method(globalThis, 'setInterval', (_callback, ms) => { intervals.push(ms); return 123; });
  t.mock.method(globalThis, 'clearInterval', () => {});
  store.configureTimer({ autoCheckUpdates: true, updateCheckFrequency: 'weekly' }, false);
  assert.deepEqual(intervals, [7 * 24 * 60 * 60 * 1000]);
  store.configureTimer({ autoCheckUpdates: true, updateCheckFrequency: 'launch' }, false);
  assert.equal(intervals.length, 1);
  assert.equal(checks, 0, 'Startup performs its own awaited initial check');
  store.configureTimer({ autoCheckUpdates: true, updateCheckFrequency: 'launch' });
  assert.equal(checks, 1);
  assert.equal(intervals.length, 1, 'On Launch must not create a recurring timer');
});

test('a verified package downloaded in the background skips downloading at installation', async () => {
  const { store, calls } = await harness();
  await store.downloadNativeUpdate(release.tagName);
  const applying = store.downloadUpdate(async () => {});
  await until(() => store.isApplyingUpdate);
  store.onSplashEntered();
  await applying;
  assert.equal(calls.filter(([name]) => name === 'download').length, 1);
  assert.equal(calls.filter(([name]) => name === 'prepare').length, 1);
  assert.equal(calls.at(-1)[0], 'install');
});

test('an in-flight background download is shared by installation and duplicate clicks', async () => {
  let releaseDownload;
  const gate = new Promise(resolve => { releaseDownload = resolve; });
  const { store, calls } = await harness({ download: () => gate });
  const background = store.downloadNativeUpdate(release.tagName);
  await until(() => store.isDownloadingUpdate);
  const applying = store.downloadUpdate(async () => {});
  await until(() => store.isApplyingUpdate);
  await store.downloadUpdate(async () => assert.fail('A duplicate update must be ignored'));
  store.onSplashEntered();
  await new Promise(resolve => setImmediate(resolve));
  assert.equal(calls.filter(([name]) => name === 'install').length, 0);
  releaseDownload();
  await Promise.all([background, applying]);
  assert.equal(calls.filter(([name]) => name === 'download').length, 1);
  assert.equal(calls.filter(([name]) => name === 'install').length, 1);
});

test('download or signature failures restore the app and do not start installation', async () => {
  const { store, calls, notifications } = await harness({ download: async () => { throw new Error('Invalid signature'); } });
  const applying = store.downloadUpdate(async () => {});
  await until(() => store.isApplyingUpdate);
  store.onSplashEntered();
  await applying;
  assert.equal(store.isApplyingUpdate, false);
  assert.equal(store.isPreparingUpdate, false);
  assert.equal(store.isDownloadingUpdate, false);
  assert.equal(calls.some(([name]) => name === 'install'), false);
  assert.match(store.errorMessage, /Invalid signature/);
  assert.equal(notifications.at(-1)[0], 'error');
});

test('a newer release waits for the previous download and shares its preparation', async () => {
  let releaseDownload;
  const gate = new Promise(resolve => { releaseDownload = resolve; });
  const { store, calls } = await harness({
    prepare: async tag => ({ version: tag.slice(1), downloaded: false }),
    download: () => gate,
  });
  const older = store.downloadNativeUpdate(release.tagName);
  await until(() => store.isDownloadingUpdate);
  const newerTag = 'v8.2.0';
  const first = store.downloadNativeUpdate(newerTag);
  const second = store.downloadNativeUpdate(newerTag);
  releaseDownload();
  await Promise.all([older, first, second]);
  assert.deepEqual(calls.filter(([name]) => name === 'prepare').map(([, tag]) => tag), [release.tagName, newerTag]);
  assert.equal(calls.filter(([name]) => name === 'download').length, 2);
});

test('a failed installer can retry its verified package without downloading again', async () => {
  const { store, calls } = await harness({ install: async () => { throw new Error('Installer could not start'); } });
  for (let attempt = 0; attempt < 2; ++attempt) {
    const applying = store.downloadUpdate(async () => {});
    await until(() => store.isApplyingUpdate);
    store.onSplashEntered();
    await applying;
    assert.equal(store.isApplyingUpdate, false);
  }
  assert.equal(calls.filter(([name]) => name === 'download').length, 1);
  assert.equal(calls.filter(([name]) => name === 'install').length, 2);
});

test('an unsupported release restores the app without stopping playback', async () => {
  const { store, calls } = await harness({ prepare: async () => { throw new Error('No signed manifest'); } });
  const applying = store.downloadUpdate(async () => assert.fail('Playback must continue'));
  store.onSplashEntered();
  await applying;
  assert.equal(store.isApplyingUpdate, false);
  assert.deepEqual(calls.map(([name]) => name), ['beginUpdate', 'prepare']);
});

test('a failed pre-install save prevents both downloading and installing', async () => {
  const { store, calls } = await harness();
  const applying = store.downloadUpdate(async () => { throw new Error('Settings could not be saved'); });
  await until(() => store.isApplyingUpdate);
  store.onSplashEntered();
  await applying;
  assert.equal(store.isApplyingUpdate, false);
  assert.equal(calls.some(([name]) => name === 'download' || name === 'install'), false);
});
