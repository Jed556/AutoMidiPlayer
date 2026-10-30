import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, mkdtempSync, mkdirSync, writeFileSync, cpSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { pathToFileURL } from 'node:url';
import { execFileSync, spawnSync } from 'node:child_process';
import { generateKeyPairSync, sign, createHash } from 'node:crypto';
import { releaseVersion, nextReleaseVersion, replaceCargoVersion, renderNotes, mergeManifests, platforms, sha256, verifyUpdaterSignature } from '../scripts/release-lib.mjs';

test('release versions reject ambiguous tags, legacy versions and command-like input', () => {
  for (const value of ['8.1.0', 'v8.1.0', '8.1.0-beta.2']) assert.equal(releaseVersion(value), value.replace(/^v/, ''));
  for (const value of ['7.15.0', '8.01.0', '8.0.0-01', '8.0.0+build', '8.1', '--help', '8.0.0;echo bad']) {
    assert.throws(() => releaseVersion(value));
  }
});

test('automatic versions select the largest conventional bump and reset lower version components', () => {
  const cases = [
    [['fix: repair playback'], '8.1.3'],
    [['fix(playback): restore held notes (#42)'], '8.1.3'],
    [['perf: reduce latency'], '8.1.3'],
    [['ref: simplify playback'], '8.1.3'],
    [['refactor(core): simplify playback'], '8.1.3'],
    [['ui: improve settings'], '8.1.3'],
    [['feat: add instrument'], '8.2.0'],
    [['feat(games): add instrument', 'fix: repair playback'], '8.2.0'],
    [['fix: repair playback', 'feat(games): add instrument'], '8.2.0'],
    [['fix!: replace timing API'], '9.0.0'],
    [['feat(core)!: replace timing API'], '9.0.0'],
    [['chore!: remove obsolete support', 'feat: add instrument'], '9.0.0'],
    [['feat: add instrument', 'fix(core)!: replace timing API', 'fix: repair playback'], '9.0.0'],
    [['refactor: replace API\n\nBREAKING CHANGE: callers must pass a device'], '9.0.0'],
    [['docs: change supported platforms\r\n\r\nBREAKING-CHANGE: remove an obsolete platform'], '9.0.0'],
    [[], null],
    [['docs: update readme', 'chore: maintenance', 'deps: update dependencies', 'ci: update workflow', 'test: cover playback'], null],
    [['Merge pull request #42\n\nfeat: add instrument'], null],
    [['chore: document the feat: and fix: prefixes'], null],
    [['feat: ', 'unprefixed message', 'fix this: message'], null],
    [['fix: explain BREAKING CHANGE: in a tooltip'], '8.1.3'],
  ];
  for (const [messages, expected] of cases) {
    assert.equal(nextReleaseVersion('v8.1.2', messages), expected, JSON.stringify(messages));
  }
  assert.equal(nextReleaseVersion('8.1.9007199254740992', ['fix: repair playback']), '8.1.9007199254740993');
});

function releaseRepository(t, version = '8.1.2') {
  const directory = mkdtempSync(join(tmpdir(), 'amp-auto-release-test-'));
  t.after(() => {
    assert.ok(resolve(directory).startsWith(`${resolve(tmpdir())}\\amp-auto-release-test-`) || resolve(directory).startsWith(`${resolve(tmpdir())}/amp-auto-release-test-`));
    rmSync(directory, { recursive: true, force: true });
  });
  mkdirSync(join(directory, 'scripts'));
  mkdirSync(join(directory, 'src-tauri'));
  mkdirSync(join(directory, 'dist'));
  for (const name of ['release.mjs', 'release-lib.mjs']) cpSync(new URL(`../scripts/${name}`, import.meta.url), join(directory, 'scripts', name));
  writeFileSync(join(directory, 'scripts/generate-licenses.mjs'), "import { writeFileSync } from 'node:fs'; writeFileSync('THIRD-PARTY-LICENSES.md', '# Test dependency notices\\n');");
  writeFileSync(join(directory, '.gitignore'), 'dist/\n');
  writeFileSync(join(directory, 'package.json'), JSON.stringify({ version }));
  writeFileSync(join(directory, 'src-tauri/tauri.conf.json'), JSON.stringify({ version }));
  writeFileSync(join(directory, 'src-tauri/Cargo.toml'), `[package]\nname = "auto-midi-player"\nversion = "${version}"\n`);
  writeFileSync(join(directory, 'Cargo.lock'), `version = 4\n\n[[package]]\nname = "amp-core"\nversion = "0.1.0"\n\n[[package]]\nname = "auto-midi-player"\nversion = "${version}"\n`);
  writeFileSync(join(directory, 'RELEASE.md'), `# Release v${version}\n\n<!-- release-prepared: v${version} -->\n`);
  writeFileSync(join(directory, 'THIRD-PARTY-LICENSES.md'), '# Original notices\n');
  const git = (...args) => execFileSync('git', args, { cwd: directory, encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] }).trim();
  git('init', '-b', 'main'); git('config', 'user.name', 'Test'); git('config', 'user.email', 'test@example.com');
  git('add', '.'); git('commit', '-m', 'feat: initial rewrite');
  const runWithEnv = (env, ...args) => spawnSync(process.execPath, ['scripts/release.mjs', ...args], {
    cwd: directory, encoding: 'utf8', env: {
      ...process.env, GITHUB_TOKEN: '', GITHUB_REPOSITORY: 'example/app', BUILD_ONLY: 'true', SOURCE_BRANCH: 'main',
      GITHUB_OUTPUT: join(directory, 'dist/github-output'), RELEASE_LICENSE_MANIFEST: '',
      BETA_RELEASE: '', ...env,
    },
  });
  const run = (...args) => runWithEnv({}, ...args);
  const beta = (...args) => runWithEnv({ BETA_RELEASE: 'true' }, ...args);
  return { directory, git, run, beta };
}

test('first automatic rewrite release uses the configured version and ignores legacy and unrelated tags', t => {
  const { git, run } = releaseRepository(t, '8.0.0-beta.2');
  git('tag', 'v7.15.0');
  git('tag', 'v99.0.0', git('commit-tree', 'HEAD^{tree}', '-m', 'Unrelated history'));
  git('commit', '--allow-empty', '-m', 'feat!: initial rewrite API');
  for (const args of [['next-version'], ['next-version', ''], ['next-version', 'auto']]) {
    const result = run(...args);
    assert.equal(result.status, 0, result.stderr);
    assert.equal(result.stdout.trim(), '8.0.0');
  }
  assert.equal(git('status', '--porcelain'), '');
});

test('automatic version uses only unreleased commits and keeps beta commits in the stable range', t => {
  const { git, run } = releaseRepository(t);
  git('tag', 'v8.1.2');
  git('tag', 'v99.0.0', git('commit-tree', 'HEAD^{tree}', '-m', 'Unrelated history'));
  const check = version => {
    const result = run('next-version');
    assert.equal(result.status, 0, result.stderr);
    assert.equal(result.stdout.trim(), version);
  };
  check('8.1.2');
  git('commit', '--allow-empty', '-m', 'fix(playback): restore held notes');
  check('8.1.3');
  git('commit', '--allow-empty', '-m', 'feat(settings): expose re-press gap');
  git('tag', 'v8.2.0-beta.1');
  git('commit', '--allow-empty', '-m', 'fix: beta regression');
  check('8.2.0');
  git('commit', '--allow-empty', '-m', 'refactor(core): replace timing API', '-m', 'BREAKING CHANGE: callers must pass a device');
  check('9.0.0');
  assert.equal(git('status', '--porcelain'), '');
});

test('stable baseline is ordered numerically and ignores malformed stable tags', t => {
  const { git, run } = releaseRepository(t);
  git('tag', 'v8.9.9');
  git('commit', '--allow-empty', '-m', 'feat!: already released breaking change');
  git('tag', 'v8.10.0');
  git('commit', '--allow-empty', '-m', 'fix: latest patch');
  git('tag', 'v09.0.0');
  const result = run('next-version');
  assert.equal(result.status, 0, result.stderr);
  assert.equal(result.stdout.trim(), '8.10.1');
});

test('beta checkbox increments counters, reuses tagged commits, and promotes the same target to stable', t => {
  const { git, run, beta } = releaseRepository(t);
  git('tag', 'v8.1.2');
  const released = beta('next-version');
  assert.notEqual(released.status, 0);
  assert.match(released.stderr, /No unreleased changes after stable release/);
  git('commit', '--allow-empty', '-m', 'feat: add instrument');
  const check = (version, command = beta) => {
    const result = command('next-version');
    assert.equal(result.status, 0, result.stderr);
    assert.equal(result.stdout.trim(), version);
  };
  check('8.2.0-beta.1');
  git('tag', 'v8.2.0-beta.1');
  check('8.2.0-beta.1');
  git('commit', '--allow-empty', '-m', 'fix: beta regression');
  check('8.2.0-beta.2');
  git('tag', 'v8.2.0-beta.2');
  check('8.2.0', run);
  // Tags on other branches still reserve their counters to prevent collisions.
  const unrelated = git('commit-tree', 'HEAD^{tree}', '-m', 'Other branch');
  for (const tag of ['v8.2.0-beta.9', 'v8.2.0-beta.10', 'v8.2.0-beta.011', 'v8.2.0-beta.99.extra']) git('tag', tag, unrelated);
  check('8.2.0-beta.2');
  git('commit', '--allow-empty', '-m', 'ui: improve beta layout');
  check('8.2.0-beta.11');
  git('commit', '--allow-empty', '-m', 'feat!: replace device API');
  check('9.0.0-beta.1');
});

test('first beta release, explicit overrides and validation respect the beta checkbox', t => {
  const { git, run, beta } = releaseRepository(t, '8.0.0');
  const check = (version, ...args) => {
    const result = beta('next-version', ...args);
    assert.equal(result.status, 0, result.stderr);
    assert.equal(result.stdout.trim(), version);
  };
  check('8.0.0-beta.1');
  git('tag', 'v8.0.0-beta.1');
  check('8.0.0-beta.1');
  git('commit', '--allow-empty', '-m', 'fix: initial beta regression');
  check('8.0.0-beta.2');
  check('8.3.0-beta.1', '8.3.0');
  check('8.3.0-beta.5', '8.3.0-beta.5');
  assert.notEqual(beta('next-version', '8.3.0-rc.1').status, 0);
  const rc = run('next-version', '8.3.0-rc.1');
  assert.equal(rc.status, 0, rc.stderr);
  assert.equal(rc.stdout.trim(), '8.3.0-rc.1');
  assert.equal(beta('validate', '8.0.0').status, 0, 'Validation must not reinterpret an existing tag as a beta');
  assert.equal(git('status', '--porcelain'), '');
});

test('beta retries at the original source commit reuse the bot release tag', t => {
  const { git, beta } = releaseRepository(t);
  git('tag', 'v8.1.2');
  git('commit', '--allow-empty', '-m', 'feat: add instrument');
  const source = git('rev-parse', 'HEAD');
  git('commit', '--allow-empty', '-m', 'chore(release): v8.2.0-beta.1');
  git('tag', '-a', 'v8.2.0-beta.1', '-m', 'Release v8.2.0-beta.1');
  git('checkout', '--detach', source);
  const result = beta('next-version');
  assert.equal(result.status, 0, result.stderr);
  assert.equal(result.stdout.trim(), '8.2.0-beta.1');
});

test('maintenance-only history stops automatic preparation before changing files and allows an override', t => {
  const { git, run } = releaseRepository(t);
  git('tag', 'v8.1.2');
  git('commit', '--allow-empty', '-m', 'docs: update readme');
  const result = run('prepare', 'auto');
  assert.notEqual(result.status, 0);
  assert.match(result.stderr, /No release-worthy commits since v8\.1\.2/);
  assert.equal(git('status', '--porcelain'), '');
  const override = run('next-version', '8.2.0-beta.1');
  assert.equal(override.status, 0, override.stderr);
  assert.equal(override.stdout.trim(), '8.2.0-beta.1');
  assert.equal(run('validate').status, 0, 'Validation without a version must use package.json, not infer a release');
});

for (const isBeta of [false, true]) test(`build-only automatic ${isBeta ? 'beta' : 'stable'} preparation synchronizes files and outputs, and tagged reruns reuse the release`, t => {
  const { directory, git, run, beta } = releaseRepository(t);
  const version = isBeta ? '8.2.0-beta.1' : '8.2.0';
  // Stub only git-cliff's external process; exercise version selection, Git,
  // synchronization and validation through the real release CLI.
  writeFileSync(join(directory, 'dist/mock-cliff.mjs'), `
    import childProcess from 'node:child_process';
    import { syncBuiltinESMExports } from 'node:module';
    const original = childProcess.execFileSync;
    childProcess.execFileSync = (command, args, options) => {
      if (command === process.env.GIT_CLIFF_BIN) return JSON.stringify([{ version: args[args.indexOf('--tag') + 1], commits: [] }]);
      return original(command, args, options);
    };
    syncBuiltinESMExports();
  `);
  git('tag', 'v8.1.2');
  git('commit', '--allow-empty', '-m', 'feat: expose re-press gap');
  const sha = git('rev-parse', 'HEAD');
  const result = spawnSync(process.execPath, ['--import', pathToFileURL(join(directory, 'dist/mock-cliff.mjs')).href, 'scripts/release.mjs', 'prepare'], {
    cwd: directory, encoding: 'utf8', env: {
      ...process.env, BUILD_ONLY: 'true', SOURCE_BRANCH: 'main', GITHUB_TOKEN: '', GITHUB_REPOSITORY: 'example/app',
      GITHUB_OUTPUT: join(directory, 'dist/github-output'), RELEASE_LICENSE_MANIFEST: '', GIT_CLIFF_BIN: 'mock-git-cliff',
      BETA_RELEASE: String(isBeta),
    },
  });
  assert.equal(result.status, 0, result.stderr);
  assert.ok(result.stdout.includes(`Automatically selected v${version}`));
  for (const path of ['package.json', 'src-tauri/tauri.conf.json']) {
    assert.equal(JSON.parse(readFileSync(join(directory, path))).version, version);
  }
  for (const path of ['src-tauri/Cargo.toml', 'Cargo.lock']) {
    assert.ok(readFileSync(join(directory, path), 'utf8').includes(`name = "auto-midi-player"\nversion = "${version}"`));
  }
  assert.match(readFileSync(join(directory, 'Cargo.lock'), 'utf8'), /name = "amp-core"\nversion = "0\.1\.0"/);
  assert.equal(run('validate', version).status, 0);
  assert.equal(git('rev-parse', 'HEAD'), sha);
  assert.equal(git('tag'), 'v8.1.2');
  const output = readFileSync(join(directory, 'dist/github-output'), 'utf8');
  assert.ok(output.includes(`version=${version}\ntag=v${version}\nsha=`));
  assert.ok(output.includes(`prerelease=${isBeta}\n`));
  for (const path of ['package.json', 'src-tauri/tauri.conf.json', 'src-tauri/Cargo.toml', 'Cargo.lock', 'RELEASE.md', 'THIRD-PARTY-LICENSES.md']) {
    assert.equal(readFileSync(join(directory, 'dist/release-input', path), 'utf8'), readFileSync(join(directory, path), 'utf8'));
  }
  git('add', '.'); git('commit', '-m', `chore(release): v${version}`); git('tag', `v${version}`);
  git('update-ref', 'refs/remotes/origin/main', 'HEAD');
  const releasedSha = git('rev-parse', 'HEAD');
  const rerun = (isBeta ? beta : run)('prepare', 'auto');
  assert.equal(rerun.status, 0, rerun.stderr);
  assert.equal(git('rev-parse', 'HEAD'), releasedSha);
  assert.equal(git('status', '--porcelain'), '');
});

test('version synchronization changes only the desktop Cargo package', () => {
  const source = '[[package]]\nname = "amp-core"\nversion = "0.1.0"\n\n[[package]]\nname = "auto-midi-player"\nversion = "8.0.0"\ndependencies = []\n';
  assert.equal(replaceCargoVersion(source, 'auto-midi-player', '8.1.0'), source.replace('"8.0.0"', '"8.1.0"'));
  assert.throws(() => replaceCargoVersion(source, 'missing', '8.1.0'));
});

test('notes omit empty groups and maintenance, retain breaking changes and deduplicate new humans', () => {
  const notes = renderNotes({ commits: [
    { id: 'abcdef123456', group: "What's New?", message: 'New instrument', scope: 'games', breaking: true },
    { id: 'def123456789', group: 'Hidden', message: 'Update documentation' },
    { id: '123456abcdef', group: 'Bug Fixes', message: 'Repair playback', remote: { pr_number: 42 } },
  ], github: { contributors: [
    { username: 'new-user', is_first_time: true, pr_number: 42 },
    { username: 'new-user', is_first_time: true, pr_number: 42 },
    { username: 'existing', is_first_time: false },
    { username: 'dependabot[bot]', is_first_time: true },
  ] } }, '8.1.0', 'Jed556/AutoMidiPlayer', 'v8.0.0');
  assert.match(notes, /## What's New\?/);
  assert.match(notes, /\*\*Breaking:\*\* \*\*games:\*\*/);
  assert.match(notes, /pull\/42/);
  assert.equal(notes.match(/@new-user/g).length, 1);
  assert.doesNotMatch(notes, /## Changes|Update documentation|@existing|dependabot/);
  assert.match(notes, /compare\/v8.0.0\.\.\.v8.1.0/);
  const first = renderNotes({}, '8.0.0', 'Jed556/AutoMidiPlayer');
  assert.doesNotMatch(first, /## /);
  assert.match(first, /commits\/v8.0.0/);
});

function fixtures() {
  const assets = new Set();
  const manifests = platforms.map(platform => {
    const file = `${platform}.bin`;
    assets.add(file);
    return { version: '8.1.0', platforms: { [platform]: { signature: 'signed', url: `https://github.com/Jed556/AutoMidiPlayer/releases/download/v8.1.0/${file}` } } };
  });
  return { assets, manifests };
}

test('merged updater manifest requires every target, correct versions, unique platforms and existing assets', () => {
  const { assets, manifests } = fixtures();
  const merge = values => mergeManifests(values, '8.1.0', 'Jed556/AutoMidiPlayer', assets);
  assert.deepEqual(Object.keys(merge(manifests).platforms), platforms);
  assert.throws(() => merge(manifests.slice(1)), /Missing updater/);
  assert.throws(() => merge([...manifests, manifests[0]]), /Duplicate/);
  assert.throws(() => merge([{ ...manifests[0], version: '8.0.0' }]), /version mismatch/);
  const entry = manifests[0].platforms[platforms[0]];
  entry.url = entry.url.replace('github.com', 'evil.example');
  assert.throws(() => merge(manifests), /Invalid updater/);
  assert.equal(sha256('abc'), 'ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad');
});

test('updater verification rejects corrupted packages, wrong keys and altered trusted comments', () => {
  const { privateKey, publicKey } = generateKeyPairSync('ed25519');
  const identifier = Buffer.from('0102030405060708', 'hex');
  const publicBytes = publicKey.export({ format: 'der', type: 'spki' }).subarray(-32);
  const key = Buffer.from(`untrusted comment: public key\n${Buffer.concat([Buffer.from('Ed'), identifier, publicBytes]).toString('base64')}\n`).toString('base64');
  const bytes = Buffer.from('signed updater package');
  const signature = sign(null, createHash('blake2b512').update(bytes).digest(), privateKey);
  const packet = Buffer.concat([Buffer.from('ED'), identifier, signature]);
  const trustedComment = 'timestamp:1793118000\tfile:installer.exe';
  const globalSignature = sign(null, Buffer.concat([signature, Buffer.from(trustedComment)]), privateKey);
  const text = `untrusted comment: signature\n${packet.toString('base64')}\ntrusted comment: ${trustedComment}\n${globalSignature.toString('base64')}\n`;
  const encoded = Buffer.from(text).toString('base64');
  verifyUpdaterSignature(bytes, encoded, key);
  assert.throws(() => verifyUpdaterSignature(Buffer.from('tampered'), encoded, key), /does not match/);
  assert.throws(() => verifyUpdaterSignature(bytes, Buffer.from(text.replace('installer.exe', 'different.exe')).toString('base64'), key), /does not match/);
  const wrong = Buffer.from(`untrusted comment: public key\n${Buffer.concat([Buffer.from('Ed'), Buffer.alloc(8), publicBytes]).toString('base64')}\n`).toString('base64');
  assert.throws(() => verifyUpdaterSignature(bytes, encoded, wrong), /Invalid minisign/);
});

for (const mode of ['new', 'draft', 'published', 'upload-fails', 'new-prerelease', 'draft-prerelease']) {
  test(`publication handles ${mode} releases without overwriting published notes`, () => {
    const directory = mkdtempSync(join(tmpdir(), 'amp-release-test-'));
    try {
      mkdirSync(join(directory, 'dist/release-assets'), { recursive: true });
      writeFileSync(join(directory, 'RELEASE.md'), 'Prepared release notes');
      writeFileSync(join(directory, 'dist/release-assets/example.zip'), 'package');
      writeFileSync(join(directory, 'mock.mjs'), `
        import { writeFileSync } from 'node:fs';
        const mode = process.env.TEST_RELEASE_MODE;
        const calls = [];
        const release = { id: 1, draft: mode !== 'published', assets: [], body: 'Edited draft notes', upload_url: 'https://uploads.github.com/repos/example/app/releases/1/assets{?name}', html_url: 'https://github.com/example/app/releases/tag/v8.1.0' };
        process.on('exit', () => writeFileSync('calls.json', JSON.stringify(calls)));
        globalThis.fetch = async (input, options = {}) => {
          const url = new URL(input);
          const method = options.method || 'GET';
          const body = typeof options.body === 'string' ? JSON.parse(options.body) : null;
          calls.push({ path: url.pathname, method, body });
          if (url.pathname.includes('/releases/tags/') && ['new', 'upload-fails', 'new-prerelease'].includes(mode)) return new Response('{}', { status: 404 });
          if (url.hostname === 'uploads.github.com') {
            if (mode === 'upload-fails') return new Response('failed', { status: 500 });
            release.assets.push({ name: url.searchParams.get('name'), size: options.body.length });
          }
          return new Response(JSON.stringify(release), { status: 200 });
        };
      `);
      const version = mode.includes('prerelease') ? '8.1.0-beta.1' : '8.1.0';
      const result = spawnSync(process.execPath, ['--import', pathToFileURL(join(directory, 'mock.mjs')).href, resolve('scripts/publish-release.mjs'), version], {
        cwd: directory, encoding: 'utf8', env: { ...process.env, GITHUB_TOKEN: 'test-only', GITHUB_REPOSITORY: 'example/app', RELEASE_SHA: 'a'.repeat(40), TEST_RELEASE_MODE: mode },
      });
      const calls = JSON.parse(readFileSync(join(directory, 'calls.json'), 'utf8'));
      if (mode === 'upload-fails') {
        assert.notEqual(result.status, 0);
        assert.ok(!calls.some(call => call.method === 'PATCH'), 'Failed uploads must leave the release in draft');
        return;
      }
      assert.equal(result.status, 0, result.stderr);
      const reactions = calls.filter(call => call.path.endsWith('/reactions'));
      assert.deepEqual(reactions.map(call => call.body.content), ['heart', 'hooray']);
      if (mode === 'published') assert.ok(calls.every(call => call.method === 'GET' || call.path.endsWith('/reactions')));
      else {
        const publication = calls.find(call => call.method === 'PATCH');
        assert.equal(publication.body.draft, false);
        assert.equal(publication.body.prerelease, mode.includes('prerelease'));
        assert.equal(publication.body.make_latest, mode.includes('prerelease') ? 'false' : 'legacy');
        assert.ok(calls.indexOf(publication) < calls.indexOf(reactions[0]));
        assert.ok(!Object.hasOwn(publication.body, 'body'), 'Publication preserves existing draft edits');
      }
      const creation = calls.find(call => call.method === 'POST' && call.path.endsWith('/releases'));
      if (['new', 'new-prerelease'].includes(mode)) {
        assert.equal(creation.body.body, 'Prepared release notes');
        assert.equal(creation.body.prerelease, mode.includes('prerelease'));
      }
      else assert.equal(creation, undefined);
    } finally {
      assert.ok(resolve(directory).startsWith(resolve(tmpdir())));
      rmSync(directory, { recursive: true, force: true });
    }
  });
}

test('real git-cliff parsing and build-only preparation use rewrite history and synchronize all versions', { skip: !process.env.GIT_CLIFF_BIN }, () => {
  const directory = mkdtempSync(join(tmpdir(), 'amp-release-test-'));
  const git = (...args) => execFileSync('git', args, { cwd: directory, encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] }).trim();
  try {
    mkdirSync(join(directory, 'scripts'));
    mkdirSync(join(directory, 'src-tauri'));
    writeFileSync(join(directory, 'scripts/generate-licenses.mjs'), "import { writeFileSync } from 'node:fs'; writeFileSync('THIRD-PARTY-LICENSES.md', '# Test dependency notices\\n');");
    for (const name of ['release.mjs', 'release-lib.mjs']) cpSync(new URL(`../scripts/${name}`, import.meta.url), join(directory, 'scripts', name));
    cpSync(new URL('../cliff.toml', import.meta.url), join(directory, 'cliff.toml'));
    writeFileSync(join(directory, '.gitignore'), 'dist/\n');
    writeFileSync(join(directory, 'package.json'), JSON.stringify({ version: '8.0.0' }));
    writeFileSync(join(directory, 'src-tauri/tauri.conf.json'), JSON.stringify({ version: '8.0.0' }));
    writeFileSync(join(directory, 'src-tauri/Cargo.toml'), '[package]\nname = "auto-midi-player"\nversion = "8.0.0"\n');
    writeFileSync(join(directory, 'Cargo.lock'), 'version = 4\n\n[[package]]\nname = "auto-midi-player"\nversion = "8.0.0"\n');
    git('init', '-b', 'main'); git('config', 'user.name', 'Test'); git('config', 'user.email', 'test@example.com');
    git('add', '.'); git('commit', '-m', 'Rewrite in Svelte + Tauri');
    git('tag', 'v7.15.0', git('commit-tree', 'HEAD^{tree}', '-m', 'Disconnected WPF history'));
    for (const message of ['feat(games): add instrument', 'fix!: repair key timing', 'ref: simplify playback', 'ui: adjust layout', 'docs: update readme', 'chore: maintenance']) git('commit', '--allow-empty', '-m', message);
    const originalSha = git('rev-parse', 'HEAD');
    const env = { ...process.env, GITHUB_TOKEN: '', GITHUB_REPOSITORY: 'Jed556/AutoMidiPlayer', BUILD_ONLY: 'true' };
    const result = spawnSync(process.execPath, ['scripts/release.mjs', 'prepare', '8.1.0-beta.1'], { cwd: directory, env, encoding: 'utf8' });
    assert.equal(result.status, 0, result.stderr);
    const notes = readFileSync(join(directory, 'RELEASE.md'), 'utf8');
    for (const value of ['add instrument', 'repair key timing', 'simplify playback', 'adjust layout', '**Breaking:**']) assert.ok(notes.includes(value), notes);
    assert.doesNotMatch(notes, /update readme|maintenance/);
    assert.doesNotMatch(notes, /v7\.15\.0/);
    assert.equal(JSON.parse(readFileSync(join(directory, 'package.json'))).version, '8.1.0-beta.1');
    assert.match(readFileSync(join(directory, 'Cargo.lock'), 'utf8'), /version = "8.1.0-beta.1"/);
    assert.equal(git('rev-parse', 'HEAD'), originalSha);
    assert.equal(git('tag'), 'v7.15.0');
    const validated = spawnSync(process.execPath, ['scripts/release.mjs', 'validate', '8.1.0-beta.1'], { cwd: directory, env, encoding: 'utf8' });
    assert.equal(validated.status, 0, validated.stderr);
    git('add', '.'); git('commit', '-m', 'chore(release): v8.1.0-beta.1');
    git('tag', 'v8.1.0-beta.1');
    git('update-ref', 'refs/remotes/origin/main', 'HEAD');
    const releasedSha = git('rev-parse', 'HEAD');
    const rerun = spawnSync(process.execPath, ['scripts/release.mjs', 'prepare', '8.1.0-beta.1'], { cwd: directory, env, encoding: 'utf8' });
    assert.equal(rerun.status, 0, rerun.stderr);
    assert.equal(git('rev-parse', 'HEAD'), releasedSha);
    git('checkout', 'main'); git('commit', '--allow-empty', '-m', 'fix: post-beta repair');
    const preview = spawnSync(process.execPath, ['scripts/release.mjs', 'preview', '8.1.0'], { cwd: directory, env, encoding: 'utf8' });
    assert.equal(preview.status, 0, preview.stderr);
    assert.match(preview.stdout, /post-beta repair/);
    assert.match(preview.stdout, /add instrument/);
    assert.match(preview.stdout, /commits\/v8\.1\.0/);
    assert.doesNotMatch(preview.stdout, /compare\//);

    // Once there is a stable release, every later beta/RC/final uses that
    // stable baseline, including tags sharing a commit with a prerelease.
    git('tag', 'v8.1.0');
    git('commit', '--allow-empty', '-m', 'feat: new beta feature');
    git('tag', 'v8.2.0-beta.1');
    git('commit', '--allow-empty', '-m', 'fix: beta regression');
    git('tag', 'v8.2.0-beta.2');
    git('commit', '--allow-empty', '-m', 'ui: release candidate layout');
    git('tag', 'v8.2.0-rc.1');
    for (const target of ['8.2.0-rc.2', '8.2.0']) {
      const cumulative = spawnSync(process.execPath, ['scripts/release.mjs', 'preview', target], { cwd: directory, env, encoding: 'utf8' });
      assert.equal(cumulative.status, 0, cumulative.stderr);
      for (const message of ['new beta feature', 'beta regression', 'release candidate layout']) {
        assert.equal(cumulative.stdout.split(message).length - 1, 1, cumulative.stdout);
      }
      assert.doesNotMatch(cumulative.stdout, /add instrument|post-beta repair/);
      assert.ok(cumulative.stdout.includes(`/compare/v8.1.0...v${target}`), cumulative.stdout);
      assert.doesNotMatch(cumulative.stdout, /compare\/v8\.2\.0-(beta|rc)/);
    }
    git('tag', 'v8.2.0');
    git('commit', '--allow-empty', '-m', 'fix: next stable patch');
    const next = spawnSync(process.execPath, ['scripts/release.mjs', 'preview', '8.2.1'], { cwd: directory, env, encoding: 'utf8' });
    assert.equal(next.status, 0, next.stderr);
    assert.match(next.stdout, /compare\/v8\.2\.0\.\.\.v8\.2\.1/);
    assert.match(next.stdout, /next stable patch/);
    assert.doesNotMatch(next.stdout, /new beta feature|beta regression/);
  } finally {
    assert.ok(resolve(directory).startsWith(`${resolve(tmpdir())}\\amp-release-test-`) || resolve(directory).startsWith(`${resolve(tmpdir())}/amp-release-test-`));
    rmSync(directory, { recursive: true, force: true });
  }
});
