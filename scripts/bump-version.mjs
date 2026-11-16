import { readFile, writeFile, appendFile, unlink, mkdtemp, rm } from 'node:fs/promises';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { releaseVersion, nextReleaseVersion, replaceCargoVersion } from './release-lib.mjs';

process.chdir(fileURLToPath(new URL('../', import.meta.url)));

const SUMMARY_FILE = fileURLToPath(new URL('../.bump-summary.json', import.meta.url));
export const VERSION_FILES = ['package.json', 'src-tauri/tauri.conf.json', 'src-tauri/Cargo.toml', 'Cargo.lock'];
export const OPTIONAL_VERSION_FILE = 'src-tauri/Cargo.lock';
const rawGit = (args, options = {}) => execFileSync('git', args, {
  encoding: 'utf8', stdio: ['pipe', 'pipe', 'pipe'], ...options,
});
const git = (...args) => rawGit(args).trim();
const text = path => readFile(path, 'utf8');
const existsRef = ref => {
  try { return git('rev-parse', '--verify', `${ref}^{commit}`); } catch { return null; }
};
const isAncestor = (ancestor, descendant) => {
  try { git('merge-base', '--is-ancestor', ancestor, descendant); return true; } catch { return false; }
};

export function isBotOrSkipCommit(message) {
  return !message || /\[(skip ci|ci skip)\]/i.test(message)
    || /^chore(?:\((?:version|release)\))?:\s*(?:bump|release)/i.test(message)
    || /^chore\((?:version|release)\):/i.test(message);
}

export function parseCliArgs(argv = process.argv.slice(2)) {
  const flags = new Set(argv.filter(arg => arg.startsWith('--')));
  const positional = argv.filter(arg => !arg.startsWith('--')).map(arg => arg.trim()).filter(Boolean);
  const subcommands = new Set(['analyze', 'evaluate', 'apply', 'write', 'commit', 'push', 'summary']);
  let command = subcommands.has(positional[0]?.toLowerCase()) ? positional.shift().toLowerCase() : 'all';
  if (command === 'evaluate') command = 'analyze';
  if (command === 'write') command = 'apply';
  let targetVersion = null;
  if (['apply', 'commit', 'push'].includes(command) && /^v?\d+\.\d+\.\d+/.test(positional[0] || '')) {
    targetVersion = releaseVersion(positional.shift());
  }
  return {
    command, targetVersion, before: positional[0] || null, after: positional[1] || null,
    commit: flags.has('--commit') || flags.has('--push') || ['commit', 'push'].includes(command),
    push: flags.has('--push') || command === 'push',
    dryRun: flags.has('--dry-run') || flags.has('--preview'),
    strictPush: flags.has('--strict-push'),
  };
}

export function getVersionAtCommit(commitRef) {
  if (!commitRef) return null;
  try { return releaseVersion(JSON.parse(git('show', `${commitRef}:package.json`)).version); } catch { return null; }
}

export function getCommitBumpType(message) {
  const header = /^([a-z][a-z0-9-]*)(?:\([^\r\n()]+\))?(!)?:[ \t]+\S[^\r\n]*/i.exec(message || '');
  if (!header) return 0;
  if (header[2] || /^BREAKING[ -]CHANGE:[ \t]+\S/m.test(message)) return 3;
  if (header[1].toLowerCase() === 'feat') return 2;
  return ['fix', 'perf', 'ref', 'refactor', 'ui'].includes(header[1].toLowerCase()) ? 1 : 0;
}

export function formatCommitType(type) {
  return ['⚪ IGNORED', '🟢 PATCH', '🟡 MINOR', '🔴 MAJOR'][type] || '⚪ IGNORED';
}

export function resolveChronologicalVersion(baseVersion, commits) {
  let version = releaseVersion(baseVersion);
  let bumped = false;
  for (const commit of commits) {
    const message = typeof commit === 'string' ? commit : commit.message;
    if (isBotOrSkipCommit(message)) continue;
    const next = nextReleaseVersion(version, [message]);
    if (next) { version = next; bumped = true; }
  }
  return bumped ? version : null;
}

export function findLastBumpCommit(targetAfter = 'HEAD') {
  // A published tag is a boundary: never rewrite it or its ancestors.
  let taggedCommit = null;
  try {
    const tag = git('describe', '--tags', '--abbrev=0', '--first-parent', '--match', 'v[0-9]*', targetAfter);
    releaseVersion(tag);
    taggedCommit = existsRef(tag);
  } catch {}

  // Compare actual versions; -S '"version"' only finds changes in occurrence count.
  const changes = git('log', '--first-parent', '--format=%H', targetAfter, '--', 'package.json').split('\n').filter(Boolean);
  for (const sha of changes) {
    if (taggedCommit && isAncestor(sha, taggedCommit)) return taggedCommit;
    const version = getVersionAtCommit(sha);
    if (!version) continue;
    const parent = existsRef(`${sha}^1`);
    if (version === getVersionAtCommit(parent)) continue;
    const message = git('show', '-s', '--format=%B', sha);
    // Re-evaluate the last amended code commit too, so reruns are idempotent and
    // an amended fix/feat classification can be corrected against its parent.
    if (parent && !isBotOrSkipCommit(message) && getCommitBumpType(message)) return parent;
    return sha; // Initial version, explicit manual version, or legacy bump commit.
  }
  return taggedCommit;
}

function resolveBaseline(before, after, options = {}) {
  if (options.strictPush && before && !/^0+$/.test(before) && existsRef(before) && isAncestor(before, after)) return before;
  return findLastBumpCommit(after) || (before && existsRef(before) && isAncestor(before, after) ? before : existsRef(`${after}^1`));
}

function parseLogOutput(raw) {
  return raw.split('\0').map(entry => entry.trim()).filter(Boolean).map(entry => {
    const [sha, timestamp, isoDate, ...message] = entry.split('\x1f');
    return { sha, timestamp: Number(timestamp), isoDate, date: isoDate.slice(0, 10), message: message.join('\x1f').trim() };
  });
}

export async function collectCommitObjects(before, after = 'HEAD', options = {}) {
  const target = existsRef(after || 'HEAD');
  if (!target) throw new Error(`Commit not found: ${after || 'HEAD'}`);
  const baseline = resolveBaseline(before, target, options);
  // Topological order ensures that parents are rewritten before their children,
  // even when commit timestamps are out of order or the push includes merges.
  return parseLogOutput(git('log', '--reverse', '--topo-order', '--format=%H%x1f%ct%x1f%cI%x1f%B%x00',
    baseline ? `${baseline}..${target}` : target));
}

export async function collectCommitMessages(before, after, options = {}) {
  return (await collectCommitObjects(before, after, options)).map(commit => commit.message);
}

function versionContents(path, source, version) {
  if (path.endsWith('.json')) {
    const updated = source.replace(/("version"\s*:\s*")[^"]+(")/, (_, prefix, suffix) => `${prefix}${version}${suffix}`);
    if (JSON.parse(updated).version !== version) throw new Error(`Failed to update ${path} to ${version}`);
    return updated;
  }
  return replaceCargoVersion(source, 'auto-midi-player', version);
}

export function versionEntries(sha, version) {
  const entries = rawGit(['ls-tree', '-z', sha, '--', ...VERSION_FILES, OPTIONAL_VERSION_FILE])
    .split('\0').filter(Boolean).map(entry => {
      const [metadata, path] = entry.split('\t');
      const [mode, type, blob] = metadata.split(' ');
      if (type !== 'blob' || mode === '120000') throw new Error(`Expected a regular version file: ${path}`);
      const source = rawGit(['cat-file', 'blob', blob]);
      return { path, mode, blob, source, updated: versionContents(path, source, version) };
    });
  for (const path of VERSION_FILES) {
    if (!entries.some(entry => entry.path === path)) throw new Error(`Missing version file at ${sha}: ${path}`);
  }
  return entries;
}

export async function bumpVersionFiles(nextVersion) {
  const version = releaseVersion(nextVersion);
  const files = [...VERSION_FILES];
  try { await text(OPTIONAL_VERSION_FILE); files.push(OPTIONAL_VERSION_FILE); } catch (error) {
    if (error.code !== 'ENOENT') throw error;
  }
  const updates = await Promise.all(files.map(async path => ({ path, updated: versionContents(path, await text(path), version) })));
  for (const { path, updated } of updates) await writeFile(path, updated);
  return files;
}

export async function saveSummary(data) {
  await writeFile(SUMMARY_FILE, JSON.stringify(data, null, 2), 'utf8');
}

export async function loadSummary() {
  try { return JSON.parse(await text(SUMMARY_FILE)); } catch (error) {
    if (error.code !== 'ENOENT') throw error;
    return null;
  }
}

export async function analyzeStep(options, before, after) {
  const afterSha = existsRef(after || 'HEAD');
  if (!afterSha) throw new Error(`Commit not found: ${after || 'HEAD'}`);
  const baseline = resolveBaseline(before, afterSha, options);
  const baselineVersion = getVersionAtCommit(baseline) || getVersionAtCommit(afterSha);
  if (!baselineVersion) throw new Error('No valid rewrite version found in package.json');
  const currentVersion = getVersionAtCommit(afterSha);
  const branch = process.env.BRANCH_NAME || git('branch', '--show-current');
  const commits = await collectCommitObjects(before, afterSha, options);

  console.log('🔍 [STEP] Analyzing Push & Commits for Version Bump');
  console.log(`Branch: ${branch}\nBaseline: ${baselineVersion} (${baseline || 'initial'})\nAfter SHA: ${afterSha}`);
  let version = baselineVersion;
  let bumped = false;
  const plan = commits.map((commit, index) => {
    const skip = isBotOrSkipCommit(commit.message);
    const bumpType = skip ? 0 : getCommitBumpType(commit.message);
    version = skip ? version : nextReleaseVersion(version, [commit.message]) || version;
    const changed = versionEntries(commit.sha, version).some(entry => entry.source !== entry.updated);
    bumped ||= changed;
    const typeLabel = skip ? '⚪ BOT/SKIP' : formatCommitType(bumpType);
    console.log(`  ${commit.sha.slice(0, 7)} ${typeLabel} → ${version} | ${commit.message.split('\n')[0]}`);
    return { ...commit, index: index + 1, typeLabel, version, changed };
  });

  const reason = bumped ? 'Per-commit versions need amendment' : 'No version bump required; all commit versions are current';
  console.log(bumped ? `Bumping version: ${currentVersion} -> ${version}; amending individual commits` : reason);
  const summary = {
    branch, status: bumped ? 'planned' : 'no_bump', before, afterSha, baseline,
    baselineVersion, previousVersion: currentVersion, currentVersion,
    targetVersion: version, reason, commits: plan,
  };
  await saveSummary(summary);
  if (process.env.GITHUB_OUTPUT) {
    await appendFile(process.env.GITHUB_OUTPUT,
      `bumped=${bumped}\ntarget_version=${version}\nprevious_version=${currentVersion}\ncommit_count=${commits.length}\n`);
  }
  return { bumped, version, previousVersion: currentVersion };
}

function assertCleanCheckout(expectedHead) {
  if (git('rev-parse', 'HEAD') !== expectedHead) throw new Error('HEAD changed since analysis; analyze the current checkout again.');
  if (git('status', '--porcelain', '--untracked-files=no')) {
    throw new Error('Commit amendment requires a clean index and working tree. Commit or stash tracked changes first.');
  }
}

function commitIdentity(headers, kind) {
  const match = new RegExp(`^${kind} (.*) <([^>]+)> (\\d+ [+-]\\d{4})$`, 'm').exec(headers);
  if (!match) throw new Error(`Missing ${kind} identity in commit`);
  const prefix = `GIT_${kind.toUpperCase()}`;
  return { [`${prefix}_NAME`]: match[1], [`${prefix}_EMAIL`]: match[2], [`${prefix}_DATE`]: `@${match[3]}` };
}

export function onlyVersionDifferences(before, after) {
  const allowed = new Set([...VERSION_FILES, OPTIONAL_VERSION_FILE]);
  const changed = rawGit(['diff-tree', '--no-commit-id', '--name-only', '-r', '-z', before, after]).split('\0').filter(Boolean);
  if (changed.some(path => !allowed.has(path))) return false;
  // Mask only our package's own version. Dependency changes, formatting,
  // file additions/deletions, and modes must survive a history rewrite.
  const originals = versionEntries(before, '8.0.0');
  const updated = versionEntries(after, '8.0.0');
  return originals.length === updated.length && originals.every(entry => updated.some(other =>
    other.path === entry.path && other.mode === entry.mode && other.updated === entry.updated));
}

export async function buildAmendedHistory(summary) {
  const temporary = await mkdtemp(join(tmpdir(), 'amp-bump-index-'));
  const env = { ...process.env, GIT_INDEX_FILE: join(temporary, 'index') };
  const rewritten = new Map();
  const commits = [];
  try {
    for (const commit of summary.commits) {
      const source = rawGit(['cat-file', 'commit', commit.sha]);
      const separator = source.indexOf('\n\n');
      const headers = source.slice(0, separator);
      const message = source.slice(separator + 2);
      const tree = /^tree (\w+)$/m.exec(headers)[1];
      const parents = [...headers.matchAll(/^parent (\w+)$/gm)].map(match => match[1]);
      const newParents = [...new Set(parents.map(parent => rewritten.get(parent) || parent))];
      if (commit.drop) {
        if (parents.length !== 1 || !onlyVersionDifferences(parents[0], commit.sha)) {
          throw new Error(`Refusing to drop ${commit.sha}: it contains changes beyond version strings or is a merge`);
        }
        rewritten.set(commit.sha, newParents[0]);
        commits.push({ ...commit, amendedSha: newParents[0] });
        console.log(`  ${commit.sha.slice(0, 7)}: removed obsolete version-only commit`);
        continue;
      }
      gitWithIndex(['read-tree', tree]);
      for (const entry of versionEntries(commit.sha, commit.version)) {
        if (entry.source === entry.updated) continue;
        const blob = rawGit(['hash-object', '-w', '--stdin'], { input: entry.updated }).trim();
        gitWithIndex(['update-index', '--cacheinfo', entry.mode, blob, entry.path]);
      }
      const newTree = gitWithIndex(['write-tree']);
      let newSha = commit.sha;
      if (newTree !== tree || parents.some((parent, index) => parent !== newParents[index])) {
        // Build from each original snapshot instead of cherry-picking: version
        // changes in later commits cannot conflict, and merge parents survive.
        newSha = rawGit(['commit-tree', newTree, ...newParents.flatMap(parent => ['-p', parent]), '--no-gpg-sign'], {
          input: message,
          env: { ...process.env, ...commitIdentity(headers, 'author'), ...commitIdentity(headers, 'committer') },
        }).trim();
      }
      rewritten.set(commit.sha, newSha);
      commits.push({ ...commit, amendedSha: newSha });
      console.log(`  ${commit.sha.slice(0, 7)} → ${newSha.slice(0, 7)}: v${commit.version}`);
    }
    return { ...summary, status: 'prepared', amendedHead: rewritten.get(summary.afterSha) || summary.afterSha, commits };
  } finally {
    await rm(temporary, { recursive: true, force: true });
  }

  function gitWithIndex(args) { return rawGit(args, { env }).trim(); }
}

export async function amendCommits(summary) {
  assertCleanCheckout(summary.afterSha);
  const updated = await buildAmendedHistory(summary);
  assertCleanCheckout(summary.afterSha);
  if (updated.amendedHead !== summary.afterSha) {
    git('read-tree', '-m', '-u', summary.afterSha, updated.amendedHead);
    try {
      rawGit(['update-ref', '-m', 'amend per-commit versions', 'HEAD', updated.amendedHead, summary.afterSha], {
        env: { ...process.env, GIT_COMMITTER_NAME: 'github-actions[bot]', GIT_COMMITTER_EMAIL: '41898282+github-actions[bot]@users.noreply.github.com' },
      });
    } catch (error) {
      git('read-tree', '-m', '-u', updated.amendedHead, summary.afterSha);
      throw error;
    }
  }
  updated.status = 'amended';
  await saveSummary(updated);
  return updated;
}

export function pushWithLease(branch, expectedSha) {
  git('check-ref-format', `refs/heads/${branch}`);
  if (!/^[a-f0-9]{40,64}$/i.test(expectedSha)) throw new Error('Original branch SHA is required for a leased push');
  // Never rebase or update the lease after a rejection: a newer push must be
  // analyzed separately, otherwise its commits could be lost or misversioned.
  git('push', `--force-with-lease=refs/heads/${branch}:${expectedSha}`, 'origin', `HEAD:refs/heads/${branch}`);
}

export async function applyStep(targetVersion) {
  const version = targetVersion || (await loadSummary())?.targetVersion;
  if (!version) throw new Error('Target version must be specified for apply step');
  console.log(`✏️ [STEP] Applying Version Bump to Project Files: v${version}`);
  return await bumpVersionFiles(version);
}

export async function pushStep(targetVersion, options = {}) {
  let summary = await loadSummary();
  if (!summary) {
    await analyzeStep(options, options.before || process.env.BEFORE_SHA, options.after || process.env.AFTER_SHA);
    summary = await loadSummary();
  }
  if (targetVersion && targetVersion !== summary.targetVersion) throw new Error('Target version does not match the analyzed per-commit plan');
  const push = options.push || options.command === 'push';
  if (push) {
    git('check-ref-format', `refs/heads/${summary.branch}`);
    const remoteSha = git('ls-remote', '--heads', 'origin', `refs/heads/${summary.branch}`).split(/\s/)[0];
    if (remoteSha !== summary.afterSha) throw new Error('Remote branch changed since this push; refusing to overwrite newer commits.');
  }
  if (summary.status === 'planned') summary = await amendCommits(summary);
  if (summary.status === 'amended' && push) {
    assertCleanCheckout(summary.amendedHead);
    pushWithLease(summary.branch, summary.afterSha);
    summary.status = 'pushed';
    await saveSummary(summary);
    console.log(`✓ Pushed amended commits to origin/${summary.branch}`);
  }
  return summary;
}

export async function summaryStep() {
  const summary = await loadSummary();
  if (!summary) return;
  const escapeCell = value => String(value ?? '').replace(/\\/g, '\\\\').replace(/\|/g, '\\|').replace(/[\r\n]+/g, ' ');
  const statuses = { planned: 'Amendments planned', amended: 'Commits amended locally', pushed: 'Amended commits pushed', no_bump: 'No bump required', preview: 'Preview only' };
  let md = '## 🏷️ Auto Version Bumper Summary\n\n';
  md += '| Property | Value |\n| :--- | :--- |\n';
  md += `| Branch | ${escapeCell(summary.branch)} |\n`;
  md += `| Version Transition | \`${summary.previousVersion}\` → **\`${summary.targetVersion}\`** |\n`;
  md += `| Status | ${statuses[summary.status] || summary.status} |\n`;
  md += `| Reason | ${escapeCell(summary.reason)} |\n\n`;
  md += '| # | Original commit | Amended commit | Classification | Version | Description |\n';
  md += '| :-: | :---: | :---: | :--- | :--- | :--- |\n';
  for (const commit of summary.commits) {
    md += `| ${commit.index} | \`${commit.sha.slice(0, 7)}\` | ${commit.amendedSha ? `\`${commit.amendedSha.slice(0, 7)}\`` : '—'} | ${commit.typeLabel} | \`${commit.version}\` | ${escapeCell(commit.message.split('\n')[0])} |\n`;
  }
  console.log(`📊 [SUMMARY] Version Bumper Execution Summary\n${md}`);
  if (process.env.GITHUB_STEP_SUMMARY) await appendFile(process.env.GITHUB_STEP_SUMMARY, `${md}\n`);
  await unlink(SUMMARY_FILE);
}

export async function main(argv = process.argv.slice(2)) {
  const options = parseCliArgs(argv);
  const before = options.before || process.env.BEFORE_SHA || null;
  const after = options.after || process.env.AFTER_SHA || null;
  if (options.command === 'summary') return await summaryStep();
  if (options.command === 'analyze') return await analyzeStep(options, before, after);
  if (options.dryRun && ['apply', 'commit', 'push'].includes(options.command)) {
    throw new Error('Use --preview with the default command to preview per-commit amendments.');
  }
  if (options.command === 'apply') return await applyStep(options.targetVersion);
  if (['commit', 'push'].includes(options.command)) return await pushStep(options.targetVersion, options);

  const result = await analyzeStep(options, before, after);
  if (result.bumped) {
    if (options.dryRun) {
      await saveSummary({ ...await loadSummary(), status: 'preview' });
    } else if (options.commit) {
      await pushStep(result.version, options);
    } else {
      await applyStep(result.version);
    }
  }
  await summaryStep();
  return { ...result, dryRun: options.dryRun };
}

if (process.argv[1] && fileURLToPath(import.meta.url) === process.argv[1]) {
  main().catch(error => { console.error(error); process.exitCode = 1; });
}
