import { readFile, writeFile, appendFile, mkdir } from 'node:fs/promises';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { releaseVersion, replaceCargoVersion, renderNotes } from './release-lib.mjs';

process.chdir(fileURLToPath(new URL('../', import.meta.url)));
const run = (command, args) => execFileSync(command, args, { encoding: 'utf8', stdio: ['ignore', 'pipe', 'inherit'] }).trim();
const git = (...args) => run('git', args);
const existsRef = ref => { try { return git('rev-parse', '--verify', ref); } catch { return null; } };
const text = path => readFile(path, 'utf8');
const [command, input] = process.argv.slice(2);
const version = releaseVersion(input ?? JSON.parse(await text('package.json')).version);
const tag = `v${version}`;
const repo = process.env.GITHUB_REPOSITORY || 'Jed556/AutoMidiPlayer';
if (!/^[\w.-]+\/[\w.-]+$/.test(repo)) throw new Error('Invalid GitHub repository');

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
  const previous = git('tag', '--merged', 'HEAD', '--sort=-version:refname')
    .split('\n').find(value => /^v\d+\.\d+\.\d+$/.test(value) && value !== tag);
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

if (command === 'preview') {
  process.stdout.write(await notes());
} else if (command === 'validate') {
  await validate();
  console.log(`Validated ${tag}`);
} else if (command === 'prepare') {
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
} else {
  throw new Error('Usage: node scripts/release.mjs <preview|prepare|validate> [version]');
}
