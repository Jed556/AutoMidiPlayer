import { readFile, readdir } from 'node:fs/promises';
import { releaseVersion } from './release-lib.mjs';

const version = releaseVersion(process.argv[2]);
const tag = `v${version}`;
const repo = process.env.GITHUB_REPOSITORY;
const token = process.env.GITHUB_TOKEN;
if (!token || !repo || !process.env.RELEASE_SHA) throw new Error('Missing release credentials or source SHA');
const headers = { Authorization: `Bearer ${token}`, Accept: 'application/vnd.github+json', 'X-GitHub-Api-Version': '2022-11-28' };
const endpoint = `https://api.github.com/repos/${repo}`;
async function api(path, options = {}) {
  const response = await fetch(`${endpoint}${path}`, { ...options, headers: { ...headers, ...options.headers } });
  if (!response.ok) throw new Error(`GitHub ${options.method || 'GET'} ${path}: ${response.status} ${await response.text()}`);
  return response.status === 204 ? null : response.json();
}
const existing = await fetch(`${endpoint}/releases/tags/${tag}`, { headers });
let release;
if (existing.ok) {
  release = await existing.json();
  if (!release.draft) {
    console.log(`${tag} is already published; preserving its notes and assets.`);
    await react(release);
    process.exit(0);
  }
} else if (existing.status !== 404) throw new Error(`Cannot look up release: ${existing.status}`);
else release = await api('/releases', { method: 'POST', body: JSON.stringify({
  tag_name: tag, target_commitish: process.env.RELEASE_SHA, name: `Auto MIDI Player ${tag}`,
  body: await readFile('RELEASE.md', 'utf8'), draft: true, prerelease: version.includes('-'),
}) });

const files = await readdir('dist/release-assets');
for (const name of files) {
  const old = release.assets.find(asset => asset.name === name);
  if (old) await api(`/releases/assets/${old.id}`, { method: 'DELETE' });
  const response = await fetch(`${release.upload_url.split('{')[0]}?name=${encodeURIComponent(name)}`, {
    method: 'POST', headers: { ...headers, 'Content-Type': 'application/octet-stream' },
    body: await readFile(`dist/release-assets/${name}`),
  });
  if (!response.ok) throw new Error(`Upload ${name}: ${response.status} ${await response.text()}`);
}
const ready = await api(`/releases/${release.id}`);
if (ready.assets.length !== files.length || ready.assets.some(asset => !files.includes(asset.name) || asset.size === 0)) {
  throw new Error('Draft release assets do not match the validated package set');
}
await api(`/releases/${release.id}`, { method: 'PATCH', body: JSON.stringify({
  draft: false, prerelease: version.includes('-'), make_latest: version.includes('-') ? 'false' : 'legacy',
}) });
await react(release);
console.log(`Published ${release.html_url}`);

async function react(release) {
  // These requests are deliberately sequential. GitHub controls display ordering.
  for (const content of ['heart', 'hooray']) {
    let error;
    for (let attempt = 0; attempt < 3; attempt++) {
      try { await api(`/releases/${release.id}/reactions`, { method: 'POST', body: JSON.stringify({ content }) }); error = null; break; }
      catch (failure) { error = failure; await new Promise(resolve => setTimeout(resolve, 1000 * (attempt + 1))); }
    }
    if (error) console.warn(`::warning::Published successfully, but ${content} reaction failed: ${error.message}`);
  }
}
