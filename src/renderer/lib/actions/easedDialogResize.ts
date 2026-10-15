import { animationStore } from '../state/animationStore.svelte';

/** Animate intrinsic height changes without scaling the dialog's contents. */
export function easedDialogResize(node: HTMLElement, enabled = true) {
  let height = node.offsetHeight;
  let animation: Animation | null = null;
  let frame = 0;
  const reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)');

  function measure() {
    frame = 0;
    const from = animation ? node.offsetHeight : height;
    animation?.cancel();
    animation = null;
    height = node.offsetHeight;
    if (!enabled || reducedMotion.matches || animationStore.isOff || Math.abs(from - height) < 1) return;

    const current = node.animate(
      [{ height: `${from}px` }, { height: `${height}px` }],
      { duration: animationStore.durationMs, easing: 'cubic-bezier(0.1, 0.9, 0.2, 1)' },
    );
    animation = current;
    current.onfinish = () => {
      if (animation === current) animation = null;
    };
  }

  function schedule() {
    if (!frame) frame = requestAnimationFrame(measure);
  }

  // Content changes can arrive while a previous resize is still running.
  const mutations = new MutationObserver(schedule);
  mutations.observe(node, { childList: true, subtree: true, characterData: true });
  const sizes = new ResizeObserver(() => {
    if (!animation && Math.abs(node.offsetHeight - height) >= 1) schedule();
  });
  sizes.observe(node);
  window.addEventListener('resize', schedule);
  reducedMotion.addEventListener('change', schedule);

  return {
    update(value: boolean) { enabled = value; schedule(); },
    destroy() {
      mutations.disconnect();
      sizes.disconnect();
      window.removeEventListener('resize', schedule);
      reducedMotion.removeEventListener('change', schedule);
      cancelAnimationFrame(frame);
      animation?.cancel();
    },
  };
}
