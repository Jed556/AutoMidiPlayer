import { readFile, writeFile, mkdir, mkdtemp, cp, readdir, stat } from 'node:fs/promises';
import { resolve, join, basename } from 'node:path';
import { execFileSync } from 'node:child_process';
import { documents, releaseVersion, mergeManifests, sha256, verifyUpdaterSignature } from './release-lib.mjs';

const [command, input, target] = process.argv.slice(2);
const version = releaseVersion(input);
const repo = process.env.GITHUB_REPOSITORY || 'Jed556/AutoMidiPlayer';
const destination = resolve('dist/release-assets');
await mkdir(destination, { recursive: true });

async function files(root) {
  const result = [];
  for (const entry of await readdir(root, { withFileTypes: true })) {
    const path = join(root, entry.name);
    if (entry.isDirectory()) result.push(...await files(path));
    else if (entry.isFile()) result.push(path);
  }
  return result;
}

async function verifyDocuments(root) {
  const contents = await files(root);
  for (const name of documents) {
    const expected = await readFile(`src-tauri/resources/docs/${name}`);
    const candidates = contents.filter(path => basename(path) === name && path.replaceAll('\\', '/').includes('/resources/docs/'));
    if (!candidates.length || !(await Promise.all(candidates.map(path => readFile(path))))
      .some(bytes => bytes.equals(expected))) throw new Error(`Bundled ${name} is missing or differs from the staged document`);
  }
}

async function inspectPackage(path, suffix) {
  await mkdir('dist/package-inspection', { recursive: true });
  const inspection = await mkdtemp(resolve('dist/package-inspection', `${suffix.replaceAll('.', '').replaceAll('-', '')}-`));
  if (suffix === '-setup.exe') {
    execFileSync('7z', ['x', '-y', path, `-o${inspection}`], { stdio: 'pipe' });
  } else if (suffix === '.deb') {
    execFileSync('dpkg-deb', ['--extract', path, inspection], { stdio: 'inherit' });
  } else if (suffix === '.AppImage') {
    execFileSync('chmod', ['+x', path]);
    execFileSync(path, ['--appimage-extract'], { cwd: inspection, stdio: 'pipe', maxBuffer: 32 * 1024 * 1024 });
  } else if (suffix === '.app.tar.gz') {
    execFileSync('tar', ['-xf', path, '-C', inspection]);
  } else if (suffix === '.dmg') {
    execFileSync('hdiutil', ['attach', '-readonly', '-nobrowse', '-mountpoint', inspection, path]);
    try { await verifyDocuments(inspection); }
    finally { execFileSync('hdiutil', ['detach', inspection]); }
    return;
  }
  await verifyDocuments(inspection);
}

if (command === 'stage') {
  const root = resolve('target', target, 'release');
  const platformName = target.includes('windows') ? 'windows-x64' : target.includes('linux') ? 'linux-x64' : `macos-${target.startsWith('aarch64') ? 'arm64' : 'x64'}`;
  const bundleFiles = await files(join(root, 'bundle'));
  const suffixes = target.includes('windows') ? ['-setup.exe'] : target.includes('linux') ? ['.AppImage', '.deb'] : ['.dmg', '.app.tar.gz'];
  for (const suffix of suffixes) {
    const matches = bundleFiles.filter(path => path.endsWith(suffix));
    if (matches.length !== 1) throw new Error(`Expected one ${suffix} package, found ${matches.length}`);
    const path = matches[0];
    await inspectPackage(path, suffix);
    // macOS updater tarballs have identical source names for Intel and ARM.
    const assetName = `AutoMidiPlayer_v${version}_${platformName}${suffix}`;
    const asset = join(destination, assetName);
    await cp(path, asset);
    if (suffix !== '.deb' && suffix !== '.dmg') {
      const signature = (await readFile(`${path}.sig`, 'utf8')).trim();
      if (!signature) throw new Error(`Missing updater signature for ${path}`);
      await cp(`${path}.sig`, `${asset}.sig`);
      const platform = target.includes('windows') ? 'windows-x86_64' : target.includes('linux') ? 'linux-x86_64' : `darwin-${target.startsWith('aarch64') ? 'aarch64' : 'x86_64'}`;
      await writeFile(join(destination, `manifest-${platform}.json`), `${JSON.stringify({ version, platforms: {
        [platform]: { signature, url: `https://github.com/${repo}/releases/download/v${version}/${encodeURIComponent(assetName)}` },
      } }, null, 2)}\n`);
    }
  }
  if (target.includes('windows')) {
    const portable = resolve('dist/portable');
    await mkdir(portable, { recursive: true });
    await cp(join(root, 'Auto MIDI Player.exe'), join(portable, 'Auto MIDI Player.exe'));
    await cp('src-tauri/resources', join(portable, 'resources'), { recursive: true });
    await writeFile(join(portable, 'portable.txt'), 'Use release downloads to update this no-install distribution.\n');
    const archive = join(destination, `AutoMidiPlayer_v${version}_windows-x64-portable.zip`);
    execFileSync('7z', ['a', '-tzip', archive, './*', '-mx=9'], { cwd: portable, stdio: 'inherit' });
    const listing = execFileSync('7z', ['l', '-slt', archive], { encoding: 'utf8' });
    for (const name of [...documents, 'default.sf2', 'portable.txt', 'Auto MIDI Player.exe']) {
      if (!listing.includes(name)) throw new Error(`Portable ZIP missing ${name}`);
    }
  }
} else if (command === 'collect') {
  const all = await files(resolve('dist/downloaded'));
  const manifestPaths = all.filter(path => /^manifest-.*\.json$/.test(basename(path)));
  const names = new Set();
  for (const path of all.filter(path => !manifestPaths.includes(path))) {
    const name = basename(path);
    if (names.has(name)) throw new Error(`Duplicate release asset: ${name}`);
    if (!(await stat(path)).size) throw new Error(`Empty asset: ${name}`);
    names.add(name);
    await cp(path, join(destination, name));
  }
  if (![...names].some(name => name.endsWith('-portable.zip'))) throw new Error('Missing portable package');
  if ([...names].filter(name => name.endsWith('.dmg')).length !== 2
    || ![...names].some(name => name.endsWith('.deb'))) throw new Error('Missing platform packages');
  const manifests = await Promise.all(manifestPaths.map(async path => JSON.parse(await readFile(path, 'utf8'))));
  const merged = mergeManifests(manifests, version, repo, names);
  if (!process.env.AMP_UPDATER_PUBLIC_KEY) throw new Error('Missing updater public key');
  for (const entry of Object.values(merged.platforms)) {
    const name = decodeURIComponent(new URL(entry.url).pathname.split('/').at(-1));
    verifyUpdaterSignature(await readFile(join(destination, name)), entry.signature, process.env.AMP_UPDATER_PUBLIC_KEY);
  }
  merged.notes = await readFile('RELEASE.md', 'utf8');
  await writeFile(join(destination, 'latest.json'), `${JSON.stringify(merged, null, 2)}\n`);
  await cp('RELEASE.md', join(destination, 'RELEASE.md'));
  const checksums = [];
  for (const name of (await readdir(destination)).sort()) {
    if (name !== 'checksums.txt') checksums.push(`${sha256(await readFile(join(destination, name)))}  ${name}`);
  }
  await writeFile(join(destination, 'checksums.txt'), `${checksums.join('\n')}\n`);
} else throw new Error('Usage: node scripts/package-release.mjs <stage|collect> <version> [rust-target]');
