import type { DefaultConfigConflict, DefaultConfigUpdates, DuplicateConfig } from '../../../shared/types/games.js';

function canonical(value: unknown): string | undefined {
  return JSON.stringify(value, (_key, item) => item && typeof item === 'object' && !Array.isArray(item)
    ? Object.fromEntries(Object.keys(item).sort().map(key => [key, item[key]])) : item);
}

export function formatConfigJson(source: string): string {
  try { return JSON.stringify(JSON.parse(source), null, 2); }
  catch { return source; }
}

export function changedConfigFields(current: string, bundled: string): string[] {
  try {
    const before = JSON.parse(current);
    const after = JSON.parse(bundled);
    if (!before || !after || typeof before !== 'object' || typeof after !== 'object') return [];
    return [...new Set([...Object.keys(before), ...Object.keys(after)])]
      .filter(key => canonical(before[key]) !== canonical(after[key])).sort();
  } catch { return []; }
}
export interface ConfigReviewItem extends DefaultConfigConflict { duplicate?: DuplicateConfig }

function parseRecord(source: string): Record<string, unknown> | null {
  try {
    const parsed: unknown = JSON.parse(source);
    return parsed && typeof parsed === 'object' && !Array.isArray(parsed)
      ? parsed as Record<string, unknown>
      : null;
  } catch {
    return null;
  }
}

function readText(record: Record<string, unknown> | null, key: string): string | null {
  const value = record?.[key];
  return typeof value === 'string' && value.length > 0 ? value : null;
}

export function configReviewHeading(
  item: Pick<ConfigReviewItem, 'path' | 'currentJson' | 'bundledJson' | 'duplicate'>,
): string {
  const before = parseRecord(item.currentJson);
  const after = parseRecord(item.bundledJson);
  const beforeName = readText(before, 'name') ?? readText(before, 'displayName');
  const afterName = readText(after, 'name') ?? readText(after, 'displayName');
  const beforeId = readText(before, 'id');
  const afterId = readText(after, 'id');
  const fileName = item.path.split('/').at(-1) || item.path;

  if (beforeName && beforeName === afterName) return beforeName;
  const sharedId = (beforeId && afterId && beforeId === afterId ? afterId : null) ?? item.duplicate?.configId ?? null;
  if (sharedId) return sharedId;
  return fileName;
}

export function configReviewItems(report: DefaultConfigUpdates | null): ConfigReviewItem[] {
  if (!report) return [];
  return [...report.conflicts, ...(report.duplicates ?? []).map(duplicate => ({
    id: duplicate.id, path: duplicate.keep.path, kind: duplicate.kind, name: duplicate.configId,
    currentJson: duplicate.olderFiles[0].json, bundledJson: duplicate.keep.json,
    token: duplicate.token, duplicate,
  }))];
}

