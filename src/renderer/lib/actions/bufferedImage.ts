interface BufferedImageOptions {
  src?: string;
  fallbackSrc?: string;
  onUnavailable?: () => void;
}

/** Keep the previous bitmap visible until a replacement has loaded and decoded. */
export function bufferedImage(node: HTMLElement, initial: BufferedImageOptions) {
  let revision = 0;
  let sourceKey: string | null = null;
  let pending: HTMLImageElement | null = null;
  let options = initial;

  function update(next: BufferedImageOptions) {
    options = next;
    const key = JSON.stringify([next.src, next.fallbackSrc]);
    if (key === sourceKey) return;
    sourceKey = key;
    const request = ++revision;
    pending?.removeAttribute('src');
    pending = null;
    const sources = [...new Set([next.src, next.fallbackSrc].filter((src): src is string => Boolean(src)))];

    void (async () => {
      for (const src of sources) {
        if (request !== revision) return;
        const image = new Image();
        pending = image;
        image.alt = '';
        image.decoding = 'async';
        image.style.cssText = 'display:block;width:100%;height:100%;object-fit:inherit;border-radius:inherit;';
        image.src = src;
        try {
          await image.decode();
          if (request !== revision) return;
          // Insert the decoded node itself, avoiding another image request during the swap.
          node.replaceChildren(image);
          pending = null;
          return;
        } catch {
          // Failed candidates stay off-screen while the fallback is decoded.
        }
      }
      if (request === revision) {
        pending = null;
        options.onUnavailable?.();
      }
    })();
  }

  update(initial);
  return {
    update,
    destroy() {
      revision++;
      pending?.removeAttribute('src');
      pending = null;
      node.replaceChildren();
    },
  };
}
