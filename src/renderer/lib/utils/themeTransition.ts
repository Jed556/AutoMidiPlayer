import { tick } from 'svelte';
import { animationStore } from '../state/animationStore.svelte';
import { tooltipManager } from '../ui/tooltipManager.svelte';

interface RevealLayer {
  element: HTMLDivElement;
  animation?: Animation;
  observers: MutationObserver[];
}

const layers: RevealLayer[] = [];
let captureQueue = Promise.resolve();
let pendingCaptures = 0;
let snapshotId = 0;

/** Count expanding circles and queued captures, excluding the static base layer. */
export function getThemeRevealSpeed(): number {
  const expanding = layers.filter(layer => layer.animation?.playState === 'running').length;
  return Math.min(3, Math.max(1, 1 + (expanding + pendingCaptures - 1) * 0.5));
}

/** A non-interactive visual copy; the real controls remain clickable underneath. */
function snapshotPage(): RevealLayer {
  const observers: MutationObserver[] = [];
  const element = document.createElement('div');
  element.className = 'theme-reveal-layer';
  element.inert = true;
  element.setAttribute('aria-hidden', 'true');
  const rootStyle = getComputedStyle(document.documentElement);
  for (const property of rootStyle) {
    if (property.startsWith('--')) element.style.setProperty(property, rootStyle.getPropertyValue(property));
  }
  const bodyStyle = getComputedStyle(document.body);
  element.style.background = bodyStyle.backgroundColor;
  element.style.color = bodyStyle.color;
  element.style.font = bodyStyle.font;

  const prefix = `theme-snapshot-${++snapshotId}-`;
  const scrollPositions: Array<[Element, number, number]> = [];
  for (const source of Array.from(document.body.children)) {
    if (source.matches('.theme-reveal-layer, .amp-tooltip-portal-root, script, style, link')) continue;
    const copy = source.cloneNode(true) as Element;
    const originals = [source, ...source.querySelectorAll('*')];
    const copies = [copy, ...copy.querySelectorAll('*')];
    const targets = new Map(originals.map((original, index) => [original, copies[index]]));
    for (const live of originals.filter(node => node.hasAttribute('data-theme-reveal-live'))) {
      // Keep animated SVG geometry live while each layer retains its own colors.
      const observer = new MutationObserver(records => {
        for (const record of records) {
          const original = record.target as Element;
          const clone = targets.get(original);
          const name = record.attributeName;
          if (!clone || !name) continue;
          if (name === 'style' && (original instanceof HTMLElement || original instanceof SVGElement) &&
              (clone instanceof HTMLElement || clone instanceof SVGElement)) {
            // Merge motion styles without discarding the snapshot's frozen paint.
            for (const property of original.style) {
              clone.style.setProperty(property, original.style.getPropertyValue(property));
            }
          } else {
            const value = original.getAttribute(name);
            if (value === null) clone.removeAttribute(name);
            else clone.setAttribute(name, value);
          }
        }
      });
      observer.observe(live, { attributes: true, subtree: true, attributeFilter: ['d', 'transform', 'style'] });
      observers.push(observer);
    }
    originals.forEach((original, index) => {
      const clone = copies[index];
      // Freeze paint values too: a few controls use :root theme selectors
      // instead of inherited tokens, and older waves must retain their palette.
      if (clone instanceof HTMLElement || clone instanceof SVGElement) {
        const paint = getComputedStyle(original);
        for (const property of ['background', 'color', 'border-color', 'box-shadow', 'fill', 'stroke', 'opacity']) {
          clone.style.setProperty(property, paint.getPropertyValue(property));
        }
      }
      scrollPositions.push([clone, original.scrollLeft, original.scrollTop]);
      if (original instanceof HTMLCanvasElement && clone instanceof HTMLCanvasElement) {
        if (original.width && original.height) clone.getContext('2d')?.drawImage(original, 0, 0);
      } else if (original instanceof HTMLInputElement && clone instanceof HTMLInputElement) {
        clone.name = prefix + original.name;
        clone.value = original.value;
        clone.checked = original.checked;
      } else if (original instanceof HTMLTextAreaElement && clone instanceof HTMLTextAreaElement) {
        clone.value = original.value;
      } else if (original instanceof HTMLSelectElement && clone instanceof HTMLSelectElement) {
        clone.value = original.value;
      }
    });
    // Keep SVG/label references local to each copy, and omit closing popups.
    const ids = new Map(copies.filter(node => node.id).map(node => [node.id, prefix + node.id]));
    for (const clone of copies) {
      if (clone.id) clone.id = ids.get(clone.id)!;
      for (const attribute of Array.from(clone.attributes)) {
        let value = attribute.value.replace(/url\(#([^)]*)\)/g, (match, id) => ids.has(id) ? `url(#${ids.get(id)})` : match);
        if (attribute.name === 'href' && value.startsWith('#') && ids.has(value.slice(1))) value = `#${ids.get(value.slice(1))}`;
        if (['for', 'aria-labelledby', 'aria-describedby'].includes(attribute.name)) value = value.split(' ').map(id => ids.get(id) ?? id).join(' ');
        if (value !== attribute.value) clone.setAttribute(attribute.name, value);
      }
    }
    copy.querySelectorAll('script, [inert]').forEach(node => node.remove());
    if (copy instanceof HTMLElement) {
      const bounds = source.getBoundingClientRect();
      Object.assign(copy.style, {
        position: 'absolute', left: `${bounds.left}px`, top: `${bounds.top}px`,
        width: `${bounds.width}px`, height: `${bounds.height}px`, margin: '0',
      });
    }
    element.append(copy);
  }
  document.body.append(element);
  for (const [node, left, top] of scrollPositions) {
    if (left) node.scrollLeft = left;
    if (top) node.scrollTop = top;
  }
  const layer = { element, observers };
  layers.push(layer);
  return layer;
}

function removeLayers(count = layers.length) {
  for (const layer of layers.splice(0, count)) {
    for (const observer of layer.observers) observer.disconnect();
    layer.animation?.cancel();
    layer.element.remove();
  }
  if (!layers.length) document.documentElement.classList.remove('theme-revealing');
}

async function startReveal(origin: HTMLElement, update: () => void) {
  const root = document.documentElement;
  if (animationStore.isOff || document.hidden || window.matchMedia('(prefers-reduced-motion: reduce)').matches) {
    removeLayers();
    update();
    return;
  }

  const bounds = origin.getBoundingClientRect();
  const x = bounds.left + bounds.width / 2;
  const y = bounds.top + bounds.height / 2;
  const radius = Math.ceil(Math.hypot(Math.max(x, innerWidth - x), Math.max(y, innerHeight - y)));
  const previousTheme = root.dataset.theme;
  root.classList.add('theme-revealing');
  tooltipManager.hide(true);
  await tick();
  if (!layers.length) snapshotPage();
  update();
  await tick();
  // Queue only snapshot preparation, never the lifetime of an expanding circle.
  // Give Studio canvases a frame to paint the new palette before copying them.
  await new Promise<void>(resolve => requestAnimationFrame(() => resolve()));
  if (root.dataset.theme === previousTheme) {
    if (layers.length === 1 && !layers[0].animation) removeLayers();
    return;
  }
  const layer = snapshotPage();
  layer.animation = layer.element.animate(
    { clipPath: [`circle(0px at ${x}px ${y}px)`, `circle(${radius}px at ${x}px ${y}px)`] },
    { duration: Math.round(animationStore.durationMs * 2.8), easing: 'cubic-bezier(0.4, 0, 0.2, 1)', fill: 'both' },
  );
  void layer.animation.finished.then(() => {
    const index = layers.indexOf(layer);
    if (index < 0) return;
    // A full-size circle covers older layers, so release their copies.
    removeLayers(index);
    if (layers.at(-1) === layer && pendingCaptures === 0) removeLayers();
  }).catch(() => {});
}

/** Add an expanding theme wave without restarting existing circles. */
export function revealThemeFrom(origin: HTMLElement, update: () => void): Promise<void> {
  pendingCaptures++;
  let updated = false;
  const capture = captureQueue.then(() => startReveal(origin, () => { updated = true; update(); }));
  captureQueue = capture.catch(() => {
    removeLayers();
    if (!updated) update();
  }).finally(() => {
    pendingCaptures--;
    if (!pendingCaptures && layers.every(layer => !layer.animation || layer.animation.playState === 'finished')) removeLayers();
  });
  return captureQueue;
}
