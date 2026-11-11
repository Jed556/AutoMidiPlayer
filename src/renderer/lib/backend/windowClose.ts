import type { WindowControlApi } from '../../../shared/types/ipc.js';

/** Every close request finishes through native policy after saving the session. */
export function createWindowCloseHandler(
  window: Pick<WindowControlApi, 'completeClose'>,
  flush: () => Promise<void>,
  onError: (error: unknown) => void,
) {
  let closing = false;
  return async (event: { preventDefault: () => void }) => {
    event.preventDefault();
    if (closing) return;
    closing = true;
    try {
      await flush();
      await window.completeClose();
    } catch (error) {
      // Cancel a pending tray Exit too, so a failed save can be retried safely.
      await window.completeClose(true).catch(onError);
      onError(error);
    } finally {
      // Closing to tray leaves this same webview alive for the next request.
      closing = false;
    }
  };
}
