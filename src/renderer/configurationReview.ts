import { mount, tick, unmount } from 'svelte';
import './app.css';
import ConfigUpdatesScreen from './lib/components/Layout/ConfigUpdatesScreen.svelte';
import { defaultConfigStore } from './lib/state/defaultConfigStore.svelte';
import { themeStore } from './lib/state/themeStore.svelte';
import { animationStore } from './lib/state/animationStore.svelte';
import { api } from './lib/backend';
import { fadeConfigurationSurface } from './lib/utils/configurationReviewFade';

/** Mount only the setup view; App, audio, DB, playback, and process monitors stay deferred. */
export async function reviewConfigurations(): Promise<void> {
  await Promise.all([
    defaultConfigStore.load(),
    api.settings.getSettings().then(settings => themeStore.init(settings.theme, settings.accentColor)).catch(console.warn),
    animationStore.init(),
  ]);
  const report = defaultConfigStore.report;
  if (!defaultConfigStore.error && !report?.conflicts.length && !report?.duplicates.length && !report?.issues.length) return;
  let proceed!: () => void;
  const finished = new Promise<void>(resolve => { proceed = resolve; });
  const view = mount(ConfigUpdatesScreen, {
    target: document.getElementById('app')!, props: { oncontinue: proceed, onskip: proceed },
  });
  await tick();
  const splash = document.getElementById('boot-screen');
  const screen = document.querySelector<HTMLElement>('.config-updates-screen')!;
  const fadeDuration = () => window.matchMedia('(prefers-reduced-motion: reduce)').matches ? 0 : animationStore.durationMs;
  try {
    await fadeConfigurationSurface(splash, screen, fadeDuration());
    await finished;
  }
  finally {
    // Do not wake the native runtime until the loader has faded back in.
    if (splash) await fadeConfigurationSurface(screen, splash, fadeDuration());
    await unmount(view);
  }
}
