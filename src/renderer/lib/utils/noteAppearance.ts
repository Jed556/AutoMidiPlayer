/** Choose the higher-contrast foreground using linear sRGB luminance. */
export function getNoteTextColor(background: string): '#000000' | '#ffffff' {
  const hex = background.trim().replace(/^#/, '');
  let channels: number[];
  if (/^[0-9a-f]{3}$/i.test(hex)) {
    channels = [...hex].map(value => parseInt(value + value, 16));
  } else if (/^[0-9a-f]{6}$/i.test(hex)) {
    channels = [0, 2, 4].map(index => parseInt(hex.slice(index, index + 2), 16));
  } else {
    const rgb = background.match(/^rgb\(\s*([\d.]+)[,\s]+([\d.]+)[,\s]+([\d.]+)\s*\)$/i);
    channels = rgb ? rgb.slice(1).map(Number) : [255, 255, 255];
  }
  const linear = channels.map(channel => {
    const value = Math.max(0, Math.min(255, channel)) / 255;
    return value <= 0.04045 ? value / 12.92 : ((value + 0.055) / 1.055) ** 2.4;
  });
  const luminance = linear[0] * 0.2126 + linear[1] * 0.7152 + linear[2] * 0.0722;
  return (luminance + 0.05) / 0.05 >= 1.05 / (luminance + 0.05) ? '#000000' : '#ffffff';
}
