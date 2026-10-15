import type { StorageUsage } from '$shared/types/ipc';
import type { AppSettings, HotkeyBinding, MouseStopClickOption, ThemeMode } from '$shared/types/settings';
import type { TransposeMode } from '$shared/types/midi';
import { DEFAULT_APP_SETTINGS } from '$shared/defaults/settings';
import { getAutoCorrectTicks, getDefaultAutoCorrectThreshold, resolveAutoCorrectThreshold } from '$shared/defaults/autoCorrect';
import { api } from '../backend/index.js';
import { notificationCenter } from '../ui/notificationCenter.svelte';
import { themeStore } from './themeStore.svelte.js';
import { updateStore } from './updateStore.svelte.js';
import { logger } from '../infrastructure/logger.js';
import { telemetry } from '../infrastructure/telemetry.js';
import { t } from '../i18n';

class SettingsStore {
  settings = $state<AppSettings>({ ...DEFAULT_APP_SETTINGS });
  isScanningFolder = $state(false);
  isResettingHotkeys = $state(false);
  isResettingSongDefaults = $state(false);
  isResettingNotesDefaults = $state(false);
  isResettingAll = $state(false);
  isGameLocationsExpanded = $state(false);

  // Derived accessors delegated to the update store.
  get updateAvailable() {
    return updateStore.updateAvailable;
  }
  set updateAvailable(v: boolean) {
    updateStore.updateAvailable = v;
  }

  get latestVersion() {
    return updateStore.latestVersion;
  }
  set latestVersion(v: string) {
    updateStore.latestVersion = v;
  }

  get programVersion() {
    return updateStore.programVersion;
  }
  set programVersion(v: string) {
    updateStore.programVersion = v;
  }

  get isCheckingUpdate() {
    return updateStore.isCheckingUpdate;
  }
  set isCheckingUpdate(v: boolean) {
    updateStore.isCheckingUpdate = v;
  }

  // Storage info state
  storageUsage = $state<StorageUsage | null>(null);
  storageLoading = $state(false);
  storageError = $state('');
  isClearingCache = $state(false);
  private storageRequest = 0;
  dataLocationPath = $state('');

  autoCorrectThresholdTicks = $state<number[]>([0]);

  async refreshAutoCorrectLayouts(value?: number) {
    const layouts = await api.games.getKeyboardLayouts();
    this.autoCorrectThresholdTicks = getAutoCorrectTicks(layouts);
    const previous = this.settings.autoCorrectThreshold;
    this.settings.autoCorrectThreshold = resolveAutoCorrectThreshold(value, this.autoCorrectThresholdTicks);
    if (this.settings.autoCorrectThreshold !== previous) this.save();
  }

  onAutoEnableListenModeChanged?: (enabled: boolean) => void;
  private _isHotkeysSuspended = false;
  private folderSync: Promise<void> = Promise.resolve();
  private pendingOverlapBehavior: AppSettings['overlapBehavior'] | null = null;
  private savingOverlapBehavior = false;


  async init() {
    try {
      this.dataLocationPath = await api.settings.getDataDirectory();
      const remote = await api.settings.getSettings();
      if (remote) {
        this.settings = { ...DEFAULT_APP_SETTINGS, ...remote };
      }
      await this.refreshAutoCorrectLayouts(remote?.autoCorrectThreshold);

      if (this.settings.accentColor) {
        themeStore.setAccentColor(this.settings.accentColor);
      }
      if (this.settings.theme) {
        themeStore.setTheme(this.settings.theme);
      }
      this.syncHotkeys();
      await this.syncSongDefaults();
      updateStore.configureTimer(this.settings, false);
      telemetry.setEnabled(this.settings.telemetryOptIn);
      logger.setVerbosity(this.settings.crashLogVerbosity);
      logger.setDebugMode(this.settings.debugModeEnabled);
      await this.syncMidiFolder();
    } catch (err) {
      console.warn('[SettingsStore] Failed to load persisted settings:', err);
    }
  }

  syncSongDefaults() {
    return api.settings.updateSongDefaults({
      defaultBaseKeyOffset: this.settings.defaultBaseKeyOffset ?? 0,
      defaultKeyOffset: this.settings.defaultKeyOffset ?? 0,
      autoDetectBaseKey: this.settings.autoDetectBaseKey ?? true,
      defaultTranspose: this.settings.defaultTranspose ?? 'Smart',
      defaultSpeed: this.settings.defaultSpeed ?? 1.0,
      defaultHoldNotes: this.settings.defaultHoldNotes ?? false,
      defaultTapThresholdMs: this.settings.defaultTapThresholdMs ?? 0,
      defaultMergeNotes: this.settings.defaultMergeNotes ?? true,
      defaultMergeToleranceMs: this.settings.defaultMergeToleranceMs ?? 25,
    }).catch(console.error);
  }

  syncHotkeys() {
    if (this._isHotkeysSuspended) {
      api.settings.updateGlobalHotkeys(false, this.settings.hotkeys).catch(console.error);
      return;
    }
    api.settings.updateGlobalHotkeys(this.settings.hotkeysEnabled, this.settings.hotkeys).catch(console.error);
  }

  suspendHotkeys() {
    this._isHotkeysSuspended = true;
    api.settings.updateGlobalHotkeys(false, this.settings.hotkeys).catch(console.error);
  }

  resumeHotkeys() {
    this._isHotkeysSuspended = false;
    if (this.settings.hotkeysEnabled) {
      api.settings.updateGlobalHotkeys(true, this.settings.hotkeys).catch(console.error);
    }
  }

  private save() {
    try {
      // UI preferences and active-game selection are owned by their respective stores.
      const { ui, activeGameId, selectedInstrumentIds, selectedLayoutIds, ...settings } = this.settings;
      api.settings.updateSettings(settings).catch((error) => console.error('[SettingsStore] Save failed:', error));
      this.syncSongDefaults();
    } catch (e) {
      console.warn('[SettingsService] Save failed:', e);
    }
  }

  private async saveOverlapBehavior() {
    this.pendingOverlapBehavior = this.settings.overlapBehavior;
    if (this.savingOverlapBehavior) return;
    this.savingOverlapBehavior = true;
    try {
      // Keep one native write in flight and replace queued switches with the
      // latest choice. A card click must not resync song defaults or locations.
      while (this.pendingOverlapBehavior !== null) {
        const overlapBehavior = this.pendingOverlapBehavior;
        this.pendingOverlapBehavior = null;
        try {
          await api.settings.updateSettings({ overlapBehavior });
        } catch (error) {
          console.error('[SettingsStore] Save overlap behavior failed:', error);
        }
      }
    } finally {
      this.savingOverlapBehavior = false;
    }
  }

  update<K extends keyof AppSettings>(key: K, value: AppSettings[K]) {
    if (key === 'overlapBehavior' && this.settings[key] === value) return;
    this.settings[key] = value;
    if (key === 'overlapBehavior') {
      void this.saveOverlapBehavior();
      return;
    }
    if (key === 'accentColor' && typeof value === 'string') {
      themeStore.setAccentColor(value);
    } else if (key === 'theme' && typeof value === 'string') {
      themeStore.setTheme(value);
    } else if (key === 'autoEnableListenMode' && typeof value === 'boolean') {
      this.onAutoEnableListenModeChanged?.(value);
    } else if (key === 'hotkeysEnabled') {
      this.syncHotkeys();
    } else if (key === 'autoCheckUpdates' || key === 'updateCheckFrequency' || key === 'autoDownloadUpdates' || key === 'includeBetaUpdates') {
      updateStore.configureTimer(this.settings);
    } else if (key === 'telemetryOptIn') {
      telemetry.setEnabled(value as boolean);
    } else if (key === 'crashLogVerbosity') {
      logger.setVerbosity(value as number);
    } else if (key === 'debugModeEnabled') {
      logger.setDebugMode(value as boolean);
    }
    this.save();
    if (key === 'midiFolder' || key === 'autoScanMidiFolder') void this.syncMidiFolder();
    if (key === 'cacheAutoClean' || key === 'cacheMaxSizeMb') void this.refreshStorage();
  }

  updateMultiple(partial: Partial<AppSettings>) {
    this.settings = { ...this.settings, ...partial };
    if (partial.accentColor) {
      themeStore.setAccentColor(partial.accentColor);
    }
    if (partial.theme) {
      themeStore.setTheme(partial.theme);
    }
    this.save();
    if ('midiFolder' in partial || 'autoScanMidiFolder' in partial) void this.syncMidiFolder();
  }

  // --- Hotkeys ---

  updateHotkey(action: string, updated: HotkeyBinding) {
    const list = [...this.settings.hotkeys];
    const idx = list.findIndex(h => h.action === action);
    if (idx >= 0) {
      list[idx] = updated;
    } else {
      list.push(updated);
    }
    this.settings.hotkeys = list;
    this.save();
    this.syncHotkeys();
  }

  clearHotkey(action: string) {
    const list = [...this.settings.hotkeys];
    const idx = list.findIndex(h => h.action === action);
    if (idx >= 0) {
      list[idx] = { ...list[idx], key: '', ctrl: false, alt: false, shift: false };
      this.settings.hotkeys = list;
      this.save();
      this.syncHotkeys();
    }
  }

  resetHotkeys() {
    this.isResettingHotkeys = true;
    this.settings.hotkeys = [...DEFAULT_APP_SETTINGS.hotkeys];
    this.save();
    this.syncHotkeys();
    notificationCenter.info(t('settings.toastTitleHotkeysReset'), t('settings.toastHotkeysResetSuccess'));
    setTimeout(() => {
      this.isResettingHotkeys = false;
    }, 600);
  }

  // --- Game Locations ---

  setGameLocation(gameId: string, path: string) {
    this.settings.gameLocations = {
      ...this.settings.gameLocations,
      [gameId]: path
    };
    api.games.setGameLocation(gameId, path).catch(() => {});
    this.save();
  }

  clearGameLocation(gameId: string) {
    const updated = { ...this.settings.gameLocations };
    delete updated[gameId];
    this.settings.gameLocations = updated;
    api.games.setGameLocation(gameId, '').catch(() => {});
    this.save();
  }

  toggleGameLocationsExpanded() {
    this.isGameLocationsExpanded = !this.isGameLocationsExpanded;
  }

  // --- MIDI Folder ---

  syncMidiFolder() {
    const folder = this.settings.autoScanMidiFolder ? this.settings.midiFolder.trim() : '';
    // Serialize folder changes so a slow scan cannot leave an older watcher active.
    this.folderSync = this.folderSync.then(async () => {
      await api.songs.watchFolder(folder || null);
      if (folder) await api.songs.scanFolder(folder);
    }).catch((error) => {
      console.error('[SettingsStore] Failed to monitor MIDI folder:', error);
      notificationCenter.error(t('common.error'), String(error));
    });
    return this.folderSync;
  }

  async browseMidiFolder() {
    try {
      const folder = await api.settings.browseFolder();
      if (folder) {
        this.update('midiFolder', folder);
        notificationCenter.success(t('settings.toastTitleBrowse'), folder);
      }
    } catch {
      notificationCenter.info(t('settings.toastTitleBrowse'), t('settings.toastBrowseFolder'));
    }
  }

  async openMidiFolder() {
    await api.settings.openFolderInExplorer(this.settings.midiFolder);
    notificationCenter.info(t('settings.openInExplorer'), this.settings.midiFolder || t('settings.noFolderSelected'));
  }

  async scanMidiFolder() {
    if (this.isScanningFolder) return;
    this.isScanningFolder = true;
    try {
      const res = await api.songs.scanFolder(this.settings.midiFolder || undefined);
      const count = res?.length ?? 0;
      notificationCenter.success(t('settings.toastTitleScan'), t('settings.toastScanComplete', { count }));
    } catch (error) {
      notificationCenter.error(t('common.error'), String(error));
    } finally {
      setTimeout(() => {
        this.isScanningFolder = false;
      }, 1000);
    }
  }

  clearMidiFolder() {
    this.update('midiFolder', '');
    notificationCenter.info(t('settings.clearFolder'), t('settings.noFolderSelected'));
  }

  // --- Data Location ---

  openDataFolder() {
    api.settings.openFolderInExplorer(this.dataLocationPath);
    notificationCenter.info(t('settings.dataLocation'), this.dataLocationPath);
  }

  // --- Storage ---

  async refreshStorage() {
    const request = ++this.storageRequest;
    this.storageLoading = true;
    this.storageError = '';
    try {
      const usage = await api.settings.getStorageUsage();
      if (request === this.storageRequest) this.storageUsage = usage;
    } catch (error) {
      if (request === this.storageRequest) this.storageError = String(error);
    } finally {
      if (request === this.storageRequest) this.storageLoading = false;
    }
  }

  async clearCache() {
    if (this.isClearingCache) return;
    this.isClearingCache = true;
    ++this.storageRequest; // Ignore any scan started before this cleanup.
    try {
      const bytes = await api.settings.clearDiscoverCache();
      notificationCenter.success(t('settings.toastTitleCache'),
        t('settings.toastCacheCleared', { size: (bytes / (1024 * 1024)).toFixed(2) }));
    } catch (error) {
      notificationCenter.error(t('common.error'), String(error));
    } finally {
      await this.refreshStorage();
      this.isClearingCache = false;
    }
  }

  // --- Version & Updates ---

  async checkForUpdates() {
    return updateStore.checkForUpdates(true);
  }

  // --- Debug Mode ---

  toggleDebugMode() {
    this.update('debugModeEnabled', !this.settings.debugModeEnabled);
  }

  // --- Resets ---

  resetSongDefaults() {
    this.isResettingSongDefaults = true;
    this.settings.defaultBaseKeyOffset = 0;
    this.settings.defaultKeyOffset = 0;
    this.settings.autoDetectBaseKey = true;
    this.settings.defaultTranspose = 'Smart';
    this.settings.defaultSpeed = 1.0;
    this.save();
    notificationCenter.info(t('settings.toastTitleSongReset'), t('settings.toastSongResetSuccess'));
    setTimeout(() => {
      this.isResettingSongDefaults = false;
    }, 600);
  }

  resetNotesDefaults() {
    this.isResettingNotesDefaults = true;
    this.settings.defaultHoldNotes = false;
    this.settings.defaultTapThresholdMs = 0;
    this.settings.defaultMergeNotes = true;
    this.settings.defaultMergeToleranceMs = 25;
    this.save();
    notificationCenter.info(t('settings.toastTitleNotesReset'), t('settings.toastNotesResetSuccess'));
    setTimeout(() => {
      this.isResettingNotesDefaults = false;
    }, 600);
  }

  resetAllDefaults() {
    this.isResettingAll = true;
    this.pendingOverlapBehavior = null;
    this.settings = { ...DEFAULT_APP_SETTINGS };
    this.settings.autoCorrectThreshold = getDefaultAutoCorrectThreshold(this.autoCorrectThresholdTicks);
    api.settings.resetSettings()
      .catch((error) => console.error('[SettingsStore] Failed to reset persisted settings:', error))
      .finally(() => this.save());
    this.syncHotkeys();
    void this.syncMidiFolder();
    themeStore.setTheme('dark');
    themeStore.setAccentColor('#1DB954');
    notificationCenter.info(t('settings.toastTitleReset'), t('settings.toastResetSuccess'));
    setTimeout(() => {
      this.isResettingAll = false;
    }, 600);
  }
}

export const settingsStore = new SettingsStore();
