import { DEFAULT_UI_PREFERENCES } from '../../../shared/defaults/settings.js';
import { loadUiPreferences, saveUiPreferences } from '../backend/preferences.js';
import { notificationCenter, type ToastPositionOffset } from '../ui/notificationCenter.svelte';
import { logger } from '../infrastructure/logger.js';
import type { UiPreferences } from '../../../shared/types/settings.js';
import { normalizePage, normalizeStudio, boundedNumber } from '../utils/sessionPreferences.js';

export interface DialogActionButton {
  text: string;
  variant?: 'primary' | 'secondary' | 'accent' | 'danger' | 'ghost';
  appearance?: string;
  action?: () => boolean | void | Promise<boolean | void>;
}

export type DialogActionOutcome = 'Confirmed' | 'Cancelled' | 'Custom' | 'None' | string;

export interface DialogActionRequest {
  title?: string;
  subtitle?: string;
  icon?: string;
  iconColor?: string;
  body?: string;
  content?: any;
  dialogMaxWidth?: number;
  hideFooter?: boolean;
  showCloseButton?: boolean;
  confirmButton?: DialogActionButton | null;
  cancelButton?: DialogActionButton | null;
  customButton?: DialogActionButton | null;
  buttons?: DialogActionButton[];
  onClose?: () => void;
  size?: 'normal' | 'large';
  _resolve?: (outcome: DialogActionOutcome) => void;
}

class UiStore {
  // Navigation & Tabs
  static readonly MAIN_TABS = ['tracks', 'studio', 'sheet', 'instrument', 'queue', 'songs', 'discover', 'settings'] as const;

  private _activeTab = $state<string>(DEFAULT_UI_PREFERENCES.lastViewedPage);
  tabHistory = $state<string[]>([DEFAULT_UI_PREFERENCES.lastViewedPage]);
  tabHistoryIndex = $state<number>(0);
  private _isNavigatingHistory = false;
  preSettingsDrawerWidth: number | null = null;
  preStudioNavWidth: number | null = null;

  // Window & Screen Responsiveness
  windowWidth = $state<number>(typeof window !== 'undefined' ? window.innerWidth : 1200);
  isSmallScreen = $derived(this.windowWidth < 1100);

  updateWindowWidth(w: number) {
    this.windowWidth = w;
    if (this.isSmallScreen && this.activeTab === 'studio' && this.libraryWidth > 85) {
      this.studioSidebarCollapsed = true;
    }
    if (this.isSmallScreen && this.libraryWidth > 280) {
      this.setLibraryWidth(260, false);
    }
    if (this.isSmallScreen && this.preStudioNavWidth && this.preStudioNavWidth > 280) {
      this.preStudioNavWidth = 260;
    }
    if (this.isSmallScreen && this.preSettingsDrawerWidth && this.preSettingsDrawerWidth > 280) {
      this.preSettingsDrawerWidth = 260;
    }
  }

  // Bottom Controls Dropdown / Picker State
  isBottomPickerActive = $state<boolean>(false);

  // Studio Sidebar Collapse State
  studioSidebarCollapsed = $state<boolean>(DEFAULT_UI_PREFERENCES.studioSidebarCollapsed);
  studio = $state(normalizeStudio());
  songsSortMode = $state<NonNullable<UiPreferences['songsSortMode']>>(DEFAULT_UI_PREFERENCES.songsSortMode);
  private lastPreferencesSnapshot = '';

  persistPreferences(selectedPlaylistId: string) {
    const preferences = {
      lastViewedPage: normalizePage(this.activeTab),
      selectedPlaylistId,
      songsSortMode: this.songsSortMode,
      studioSidebarCollapsed: this.studioSidebarCollapsed,
      studio: { ...this.studio },
    };
    const snapshot = JSON.stringify(preferences);
    if (snapshot === this.lastPreferencesSnapshot) return;
    this.lastPreferencesSnapshot = snapshot;
    saveUiPreferences(preferences);
  }

  setStudioSidebarCollapsed = (collapsed: boolean) => {
    if (this.studioSidebarCollapsed === collapsed) return;
    this.studioSidebarCollapsed = collapsed;

    if (this.activeTab === 'studio' && this.isSmallScreen) {
      if (!collapsed) {
        // Right drawer is expanding/showing: force left navigation drawer to shrink to collapsed mode (80px)
        if (this.libraryWidth > 85) {
          this.preStudioNavWidth = this.libraryWidth;
          this.setLibraryWidth(80, false);
        } else {
          this.preStudioNavWidth = null;
        }
      } else {
        // Right drawer is collapsing/hiding: restore state before studio view drawer was pulled
        if (this.preStudioNavWidth !== null && this.preStudioNavWidth > 85) {
          this.setLibraryWidth(this.preStudioNavWidth, false);
        }
      }
    }
  };

  toggleStudioSidebar = () => {
    this.setStudioSidebarCollapsed(!this.studioSidebarCollapsed);
  };

  private handleTabTransition(fromTab: string, toTab: string) {
    // 1. Settings tab transitions
    if (fromTab !== 'settings' && toTab === 'settings') {
      const currentWidth = this.libraryWidth > 85 ? this.libraryWidth : this.preStudioNavWidth;
      if (currentWidth && currentWidth > 85) {
        this.preSettingsDrawerWidth = currentWidth;
        this.preStudioNavWidth = null;
        this.setLibraryWidth(80, false);
      } else {
        this.preSettingsDrawerWidth = null;
      }
    } else if (fromTab === 'settings' && toTab !== 'settings') {
      if (this.preSettingsDrawerWidth !== null && this.preSettingsDrawerWidth > 85) {
        const restored = this.preSettingsDrawerWidth;
        this.preSettingsDrawerWidth = null;
        if (toTab === 'studio') {
          if (!this.studioSidebarCollapsed && this.isSmallScreen) {
            this.preStudioNavWidth = restored;
            this.setLibraryWidth(80, false);
          } else {
            this.setLibraryWidth(restored, false);
            this.preStudioNavWidth = null;
          }
        } else {
          this.setLibraryWidth(restored, false);
        }
      }
    }

    // 2. Studio tab transitions
    if (fromTab !== 'studio' && toTab === 'studio') {
      if (fromTab !== 'settings') {
        if (!this.studioSidebarCollapsed && this.isSmallScreen) {
          if (this.libraryWidth > 85) {
            this.preStudioNavWidth = this.libraryWidth;
            this.setLibraryWidth(80, false);
          } else {
            this.preStudioNavWidth = null;
          }
        }
      }
    } else if (fromTab === 'studio' && toTab !== 'studio') {
      if (toTab !== 'settings') {
        if (this.preStudioNavWidth !== null && this.preStudioNavWidth > 85) {
          this.setLibraryWidth(this.preStudioNavWidth, false);
          this.preStudioNavWidth = null;
        }
      }
    }
  }

  get activeTab(): string {
    return this._activeTab;
  }

  set activeTab(val: string) {
    const normalized = (val || DEFAULT_UI_PREFERENCES.lastViewedPage).toLowerCase();
    if (this._activeTab === normalized) return;
    const prevTab = this._activeTab;
    this.handleTabTransition(prevTab, normalized);
    this._activeTab = normalized;
    logger.pageVisit(normalized, `tab-change-from-${prevTab}`);

    if (!this._isNavigatingHistory) {
      const newHist = this.tabHistory.slice(0, this.tabHistoryIndex + 1);
      newHist.push(normalized);
      this.tabHistory = newHist;
      this.tabHistoryIndex = newHist.length - 1;
    }
  }

  // Dialogs & Modals
  gameSelectorOpen = $state<boolean>(false);
  gameSelectorAnchor = $state<HTMLElement | null>(null);
  activeDialog = $state<string | null>(null);
  dialogData = $state<any>(null);

  openDialog(name: string, data?: any) {
    this.activeDialog = name;
    this.dialogData = data ?? null;
  }

  showActionDialog(request: DialogActionRequest): Promise<DialogActionOutcome> {
    return new Promise((resolve) => {
      this.openDialog('action', {
        ...request,
        _resolve: resolve
      });
    });
  }

  closeDialog() {
    if (this.dialogData && typeof this.dialogData === 'object' && this.dialogData._resolve) {
      this.dialogData._resolve('Cancelled');
    }
    this.activeDialog = null;
    this.dialogData = null;
  }

  // Library Sidebar Layout (Compressed: 80px, 2-Column: 260px, 3-Column: 340px)
  libraryWidth = $state<number>(DEFAULT_UI_PREFERENCES.libraryWidth);
  lastExpandedWidth = $state<number>(DEFAULT_UI_PREFERENCES.libraryExpandedWidth);
  libraryMode = $derived<'compressed' | 'two-column' | 'three-column'>(
    this.libraryWidth <= 85
      ? 'compressed'
      : (this.isSmallScreen || this.libraryWidth < 315)
        ? 'two-column'
        : 'three-column'
  );

  setLibraryWidth(width: number, isUserAction = true) {
    const maxW = this.isSmallScreen ? 280 : 385;
    const clamped = width <= 85 ? 80 : Math.min(Math.max(width, 220), maxW);
    this.libraryWidth = clamped;
    if (isUserAction && this.activeTab === 'studio' && this.isSmallScreen && clamped > 85) {
      this.studioSidebarCollapsed = true;
    }

    if (isUserAction) {
      if (this.activeTab === 'studio') {
        if (clamped > 85) {
          this.preStudioNavWidth = clamped;
        } else {
          this.preStudioNavWidth = null;
        }
      } else if (this.activeTab === 'settings') {
        if (clamped > 85) {
          this.preSettingsDrawerWidth = clamped;
        } else {
          this.preSettingsDrawerWidth = null;
        }
      }
    }

    if (clamped > 85) this.lastExpandedWidth = clamped;
    if (isUserAction) saveUiPreferences({
      libraryWidth: clamped,
      libraryExpandedWidth: this.lastExpandedWidth,
    });
  }

  setLibraryMode(mode: 'compressed' | 'two-column' | 'three-column', isUserAction = true) {
    if (mode === 'compressed') {
      this.setLibraryWidth(80, isUserAction);
    } else if (mode === 'two-column') {
      this.setLibraryWidth(260, isUserAction);
    } else if (mode === 'three-column') {
      this.setLibraryWidth(this.isSmallScreen ? 260 : 340, isUserAction);
    }
  }

  toggleLibraryExpanded(isUserAction = true) {
    if (this.libraryWidth <= 85) {
      let target = this.lastExpandedWidth || 340;
      if (this.isSmallScreen) {
        target = Math.min(target, 280);
        if (target < 220) target = 260;
      }
      this.setLibraryWidth(target, isUserAction);
    } else {
      this.setLibraryWidth(80, isUserAction);
    }
  }

  goBack() {
    // If a modal dialog is open, dismiss it first
    if (this.activeDialog) {
      this.closeDialog();
      return;
    }
    // If the game selector flyout is open, dismiss it first
    if (this.gameSelectorOpen) {
      this.gameSelectorOpen = false;
      return;
    }

    if (this.tabHistoryIndex > 0) {
      this._isNavigatingHistory = true;
      this.tabHistoryIndex--;
      const target = this.tabHistory[this.tabHistoryIndex];
      const prev = this._activeTab;
      this.handleTabTransition(prev, target);
      this._activeTab = target;
      this._isNavigatingHistory = false;
      return;
    }

    // Fallback: move to previous page in drawer tab order
    const tabs = UiStore.MAIN_TABS;
    const curr = this._activeTab.toLowerCase();
    const idx = tabs.indexOf(curr as any);
    const prevIdx = idx > 0 ? idx - 1 : tabs.length - 1;
    this.activeTab = tabs[prevIdx];
  }

  goForward() {
    if (this.tabHistoryIndex < this.tabHistory.length - 1) {
      this._isNavigatingHistory = true;
      this.tabHistoryIndex++;
      const target = this.tabHistory[this.tabHistoryIndex];
      const prev = this._activeTab;
      this.handleTabTransition(prev, target);
      this._activeTab = target;
      this._isNavigatingHistory = false;
      return;
    }

    // Fallback: move to next page in drawer tab order
    const tabs = UiStore.MAIN_TABS;
    const curr = this._activeTab.toLowerCase();
    const idx = tabs.indexOf(curr as any);
    const nextIdx = (idx >= 0 && idx < tabs.length - 1) ? idx + 1 : 0;
    this.activeTab = tabs[nextIdx];
  }

  async init() {
    if (typeof window !== 'undefined') this.windowWidth = window.innerWidth;
    try {
      const preferences = await loadUiPreferences();
      this.studio = normalizeStudio(preferences.studio ?? {});
      this.studioSidebarCollapsed = typeof preferences.studioSidebarCollapsed === 'boolean' ? preferences.studioSidebarCollapsed : DEFAULT_UI_PREFERENCES.studioSidebarCollapsed;
      this.songsSortMode = ['title', 'artist', 'dateAdded', 'duration'].includes(preferences.songsSortMode ?? '')
        ? preferences.songsSortMode! : DEFAULT_UI_PREFERENCES.songsSortMode;
      this.setLibraryWidth(boundedNumber(preferences.libraryWidth, DEFAULT_UI_PREFERENCES.libraryWidth, 80, 385), false);
      this.lastExpandedWidth = boundedNumber(preferences.libraryExpandedWidth, DEFAULT_UI_PREFERENCES.libraryExpandedWidth, 220, 385);
      const tab = normalizePage(preferences.lastViewedPage);
      this.handleTabTransition('songs', tab);
      this._activeTab = tab;
      this.tabHistory = [tab];
      this.tabHistoryIndex = 0;
    } catch (error) {
      console.warn('[UiStore] Could not load preferences:', error);
    }
  }

  setToastOffset(offset: ToastPositionOffset, id?: string) {
    return notificationCenter.setToastOffset(offset, id);
  }

  resetToastOffset(id?: string) {
    notificationCenter.resetToastOffset(id);
  }
}

export const uiStore = new UiStore();

if (typeof window !== 'undefined') {
  (window as any).uiStore = uiStore;
}
