import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { parseLicenseMarkdown } from '../src/shared/licenses.ts';
import { renderLicenseMarkdown } from '../scripts/license-manifest.mjs';

const markdown = readFileSync(new URL('../THIRD-PARTY-LICENSES.md', import.meta.url), 'utf8');
const entries = parseLicenseMarkdown(markdown);

test('the consolidated notices cover the current app stack and omit obsolete WPF libraries', () => {
  assert.ok(entries.length > 100);
  for (const name of ['svelte', '@tauri-apps/api', '@fluentui/svg-icons', 'tauri', 'midly', 'enigo', 'rustysynth', 'rodio', 'rusqlite', 'sentry', 'TimGM6mb soundfont', 'GenshinLyreMidiPlayer']) {
    assert.ok(entries.some(entry => entry.name === name && entry.licenseText.length > 200 && entry.url.startsWith('https://')), name);
  }
  for (const name of ['DryWetMIDI', '.NET Foundation', 'Fody', 'Stylet', 'WPF UI', 'JetBrains.Annotations']) assert.ok(!entries.some(entry => entry.name === name), name);
  for (const name of ['is-reference', 'locate-character']) {
    const entry = entries.find(entry => entry.name === name);
    assert.match(entry.licenseText, /Rich Harris/);
    assert.doesNotMatch(entry.licenseText, /<year> <copyright holders>/);
  }
  assert.doesNotMatch(markdown, /C:\\Users\\|\/home\/runner\//);
});

test('shared notices preserve complete texts and distinct versions through serialization', () => {
  const text = 'MIT notice\nCopyright Example\n```\nembedded backticks\n```';
  const input = ['1.0.0', '2.0.0'].map(version => ({ name: 'example', version, licenseName: 'MIT', url: 'https://github.com/example/library', kind: 'javascript', licenseText: text }));
  const rendered = renderLicenseMarkdown(input);
  assert.equal([...rendered.matchAll(/amp-license-text:/g)].length, 1);
  assert.deepEqual(parseLicenseMarkdown(rendered), input);
  assert.deepEqual(parseLicenseMarkdown(rendered.replaceAll('\n', '\r\n')), input);
  assert.deepEqual(parseLicenseMarkdown(renderLicenseMarkdown(entries)), entries);
});

test('missing notices, unsafe links and duplicate entries are rejected', () => {
  const sample = { name: 'example', version: '1.0.0', licenseName: 'MIT', url: 'https://github.com/example/library', kind: 'javascript', licenseText: 'Full license text' };
  const rendered = renderLicenseMarkdown([sample]);
  assert.throws(() => parseLicenseMarkdown(rendered.replace(/amp-license-text:/, 'missing-text:')), /Invalid license/);
  assert.throws(() => parseLicenseMarkdown(rendered.replaceAll('https://github.com/example/library', 'javascript:alert(1)')), /Invalid license/);
  assert.throws(() => parseLicenseMarkdown(renderLicenseMarkdown([sample, sample])), /Duplicate/);
});

test('About and its dialog share the Markdown-derived library list and current app version', () => {
  const source = readFileSync(new URL('../src/renderer/lib/data/licenseData.ts', import.meta.url), 'utf8');
  assert.match(source, /THIRD-PARTY-LICENSES\.md\?raw/);
  assert.match(source, /parseLicenseMarkdown\(licenseMarkdown\)/);
  assert.match(source, /import \{ version \} from .*package\.json/);
  assert.doesNotMatch(source, /DryWetMIDI|WPF UI|version: '7\./);
  const about = readFileSync(new URL('../src/renderer/lib/data/aboutData.ts', import.meta.url), 'utf8');
  assert.match(about, /await import\('\.\/licenseData'\)/);
});
