import { tooltipManager, type TooltipOptions } from '../ui/tooltipManager.svelte';

export type TooltipParam = string | TooltipOptions | undefined | null;

export function tooltip(node: HTMLElement, param: TooltipParam) {
  let currentOptions: TooltipOptions | null = null;

  function parseParam(p: TooltipParam): TooltipOptions | null {
    if (!p) return null;
    const base: TooltipOptions = typeof p === 'string' ? { text: p } : { ...p };
    const position = (node.getAttribute('data-tooltip-position') || node.getAttribute('data-position')) as any;
    const followAttr = node.getAttribute('data-tooltip-follow');
    const followCursor = followAttr === 'true' ? true : followAttr === 'false' ? false : undefined;
    const isAttrDisabled =
      node.getAttribute('data-tooltip-disabled') === 'true' ||
      node.getAttribute('data-tooltip-disabled') === '' ||
      Boolean(node.closest?.('[data-tooltip-disabled="true"], [data-tooltip-disabled=""]'));
    return {
      ...(position && !base.position ? { position } : {}),
      ...(followCursor !== undefined && base.followCursor === undefined ? { followCursor } : {}),
      ...(isAttrDisabled ? { disabled: true } : {}),
      ...base
    };
  }

  function hasContent(opts: TooltipOptions | null): boolean {
    if (!opts || opts.disabled) return false;
    return Boolean(opts.text?.trim() || opts.contentType === 'pedals');
  }

  function syncAttributes(opts: TooltipOptions | null) {
    if (opts && hasContent(opts)) {
      node.setAttribute('data-amp-tooltip', opts.text || (opts.contentType === 'pedals' ? 'pedals' : ''));
      if (opts.contentType) {
        node.setAttribute('data-tooltip-content', opts.contentType);
      } else {
        node.removeAttribute('data-tooltip-content');
      }
    }
  }

  currentOptions = parseParam(param);

  // If node has native title, strip it to prevent duplicate/native browser tooltip
  if (node.hasAttribute('title')) {
    const nativeTitle = node.getAttribute('title');
    if ((!currentOptions || !currentOptions.text) && nativeTitle) {
      currentOptions = { ...(currentOptions || {}), text: nativeTitle };
    }
    node.removeAttribute('title');
  }

  syncAttributes(currentOptions);

  function handleMouseEnter(e: MouseEvent) {
    if (hasContent(currentOptions)) {
      tooltipManager.show(node, currentOptions!, e);
    }
  }

  function handleMouseMove(e: MouseEvent) {
    if (currentOptions && !currentOptions.disabled) {
      tooltipManager.updateMousePosition(e);
    }
  }

  function handleMouseLeave() {
    tooltipManager.hideAfterUpdate(false, node);
  }

  function handleFocus() {
    if (hasContent(currentOptions)) {
      tooltipManager.show(node, currentOptions!);
    }
  }

  function handleBlur() {
    tooltipManager.hideAfterUpdate(false, node);
  }

  node.addEventListener('mouseenter', handleMouseEnter);
  node.addEventListener('mousemove', handleMouseMove, { passive: true });
  node.addEventListener('mouseleave', handleMouseLeave);
  node.addEventListener('focus', handleFocus);
  node.addEventListener('blur', handleBlur);

  return {
    update(newParam: TooltipParam) {
      currentOptions = parseParam(newParam);
      if (node.hasAttribute('title')) {
        const nativeTitle = node.getAttribute('title');
        if ((!currentOptions || !currentOptions.text) && nativeTitle) {
          currentOptions = { ...(currentOptions || {}), text: nativeTitle };
        }
        node.removeAttribute('title');
      }
      syncAttributes(currentOptions);
      if (tooltipManager.isCurrentTarget(node) && hasContent(currentOptions)) {
        tooltipManager.updateOptionsAfterUpdate(node, currentOptions!);
      }
      if (!hasContent(currentOptions)) {
        tooltipManager.hideAfterUpdate(true, node);
      }
    },
    destroy() {
      tooltipManager.hideAfterUpdate(true, node);
      node.removeEventListener('mouseenter', handleMouseEnter);
      node.removeEventListener('mousemove', handleMouseMove);
      node.removeEventListener('mouseleave', handleMouseLeave);
      node.removeEventListener('focus', handleFocus);
      node.removeEventListener('blur', handleBlur);
    }
  };
}
