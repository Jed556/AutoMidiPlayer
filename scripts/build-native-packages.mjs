import { spawn } from 'node:child_process';
import { unlink } from 'node:fs/promises';
import { basename, dirname, isAbsolute, resolve } from 'node:path';
import { pathToFileURL } from 'node:url';

const macTargets = new Set(['aarch64-apple-darwin', 'x86_64-apple-darwin']);

async function runCommand(command, args, input) {
  return new Promise((accept, reject) => {
    const child = spawn(command, args, { stdio: ['pipe', 'pipe', 'pipe'] });
    let output = '';
    const capture = (stream, bytes) => {
      stream.write(bytes);
      // Keep enough diagnostic output to recognize the failure without retaining
      // the entire compiler log. Mounted image metadata is captured separately.
      output = (output + bytes.toString()).slice(-1024 * 1024);
    };
    child.stdout.on('data', bytes => capture(process.stdout, bytes));
    child.stderr.on('data', bytes => capture(process.stderr, bytes));
    child.on('error', reject);
    child.on('close', status => accept({ status: status ?? 1, output }));
    child.stdin.end(input);
  });
}

// Read plist metadata without printing it into the build log or mixing stderr
// with the data passed to the plist parser.
async function runMetadata(command, args, input) {
  return new Promise((accept, reject) => {
    const child = spawn(command, args, { stdio: ['pipe', 'pipe', 'pipe'] });
    let output = '';
    let error = '';
    child.stdout.on('data', bytes => { output += bytes.toString(); });
    child.stderr.on('data', bytes => { error += bytes.toString(); });
    child.on('error', reject);
    child.on('close', status => {
      if (error) process.stderr.write(error);
      accept({ status: status ?? 1, output });
    });
    child.stdin.end(input);
  });
}

export async function detachDmg(device, run = runMetadata) {
  let detached = await run('/usr/bin/hdiutil', ['detach', device]);
  // Callers supply only their own temporary image device or inspection mount.
  if (detached.status === 16) detached = await run('/usr/bin/hdiutil', ['detach', '-force', device]);
  if (detached.status !== 0) throw new Error(`Cannot detach temporary DMG: ${device}`);
}

export async function cleanupDmgImages(cwd, target, run = runMetadata, remove = unlink) {
  if (!macTargets.has(target)) throw new Error(`Unsupported macOS target: ${target}`);
  const directory = resolve(cwd, 'target', target, 'release/bundle/macos');
  const info = await run('/usr/bin/hdiutil', ['info', '-plist']);
  if (info.status !== 0) throw new Error('Unable to inspect mounted DMG images');
  // hdiutil includes binary aliases in its plist, which plutil cannot convert
  // to JSON. plistlib reads them; select only the fields needed for cleanup.
  const converted = await run('python3', ['-c', `
import json, plistlib, sys
# Buffer piped stdin because Python 3.10's plistlib.load seeks during format detection.
info = plistlib.loads(sys.stdin.buffer.read())
json.dump({"images": [{
    "image-path": image.get("image-path"),
    "system-entities": [{"dev-entry": entity.get("dev-entry")} for entity in image.get("system-entities", [])],
} for image in info["images"]]}, sys.stdout)
`], info.output);
  if (converted.status !== 0) throw new Error('Unable to parse mounted DMG metadata');
  const metadata = JSON.parse(converted.output);
  if (!Array.isArray(metadata.images)) throw new Error('Missing mounted DMG image metadata');
  for (const image of metadata.images) {
    if (typeof image['image-path'] !== 'string' || !isAbsolute(image['image-path'])) continue;
    const path = resolve(image['image-path']);
    if (dirname(path) !== directory || !/^rw\.\d+\..+\.dmg$/.test(basename(path))) continue;
    // Detach the backing device once. APFS can also list a synthesized disk
    // which disappears when its backing device is detached.
    const device = (image['system-entities'] ?? [])
      .map(entity => entity['dev-entry']).find(device => /^\/dev\/disk\d+$/.test(device));
    if (!device) throw new Error(`Cannot identify the temporary DMG device: ${path}`);
    await detachDmg(device, run);
    await remove(path);
  }
}

export async function buildNativePackages(args, {
  platform = process.platform, cwd = process.cwd(), run = runCommand,
  cleanup = cleanupDmgImages, log = console.error,
} = {}) {
  const targetIndex = args.indexOf('--target');
  const target = targetIndex === -1 ? undefined : args[targetIndex + 1];
  const cli = resolve(cwd, 'node_modules/@tauri-apps/cli/tauri.js');
  const separator = args.indexOf('--');
  const bundleArgs = separator === -1 ? args : args.slice(0, separator);
  let result = await run(process.execPath, [cli, 'build', ...args]);
  for (let attempt = 2; attempt <= 3; attempt++) {
    if (result.status === 0 || platform !== 'darwin' || !macTargets.has(target)
      || !result.output.includes('bundle_dmg.sh')
      || !/hdiutil: couldn't eject[^\r\n]*Resource busy/.test(result.output)) break;
    log(`Temporary DMG is busy; cleaning up this build's image and retrying packaging (${attempt}/3).`);
    await cleanup(cwd, target);
    // Packaging reuses the compiled binary. Cargo's --locked belongs to build,
    // while Tauri still handles app signing, DMG signing and updater archives.
    result = await run(process.execPath, [cli, 'bundle', ...bundleArgs]);
  }
  return result.status ?? 1;
}

if (process.argv[1] && pathToFileURL(resolve(process.argv[1])).href === import.meta.url) {
  try { process.exitCode = await buildNativePackages(process.argv.slice(2)); }
  catch (error) { console.error(error.message); process.exitCode = 1; }
}
