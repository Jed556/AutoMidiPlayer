export interface GitAsset {
  name: string;
  downloadUrl: string;
  size: number;
  contentType?: string;
}

export interface GitRelease {
  id: number;
  tagName: string;
  version: string;
  name: string;
  body: string;
  htmlUrl: string;
  draft: boolean;
  prerelease: boolean;
  publishedAt: string;
  assets: GitAsset[];
}

export interface UpdateCheckResult {
  updateAvailable: boolean;
  currentVersion: string;
  latestVersion?: string;
  latestRelease?: GitRelease;
  error?: string;
}

export type UpdatePhase = 'preparing' | 'downloading' | 'verifying' | 'extracting' | 'installing' | 'restarting';

export interface UpdateProgress {
  phase: UpdatePhase;
  progress: number;
  downloadedBytes?: number;
  totalBytes?: number;
}

export interface PreparedUpdate {
  version: string;
  downloaded: boolean;
}

export interface UpdateResume {
  version: string;
  progress: number;
}

export interface UpdatesApi {
  prepare: (tagName: string) => Promise<PreparedUpdate>;
  download: (onProgress: (progress: UpdateProgress) => void) => Promise<void>;
  install: (onProgress: (progress: UpdateProgress) => void) => Promise<void>;
  complete: () => Promise<void>;
}
