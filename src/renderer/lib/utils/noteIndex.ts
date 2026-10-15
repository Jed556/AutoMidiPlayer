import type { RollNote } from '$shared/types/midi';

export const noteStart = (note: RollNote) => note.startTime ?? note.startMs / 1000;
export const noteDuration = (note: RollNote) => note.duration ?? note.lengthMs / 1000;

/** Built once per note collection, not once per frame or pointer event. */
export function createNoteIndex(notes: readonly RollNote[]) {
  const sorted = [...notes].sort((a, b) => noteStart(a) - noteStart(b));
  const starts = sorted.map(noteStart);
  const ends = sorted.map((note, index) => starts[index] + noteDuration(note));
  const blockSize = 128;
  const blockEnds: number[] = [];
  for (let i = 0; i < sorted.length; i++) {
    const block = Math.floor(i / blockSize);
    blockEnds[block] = Math.max(blockEnds[block] ?? -Infinity, ends[i]);
  }
  return {
    notes: sorted,
    *between(start: number, end: number): Generator<RollNote> {
      let lo = 0;
      let hi = sorted.length;
      while (lo < hi) {
        const mid = (lo + hi) >>> 1;
        if (starts[mid] <= end) lo = mid + 1;
        else hi = mid;
      }
      // Skip expired blocks independently: one sustained note must not force a
      // scan of every intervening note in a dense song.
      for (let block = 0; block * blockSize < lo; block++) {
        if (blockEnds[block] < start) continue;
        const limit = Math.min(lo, (block + 1) * blockSize);
        for (let i = block * blockSize; i < limit; i++) {
          if (ends[i] >= start) yield sorted[i];
        }
      }
    },
  };
}
