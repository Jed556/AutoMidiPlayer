import { readFile, writeFile, appendFile, mkdir } from 'node:fs/promises';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { releaseVersion, compareReleaseVersions, nextReleaseVersion, replaceCargoVersion, renderNotes } from './release-lib.mjs';

process.chdir(fileURLToPath(new URL('../', import.meta.url)));
const run = (command, args) => execFileSync(command, args, { encoding: 'utf8', stdio: ['ignore', 'pipe', 'inherit'] }).trim();
const git = (...args) => run('git', args);
const existsRef = ref => { try { return git('rev-parse', '--verify', ref); } catch { return null; } };
const isAncestor = (ancestor, descendant) => {
  try { git('merge-base', '--is-ancestor', ancestor, descendant); return true; } catch { return false; }
};
const text = path => readFile(path, 'utf8');
const [command, input] = process.argv.slice(2);
if (!['next-version', 'preview', 'prepare', 'validate'].includes(command)) {
  throw new Error('Usage: node scripts/release.mjs <next-version|preview|prepare|validate> [auto|version]');
}
const requested = input?.trim();
const isCurrent = requested === 'current';
const automatic = command !== 'validate' && !isCurrent && (!requested || requested === 'auto');
const beta = command !== 'validate' && process.env.BETA_RELEASE === 'true';
const baseVersion = isCurrent
  ? releaseVersion(JSON.parse(await text('package.json')).version)
  : (automatic ? await automaticVersion() : releaseVersion(requested || JSON.parse(await text('package.json')).version));
const version = beta ? betaVersion(baseVersion) : baseVersion;
const tag = `v${version}`;
const repo = process.env.GITHUB_REPOSITORY || 'Jed556/AutoMidiPlayer';
if (!/^[\w.-]+\/[\w.-]+$/.test(repo)) throw new Error('Invalid GitHub repository');

function releaseAnchor(tagName, mergedTags) {
  const commit = existsRef(`refs/tags/${tagName}^{commit}`);
  if (!commit) return null;
  if (mergedTags.has(tagName)) return commit;
  // Detached beta tags point to a chore(release) commit off the source branch.
  const info = git('log', '-1', '--format=%P%n%s', commit).split('\n');
  if (info[0] && !/\s/.test(info[0]) && info[1] === `chore(release): ${tagName}` && isAncestor(info[0], 'HEAD')) {
    return info[0];
  }
  return null;
}

function reachableReleaseTags({ stableOnly = false } = {}) {
  const tags = git('tag', '--list', 'v[0-9]*').split('\n').filter(Boolean);
  const mergedTags = new Set(git('tag', '--merged', 'HEAD', '--list', 'v[0-9]*').split('\n').filter(Boolean));
  const pattern = stableOnly ? /^v(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$/ : null;
  const matched = [];
  for (const name of tags) {
    if (pattern && !pattern.test(name)) continue;
    let parsed;
    try { parsed = releaseVersion(name); } catch { continue; }
    if (Number(parsed.split('.')[0]) < 8) continue;
    const anchor = releaseAnchor(name, mergedTags);
    if (anchor) matched.push({ tag: name, anchor });
  }
  matched.sort((a, b) => compareReleaseVersions(b.tag, a.tag));
  return matched;
}

function stableTags() {
  return reachableReleaseTags({ stableOnly: true }).map(entry => entry.tag);
}

function betaVersion(base) {
  // Explicit -beta versions remain exact overrides; other prerelease channels
  // require the checkbox to be off so the requested channel is unambiguous.
  if (base.includes('-')) {
    if (!/^\d+\.\d+\.\d+-beta$/.test(base)) throw new Error('Uncheck beta when specifying a custom prerelease.');
    return base;
  }
  const target = `${base}-beta`;
  const tagName = `v${target}`;
  const tagged = existsRef(`refs/tags/${tagName}^{commit}`);
  if (!tagged) return target;
  const head = git('rev-parse', 'HEAD');
  if (tagged === head) return target;
  // A workflow retry can still be checked out at the source commit just
  // before the bot's release commit. Reuse that release instead of failing.
  const release = git('log', '-1', '--format=%P%n%s', tagged).split('\n');
  if (release[0] === head && release[1] === `chore(release): ${tagName}`) return target;
  throw new Error(`Beta tag ${tagName} already exists on another commit; push a version-bumping commit or specify an explicit version.`);
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
  if (!releaseNotes.startsWith(`<!-- release-prepared: ${tag} -->\n`)) throw new Error('RELEASE.md was not prepared for this tag');
}

async function notes() {
  const isPrerelease = version.includes('-');
  const previousEntry = reachableReleaseTags({ stableOnly: !isPrerelease }).find(entry => entry.tag !== tag);
  const previous = previousEntry?.tag;
  if (previousEntry && previousEntry.anchor === git('rev-parse', 'HEAD')) {
    return renderNotes({ version: tag, commits: [] }, version, repo, previous);
  }
  const args = ['--config', 'cliff.toml', '--tag', tag, '--use-branch-tags', '--context'];
  // Pre-release notes cover only commits since the previous release (stable or
  // pre-release); stable notes aggregate everything since the previous stable tag.
  // Avoid --unreleased with an explicit range because git-cliff lets --unreleased
  // override the range back to the latest tag matching cliff.toml's tag_pattern.
  if (previousEntry) args.push(`${previousEntry.anchor}..HEAD`);
  else args.push('--unreleased');
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
    const release = git('log', '-1', '--format=%P%n%s', existing).split('\n');
    const detachedBeta = isCurrent && beta && release[0] && !/\s/.test(release[0]) && release[1] === `chore(release): ${tag}`;
    git('merge-base', '--is-ancestor', detachedBeta ? release[0] : existing, `refs/remotes/origin/${branch}`);
    git('checkout', '--detach', existing);
    await validate();
    await output(existing);
  } else {
    if (process.env.RELEASE_LICENSE_MANIFEST) {
      await writeFile('THIRD-PARTY-LICENSES.md', await readFile(process.env.RELEASE_LICENSE_MANIFEST));
    } else run(process.execPath, ['scripts/generate-licenses.mjs']);
    const releaseNotes = await notes();
    const syncVersionFiles = !isCurrent || version !== baseVersion;
    if (syncVersionFiles) {
      for (const path of ['package.json', 'src-tauri/tauri.conf.json']) {
        const source = await text(path);
        await writeFile(path, source.replace(/("version"\s*:\s*")[^"]+(")/, (_, prefix, suffix) => `${prefix}${version}${suffix}`));
      }
      for (const path of ['src-tauri/Cargo.toml', 'Cargo.lock']) {
        await writeFile(path, replaceCargoVersion(await text(path), 'auto-midi-player', version));
      }
    }
    await writeFile('RELEASE.md', releaseNotes);
    await validate();
    if (!buildOnly) {
      git('config', 'user.name', 'github-actions[bot]');
      git('config', 'user.email', '41898282+github-actions[bot]@users.noreply.github.com');
      if (!isCurrent) {
        git('add', '--', 'package.json', 'src-tauri/tauri.conf.json', 'src-tauri/Cargo.toml', 'Cargo.lock', 'RELEASE.md', 'THIRD-PARTY-LICENSES.md');
        git('commit', '-m', `chore(release): ${tag}`);
        git('tag', '-a', tag, '-m', `Release ${tag}`);
        git('push', '--atomic', 'origin', `HEAD:refs/heads/${branch}`, `refs/tags/${tag}`);
      } else if (syncVersionFiles) {
        // Keep the source branch at its commit-bumped version; attach the
        // -beta release commit only to the pushed tag.
        git('checkout', '--detach', 'HEAD');
        git('add', '--', 'package.json', 'src-tauri/tauri.conf.json', 'src-tauri/Cargo.toml', 'Cargo.lock', 'RELEASE.md', 'THIRD-PARTY-LICENSES.md');
        git('commit', '-m', `chore(release): ${tag}`);
        git('tag', '-a', tag, '-m', `Release ${tag}`);
        git('push', 'origin', `refs/tags/${tag}`);
      } else {
        const tagExists = existsRef(`refs/tags/${tag}^{commit}`);
        if (!tagExists) {
          git('tag', '-a', tag, '-m', `Release ${tag}`);
          git('push', 'origin', `refs/tags/${tag}`);
        }
      }
    }
    await output(git('rev-parse', 'HEAD'));
  }
  // Build-only runs overlay these files onto the immutable source checkout.
  await mkdir('dist/release-input/src-tauri', { recursive: true });
  for (const path of ['package.json', 'src-tauri/tauri.conf.json', 'src-tauri/Cargo.toml', 'Cargo.lock', 'RELEASE.md', 'THIRD-PARTY-LICENSES.md']) {
    await writeFile(`dist/release-input/${path}`, await readFile(path));
  }
}
