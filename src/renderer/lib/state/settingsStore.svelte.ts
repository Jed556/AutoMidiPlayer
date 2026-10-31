import type { StorageUsage } from '$shared/types/ipc';
import type { AppSettings, HotkeyBinding, MouseStopClickOption, ThemeMode, UiPreferences } from '$shared/types/settings';
import type { TransposeMode } from '$shared/types/midi';
import { DEFAULT_APP_SETTINGS, createDefaultSettings } from '$shared/defaults/settings';
import { getAutoCorrectTicks, getDefaultAutoCorrectThreshold, resolveAutoCorrectThreshold } from '$shared/defaults/autoCorrect';
import { api } from '../backend/index.js';
import { notificationCenter } from '../ui/notificationCenter.svelte';
import { themeStore } from './themeStore.svelte.js';
import { updateStore } from './updateStore.svelte.js';
import { logger } from '../infrastructure/logger.js';
import { telemetry } from '../infrastructure/telemetry.js';
import { t } from '../i18n';

type OverlapSettings = Pick<AppSettings, 'overlapBehavior' | 'repressGapMs'>;
const normalizeRepressGapMs = (value: unknown): number => typeof value === 'number' && Number.isFinite(value)
  ? Math.max(0, Math.min(1000, Math.round(value))) : DEFAULT_APP_SETTINGS.repressGapMs;

class SettingsStore {
  settings = $state<AppSettings>(createDefaultSettings());
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
  private pendingOverlapSettings: Partial<OverlapSettings> | null = null;
  private savingOverlapBehavior = false;


  async init() {
    try {
      this.dataLocationPath = await api.settings.getDataDirectory();
      const remote = await api.settings.getSettings();
      if (remote) {
        this.settings = { ...createDefaultSettings(), ...remote };
        this.settings.repressGapMs = normalizeRepressGapMs(remote.repressGapMs);
      }
      await this.refreshAutoCorrectLayouts(remote?.autoCorrectThreshold);

      if (this.settings.accentColor) {
        themeStore.setAccentColor(this.settings.accentColor);
      }
      if (this.settings.theme) {
        themeStore.setTheme(this.settings.theme);
      }
      if (!this.settings.onboardingCompleted) this.suspendHotkeys();
      this.syncHotkeys();
      await this.syncSongDefaults();
      if (this.settings.onboardingCompleted) updateStore.configureTimer(this.settings, false);
      await telemetry.setEnabled(this.settings.onboardingCompleted && this.settings.telemetryOptIn);
      logger.setVerbosity(this.settings.crashLogVerbosity);
      logger.setDebugMode(this.settings.debugModeEnabled);
      if (this.settings.onboardingCompleted) await this.syncMidiFolder();
    } catch (err) {
      console.warn('[SettingsStore] Failed to load persisted settings:', err);
    }
  }

  syncSongDefaults(strict = false) {
    return api.settings.updateSongDefaults({
      defaultBaseKeyOffset: this.settings.defaultBaseKeyOffset ?? DEFAULT_APP_SETTINGS.defaultBaseKeyOffset,
      defaultKeyOffset: this.settings.defaultKeyOffset ?? DEFAULT_APP_SETTINGS.defaultKeyOffset,
      autoDetectBaseKey: this.settings.autoDetectBaseKey ?? DEFAULT_APP_SETTINGS.autoDetectBaseKey,
      defaultTranspose: this.settings.defaultTranspose ?? DEFAULT_APP_SETTINGS.defaultTranspose,
      defaultSpeed: this.settings.defaultSpeed ?? DEFAULT_APP_SETTINGS.defaultSpeed,
      defaultHoldNotes: this.settings.defaultHoldNotes ?? DEFAULT_APP_SETTINGS.defaultHoldNotes,
      defaultTapThresholdMs: this.settings.defaultTapThresholdMs ?? DEFAULT_APP_SETTINGS.defaultTapThresholdMs,
      defaultMergeNotes: this.settings.defaultMergeNotes ?? DEFAULT_APP_SETTINGS.defaultMergeNotes,
      defaultMergeToleranceMs: this.settings.defaultMergeToleranceMs ?? DEFAULT_APP_SETTINGS.defaultMergeToleranceMs,
      defaultAutoChord: this.settings.defaultAutoChord ?? DEFAULT_APP_SETTINGS.defaultAutoChord,
      defaultChordDetectionMilliseconds: this.settings.defaultChordDetectionMilliseconds ?? DEFAULT_APP_SETTINGS.defaultChordDetectionMilliseconds,
      defaultEnableKeyChangeMarkers: this.settings.defaultEnableKeyChangeMarkers ?? DEFAULT_APP_SETTINGS.defaultEnableKeyChangeMarkers,
    }).then(result => {
      if (strict && !result) throw new Error('Unable to save song defaults.');
      return result;
    }).catch(error => {
      if (strict) throw error;
      console.error(error);
    });
  }

  /** Commit the draft before anything can import a song with old defaults. */
  async finishOnboarding(draft: Partial<AppSettings>, preferences: Pick<UiPreferences, 'locale' | 'animationTiming'> = {}) {
    const telemetryOptIn = draft.telemetryOptIn ?? this.settings.telemetryOptIn;
    await this.folderSync;
    await api.songs.watchFolder(null);
    this.settings = { ...this.settings, ...draft, telemetryOptIn: false, onboardingCompleted: false };
    const { ui, activeGameId, selectedInstrumentIds, selectedLayoutIds, ...settings } = this.settings;
    if (!await api.settings.updateSettings(settings)) throw new Error('Unable to save setup preferences.');
    await this.syncSongDefaults(true);
    const folder = this.settings.midiFolder.trim();
    let count = 0;
    try {
      if (folder) {
        const songs = await api.songs.scanFolder(folder);
        const root = folder.replace(/\\/g, '/').replace(/\/+$/, '').toLowerCase() + '/';
        count = songs.filter(song => song.filePath.replace(/\\/g, '/').toLowerCase().startsWith(root)).length;
        if (this.settings.autoScanMidiFolder) await api.songs.watchFolder(folder);
      }
      if (!await api.settings.updateSettings({
        onboardingCompleted: true,
        telemetryOptIn,
        ui: { ...preferences, lastViewedPage: 'songs', selectedPlaylistId: 'songs' },
      })) {
        throw new Error('Unable to save setup completion.');
      }
    } catch (error) {
      await api.songs.watchFolder(null).catch(console.error);
      throw error;
    }
    this.settings.onboardingCompleted = true;
    this.settings.telemetryOptIn = telemetryOptIn;
    await telemetry.setEnabled(telemetryOptIn);
    this.onAutoEnableListenModeChanged?.(this.settings.autoEnableListenMode);
    updateStore.configureTimer(this.settings, false);
    return count;
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

  private async saveOverlapSettings(patch: Partial<OverlapSettings>) {
    this.pendingOverlapSettings = { ...this.pendingOverlapSettings, ...patch };
    if (this.savingOverlapBehavior) return;
    this.savingOverlapBehavior = true;
    try {
      // Keep one native write in flight and replace queued switches with the
      // latest values. Editing overlap settings must not resync song defaults or locations.
      while (this.pendingOverlapSettings !== null) {
        const settings = this.pendingOverlapSettings;
        this.pendingOverlapSettings = null;
        try {
          await api.settings.updateSettings(settings);
        } catch (error) {
          console.error('[SettingsStore] Save overlap behavior failed:', error);
        }
      }
    } finally {
      this.savingOverlapBehavior = false;
    }
  }

  update<K extends keyof AppSettings>(key: K, value: AppSettings[K]) {
    if (key === 'repressGapMs') value = normalizeRepressGapMs(value) as AppSettings[K];
    if ((key === 'overlapBehavior' || key === 'repressGapMs') && this.settings[key] === value) return;
    this.settings[key] = value;
    if (key === 'overlapBehavior' || key === 'repressGapMs') {
      void this.saveOverlapSettings({ [key]: value });
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
      telemetry.setEnabled(this.settings.onboardingCompleted && value as boolean);
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
    this.settings.hotkeys = createDefaultSettings().hotkeys;
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
    this.settings.defaultBaseKeyOffset = DEFAULT_APP_SETTINGS.defaultBaseKeyOffset;
    this.settings.defaultKeyOffset = DEFAULT_APP_SETTINGS.defaultKeyOffset;
    this.settings.autoDetectBaseKey = DEFAULT_APP_SETTINGS.autoDetectBaseKey;
    this.settings.defaultTranspose = DEFAULT_APP_SETTINGS.defaultTranspose;
    this.settings.defaultSpeed = DEFAULT_APP_SETTINGS.defaultSpeed;
    this.save();
    notificationCenter.info(t('settings.toastTitleSongReset'), t('settings.toastSongResetSuccess'));
    setTimeout(() => {
      this.isResettingSongDefaults = false;
    }, 600);
  }

  resetNotesDefaults() {
    this.isResettingNotesDefaults = true;
    this.settings.defaultHoldNotes = DEFAULT_APP_SETTINGS.defaultHoldNotes;
    this.settings.defaultTapThresholdMs = DEFAULT_APP_SETTINGS.defaultTapThresholdMs;
    this.settings.defaultMergeNotes = DEFAULT_APP_SETTINGS.defaultMergeNotes;
    this.settings.defaultMergeToleranceMs = DEFAULT_APP_SETTINGS.defaultMergeToleranceMs;
    this.save();
    notificationCenter.info(t('settings.toastTitleNotesReset'), t('settings.toastNotesResetSuccess'));
    setTimeout(() => {
      this.isResettingNotesDefaults = false;
    }, 600);
  }

  resetAllDefaults() {
    this.isResettingAll = true;
    this.pendingOverlapSettings = null;
    this.settings = { ...createDefaultSettings(), onboardingCompleted: this.settings.onboardingCompleted };
    this.settings.autoCorrectThreshold = getDefaultAutoCorrectThreshold(this.autoCorrectThresholdTicks);
    api.settings.resetSettings()
      .catch((error) => console.error('[SettingsStore] Failed to reset persisted settings:', error))
      .finally(() => this.save());
    this.syncHotkeys();
    void this.syncMidiFolder();
    themeStore.setTheme(DEFAULT_APP_SETTINGS.theme);
    themeStore.setAccentColor(DEFAULT_APP_SETTINGS.accentColor);
    notificationCenter.info(t('settings.toastTitleReset'), t('settings.toastResetSuccess'));
    setTimeout(() => {
      this.isResettingAll = false;
    }, 600);
  }
}

export const settingsStore = new SettingsStore();
