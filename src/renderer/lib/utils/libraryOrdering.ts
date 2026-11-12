export type LibrarySortMode = 'custom' | 'title' | 'artist' | 'duration' | 'dateAdded';
export type LibrarySortDirection = 'asc' | 'desc';

interface SortableSong {
  id: string;
  title?: string;
  artist?: string;
  dateAdded?: string;
  durationSeconds?: number;
  durationMs?: number;
}

/** Unknown/new songs append in their source order without changing saved ranks. */
export function sortLibrarySongs<T extends SortableSong>(
  songs: readonly T[], mode: LibrarySortMode, direction: LibrarySortDirection, customOrder: readonly string[] = [],
): T[] {
  const ranks = new Map(customOrder.map((id, index) => [id, index]));
  const sign = direction === 'desc' ? -1 : 1;
  const date = (value?: string) => {
    const parsed = value ? Date.parse(value) : 0;
    return Number.isFinite(parsed) ? parsed : 0;
  };
  return [...songs].sort((a, b) => {
    if (mode === 'custom') return (ranks.get(a.id) ?? Infinity) - (ranks.get(b.id) ?? Infinity) || 0;
    if (mode === 'title' || mode === 'artist') return (a[mode] ?? '').localeCompare(b[mode] ?? '') * sign;
    if (mode === 'duration') return ((a.durationSeconds ?? (a.durationMs ?? 0) / 1000)
      - (b.durationSeconds ?? (b.durationMs ?? 0) / 1000)) * sign;
    return (date(a.dateAdded) - date(b.dateAdded)) * sign;
  });
}

/** Reorder visible IDs while retaining unavailable songs in their existing slots. */
export function replaceVisibleSongOrder(
  savedOrder: readonly string[], visibleIds: readonly string[], reorderedIds: readonly string[],
): string[] {
  const visible = new Set(visibleIds);
  if (visible.size !== visibleIds.length || reorderedIds.length !== visibleIds.length
    || new Set(reorderedIds).size !== reorderedIds.length || reorderedIds.some(id => !visible.has(id))) {
    throw new Error('The reordered songs must match the current library selection.');
  }
  const fullOrder = [...new Set([...savedOrder, ...visibleIds])];
  let index = 0;
  return fullOrder.map(id => visible.has(id) ? reorderedIds[index++] : id);
}
