import { readFile, writeFile, appendFile, unlink } from 'node:fs/promises';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { existsSync } from 'node:fs';
import { releaseVersion, nextReleaseVersion, replaceCargoVersion } from './release-lib.mjs';

process.chdir(fileURLToPath(new URL('../', import.meta.url)));

const SUMMARY_FILE = fileURLToPath(new URL('../.bump-summary.json', import.meta.url));

const run = (command, args, options = {}) => execFileSync(command, args, { encoding: 'utf8', stdio: ['ignore', 'pipe', 'inherit'], ...options }).trim();
const git = (...args) => run('git', args);
const existsRef = ref => {
  try { return git('rev-parse', '--verify', `${ref}^{commit}`); } catch { return null; }
};
const isAncestor = (ancestor, descendant) => {
  try { git('merge-base', '--is-ancestor', ancestor, descendant); return true; } catch { return false; }
};
const text = path => readFile(path, 'utf8');

export function isBotOrSkipCommit(message) {
  if (!message) return true;
  if (/\[(skip ci|ci skip)\]/i.test(message)) return true;
  if (/^chore(?:\((?:version|release)\))?:\s*(?:bump|release)/i.test(message)) return true;
  if (/^chore\((?:version|release)\):/i.test(message)) return true;
  return false;
}

export function parseCliArgs(argv = process.argv.slice(2)) {
  const flags = new Set();
  const positional = [];
  for (const arg of argv) {
    if (arg.startsWith('--')) flags.add(arg);
    else if (arg.trim()) positional.push(arg.trim());
  }

  const subcommands = new Set(['analyze', 'evaluate', 'apply', 'write', 'commit', 'push', 'summary']);
  let command = 'all';
  let targetVersion = null;

  if (positional.length > 0 && subcommands.has(positional[0].toLowerCase())) {
    command = positional.shift().toLowerCase();
    if (command === 'evaluate') command = 'analyze';
    if (command === 'write') command = 'apply';
  }

  let before = positional[0] || null;
  let after = positional[1] || null;

  if ((command === 'apply' || command === 'commit' || command === 'push') && before && /^v?\d+\.\d+\.\d+/.test(before)) {
    targetVersion = releaseVersion(before);
    before = null;
    after = null;
  }

  return {
    command,
    targetVersion,
    before,
    after,
    commit: flags.has('--commit') || command === 'commit' || command === 'push',
    push: flags.has('--push') || command === 'push',
    dryRun: flags.has('--dry-run') || flags.has('--preview'),
    strictPush: flags.has('--strict-push'),
  };
}

export function findLastBumpCommit(targetAfter = 'HEAD') {
  // 1. Look for the most recent chore(version): or chore(release): commit reachable from targetAfter
  try {
    const output = git('log', '--grep=^chore(version):\\|^chore(release):\\|^chore:\\s*bump\\|^chore:\\s*release', '-n', '1', '--format=%H', targetAfter);
    if (output) return output;
  } catch {}

  // 2. Look for the most recent stable tag reachable from targetAfter
  try {
    const tags = git('tag', '--merged', targetAfter, '--sort=-version:refname').split('\n')
      .map(t => t.trim())
      .filter(t => /^v(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$/.test(t) && Number(t.slice(1).split('.')[0]) >= 8);
    if (tags[0]) {
      const tagCommit = git('rev-parse', `${tags[0]}^{commit}`);
      if (tagCommit) return tagCommit;
    }
  } catch {}

  // 3. Look for the most recent commit that changed the version in package.json
  try {
    const pkgVersionCommit = git('log', '-n', '1', '--format=%H', '-S', '"version"', targetAfter, '--', 'package.json');
    if (pkgVersionCommit) return pkgVersionCommit;
  } catch {}

  return null;
}

export function getVersionAtCommit(commitRef) {
  if (!commitRef) return null;
  try {
    const content = git('show', `${commitRef}:package.json`);
    const parsed = JSON.parse(content);
    return releaseVersion(parsed.version);
  } catch {
    return null;
  }
}

export function getCommitBumpType(message) {
  if (!message) return 0;
  const header = /^([a-z][a-z0-9-]*)(?:\([^\r\n()]+\))?(!)?:[ \t]+\S[^\r\n]*/i.exec(message);
  if (!header) return 0;
  const type = header[1].toLowerCase();
  if (header[2] || /^BREAKING[ -]CHANGE:[ \t]+\S/m.test(message)) return 3; // Major
  if (type === 'feat') return 2; // Minor
  if (['fix', 'perf', 'ref', 'refactor', 'ui'].includes(type)) return 1; // Patch
  return 0; // None (docs, chore, ci, deps, etc.)
}

export function formatCommitType(type) {
  switch (type) {
    case 3: return '🔴 MAJOR';
    case 2: return '🟡 MINOR';
    case 1: return '🟢 PATCH';
    default: return '⚪ IGNORED';
  }
}

export function resolveChronologicalVersion(baseVersion, commits) {
  let [major, minor, patch] = releaseVersion(baseVersion)
    .split('-')[0]
    .split('.')
    .map(v => BigInt(v));

  let hasBump = false;

  // Group commits by date (YYYY-MM-DD) or session
  const dateGroups = new Map();
  for (const commit of commits) {
    const message = typeof commit === 'string' ? commit : commit.message;
    if (isBotOrSkipCommit(message)) continue;
    const type = getCommitBumpType(message);
    if (type === 0) continue; // Skip non-bumpable commits like docs/chore

    const dateKey = (typeof commit === 'object' && commit.date) ? commit.date : 'default';
    if (!dateGroups.has(dateKey)) dateGroups.set(dateKey, []);
    dateGroups.get(dateKey).push({ message, type });
  }

  // Process each date's commits in chronological order
  for (const [, group] of dateGroups) {
    let dateHadMajor = false;
    let dateHadMinor = false;
    let dateHadPatchAfterMinorOrMajor = false;
    let dateHadPatchBefore = false;

    let sawMajor = false;
    let sawMinor = false;

    for (const commit of group) {
      if (commit.type === 3) {
        sawMajor = true;
        sawMinor = false;
        dateHadMajor = true;
        dateHadMinor = false;
        dateHadPatchAfterMinorOrMajor = false;
      } else if (commit.type === 2) {
        sawMinor = true;
        dateHadMinor = true;
        dateHadPatchAfterMinorOrMajor = false;
      } else if (commit.type === 1) {
        if (sawMajor || sawMinor) {
          dateHadPatchAfterMinorOrMajor = true;
        } else {
          dateHadPatchBefore = true;
        }
      }
    }

    if (dateHadMajor) {
      major += 1n;
      minor = 0n;
      patch = 0n;
      hasBump = true;
      if (dateHadPatchAfterMinorOrMajor) {
        patch += 1n;
      }
    } else if (dateHadMinor) {
      minor += 1n;
      patch = 0n;
      hasBump = true;
      if (dateHadPatchAfterMinorOrMajor) {
        patch += 1n;
      }
    } else if (dateHadPatchBefore) {
      patch += 1n;
      hasBump = true;
    }
  }

  if (!hasBump) return null;
  return `${major}.${minor}.${patch}`;
}

function parseLogOutput(raw) {
  if (!raw) return [];
  const entries = raw.split('\0').map(e => e.trim()).filter(Boolean);
  return entries.map(entry => {
    const parts = entry.split('\x1f');
    if (parts.length >= 4) {
      return {
        sha: parts[0],
        timestamp: Number(parts[1]) || 0,
        isoDate: parts[2],
        date: parts[2]?.slice(0, 10) || 'unknown',
        message: parts.slice(3).join('\x1f').trim(),
      };
    }
    return { sha: '', timestamp: 0, isoDate: '', date: 'unknown', message: entry };
  });
}

export async function collectCommitObjects(before, after, options = {}) {
  const isZeroSha = sha => !sha || /^0+$/.test(sha);
  const targetAfter = after || 'HEAD';

  // 1. Check for unbumped commits since the last version bump / change
  if (!options.strictPush) {
    const lastBump = findLastBumpCommit(targetAfter);
    if (lastBump && isAncestor(lastBump, targetAfter)) {
      const targetSha = git('rev-parse', `${targetAfter}^{commit}`);
      if (lastBump === targetSha) return [];

      const output = git('log', '--reverse', '--format=%H%x1f%ct%x1f%cI%x1f%B%x00', `${lastBump}..${targetAfter}`);
      const objects = parseLogOutput(output);
      if (objects.length > 0) return objects;
    }
  }

  // 2. If a valid before SHA is provided and is an ancestor of targetAfter
  if (before && !isZeroSha(before) && existsRef(before)) {
    if (isAncestor(before, targetAfter)) {
      const output = git('log', '--reverse', '--format=%H%x1f%ct%x1f%cI%x1f%B%x00', `${before}..${targetAfter}`);
      return parseLogOutput(output);
    }
  }

  // 3. If running in GitHub Actions with GITHUB_EVENT_PATH
  if (process.env.GITHUB_EVENT_PATH) {
    try {
      const event = JSON.parse(await text(process.env.GITHUB_EVENT_PATH));
      if (Array.isArray(event.commits) && event.commits.length > 0) {
        return event.commits.map(c => ({
          sha: c.id || '',
          timestamp: c.timestamp ? Math.floor(new Date(c.timestamp).getTime() / 1000) : 0,
          isoDate: c.timestamp || '',
          date: c.timestamp ? c.timestamp.slice(0, 10) : 'unknown',
          message: c.message?.trim() || '',
        }));
      }
    } catch {}
  }

  // 4. Fallback: inspect target commit
  try {
    const single = git('log', '-1', '--format=%H%x1f%ct%x1f%cI%x1f%B%x00', targetAfter);
    return parseLogOutput(single);
  } catch {
    return [];
  }
}

// Retain collectCommitMessages for backward compatibility
export async function collectCommitMessages(before, after, options = {}) {
  const objects = await collectCommitObjects(before, after, options);
  return objects.map(o => o.message);
}

export async function bumpVersionFiles(nextVersion) {
  for (const path of ['package.json', 'src-tauri/tauri.conf.json']) {
    const source = await text(path);
    await writeFile(path, source.replace(/("version"\s*:\s*")[^"]+(")/, (_, prefix, suffix) => `${prefix}${nextVersion}${suffix}`));
  }

  const cargoPaths = ['src-tauri/Cargo.toml', 'Cargo.lock'];
  try {
    await text('src-tauri/Cargo.lock');
    cargoPaths.push('src-tauri/Cargo.lock');
  } catch {}

  for (const path of cargoPaths) {
    const source = await text(path);
    await writeFile(path, replaceCargoVersion(source, 'auto-midi-player', nextVersion));
  }

  // Validate that all target files were updated
  for (const path of ['package.json', 'src-tauri/tauri.conf.json']) {
    if (JSON.parse(await text(path)).version !== nextVersion) {
      throw new Error(`Failed to update ${path} to ${nextVersion}`);
    }
  }
  for (const path of cargoPaths) {
    const source = await text(path);
    if (replaceCargoVersion(source, 'auto-midi-player', nextVersion) !== source) {
      throw new Error(`Failed to update ${path} to ${nextVersion}`);
    }
  }

  return ['package.json', 'src-tauri/tauri.conf.json', ...cargoPaths];
}

export function commitBump(nextVersion, files) {
  try { git('config', 'user.name'); } catch { git('config', 'user.name', 'github-actions[bot]'); }
  try { git('config', 'user.email'); } catch { git('config', 'user.email', '41898282+github-actions[bot]@users.noreply.github.com'); }

  git('add', '--', ...files);
  git('commit', '-m', `chore(version): bump to v${nextVersion} [skip ci]`);
}

export function pushWithRetry(branch, retries = 3) {
  for (let attempt = 1; attempt <= retries; attempt++) {
    try {
      git('push', 'origin', `HEAD:refs/heads/${branch}`);
      return;
    } catch (err) {
      if (attempt === retries) throw err;
      console.warn(`Push failed (attempt ${attempt}/${retries}), rebasing on origin/${branch}...`);
      git('fetch', 'origin', branch);
      git('rebase', `origin/${branch}`);
    }
  }
}

export async function saveSummary(data) {
  try {
    await writeFile(SUMMARY_FILE, JSON.stringify(data, null, 2), 'utf8');
  } catch (err) {
    console.warn('Warning: Could not save summary state:', err.message);
  }
}

export async function loadSummary() {
  try {
    if (!existsSync(SUMMARY_FILE)) return null;
    return JSON.parse(await readFile(SUMMARY_FILE, 'utf8'));
  } catch {
    return null;
  }
}

export async function analyzeStep(options, before, after) {
  const headMsg = process.env.HEAD_COMMIT_MESSAGE;
  const headAuthor = process.env.HEAD_COMMIT_AUTHOR;

  console.log('='.repeat(80));
  console.log('🔍 [STEP] Analyzing Push & Commits for Version Bump');
  console.log('='.repeat(80));

  const branch = process.env.BRANCH_NAME || git('branch', '--show-current') || 'rewrite-experimental';
  console.log(`Branch:                  ${branch}`);
  console.log(`Before SHA:              ${before || '(initial)'}`);
  console.log(`After SHA:               ${after || 'HEAD'}`);

  if ((headMsg && isBotOrSkipCommit(headMsg)) || (headAuthor && headAuthor.toLowerCase().includes('github-actions'))) {
    console.log(`ℹ️ Bot or [skip ci] commit detected on HEAD: "${headMsg ? headMsg.split('\n')[0] : ''}" by ${headAuthor || 'bot'}`);
    console.log(`ℹ️ Skipping version bump evaluation to prevent automated loops.`);
    if (process.env.GITHUB_OUTPUT) {
      await appendFile(process.env.GITHUB_OUTPUT, `bumped=false\nreason=bot_or_skip_ci\n`);
    }
    await saveSummary({
      branch,
      status: 'skipped',
      reason: 'Automated bot commit or [skip ci] marker detected',
      commits: [],
    });
    return { bumped: false, reason: 'bot_or_skip_ci' };
  }

  const commits = await collectCommitObjects(before, after, options);
  const candidateCommits = commits.filter(c => !isBotOrSkipCommit(c.message));

  const pkg = JSON.parse(await text('package.json'));
  const currentVersion = releaseVersion(pkg.version);

  const targetAfter = after || 'HEAD';
  const lastBump = findLastBumpCommit(targetAfter);
  const baselineVersion = (lastBump && isAncestor(lastBump, targetAfter))
    ? (getVersionAtCommit(lastBump) || currentVersion)
    : currentVersion;

  console.log(`Current version:         ${currentVersion}`);
  console.log(`Baseline version:        ${baselineVersion} (at ${lastBump ? lastBump.slice(0, 7) : 'repo baseline'})`);
  console.log(`Total commits in range:  ${commits.length}`);
  console.log(`Candidate commits:       ${candidateCommits.length}`);
  console.log('-'.repeat(80));
  console.log('📋 Commit Evaluation Audit:');

  const auditList = [];
  for (let i = 0; i < commits.length; i++) {
    const c = commits[i];
    const isBot = isBotOrSkipCommit(c.message);
    const bumpType = isBot ? 0 : getCommitBumpType(c.message);
    const typeLabel = isBot ? '⚪ BOT/SKIP' : formatCommitType(bumpType);
    const shortSha = c.sha ? c.sha.slice(0, 7) : '       ';
    const date = c.date || 'unknown   ';
    const firstLine = (c.message || '').split('\n')[0].slice(0, 60);
    console.log(`  [${i + 1}/${commits.length}] ${shortSha} ${date} | ${typeLabel.padEnd(12)} | ${firstLine}`);
    auditList.push({
      index: i + 1,
      sha: shortSha,
      date,
      typeLabel,
      message: firstLine,
    });
  }
  console.log('-'.repeat(80));

  if (candidateCommits.length === 0) {
    if (currentVersion !== baselineVersion) {
      console.log(`Force-push / commit change detected: restoring version ${currentVersion} -> ${baselineVersion}`);
      if (process.env.GITHUB_OUTPUT) {
        await appendFile(process.env.GITHUB_OUTPUT, `bumped=true\ntarget_version=${baselineVersion}\nprevious_version=${currentVersion}\nreason=force_push_restore\n`);
      }
      await saveSummary({
        branch,
        status: 'bumped',
        previousVersion: currentVersion,
        targetVersion: baselineVersion,
        reason: 'Force-push / commit rewrite restored baseline version',
        commits: auditList,
      });
      return { bumped: true, version: baselineVersion, previousVersion: currentVersion };
    }

    console.log(`No eligible commits to evaluate (no version bump required) across ${commits.length} pushed commit(s). Current version: ${currentVersion}`);
    if (process.env.GITHUB_OUTPUT) {
      await appendFile(process.env.GITHUB_OUTPUT, `bumped=false\ntarget_version=${currentVersion}\nprevious_version=${currentVersion}\nreason=no_eligible_commits\n`);
    }
    await saveSummary({
      branch,
      status: 'no_bump',
      currentVersion,
      reason: 'All commits are maintenance or non-bumpable (e.g. docs, chore)',
      commits: auditList,
    });
    return { bumped: false, version: currentVersion, previousVersion: currentVersion };
  }

  const targetVersion = resolveChronologicalVersion(baselineVersion, candidateCommits) || baselineVersion;
  if (targetVersion === currentVersion) {
    console.log(`Version is already at ${targetVersion}. No version bump required for ${candidateCommits.length} commit(s).`);
    if (process.env.GITHUB_OUTPUT) {
      await appendFile(process.env.GITHUB_OUTPUT, `bumped=false\ntarget_version=${currentVersion}\nprevious_version=${currentVersion}\nreason=already_at_target\n`);
    }
    await saveSummary({
      branch,
      status: 'no_bump',
      currentVersion,
      reason: `Version is already up to date at ${currentVersion}`,
      commits: auditList,
    });
    return { bumped: false, version: currentVersion, previousVersion: currentVersion };
  }

  console.log(`Bumping version: ${currentVersion} -> ${targetVersion} (from ${candidateCommits.length} commit(s))`);
  if (process.env.GITHUB_OUTPUT) {
    await appendFile(process.env.GITHUB_OUTPUT, `bumped=true\ntarget_version=${targetVersion}\nprevious_version=${currentVersion}\ncommit_count=${candidateCommits.length}\nreason=bump_required\n`);
  }
  await saveSummary({
    branch,
    status: 'bumped',
    previousVersion: currentVersion,
    targetVersion,
    reason: `Conventional bump from ${candidateCommits.length} commit(s)`,
    commits: auditList,
  });

  return { bumped: true, version: targetVersion, previousVersion: currentVersion };
}

export async function applyStep(targetVersion) {
  let ver = targetVersion;
  if (!ver) {
    const summary = await loadSummary();
    ver = summary?.targetVersion;
  }
  if (!ver) {
    throw new Error('Target version must be specified for apply step');
  }

  console.log('='.repeat(80));
  console.log(`✏️ [STEP] Applying Version Bump to Project Files: v${ver}`);
  console.log('='.repeat(80));

  const updatedFiles = await bumpVersionFiles(ver);
  for (const file of updatedFiles) {
    console.log(`  ✓ Updated ${file} -> ${ver}`);
  }
  console.log(`Verification: All ${updatedFiles.length} version files successfully synchronized to v${ver}.`);
  return updatedFiles;
}

export async function pushStep(targetVersion, options = {}) {
  let ver = targetVersion;
  if (!ver) {
    const summary = await loadSummary();
    ver = summary?.targetVersion;
  }
  if (!ver) {
    const pkg = JSON.parse(await text('package.json'));
    ver = releaseVersion(pkg.version);
  }

  console.log('='.repeat(80));
  console.log(`🚀 [STEP] Committing and Pushing Version Bump: v${ver}`);
  console.log('='.repeat(80));

  const files = ['package.json', 'src-tauri/tauri.conf.json', 'src-tauri/Cargo.toml', 'Cargo.lock'];
  try {
    await text('src-tauri/Cargo.lock');
    files.push('src-tauri/Cargo.lock');
  } catch {}

  commitBump(ver, files);
  console.log(`Committed: chore(version): bump to v${ver} [skip ci]`);

  if (options.push || options.command === 'push') {
    const branch = process.env.BRANCH_NAME || git('branch', '--show-current');
    if (!branch || branch.startsWith('-')) throw new Error('Valid branch name required for push');

    console.log(`  Pushing to origin/${branch}...`);
    pushWithRetry(branch);
    console.log(`  ✓ Pushed to origin/${branch}`);
  }
}

export async function summaryStep() {
  const summary = await loadSummary();
  if (!summary) {
    console.log('No version bump summary data available.');
    return;
  }

  console.log('='.repeat(80));
  console.log('📊 [SUMMARY] Version Bumper Execution Summary');
  console.log('='.repeat(80));

  const escapeMarkdownTableCell = (value) => String(value)
    .replace(/\\/g, '\\\\')
    .replace(/\|/g, '\\|');

  let md = `## 🏷️ Auto Version Bumper Summary\n\n`;
  md += `| Property | Value |\n`;
  md += `| :--- | :--- |\n`;
  md += `| **Branch** | \`${summary.branch || 'unknown'}\` |\n`;

  if (summary.status === 'bumped') {
    md += `| **Version Transition** | \`${summary.previousVersion}\` → **\`${summary.targetVersion}\`** |\n`;
    md += `| **Status** | 🚀 **Bump Applied & Pushed** |\n`;
  } else if (summary.status === 'skipped') {
    md += `| **Status** | ⏩ **Skipped** |\n`;
  } else {
    md += `| **Current Version** | \`${summary.currentVersion}\` |\n`;
    md += `| **Status** | ℹ️ **No Bump Required** |\n`;
  }
  md += `| **Reason** | ${summary.reason || 'N/A'} |\n`;
  md += `| **Commits Checked** | ${summary.commits?.length || 0} |\n\n`;

  if (summary.commits && summary.commits.length > 0) {
    md += `### 📋 Commit Evaluation Audit\n\n`;
    md += `| # | Commit | Date | Classification | Description |\n`;
    md += `| :-: | :---: | :---: | :---: | :--- |\n`;
    for (const c of summary.commits) {
      md += `| ${c.index} | \`${c.sha}\` | ${c.date} | ${c.typeLabel} | ${escapeMarkdownTableCell(c.message)} |\n`;
    }
  }

  console.log(md);

  if (process.env.GITHUB_STEP_SUMMARY) {
    await appendFile(process.env.GITHUB_STEP_SUMMARY, `${md}\n`);
    console.log('✓ Successfully wrote summary to $GITHUB_STEP_SUMMARY');
  }

  try {
    if (existsSync(SUMMARY_FILE)) {
      await unlink(SUMMARY_FILE);
    }
  } catch {}
}

export async function main(argv = process.argv.slice(2)) {
  const options = parseCliArgs(argv);
  const before = options.before || process.env.BEFORE_SHA || null;
  const after = options.after || process.env.AFTER_SHA || null;

  if (options.command === 'analyze') {
    return await analyzeStep(options, before, after);
  }

  if (options.command === 'apply') {
    return await applyStep(options.targetVersion);
  }

  if (options.command === 'commit' || options.command === 'push') {
    return await pushStep(options.targetVersion, options);
  }

  if (options.command === 'summary') {
    return await summaryStep();
  }

  // Unified flow (for backward compatibility and CLI execution)
  const result = await analyzeStep(options, before, after);

  if (result.bumped) {
    if (options.dryRun) {
      console.log(`Dry-run enabled. Skipping file changes and commit.`);
      if (process.env.GITHUB_OUTPUT) {
        await appendFile(process.env.GITHUB_OUTPUT, `bumped=true\nversion=${result.version}\nprevious_version=${result.previousVersion}\n`);
      }
      await summaryStep();
      return { bumped: true, version: result.version, previousVersion: result.previousVersion, dryRun: true };
    }

    const updatedFiles = await applyStep(result.version);
    if (options.commit) {
      await pushStep(result.version, options);
    }
  }

  if (process.env.GITHUB_OUTPUT) {
    await appendFile(process.env.GITHUB_OUTPUT, `bumped=${result.bumped}\nversion=${result.version || result.currentVersion}\nprevious_version=${result.previousVersion || result.currentVersion}\n`);
  }

  await summaryStep();
  return result;
}

if (process.argv[1] && fileURLToPath(import.meta.url) === process.argv[1]) {
  main().catch(err => {
    console.error(err);
    process.exit(1);
  });
}
