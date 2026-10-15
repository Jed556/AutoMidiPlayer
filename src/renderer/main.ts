// Keep this entry independent of Svelte, app styles, telemetry and player code.
// Vite must not compile that module graph before the splash window can appear.
import { setBackendReady } from './lib/backend/startupGate';
import { startupProgress, nativeStartupProgress } from './lib/backend/startupProgress';
import type { UpdateResume } from '../shared/types/update';

interface StartupStatus {
  stage: string;
  ready: boolean;
  error: string | null;
  updateResume: UpdateResume | null;
}

const stopBootProgress = startupProgress.subscribe((value) => {
  const fill = document.getElementById('boot-progress-fill');
  if (fill) fill.style.transform = `scaleX(${value / 100})`;
  document.getElementById('boot-progress')?.setAttribute('aria-valuenow', String(Math.round(value)));
});

async function start() {
  startupProgress.advanceStartup(2);
  const logo = document.querySelector<HTMLImageElement>('#boot-screen img');
  if (logo) await logo.decode().catch(() => {});

  const isDesktop = '__TAURI_INTERNALS__' in window || '__TAURI__' in window;
  let initialStatus: StartupStatus | undefined;
  if (isDesktop) {
    const { invoke } = await import('@tauri-apps/api/core');
    initialStatus = await invoke<StartupStatus>('get_startup_status');
    if (initialStatus.updateResume) startupProgress.resumeUpdate(initialStatus.updateResume.progress);
    const { getCurrentWindow } = await import('@tauri-apps/api/window');
    await getCurrentWindow().show();
    showBackendStatus(initialStatus);
  }

  // Give the visible HTML splash a frame before requesting the application.
  await new Promise<void>((resolve) => requestAnimationFrame(() => resolve()));
  // Compile/load the frontend concurrently with native audio, DB and game setup.
  // Mount only after AppState is available to all normal application commands.
  const backendReady = isDesktop ? waitForBackend(initialStatus) : Promise.resolve();
  setBackendReady(backendReady);
  const [application] = await Promise.all([
    import('./bootstrap'),
    backendReady,
  ]);
  const text = document.getElementById('boot-status-text');
  if (text) text.textContent = 'Initializing interface…';
  startupProgress.advanceStartup(48);
  await application.mountApplication();
  stopBootProgress();
}

function showBackendStatus(status: StartupStatus) {
  if (status.error) throw new Error(status.error);
  const text = document.getElementById('boot-status-text');
  if (text) text.textContent = status.stage;
  document.getElementById('boot-progress')?.setAttribute('aria-label', status.stage);
  startupProgress.advanceStartup(status.ready ? 40 : nativeStartupProgress[status.stage] ?? 2);
}

async function waitForBackend(initialStatus?: StartupStatus) {
  const { invoke } = await import('@tauri-apps/api/core');
  let status = initialStatus;
  for (;;) {
    status ??= await invoke<StartupStatus>('get_startup_status');
    showBackendStatus(status);
    if (status.ready) return;
    await new Promise<void>((resolve) => setTimeout(resolve, 50));
    status = undefined;
  }
}

void start().catch((error) => {
  console.error('App startup failed:', error);
  const screen = document.getElementById('boot-screen');
  if (screen) {
    const message = document.getElementById('boot-status-text') ?? document.createElement('p');
    message.textContent = 'Unable to start Auto MIDI Player. Please restart the app.';
    if (!message.parentElement) screen.append(message);
  }
});
