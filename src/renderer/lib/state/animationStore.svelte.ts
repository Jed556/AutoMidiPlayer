import { loadUiPreferences, saveUiPreferences } from '../backend/preferences.js';
import type { SearchExpansionDirection } from '$shared/types/settings';
/**
 * Reactive animation and motion preferences.
 * Controls animation timings across all dialogs, modals, and popups.
 */

export const ANIMATION_PRESETS = [
  { value: 'off', seconds: 0, labelKey: 'settings.animOff' },
  { value: '0.10', seconds: 0.10, labelKey: 'settings.animFast' },
  { value: '0.15', seconds: 0.15, labelKey: '0.15s' },
  { value: '0.20', seconds: 0.20, labelKey: 'settings.animDefault' },
  { value: '0.25', seconds: 0.25, labelKey: '0.25s' },
  { value: '0.30', seconds: 0.30, labelKey: 'settings.animSmooth' },
  { value: '0.40', seconds: 0.40, labelKey: '0.40s' },
  { value: '0.50', seconds: 0.50, labelKey: 'settings.animSlow' },
  { value: 'custom', seconds: 0.20, labelKey: 'settings.animCustom' }
] as const;

class AnimationStore {
  // Current duration in seconds (0 = off)
  timing = $state<number>(0.20);
  selectedPreset = $state<string>('0.20');
  customSeconds = $state<number>(0.20);
  // Layout configuration, controlled in code rather than saved preferences.
  readonly searchExpansionDirection: SearchExpansionDirection = 'left';

  isOff = $derived(this.timing <= 0);
  durationMs = $derived(Math.round(this.timing * 1000));

  async init() {
    try {
      const preferences = await loadUiPreferences();
      this.setTiming(preferences.animationTiming ?? 0.2, undefined, false);
    } catch (error) {
      console.warn('[AnimationStore] Could not load preferences:', error);
      this.applyCssVariables();
    }
  }

  setTiming(seconds: number, presetKey?: string, persist = true) {
    const clamped = Math.max(0, Math.min(2.0, Math.round(seconds * 100) / 100));
    this.timing = clamped;
    this.customSeconds = clamped > 0 ? clamped : 0.20;

    if (presetKey) {
      this.selectedPreset = presetKey;
    } else if (clamped === 0) {
      this.selectedPreset = 'off';
    } else {
      const formatted = clamped.toFixed(2);
      const isKnown = ANIMATION_PRESETS.some(p => p.value === formatted);
      this.selectedPreset = isKnown ? formatted : 'custom';
    }

    if (persist) saveUiPreferences({ animationTiming: clamped });

    this.applyCssVariables();
  }

  setPreset(preset: string) {
    if (preset === 'off') {
      this.setTiming(0, 'off');
    } else if (preset === 'custom') {
      this.selectedPreset = 'custom';
      this.setTiming(this.customSeconds, 'custom');
    } else {
      const num = parseFloat(preset);
      if (!isNaN(num)) {
        this.setTiming(num, preset);
      }
    }
  }

  private applyCssVariables() {
    if (typeof document !== 'undefined') {
      const sec = this.timing;
      const ms = this.durationMs;
      const isOff = this.isOff;

      document.documentElement.style.setProperty('--anim-duration', `${sec}s`);
      document.documentElement.style.setProperty('--anim-duration-ms', `${ms}ms`);

      // Derived transition timings for elements across the app
      const fastSec = isOff ? 0 : Math.round(sec * 0.75 * 100) / 100;
      const normalSec = isOff ? 0 : sec;
      const slowSec = isOff ? 0 : Math.round(sec * 1.25 * 100) / 100;

      document.documentElement.style.setProperty('--anim-fast', `${fastSec}s`);
      document.documentElement.style.setProperty('--anim-normal', `${normalSec}s`);
      document.documentElement.style.setProperty('--anim-slow', `${slowSec}s`);

      document.documentElement.style.setProperty(
        '--transition-fast',
        `${fastSec}s cubic-bezier(0, 0, 0.2, 1)`
      );
      document.documentElement.style.setProperty(
        '--transition-normal',
        `${normalSec}s cubic-bezier(0, 0, 0.2, 1)`
      );
      document.documentElement.style.setProperty(
        '--transition-slow',
        `${slowSec}s cubic-bezier(0.1, 0.9, 0.2, 1)`
      );

      // Class flag on documentElement
      if (isOff) {
        document.documentElement.classList.add('no-animations');
      } else {
        document.documentElement.classList.remove('no-animations');
      }
    }
  }
}

export const animationStore = new AnimationStore();
