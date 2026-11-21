export interface PlaylistThemePreset {
  id: string;
  name: string;
  gradient: string;
  bannerColor: string;
  swatch: string;
}

export const PLAYLIST_ICONS = [
  'MusicNote2',
  'MusicNote1',
  'Album',
  'Cd',
  'Guitar',
  'Headphones',
  'Heart',
  'Star',
  'Sparkle',
  'Flash',
  'Games',
  'Lyrics',
  'Radio',
  'ReadingList',
  'Moon',
  'Sun',
] as const;

export const PLAYLIST_THEMES: PlaylistThemePreset[] = [
  {
    id: 'emerald',
    name: 'Emerald',
    gradient: 'linear-gradient(135deg, #1DB954 0%, #0d9488 100%)',
    bannerColor: 'rgba(29, 185, 84, 0.45)',
    swatch: '#1DB954',
  },
  {
    id: 'ocean',
    name: 'Ocean',
    gradient: 'linear-gradient(135deg, #2563eb 0%, #06b6d4 100%)',
    bannerColor: 'rgba(37, 99, 235, 0.45)',
    swatch: '#2563eb',
  },
  {
    id: 'violet',
    name: 'Violet',
    gradient: 'linear-gradient(135deg, #7c3aed 0%, #c084fc 100%)',
    bannerColor: 'rgba(124, 58, 237, 0.45)',
    swatch: '#7c3aed',
  },
  {
    id: 'rose',
    name: 'Rose',
    gradient: 'linear-gradient(135deg, #e11d48 0%, #fb7185 100%)',
    bannerColor: 'rgba(225, 29, 72, 0.45)',
    swatch: '#e11d48',
  },
  {
    id: 'amber',
    name: 'Amber',
    gradient: 'linear-gradient(135deg, #d97706 0%, #f59e0b 100%)',
    bannerColor: 'rgba(217, 119, 6, 0.45)',
    swatch: '#f59e0b',
  },
  {
    id: 'magenta',
    name: 'Magenta',
    gradient: 'linear-gradient(135deg, #c026d3 0%, #ec4899 100%)',
    bannerColor: 'rgba(192, 38, 211, 0.45)',
    swatch: '#d946ef',
  },
  {
    id: 'crimson',
    name: 'Crimson',
    gradient: 'linear-gradient(135deg, #dc2626 0%, #ea580c 100%)',
    bannerColor: 'rgba(220, 38, 38, 0.45)',
    swatch: '#dc2626',
  },
  {
    id: 'slate',
    name: 'Slate',
    gradient: 'linear-gradient(135deg, #334155 0%, #64748b 100%)',
    bannerColor: 'rgba(100, 116, 139, 0.45)',
    swatch: '#64748b',
  },
];

export function findMatchingTheme(gradient?: string, bannerColor?: string): PlaylistThemePreset {
  return (
    PLAYLIST_THEMES.find((theme) => theme.gradient === gradient || theme.bannerColor === bannerColor)
    ?? PLAYLIST_THEMES[0]
  );
}
