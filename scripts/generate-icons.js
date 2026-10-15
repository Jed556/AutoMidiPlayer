import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const rootDir = path.resolve(__dirname, '..');
const fluentDir = path.join(rootDir, 'node_modules/@fluentui/svg-icons/icons');
const localIconsDir = path.join(rootDir, 'src/renderer/assets/icons');
const outputTs = path.join(rootDir, 'src/renderer/lib/components/Common/iconData.ts');

function parseSvg(svgContent) {
  const viewBoxMatch = svgContent.match(/viewBox="([^"]+)"/);
  const viewBox = viewBoxMatch ? viewBoxMatch[1] : '0 0 24 24';

  const paths = [];
  const pathRegex = /<path[^>]*d="([^"]+)"/g;
  let match;
  while ((match = pathRegex.exec(svgContent)) !== null) {
    paths.push(match[1]);
  }
  return { viewBox, paths };
}

// Canonical Fluent Icon Definitions (Symbol -> snake_case base prefix)
// Size preference order: 24 -> 20 -> 16 -> 28 -> 32 -> 12 -> 48
const FLUENT_ICONS = [
  { symbol: 'Add', base: 'add' },
  { symbol: 'AppsListDetail', base: 'apps_list_detail' },
  { symbol: 'ArrowCircleDown', base: 'arrow_circle_down' },
  { symbol: 'ArrowCircleUp', base: 'arrow_circle_up' },
  { symbol: 'ArrowClockwise', base: 'arrow_clockwise' },
  { symbol: 'ArrowDownload', base: 'arrow_download' },
  { symbol: 'ArrowLeft', base: 'arrow_left' },
  { symbol: 'ArrowRight', base: 'arrow_right' },
  { symbol: 'ArrowSort', base: 'arrow_sort' },
  { symbol: 'ArrowSync', base: 'arrow_sync' },
  { symbol: 'Bug', base: 'bug' },
  { symbol: 'CalendarLtr', base: 'calendar_ltr' },
  { symbol: 'CalendarMonth', base: 'calendar_month' },
  { symbol: 'Cd', base: 'cd' },
  { symbol: 'Checkmark', base: 'checkmark' },
  { symbol: 'CheckmarkCircle', base: 'checkmark_circle' },
  { symbol: 'ChevronDown', base: 'chevron_down' },
  { symbol: 'ChevronLeft', base: 'chevron_left' },
  { symbol: 'ChevronRight', base: 'chevron_right' },
  { symbol: 'ChevronUp', base: 'chevron_up' },
  { symbol: 'Clock', base: 'clock' },
  { symbol: 'Color', base: 'color' },
  { symbol: 'Copy', base: 'copy' },
  { symbol: 'Delete', base: 'delete' },
  { symbol: 'Dismiss', base: 'dismiss' },
  { symbol: 'DismissCircle', base: 'dismiss_circle' },
  { symbol: 'Document', base: 'document' },
  { symbol: 'DocumentQuestionMark', base: 'document_question_mark' },
  { symbol: 'Edit', base: 'edit' },
  { symbol: 'ErrorCircle', base: 'error_circle' },
  { symbol: 'Eye', base: 'eye' },
  { symbol: 'EyeOff', base: 'eye_off' },
  { symbol: 'Flash', base: 'flash' },
  { symbol: 'Folder', base: 'folder' },
  { symbol: 'FolderSync', base: 'folder_sync' },
  { symbol: 'Games', base: 'games' },
  { symbol: 'Globe', base: 'globe' },
  { symbol: 'GlobeSearch', base: 'globe_search' },
  { symbol: 'Grid', base: 'grid' },
  { symbol: 'Guitar', base: 'guitar' },
  { symbol: 'Handshake', base: 'handshake' },
  { symbol: 'Headphones', base: 'headphones' },
  { symbol: 'Heart', base: 'heart' },
  { symbol: 'HeartPulse', base: 'heart_pulse' },
  { symbol: 'Info', base: 'info' },
  { symbol: 'Keyboard', base: 'keyboard' },
  { symbol: 'Layer', base: 'layer' },
  { symbol: 'Lightbulb', base: 'lightbulb' },
  { symbol: 'Location', base: 'location' },
  { symbol: 'LockClosed', base: 'lock_closed' },
  { symbol: 'LockOpen', base: 'lock_open' },
  { symbol: 'MoreHorizontal', base: 'more_horizontal' },
  { symbol: 'MusicNote1', base: 'music_note_1' },
  { symbol: 'MusicNote2', base: 'music_note_2' },
  { symbol: 'Notepad', base: 'notepad' },
  { symbol: 'Open', base: 'open' },
  { symbol: 'PeopleTeam', base: 'people_team' },
  { symbol: 'Person', base: 'person' },
  { symbol: 'PersonCircle', base: 'person_circle' },
  { symbol: 'Pin', base: 'pin' },
  { symbol: 'PlayCircle', base: 'play_circle' },
  { symbol: 'Pulse', base: 'pulse' },
  { symbol: 'QuestionCircle', base: 'question_circle' },
  { symbol: 'ReadingList', base: 'reading_list' },
  { symbol: 'Reorder', base: 'reorder' },
  { symbol: 'ScanDash', base: 'scan_dash' },
  { symbol: 'Search', base: 'search' },
  { symbol: 'Settings', base: 'settings' },
  { symbol: 'ShieldCheckmark', base: 'shield_checkmark' },
  { symbol: 'Sparkle', base: 'sparkle' },
  { symbol: 'Speaker2', base: 'speaker_2' },
  { symbol: 'SpeakerOff', base: 'speaker_off' },
  { symbol: 'Square', base: 'square' },
  { symbol: 'SquareMultiple', base: 'square_multiple' },
  { symbol: 'Star', base: 'star' },
  { symbol: 'StarEmphasis', base: 'star_emphasis' },
  { symbol: 'Stop', base: 'stop' },
  { symbol: 'Subtract', base: 'subtract' },
  { symbol: 'TagMultiple', base: 'tag_multiple' },
  { symbol: 'TextBulletListSquare', base: 'text_bullet_list_square' },
  { symbol: 'TextDescription', base: 'text_description' },
  { symbol: 'Timer', base: 'timer' },
  { symbol: 'TopSpeed', base: 'top_speed' },
  { symbol: 'TriangleRight', base: 'triangle_right' },
  { symbol: 'Warning', base: 'warning' },
  { symbol: 'Moon', base: 'weather_moon' },
  { symbol: 'Sun', base: 'weather_sunny' }
];

const fluentFiles = fs.existsSync(fluentDir) ? fs.readdirSync(fluentDir) : [];

function findBestFluentFile(baseName, variant) {
  const sizes = [24, 20, 16, 28, 32, 12, 48];
  for (const s of sizes) {
    const candidate = `${baseName}_${s}_${variant}.svg`;
    if (fluentFiles.includes(candidate)) return candidate;
  }
  const fallback = fluentFiles.find(f => f.startsWith(`${baseName}_`) && f.endsWith(`_${variant}.svg`));
  return fallback || null;
}

const iconRegistry = {};

// 1. Process Fluent System Icons (Consolidated outlined + filled)
for (const item of FLUENT_ICONS) {
  const regFile = findBestFluentFile(item.base, 'regular');
  const fillFile = findBestFluentFile(item.base, 'filled');

  if (!regFile && !fillFile) {
    console.warn(`Warning: Could not find SVG for ${item.symbol} (${item.base})`);
    continue;
  }

  let viewBox = '0 0 24 24';
  let paths = [];
  let filledPaths = undefined;
  let filledViewBox = undefined;

  if (regFile) {
    const svg = fs.readFileSync(path.join(fluentDir, regFile), 'utf8');
    const parsed = parseSvg(svg);
    viewBox = parsed.viewBox;
    paths = parsed.paths;
  }

  if (fillFile) {
    const svg = fs.readFileSync(path.join(fluentDir, fillFile), 'utf8');
    const parsed = parseSvg(svg);
    filledPaths = parsed.paths;
    if (parsed.viewBox !== viewBox) {
      filledViewBox = parsed.viewBox;
    }
    if (paths.length === 0) {
      paths = filledPaths;
      viewBox = parsed.viewBox;
    }
  }

  const def = { viewBox, paths };
  if (filledPaths && filledPaths.length > 0) {
    def.filledPaths = filledPaths;
    if (filledViewBox) def.filledViewBox = filledViewBox;
  }

  iconRegistry[item.symbol] = def;
}

// 2. Process Local WPF Icons
if (fs.existsSync(localIconsDir)) {
  function scanDir(dir) {
    const items = fs.readdirSync(dir, { withFileTypes: true });
    for (const item of items) {
      const fullPath = path.join(dir, item.name);
      if (item.isDirectory()) {
        scanDir(fullPath);
      } else if (item.name.endsWith('.svg')) {
        const svg = fs.readFileSync(fullPath, 'utf8');
        const parsed = parseSvg(svg);

        const rawName = path.basename(item.name, '.svg');
        const isFilledVariant = rawName.endsWith(' Filled') || rawName.endsWith('_Filled');
        const cleanName = rawName.replace(/[\s_]+Filled$/i, '').replace(/\s+/g, '');

        if (isFilledVariant) {
          if (iconRegistry[cleanName]) {
            iconRegistry[cleanName].filledPaths = parsed.paths;
            if (parsed.viewBox !== iconRegistry[cleanName].viewBox) {
              iconRegistry[cleanName].filledViewBox = parsed.viewBox;
            }
          } else {
            iconRegistry[`${cleanName}Filled`] = parsed;
          }
        } else {
          // If not already defined by Fluent icon set, add it:
          if (!iconRegistry[cleanName]) {
            iconRegistry[cleanName] = parsed;
          }
        }
      }
    }
  }
  scanDir(localIconsDir);
}

// 3. Custom Zoom & Transpose Geometries
if (iconRegistry['Add']) {
  iconRegistry['ZoomIn'] = {
    viewBox: iconRegistry['Add'].viewBox,
    paths: iconRegistry['Add'].paths,
    filledPaths: iconRegistry['Add'].filledPaths
  };
}
if (iconRegistry['Subtract']) {
  iconRegistry['ZoomOut'] = {
    viewBox: iconRegistry['Subtract'].viewBox,
    paths: iconRegistry['Subtract'].paths,
    filledPaths: iconRegistry['Subtract'].filledPaths
  };
}

iconRegistry['TransposeIgnore'] = {
  viewBox: '0 0 800 800',
  paths: ['M750,225c0-19.9-7.9-39-22-53c-14.1-14.1-33.1-22-53-22H125c-19.9,0-39,7.9-53,22c-14.1,14.1-22,33.1-22,53c0,86.6,0,263.4,0,350c0,19.9,7.9,39,22,53c14.1,14.1,33.1,22,53,22h550c19.9,0,39-7.9,53-22c14.1-14.1,22-33.1,22-53V225zM700,225v350c0,6.6-2.6,13-7.3,17.7c-4.7,4.7-11,7.3-17.7,7.3H125c-6.6,0-13-2.6-17.7-7.3c-4.7-4.7-7.3-11-7.3-17.7V225c0-6.6,2.6-13,7.3-17.7c4.7-4.7,11-7.3,17.7-7.3h550c6.6,0,13,2.6,17.7,7.3C697.4,212,700,218.4,700,225zM375,275l0,249.7c0,13.8,11.2,25,25,25s25-11.2,25-25L425,275c0-13.8-11.2-25-25-25C386.2,250,375,261.2,375,275zM574.9,375v49.7c0,13.8,11.2,25,25,25c13.8,0,25-11.2,25-25V375c0-13.8-11.2-25-25-25C586.1,350,574.9,361.2,574.9,375zM175.1,375v49.7c0,13.8,11.2,25,25,25c13.8,0,25-11.2,25-25V375c0-13.8-11.2-25-25-25S175.1,361.2,175.1,375zM475,325v150c0,13.8,11.2,25,25,25s25-11.2,25-25V325c0-13.8-11.2-25-25-25S475,311.2,475,325zM275,325v150c0,13.8,11.2,25,25,25c13.8,0,25-11.2,25-25V325c0-13.8-11.2-25-25-25C286.2,300,275,311.2,275,325z']
};
iconRegistry['TransposeUp'] = {
  viewBox: '0 0 800 800',
  paths: ['M750,225c0-19.9-7.9-39-22-53c-14.1-14.1-33.1-22-53-22H125c-19.9,0-39,7.9-53,22c-14.1,14.1-22,33.1-22,53c0,86.6,0,263.4,0,350c0,19.9,7.9,39,22,53c14.1,14.1,33.1,22,53,22h550c19.9,0,39-7.9,53-22c14.1-14.1,22-33.1,22-53V225zM700,225v350c0,6.6-2.6,13-7.3,17.7c-4.7,4.7-11,7.3-17.7,7.3H125c-6.6,0-13-2.6-17.7-7.3c-4.7-4.7-7.3-11-7.3-17.7V225c0-6.6,2.6-13,7.3-17.7c4.7-4.7,11-7.3,17.7-7.3h550c6.6,0,13,2.6,17.7,7.3C697.4,212,700,218.4,700,225zM425.1,335c0-13.8-11.2-25-25-25c-13.8,0-25,11.2-25,25L375,464.7c0,13.8,11.2,25,25,25s25-11.2,25-25L425.1,335zM225.1,375c0-13.8-11.2-25-25-25s-25,11.2-25,25v49.7c0,13.8,11.2,25,25,25s25-11.2,25-25V375zM624.9,278.1c0-13.8-11.2-25-25-25c-13.8,0-25,11.2-25,25v246.6c0,13.8,11.2,25,25,25s25-11.2,25-25V278.1zM300,330c-13.8,0-25,11.2-25,25l0,90c0,13.8,11.2,25,25,25c13.8,0,25-11.2,25-25l0-90C325,341.2,313.8,330,300,330zM500,280c-13.8,0-25,11.2-25,25v190c0,13.8,11.2,25,25,25c13.8,0,25-11.2,25-25V305C525,291.2,513.8,280,500,280z']
};
iconRegistry['TransposeDown'] = {
  viewBox: '0 0 800 800',
  paths: ['M750,225c0-19.9-7.9-39-22-53c-14.1-14.1-33.1-22-53-22H125c-19.9,0-39,7.9-53,22c-14.1,14.1-22,33.1-22,53c0,86.6,0,263.4,0,350c0,19.9,7.9,39,22,53c14.1,14.1,33.1,22,53,22h550c19.9,0,39-7.9,53-22c14.1-14.1,22-33.1,22-53V225zM700,225v350c0,6.6-2.6,13-7.3,17.7c-4.7,4.7-11,7.3-17.7,7.3H125c-6.6,0-13-2.6-17.7-7.3c-4.7-4.7-7.3-11-7.3-17.7V225c0-6.6,2.6-13,7.3-17.7c4.7-4.7,11-7.3,17.7-7.3h550c6.6,0,13,2.6,17.7,7.3C697.4,212,700,218.4,700,225zM375,335l0,129.7c0,13.8,11.2,25,25,25s25-11.2,25-25L425,335c0-13.8-11.2-25-25-25C386.2,310,375,321.2,375,335zM574.9,375v49.7c0,13.8,11.2,25,25,25c13.8,0,25-11.2,25-25V375c0-13.8-11.2-25-25-25C586.1,350,574.9,361.2,574.9,375zM175.1,278.1v246.6c0,13.8,11.2,25,25,25c13.8,0,25-11.2,25-25V278.1c0-13.8-11.2-25-25-25S175.1,264.3,175.1,278.1zM475,355v90c0,13.8,11.2,25,25,25s25-11.2,25-25v-90c0-13.8-11.2-25-25-25S475,341.2,475,355zM275,305v190c0,13.8,11.2,25,25,25c13.8,0,25-11.2,25-25V305c0-13.8-11.2-25-25-25C286.2,280,275,291.2,275,305z']
};
iconRegistry['TransposeSmart'] = {
  viewBox: '0 0 800 800',
  paths: ['M750,225c0-19.9-7.9-39-22-53c-14.1-14.1-33.1-22-53-22H125c-19.9,0-39,7.9-53,22c-14.1,14.1-22,33.1-22,53c0,86.6,0,263.4,0,350c0,19.9,7.9,39,22,53c14.1,14.1,33.1,22,53,22h550c19.9,0,39-7.9,53-22c14.1-14.1,22-33.1,22-53V225zM700,225v350c0,6.6-2.6,13-7.3,17.7c-4.7,4.7-11,7.3-17.7,7.3H125c-6.6,0-13-2.6-17.7-7.3c-4.7-4.7-7.3-11-7.3-17.7V225c0-6.6,2.6-13,7.3-17.7c4.7-4.7,11-7.3,17.7-7.3h550c6.6,0,13,2.6,17.7,7.3C697.4,212,700,218.4,700,225zM375,305l0,189.7c0,13.8,11.2,25,25,25s25-11.2,25-25L425,305c0-13.8-11.2-25-25-25C386.2,280,375,291.2,375,305zM574.9,385v29.7c0,13.8,11.2,25,25,25c13.8,0,25-11.2,25-25V385c0-13.8-11.2-25-25-25C586.1,360,574.9,371.2,574.9,385zM175.1,345v109.7c0,13.8,11.2,25,25,25c13.8,0,25-11.2,25-25V345c0-13.8-11.2-25-25-25S175.1,331.2,175.1,345zM475,295v210c0,13.8,11.2,25,25,25s25-11.2,25-25V295c0-13.8-11.2-25-25-25S475,281.2,475,295zM275,275v250c0,13.8,11.2,25,25,25c13.8,0,25-11.2,25-25V275c0-13.8-11.2-25-25-25C286.2,250,275,291.2,275,305z']
};
iconRegistry['RemoveCircle'] = {
  viewBox: '0 0 16 16',
  paths: ['M8 1.5a6.5 6.5 0 1 0 0 13 6.5 6.5 0 0 0 0-13M0 8a8 8 0 1 1 16 0A8 8 0 0 1 0 8 M11.75 8a.75.75 0 0 1-.75.75H5a.75.75 0 0 1 0-1.5h6a.75.75 0 0 1 .75.75']
};
iconRegistry['DeleteCircle'] = {
  viewBox: '0 0 16 16',
  paths: ['M8 1.5a6.5 6.5 0 1 0 0 13 6.5 6.5 0 0 0 0-13M0 8a8 8 0 1 1 16 0A8 8 0 0 1 0 8 M11.005 4.995a.75.75 0 0 1 0 1.06L9.061 8l1.944 1.945a.75.75 0 1 1-1.06 1.06L8 9.061l-1.945 1.944a.75.75 0 1 1-1.06-1.06L6.939 8 4.995 6.055a.75.75 0 1 1 1.06-1.06L8 6.939l1.945-1.944a.75.75 0 0 1 1.06 0']
};


// Sort icons alphabetically
const sortedRegistry = {};
for (const key of Object.keys(iconRegistry).sort()) {
  sortedRegistry[key] = iconRegistry[key];
}

// Generate iconData.ts
const fileHeader = `// Auto-generated by scripts/generate-icons.js
// Bundles Microsoft Fluent System Icons + WPF Local SVGs for 1:1 UI parity.

export interface IconDef {
  viewBox: string;
  paths: string[];
  filledPaths?: string[];
  filledViewBox?: string;
}

export interface SliderPreset {
  type: 'slider';
  id: string;
  label: string;
  min: number;
  max: number;
  step?: number;
  defaultValue: number;
  unit?: string;
  presets?: { label: string; value: number }[];
}

export interface OptionPreset {
  type: 'option';
  id: string;
  label: string;
  defaultValue: any;
  options: {
    label: string;
    value: any;
    props?: Record<string, any>;
  }[];
}

export interface TogglePreset {
  type: 'toggle';
  id: string;
  label: string;
  defaultValue: boolean;
  onProps?: Record<string, any>;
  offProps?: Record<string, any>;
}

export type IconParamPreset = SliderPreset | OptionPreset | TogglePreset;

export interface IconMetadata {
  name: string;
  description?: string;
  animationParamId?: string;
  params: IconParamPreset[];
}

export type IconName = keyof typeof ICONS;

export const ICONS: Record<string, IconDef> = ${JSON.stringify(sortedRegistry, null, 2)};
`;

fs.writeFileSync(outputTs, fileHeader, 'utf8');
console.log(`Successfully generated consolidated icon registry with ${Object.keys(sortedRegistry).length} unique icons! Saved to: ${outputTs}`);
