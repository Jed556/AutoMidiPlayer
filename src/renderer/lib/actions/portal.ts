/**
 * Svelte action that teleports a DOM element directly to document.body (or a specified container),
 * completely escaping any parent overflow:hidden, mask-image, transform, contain, or z-index stacking contexts.
 */
export function portal(node: HTMLElement, target: HTMLElement = document.body) {
  function mountToTarget(dest: HTMLElement) {
    if (dest && node.parentElement !== dest) {
      dest.appendChild(node);
    }
  }

  if (typeof document !== 'undefined') {
    mountToTarget(target || document.body);
  }

  return {
    update(newTarget: HTMLElement) {
      if (typeof document !== 'undefined') {
        mountToTarget(newTarget || document.body);
      }
    },
    destroy() {
      if (node.parentElement) {
        node.parentElement.removeChild(node);
      }
    }
  };
}
