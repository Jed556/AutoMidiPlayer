const TEXT_INPUT_TYPES = new Set([
  '',
  'email',
  'number',
  'password',
  'search',
  'tel',
  'text',
  'url',
]);

interface ElementLike {
  closest?: (selector: string) => ElementLike | null;
  parentElement?: ElementLike | null;
  tagName?: string;
  type?: string;
  isContentEditable?: boolean;
  getAttribute?: (name: string) => string | null;
}

/** Returns true when the target should keep the platform's default context menu (text editing). */
export function shouldAllowNativeContextMenu(target: EventTarget | null): boolean {
  if (!target || typeof target !== 'object') return false;
  const candidate = target as ElementLike;
  const element = typeof candidate.closest === 'function' ? candidate : candidate.parentElement ?? null;
  if (!element || typeof element.closest !== 'function') return false;

  // Never expose browser context menus inside the left navigation drawer.
  if (element.closest('.nav-drawer')) return false;

  const editable = element.closest('input, textarea, [contenteditable]');
  if (!editable) return false;

  const tag = editable.tagName?.toUpperCase();
  if (tag === 'TEXTAREA') return true;
  if (tag === 'INPUT') {
    const inputType = (editable.type ?? '').toLowerCase();
    return TEXT_INPUT_TYPES.has(inputType);
  }
  const attr = editable.getAttribute?.('contenteditable');
  return editable.isContentEditable === true || (attr !== null && attr !== undefined && attr.toLowerCase() !== 'false');
}

/** Suppresses the webview's default context menu outside editable text controls. */
export function handleAppContextMenu(event: Pick<MouseEvent, 'defaultPrevented' | 'target' | 'preventDefault'>): void {
  if (event.defaultPrevented) return;
  if (!shouldAllowNativeContextMenu(event.target)) {
    event.preventDefault();
  }
}

export interface ContextMenuLayout {
  left: number;
  top: number;
  originX: number;
  originY: number;
}

/**
 * Computes clamped floating context menu coordinates and a cursor-anchored transform origin.
 * Flips horizontally or vertically when overflowing the viewport and space permits.
 */
export function computeContextMenuPosition(
  anchor: { x: number; y: number },
  menuSize: { width: number; height: number },
  viewport: { width: number; height: number },
  margin = 8,
): ContextMenuLayout {
  const width = Math.max(0, menuSize.width);
  const height = Math.max(0, menuSize.height);
  const maxLeft = Math.max(margin, viewport.width - width - margin);
  const maxTop = Math.max(margin, viewport.height - height - margin);
  const flipX = anchor.x + width + margin > viewport.width && anchor.x - width >= margin;
  const flipY = anchor.y + height + margin > viewport.height && anchor.y - height >= margin;
  const left = Math.max(margin, Math.min(flipX ? anchor.x - width : anchor.x, maxLeft));
  const top = Math.max(margin, Math.min(flipY ? anchor.y - height : anchor.y, maxTop));
  const originX = Math.max(0, Math.min(width, anchor.x - left));
  const originY = Math.max(0, Math.min(height, anchor.y - top));

  return { left, top, originX, originY };
}
