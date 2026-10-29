import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { stripTypeScriptTypes } from 'node:module';
import { compileModule } from 'svelte/compiler';
import { enUS } from '../src/renderer/lib/i18n/locales/en-US.ts';
import { zhCN } from '../src/renderer/lib/i18n/locales/zh-CN.ts';

let harnessId = 0;
async function harness(language, preferences = {}) {
  const writes = [];
  const key = `__localeHarness${++harnessId}`;
  globalThis[key] = {
    enUS, zhCN, mockNavigator: { language },
    loadUiPreferences: async () => preferences,
    saveUiPreferences: patch => writes.push(patch),
  };
  const source = stripTypeScriptTypes(readFileSync(new URL('../src/renderer/lib/i18n/i18n.svelte.ts', import.meta.url), 'utf8'))
    .replace(/^import \{([^}]+)\} from '[^']+';/gm, (_, names) => `const {${names}} = globalThis.${key};`)
    .replace(/\bnavigator\b/g, 'mockNavigator');
  const compiled = compileModule(`const { mockNavigator } = globalThis.${key};\n${source}`, { filename: 'i18n.svelte.js', generate: 'client' }).js.code
    .replace("'svelte/internal/client'", JSON.stringify(import.meta.resolve('svelte/internal/client')));
  const result = await import(`data:text/javascript;base64,${Buffer.from(compiled).toString('base64')}`);
  delete globalThis[key];
  return { ...result, writes };
}

test('first launch uses supported system language and falls back to English', async () => {
  assert.equal((await harness('zh-CN')).i18n.currentLocale, 'zh-CN');
  assert.equal((await harness('zh-SG')).i18n.currentLocale, 'zh-CN');
  assert.equal((await harness('en-PH')).i18n.currentLocale, 'en-US');
  assert.equal((await harness('fr-FR')).i18n.currentLocale, 'en-US');
});

test('saved language takes priority and onboarding can preview language before Finish', async () => {
  const h = await harness('zh-CN', { locale: 'en-US' });
  assert.equal(h.i18n.currentLocale, 'en-US');
  h.i18n.setLocale('zh-CN', false);
  assert.equal(h.t('onboarding.next'), '下一步');
  assert.deepEqual(h.writes, []);
  h.i18n.setLocale('en-US');
  assert.equal(h.t('onboarding.next'), 'Next');
  assert.deepEqual(h.writes, [{ locale: 'en-US' }]);
});
