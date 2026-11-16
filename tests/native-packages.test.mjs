import test from 'node:test';
import assert from 'node:assert/strict';
import { join, resolve } from 'node:path';
import { spawnSync } from 'node:child_process';
import { buildNativePackages, cleanupDmgImages, detachDmg } from '../scripts/build-native-packages.mjs';

const busy = "failed to run bundle_dmg.sh: hdiutil: couldn't eject \"disk2\" - Resource busy";
const target = 'x86_64-apple-darwin';
const cwd = resolve('native package fixture');
const args = ['--target', target, '--bundles', 'app,dmg', '--config', 'dist/release-config.json', '--', '--locked'];
const quiet = () => {};

test('successful native builds preserve all original arguments and skip recovery', async () => {
  const calls = [];
  const status = await buildNativePackages(args, {
    platform: 'darwin', cwd, log: quiet,
    run: async (command, commandArgs) => {
      calls.push([command, commandArgs]);
      return { status: 0, output: 'Built application' };
    },
    cleanup: async () => assert.fail('A successful build must not detach images'),
  });
  assert.equal(status, 0);
  assert.equal(calls.length, 1);
  assert.equal(calls[0][0], process.execPath);
  assert.ok(calls[0][1][0].endsWith('tauri.js'));
  assert.deepEqual(calls[0][1].slice(1), ['build', ...args]);
});

test('busy DMG recovery reuses compiled binaries and preserves target, bundle and signing config arguments', async () => {
  for (const architecture of ['x86_64-apple-darwin', 'aarch64-apple-darwin']) {
    const original = ['--target', architecture, '--bundles', 'app,dmg', '--config', 'dist/release-config.json', '--config', 'signing config.json', '--', '--locked'];
    const calls = [];
    const events = [];
    const run = async (command, commandArgs) => {
      calls.push([command, commandArgs]);
      events.push(commandArgs[1]);
      return calls.length === 1 ? { status: 16, output: busy } : { status: 0, output: 'Bundled' };
    };
    const status = await buildNativePackages(original, {
      platform: 'darwin', cwd, run, log: quiet,
      cleanup: async (...received) => {
        assert.deepEqual(received, [cwd, architecture]);
        events.push('cleanup');
      },
    });
    assert.equal(status, 0);
    assert.deepEqual(events, ['build', 'cleanup', 'bundle']);
    assert.deepEqual(calls[0][1].slice(1), ['build', ...original]);
    assert.equal(calls[1][0], calls[0][0]);
    assert.equal(calls[1][1][0], calls[0][1][0]);
    assert.deepEqual(calls[1][1].slice(1), ['bundle', ...original.slice(0, original.indexOf('--'))]);
  }
});

test('recovery requires macOS, a supported Apple target and every specific DMG failure marker', async () => {
  const cases = [
    ['linux', args, busy],
    ['win32', args, busy],
    ['darwin', [], busy],
    ['darwin', [target], busy],
    ['darwin', ['--target', 'x86_64-unknown-linux-gnu'], busy],
    ['darwin', ['--target', '../../outside'], busy],
    ['darwin', args, busy.replace('bundle_dmg.sh', 'other.sh')],
    ['darwin', args, busy.replace("hdiutil: couldn't eject", 'another error')],
    ['darwin', args, busy.replace('Resource busy', 'Permission denied')],
    ['darwin', args, 'error: Rust compilation failed'],
  ];
  for (const [platform, original, output] of cases) {
    let count = 0;
    const status = await buildNativePackages(original, {
      platform, cwd, log: quiet,
      run: async () => { count++; return { status: 7, output }; },
      cleanup: async () => assert.fail('Unrelated errors must not detach images'),
    });
    assert.equal(status, 7, `${platform}: ${output}`);
    assert.equal(count, 1);
  }
});

test('persistent busy DMGs stop after three attempts and report the final failure', async () => {
  let attempts = 0;
  let cleanups = 0;
  const status = await buildNativePackages(args, {
    platform: 'darwin', cwd, log: quiet,
    run: async () => { attempts++; return { status: 16, output: busy }; },
    cleanup: async () => { cleanups++; },
  });
  assert.equal(status, 16);
  assert.equal(attempts, 3);
  assert.equal(cleanups, 2);
});

test('a different packaging failure ends recovery immediately', async () => {
  let attempts = 0;
  let cleanups = 0;
  const status = await buildNativePackages(args, {
    platform: 'darwin', cwd, log: quiet,
    run: async () => ++attempts === 1 ? { status: 16, output: busy } : { status: 9, output: 'Signing failed' },
    cleanup: async () => { cleanups++; },
  });
  assert.equal(status, 9);
  assert.equal(attempts, 2);
  assert.equal(cleanups, 1);
});

test('cleanup failure prevents a retry and signaled builds return a failure status', async () => {
  let attempts = 0;
  await assert.rejects(buildNativePackages(args, {
    platform: 'darwin', cwd, log: quiet,
    run: async () => { attempts++; return { status: 16, output: busy }; },
    cleanup: async () => { throw new Error('Could not detach owned image'); },
  }), /Could not detach owned image/);
  assert.equal(attempts, 1);
  assert.equal(await buildNativePackages(args, {
    platform: 'darwin', cwd, log: quiet,
    run: async () => ({ status: null, output: '' }),
    cleanup: async () => assert.fail('A signal must not trigger recovery'),
  }), 1);
});

const imageDirectory = join(cwd, 'target', target, 'release', 'bundle', 'macos');
const image = (path, entries = ['/dev/disk2', '/dev/disk2s1']) => ({
  'image-path': path,
  'system-entities': entries.map(device => ({ 'dev-entry': device })),
});

function diskCommands(images, detach = () => ({ status: 0, output: '' })) {
  const calls = [];
  const run = async (command, commandArgs, input) => {
    calls.push([command, commandArgs, input]);
    if (commandArgs[0] === 'info') {
      assert.equal(command, '/usr/bin/hdiutil');
      assert.deepEqual(commandArgs, ['info', '-plist']);
      return { status: 0, output: '<plist>disk info</plist>' };
    }
    if (command === 'python3') {
      assert.equal(commandArgs[0], '-c');
      assert.equal(input, '<plist>disk info</plist>');
      return { status: 0, output: JSON.stringify({ images }) };
    }
    assert.equal(command, '/usr/bin/hdiutil');
    assert.equal(commandArgs[0], 'detach');
    return detach(commandArgs);
  };
  return { calls, run };
}

test('cleanup detaches and removes only this build’s mounted temporary DMGs', async () => {
  const owned = join(imageDirectory, 'rw.123.Auto MIDI Player_8.10.4_x64.dmg');
  const { calls, run } = diskCommands([
    image(owned, ['/dev/disk2', '/dev/disk2', '/dev/disk2s1']),
    image(join(imageDirectory, 'Auto MIDI Player_8.10.4_x64.dmg'), ['/dev/disk3']),
    image(join(imageDirectory, 'rw.invalid.Auto MIDI Player.dmg'), ['/dev/disk4']),
    image(join(`${imageDirectory}-other`, 'rw.1.Other.dmg'), ['/dev/disk5']),
    image(join(imageDirectory, 'subdirectory', 'rw.2.Other.dmg'), ['/dev/disk6']),
    image(join(cwd, 'target', 'aarch64-apple-darwin', 'release', 'bundle', 'macos', 'rw.3.Other.dmg'), ['/dev/disk7']),
    image('rw.4.Relative.dmg', ['/dev/disk8']),
  ]);
  const removed = [];
  await cleanupDmgImages(cwd, target, run, async path => { removed.push(path); });
  assert.deepEqual(calls.slice(2).map(call => call[1]), [['detach', '/dev/disk2']]);
  assert.deepEqual(removed, [owned]);
});

test('cleanup parses real hdiutil plist data aliases and selects the backing disk before APFS synthesized devices', async t => {
  const python = ['python3', 'python'].find(command => spawnSync(command, ['--version']).status === 0);
  if (!python) {
    t.skip('Requires Python to exercise the actual macOS plist parser');
    return;
  }
  const owned = join(imageDirectory, 'rw.789.Auto MIDI Player.dmg');
  const xmlPath = owned.replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('>', '&gt;');
  const plist = `<?xml version="1.0" encoding="UTF-8"?>
<plist version="1.0"><dict><key>images</key><array><dict>
<key>image-alias</key><data>AAECAwQFBgcICQ==</data>
<key>image-path</key><string>${xmlPath}</string>
<key>system-entities</key><array>
<dict><key>dev-entry</key><string>/dev/disk2</string></dict>
<dict><key>dev-entry</key><string>/dev/disk2s1</string></dict>
<dict><key>dev-entry</key><string>/dev/disk3</string></dict>
<dict><key>dev-entry</key><string>/dev/disk3s1</string></dict>
</array></dict></array></dict></plist>`;
  const detached = [];
  const removed = [];
  await cleanupDmgImages(cwd, target, async (command, commandArgs, input) => {
    if (commandArgs[0] === 'info') return { status: 0, output: plist };
    if (command === 'python3') {
      const result = spawnSync(python, commandArgs, { input, encoding: 'utf8' });
      assert.equal(result.status, 0, result.stderr || result.error?.message);
      assert.deepEqual(Object.keys(JSON.parse(result.stdout).images[0]).sort(), ['image-path', 'system-entities']);
      return { status: result.status, output: result.stdout };
    }
    detached.push(commandArgs);
    return { status: 0, output: '' };
  }, async path => { removed.push(path); });
  assert.deepEqual(detached, [['detach', '/dev/disk2']]);
  assert.deepEqual(removed, [owned]);

  let parserFailed = false;
  await assert.rejects(cleanupDmgImages(cwd, target, async (command, commandArgs, input) => {
    if (commandArgs[0] === 'info') return { status: 0, output: '<plist version="1.0"><dict></dict></plist>' };
    assert.equal(command, 'python3');
    const result = spawnSync(python, commandArgs, { input, encoding: 'utf8' });
    assert.notEqual(result.status, 0, 'Missing image metadata must not be treated as an empty image list');
    parserFailed = true;
    return { status: result.status, output: result.stdout };
  }, async () => assert.fail('Invalid metadata must not remove files')), /Unable to parse/);
  assert.equal(parserFailed, true);
});

test('package inspection cleanup detaches the same owned mountpoint and forces only an EBUSY failure', async () => {
  const mountpoint = join(cwd, 'dist', 'package-inspection', 'dmg-owned');
  for (const busyFirst of [false, true]) {
    const calls = [];
    await detachDmg(mountpoint, async (command, commandArgs) => {
      assert.equal(command, '/usr/bin/hdiutil');
      calls.push(commandArgs);
      return { status: busyFirst && calls.length === 1 ? 16 : 0, output: '' };
    });
    assert.deepEqual(calls, [['detach', mountpoint], ...(busyFirst ? [['detach', '-force', mountpoint]] : [])]);
  }
  let calls = 0;
  await assert.rejects(detachDmg(mountpoint, async () => {
    calls++;
    return { status: 1, output: 'Permission denied' };
  }), /Cannot detach/);
  assert.equal(calls, 1, 'Permission errors must not trigger forced detach');
});

test('cleanup forces only a busy owned device and removes its image after successful detach', async () => {
  const owned = join(imageDirectory, 'rw.456.Auto MIDI Player.dmg');
  const events = [];
  const { run } = diskCommands([image(owned)], commandArgs => {
    events.push(commandArgs);
    return { status: commandArgs.includes('-force') ? 0 : 16, output: 'Resource busy' };
  });
  await cleanupDmgImages(cwd, target, run, async path => { events.push(['remove', path]); });
  assert.deepEqual(events, [['detach', '/dev/disk2'], ['detach', '-force', '/dev/disk2'], ['remove', owned]]);
});

test('cleanup fails closed when an owned image has no valid root disk device', async () => {
  for (const devices of [[], ['/dev/disk2s1'], ['/dev/disk2;touch bad'], ['/dev/rdisk2']]) {
    const { calls, run } = diskCommands([image(join(imageDirectory, 'rw.1.Owned.dmg'), devices)]);
    await assert.rejects(cleanupDmgImages(cwd, target, run, async () => assert.fail('Unsafe image must not be removed')));
    assert.equal(calls.length, 2);
  }
});

test('detach failures preserve temporary images and do not force unrelated errors', async () => {
  for (const statuses of [[1], [16, 1]]) {
    let attempt = 0;
    const { calls, run } = diskCommands([image(join(imageDirectory, 'rw.1.Owned.dmg'))], () => ({ status: statuses[attempt++], output: 'Detach failed' }));
    await assert.rejects(cleanupDmgImages(cwd, target, run, async () => assert.fail('Mounted image must not be removed')));
    assert.equal(calls.length, 2 + statuses.length);
  }
});

test('cleanup fails closed on discovery and plist conversion errors', async () => {
  for (const phase of ['info', 'convert', 'json']) {
    let calls = 0;
    await assert.rejects(cleanupDmgImages(cwd, target, async (_command, commandArgs) => {
      calls++;
      if (commandArgs[0] === 'info') return { status: phase === 'info' ? 1 : 0, output: '<plist />' };
      return { status: phase === 'convert' ? 1 : 0, output: 'invalid JSON' };
    }, async () => assert.fail('Discovery errors must not remove files')));
    assert.equal(calls, phase === 'info' ? 1 : 2);
  }
});

test('cleanup permits a packaging retry when no images remain mounted', async () => {
  const { calls, run } = diskCommands([]);
  await cleanupDmgImages(cwd, target, run, async () => assert.fail('No files belong to a mounted image'));
  assert.equal(calls.length, 2);
});
