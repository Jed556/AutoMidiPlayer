// One-time migration of legacy version-bump history. Preview is read-only;
// prepare builds and verifies objects; apply updates only the current branch.
// This tool never pushes or changes tags/other existing branches.
import { execFileSync } from 'node:child_process';
import { readFile, writeFile, mkdir, mkdtemp, copyFile } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import { dirname, join, resolve, sep } from 'node:path';
import { fileURLToPath } from 'node:url';
import {
  VERSION_FILES, OPTIONAL_VERSION_FILE, versionEntries, onlyVersionDifferences,
  getVersionAtCommit, isBotOrSkipCommit, getCommitBumpType, formatCommitType, buildAmendedHistory,
} from './bump-version.mjs';
import { nextReleaseVersion, releaseVersion } from './release-lib.mjs';

const rawGit = (...args) => execFileSync('git', args, { encoding: 'utf8', stdio: ['pipe', 'pipe', 'pipe'] });
const git = (...args) => rawGit(...args).trim();
const allVersionFiles = [...VERSION_FILES, OPTIONAL_VERSION_FILE];
const hash = bytes => createHash('sha256').update(bytes).digest('hex');
const zeroSha = '0'.repeat(git('rev-parse', 'HEAD').length);
const legacyBump = message => /^chore\(version\):\s*bump to v\d+\.\d+\.\d+(?:-[\w.-]+)?(?:\s+\[(?:skip ci|ci skip)\])?\s*$/i.test(message.split('\n')[0]);
const parentShas = headers => [...headers.matchAll(/^parent (\w+)$/gm)].map(match => match[1]);
function commitData(sha) {
  const source = rawGit('cat-file', 'commit', sha);
  const split = source.indexOf('\n\n');
  return { headers: source.slice(0, split), message: source.slice(split + 2) };
}
function updateRef(ref, next, previous) {
  execFileSync('git', ['update-ref', '-m', 'backfill per-commit versions', ref, next, previous], {
    stdio: ['pipe', 'pipe', 'pipe'],
    env: { ...process.env, GIT_COMMITTER_NAME: 'github-actions[bot]', GIT_COMMITTER_EMAIL: '41898282+github-actions[bot]@users.noreply.github.com' },
  });
}

export function analyzeBackfill() {
  const afterSha = git('rev-parse', 'HEAD');
  const branch = git('branch', '--show-current');
  if (!branch) throw new Error('Check out the branch to backfill first');
  const versionChanges = git('log', '--first-parent', '--reverse', '--format=%H', afterSha, '--', 'package.json').split('\n');
  const baseline = versionChanges.find(sha => getVersionAtCommit(sha));
  if (!baseline) throw new Error('No rewrite version baseline found');
  const baselineVersion = getVersionAtCommit(baseline);
  const commits = rawGit('log', '--reverse', '--topo-order', '--format=%H%x1f%B%x00', `${baseline}..${afterSha}`)
    .split('\0').map(value => value.trim()).filter(Boolean).map(value => {
      const [sha, ...message] = value.split('\x1f');
      return { sha, message: message.join('\x1f') };
    });
  const droppedCount = commits.filter(commit => legacyBump(commit.message)).length;
  const summary = { branch, afterSha, baseline, baselineVersion, previousVersion: getVersionAtCommit(afterSha), droppedCount };
  if (!droppedCount) return { ...summary, commits: [], targetVersion: summary.previousVersion, status: 'no_bump' };

  for (const tag of git('tag', '--merged', afterSha).split('\n').filter(Boolean)) {
    try { releaseVersion(tag); } catch { continue; }
    const tagSha = git('rev-parse', `${tag}^{commit}`);
    if (git('rev-list', `${baseline}..${tagSha}`)) throw new Error(`Release tag ${tag} intersects the backfill; refusing to rewrite published history`);
  }
  let version = baselineVersion;
  const plan = commits.map((commit, index) => {
    const drop = legacyBump(commit.message);
    if (drop) {
      const parents = parentShas(commitData(commit.sha).headers);
      if (parents.length !== 1 || !onlyVersionDifferences(parents[0], commit.sha)) {
        throw new Error(`Refusing to remove ${commit.sha}: legacy bump contains non-version changes or is a merge`);
      }
    } else if (!isBotOrSkipCommit(commit.message)) {
      version = nextReleaseVersion(version, [commit.message]) || version;
    }
    return { ...commit, index: index + 1, version, drop, typeLabel: drop ? 'REMOVE' : formatCommitType(getCommitBumpType(commit.message)) };
  });
  return { ...summary, status: 'planned', targetVersion: version, commits: plan };
}

export function verifyBackfill(report) {
  const mapping = new Map();
  let retained = 0;
  for (const commit of report.commits) {
    const original = commitData(commit.sha);
    const parents = parentShas(original.headers);
    const expectedParents = [...new Set(parents.map(parent => mapping.get(parent) || parent))];
    if (commit.drop) {
      if (parents.length !== 1 || !onlyVersionDifferences(parents[0], commit.sha) || commit.amendedSha !== expectedParents[0]) {
        throw new Error(`Invalid removal: ${commit.sha}`);
      }
    } else {
      const otherChanges = rawGit('diff-tree', '--no-commit-id', '--name-only', '-r', '-z', commit.sha, commit.amendedSha,
        '--', '.', ...allVersionFiles.map(path => `:(exclude)${path}`));
      if (otherChanges) throw new Error(`Non-version content changed at ${commit.sha}`);
      // Compare the complete manifest/lockfile bytes to the intended edit,
      // including every dependency and all surrounding formatting.
      const expectedFiles = versionEntries(commit.sha, commit.version);
      const actualFiles = versionEntries(commit.amendedSha, commit.version);
      if (expectedFiles.length !== actualFiles.length || expectedFiles.some(expected => !actualFiles.some(actual =>
        actual.path === expected.path && actual.mode === expected.mode && actual.source === expected.updated))) {
        throw new Error(`Version files do not match the planned edit at ${commit.sha}`);
      }
      const amended = commitData(commit.amendedSha);
      for (const identity of ['author', 'committer']) {
        const pattern = new RegExp(`^${identity} .+$`, 'm');
        if (pattern.exec(original.headers)?.[0] !== pattern.exec(amended.headers)?.[0]) throw new Error(`Identity changed at ${commit.sha}`);
      }
      if (original.message !== amended.message) throw new Error(`Commit message changed at ${commit.sha}`);
      if (parentShas(amended.headers).join(' ') !== expectedParents.join(' ')) throw new Error(`Parent mapping changed at ${commit.sha}`);
      retained++;
      if (retained % 10 === 0) console.log(`  Verified ${retained} retained snapshots...`);
    }
    mapping.set(commit.sha, commit.amendedSha);
  }
  if (!onlyVersionDifferences(report.afterSha, report.amendedHead)) throw new Error('Non-version files at the branch tip changed');
  const remaining = git('rev-list', '--count', `${report.baseline}..${report.amendedHead}`);
  if (Number(remaining) !== retained) throw new Error('Rewritten history contains unexpected commits');
  if (git('log', '--format=%s', `${report.baseline}..${report.amendedHead}`).split('\n').some(legacyBump)) {
    throw new Error('Legacy bump commits remain in rewritten history');
  }
  console.log(`Verified ${retained} retained commits: non-version content, version edits, authors, timestamps, messages, and parents.`);
  return retained;
}

function nonVersionIndex() {
  return rawGit('ls-files', '--stage', '-z', '--', '.', ...allVersionFiles.map(path => `:(exclude)${path}`));
}

async function captureCheckout(directory) {
  await mkdir(directory, { recursive: true });
  const index = nonVersionIndex();
  await writeFile(join(directory, 'non-version-index.txt'), index);
  await copyFile(resolve(git('rev-parse', '--git-path', 'index')), join(directory, 'index'));
  const dirtyPaths = new Set([
    ...rawGit('diff', '--no-ext-diff', '--name-only', '--no-renames', '-z').split('\0'),
    ...rawGit('diff', '--no-ext-diff', '--cached', '--name-only', '--no-renames', '-z').split('\0'),
  ].filter(path => path && !allVersionFiles.includes(path)));
  const files = [];
  const root = resolve('.') + sep;
  for (const path of dirtyPaths) {
    const absolute = resolve(path);
    if (!absolute.startsWith(root)) throw new Error(`Invalid tracked path: ${path}`);
    let bytes;
    try { bytes = await readFile(absolute); } catch (error) { if (error.code !== 'ENOENT') throw error; }
    if (bytes) {
      const destination = join(directory, 'working', path);
      await mkdir(dirname(destination), { recursive: true });
      await writeFile(destination, bytes);
    }
    files.push({ path, hash: bytes ? hash(bytes) : null });
  }
  const snapshot = { indexHash: hash(index), files };
  await writeFile(join(directory, 'manifest.json'), JSON.stringify(snapshot, null, 2));
  return snapshot;
}

async function verifyCheckout(snapshot) {
  if (hash(nonVersionIndex()) !== snapshot.indexHash) throw new Error('Non-version index entries changed');
  for (const file of snapshot.files) {
    let bytes;
    try { bytes = await readFile(file.path); } catch (error) { if (error.code !== 'ENOENT') throw error; }
    if ((bytes ? hash(bytes) : null) !== file.hash) throw new Error(`Working file changed: ${file.path}`);
  }
}

function assertSourceCheckout(report) {
  if (git('branch', '--show-current') !== report.branch || git('rev-parse', 'HEAD') !== report.afterSha) {
    throw new Error('Source branch changed since preparation; prepare a fresh backfill');
  }
  if (git('status', '--porcelain', '--', ...allVersionFiles)) throw new Error('Commit or stash edits to version files before backfilling');
}

export async function prepareBackfill() {
  const plan = analyzeBackfill();
  if (!plan.droppedCount) { console.log('No legacy bump commits remain; nothing to backfill.'); return plan; }
  assertSourceCheckout(plan);
  const backupRoot = resolve('..', 'bak');
  await mkdir(backupRoot, { recursive: true });
  const directory = await mkdtemp(join(backupRoot, 'version-backfill-'));
  const backupBranch = `backup/${plan.branch}-before-version-backfill-${directory.split(/[\\/]/).at(-1)}`;
  updateRef(`refs/heads/${backupBranch}`, plan.afterSha, zeroSha);
  await captureCheckout(join(directory, 'checkout-at-prepare'));
  const report = { ...await buildAmendedHistory(plan), backupBranch, directory };
  report.verifiedRetained = verifyBackfill(report);
  assertSourceCheckout(plan);
  await writeFile(join(directory, 'report.json'), JSON.stringify(report, null, 2));
  let markdown = `# Version history backfill\n\nBranch: ${report.branch}\n\nBackup: ${backupBranch}\n\nVersion: ${report.previousVersion} → ${report.targetVersion}\n\n`;
  markdown += `Removed: ${report.droppedCount} version-only commits. Verified: ${report.verifiedRetained} retained commits.\n\n`;
  markdown += '| Original | Rewritten | Version | Commit |\n| --- | --- | --- | --- |\n';
  for (const commit of report.commits) markdown += `| ${commit.sha.slice(0, 7)} | ${commit.drop ? 'removed' : commit.amendedSha.slice(0, 7)} | ${commit.version} | ${commit.message.split('\n')[0].replaceAll('|', '\\|')} |\n`;
  await writeFile(join(directory, 'report.md'), markdown);
  console.log(`Prepared backfill; current branch is unchanged.\nBACKFILL_REPORT=${directory}`);
  return report;
}

export async function applyBackfill(directory) {
  const report = JSON.parse(await readFile(join(resolve(directory), 'report.json'), 'utf8'));
  assertSourceCheckout(report);
  if (git('rev-parse', `refs/heads/${report.backupBranch}`) !== report.afterSha) throw new Error('Original history backup is missing');
  verifyBackfill(report);
  const snapshot = await captureCheckout(join(report.directory, 'checkout-at-apply'));
  const files = versionEntries(report.amendedHead, report.targetVersion).map(entry => entry.path);
  assertSourceCheckout(report);
  // Targeted restore preserves every unrelated staged and working-tree edit.
  git('restore', `--source=${report.amendedHead}`, '--staged', '--worktree', '--', ...files);
  try { updateRef(`refs/heads/${report.branch}`, report.amendedHead, report.afterSha); } catch (error) {
    git('restore', `--source=${report.afterSha}`, '--staged', '--worktree', '--', ...files);
    throw error;
  }
  await verifyCheckout(snapshot);
  report.status = 'applied';
  report.checkoutPreserved = true;
  await writeFile(join(report.directory, 'report.json'), JSON.stringify(report, null, 2));
  console.log(`Applied backfill to ${report.branch}: removed ${report.droppedCount} legacy bump commits; version ${report.targetVersion}.`);
  console.log(`Preserved all non-version index entries and ${snapshot.files.length} modified working files.\nBackup branch: ${report.backupBranch}\nReport: ${join(report.directory, 'report.md')}\nNo remote was updated.`);
  return report;
}

if (process.argv[1] && fileURLToPath(import.meta.url) === process.argv[1]) {
  const args = process.argv.slice(2);
  const action = args[0] || '--preview';
  const run = async () => {
    if (action === '--prepare' && args.length === 1) return await prepareBackfill();
    if (action === '--apply' && args.length === 2) return await applyBackfill(args[1]);
    if (action !== '--preview' || args.length > 1) throw new Error('Usage: node scripts/backfill-versions.mjs [--preview | --prepare | --apply <report-directory>]');
    const plan = analyzeBackfill();
    for (const commit of plan.commits) console.log(`${commit.sha.slice(0, 7)} ${commit.drop ? 'REMOVE' : commit.version} ${commit.message.split('\n')[0]}`);
    console.log(`${plan.droppedCount} version-only commits to remove; ${plan.previousVersion} → ${plan.targetVersion}. No files or refs changed.`);
    return plan;
  };
  run().catch(error => { console.error(error); process.exitCode = 1; });
}
