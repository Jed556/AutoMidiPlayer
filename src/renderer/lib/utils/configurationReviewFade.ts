/** Keep both surfaces mounted until the fade finishes so the loader never flashes. */
export async function fadeConfigurationSurface(
  outgoing: HTMLElement | null,
  incoming: HTMLElement,
  duration: number,
): Promise<void> {
  incoming.inert = true;
  incoming.style.opacity = '0';
  incoming.style.removeProperty('display');
  if (outgoing) outgoing.inert = true;
  const animations: Animation[] = [];
  try {
    if (duration > 0 && typeof incoming.animate === 'function') {
      const options: KeyframeAnimationOptions = { duration, easing: 'ease-in-out', fill: 'both' };
      animations.push(incoming.animate([{ opacity: 0 }, { opacity: 1 }], options));
      if (outgoing) animations.push(outgoing.animate([{ opacity: 1 }, { opacity: 0 }], options));
      await Promise.allSettled(animations.map(animation => animation.finished));
    }
  } finally {
    incoming.style.opacity = '1';
    incoming.inert = false;
    if (outgoing) outgoing.style.display = 'none';
    animations.forEach(animation => animation.cancel());
  }
}
