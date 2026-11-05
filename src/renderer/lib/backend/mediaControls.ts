import type { PlaybackApi } from '../../../shared/types/ipc.js';
import type { MediaControlAction } from '../../../shared/types/mediaControls.js';

/** Serialize OS commands so rapid skips cannot race native MIDI loads. */
export function connectMediaControls(
  playback: Pick<PlaybackApi, 'onMediaControl'>,
  handle: (command: MediaControlAction) => Promise<void>,
  onError: (error: unknown) => void,
) {
  let pending: Promise<void> = Promise.resolve();
  let disposed = false;
  const unlisten = playback.onMediaControl(command => {
    pending = pending.then(async () => {
      if (!disposed) await handle(command);
    }).catch(onError);
  });
  return () => { disposed = true; unlisten(); };
}
