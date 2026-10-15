/**
 * Shared Tauri runtime utilities. The desktop app owns all persistent state;
 * a browser preview is intentionally unsupported instead of using fake data.
 */
import { waitForBackendReady } from './startupGate';

export const isTauriEnvironment =
  typeof window !== 'undefined' &&
  ('__TAURI_INTERNALS__' in window || '__TAURI__' in window);

export async function invokeTauri<T>(
  scope: string,
  command: string,
  args: Record<string, unknown> | undefined,
): Promise<T> {
  if (!isTauriEnvironment) {
    throw new Error(`[Tauri ${scope}] ${command} requires the desktop application.`);
  }

  try {
    await waitForBackendReady();
    const { invoke } = await import('@tauri-apps/api/core');
    return args === undefined
      ? await invoke<T>(command)
      : await invoke<T>(command, args);
  } catch (error) {
    console.error(`[Tauri ${scope}] ${command} failed:`, error);
    throw error;
  }
}

/** Invoke a command whose Rust side intentionally returns no value. */
export async function runTauriCommand(
  scope: string,
  command: string,
  args?: Record<string, unknown>,
): Promise<boolean> {
  try {
    await invokeTauri(scope, command, args);
    return true;
  } catch (error) {
    console.error(`[Tauri ${scope}] ${command} failed:`, error);
    return false;
  }
}

/**
 * Registers a Tauri event listener and handles the race where a component is
 * destroyed before Tauri resolves the asynchronous `listen` call.
 */
export function listenTauri<T>(
  eventName: string,
  callback: (payload: T) => void,
): () => void {
  if (!isTauriEnvironment) {
    console.warn(`[Tauri Events] ${eventName} requires the desktop application.`);
    return () => {};
  }

  let disposed = false;
  let unlisten: (() => void) | undefined;

  void import('@tauri-apps/api/event')
    .then(({ listen }) => listen<T>(eventName, (event) => callback(event.payload)))
    .then((stopListening) => {
      if (disposed) {
        stopListening();
      } else {
        unlisten = stopListening;
      }
    })
    .catch((error) => {
      console.error(`[Tauri Events] ${eventName} subscription failed:`, error);
    });

  return () => {
    disposed = true;
    unlisten?.();
  };
}
