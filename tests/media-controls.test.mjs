import test from 'node:test';
import assert from 'node:assert/strict';
import { connectMediaControls } from '../src/renderer/lib/backend/mediaControls.ts';

const tick = () => new Promise(resolve => setImmediate(resolve));

test('rapid media commands await each load and recover after a failed command', async () => {
  let receive;
  let finishLoad;
  const commands = [];
  const errors = [];
  const failed = new Error('MIDI load failed');
  const disconnect = connectMediaControls({ onMediaControl: callback => {
    receive = callback;
    return () => {};
  } }, async command => {
    commands.push(command.action);
    if (commands.length === 1) await new Promise(resolve => { finishLoad = resolve; });
    if (command.action === 'previous') throw failed;
  }, error => errors.push(error));

  for (const action of ['next', 'next', 'previous', 'pause']) receive({ action });
  await tick();
  assert.deepEqual(commands, ['next']);
  finishLoad();
  await tick();
  assert.deepEqual(commands, ['next', 'next', 'previous', 'pause']);
  assert.deepEqual(errors, [failed]);
  disconnect();
});

test('disconnect removes the listener and cancels queued commands during an update or shutdown', async () => {
  let receive;
  let finishLoad;
  let removed = 0;
  const commands = [];
  const disconnect = connectMediaControls({ onMediaControl: callback => {
    receive = callback;
    return () => { removed++; };
  } }, async command => {
    commands.push(command.action);
    await new Promise(resolve => { finishLoad = resolve; });
  }, assert.fail);

  receive({ action: 'next' });
  receive({ action: 'next' });
  await tick();
  disconnect();
  finishLoad();
  await tick();
  assert.equal(removed, 1);
  assert.deepEqual(commands, ['next']);
});
