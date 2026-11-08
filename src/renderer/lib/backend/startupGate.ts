// The small launch entry installs this before importing any application stores.
let backendReady: Promise<void> = Promise.resolve();

export function setBackendReady(ready: Promise<void>) {
  backendReady = ready;
}

export function waitForBackendReady() {
  return backendReady;
}

/** Start the deferred native runtime, then wait until its commands are ready. */
export async function startRuntime(onProgress: (stage: string) => void) {
  const { invoke } = await import('@tauri-apps/api/core');
  await invoke('start_runtime');
  for (;;) {
    const status = await invoke<{ stage: string; ready: boolean; error: string | null }>('get_startup_status');
    if (status.error) throw new Error(status.error);
    onProgress(status.stage);
    if (status.ready) return;
    await new Promise(resolve => setTimeout(resolve, 50));
  }
}
