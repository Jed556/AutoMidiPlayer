import type { ThemeMode } from '../../../shared/types/settings.js';

export interface AccentColorOption {
  name: string;
  hex: string;
  value: string;
}

export const ACCENT_COLORS: AccentColorOption[] = [
  { name: 'Green', hex: '#1DB954', value: '#1DB954' },
  { name: 'Blue', hex: '#0078D4', value: '#0078D4' },
  { name: 'Purple', hex: '#8B5CF6', value: '#8B5CF6' },
  { name: 'Red', hex: '#EF4444', value: '#EF4444' },
  { name: 'Orange', hex: '#F97316', value: '#F97316' },
  { name: 'Pink', hex: '#EC4899', value: '#EC4899' },
  { name: 'Teal', hex: '#14B8A6', value: '#14B8A6' },
  { name: 'Yellow', hex: '#EAB308', value: '#EAB308' },
  { name: 'Indigo', hex: '#6366F1', value: '#6366F1' },
  { name: 'Cyan', hex: '#06B6D4', value: '#06B6D4' }
];

class ThemeStore {
  mode = $state<ThemeMode>('Dark');
  accentColor = $state<string>('#1DB954');
  isDark = $state<boolean>(true);
  themeProgress = $state<number>(0); // 0 = dark, 1 = light

  private animFrameId: number | null = null;
  private transitionTimeout: ReturnType<typeof setTimeout> | null = null;
  private initialized = false;

  // Getter/setter for components expecting .theme string ('dark' | 'light' | 'system')
  get theme(): string {
    return this.mode.toLowerCase();
  }

  set theme(val: string) {
    this.setTheme(val);
  }

  init(initialMode?: ThemeMode, initialAccent?: string) {
    if (initialMode) this.mode = initialMode;
    if (initialAccent) this.accentColor = initialAccent;

    this.apply(false);
    this.initialized = true;

    // Listen to OS prefers-color-scheme
    if (typeof window !== 'undefined' && window.matchMedia) {
      window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', () => {
        if (this.mode === 'System') {
          this.apply(true);
        }
      });
    }
  }

  setMode(mode: ThemeMode) {
    this.mode = mode;

    this.apply(true);
  }

  setTheme(themeName: string) {
    const normalized = (themeName || '').toLowerCase();
    if (normalized === 'light') {
      this.setMode('Light');
    } else if (normalized === 'system') {
      this.setMode('System');
    } else {
      this.setMode('Dark');
    }
  }

  toggleTheme() {
    this.setMode(this.isDark ? 'Light' : 'Dark');
  }

  setAccentColor(hex: string) {
    this.accentColor = hex;

    this.apply(false);
  }

  private animateProgress(target: number, durationMs = 200) {
    if (typeof window === 'undefined') {
      this.themeProgress = target;
      return;
    }

    if (this.animFrameId) {
      cancelAnimationFrame(this.animFrameId);
      this.animFrameId = null;
    }

    const startVal = this.themeProgress;
    const diff = target - startVal;
    if (Math.abs(diff) < 0.001) {
      this.themeProgress = target;
      return;
    }

    const startTime = performance.now();
    const step = (now: number) => {
      const elapsed = now - startTime;
      const t = Math.min(1, elapsed / durationMs);
      // Cubic ease-out curve matching CSS var(--transition-fast)
      const eased = 1 - Math.pow(1 - t, 3);
      this.themeProgress = startVal + diff * eased;

      if (t < 1) {
        this.animFrameId = requestAnimationFrame(step);
      } else {
        this.themeProgress = target;
        this.animFrameId = null;
      }
    };

    this.animFrameId = requestAnimationFrame(step);
  }

  apply(animate = true) {
    if (typeof document === 'undefined') return;

    if (this.mode === 'System') {
      const systemDark = typeof window !== 'undefined' && window.matchMedia
        ? window.matchMedia('(prefers-color-scheme: dark)').matches
        : true;
      this.isDark = systemDark;
    } else {
      this.isDark = this.mode === 'Dark';
    }

    const targetProgress = this.isDark ? 0 : 1;
    const root = document.documentElement;

    const shouldAnimate = animate && this.initialized &&
      !root.classList.contains('theme-revealing') &&
      !root.classList.contains('no-animations') &&
      !window.matchMedia('(prefers-reduced-motion: reduce)').matches;

    if (shouldAnimate) {
      root.classList.add('theme-transitioning');
      if (this.transitionTimeout) clearTimeout(this.transitionTimeout);
      this.transitionTimeout = setTimeout(() => {
        root.classList.remove('theme-transitioning');
      }, 220);
      this.animateProgress(targetProgress, 200);
    } else {
      if (this.animFrameId !== null) {
        cancelAnimationFrame(this.animFrameId);
        this.animFrameId = null;
      }
      if (this.transitionTimeout !== null) {
        clearTimeout(this.transitionTimeout);
        this.transitionTimeout = null;
      }
      root.classList.remove('theme-transitioning');
      this.themeProgress = targetProgress;
    }

    if (this.isDark) {
      root.classList.add('dark');
      root.classList.remove('light');
      root.setAttribute('data-theme', 'dark');
      root.style.colorScheme = 'dark';
    } else {
      root.classList.add('light');
      root.classList.remove('dark');
      root.setAttribute('data-theme', 'light');
      root.style.colorScheme = 'light';
    }

    root.style.setProperty('--accent', this.accentColor);
    root.style.setProperty('--accent-color', this.accentColor);
    root.style.setProperty('--accent-glow', `${this.accentColor}33`);
    root.style.setProperty('--accent-subtle', `${this.accentColor}18`);
  }
}

export const themeStore = new ThemeStore();
