import defaultReleaseNotes from '../../../../RELEASE.md?raw';

/**
 * Returns the bundled default release notes markdown directly from the repo's RELEASE.md.
 */
export function getDefaultReleaseNotes(): string {
  return defaultReleaseNotes;
}

/**
 * Returns the release notes markdown, preferring the latest remote release notes body if available,
 * and falling back to the 1:1 RELEASE.md bundled with the repository.
 */
export function getReleaseNotesMarkdown(remoteBody?: string | null): string {
  if (remoteBody && remoteBody.trim().length > 0) {
    return remoteBody;
  }
  return defaultReleaseNotes;
}
