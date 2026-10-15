import { mount, tick } from 'svelte';
import './app.css';
import App from './App.svelte';
import { logger } from './lib/infrastructure/logger';
import { telemetry } from './lib/infrastructure/telemetry';

export async function mountApplication() {
  telemetry.init();
  logger.info('Auto MIDI Player frontend initialized', 'main');

  const app = mount(App, {
    target: document.getElementById('app')!,
  });

  // Keep the first splash until its Svelte replacement has rendered and decoded.
  await tick();
  const logo = document.querySelector<HTMLImageElement>('.startup-logo');
  if (logo) await logo.decode().catch(() => {});
  await new Promise<void>((resolve) => requestAnimationFrame(() => resolve()));
  document.getElementById('boot-screen')?.remove();
  return app;
}
