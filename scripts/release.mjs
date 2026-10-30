import { readFile, writeFile, appendFile, mkdir } from 'node:fs/promises';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { releaseVersion, nextReleaseVersion, replaceCargoVersion, renderNotes } from './release-lib.mjs';

process.chdir(fileURLToPath(new URL('../', import.meta.url)));
const run = (command, args) => execFileSync(command, args, { encoding: 'utf8', stdio: ['ignore', 'pipe', 'inherit'] }).trim();
const git = (...args) => run('git', args);
const existsRef = ref => { try { return git('rev-parse', '--verify', ref); } catch { return null; } };
const text = path => readFile(path, 'utf8');
const [command, input] = process.argv.slice(2);
if (!['next-version', 'preview', 'prepare', 'validate'].includes(command)) {
  throw new Error('Usage: node scripts/release.mjs <next-version|preview|prepare|validate> [auto|version]');
}
const requested = input?.trim();
const automatic = command !== 'validate' && (!requested || requested === 'auto');
const beta = command !== 'validate' && process.env.BETA_RELEASE === 'true';
const baseVersion = automatic ? await automaticVersion() : releaseVersion(requested || JSON.parse(await text('package.json')).version);
const version = beta ? betaVersion(baseVersion) : baseVersion;
const tag = `v${version}`;
const repo = process.env.GITHUB_REPOSITORY || 'Jed556/AutoMidiPlayer';
if (!/^[\w.-]+\/[\w.-]+$/.test(repo)) throw new Error('Invalid GitHub repository');

function stableTags() {
  return git('tag', '--merged', 'HEAD', '--sort=-version:refname').split('\n')
    .filter(value => /^v(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$/.test(value) && Number(value.slice(1).split('.')[0]) >= 8);
}

function betaVersion(base) {
  // Explicit beta versions remain exact overrides; other prerelease channels
  // require the checkbox to be off so the requested channel is unambiguous.
  if (base.includes('-')) {
    if (!/^\d+\.\d+\.\d+-beta\.(0|[1-9]\d*)$/.test(base)) throw new Error('Uncheck beta when specifying a non-beta prerelease such as an RC.');
    return base;
  }
  const pattern = new RegExp(`^v${base.replaceAll('.', '\\.')}-beta\\.(0|[1-9]\\d*)$`);
  // All tags reserve their version numbers, even on other branches. Sort beta
  // counters numerically so beta.10 follows beta.9.
  const tags = git('tag', '--list', `v${base}-beta.*`).split('\n').filter(value => pattern.test(value))
    .sort((a, b) => BigInt(a.split('.').at(-1)) < BigInt(b.split('.').at(-1)) ? 1 : -1);
  const head = git('rev-parse', 'HEAD');
  const atHead = git('tag', '--points-at', 'HEAD').split('\n');
  const existing = tags.find(value => atHead.includes(value));
  if (existing) return releaseVersion(existing);
  if (tags[0]) {
    // A workflow retry can still be checked out at the source commit just
    // before the bot's release commit. Reuse that release instead of bumping.
    const release = git('log', '-1', '--format=%P%n%s', tags[0]).split('\n');
    if (release[0] === head && release[1] === `chore(release): ${tags[0]}`) return releaseVersion(tags[0]);
  }
  const next = tags[0] ? BigInt(tags[0].split('.').at(-1)) + 1n : 1n;
  return `${base}-beta.${next}`;
}

async function automaticVersion() {
  const previous = stableTags()[0];
  // Bootstrap the rewrite at its configured version, without letting legacy
  // history or the rewrite's initial features force another major bump.
  if (!previous) return releaseVersion(JSON.parse(await text('package.json')).version).split('-')[0];
  // Re-running a release at its tagged commit must reuse the prepared release.
  if (git('rev-parse', `${previous}^{commit}`) === git('rev-parse', 'HEAD')) {
    if (beta) throw new Error(`No unreleased changes after stable release ${previous}; add a release-worthy commit before creating a beta.`);
    return releaseVersion(previous);
  }
  const messages = git('log', '--format=%B%x00', `${previous}..HEAD`).split('\0').map(value => value.trim()).filter(Boolean);
  const next = nextReleaseVersion(previous, messages);
  if (!next) throw new Error(`No release-worthy commits since ${previous}. Use fix:, feat:, perf:, refactor:, ref:, ui:, or a breaking change; alternatively supply an explicit version.`);
  return next;
}

async function validate() {
  for (const path of ['package.json', 'src-tauri/tauri.conf.json']) {
    if (JSON.parse(await text(path)).version !== version) throw new Error(`${path} version does not match ${tag}`);
  }
  for (const path of ['src-tauri/Cargo.toml', 'Cargo.lock']) {
    const source = await text(path);
    if (replaceCargoVersion(source, 'auto-midi-player', version) !== source) throw new Error(`${path} version does not match ${tag}`);
  }
  const releaseNotes = (await text('RELEASE.md')).replaceAll('\r\n', '\n');
  if (!releaseNotes.startsWith(`# Release ${tag}\n`) || !releaseNotes.includes(`<!-- release-prepared: ${tag} -->`)) throw new Error('RELEASE.md was not prepared for this tag');
}

async function notes() {
  const previous = stableTags().find(value => value !== tag);
  const args = ['--config', 'cliff.toml', '--unreleased', '--tag', tag, '--use-branch-tags', '--context'];
  // Prerelease tags never partition this range: every beta/RC and the final
  // release carry all commits since the previous reachable stable tag.
  if (previous) args.push(`${previous}..HEAD`);
  if (process.env.GITHUB_TOKEN) args.push('--github-repo', repo);
  else args.push('--offline');
  const context = JSON.parse(run(process.env.GIT_CLIFF_BIN || 'git-cliff', args));
  const current = context.find(release => release.version === tag) ?? context[0] ?? {};
  return renderNotes(current, version, repo, previous);
}

async function output(sha) {
  if (process.env.GITHUB_OUTPUT) await appendFile(process.env.GITHUB_OUTPUT,
    `version=${version}\ntag=${tag}\nsha=${sha}\nprerelease=${version.includes('-')}\n`);
}

if (command === 'next-version') {
  console.log(version);
} else if (command === 'preview') {
  process.stdout.write(await notes());
} else if (command === 'validate') {
  await validate();
  console.log(`Validated ${tag}`);
} else if (command === 'prepare') {
  console.log(`${automatic ? 'Automatically selected' : 'Preparing'} ${tag}`);
  const buildOnly = process.env.BUILD_ONLY === 'true';
  const branch = process.env.SOURCE_BRANCH || git('branch', '--show-current');
  if (!branch || branch.startsWith('-')) throw new Error('Preparation requires a source branch');
  git('check-ref-format', '--branch', branch);
  if (git('status', '--porcelain')) throw new Error('Release preparation requires a clean checkout');
  const existing = existsRef(`refs/tags/${tag}^{commit}`);
  if (existing) {
    git('merge-base', '--is-ancestor', existing, `refs/remotes/origin/${branch}`);
    git('checkout', '--detach', existing);
    await validate();
    await output(existing);
  } else {
    if (process.env.RELEASE_LICENSE_MANIFEST) {
      await writeFile('THIRD-PARTY-LICENSES.md', await readFile(process.env.RELEASE_LICENSE_MANIFEST));
    } else run(process.execPath, ['scripts/generate-licenses.mjs']);
    const releaseNotes = await notes();
    for (const path of ['package.json', 'src-tauri/tauri.conf.json']) {
      const source = await text(path);
      await writeFile(path, source.replace(/("version"\s*:\s*")[^"]+(")/, (_, prefix, suffix) => `${prefix}${version}${suffix}`));
    }
    for (const path of ['src-tauri/Cargo.toml', 'Cargo.lock']) {
      await writeFile(path, replaceCargoVersion(await text(path), 'auto-midi-player', version));
    }
    await writeFile('RELEASE.md', releaseNotes);
    await validate();
    if (!buildOnly) {
      git('config', 'user.name', 'github-actions[bot]');
      git('config', 'user.email', '41898282+github-actions[bot]@users.noreply.github.com');
      git('add', '--', 'package.json', 'src-tauri/tauri.conf.json', 'src-tauri/Cargo.toml', 'Cargo.lock', 'RELEASE.md', 'THIRD-PARTY-LICENSES.md');
      git('commit', '-m', `chore(release): ${tag}`);
      git('tag', '-a', tag, '-m', `Release ${tag}`);
      git('push', '--atomic', 'origin', `HEAD:refs/heads/${branch}`, `refs/tags/${tag}`);
    }
    await output(git('rev-parse', 'HEAD'));
  }
  // Build-only runs overlay these files onto the immutable source checkout.
  await mkdir('dist/release-input/src-tauri', { recursive: true });
  for (const path of ['package.json', 'src-tauri/tauri.conf.json', 'src-tauri/Cargo.toml', 'Cargo.lock', 'RELEASE.md', 'THIRD-PARTY-LICENSES.md']) {
    await writeFile(`dist/release-input/${path}`, await readFile(path));
  }
}
