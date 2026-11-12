import { loadUiPreferences, saveUiPreferences } from '../backend/preferences.js';
import { ScrollPositionStore } from '../utils/scrollPositions.js';

export const scrollPositionStore = new ScrollPositionStore(
  async () => (await loadUiPreferences()).scrollPositions,
  positions => saveUiPreferences({ scrollPositions: positions }),
);
