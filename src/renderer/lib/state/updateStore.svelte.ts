import type { GitRelease, GitAsset, UpdateCheckResult, PreparedUpdate, UpdateProgress } from '$shared/types/update';
import { compareVersions, parseChangelog } from '../utils/versionUtils';
import { notificationCenter } from '../ui/notificationCenter.svelte';
import { api, isTauri } from '../backend/index.js';
import { t } from '../i18n';
import { startupProgress } from '../backend/startupProgress';

class UpdateStore {
  isCheckingUpdate = $state(false);
  updateAvailable = $state(false);
  latestRelease = $state<GitRelease | null>(null);
  latestVersion = $state('v8.0.0');
  programVersion = $state('8.0.0');
  lastCheckedTime = $state<Date | null>(null);
  errorMessage = $state<string | null>(null);
  includeBeta = $state(false);
  isPreparingUpdate = $state(false);
  isApplyingUpdate = $state(false);
  isDownloadingUpdate = $state(false);
  updateDownloaded = $state(false);
  progress = $state<UpdateProgress>({ phase: 'preparing', progress: 4 });

  private _timer: ReturnType<typeof setInterval> | null = null;
  private _isInitialized = false;
  private autoDownload = false;
  private preparedTag: string | null = null;
  private preparedPackage: PreparedUpdate | null = null;
  private prepareTask: Promise<PreparedUpdate> | null = null;
  private downloadTask: Promise<void> | null = null;
  private splashEntered: (() => void) | null = null;

  constructor() {
    this.init();
  }

  async init() {
    if (this._isInitialized) return;
    this._isInitialized = true;

    try {
      const v = await api.settings.getAppVersion();
      if (v) {
        this.programVersion = v.replace(/^v/i, '');
      }
    } catch (e) {
      console.warn('[UpdateService] Failed to get app version from backend:', e);
    }
  }

  get changelogList(): string[] {
    return parseChangelog(this.latestRelease?.body);
  }

  /**
   * Checks GitHub releases API for updates.
   * Compares the latest matching release version with the current programVersion.
   */
  async checkForUpdates(manual: boolean = false, includeBetaOverride?: boolean): Promise<UpdateCheckResult> {
    if (this.isCheckingUpdate || this.isPreparingUpdate || this.isApplyingUpdate) {
      return {
        updateAvailable: this.updateAvailable,
        currentVersion: this.programVersion,
        latestVersion: this.latestVersion,
        latestRelease: this.latestRelease || undefined,
      };
    }

    this.isCheckingUpdate = true;
    this.errorMessage = null;

    try {
      const includeBeta = includeBetaOverride !== undefined
        ? includeBetaOverride
        : this.includeBeta;

      let rawList: any = null;

      // 1. Prefer native backend fetch (native OS network call, immune to WebView CSP restrictions, matching WPF)
      if (isTauri) {
        try {
          const { invoke } = await import('@tauri-apps/api/core');
          const jsonText = await invoke<string>('fetch_github_releases', {
            userAgent: `AutoMidiPlayer/${this.programVersion}`,
          });
          if (jsonText) {
            rawList = JSON.parse(jsonText);
          }
        } catch (tauriErr) {
          console.warn('[UpdateService] Native fetch failed, falling back to browser fetch:', tauriErr);
        }
      }

      // 2. Fallback to browser fetch (allowed by connect-src in index.html)
      if (!rawList) {
        const res = await fetch('https://api.github.com/repos/Jed556/AutoMidiPlayer/releases', {
          signal: AbortSignal.timeout(15000),
          headers: {
            'Accept': 'application/vnd.github.v3+json',
          },
        });

        if (!res.ok) {
          throw new Error(`GitHub API returned status ${res.status}: ${res.statusText}`);
        }

        rawList = await res.json();
      }

      if (!Array.isArray(rawList)) {
        throw new Error('Invalid release format returned by GitHub API');
      }

      const releases: GitRelease[] = rawList
        .filter((r: any) => !r.draft)
        .map((r: any) => {
          const assets: GitAsset[] = Array.isArray(r.assets)
            ? r.assets.map((a: any) => ({
                name: a.name || '',
                downloadUrl: a.browser_download_url || '',
                size: a.size || 0,
                contentType: a.content_type,
              }))
            : [];

          const tag = r.tag_name || '0.0.0';
          return {
            id: r.id,
            tagName: tag,
            version: tag.replace(/^v/i, ''),
            name: r.name || tag,
            body: r.body || '',
            htmlUrl: r.html_url || '',
            draft: Boolean(r.draft),
            prerelease: Boolean(r.prerelease),
            publishedAt: r.published_at || '',
            assets,
          };
        });

      // Filter by beta/prerelease preference
      const filtered = releases.filter(r => includeBeta || !r.prerelease);

      // Sort descending by semantic version
      filtered.sort((a, b) => compareVersions(b.version, a.version));

      const top = filtered[0];

      if (top) {
        const hasNewer = compareVersions(top.version, this.programVersion) > 0;
        this.latestRelease = top;
        this.updateDownloaded = this.preparedTag === top.tagName && Boolean(this.preparedPackage?.downloaded);
        this.latestVersion = top.tagName.startsWith('v') ? top.tagName : `v${top.tagName}`;

        if (hasNewer) {
          this.updateAvailable = true;
          notificationCenter.success(
            t('settings.toastTitleUpdate'),
            t('settings.toastUpdateAvailable', { version: this.latestVersion })
          );
          if (this.autoDownload && isTauri) {
            void this.downloadNativeUpdate(top.tagName).catch(error => {
              console.warn('[UpdateService] Background download failed:', error);
            });
          }
        } else {
          this.updateAvailable = false;
          if (manual) {
            notificationCenter.info(
              t('settings.toastTitleUpdate'),
              t('settings.toastUpdateUpToDate')
            );
          }
        }

        return {
          updateAvailable: this.updateAvailable,
          currentVersion: this.programVersion,
          latestVersion: this.latestVersion,
          latestRelease: top,
        };
      } else {
        this.updateAvailable = false;
        if (manual) {
          notificationCenter.info(
            t('settings.toastTitleUpdate'),
            t('settings.toastUpdateUpToDate')
          );
        }
        return {
          updateAvailable: false,
          currentVersion: this.programVersion,
        };
      }
    } catch (err: any) {
      const msg = err?.message || 'Unknown network error';
      this.errorMessage = msg;
      console.error('[UpdateService] Check for updates failed:', err);

      if (manual) {
        notificationCenter.error(
          t('settings.toastTitleUpdate'),
          `Failed to check updates: ${msg}`
        );
      }

      return {
        updateAvailable: this.updateAvailable,
        currentVersion: this.programVersion,
        error: msg,
      };
    } finally {
      this.isCheckingUpdate = false;
      this.lastCheckedTime = new Date();
    }
  }

  /**
   * Configures the automatic background update check timer according to user settings.
   */
  configureTimer(options?: { autoCheckUpdates: boolean; updateCheckFrequency: string; includeBetaUpdates?: boolean; autoDownloadUpdates?: boolean }, checkImmediately = true) {
    if (this._timer) {
      clearInterval(this._timer);
      this._timer = null;
    }

    if (options) {
      this.includeBeta = Boolean(options.includeBetaUpdates);
      this.autoDownload = Boolean(options.autoDownloadUpdates);
      if (!options.autoCheckUpdates) {
        return;
      }
    }

    const frequency = options?.updateCheckFrequency || '10min';
    let intervalMs = 10 * 60 * 1000; // 10 minutes default
    if (frequency === 'hourly') {
      intervalMs = 60 * 60 * 1000;
    } else if (frequency === 'daily') {
      intervalMs = 24 * 60 * 60 * 1000;
    }

    // Schedule background recurring timer
    this._timer = setInterval(() => {
      this.checkForUpdates(false);
    }, intervalMs);

    // Startup owns and awaits its initial check; settings changes can check now.
    if (checkImmediately) void this.checkForUpdates(false);
  }

  /**
   * Opens the release notes / GitHub release page in the default web browser.
   */
  async openReleaseNotes() {
    const url = this.latestRelease?.htmlUrl || 'https://github.com/Jed556/AutoMidiPlayer/releases';
    await api.settings.openUrl(url);
  }

  private async prepareNativeUpdate(tagName: string): Promise<PreparedUpdate> {
    if (this.prepareTask) await this.prepareTask;
    if (this.preparedTag === tagName && this.preparedPackage) return this.preparedPackage;
    // Finish an older background package before replacing the native pending update.
    if (this.downloadTask) {
      await this.downloadTask;
      return this.prepareNativeUpdate(tagName);
    }
    this.prepareTask = api.updates.prepare(tagName);
    try {
      const prepared = await this.prepareTask;
      this.preparedTag = tagName;
      this.preparedPackage = prepared;
      this.updateDownloaded = prepared.downloaded;
      return prepared;
    } finally {
      this.prepareTask = null;
    }
  }

  private receiveProgress = (progress: UpdateProgress) => {
    this.progress = progress;
  };

  private async downloadNativeUpdate(tagName: string) {
    const prepared = await this.prepareNativeUpdate(tagName);
    if (prepared.downloaded) return;
    if (this.downloadTask) return this.downloadTask;
    this.isDownloadingUpdate = true;
    const task = api.updates.download(this.receiveProgress);
    this.downloadTask = task;
    try {
      await task;
      prepared.downloaded = true;
      this.updateDownloaded = true;
    } finally {
      this.downloadTask = null;
      this.isDownloadingUpdate = false;
    }
  }

  onSplashEntered() {
    this.splashEntered?.();
    this.splashEntered = null;
  }

  /** Apply the selected signed native package behind the shared startup splash. */
  async downloadUpdate(beforeInstall: () => Promise<void>) {
    if (this.isPreparingUpdate || this.isApplyingUpdate || !this.latestRelease) return;
    this.isPreparingUpdate = true;
    this.errorMessage = null;
    try {
      const tagName = this.latestRelease.tagName;
      const entered = new Promise<void>(resolve => { this.splashEntered = resolve; });
      startupProgress.beginUpdate();
      if (!this.isDownloadingUpdate) this.progress = { phase: 'preparing', progress: 4 };
      this.isApplyingUpdate = true;
      await entered;
      const prepared = await this.prepareNativeUpdate(tagName);
      await beforeInstall();
      if (!prepared.downloaded) await this.downloadNativeUpdate(tagName);
      await api.updates.install(this.receiveProgress);
    } catch (error) {
      this.errorMessage = String(error);
      this.isApplyingUpdate = false;
      this.splashEntered = null;
      notificationCenter.error(t('settings.toastTitleUpdate'), `${t('startup.updateFailed')} ${this.errorMessage}`);
    } finally {
      this.isPreparingUpdate = false;
    }
  }

  /**
   * Sets a test simulated program version (e.g. "7.14.3") for testing update detection against real GitHub releases.
   */
  async testSimulateVersion(mockVersion: string) {
    this.programVersion = mockVersion;
    return await this.checkForUpdates(true);
  }
}

export const updateStore = new UpdateStore();
