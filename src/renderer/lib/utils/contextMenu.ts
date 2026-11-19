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
