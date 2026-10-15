import { readFile, writeFile, mkdir, stat } from 'node:fs/promises';
import { basename, dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

// Each release carries its own manifest so prerelease selection and installation
// always refer to the same release, rather than GitHub's stable /latest endpoint.
const [tag, platform, artifact, destination = 'dist/updater/latest.json'] = process.argv.slice(2);
if (!tag || !platform || !artifact) {
  throw new Error('Usage: node scripts/create-update-manifest.mjs <tag> <platform> <signed-artifact> [output]');
}
if (!/^v?\d+\.\d+\.\d+(?:-[\da-z.-]+)?(?:\+[\da-z.-]+)?$/i.test(tag)) {
  throw new Error('The release tag must be a semantic version, optionally prefixed with v.');
}
if (!/^(windows|darwin|linux)-(x86_64|aarch64|i686|armv7)(-(nsis|msi|app|appimage|deb|rpm))?$/.test(platform)) {
  throw new Error('Invalid Tauri updater platform. For Windows NSIS use windows-x86_64-nsis.');
}
const root = fileURLToPath(new URL('../', import.meta.url));
const { version } = JSON.parse(await readFile(resolve(root, 'package.json'), 'utf8'));
if (version !== tag.replace(/^v/i, '')) throw new Error('The release tag must match package.json before building.');
const artifactPath = resolve(artifact);
const signature = (await readFile(`${artifactPath}.sig`, 'utf8')).trim();
if (!signature) throw new Error('The updater artifact signature is empty.');
const artifactInfo = await stat(artifactPath);
if (!artifactInfo.isFile() || artifactInfo.size === 0) throw new Error('The updater artifact is missing or empty.');
const output = resolve(destination);
let platforms = {};
try {
  const existing = JSON.parse(await readFile(output, 'utf8'));
  if (existing.version === version) platforms = existing.platforms ?? {};
} catch (error) {
  if (error.code !== 'ENOENT') throw error;
}
platforms[platform] = {
  signature,
  url: `https://github.com/Jed556/AutoMidiPlayer/releases/download/${encodeURIComponent(tag)}/${encodeURIComponent(basename(artifactPath))}`,
};
await mkdir(dirname(output), { recursive: true });
await writeFile(output, `${JSON.stringify({ version, pub_date: new Date().toISOString(), platforms }, null, 2)}\n`);
console.log(`Updater manifest written to ${output}`);
