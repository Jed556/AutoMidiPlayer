import { animationStore } from '../state/animationStore.svelte.js';

export type TooltipPosition = 'top' | 'bottom' | 'left' | 'right';
export type TooltipVariant = 'dark' | 'light' | 'fluent' | 'danger';

export interface TooltipOptions {
  text?: string;
  position?: TooltipPosition | 'up' | 'down';
  delay?: number;
  exitDelay?: number;
  arrow?: boolean;
  disabled?: boolean;
  className?: string;
  variant?: TooltipVariant;
  maxWidth?: string;
  interactive?: boolean;
  /** Track the pointer when true; anchor to the element when false. Omit to use the global default. */
  followCursor?: boolean;
}

class TooltipManager {
  isRendered = $state(false);
  isVisible = $state(false);
  coords = $state<{
    top: number;
    left: number;
    actualPosition: TooltipPosition;
  }>({
    top: 0,
    left: 0,
    actualPosition: 'top'
  });

  options = $state<TooltipOptions>({});
  contentRevision = $state(0);
  morphContent = $state(false);
  followCursor = $state(false);
  defaultPosition = $state<TooltipPosition>('top');

  private currentTarget: HTMLElement | null = null;
  private targetRevision = 0;
  private enterTimeout: any = null;
  private exitTimeout: any = null;
  private handoffTimeout: ReturnType<typeof setTimeout> | null = null;
  private tooltipEl: HTMLElement | null = null;
  private listenersAttached = false;
  private globalInitialized = false;
  private lastMousePos = { x: 0, y: 0 };
  private pointerTriggered = false;
  private rafPending = false;
  private globalObserver: MutationObserver | null = null;

  isCurrentTarget(el: HTMLElement | null): boolean {
    return this.currentTarget === el;
  }

  setFollowCursor(val: boolean) {
    this.followCursor = val;
    this.updatePosition();
  }

  setDefaultPosition(pos: TooltipPosition) {
    this.defaultPosition = pos;
    this.updatePosition();
  }

  registerTooltipElement(el: HTMLElement | null) {
    this.tooltipEl = el;
  }

  updateOptionsAfterUpdate(target: HTMLElement, options: TooltipOptions) {
    // Action updates can run during Svelte rendering, just like focusout.
    const revision = this.targetRevision;
    queueMicrotask(() => {
      if (this.currentTarget !== target || this.targetRevision !== revision) return;
      if (options.text !== this.options.text) {
        this.morphContent = this.isVisible;
        this.contentRevision++;
      }
      this.options = options;
      this.updatePosition();
      requestAnimationFrame(() => {
        if (this.currentTarget === target && this.targetRevision === revision) this.updatePosition();
      });
    });
  }

  private get shouldFollow() {
    // Keyboard focus has no pointer anchor; use the focused element instead.
    return this.pointerTriggered && (this.options.followCursor ?? this.followCursor);
  }

  updateCurrentText(text: string) {
    if (!this.currentTarget || !this.isRendered) return;
    if (
      this.currentTarget.getAttribute('data-tooltip-disabled') === 'true' ||
      this.currentTarget.getAttribute('data-tooltip-disabled') === '' ||
      Boolean(this.currentTarget.closest?.('[data-tooltip-disabled="true"], [data-tooltip-disabled=""]'))
    ) {
      this.hide();
      return;
    }
    if (text === this.options.text) return;
    this.morphContent = this.isVisible;
    this.contentRevision++;
    this.options = { ...this.options, text };
    this.updatePosition();
    requestAnimationFrame(() => {
      this.updatePosition();
    });
  }

  show(target: HTMLElement, opts: TooltipOptions | string, mouseEvent?: MouseEvent) {
    if (document.documentElement.classList.contains('theme-revealing')) {
      this.hide(true);
      return;
    }
    const parsedOpts: TooltipOptions = typeof opts === 'string' ? { text: opts } : opts;

    // Check disabled condition
    const isAttrDisabled =
      target.getAttribute('data-tooltip-disabled') === 'true' ||
      target.getAttribute('data-tooltip-disabled') === '' ||
      Boolean(target.closest?.('[data-tooltip-disabled="true"], [data-tooltip-disabled=""]'));

    if (parsedOpts.disabled || isAttrDisabled || !parsedOpts.text || !parsedOpts.text.trim()) {
      this.hide();
      return;
    }

    this.targetRevision++;
    this.pointerTriggered = Boolean(mouseEvent);
    if (mouseEvent) {
      this.lastMousePos = { x: mouseEvent.clientX, y: mouseEvent.clientY };
    }

    // Bridge small gaps between controls without starting an exit animation.
    const previous = this.currentTarget?.getBoundingClientRect();
    const next = target.getBoundingClientRect();
    const distance = previous ? Math.hypot(
      Math.max(0, previous.left - next.right, next.left - previous.right),
      Math.max(0, previous.top - next.bottom, next.top - previous.bottom)
    ) : Infinity;
    const isAlreadyActive = this.isVisible && distance <= 120;
    const initialDelay = isAlreadyActive ? 0 : (parsedOpts.delay ?? 80);

    if (this.handoffTimeout) {
      clearTimeout(this.handoffTimeout);
      this.handoffTimeout = null;
    }

    if (this.exitTimeout) {
      clearTimeout(this.exitTimeout);
      this.exitTimeout = null;
    }
    if (this.enterTimeout) {
      clearTimeout(this.enterTimeout);
      this.enterTimeout = null;
    }

    this.morphContent = isAlreadyActive;
    this.contentRevision++;
    if (!isAlreadyActive) this.isVisible = false;
    this.currentTarget = target;
    this.options = parsedOpts;

    if (isAlreadyActive) {
      this.updatePosition();
      return;
    }

    this.enterTimeout = setTimeout(() => {
      this.updatePosition();
      this.isRendered = true;
      this.attachListeners();

      // Double rAF ensures the element is mounted in DOM before animating
      requestAnimationFrame(() => {
        requestAnimationFrame(() => {
          if (this.currentTarget === target) {
            this.updatePosition();
            this.isVisible = true;
          }
        });
      });
    }, initialDelay);
  }

  hideAfterUpdate(immediate = false, target = this.currentTarget) {
    // Removing a focused node fires focusout synchronously inside Svelte's
    // render pass. Wait until it finishes before updating the tooltip state.
    const revision = this.targetRevision;
    queueMicrotask(() => {
      if (this.currentTarget === target && this.targetRevision === revision) this.hide(immediate);
    });
  }

  hide(immediate = false) {
    if (this.enterTimeout) {
      clearTimeout(this.enterTimeout);
      this.enterTimeout = null;
    }
    if (this.exitTimeout) {
      clearTimeout(this.exitTimeout);
      this.exitTimeout = null;
    }

    if (this.handoffTimeout) clearTimeout(this.handoffTimeout);
    this.handoffTimeout = null;

    if (!immediate && this.isVisible) {
      this.handoffTimeout = setTimeout(() => {
        this.handoffTimeout = null;
        this.dismiss();
      }, 120);
      return;
    }

    this.dismiss(immediate);
  }

  private dismiss(immediate = false) {

    this.currentTarget = null;
    this.isVisible = false;

    const isOff =
      animationStore.isOff ||
      (typeof document !== 'undefined' &&
        document.documentElement.classList.contains('no-animations'));

    const animDurationMs = isOff ? 0 : animationStore.durationMs;
    const exitDelay =
      this.options.exitDelay !== undefined ? this.options.exitDelay : animDurationMs;

    if (immediate || exitDelay <= 0 || !this.isRendered) {
      this.isRendered = false;
      this.detachListeners();
    } else {
      this.exitTimeout = setTimeout(() => {
        this.isRendered = false;
        this.detachListeners();
      }, exitDelay);
    }
  }

  updateMousePosition(e: MouseEvent) {
    this.lastMousePos = { x: e.clientX, y: e.clientY };
    if (this.currentTarget?.contains(e.target as Node | null)) this.pointerTriggered = true;

    if (this.shouldFollow && this.currentTarget && this.isRendered) {
      if (!this.rafPending) {
        this.rafPending = true;
        requestAnimationFrame(() => {
          this.rafPending = false;
          this.updatePosition();
        });
      }
    }
  }

  updatePosition = () => {
    if (!this.currentTarget || typeof window === 'undefined') return;

    const rect = this.currentTarget.getBoundingClientRect();

    // If target is unmounted, collapsed to 0, or completely outside viewport
    if (
      (rect.width === 0 && rect.height === 0) ||
      rect.bottom < 0 ||
      rect.top > window.innerHeight ||
      rect.right < 0 ||
      rect.left > window.innerWidth
    ) {
      this.hide(true);
      return;
    }

    const tooltipEl = this.tooltipEl;
    const tooltipWidth = tooltipEl ? tooltipEl.offsetWidth : 120;
    const tooltipHeight = tooltipEl ? tooltipEl.offsetHeight : 28;
    const gap = this.options.arrow ? 8 : 6;

    // Check requested target position (normalized: 'up' -> 'top', 'down' -> 'bottom')
    let rawPos = (this.options.position as string) || this.defaultPosition || 'top';
    let targetPos: TooltipPosition =
      rawPos === 'up' ? 'top' : rawPos === 'down' ? 'bottom' : (rawPos as TooltipPosition);

    const { x: cursorX, y: cursorY } = this.lastMousePos;

    let top = 0;
    let left = 0;

    if (this.shouldFollow) {
      // -------------------------------------------------------------
      // FOLLOW CURSOR MODE: Directly tracks cursor position
      // -------------------------------------------------------------
      if (targetPos === 'top') {
        top = cursorY - gap - 6;
        left = cursorX;

        // Auto-flip if approaching top boundary of viewport
        if (top - tooltipHeight < 8) {
          targetPos = 'bottom';
          top = cursorY + gap + 18;
        }
      } else if (targetPos === 'bottom') {
        top = cursorY + gap + 18;
        left = cursorX;

        // Auto-flip if approaching bottom boundary of viewport
        if (top + tooltipHeight > window.innerHeight - 8) {
          targetPos = 'top';
          top = cursorY - gap - 6;
        }
      } else if (targetPos === 'left') {
        top = cursorY;
        left = cursorX - gap - 12;

        // Auto-flip if approaching left boundary of viewport
        if (left - tooltipWidth < 8) {
          targetPos = 'right';
          left = cursorX + gap + 12;
        }
      } else if (targetPos === 'right') {
        top = cursorY;
        left = cursorX + gap + 12;

        // Auto-flip if approaching right boundary of viewport
        if (left + tooltipWidth > window.innerWidth - 8) {
          targetPos = 'left';
          left = cursorX - gap - 12;
        }
      }
    } else {
      // -------------------------------------------------------------
      // ANCHORED / CENTERED ELEMENT POSITIONING (Non-following)
      // Always anchor to the element, independent of the pointer's entry point.
      // -------------------------------------------------------------
      if (targetPos === 'top') {
        top = rect.top - gap;
        left = rect.left + rect.width / 2;

        if (top - tooltipHeight < 8) {
          targetPos = 'bottom';
          top = rect.bottom + gap;
        }
      } else if (targetPos === 'bottom') {
        top = rect.bottom + gap;
        left = rect.left + rect.width / 2;

        if (top + tooltipHeight > window.innerHeight - 8) {
          targetPos = 'top';
          top = rect.top - gap;
        }
      } else if (targetPos === 'left') {
        left = rect.left - gap;
        top = rect.top + rect.height / 2;

        if (left - tooltipWidth < 8) {
          targetPos = 'right';
          left = rect.right + gap;
        }
      } else if (targetPos === 'right') {
        left = rect.right + gap;
        top = rect.top + rect.height / 2;

        if (left + tooltipWidth > window.innerWidth - 8) {
          targetPos = 'left';
          left = rect.left - gap;
        }
      }
    }

    // -------------------------------------------------------------
    // VIEWPORT BOUNDARY CONTAINMENT / EDGE CLAMPING
    // -------------------------------------------------------------
    if (targetPos === 'top') {
      if (top - tooltipHeight < 8) top = tooltipHeight + 8;
      else if (top > window.innerHeight - 8) top = window.innerHeight - 8;
    } else if (targetPos === 'bottom') {
      if (top < 8) top = 8;
      else if (top + tooltipHeight > window.innerHeight - 8) top = window.innerHeight - tooltipHeight - 8;
    }

    if (targetPos === 'top' || targetPos === 'bottom') {
      const halfWidth = tooltipWidth / 2;
      const minLeft = halfWidth + 8;
      const maxLeft = window.innerWidth - halfWidth - 8;

      if (left < minLeft) left = minLeft;
      else if (left > maxLeft) left = maxLeft;
    }

    if (targetPos === 'left') {
      if (left - tooltipWidth < 8) left = tooltipWidth + 8;
      else if (left > window.innerWidth - 8) left = window.innerWidth - 8;
    } else if (targetPos === 'right') {
      if (left < 8) left = 8;
      else if (left + tooltipWidth > window.innerWidth - 8) left = window.innerWidth - tooltipWidth - 8;
    }

    if (targetPos === 'left' || targetPos === 'right') {
      const halfHeight = tooltipHeight / 2;
      const minTop = halfHeight + 8;
      const maxTop = window.innerHeight - halfHeight - 8;

      if (top < minTop) top = minTop;
      else if (top > maxTop) top = maxTop;
    }

    this.coords = {
      top,
      left,
      actualPosition: targetPos
    };
  };

  private handleKeyDown = (e: KeyboardEvent) => {
    if (e.key === 'Escape') {
      this.hide(true);
    }
  };

  private attachListeners() {
    if (this.listenersAttached || typeof window === 'undefined') return;
    window.addEventListener('scroll', this.updatePosition, true);
    window.addEventListener('resize', this.updatePosition);
    window.addEventListener('keydown', this.handleKeyDown);
    this.listenersAttached = true;
  }

  private detachListeners() {
    if (!this.listenersAttached || typeof window === 'undefined') return;
    window.removeEventListener('scroll', this.updatePosition, true);
    window.removeEventListener('resize', this.updatePosition);
    window.removeEventListener('keydown', this.handleKeyDown);
    this.listenersAttached = false;
  }

  /**
   * Initializes global tooltip interception.
   * Intercepts ANY element across the app with `title`, `data-amp-tooltip`, or `data-tooltip`,
   * completely suppressing native Windows tooltips and replacing them with our custom animated floating tooltip.
   */
  initGlobal() {
    if (this.globalInitialized || typeof window === 'undefined') return;
    this.globalInitialized = true;

    let activeGlobalTarget: HTMLElement | null = null;
    let clickSuppressedTarget: HTMLElement | null = null;

    // Helper to immediately strip title from element and store in data-amp-tooltip
    const processTitleElement = (el: HTMLElement) => {
      if (!el || !el.hasAttribute || !el.hasAttribute('title')) return;
      const title = el.getAttribute('title');
      if (title !== null && title.trim()) {
        el.setAttribute('data-amp-tooltip', title);
        if (!el.hasAttribute('aria-label')) {
          el.setAttribute('aria-label', title);
        }
        // Remove native title attribute immediately so Windows OS tooltip CANNOT trigger!
        el.removeAttribute('title');

        // If this element is currently hovered, reactively update the active tooltip text!
        if (this.currentTarget === el || activeGlobalTarget === el) {
          this.updateCurrentText(title);
        }
      }
    };

    // 1. Proactive sweep of existing DOM elements with `title`
    if (typeof document !== 'undefined') {
      document.querySelectorAll('[title]').forEach((el) => {
        processTitleElement(el as HTMLElement);
      });

      // 2. Continuous MutationObserver: catches newly mounted nodes (childList) and updated attributes
      if (document.body && !this.globalObserver) {
        this.globalObserver = new MutationObserver((mutations) => {
          for (const m of mutations) {
            if (m.type === 'attributes') {
              if (m.attributeName === 'title') {
                const el = m.target as HTMLElement;
                processTitleElement(el);
              } else if (m.attributeName === 'data-tooltip-disabled') {
                const el = m.target as HTMLElement;
                const isDisabled =
                  el.getAttribute('data-tooltip-disabled') === 'true' ||
                  el.getAttribute('data-tooltip-disabled') === '';
                if (isDisabled) {
                  if (
                    this.currentTarget === el ||
                    activeGlobalTarget === el ||
                    (this.currentTarget && el.contains(this.currentTarget)) ||
                    (activeGlobalTarget && el.contains(activeGlobalTarget))
                  ) {
                    this.hide();
                    activeGlobalTarget = null;
                  }
                }
              } else if (m.attributeName === 'data-tooltip-follow' ||
                         m.attributeName === 'data-tooltip-position' || m.attributeName === 'data-position') {
                const el = m.target as HTMLElement;
                if (this.currentTarget === el && activeGlobalTarget === el) {
                  const followAttr = el.getAttribute('data-tooltip-follow');
                  this.options = {
                    ...this.options,
                    followCursor: followAttr === 'true' ? true : followAttr === 'false' ? false : undefined,
                    position: (el.getAttribute('data-tooltip-position') || el.getAttribute('data-position') || undefined) as TooltipOptions['position'],
                  };
                  this.updatePosition();
                }
              }
            } else if (m.type === 'childList') {
              m.addedNodes.forEach((node) => {
                if (node.nodeType === 1) {
                  const el = node as HTMLElement;
                  if (el.hasAttribute && el.hasAttribute('title')) {
                    processTitleElement(el);
                  }
                  el.querySelectorAll?.('[title]').forEach((child) => {
                    processTitleElement(child as HTMLElement);
                  });
                }
              });
            }
          }
        });

        this.globalObserver.observe(document.body, {
          attributes: true,
          childList: true,
          subtree: true,
          attributeFilter: ['title', 'data-tooltip-disabled', 'data-tooltip-follow', 'data-tooltip-position', 'data-position']
        });
      }
    }

    // 3. Track mouse movement
    window.addEventListener(
      'mousemove',
      (e: MouseEvent) => {
        this.updateMousePosition(e);
      },
      { capture: true, passive: true }
    );

    // 4. Global click listener: when user clicks a button, handle bottom controls click suppression & hide, or check for updated tooltip text
    window.addEventListener(
      'click',
      (e: MouseEvent) => {
        const raw = e.target as HTMLElement | null;
        const bottomPanel = raw?.closest?.('.player-control-panel');
        if (bottomPanel) {
          this.hide();
          activeGlobalTarget = null;
          const clickedControl = raw?.closest?.(
            'button, [data-amp-tooltip], [data-tooltip], [title], .pill-btn, .ctrl-btn, .play-main-btn'
          ) as HTMLElement | null;

          if (clickedControl) {
            clickSuppressedTarget = clickedControl;
            const clearClickSuppression = () => {
              if (clickSuppressedTarget === clickedControl) {
                clickSuppressedTarget = null;
              }
              clickedControl.removeEventListener('mouseleave', clearClickSuppression);
              clickedControl.removeEventListener('pointerleave', clearClickSuppression);
              clickedControl.removeEventListener('blur', clearClickSuppression);
            };
            clickedControl.addEventListener('mouseleave', clearClickSuppression, { once: true });
            clickedControl.addEventListener('pointerleave', clearClickSuppression, { once: true });
            clickedControl.addEventListener('blur', clearClickSuppression, { once: true });
          }
          return;
        }

        requestAnimationFrame(() => {
          const target = this.currentTarget || activeGlobalTarget;
          if (!target) return;
          if (target.hasAttribute('title')) {
            processTitleElement(target);
          } else if (target.hasAttribute('data-amp-tooltip')) {
            const text = target.getAttribute('data-amp-tooltip') || '';
            if (text && text !== this.options.text) {
              this.updateCurrentText(text);
            }
          }
        });
      },
      true
    );

    // 5. Global mouseover: entry point for tooltips
    window.addEventListener(
      'mouseover',
      (e: MouseEvent) => {
        const raw = e.target as HTMLElement | null;
        if (!raw) return;

        // If this element was just clicked in the bottom controls, prevent tooltip from showing while pointer stays on it
        if (
          clickSuppressedTarget &&
          (raw === clickSuppressedTarget ||
            clickSuppressedTarget.contains(raw) ||
            raw.closest?.('button, [data-amp-tooltip], [data-tooltip], [title], .pill-btn, .ctrl-btn, .play-main-btn') === clickSuppressedTarget)
        ) {
          return;
        }

        // Check if target or any ancestor explicitly disables tooltips
        const disabledParent = raw.closest?.('[data-tooltip-disabled="true"], [data-tooltip-disabled=""]');
        if (disabledParent) {
          if (activeGlobalTarget) {
            this.hide();
            activeGlobalTarget = null;
          }
          return;
        }

        const target = raw.closest?.('[title], [data-amp-tooltip], [data-tooltip]') as HTMLElement | null;
        if (!target) {
          if (activeGlobalTarget) {
            this.hide();
            activeGlobalTarget = null;
          }
          return;
        }

        // Check if target itself or any ancestor is disabled
        if (
          target.getAttribute('data-tooltip-disabled') === 'true' ||
          target.getAttribute('data-tooltip-disabled') === '' ||
          Boolean(target.closest?.('[data-tooltip-disabled="true"], [data-tooltip-disabled=""]'))
        ) {
          if (activeGlobalTarget) {
            this.hide();
            activeGlobalTarget = null;
          }
          return;
        }

        this.lastMousePos = { x: e.clientX, y: e.clientY };

        // Strip native title if present
        processTitleElement(target);

        if (target === activeGlobalTarget) return;
        activeGlobalTarget = target;

        let text = target.getAttribute('data-amp-tooltip') || target.getAttribute('data-tooltip') || '';

        if (!text || !text.trim()) {
          this.hide();
          return;
        }

        const position = (target.getAttribute('data-tooltip-position') || target.getAttribute('data-position') || undefined) as TooltipPosition | undefined;
        const arrow = target.hasAttribute('data-tooltip-arrow');
        const variant = (target.getAttribute('data-tooltip-variant') as any) || 'dark';
        const followAttr = target.getAttribute('data-tooltip-follow');
        const followCursor = followAttr === 'true' ? true : followAttr === 'false' ? false : undefined;

        this.show(
          target,
          {
            text,
            position,
            arrow,
            variant,
            followCursor
          },
          e
        );
      },
      true // capture phase: runs before any element handlers and before Chromium's native tooltip timer!
    );

    // 6. Global mouseout
    window.addEventListener(
      'mouseout',
      (e: MouseEvent) => {
        if (!activeGlobalTarget) return;
        const related = e.relatedTarget as HTMLElement | null;
        if (related && activeGlobalTarget.contains(related)) return;

        this.hideAfterUpdate();
        activeGlobalTarget = null;
      },
      true
    );

    // 7. Focus navigation
    window.addEventListener(
      'focusin',
      (e: FocusEvent) => {
        const target = (e.target as HTMLElement | null)?.closest?.('[data-amp-tooltip], [data-tooltip], [title]') as HTMLElement | null;
        if (!target) return;

        if (
          clickSuppressedTarget &&
          (target === clickSuppressedTarget || clickSuppressedTarget.contains(target))
        ) {
          return;
        }

        if (
          target.getAttribute('data-tooltip-disabled') === 'true' ||
          target.getAttribute('data-tooltip-disabled') === '' ||
          Boolean(target.closest?.('[data-tooltip-disabled="true"], [data-tooltip-disabled=""]'))
        ) {
          return;
        }

        processTitleElement(target);
        let text = target.getAttribute('data-amp-tooltip') || target.getAttribute('data-tooltip') || '';

        if (text && text.trim()) {
          const position = (target.getAttribute('data-tooltip-position') || target.getAttribute('data-position') || undefined) as TooltipPosition | undefined;
          const arrow = target.hasAttribute('data-tooltip-arrow');
          const variant = (target.getAttribute('data-tooltip-variant') as any) || 'dark';
          const followAttr = target.getAttribute('data-tooltip-follow');
          const followCursor = followAttr === 'true' ? true : followAttr === 'false' ? false : undefined;
          this.show(target, { text, position, arrow, variant, followCursor });
        }
      },
      true
    );

    window.addEventListener(
      'focusout',
      () => {
        this.hideAfterUpdate();
      },
      true
    );
  }
}

export const tooltipManager = new TooltipManager();
