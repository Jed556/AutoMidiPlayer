export function isMidiFilePath(path: string): boolean {
  return /\.midi?$/i.test(path.trim());
}

export function filterMidiFilePaths(paths: readonly string[] | null | undefined): string[] {
  if (!paths?.length) return [];
  const seen = new Set<string>();
  const result: string[] = [];
  for (const path of paths) {
    if (typeof path !== 'string') continue;
    const trimmed = path.trim();
    if (!trimmed || !isMidiFilePath(trimmed) || seen.has(trimmed)) continue;
    seen.add(trimmed);
    result.push(trimmed);
  }
  return result;
}

export function normalizeTargetPlaylistId(playlistId: string | null | undefined): string | null {
  if (!playlistId || playlistId === 'songs' || playlistId === 'all-songs') {
    return null;
  }
  return playlistId;
}

export function resolveDropTargetPlaylistId(options: {
  activeTab: string;
  selectedPlaylistId: string | null | undefined;
  hoveredPlaylistId?: string | null;
}): string | null {
  if (options.hoveredPlaylistId) {
    return normalizeTargetPlaylistId(options.hoveredPlaylistId);
  }
  if ((options.activeTab || '').toLowerCase() === 'songs') {
    return normalizeTargetPlaylistId(options.selectedPlaylistId);
  }
  return null;
}
