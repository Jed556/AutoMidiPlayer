// The small launch entry installs this before importing any application stores.
let backendReady: Promise<void> = Promise.resolve();

export function setBackendReady(ready: Promise<void>) {
  backendReady = ready;
}

export function waitForBackendReady() {
  return backendReady;
}
