// Framework-independent so the very first HTML frame and Svelte share one bar.
export function createStartupProgress(clock = {
  now: () => performance.now(),
  frame: (callback: FrameRequestCallback) => requestAnimationFrame(callback),
  cancel: (id: number) => cancelAnimationFrame(id),
}) {
  let current = 0;
  let target = 0;
  let from = 0;
  let started = 0;
  let duration = 80;
  let frame: number | null = null;
  let revision = 0;
  let startupFloor = 0;
  const listeners = new Set<(value: number) => void>();

  function animate(now: number, version: number) {
    if (version !== revision) return;
    frame = null;
    const elapsed = Math.max(0, Math.min(1, (now - started) / duration));
    // Respond on the first frame, then settle quickly at the reported milestone.
    const eased = 1 - (1 - elapsed) ** 3;
    current = Math.max(current, from + (target - from) * eased);
    for (const listener of listeners) listener(current);
    if (elapsed < 1) frame = clock.frame(now => animate(now, version));
  }

  function reset(value: number) {
    if (frame !== null) clock.cancel(frame);
    frame = null;
    ++revision;
    current = target = from = value;
    for (const listener of listeners) listener(current);
  }

  return {
    get current() { return current; },
    beginUpdate() {
      startupFloor = 75;
      reset(0);
    },
    resumeUpdate(progress: number) {
      startupFloor = Number.isFinite(progress) ? Math.max(0, Math.min(99, progress)) : 75;
      reset(startupFloor);
    },
    advanceStartup(value: number) {
      this.advance(startupFloor + (100 - startupFloor) * value / 100);
    },
    advance(value: number) {
      if (!Number.isFinite(value) || value <= target) return;
      const now = clock.now();
      const remaining = frame === null ? Infinity : Math.max(16, started + duration - now);
      if (frame !== null) clock.cancel(frame);
      const version = ++revision;
      target = Math.min(100, value);
      from = current;
      // Replace the old destination without extending its animation deadline.
      duration = Math.min(remaining, Math.max(80, Math.min(120, (target - from) * 3)));
      started = now;
      frame = clock.frame(now => animate(now, version));
    },
    subscribe(listener: (value: number) => void) {
      listeners.add(listener);
      listener(current);
      return () => { listeners.delete(listener); };
    },
  };
}

export const startupProgress = createStartupProgress();

// These are completed startup milestones, not a time estimate.
export const nativeStartupProgress: Record<string, number> = {
  'Initializing application…': 2,
  'Initializing audio…': 8,
  'Initializing player…': 18,
  'Loading library database…': 24,
  'Migrating library database…': 27,
  'Loading game configurations…': 30,
  'Initializing game monitor…': 36,
  'Loading interface…': 40,
};

export const frontendStartupProgress: Record<string, number> = {
  'startup.settings': 48,
  'startup.interface': 54,
  'startup.songsAndGames': 62,
  'startup.player': 76,
  'startup.queue': 82,
  'startup.preferences': 88,
  'startup.view': 94,
  'startup.updates': 97,
};
