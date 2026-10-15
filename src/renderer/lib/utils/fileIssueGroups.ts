import type { LibraryFileIssue } from '$shared/types/ipc';
import type { MidiFile } from '$shared/types/midi';

export interface DuplicateFileGroup {
  id: string;
  title: string;
  copies: { path: string; name: string; isCurrent: boolean }[];
}

const fileName = (path: string) => path.split(/[\\/]/).pop() || path;

export function groupDuplicateFiles(issues: LibraryFileIssue[], songs: Pick<MidiFile, 'id' | 'title' | 'filePath'>[]): DuplicateFileGroup[] {
  const songsById = new Map(songs.map(song => [song.id, song]));
  const groups = new Map<string, DuplicateFileGroup>();

  for (const issue of issues) {
    if (issue.kind !== 'duplicate') continue;
    const song = issue.songId ? songsById.get(issue.songId) : undefined;
    const currentPath = song?.filePath || issue.relatedPath;
    const id = issue.songId ? `song:${issue.songId}` : `path:${currentPath || issue.path}`;
    let group = groups.get(id);
    if (!group) {
      group = {
        id,
        title: song?.title || (currentPath ? fileName(currentPath).replace(/\.(mid|midi)$/i, '') : issue.title),
        copies: [],
      };
      if (currentPath) group.copies.push({ path: currentPath, name: fileName(currentPath), isCurrent: true });
      groups.set(id, group);
    }
    if (!group.copies.some(copy => copy.path === issue.path)) {
      group.copies.push({ path: issue.path, name: fileName(issue.path), isCurrent: false });
    }
  }

  return [...groups.values()];
}
