export interface LicenseInfo {
  name: string;
  version: string;
  licenseName: string;
  url: string;
  licenseText: string;
  kind?: string;
  notice?: string;
}

/** Read the index and shared full notices in THIRD-PARTY-LICENSES.md. */
export function parseLicenseMarkdown(markdown: string): LicenseInfo[] {
  const normalized = markdown.replaceAll('\r\n', '\n');
  const texts = new Map<string, string>();
  const pattern = /<!-- amp-license-text: ([a-f0-9]+) -->\s*\n### [^\n]+\n\n(`{3,})text\n([\s\S]*?)\n\2(?=\n|$)/g;
  for (const match of normalized.matchAll(pattern)) texts.set(match[1], match[3]);
  const entries: LicenseInfo[] = [];
  const seen = new Set<string>();
  for (const match of normalized.matchAll(/<!-- amp-license: (.+) -->/g)) {
    const { textId, ...entry } = JSON.parse(match[1]);
    if (!entry.name || typeof entry.version !== 'string' || !entry.licenseName
      || typeof entry.url !== 'string' || (entry.url && !entry.url.startsWith('https://')) || !texts.has(textId)) {
      throw new Error(`Invalid license entry: ${entry.name ?? 'unknown'}`);
    }
    const key = `${entry.name}@${entry.version}`;
    if (seen.has(key)) throw new Error(`Duplicate license entry: ${key}`);
    seen.add(key);
    entries.push({ ...entry, licenseText: texts.get(textId)! });
  }
  return entries.sort((a, b) => a.name.localeCompare(b.name) || a.version.localeCompare(b.version, undefined, { numeric: true }));
}
