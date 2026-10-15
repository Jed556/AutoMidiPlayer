export interface PianoStrike {
  id: string;
  x: number;
  y: number;
  height: number;
  color: string;
}

interface Spark {
  x: number;
  y: number;
  vx: number;
  vy: number;
  life: number;
  duration: number;
  color: string;
}

/** A separate overlay keeps particle animation out of the piano roll's redraw path. */
export class PianoStrikeEffects {
  private ctx: CanvasRenderingContext2D;
  private strikes = new Map<string, PianoStrike>();
  private sparks: Spark[] = [];
  private sprites = new Map<string, HTMLCanvasElement>();
  private lastFrame = 0;
  private lastPlaybackTime = 0;
  private emissionTime = 0;
  private width = 0;
  private height = 0;
  private hasPixels = false;
  private isLight = false;
  private readonly maxSparks = 320;

  constructor(private canvas: HTMLCanvasElement) {
    this.ctx = canvas.getContext('2d')!;
  }

  resize(width: number, height: number, dpr: number) {
    const targetWidth = Math.round(width * dpr);
    const targetHeight = Math.round(height * dpr);
    const changed = this.canvas.width !== targetWidth || this.canvas.height !== targetHeight;
    if (this.canvas.width !== targetWidth) this.canvas.width = targetWidth;
    if (this.canvas.height !== targetHeight) this.canvas.height = targetHeight;
    this.ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    this.width = width;
    this.height = height;
    // Keep emitters and live particles through resizing, avoiding repeated
    // note-on bursts while a drawer or window is being dragged.
    if (changed) this.hasPixels = false;
  }

  clear() {
    this.strikes.clear();
    this.sparks.length = 0;
    if (this.hasPixels) this.ctx.clearRect(0, 0, this.width, this.height);
    this.hasPixels = false;
    this.lastFrame = 0;
    this.emissionTime = 0;
  }

  update(strikes: PianoStrike[], playbackTime: number, enabled: boolean, isLight: boolean) {
    this.isLight = isLight;
    if (!enabled) {
      this.clear();
      this.lastPlaybackTime = playbackTime;
      return;
    }
    const jumped = playbackTime < this.lastPlaybackTime || playbackTime - this.lastPlaybackTime > 1;
    if (jumped) this.clear();
    this.lastPlaybackTime = playbackTime;
    const next = new Map<string, PianoStrike>();
    for (const strike of strikes) {
      next.set(strike.id, strike);
      if (!this.strikes.has(strike.id) && !jumped) this.burst(strike, 7);
    }
    this.strikes = next;
  }

  private burst(strike: PianoStrike, count: number) {
    for (let i = 0; i < count && this.sparks.length < this.maxSparks; i++) {
      const duration = 0.25 + Math.random() * 0.45;
      this.sparks.push({
        x: strike.x + Math.random() * 4,
        y: strike.y + (Math.random() - 0.5) * strike.height,
        vx: 25 + Math.random() * 115,
        vy: (Math.random() - 0.5) * 50,
        life: duration, duration, color: strike.color,
      });
    }
  }

  private sprite(color: string) {
    let sprite = this.sprites.get(color);
    if (sprite) return sprite;
    sprite = document.createElement('canvas');
    sprite.width = sprite.height = 64;
    const ctx = sprite.getContext('2d')!;
    const bloom = ctx.createRadialGradient(32, 32, 0, 32, 32, 32);
    bloom.addColorStop(0, '#ffffff');
    bloom.addColorStop(0.12, color);
    bloom.addColorStop(0.45, color);
    bloom.addColorStop(1, color);
    ctx.fillStyle = bloom;
    ctx.fillRect(0, 0, 64, 64);
    // Fade alpha separately so the colored edge never interpolates toward black.
    const opacity = ctx.createRadialGradient(32, 32, 0, 32, 32, 32);
    opacity.addColorStop(0.45, '#ffffff');
    opacity.addColorStop(1, 'rgba(255, 255, 255, 0)');
    ctx.globalCompositeOperation = 'destination-in';
    ctx.fillStyle = opacity;
    ctx.fillRect(0, 0, 64, 64);
    ctx.globalCompositeOperation = 'source-over';
    if (this.sprites.size >= 48) this.sprites.clear();
    this.sprites.set(color, sprite);
    return sprite;
  }

  draw(now: number) {
    if (!this.strikes.size && !this.sparks.length) {
      if (this.hasPixels) this.ctx.clearRect(0, 0, this.width, this.height);
      this.hasPixels = false;
      this.lastFrame = now;
      return;
    }
    const dt = this.lastFrame ? Math.min(0.05, (now - this.lastFrame) / 1000) : 0;
    this.lastFrame = now;
    this.emissionTime += dt;
    if (this.emissionTime >= 0.06) {
      for (const strike of this.strikes.values()) this.burst(strike, 1);
      this.emissionTime = 0;
    }
    const ctx = this.ctx;
    ctx.clearRect(0, 0, this.width, this.height);
    ctx.save();
    ctx.globalCompositeOperation = this.isLight ? 'source-over' : 'lighter';
    for (const strike of this.strikes.values()) {
      ctx.globalAlpha = this.isLight ? 0.28 : 0.52;
      const glowHeight = Math.max(28, strike.height * 3);
      ctx.drawImage(this.sprite(strike.color), strike.x - 26, strike.y - glowHeight / 2, 68, glowHeight);
      ctx.globalAlpha = 0.95;
      ctx.fillStyle = '#ffffff';
      ctx.fillRect(strike.x - 1, strike.y - strike.height / 2, 2, strike.height);
    }
    let alive = 0;
    for (const spark of this.sparks) {
      spark.life -= dt;
      if (spark.life <= 0) continue;
      const oldX = spark.x;
      const oldY = spark.y;
      spark.x += spark.vx * dt;
      spark.y += spark.vy * dt;
      ctx.globalAlpha = Math.min(1, spark.life / spark.duration * 1.4);
      ctx.strokeStyle = spark.color;
      ctx.lineWidth = 1.4;
      ctx.beginPath();
      ctx.moveTo(oldX, oldY);
      ctx.lineTo(spark.x + spark.vx * 0.018, spark.y + spark.vy * 0.018);
      ctx.stroke();
      ctx.drawImage(this.sprite(spark.color), spark.x - 3, spark.y - 3, 6, 6);
      this.sparks[alive++] = spark;
    }
    this.sparks.length = alive;
    ctx.restore();
    this.hasPixels = true;
  }
}
