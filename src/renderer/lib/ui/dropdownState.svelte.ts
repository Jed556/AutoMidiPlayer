import { onDestroy } from 'svelte';
import { popupScaleOut } from '../utils/transitions';

let activeDropdown = $state<symbol | null>(null);
const layers = new Map<symbol, Set<HTMLElement>>();

/** One owner for a dropdown and its child flyout, shared by all picker types. */
export function createDropdownState() {
  const owner = Symbol('dropdown');

  function close() {
    if (activeDropdown === owner) activeDropdown = null;
  }

  // Component destruction can run during a render; release only this owner afterward.
  onDestroy(() => queueMicrotask(close));

  return {
    get isOpen() { return activeDropdown === owner; },
    set isOpen(open: boolean) {
      if (!open) {
        close();
        return;
      }
      // Hide outgoing layers too, so their exit animation cannot overlap a new menu.
      for (const [id, elements] of layers) {
        for (const element of elements) {
          element.style.visibility = id === owner ? '' : 'hidden';
          element.inert = id !== owner;
        }
      }
      activeDropdown = owner;
    },
    layer(node: HTMLElement) {
      const elements = layers.get(owner) ?? new Set<HTMLElement>();
      layers.set(owner, elements);
      elements.add(node);
      return {
        destroy() {
          elements.delete(node);
          if (!elements.size) layers.delete(owner);
        }
      };
    },
    outro(node: Element) {
      return activeDropdown !== null && activeDropdown !== owner
        ? { duration: 0 }
        : popupScaleOut(node);
    }
  };
}
