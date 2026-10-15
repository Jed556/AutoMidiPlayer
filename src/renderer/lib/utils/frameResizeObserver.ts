/**
 * Deliver resize-driven UI updates on the next animation frame. Updating Svelte
 * state inside ResizeObserver's delivery cycle can change an observed ancestor's
 * layout and produce "loop completed with undelivered notifications" errors.
 */
export function createFrameResizeObserver(
  callback: (entries: ResizeObserverEntry[]) => void,
): Pick<ResizeObserver, 'observe' | 'unobserve' | 'disconnect'> {
  const pending = new Map<Element, ResizeObserverEntry>();
  let frame: number | null = null;

  const observer = new ResizeObserver((entries) => {
    for (const entry of entries) pending.set(entry.target, entry);
    if (frame !== null) return;
    frame = requestAnimationFrame(() => {
      frame = null;
      const latest = [...pending.values()];
      pending.clear();
      if (latest.length > 0) callback(latest);
    });
  });

  return {
    observe: (target, options) => observer.observe(target, options),
    unobserve(target) {
      observer.unobserve(target);
      pending.delete(target);
    },
    disconnect() {
      observer.disconnect();
      if (frame !== null) cancelAnimationFrame(frame);
      frame = null;
      pending.clear();
    },
  };
}
