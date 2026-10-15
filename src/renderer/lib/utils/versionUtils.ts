export interface ParsedVersion {
  major: number;
  minor: number;
  patch: number;
  build: number;
  prerelease: string;
  raw: string;
}

/**
 * Parses a version string (e.g. "v7.14.6", "8.0.0", "7.14.1-beta") into structured components.
 */
export function parseVersion(vStr: string): ParsedVersion {
  const raw = vStr.trim();
  const clean = raw.replace(/^v/i, '');
  const [versionPart, ...preParts] = clean.split('-');
  const prerelease = preParts.join('-').toLowerCase();

  const parts = versionPart.split('.').map(p => {
    const n = parseInt(p, 10);
    return isNaN(n) ? 0 : n;
  });

  return {
    major: parts[0] ?? 0,
    minor: parts[1] ?? 0,
    patch: parts[2] ?? 0,
    build: parts[3] ?? 0,
    prerelease,
    raw,
  };
}

/**
 * Compares two version strings.
 * Returns:
 *   > 0 if v1 > v2 (v1 is newer)
 *   < 0 if v1 < v2 (v1 is older)
 *   0 if v1 == v2
 */
export function compareVersions(v1Str: string, v2Str: string): number {
  const v1 = parseVersion(v1Str);
  const v2 = parseVersion(v2Str);

  if (v1.major !== v2.major) return v1.major - v2.major;
  if (v1.minor !== v2.minor) return v1.minor - v2.minor;
  if (v1.patch !== v2.patch) return v1.patch - v2.patch;
  if (v1.build !== v2.build) return v1.build - v2.build;

  // Pre-release comparison: full release is newer than a pre-release of the same version
  // e.g. 8.0.0 > 8.0.0-beta.1
  if (!v1.prerelease && v2.prerelease) return 1;
  if (v1.prerelease && !v2.prerelease) return -1;
  if (v1.prerelease && v2.prerelease) {
    return v1.prerelease.localeCompare(v2.prerelease, undefined, { numeric: true });
  }

  return 0;
}

/**
 * Formats a byte count into human-readable string (e.g. "74.0 MB").
 */
export function formatBytes(bytes: number, decimals: number = 1): string {
  if (bytes === 0) return '0 B';
  const k = 1024;
  const dm = decimals < 0 ? 0 : decimals;
  const sizes = ['B', 'KB', 'MB', 'GB', 'TB'];
  const i = Math.floor(Math.log(bytes) / Math.log(k));
  const val = parseFloat((bytes / Math.pow(k, i)).toFixed(dm));
  return `${val} ${sizes[i]}`;
}

/**
 * Parses markdown release body into readable bullet points or sections.
 */
export function parseChangelog(body?: string): string[] {
  if (!body || !body.trim()) return [];

  const lines = body.split(/\r?\n/);
  const items: string[] = [];

  for (const line of lines) {
    const trimmed = line.trim();
    if (!trimmed) continue;

    // Skip section headers like "# What's New?", "## Covered issues"
    if (trimmed.startsWith('#')) continue;

    // Skip full changelog compare links
    if (trimmed.toLowerCase().includes('full changelog') && trimmed.includes('http')) continue;

    // Bullet points
    if (trimmed.startsWith('- ') || trimmed.startsWith('* ') || trimmed.startsWith('• ')) {
      const clean = trimmed.replace(/^[-*•]\s+/, '').trim();
      // Remove raw markdown links or git commit hashes formatting if simple
      const formatted = clean
        .replace(/\[([^\]]+)\]\([^)]+\)/g, '$1') // convert [text](url) to text
        .replace(/\*\*([^*]+)\*\*/g, '$1')       // bold removal for plain text
        .replace(/`([^`]+)`/g, '$1');            // code removal
      if (formatted) items.push(formatted);
    } else if (items.length === 0 && !trimmed.startsWith('[')) {
      // Single line summary if no bullet points
      items.push(trimmed);
    }
  }

  return items;
}
