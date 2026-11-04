import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, mkdirSync, writeFileSync, readFileSync, rmSync, cpSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { execFileSync, spawnSync } from 'node:child_process';
import { isBotOrSkipCommit, parseCliArgs, bumpVersionFiles, resolveChronologicalVersion } from '../scripts/bump-version.mjs';
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

test('resolveChronologicalVersion is date/time and sequence aware', () => {
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

  // Fixes on multiple dates after a feature advance patch for each date session
  const multiDayProgression = [
    { date: '2026-10-30', message: 'feat: add audio exporter' },
    { date: '2026-10-31', message: 'fix: handle wav buffer overflow' },
    { date: '2026-11-01', message: 'fix: correct sample rate' },
  ];
  assert.equal(resolveChronologicalVersion('8.0.0', multiDayProgression), '8.1.2');

  // Multiple fixes on the same date/session only bump patch once
  const multipleFixesSameDay = [
    { date: '2026-10-30', message: 'fix: bug 1' },
    { date: '2026-10-30', message: 'fix: bug 2' },
    { date: '2026-10-30', message: 'fix: bug 3' },
  ];
  assert.equal(resolveChronologicalVersion('8.0.0', multipleFixesSameDay), '8.0.1');
});

function createTempRepo(t, initialVersion = '8.0.0') {
  const directory = mkdtempSync(join(tmpdir(), 'amp-bump-test-'));
  t.after(() => {
    assert.ok(resolve(directory).startsWith(`${resolve(tmpdir())}\\amp-bump-test-`) || resolve(directory).startsWith(`${resolve(tmpdir())}/amp-bump-test-`));
    rmSync(directory, { recursive: true, force: true });
  });

  mkdirSync(join(directory, 'scripts'));
  mkdirSync(join(directory, 'src-tauri'));

  for (const name of ['bump-version.mjs', 'release-lib.mjs']) {
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

  const runBump = (...args) => spawnSync(process.execPath, ['scripts/bump-version.mjs', ...args], {
    cwd: directory,
    encoding: 'utf8',
    env: { ...process.env, GITHUB_TOKEN: '', GITHUB_EVENT_PATH: '', BEFORE_SHA: '', AFTER_SHA: '', BRANCH_NAME: 'rewrite-experimental' },
  });

  return { directory, git, runBump };
}

test('bump-version in a packed push checks all commits and creates a single bump commit', t => {
  const { directory, git, runBump } = createTempRepo(t, '8.0.0');
  const baseSha = git('rev-parse', 'HEAD');

  // Push contains 3 commits: chore, fix, feat
  git('commit', '--allow-empty', '-m', 'chore: update internal tooling');
  git('commit', '--allow-empty', '-m', 'fix: prevent hang on midi device unplug');
  git('commit', '--allow-empty', '-m', 'feat: support multi-track export');
  const headSha = git('rev-parse', 'HEAD');

  // Run bumper on the range baseSha..headSha with --commit
  const result = runBump(baseSha, headSha, '--commit');
  assert.equal(result.status, 0, result.stderr);
  assert.ok(result.stdout.includes('Bumping version: 8.0.0 -> 8.1.0'));
  assert.ok(result.stdout.includes('Committed: chore(version): bump to v8.1.0 [skip ci]'));

  // Verify all files were bumped to 8.1.0
  assert.equal(JSON.parse(readFileSync(join(directory, 'package.json'), 'utf8')).version, '8.1.0');
  assert.equal(JSON.parse(readFileSync(join(directory, 'src-tauri/tauri.conf.json'), 'utf8')).version, '8.1.0');
  assert.ok(readFileSync(join(directory, 'src-tauri/Cargo.toml'), 'utf8').includes('version = "8.1.0"'));
  assert.ok(readFileSync(join(directory, 'Cargo.lock'), 'utf8').includes('name = "auto-midi-player"\nversion = "8.1.0"'));

  // Verify exactly ONE bump commit was added on top of headSha
  const log = git('log', '-n', '2', '--oneline').split('\n');
  assert.match(log[0], /chore\(version\): bump to v8\.1\.0 \[skip ci\]/);
  assert.match(log[1], /feat: support multi-track export/);
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
  assert.ok(result.stdout.includes('No eligible commits to evaluate'));
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

  // Subsequent check sees that all commits are now accounted for by the bump commit
  const rerun = runBump('--dry-run');
  assert.equal(rerun.status, 0, rerun.stderr);
  assert.ok(rerun.stdout.includes('No eligible commits to evaluate'));
});

test('bump-version handles force push with amended commit message (fix upgraded to feat)', t => {
  const { directory, git, runBump } = createTempRepo(t, '8.0.0');
  const baseSha = git('rev-parse', 'HEAD');

  // Push 1: Developer commits a fix, bump runs and creates 8.0.1
  git('commit', '--allow-empty', '-m', 'fix: basic playback fix');
  const fixSha = git('rev-parse', 'HEAD');
  const bumpResult = runBump(baseSha, fixSha, '--commit');
  assert.equal(bumpResult.status, 0, bumpResult.stderr);
  assert.equal(JSON.parse(readFileSync(join(directory, 'package.json'), 'utf8')).version, '8.0.1');

  // Developer amends the commit to a feat (simulating interactive rebase or git commit --amend)
  // and force pushes, overwriting the bot's bump commit
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

  // Developer amends commit to fix and force pushes over the bump commit
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

test('analyze step logs and skips bot commits and [skip ci] markers', t => {
  const { directory, git } = createTempRepo(t, '8.1.0');
  const baseSha = git('rev-parse', 'HEAD');

  git('commit', '--allow-empty', '-m', 'chore(version): bump to v8.1.0 [skip ci]');
  const headSha = git('rev-parse', 'HEAD');

  const githubOutput = join(directory, 'github_output.txt');

  const analyzeRes = spawnSync(process.execPath, ['scripts/bump-version.mjs', 'analyze', baseSha, headSha], {
    cwd: directory,
    encoding: 'utf8',
    env: {
      ...process.env,
      GITHUB_OUTPUT: githubOutput,
      HEAD_COMMIT_MESSAGE: 'chore(version): bump to v8.1.0 [skip ci]',
      HEAD_COMMIT_AUTHOR: 'github-actions[bot]',
      BRANCH_NAME: 'rewrite-experimental',
    },
  });

  assert.equal(analyzeRes.status, 0, analyzeRes.stderr);
  assert.ok(analyzeRes.stdout.includes('Bot or [skip ci] commit detected on HEAD'));
  assert.ok(analyzeRes.stdout.includes('Skipping version bump evaluation to prevent automated loops'));
  assert.ok(readFileSync(githubOutput, 'utf8').includes('bumped=false'));
});
