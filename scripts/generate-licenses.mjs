import { readFile, writeFile, mkdir, readdir } from 'node:fs/promises';
import { execFileSync } from 'node:child_process';
import { join } from 'node:path';
import { parseLicenseMarkdown } from '../src/shared/licenses.ts';
import { renderLicenseMarkdown } from './license-manifest.mjs';

const existing = parseLicenseMarkdown(await readFile('THIRD-PARTY-LICENSES.md', 'utf8'));
if (!existing.length) throw new Error('Missing consolidated supplemental notices in THIRD-PARTY-LICENSES.md');
const entries = existing.filter(entry => ['attribution', 'resource'].includes(entry.kind));
const command = process.platform === 'win32' ? 'cmd.exe' : 'pnpm';
const args = process.platform === 'win32' ? ['/d', '/s', '/c', 'pnpm licenses list --json'] : ['licenses', 'list', '--json'];
const groups = JSON.parse(execFileSync(command, args, { encoding: 'utf8', maxBuffer: 32 * 1024 * 1024 }));
const packages = Object.values(groups).flat();
function repository(value, fallback) {
  const url = (typeof value === 'string' ? value : value?.url ?? '').replace(/^git\+/, '').replace(/^git:\/\//, 'https://').replace(/^git@github.com:/, 'https://github.com/').replace(/\.git$/, '');
  return url.startsWith('https://') ? url : fallback;
}
for (const pkg of packages) {
  const texts = [];
  const parentName = pkg.name.startsWith('@esbuild/') ? 'esbuild'
    : pkg.name.startsWith('@rolldown/binding-') ? 'rolldown'
    : pkg.name.startsWith('lightningcss-') ? 'lightningcss'
    : pkg.name.startsWith('@tauri-apps/cli-') ? '@tauri-apps/cli' : null;
  const parent = packages.find(other => other.name === parentName && other.versions.join() === pkg.versions.join());
  for (const directory of [pkg.paths[0], ...(parent ? [parent.paths[0]] : [])]) {
    for (const file of (await readdir(directory)).sort()) {
      if (!/^(licen[cs]e|copying|notice)(\.|$|-)/i.test(file)) continue;
      try { texts.push(await readFile(join(directory, file), 'utf8')); }
      catch (error) { if (error.code !== 'EISDIR') throw error; }
    }
  }
  // Reviewed notices for upstream archives that omit LICENSE stay in the MD.
  const supplemental = existing.find(entry => entry.name === pkg.name && entry.licenseName === pkg.license);
  if (!texts.length && ['@fluentui/svg-icons', 'is-reference', 'locate-character'].includes(pkg.name) && supplemental) texts.push(supplemental.licenseText);
  if (!texts.length) throw new Error(`Missing upstream license text for ${pkg.name}; add its notice to THIRD-PARTY-LICENSES.md.`);
  const metadata = JSON.parse(await readFile(join(pkg.paths[0], 'package.json'), 'utf8'));
  for (const version of pkg.versions) entries.push({
    name: pkg.name, version, licenseName: pkg.license,
    url: repository(metadata.repository, `https://www.npmjs.com/package/${pkg.name}/v/${version}`),
    kind: 'javascript', licenseText: [...new Set(texts.map(text => text.trim()))].join('\n\n---\n\n'),
    ...(supplemental?.notice ? { notice: supplemental.notice } : {}),
  });
}

await mkdir('dist/licenses', { recursive: true });
execFileSync(process.env.CARGO_ABOUT_BIN || 'cargo-about', ['generate', '--locked', '--format', 'json', '--output-file', 'dist/licenses/rust.json'], { stdio: 'inherit' });
const rust = JSON.parse(await readFile('dist/licenses/rust.json', 'utf8'));
const byCrate = new Map();
for (const license of rust.licenses) {
  for (const { crate } of license.used_by) {
    if (!byCrate.has(crate.id)) byCrate.set(crate.id, []);
    byCrate.get(crate.id).push(license);
  }
}
for (const { package: pkg } of rust.crates) {
  if (!pkg.source) continue;
  const licenses = byCrate.get(pkg.id);
  if (!licenses?.length) throw new Error(`Missing resolved notice for Rust crate ${pkg.name} ${pkg.version}`);
  entries.push({ name: pkg.name, version: pkg.version,
    licenseName: [...new Set(licenses.map(license => license.id))].join(' AND '),
    url: repository(pkg.repository, `https://crates.io/crates/${pkg.name}/${pkg.version}`),
    kind: 'rust', licenseText: [...new Set(licenses.map(license => license.text.trim()))].join('\n\n---\n\n'),
  });
}
const markdown = renderLicenseMarkdown(entries);
if (parseLicenseMarkdown(markdown).length !== entries.length) throw new Error('Generated license index did not round-trip');
await writeFile('THIRD-PARTY-LICENSES.md', markdown);
console.log(`Updated THIRD-PARTY-LICENSES.md: ${entries.length} entries from locked dependencies and bundled resources.`);
