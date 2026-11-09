/** Legacy speed range, with the quarter-step presets retained for existing users. */
export const PLAYBACK_SPEEDS = [
  ...Array.from({ length: 20 }, (_, index) => (index + 1) / 10),
  0.25, 0.75, 1.25, 1.75, 2.5, 3, 3.5, 4,
].sort((a, b) => a - b);

export const PLAYBACK_SPEED_OPTIONS = PLAYBACK_SPEEDS.map(value => ({
  value,
  label: `${value}x`,
}));
