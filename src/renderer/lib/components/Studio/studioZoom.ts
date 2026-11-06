/** Retargetable zoom driven by the piano roll's render loop. */
export class StudioZoom {
  private animation: { from: number; to: number; start: number; duration: number } | null = null;

  get active(): boolean {
    return this.animation !== null;
  }

  target(current: number): number {
    return this.animation?.to ?? current;
  }

  start(from: number, to: number, now: number, duration: number): void {
    // Repeated wheel events at a limit must not keep restarting the animation.
    if (this.animation?.to === to) return;
    this.animation = { from, to, start: now, duration };
  }

  cancel(): void {
    this.animation = null;
  }

  advance(now: number): { value: number; easedProgress: number } | null {
    const animation = this.animation;
    if (!animation) return null;
    // Input may arrive after a frame's timestamp was captured.
    const progress = animation.duration <= 0 ? 1
      : Math.max(0, Math.min(1, (now - animation.start) / animation.duration));
    const eased = 1 - Math.pow(1 - progress, 3);
    if (progress === 1) this.animation = null;
    return {
      value: progress === 1 ? animation.to : animation.from + (animation.to - animation.from) * eased,
      easedProgress: eased,
    };
  }
}
