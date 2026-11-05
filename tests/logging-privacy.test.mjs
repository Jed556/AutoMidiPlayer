import test from 'node:test';
import assert from 'node:assert/strict';
import { redactLogPaths } from '../src/renderer/lib/infrastructure/logPrivacy.ts';
import { logger } from '../src/renderer/lib/infrastructure/logger.ts';

test('redacts the config reload example while retaining the app author directory', () => {
  assert.equal(
    redactLogPaths(String.raw`[ConfigManager] Reloading configs from "C:\\Users\\Jane Smith\\AppData\\Local\\Jed556\\AutoMIDIPlayer"`),
    String.raw`[ConfigManager] Reloading configs from "%USERPROFILE%\\AppData\\Local\\Jed556\\AutoMIDIPlayer"`,
  );
});

test('supports raw, escaped, mixed-case, Unicode, extended and file URL paths', () => {
  const cases = [
    [String.raw`c:\uSeRs\María 李\Music\song.mid`, String.raw`%USERPROFILE%\Music\song.mid`],
    [String.raw`\\?\C:\Users\Jane Smith\Music\song.mid`, String.raw`%USERPROFILE%\Music\song.mid`],
    ['file:///D:/Users/Jane%20Smith/Music/song.mid', 'file:///%USERPROFILE%/Music/song.mid'],
    ['/home/Jane Smith/a.mid /Users/María/b.mid', '$HOME/a.mid $HOME/b.mid'],
    ['"C:/Users/Jane Smith"', '"%USERPROFILE%"'],
  ];
  for (const [input, expected] of cases) assert.equal(redactLogPaths(input), expected);
});

test('keeps non-profile paths, names, URLs and placeholders intact and is idempotent', () => {
  const text = String.raw`C:\Music\Jed556\a.mid; user Jane Smith; %USERPROFILE%\Music\a.mid; https://example.com/users/Jane`;
  assert.equal(redactLogPaths(text), text);
  const safe = redactLogPaths(String.raw`C:\Users\Jane Smith\a.mid`);
  assert.equal(redactLogPaths(safe), safe);
});

test('all renderer log methods redact console output and the retained buffer, including error stacks', async (t) => {
  const output = [];
  for (const level of ['log', 'warn', 'error', 'debug']) {
    t.mock.method(console, level, (...args) => output.push(args.join(' ')));
  }
  await logger.clearLogs();
  const path = String.raw`C:\Users\Jane Smith\Music\song.mid`;
  const error = new Error(`Could not open ${path}`);
  error.stack = `Error: ${error.message}\n    at ${path}:42`;
  await logger.info(path, path);
  await logger.warn(path, path);
  await logger.error(path, error, path);
  await logger.step('IMPORT', path);
  await logger.pageVisit('Studio', path);
  assert.equal(output.length, 5);
  for (const line of output) {
    assert.ok(!line.includes('Jane Smith'));
    assert.ok(line.includes('%USERPROFILE%'));
  }
  const tail = await logger.readTail();
  assert.ok(!tail.includes('Jane Smith'));
  assert.ok(tail.includes(String.raw`%USERPROFILE%\Music\song.mid:42`));
  assert.equal(path, String.raw`C:\Users\Jane Smith\Music\song.mid`);
  assert.ok(error.stack.includes('Jane Smith'));
  await logger.clearLogs();
});
