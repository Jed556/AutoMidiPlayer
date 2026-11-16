import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, mkdirSync, writeFileSync, readFileSync, rmSync, cpSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { execFileSync, spawnSync } from 'node:child_process';
import { isBotOrSkipCommit, parseCliArgs, resolveChronologicalVersion } from '../scripts/bump-version.mjs';
import { nextReleaseVersion } from '../scripts/release-lib.mjs';

test('isBotOrSkipCommit detects automated and skipped commits', () => {
  assert.equal(isBotOrSkipCommit('chore(version): bump to v8.1.0 [skip ci]'), true);
  assert.equal(isBotOrSkipCommit('chore(release): v8.1.0'), true);
  assert.equal(isBotOrSkipCommit('feat: add audio recording [skip ci]'), true);
  assert.equal(isBotOrSkipCommit('fix: audio bug [ci skip]'), true);
  assert.equal(isBotOrSkipCommit(''), true);
  assert.equal(isBotOrSkipCommit(null), true);

  assert.equal(isBotOrSkipCommit('feat: add audio recording'), false);
  assert.equal(isBotOrSkipCommit('fix: repair playback'), false);
  assert.equal(isBotOrSkipCommit('chore: update dependencies'), false);
  assert.equal(isBotOrSkipCommit('ui: modernize dialogs'), false);
});

test('parseCliArgs parses positional shas and flags accurately', () => {
  const parsed = parseCliArgs(['abc1234', 'def5678', '--commit', '--push']);
  assert.equal(parsed.before, 'abc1234');
  assert.equal(parsed.after, 'def5678');
  assert.equal(parsed.commit, true);
  assert.equal(parsed.push, true);
  assert.equal(parsed.dryRun, false);

  const dry = parseCliArgs(['--dry-run']);
  assert.equal(dry.before, null);
  assert.equal(dry.after, null);
  assert.equal(dry.commit, false);
  assert.equal(dry.dryRun, true);

  assert.equal(parseCliArgs(['--push']).commit, true);
  assert.equal(parseCliArgs(['commit']).command, 'commit');
});

test('packed push conventional resolution calculates a single combined bump', () => {
  // Multiple fixes in one push result in ONE patch bump
  const multipleFixes = ['fix: resolve buffer underrun', 'fix: repair note release', 'fix: fix midi clock'];
  assert.equal(nextReleaseVersion('8.0.0', multipleFixes), '8.0.1');

  // Fixes + a feature in one push result in ONE minor bump
  const fixesAndFeat = ['fix: resolve buffer underrun', 'feat: add virtual keyboard', 'ui: adjust button colors'];
  assert.equal(nextReleaseVersion('8.0.0', fixesAndFeat), '8.1.0');

  // Feature + breaking change in one push results in ONE major bump
  const featAndBreaking = ['feat: add virtual keyboard', 'feat!: redesign configuration format'];
  assert.equal(nextReleaseVersion('8.0.0', featAndBreaking), '9.0.0');

  // Maintenance and docs without parenthesis (docs:, chore:, ci:, etc.) are ignored
  const noParenthesisMaintenance = ['chore: clean up dependencies', 'docs: update readme', 'ci: improve workflow', 'test: add test'];
  assert.equal(nextReleaseVersion('8.0.0', noParenthesisMaintenance), null);

  // Maintenance and docs with parenthesis are also ignored
  const withParenthesisMaintenance = ['chore(deps): update pnpm', 'docs(api): explain endpoints'];
  assert.equal(nextReleaseVersion('8.0.0', withParenthesisMaintenance), null);

  // Mixed push: docs/chore (with or without parenthesis) + fix results in patch bump
  const mixedPushWithDocs = ['docs: update changelog', 'chore: tidy imports', 'fix: handle missing port'];
  assert.equal(nextReleaseVersion('8.0.0', mixedPushWithDocs), '8.0.1');
});

test('resolveChronologicalVersion advances independently for each eligible commit', () => {
  // Fix coming AFTER a feature bumps patch on top of minor
  const featThenFix = [
    { date: '2026-10-30', message: 'feat: add audio exporter' },
    { date: '2026-10-30', message: 'fix: handle wav buffer overflow' },
  ];
  assert.equal(resolveChronologicalVersion('8.0.0', featThenFix), '8.1.1');

  // Fix coming BEFORE a feature is superseded by the minor bump
  const fixThenFeat = [
    { date: '2026-10-30', message: 'fix: handle wav buffer overflow' },
    { date: '2026-10-30', message: 'feat: add audio exporter' },
  ];
  assert.equal(resolveChronologicalVersion('8.0.0', fixThenFeat), '8.1.0');

  // Fixes advance patch regardless of date.
  const multiDayProgression = [
    { date: '2026-10-30', message: 'feat: add audio exporter' },
    { date: '2026-10-31', message: 'fix: handle wav buffer overflow' },
    { date: '2026-11-01', message: 'fix: correct sample rate' },
  ];
  assert.equal(resolveChronologicalVersion('8.0.0', multiDayProgression), '8.1.2');

  // Each fix advances patch, including multiple commits on the same day.
  const multipleFixesSameDay = [
    { date: '2026-10-30', message: 'fix: bug 1' },
    { date: '2026-10-30', message: 'fix: bug 2' },
    { date: '2026-10-30', message: 'fix: bug 3' },
  ];
  assert.equal(resolveChronologicalVersion('8.0.0', multipleFixesSameDay), '8.0.3');
  assert.equal(resolveChronologicalVersion('8.0.0', ['feat: one', 'feat: two', 'fix: three']), '8.2.1');
  assert.equal(resolveChronologicalVersion('8.0.0', ['fix!: one', 'feat!: two']), '10.0.0');
  assert.equal(resolveChronologicalVersion('8.0.0', ['docs: one', 'fix: ignored [skip ci]']), null);
});

function createTempRepo(t, initialVersion = '8.0.0') {
  const directory = mkdtempSync(join(tmpdir(), 'amp-bump-test-'));
  t.after(() => {
    assert.ok(resolve(directory).startsWith(`${resolve(tmpdir())}\\amp-bump-test-`) || resolve(directory).startsWith(`${resolve(tmpdir())}/amp-bump-test-`));
    rmSync(directory, { recursive: true, force: true });
  });

  mkdirSync(join(directory, 'scripts'));
  mkdirSync(join(directory, 'src-tauri'));

  for (const name of ['bump-version.mjs', 'backfill-versions.mjs', 'release-lib.mjs']) {
    cpSync(new URL(`../scripts/${name}`, import.meta.url), join(directory, 'scripts', name));
  }

  writeFileSync(join(directory, 'package.json'), JSON.stringify({ name: 'auto-midi-player', version: initialVersion }, null, 2));
  writeFileSync(join(directory, 'src-tauri/tauri.conf.json'), JSON.stringify({ productName: 'Auto MIDI Player', version: initialVersion }, null, 2));
  writeFileSync(join(directory, 'src-tauri/Cargo.toml'), `[package]\nname = "auto-midi-player"\nversion = "${initialVersion}"\n`);
  writeFileSync(join(directory, 'Cargo.lock'), `version = 4\n\n[[package]]\nname = "amp-core"\nversion = "0.1.0"\n\n[[package]]\nname = "auto-midi-player"\nversion = "${initialVersion}"\n`);

  const git = (...args) => execFileSync('git', args, { cwd: directory, encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] }).trim();
  git('init', '-b', 'rewrite-experimental');
  git('config', 'user.name', 'Test User');
  git('config', 'user.email', 'test@example.com');
  git('add', '.');
  git('commit', '-m', 'chore: initial base commit');

  const runBumpWithEnv = (env, ...args) => spawnSync(process.execPath, ['scripts/bump-version.mjs', ...args], {
    cwd: directory,
    encoding: 'utf8',
    env: { ...process.env, GITHUB_TOKEN: '', GITHUB_EVENT_PATH: '', BEFORE_SHA: '', AFTER_SHA: '', HEAD_COMMIT_MESSAGE: '', HEAD_COMMIT_AUTHOR: '', BRANCH_NAME: 'rewrite-experimental', ...env },
  });
  const runBump = (...args) => runBumpWithEnv({}, ...args);

  return { directory, git, runBump, runBumpWithEnv };
}

test('bump-version amends every commit in a packed push without adding commits', t => {
  const { directory, git, runBump } = createTempRepo(t, '8.0.0');
  const baseSha = git('rev-parse', 'HEAD');

  // Push contains 3 commits: chore, fix, feat
  git('commit', '--allow-empty', '-m', 'chore: update internal tooling');
  git('commit', '--allow-empty', '-m', 'fix: prevent hang on midi device unplug');
  git('commit', '--allow-empty', '-m', 'feat: support multi-track export');
  const headSha = git('rev-parse', 'HEAD');
  const originalMetadata = git('log', '--reverse', '--format=%an <%ae> %aI %cn <%ce> %cI %B', `${baseSha}..${headSha}`);

  // Run bumper on the range baseSha..headSha with --commit
  const result = runBump(baseSha, headSha, '--commit');
  assert.equal(result.status, 0, result.stderr);
  assert.ok(result.stdout.includes('Bumping version: 8.0.0 -> 8.1.0'));

  // Verify all files were bumped to 8.1.0
  assert.equal(JSON.parse(readFileSync(join(directory, 'package.json'), 'utf8')).version, '8.1.0');
  assert.equal(JSON.parse(readFileSync(join(directory, 'src-tauri/tauri.conf.json'), 'utf8')).version, '8.1.0');
  assert.ok(readFileSync(join(directory, 'src-tauri/Cargo.toml'), 'utf8').includes('version = "8.1.0"'));
  assert.ok(readFileSync(join(directory, 'Cargo.lock'), 'utf8').includes('name = "auto-midi-player"\nversion = "8.1.0"'));

  const shas = git('rev-list', '--reverse', `${baseSha}..HEAD`).split('\n');
  assert.equal(shas.length, 3);
  assert.deepEqual(shas.map(sha => JSON.parse(git('show', `${sha}:package.json`)).version), ['8.0.0', '8.0.1', '8.1.0']);
  assert.equal(git('log', '--reverse', '--format=%an <%ae> %aI %cn <%ce> %cI %B', `${baseSha}..HEAD`), originalMetadata);
  assert.doesNotMatch(git('log', '--format=%s', `${baseSha}..HEAD`), /chore\(version\)/);
  assert.equal(git('status', '--porcelain'), '');
});

test('bump-version does nothing when push contains only maintenance commits', t => {
  const { directory, git, runBump } = createTempRepo(t, '8.1.0');
  const baseSha = git('rev-parse', 'HEAD');

  git('commit', '--allow-empty', '-m', 'chore: update pnpm lockfile');
  git('commit', '--allow-empty', '-m', 'docs: revise quick start instructions');
  const headSha = git('rev-parse', 'HEAD');

  const result = runBump(baseSha, headSha, '--commit');
  assert.equal(result.status, 0, result.stderr);
  assert.ok(result.stdout.includes('No version bump required'));

  // No files changed, HEAD remains at headSha
  assert.equal(git('rev-parse', 'HEAD'), headSha);
  assert.equal(JSON.parse(readFileSync(join(directory, 'package.json'), 'utf8')).version, '8.1.0');
});

test('bump-version ignores bot and [skip ci] commits to prevent loops', t => {
  const { directory, git, runBump } = createTempRepo(t, '8.1.0');
  const baseSha = git('rev-parse', 'HEAD');

  git('commit', '--allow-empty', '-m', 'chore(version): bump to v8.1.0 [skip ci]');
  const headSha = git('rev-parse', 'HEAD');

  const result = runBump(baseSha, headSha, '--commit');
  assert.equal(result.status, 0, result.stderr);
  assert.ok(result.stdout.includes('No version bump required'));
  assert.equal(git('rev-parse', 'HEAD'), headSha);
});

test('bump-version detects previously unbumped commits from earlier pushes', t => {
  const { directory, git, runBump } = createTempRepo(t, '8.0.0');

  // Push 1: A feat commit lands without triggering a bump (e.g. skipped CI)
  git('commit', '--allow-empty', '-m', 'feat: earlier unbumped feature');
  const skippedSha = git('rev-parse', 'HEAD');

  // Push 2: A subsequent fix commit is pushed
  git('commit', '--allow-empty', '-m', 'fix: patch bug in feature');
  const latestSha = git('rev-parse', 'HEAD');

  // Passing only skippedSha..latestSha into bump-version still detects the earlier unbumped feat
  const result = runBump(skippedSha, latestSha, '--commit');
  assert.equal(result.status, 0, result.stderr);
  assert.ok(result.stdout.includes('Bumping version: 8.0.0 -> 8.1.1'));

  // Version in files is now 8.1.1 (feat bumped to 8.1.0, then subsequent fix bumped to 8.1.1)
  assert.equal(JSON.parse(readFileSync(join(directory, 'package.json'), 'utf8')).version, '8.1.1');

  const featureSha = git('rev-parse', 'HEAD^');
  assert.equal(JSON.parse(git('show', `${featureSha}:package.json`)).version, '8.1.0');
  // Subsequent checks do not increment an already amended commit again.
  const rerun = runBump('--dry-run');
  assert.equal(rerun.status, 0, rerun.stderr);
  assert.ok(rerun.stdout.includes('No version bump required'));
});

test('bump-version handles force push with amended commit message (fix upgraded to feat)', t => {
  const { directory, git, runBump } = createTempRepo(t, '8.0.0');
  const baseSha = git('rev-parse', 'HEAD');

  // Push 1: The fix is amended to contain version 8.0.1.
  git('commit', '--allow-empty', '-m', 'fix: basic playback fix');
  const fixSha = git('rev-parse', 'HEAD');
  const bumpResult = runBump(baseSha, fixSha, '--commit');
  assert.equal(bumpResult.status, 0, bumpResult.stderr);
  assert.equal(JSON.parse(readFileSync(join(directory, 'package.json'), 'utf8')).version, '8.0.1');

  // Simulate a developer rewriting the original commit and force-pushing it.
  git('reset', '--hard', fixSha);
  git('commit', '--amend', '--allow-empty', '-m', 'feat: comprehensive playback overhaul');
  const amendedSha = git('rev-parse', 'HEAD');

  // Bumper runs on the force-pushed commit
  const forceResult = runBump(fixSha, amendedSha, '--commit');
  assert.equal(forceResult.status, 0, forceResult.stderr);
  assert.ok(forceResult.stdout.includes('8.1.0'));
  assert.equal(JSON.parse(readFileSync(join(directory, 'package.json'), 'utf8')).version, '8.1.0');
});

test('bump-version handles force push downgrading an amended commit (feat changed to fix)', t => {
  const { directory, git, runBump } = createTempRepo(t, '8.0.0');
  const baseSha = git('rev-parse', 'HEAD');

  // Push 1: Developer committed feat, bumped to 8.1.0
  git('commit', '--allow-empty', '-m', 'feat: accidental feature label');
  const featSha = git('rev-parse', 'HEAD');
  const bumpResult = runBump(baseSha, featSha, '--commit');
  assert.equal(bumpResult.status, 0, bumpResult.stderr);
  assert.equal(JSON.parse(readFileSync(join(directory, 'package.json'), 'utf8')).version, '8.1.0');

  // Developer rewrites the original commit as a fix and force-pushes it.
  git('reset', '--hard', featSha);
  git('commit', '--amend', '--allow-empty', '-m', 'fix: correct scope to bugfix');
  const amendedSha = git('rev-parse', 'HEAD');

  // Bumper detects that relative to the 8.0.0 baseline, the amended commit is only a fix
  const forceResult = runBump(featSha, amendedSha, '--commit');
  assert.equal(forceResult.status, 0, forceResult.stderr);
  assert.ok(forceResult.stdout.includes('8.0.1'));
  assert.equal(JSON.parse(readFileSync(join(directory, 'package.json'), 'utf8')).version, '8.0.1');
});

test('multi-step workflow subcommands (analyze -> apply -> summary) execute sequentially and write outputs', t => {
  const { directory, git, runBump } = createTempRepo(t, '8.0.0');
  const baseSha = git('rev-parse', 'HEAD');

  git('commit', '--allow-empty', '-m', 'feat: add vst plugin hosting');
  const featSha = git('rev-parse', 'HEAD');

  const githubOutput = join(directory, 'github_output.txt');
  const stepSummary = join(directory, 'step_summary.md');

  // Step 1: analyze
  const analyzeRes = spawnSync(process.execPath, ['scripts/bump-version.mjs', 'analyze', baseSha, featSha], {
    cwd: directory,
    encoding: 'utf8',
    env: { ...process.env, GITHUB_OUTPUT: githubOutput, GITHUB_STEP_SUMMARY: stepSummary, BRANCH_NAME: 'rewrite-experimental' },
  });
  assert.equal(analyzeRes.status, 0, analyzeRes.stderr);
  assert.ok(analyzeRes.stdout.includes('🔍 [STEP] Analyzing Push & Commits for Version Bump'));
  assert.ok(analyzeRes.stdout.includes('Bumping version: 8.0.0 -> 8.1.0'));

  const outputContent = readFileSync(githubOutput, 'utf8');
  assert.ok(outputContent.includes('bumped=true'));
  assert.ok(outputContent.includes('target_version=8.1.0'));
  assert.ok(outputContent.includes('previous_version=8.0.0'));

  // Step 2: apply
  const applyRes = spawnSync(process.execPath, ['scripts/bump-version.mjs', 'apply', '8.1.0'], {
    cwd: directory,
    encoding: 'utf8',
    env: { ...process.env },
  });
  assert.equal(applyRes.status, 0, applyRes.stderr);
  assert.ok(applyRes.stdout.includes('✏️ [STEP] Applying Version Bump to Project Files: v8.1.0'));
  assert.equal(JSON.parse(readFileSync(join(directory, 'package.json'), 'utf8')).version, '8.1.0');

  // Step 3: summary
  const summaryRes = spawnSync(process.execPath, ['scripts/bump-version.mjs', 'summary'], {
    cwd: directory,
    encoding: 'utf8',
    env: { ...process.env, GITHUB_STEP_SUMMARY: stepSummary },
  });
  assert.equal(summaryRes.status, 0, summaryRes.stderr);
  assert.ok(summaryRes.stdout.includes('📊 [SUMMARY] Version Bumper Execution Summary'));

  const summaryContent = readFileSync(stepSummary, 'utf8');
  assert.ok(summaryContent.includes('## 🏷️ Auto Version Bumper Summary'));
  assert.ok(summaryContent.includes('`8.0.0` → **`8.1.0`**'));
  assert.ok(summaryContent.includes('feat: add vst plugin hosting'));
});

test('a skipped or bot-authored HEAD does not hide eligible earlier commits', t => {
  const { directory, git } = createTempRepo(t, '8.1.0');
  const baseSha = git('rev-parse', 'HEAD');

  git('commit', '--allow-empty', '-m', 'fix: repair earlier playback failure');
  git('commit', '--allow-empty', '-m', 'docs: update help [skip ci]');
  const headSha = git('rev-parse', 'HEAD');

  const githubOutput = join(directory, 'github_output.txt');

  const analyzeRes = spawnSync(process.execPath, ['scripts/bump-version.mjs', 'analyze', baseSha, headSha], {
    cwd: directory,
    encoding: 'utf8',
    env: {
      ...process.env,
      GITHUB_OUTPUT: githubOutput,
      HEAD_COMMIT_MESSAGE: 'docs: update help [skip ci]',
      HEAD_COMMIT_AUTHOR: 'github-actions[bot]',
      BRANCH_NAME: 'rewrite-experimental',
    },
  });

  assert.equal(analyzeRes.status, 0, analyzeRes.stderr);
  assert.ok(analyzeRes.stdout.includes('fix: repair earlier playback failure'));
  assert.ok(readFileSync(githubOutput, 'utf8').includes('bumped=true'));
  assert.ok(readFileSync(githubOutput, 'utf8').includes('target_version=8.1.1'));
});

function assertVersionAt(git, sha, version) {
  assert.equal(JSON.parse(git('show', `${sha}:package.json`)).version, version);
  assert.equal(JSON.parse(git('show', `${sha}:src-tauri/tauri.conf.json`)).version, version);
  assert.match(git('show', `${sha}:src-tauri/Cargo.toml`), new RegExp(`version = "${version.replaceAll('.', '\\.')}"`));
  assert.ok(git('show', `${sha}:Cargo.lock`).includes(`name = "auto-midi-player"\nversion = "${version}"`));
}

test('each fix has its own version and later maintenance inherits it', t => {
  const { directory, git, runBump } = createTempRepo(t);
  const base = git('rev-parse', 'HEAD');
  for (const message of ['fix: one', 'fix: two', 'docs: help', 'feat: three', 'fix: four', 'chore: cleanup']) {
    writeFileSync(join(directory, 'changes.txt'), message);
    git('add', 'changes.txt');
    git('commit', '-m', message);
  }
  const original = git('rev-list', '--reverse', `${base}..HEAD`).split('\n');
  const result = runBump(base, 'HEAD', '--commit');
  assert.equal(result.status, 0, result.stderr);
  const amended = git('rev-list', '--reverse', `${base}..HEAD`).split('\n');
  assert.equal(amended.length, original.length);
  ['8.0.1', '8.0.2', '8.0.2', '8.1.0', '8.1.1', '8.1.1'].forEach((version, index) => {
    assertVersionAt(git, amended[index], version);
    assert.equal(git('show', `${amended[index]}:changes.txt`), git('show', `${original[index]}:changes.txt`));
    assert.equal(git('show', '-s', '--format=%B', amended[index]), git('show', '-s', '--format=%B', original[index]));
  });
  const tip = git('rev-parse', 'HEAD');
  const rerun = runBump('--commit');
  assert.equal(rerun.status, 0, rerun.stderr);
  assert.equal(git('rev-parse', 'HEAD'), tip);
});

test('new pushes advance from amended history and correct a relabeled stamped commit', t => {
  const { git, runBump } = createTempRepo(t);
  git('commit', '--allow-empty', '-m', 'fix: first');
  let result = runBump('--commit');
  assert.equal(result.status, 0, result.stderr);
  const stampedFix = git('rev-parse', 'HEAD');
  git('commit', '--allow-empty', '-m', 'fix: second');
  result = runBump(stampedFix, 'HEAD', '--commit');
  assert.equal(result.status, 0, result.stderr);
  assertVersionAt(git, 'HEAD', '8.0.2');
  assert.equal(git('rev-parse', 'HEAD^'), stampedFix);

  git('commit', '--amend', '--allow-empty', '-m', 'feat: relabeled second change');
  result = runBump('--commit');
  assert.equal(result.status, 0, result.stderr);
  assertVersionAt(git, 'HEAD', '8.1.0');
  git('commit', '--amend', '--allow-empty', '-m', 'fix: corrected second change');
  result = runBump('--commit');
  assert.equal(result.status, 0, result.stderr);
  assertVersionAt(git, 'HEAD', '8.0.2');
});

test('preview leaves commits and version files unchanged', t => {
  const { git, runBump } = createTempRepo(t);
  git('commit', '--allow-empty', '-m', 'feat: preview only');
  const tip = git('rev-parse', 'HEAD');
  const result = runBump('--preview', '--push');
  assert.equal(result.status, 0, result.stderr);
  assert.match(result.stdout, /Preview only/);
  assert.equal(git('rev-parse', 'HEAD'), tip);
  assertVersionAt(git, 'HEAD', '8.0.0');
  assert.equal(git('status', '--porcelain'), '');
});

test('amendment refuses staged and unstaged changes without losing them', t => {
  const { directory, git, runBump } = createTempRepo(t);
  git('commit', '--allow-empty', '-m', 'fix: needs a version');
  const tip = git('rev-parse', 'HEAD');
  const packagePath = join(directory, 'package.json');
  const original = readFileSync(packagePath, 'utf8');
  writeFileSync(packagePath, `${original}\n`);
  git('add', 'package.json');
  writeFileSync(packagePath, `${original}\n\n`);
  const status = git('status', '--porcelain', '--untracked-files=no');
  const index = git('show', ':package.json');
  const result = runBump('--commit');
  assert.notEqual(result.status, 0);
  assert.match(result.stderr, /clean index and working tree/);
  assert.equal(git('rev-parse', 'HEAD'), tip);
  assert.equal(git('show', ':package.json'), index);
  assert.equal(readFileSync(packagePath, 'utf8'), `${original}\n\n`);
  assert.equal(git('status', '--porcelain', '--untracked-files=no'), status);
});

test('rewriting preserves merge parents, messages, metadata, and original file snapshots', t => {
  const { directory, git, runBump } = createTempRepo(t);
  const base = git('rev-parse', 'HEAD');
  writeFileSync(join(directory, 'main.txt'), 'main content\n');
  git('add', 'main.txt');
  git('commit', '-m', 'fix: main branch repair');
  git('switch', '-c', 'feature', base);
  writeFileSync(join(directory, 'feature.txt'), 'feature content\n');
  git('add', 'feature.txt');
  git('commit', '-m', 'feat: branch feature', '-m', 'Preserve this body exactly.');
  git('switch', 'rewrite-experimental');
  git('merge', '--no-ff', 'feature', '-m', 'Merge feature');
  const original = git('rev-list', '--reverse', '--topo-order', `${base}..HEAD`).split('\n');
  const result = runBump(base, 'HEAD', '--commit');
  assert.equal(result.status, 0, result.stderr);
  const amended = git('rev-list', '--reverse', '--topo-order', `${base}..HEAD`).split('\n');
  assert.equal(amended.length, original.length);
  const mapping = new Map(original.map((sha, index) => [sha, amended[index]]));
  const messages = [];
  amended.forEach((sha, index) => {
    const old = original[index];
    messages.push(git('show', '-s', '--format=%B', old));
    const version = resolveChronologicalVersion('8.0.0', messages) || '8.0.0';
    assertVersionAt(git, sha, version);
    const oldParents = git('show', '-s', '--format=%P', old).split(' ');
    assert.equal(git('show', '-s', '--format=%P', sha), oldParents.map(parent => mapping.get(parent) || parent).join(' '));
    assert.equal(git('show', '-s', '--format=%an <%ae> %aI %cn <%ce> %cI %B', sha), git('show', '-s', '--format=%an <%ae> %aI %cn <%ce> %cI %B', old));
    assert.equal(git('diff', '--name-only', old, sha, '--', '.', ':!package.json', ':!src-tauri/tauri.conf.json', ':!src-tauri/Cargo.toml', ':!Cargo.lock'), '');
  });
  assert.equal(git('show', '-s', '--format=%P', 'HEAD').split(' ').length, 2);
  const tip = git('rev-parse', 'HEAD');
  assert.equal(runBump('--commit').status, 0);
  assert.equal(git('rev-parse', 'HEAD'), tip);
});

test('release tags bound rewrites and optional Cargo lockfiles stay synchronized', t => {
  const { directory, git, runBump } = createTempRepo(t);
  writeFileSync(join(directory, 'src-tauri/Cargo.lock'), 'version = 4\n\n[[package]]\nname = "auto-midi-player"\nversion = "8.0.0"\n');
  git('add', 'src-tauri/Cargo.lock');
  git('commit', '-m', 'feat: released feature');
  let result = runBump('--commit');
  assert.equal(result.status, 0, result.stderr);
  const released = git('rev-parse', 'HEAD');
  git('tag', 'v8.1.0');
  git('commit', '--allow-empty', '-m', 'fix: after release');
  result = runBump('--commit');
  assert.equal(result.status, 0, result.stderr);
  assertVersionAt(git, 'HEAD', '8.1.1');
  assert.ok(git('show', 'HEAD:src-tauri/Cargo.lock').includes('version = "8.1.1"'));
  assert.equal(git('rev-parse', 'HEAD^'), released);
  assert.equal(git('rev-parse', 'v8.1.0'), released);
});

test('workflow supports detached checkouts without configured git identity', t => {
  const { directory, git, runBumpWithEnv } = createTempRepo(t);
  git('commit', '--allow-empty', '-m', 'fix: detached head');
  git('switch', '--detach');
  git('config', '--unset', 'user.name');
  git('config', '--unset', 'user.email');
  const config = join(directory, 'empty-gitconfig');
  writeFileSync(config, '');
  const result = runBumpWithEnv({ GIT_CONFIG_GLOBAL: config, GIT_CONFIG_NOSYSTEM: '1' }, '--commit');
  assert.equal(result.status, 0, result.stderr);
  assertVersionAt(git, 'HEAD', '8.0.1');
  assert.equal(git('show', '-s', '--format=%an <%ae>', 'HEAD'), 'Test User <test@example.com>');
});

function addRemote(directory, git) {
  const remote = join(directory, 'remote.git');
  git('init', '--bare', remote);
  git('remote', 'add', 'origin', remote);
  git('push', 'origin', 'HEAD:refs/heads/rewrite-experimental');
  return remote;
}

test('workflow analyze -> commit -> push publishes amended history using the original tip', t => {
  const { directory, git, runBump } = createTempRepo(t);
  const base = git('rev-parse', 'HEAD');
  git('commit', '--allow-empty', '-m', 'fix: first pushed repair');
  git('commit', '--allow-empty', '-m', 'fix: second pushed repair');
  const original = git('rev-parse', 'HEAD');
  const remote = addRemote(directory, git);
  git('switch', '--detach');
  for (const args of [['analyze', base, original], ['commit'], ['push']]) {
    const result = runBump(...args);
    assert.equal(result.status, 0, result.stderr);
  }
  const tip = git('rev-parse', 'HEAD');
  assert.notEqual(tip, original);
  assertVersionAt(git, 'HEAD^', '8.0.1');
  assertVersionAt(git, 'HEAD', '8.0.2');
  assert.equal(git('--git-dir', remote, 'rev-parse', 'refs/heads/rewrite-experimental'), tip);
  assert.equal(git('rev-list', '--count', `${base}..HEAD`), '2');
  const summary = runBump('summary');
  assert.equal(summary.status, 0, summary.stderr);
  assert.match(summary.stdout, /Amended commits pushed/);
});

test('stale pushes and explicit leases protect concurrent remote commits', t => {
  const { directory, git, runBump } = createTempRepo(t);
  git('commit', '--allow-empty', '-m', 'fix: first push');
  const original = git('rev-parse', 'HEAD');
  const remote = addRemote(directory, git);
  assert.equal(runBump('analyze').status, 0);
  assert.equal(runBump('commit').status, 0);
  const amended = git('rev-parse', 'HEAD');
  const concurrent = git('commit-tree', `${original}^{tree}`, '-p', original, '-m', 'fix: concurrent push');
  git('push', 'origin', `${concurrent}:refs/heads/rewrite-experimental`);
  const result = runBump('push');
  assert.notEqual(result.status, 0);
  assert.match(result.stderr, /Remote branch changed/);
  assert.equal(git('rev-parse', 'HEAD'), amended);

  // Exercise the final atomic lease check too, independent of the preflight.
  const lease = spawnSync(process.execPath, ['--input-type=module', '-e',
    `import { pushWithLease } from './scripts/bump-version.mjs'; pushWithLease('rewrite-experimental', '${original}');`],
  { cwd: directory, encoding: 'utf8' });
  assert.notEqual(lease.status, 0);
  assert.equal(git('--git-dir', remote, 'rev-parse', 'refs/heads/rewrite-experimental'), concurrent);
  assert.equal(git('rev-parse', 'HEAD'), amended);
  const summary = runBump('summary');
  assert.match(summary.stdout, /Commits amended locally/);
  assert.doesNotMatch(summary.stdout, /Amended commits pushed/);
});

function backfillRunner(directory) {
  return (...args) => spawnSync(process.execPath, ['scripts/backfill-versions.mjs', ...args], {
    cwd: directory, encoding: 'utf8',
    env: { ...process.env, GITHUB_EVENT_PATH: '', BEFORE_SHA: '', AFTER_SHA: '', BRANCH_NAME: 'rewrite-experimental' },
  });
}

function legacyBump(directory, git, version, includeCode = false) {
  const files = ['package.json', 'src-tauri/tauri.conf.json', 'src-tauri/Cargo.toml', 'Cargo.lock'];
  for (const file of files) {
    const path = join(directory, file);
    const source = readFileSync(path, 'utf8');
    const updated = file.endsWith('.json')
      ? source.replace(/("version"\s*:\s*")[^"]+("?)/, (_, prefix) => `${prefix}${version}"`)
      : source.replace(/(name = "auto-midi-player"\nversion = ")[^"]+"/, `$1${version}"`);
    writeFileSync(path, updated);
  }
  if (includeCode) writeFileSync(join(directory, 'important.txt'), 'Do not lose this change.\n');
  git('add', ...files, ...(includeCode ? ['important.txt'] : []));
  git('commit', '-m', `chore(version): bump to v${version} [skip ci]`);
}

test('backfill removes legacy bumps, verifies all snapshots, and preserves unrelated staged and working edits', t => {
  const { directory, git, runBump } = createTempRepo(t);
  const runBackfill = backfillRunner(directory);
  const base = git('rev-parse', 'HEAD');
  for (const message of ['fix: one', 'fix: two']) {
    writeFileSync(join(directory, 'content.txt'), message);
    git('add', 'content.txt');
    git('commit', '-m', message);
  }
  legacyBump(directory, git, '8.0.1');
  git('commit', '--allow-empty', '-m', 'feat: export');
  legacyBump(directory, git, '8.1.0');
  git('commit', '--allow-empty', '-m', 'docs: update help');
  const source = git('rev-parse', 'HEAD');
  writeFileSync(join(directory, 'content.txt'), 'staged code\n');
  git('add', 'content.txt');
  writeFileSync(join(directory, 'content.txt'), 'working code after staging\n');
  writeFileSync(join(directory, 'untracked.txt'), 'preserve me\n');
  const staged = git('show', ':content.txt');
  const preview = runBackfill('--preview');
  assert.equal(preview.status, 0, preview.stderr);
  assert.match(preview.stdout, /2 version-only commits to remove/);
  assert.equal(git('rev-parse', 'HEAD'), source);
  const prepare = runBackfill('--prepare');
  assert.equal(prepare.status, 0, prepare.stderr);
  const reportPath = prepare.stdout.match(/BACKFILL_REPORT=(.+)/)[1].trim();
  assert.ok(resolve(reportPath).startsWith(`${resolve(tmpdir())}\\bak\\version-backfill-`) || resolve(reportPath).startsWith(`${resolve(tmpdir())}/bak/version-backfill-`));
  t.after(() => rmSync(reportPath, { recursive: true, force: true }));
  assert.equal(git('rev-parse', 'HEAD'), source);
  const report = JSON.parse(readFileSync(join(reportPath, 'report.json'), 'utf8'));
  assert.equal(report.verifiedRetained, 4);
  assert.equal(git('rev-parse', report.backupBranch), source);
  const apply = runBackfill('--apply', reportPath);
  assert.equal(apply.status, 0, apply.stderr);
  const rewritten = git('rev-list', '--reverse', `${base}..HEAD`).split('\n');
  assert.equal(rewritten.length, 4);
  ['8.0.1', '8.0.2', '8.1.0', '8.1.0'].forEach((version, index) => assertVersionAt(git, rewritten[index], version));
  assert.doesNotMatch(git('log', '--format=%s'), /chore\(version\)/);
  assert.equal(git('show', ':content.txt'), staged);
  assert.equal(readFileSync(join(directory, 'content.txt'), 'utf8'), 'working code after staging\n');
  assert.equal(readFileSync(join(directory, 'untracked.txt'), 'utf8'), 'preserve me\n');
  assert.equal(git('diff', '--name-only', source, 'HEAD', '--', '.', ':!package.json', ':!src-tauri/tauri.conf.json', ':!src-tauri/Cargo.toml', ':!Cargo.lock'), '');
  const rerun = runBackfill('--preview');
  assert.equal(rerun.status, 0, rerun.stderr);
  assert.match(rerun.stdout, /0 version-only commits/);
  const check = runBump('--preview');
  assert.equal(check.status, 0, check.stderr);
  assert.match(check.stdout, /No version bump required/);
});

test('backfill refuses to remove a bump containing non-version changes', t => {
  const { directory, git } = createTempRepo(t);
  git('commit', '--allow-empty', '-m', 'fix: one');
  legacyBump(directory, git, '8.0.1', true);
  const source = git('rev-parse', 'HEAD');
  const result = backfillRunner(directory)('--prepare');
  assert.notEqual(result.status, 0);
  assert.match(result.stderr, /legacy bump contains non-version changes/);
  assert.equal(git('rev-parse', 'HEAD'), source);
  assert.equal(readFileSync(join(directory, 'important.txt'), 'utf8'), 'Do not lose this change.\n');
});

test('backfill refuses histories intersecting a release tag', t => {
  const { directory, git } = createTempRepo(t);
  git('commit', '--allow-empty', '-m', 'fix: released fix');
  legacyBump(directory, git, '8.0.1');
  git('tag', 'v8.0.1');
  const source = git('rev-parse', 'HEAD');
  const result = backfillRunner(directory)('--prepare');
  assert.notEqual(result.status, 0);
  assert.match(result.stderr, /Release tag v8.0.1 intersects/);
  assert.equal(git('rev-parse', 'HEAD'), source);
  assert.equal(git('rev-parse', 'v8.0.1'), source);
});
