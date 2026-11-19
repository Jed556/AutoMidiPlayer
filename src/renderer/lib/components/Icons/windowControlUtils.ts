export interface WindowControlIconProps {
  maximized?: boolean;
  hovered?: boolean;
  trigger?: number | boolean;
  size?: number;
  class?: string;
  style?: string;
  title?: string;
  duration?: number;
}

// Shared by the mute and window-close icons: upper-right stroke leads by 20%.
export function crossStrokeProgress(progress: number) {
  const clamped = Math.max(0, Math.min(1, progress));
  const ease = (p: number) => 1 - Math.pow(1 - p, 3);
  return {
    first: ease(Math.min(1, clamped / 0.8)),
    second: ease(Math.max(0, (clamped - 0.2) / 0.8))
  };
}
