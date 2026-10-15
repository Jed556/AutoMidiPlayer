/** Replace a label along the same diagonal edge used by morphing tooltips. */
export function animateTextSweep(
  label: HTMLElement,
  previous: HTMLElement,
  content: HTMLElement,
  duration: number,
  previousWidth = previous.offsetWidth,
): Animation[] {
  previous.setAttribute('aria-hidden', 'true');
  previous.style.cssText = `position:absolute;left:0;top:0;width:${previousWidth}px;pointer-events:none;`;
  content.appendChild(previous);

  const extent = Math.max(previousWidth, label.offsetWidth) + label.offsetHeight;
  const slant = Math.max(previous.offsetHeight, label.offsetHeight);
  const reveal = (edge: number) => `polygon(0px 0%, ${edge + slant}px 0%, ${edge}px 100%, 0px 100%)`;
  const conceal = (edge: number) => `polygon(${edge + slant}px 0%, ${extent}px 0%, ${extent}px 100%, ${edge}px 100%)`;
  const timing = { duration, easing: 'cubic-bezier(0.16, 1, 0.3, 1)' };
  const animations = [
    previous.animate([{ clipPath: conceal(-slant) }, { clipPath: conceal(extent) }], timing),
    label.animate([{ clipPath: reveal(-slant) }, { clipPath: reveal(extent) }], timing),
  ];
  void animations[1].finished.then(() => previous.remove()).catch(() => {});
  return animations;
}
