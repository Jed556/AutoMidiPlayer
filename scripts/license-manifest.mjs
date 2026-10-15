import { createHash } from 'node:crypto';

export function renderLicenseMarkdown(entries) {
  const texts = new Map();
  const lines = ['# Third-Party Licenses', '',
    'Notices for the current Svelte/Tauri/Rust dependency graph, including transitive dependencies and build tools. Rust dependencies cover Windows, Linux, and both macOS architectures.', '',
    'The original-project attribution and bundled soundfont notices are retained. Legacy WPF-only dependencies are excluded.', '',
    'The About page reads the index below automatically. Full license texts are shared by reference to preserve each copyright notice without duplicating identical text.', '',
    'Regenerate dependency entries with `pnpm licenses:generate` after updating lockfiles. Supplemental notices also live in this file; no separate license-document directory is needed.', '',
    '## Library index', ''];
  for (const entry of [...entries].sort((a, b) => a.name.localeCompare(b.name) || a.version.localeCompare(b.version, undefined, { numeric: true }))) {
    const { licenseText, ...metadata } = entry;
    const text = licenseText.trim().replaceAll('\r\n', '\n').replace(/[ \t]+$/gm, '');
    if (!text) throw new Error(`Empty notice: ${entry.name}`);
    const textId = createHash('sha256').update(text).digest('hex').slice(0, 20);
    texts.set(textId, text);
    lines.push(`<!-- amp-license: ${JSON.stringify({ ...metadata, textId })} -->`,
      `### ${entry.name}${entry.version ? ` ${entry.version}` : ''}`, '',
      `- License: ${entry.licenseName}`, `- Repository/source: [${entry.url}](${entry.url})`,
      `- Category: ${entry.kind}`, `- Full notice: [${textId}](#notice-${textId})`,
      ...(entry.notice ? [`- ${entry.notice}`] : []), '');
  }
  lines.push('## Full license texts', '');
  for (const [id, text] of texts) {
    const longest = Math.max(0, ...[...text.matchAll(/`+/g)].map(match => match[0].length));
    const fence = '`'.repeat(Math.max(3, longest + 1));
    lines.push(`<!-- amp-license-text: ${id} -->`, `### Notice ${id}`, '', `${fence}text`, text, fence, '');
  }
  return lines.join('\n');
}
