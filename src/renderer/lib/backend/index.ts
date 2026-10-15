import { createAppApi } from './nativeApi.js';
import { isTauriEnvironment } from './runtime.js';

export {
  getBundledGameImage,
  loadedBundleToGameInfo,
  loadedMidiToMidiFile,
  songRecordToMidiFile,
  type RawLoadedBundle,
  type RawLoadedMidiFile,
  type RawSongRecord,
} from './mappers.js';

/** The renderer's sole backend boundary, organized by domain. */
export const api = createAppApi();
export const isTauri = isTauriEnvironment;
