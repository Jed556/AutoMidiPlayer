import { scrollPositionStore } from '../state/scrollPositionStore.js';
import type { ScrollPositionStore } from '../utils/scrollPositions.js';

/** A pending restoration survives empty/loading content until layout can satisfy it. */
export function attachScrollPersistence(node: HTMLElement, key: string, store: ScrollPositionStore = scrollPositionStore) {
  let disposed = false;
  let loaded = false;
  let restoring = true;
  let interacted = false;
  let target: { x: number; y: number } | undefined;
  let frame = 0;

  const remember = () => {
    if (loaded && !restoring) store.set(key, { x: node.scrollLeft, y: node.scrollTop });
  };
  const restore = () => {
    frame = 0;
    if (disposed || !loaded || !restoring || !target) return;
    const maxX = Math.max(0, node.scrollWidth - node.clientWidth);
    const maxY = Math.max(0, node.scrollHeight - node.clientHeight);
    node.scrollLeft = Math.min(target.x, maxX);
    node.scrollTop = Math.min(target.y, maxY);
    // Notify virtualization/fade listeners immediately; browsers can defer scroll events.
    node.dispatchEvent(new Event('scroll'));
    if (target.x <= maxX && target.y <= maxY) restoring = false;
  };
  const refresh = () => {
    if (!disposed && !frame) frame = requestAnimationFrame(restore);
  };
  const interact = () => {
    interacted = true;
    if (loaded) { restoring = false; remember(); }
  };
  const observer = typeof ResizeObserver !== 'undefined' ? new ResizeObserver(refresh) : null;
  observer?.observe(node);
  const observeChildren = () => {
    for (const child of node.children) observer?.observe(child);
    refresh();
  };
  const mutations = typeof MutationObserver !== 'undefined' ? new MutationObserver(observeChildren) : null;
  mutations?.observe(node, { childList: true, subtree: true, characterData: true });
  observeChildren();
  node.addEventListener('scroll', remember, { passive: true });
  for (const event of ['wheel', 'pointerdown', 'touchstart', 'keydown']) node.addEventListener(event, interact, { passive: true });
  void store.ready().then(() => {
    if (disposed) return;
    loaded = true;
    target = store.get(key) ?? { x: 0, y: 0 };
    restoring = !interacted;
    if (restoring) refresh();
    else if (interacted) remember();
  });

  return () => {
    // Scroll events record synchronously. Reading the node here could capture a
    // new song/playlist's already-updated layout under the departing key.
    disposed = true;
    if (frame) cancelAnimationFrame(frame);
    observer?.disconnect();
    mutations?.disconnect();
    node.removeEventListener('scroll', remember);
    for (const event of ['wheel', 'pointerdown', 'touchstart', 'keydown']) node.removeEventListener(event, interact);
  };
}

export function persistScroll(node: HTMLElement, key: string) {
  let detach = attachScrollPersistence(node, key);
  return {
    update(next: string) { detach(); detach = attachScrollPersistence(node, next); },
    destroy() { detach(); },
  };
}
