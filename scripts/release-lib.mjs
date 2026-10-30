import { createHash, createPublicKey, verify } from 'node:crypto';

export const documents = ['RELEASE.md', 'LICENSE', 'README.md', 'THIRD-PARTY-LICENSES.md', 'TRADEMARK.md'];
export const platforms = ['windows-x86_64', 'linux-x86_64', 'darwin-aarch64', 'darwin-x86_64'];

export function releaseVersion(input) {
  const version = String(input ?? '').replace(/^v/, '');
  if (!/^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?$/.test(version)
    || Number(version.split('.')[0]) < 8
    || version.split('-').slice(1).join('-').split('.').some(id => /^0\d+$/.test(id))) {
    throw new Error('Use a rewrite version such as 8.1.0 or 8.1.0-beta.1 (major >= 8, no leading zeros).');
  }
  return version;
}

export function nextReleaseVersion(base, messages) {
  const parts = releaseVersion(base).split('-')[0].split('.').map(value => BigInt(value));
  let bump = 0;
  for (const message of messages) {
    const header = /^([a-z][a-z0-9-]*)(?:\([^\r\n()]+\))?(!)?:[ \t]+\S[^\r\n]*/i.exec(message);
    if (!header) continue;
    const type = header[1].toLowerCase();
    if (header[2] || /^BREAKING[ -]CHANGE:[ \t]+\S/m.test(message)) bump = 3;
    else if (type === 'feat') bump = Math.max(bump, 2);
    else if (['fix', 'perf', 'ref', 'refactor', 'ui'].includes(type)) bump = Math.max(bump, 1);
  }
  if (!bump) return null;
  const index = 3 - bump;
  parts[index] += 1n;
  for (let i = index + 1; i < parts.length; i++) parts[i] = 0n;
  return parts.join('.');
}

export function replaceCargoVersion(source, name, version) {
  let found = false;
  const result = source.replace(/(^\[\[?package\]?\][\s\S]*?)(?=^\[|$(?![\s\S]))/gm, block => {
    if (!new RegExp(`^name = "${name}"\\r?$`, 'm').test(block)) return block;
    found = true;
    return block.replace(/^version = "[^"]+"/m, `version = "${version}"`);
  });
  if (!found) throw new Error(`Missing Cargo package ${name}`);
  return result;
}

const escape = value => String(value).replace(/[\\`*\[\]<>]/g, '\\$&').replace(/[\r\n]+/g, ' ');

export function renderNotes(release, version, repo, previous) {
  const tag = `v${releaseVersion(version)}`;
  const base = `https://github.com/${repo}`;
  const lines = [`# Release ${tag}`, ''];
  lines.push(`<!-- release-prepared: ${tag} -->`, '');
  for (const group of ["What's New?", 'Changes', 'Bug Fixes']) {
    const commits = (release.commits ?? []).filter(commit => commit.group === group);
    if (!commits.length) continue;
    lines.push(`## ${group}`, '');
    for (const commit of commits) {
      const remote = commit.remote ?? commit.github ?? {};
      const link = remote.pr_number
        ? `[#${remote.pr_number}](${base}/pull/${remote.pr_number})`
        : `[${commit.id.slice(0, 7)}](${base}/commit/${commit.id})`;
      lines.push(`- ${commit.breaking ? '**Breaking:** ' : ''}${commit.scope ? `**${escape(commit.scope)}:** ` : ''}${escape(commit.message)} (${link})`);
    }
    lines.push('');
  }
  const contributors = new Map();
  for (const person of (release.github?.contributors ?? [])) {
    if (person.is_first_time && person.username && !/\[bot\]$/.test(person.username)) contributors.set(person.username, person);
  }
  if (contributors.size) {
    lines.push('## New Contributors', '');
    for (const [name, person] of contributors) {
      lines.push(`- @${name} made their first contribution${person.pr_number ? ` in [#${person.pr_number}](${base}/pull/${person.pr_number})` : ''}.`);
    }
    lines.push('');
  }
  const url = previous ? `${base}/compare/${previous}...${tag}` : `${base}/commits/${tag}`;
  lines.push(`**Full Changelog**: [${previous ? `${previous}...${tag}` : tag}](${url})`, '');
  return lines.join('\n');
}

export function mergeManifests(manifests, version, repo, assets) {
  const merged = { version, notes: '', pub_date: new Date().toISOString(), platforms: {} };
  for (const manifest of manifests) {
    if (manifest.version !== version) throw new Error('Updater manifest version mismatch');
    for (const [platform, entry] of Object.entries(manifest.platforms ?? {})) {
      if (merged.platforms[platform]) throw new Error(`Duplicate updater platform: ${platform}`);
      const url = new URL(entry.url);
      if (!entry.signature?.trim() || url.origin !== 'https://github.com'
        || !url.pathname.startsWith(`/${repo}/releases/download/v${version}/`)
        || !assets.has(decodeURIComponent(url.pathname.split('/').at(-1)))) throw new Error(`Invalid updater asset: ${platform}`);
      merged.platforms[platform] = entry;
    }
  }
  for (const platform of platforms) {
    if (!merged.platforms[platform]) throw new Error(`Missing updater platform: ${platform}`);
  }
  return merged;
}

export const sha256 = bytes => createHash('sha256').update(bytes).digest('hex');

// Tauri stores minisign key/signature text files as base64 strings. Check both
// the file signature and its trusted comment before publishing updater URLs.
export function verifyUpdaterSignature(bytes, encodedSignature, encodedPublicKey) {
  const keyLines = Buffer.from(encodedPublicKey.trim(), 'base64').toString('utf8').trim().split(/\r?\n/);
  const sigLines = Buffer.from(encodedSignature.trim(), 'base64').toString('utf8').trim().split(/\r?\n/);
  const key = Buffer.from(keyLines[1] ?? '', 'base64');
  const packet = Buffer.from(sigLines[1] ?? '', 'base64');
  if (key.length !== 42 || packet.length !== 74 || !key.subarray(2, 10).equals(packet.subarray(2, 10))
    || !sigLines[2]?.startsWith('trusted comment: ')) throw new Error('Invalid minisign key or signature');
  const algorithm = packet.subarray(0, 2).toString();
  if (!['Ed', 'ED'].includes(algorithm)) throw new Error('Unsupported minisign algorithm');
  const publicKey = createPublicKey({ key: Buffer.concat([
    Buffer.from('302a300506032b6570032100', 'hex'), key.subarray(10),
  ]), format: 'der', type: 'spki' });
  const signature = packet.subarray(10);
  const payload = algorithm === 'ED' ? createHash('blake2b512').update(bytes).digest() : bytes;
  if (!verify(null, payload, publicKey, signature)
    || !verify(null, Buffer.concat([signature, Buffer.from(sigLines[2].slice('trusted comment: '.length))]), publicKey, Buffer.from(sigLines[3] ?? '', 'base64'))) {
    throw new Error('Updater signature does not match the artifact and configured public key');
  }
}
