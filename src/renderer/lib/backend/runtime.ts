/**
 * Shared Tauri runtime utilities. The desktop app owns all persistent state;
 * a browser preview is intentionally unsupported instead of using fake data.
 */
import { waitForBackendReady } from './startupGate';

export const isTauriEnvironment =
  typeof window !== 'undefined' &&
  ('__TAURI_INTERNALS__' in window || '__TAURI__' in window);

let clearingData = false;
const activeCommands = new Set<Promise<unknown>>();

/** Freeze the old interface and drain native work before clearing persisted data.
 * The caller must reload the renderer on success or failure to discard old stores.
 */
export async function clearTauriAppData(): Promise<void> {
  clearingData = true;
  await Promise.allSettled([...activeCommands]);
  await waitForBackendReady();
  const { invoke } = await import('@tauri-apps/api/core');
  await invoke('clear_app_data');
}

export function invokeTauri<T>(
  scope: string,
  command: string,
  args: Record<string, unknown> | undefined,
): Promise<T> {
  // Keep window controls usable. Other old-store requests wait for the renderer
  // to be discarded instead of repopulating the freshly cleared database.
  if (clearingData && scope !== 'Window') return new Promise<T>(() => {});
  const request = invokeCommand<T>(scope, command, args);
  activeCommands.add(request);
  void request.finally(() => activeCommands.delete(request)).catch(() => {});
  return request;
}

async function invokeCommand<T>(scope: string, command: string, args: Record<string, unknown> | undefined): Promise<T> {
  if (!isTauriEnvironment) {
    throw new Error(`[Tauri ${scope}] ${command} requires the desktop application.`);
  }

  try {
    // These native commands also work before AppState exists or after startup fails.
    if (scope !== 'Window') await waitForBackendReady();
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
    .then(({ listen }) => listen<T>(eventName, (event) => { if (!clearingData) callback(event.payload); }))
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
