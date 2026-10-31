import { DEFAULT_UI_PREFERENCES } from '../../../shared/defaults/settings.js';
import { loadUiPreferences, saveUiPreferences } from '../backend/preferences.js';
import { enUS, type TranslationSchema } from './locales/en-US';
import { zhCN } from './locales/zh-CN';

export type LocaleCode = 'en-US' | 'zh-CN';

export interface LocaleInfo {
  code: LocaleCode;
  name: string;
}

export const AVAILABLE_LOCALES: LocaleInfo[] = [
  { code: 'en-US', name: 'English (US)' },
  { code: 'zh-CN', name: '简体中文 (Simplified Chinese)' },
];

const LOCALES: Record<string, TranslationSchema> = {
  'en-US': enUS,
  'zh-CN': zhCN,
};

class I18nService {
  currentLocale = $state<LocaleCode>(DEFAULT_UI_PREFERENCES.locale);

  constructor() {
    this.init();
  }

  private async init() {
    this.currentLocale = typeof navigator !== 'undefined' && navigator.language.startsWith('zh') ? 'zh-CN' : DEFAULT_UI_PREFERENCES.locale;
    try {
      const preferences = await loadUiPreferences();
      if (preferences.locale && LOCALES[preferences.locale]) this.currentLocale = preferences.locale;
    } catch (error) {
      console.warn('[I18n] Could not load preferences:', error);
    }
  }

  setLocale(locale: LocaleCode, persist = true) {
    if (LOCALES[locale]) {
      this.currentLocale = locale;
      if (persist) saveUiPreferences({ locale });
    }
  }

  translate(keyPath: string, params?: Record<string, string | number>): string {
    const keys = keyPath.split('.');

    // 1. Try current locale
    let val: any = LOCALES[this.currentLocale];
    for (const k of keys) {
      if (val && typeof val === 'object' && k in val) {
        val = val[k];
      } else {
        val = undefined;
        break;
      }
    }

    // 2. Fallback to English if not found
    if (typeof val !== 'string') {
      let fallbackVal: any = enUS;
      for (const k of keys) {
        if (fallbackVal && typeof fallbackVal === 'object' && k in fallbackVal) {
          fallbackVal = fallbackVal[k];
        } else {
          fallbackVal = undefined;
          break;
        }
      }
      if (typeof fallbackVal === 'string') {
        val = fallbackVal;
      }
    }

    // 3. Fallback to key itself
    if (typeof val !== 'string') {
      return keyPath;
    }

    // 4. Interpolate parameters {param}
    if (params) {
      return val.replace(/\{(\w+)\}/g, (match, p1) => {
        if (p1 in params) {
          return String(params[p1]);
        }
        return match;
      });
    }

    return val;
  }
}

export const i18n = new I18nService();

/**
 * Reactive translation helper function for Svelte components.
 * Usage:
 *   t('nav.tracks')
 *   t('songs.songCount', { count: 5 })
 */
export function t(key: string, params?: Record<string, string | number>): string {
  // Accessing i18n.currentLocale makes calls to t() reactive in Svelte 5 templates
  const _ = i18n.currentLocale;
  return i18n.translate(key, params);
}
