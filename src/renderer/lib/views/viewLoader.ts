const loaders = {
  songs: () => import('./SongsView.svelte'),
  tracks: () => import('./TracksView.svelte'),
  studio: () => import('./StudioView.svelte'),
  sheet: () => import('./PianoSheetView.svelte'),
  instrument: () => import('./InstrumentView.svelte'),
  queue: () => import('./QueueView.svelte'),
  settings: () => import('./SettingsView.svelte'),
  about: () => import('./AboutView.svelte'),
  'icon-debug': () => import('./IconDebugView.svelte'),
};

type View = Awaited<ReturnType<typeof loaders[keyof typeof loaders]>>;
const loaded = new Map<string, Promise<View>>();

export function loadView(tab: string): Promise<View> {
  const key = tab === 'icons' ? 'icon-debug' : tab in loaders ? tab as keyof typeof loaders : 'songs';
  let view = loaded.get(key);
  if (!view) {
    view = loaders[key]().catch(error => {
      loaded.delete(key);
      throw error;
    });
    loaded.set(key, view);
  }
  return view;
}
