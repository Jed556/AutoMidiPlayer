import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const rootDir = path.resolve(__dirname, '..');

const defaultsDir = path.join(rootDir, 'src-tauri', 'resources', 'defaults');
const publicImagesDir = path.join(rootDir, 'public', 'images', 'games');

const localAppData = process.env.LOCALAPPDATA || path.join(process.env.USERPROFILE || '', 'AppData', 'Local');
const localAppDataPlayerDir = path.join(localAppData, 'Jed556', 'AutoMIDIPlayer');

// Custom formatter that preserves multi-item rows for 2D arrays, making keyboard grids & notes human-readable
function formatJson(data, currentIndent = 0) {
  const pad = '  '.repeat(currentIndent);
  const childPad = '  '.repeat(currentIndent + 1);

  if (data === null) return 'null';
  if (typeof data === 'string') return JSON.stringify(data);
  if (typeof data === 'number' || typeof data === 'boolean') return String(data);

  if (Array.isArray(data)) {
    if (data.length === 0) return '[]';

    // 2D Array: each inner array becomes a formatted row
    if (Array.isArray(data[0])) {
      const rowStrings = data.map((row, idx) => {
        const isLast = idx === data.length - 1;
        const rowContent = row.map(v => JSON.stringify(v)).join(', ');
        return childPad + rowContent + (isLast ? '' : ',');
      });
      return '[\n' + rowStrings.join('\n') + '\n' + pad + ']';
    }

    // Array of chord pad objects
    if (typeof data[0] === 'object' && data[0] !== null) {
      const items = data.map((item, idx) => {
        const isLast = idx === data.length - 1;
        if (item.name && item.pitchClasses) {
          return childPad + '{ "name": ' + JSON.stringify(item.name) + ', "pitchClasses": [' + item.pitchClasses.join(', ') + '] }' + (isLast ? '' : ',');
        }
        return childPad + formatJson(item, currentIndent + 1) + (isLast ? '' : ',');
      });
      return '[\n' + items.join('\n') + '\n' + pad + ']';
    }

    // 1D Array of primitives
    return '[' + data.map(v => JSON.stringify(v)).join(', ') + ']';
  }

  if (typeof data === 'object') {
    const entries = Object.entries(data).filter(([_, v]) => v !== undefined);
    if (entries.length === 0) return '{}';
    const lines = entries.map(([k, v], idx) => {
      const isLast = idx === entries.length - 1;
      const formattedVal = formatJson(v, currentIndent + 1);
      return childPad + JSON.stringify(k) + ': ' + formattedVal + (isLast ? '' : ',');
    });
    return '{\n' + lines.join('\n') + '\n' + pad + '}';
  }

  return JSON.stringify(data);
}

function writeConfigFile(targetRoots, relativePath, data) {
  const formatted = formatJson(data) + '\n';
  for (const root of targetRoots) {
    const fullPath = path.join(root, relativePath);
    fs.mkdirSync(path.dirname(fullPath), { recursive: true });
    fs.writeFileSync(fullPath, formatted, 'utf-8');
  }
}

function copyImageFile(targetRoots, srcFileName, relGameFolder) {
  const src = path.join(publicImagesDir, srcFileName);
  if (!fs.existsSync(src)) return;
  for (const root of targetRoots) {
    const dest = path.join(root, 'Games', relGameFolder, 'icon.png');
    fs.mkdirSync(path.dirname(dest), { recursive: true });
    fs.copyFileSync(src, dest);
  }
}

// Target roots to sync: bundled resources + live local app data
const targetRoots = [defaultsDir];
if (fs.existsSync(localAppDataPlayerDir)) {
  targetRoots.push(localAppDataPlayerDir);
}

// Clean old defaults in both target directories
for (const root of targetRoots) {
  const rootLayouts = path.join(root, 'KeyboardLayouts');
  const rootGames = path.join(root, 'Games');
  if (fs.existsSync(rootLayouts)) {
    fs.rmSync(rootLayouts, { recursive: true, force: true });
  }
  // Remove "Sky Children of the Light" if it exists in local app data
  const oldSky = path.join(rootGames, 'Sky Children of the Light');
  if (fs.existsSync(oldSky)) {
    fs.rmSync(oldSky, { recursive: true, force: true });
  }
  fs.mkdirSync(rootLayouts, { recursive: true });
  fs.mkdirSync(rootGames, { recursive: true });
}

// ==========================================
// 1. KEYBOARD LAYOUTS
// ==========================================
const keyboardLayouts = [
  // ── Genshin Impact ──────────────────────────────────
  {
    file: 'Genshin_QWERTY.json',
    data: {
      id: 'Genshin_QWERTY',
      name: 'QWERTY',
      keys: [
        ['q', 'w', 'e', 'r', 't', 'y', 'u'],
        ['a', 's', 'd', 'f', 'g', 'h', 'j'],
        ['z', 'x', 'c', 'v', 'b', 'n', 'm']
      ]
    }
  },
  {
    file: 'Genshin_QWERTY_Drums.json',
    data: {
      id: 'Genshin_QWERTY_Drums',
      name: 'QWERTY (Drums)',
      keys: [
        ['q', 'w', 'e', 'r'],
        ['a', 's', 'd', 'f']
      ]
    }
  },
  {
    file: 'Genshin_QWERTZ.json',
    data: {
      id: 'Genshin_QWERTZ',
      name: 'QWERTZ',
      keys: [
        ['q', 'w', 'e', 'r', 't', 'z', 'u'],
        ['a', 's', 'd', 'f', 'g', 'h', 'j'],
        ['y', 'x', 'c', 'v', 'b', 'n', 'm']
      ]
    }
  },
  {
    file: 'Genshin_AZERTY.json',
    data: {
      id: 'Genshin_AZERTY',
      name: 'AZERTY',
      keys: [
        ['a', 'z', 'e', 'r', 't', 'y', 'u'],
        ['q', 's', 'd', 'f', 'g', 'h', 'j'],
        ['w', 'x', 'c', 'v', 'b', 'n', ',']
      ]
    }
  },
  {
    file: 'Genshin_DVORAK.json',
    data: {
      id: 'Genshin_DVORAK',
      name: 'DVORAK',
      keys: [
        ['x', ',', 'd', 'o', 'k', 't', 'f'],
        ['a', ';', 'h', 'y', 'u', 'j', 'c'],
        ['/', 'b', 'i', '.', 'n', 'l', 'm']
      ]
    }
  },
  {
    file: 'Genshin_DVORAKLeft.json',
    data: {
      id: 'Genshin_DVORAKLeft',
      name: 'DVORAKLeft',
      keys: [
        ['w', 'b', 'j', 'y', 'g', 'r', 't'],
        ['k', 'u', 'f', '5', 'c', 'h', '8'],
        ['l', 'x', 'd', 'v', 'e', 'n', '6']
      ]
    }
  },
  {
    file: 'Genshin_DVORAKRight.json',
    data: {
      id: 'Genshin_DVORAKRight',
      name: 'DVORAKRight',
      keys: [
        ['e', 'm', 'g', 'y', 'j', 'o', 'i'],
        ['f', 'u', 'k', '8', '.', 'h', '5'],
        ['d', 'c', 'l', ',', 'p', 'n', '7']
      ]
    }
  },
  {
    file: 'Genshin_Colemak.json',
    data: {
      id: 'Genshin_Colemak',
      name: 'Colemak',
      keys: [
        ['q', 'w', 'k', 's', 'f', 'o', 'i'],
        ['a', 'd', 'g', 'e', 't', 'h', 'y'],
        ['z', 'x', 'c', 'v', 'b', 'j', 'm']
      ]
    }
  },
  {
    file: 'Genshin_QWERTY_Two_Octaves.json',
    data: {
      id: 'Genshin_QWERTY_Two_Octaves',
      name: 'QWERTY (Two Octaves)',
      keys: [
        ['q', 'w', 'e', 'r', 't', 'y', 'u'],
        ['a', 's', 'd', 'f', 'g', 'h', 'j']
      ]
    }
  },
  {
    file: 'Genshin_QWERTY_Melody.json',
    data: {
      id: 'Genshin_QWERTY_Melody',
      name: 'QWERTY (Melody)',
      keys: [
        ['a', 's', 'd', 'f', 'g', 'h', 'j'],
        ['z', 'x', 'c', 'v', 'b', 'n', 'm']
      ],
      chordKeys: [
        ['q', 'w', 'e', 'r', 't', 'y', 'u']
      ]
    }
  },

  // ── Sky: Children of the Light ──────────────────────
  {
    file: 'Sky_QWERTY_15.json',
    data: {
      id: 'Sky_QWERTY_15',
      name: 'QWERTY',
      keys: [
        ['y', 'u', 'i', 'o', 'p'],
        ['h', 'j', 'k', 'l', ';'],
        ['n', 'm', ',', '.', '/']
      ]
    }
  },
  {
    file: 'Sky_QWERTY_8.json',
    data: {
      id: 'Sky_QWERTY_8',
      name: 'QWERTY',
      keys: [
        ['y', 'u', 'i', 'o'],
        ['p', 'h', 'j', 'k']
      ]
    }
  },
  {
    file: 'Sky_QWERTY_4.json',
    data: {
      id: 'Sky_QWERTY_4',
      name: 'QWERTY',
      keys: [
        ['y', 'u'],
        ['i', 'o']
      ]
    }
  },

  // ── Wuthering Waves ─────────────────────────────────
  {
    file: 'WutheringWaves_QWERTY.json',
    data: {
      id: 'WutheringWaves_QWERTY',
      name: 'QWERTY',
      keys: [
        ['q', 'w', 'e', 'r', 't', 'y', 'u'],
        ['a', 's', 'd', 'f', 'g', 'h', 'j'],
        ['z', 'x', 'c', 'v', 'b', 'n', 'm']
      ]
    }
  },

  // ── Roblox ──────────────────────────────────────────
  {
    file: 'Roblox_QWERTY_61Key.json',
    data: {
      id: 'Roblox_QWERTY_61Key',
      name: 'QWERTY',
      keys: [
        ['1', '!', '2', '@', '3', '4', '$', '5', '%', '6', '^', '7'],
        ['8', '*', '9', '(', '0', 'q', 'Q', 'w', 'W', 'e', 'E', 'r'],
        ['t', 'T', 'y', 'Y', 'u', 'i', 'I', 'o', 'O', 'p', 'P', 'a'],
        ['s', 'S', 'd', 'D', 'f', 'g', 'G', 'h', 'H', 'j', 'J', 'k'],
        ['l', 'L', 'z', 'Z', 'x', 'c', 'C', 'v', 'V', 'b', 'B', 'n'],
        ['m']
      ],
      sustainKey: 'space'
    }
  },
  {
    file: 'Roblox_QWERTY_88Key.json',
    data: {
      id: 'Roblox_QWERTY_88Key',
      name: 'QWERTY',
      keys: [
        ['^1', '^2', '^3'],
        ['^4', '^5', '^6', '^7', '^8', '^9', '^0', '^q', '^w', '^e', '^r', '^t'],
        ['1', '!', '2', '@', '3', '4', '$', '5', '%', '6', '^', '7'],
        ['8', '*', '9', '(', '0', 'q', 'Q', 'w', 'W', 'e', 'E', 'r'],
        ['t', 'T', 'y', 'Y', 'u', 'i', 'I', 'o', 'O', 'p', 'P', 'a'],
        ['s', 'S', 'd', 'D', 'f', 'g', 'G', 'h', 'H', 'j', 'J', 'k'],
        ['l', 'L', 'z', 'Z', 'x', 'c', 'C', 'v', 'V', 'b', 'B', 'n'],
        ['m', '^y', '^u', '^i', '^o', '^p', '^a', '^s', '^d', '^f', '^g', '^h'],
        ['^j']
      ],
      sustainKey: 'space'
    }
  },

  // ── Heartopia ───────────────────────────────────────
  {
    file: 'Heartopia_QWERTY_2Row.json',
    data: {
      id: 'Heartopia_QWERTY_2Row',
      name: 'QWERTY',
      keys: [
        ['q', 'w', 'e', 'r', 't', 'y', 'u', 'i'],
        ['a', 's', 'd', 'f', 'g', 'h', 'j']
      ]
    }
  },
  {
    file: 'Heartopia_QWERTY_3Row.json',
    data: {
      id: 'Heartopia_QWERTY_3Row',
      name: 'QWERTY',
      keys: [
        ['y', 'u', 'i', 'o', 'p'],
        ['h', 'j', 'k', 'l', ';'],
        ['n', 'm', ',', '.', '/']
      ]
    }
  },
  {
    file: 'Heartopia_QWERTY_22Key.json',
    data: {
      id: 'Heartopia_QWERTY_22Key',
      name: 'QWERTY',
      keys: [
        ['q', 'w', 'e', 'r', 't', 'y', 'u', 'i'],
        ['a', 's', 'd', 'f', 'g', 'h', 'j'],
        ['z', 'x', 'c', 'v', 'b', 'n', 'm']
      ]
    }
  },
  {
    file: 'Heartopia_QWERTY_37Key.json',
    data: {
      id: 'Heartopia_QWERTY_37Key',
      name: 'QWERTY',
      keys: [
        ['q', '2', 'w', '3', 'e', 'r', '5', 't', '6', 'y', '7', 'u', 'i'],
        ['z', 's', 'x', 'd', 'c', 'v', 'g', 'b', 'h', 'n', 'j', 'm'],
        [',', 'l', '.', ';', '/', 'o', '0', 'p', '-', '[', '=', ']']
      ]
    }
  },

  // ── Core Keeper ─────────────────────────────────────
  {
    file: 'CoreKeeper_QWERTY.json',
    data: {
      id: 'CoreKeeper_QWERTY',
      name: 'QWERTY',
      keys: [
        ['q', '2', 'w', '3', 'e', 'r', '5', 't', '6', 'y', '7', 'u'],
        ['z', 's', 'x', 'd', 'c', 'v', 'g', 'b', 'h', 'n', 'j', 'm']
      ]
    }
  },

  // ── HPMA ────────────────────────────────────────────
  {
    file: 'HPMA_QWERTY_14Key.json',
    data: {
      id: 'HPMA_QWERTY_14Key',
      name: 'QWERTY',
      keys: [
        [',', '.', '/', 'q', 'w', 'e', 'r'],
        ['z', 'x', 'c', 'v', 'b', 'n', 'm']
      ]
    }
  },
  {
    file: 'HPMA_QWERTY_36Key.json',
    data: {
      id: 'HPMA_QWERTY_36Key',
      name: 'QWERTY',
      keys: [
        ['t', '6', 'y', '7', 'u', 'i', '9', 'o', '0', 'p', '-', '['],
        [',', 'l', '.', ';', '/', 'q', '2', 'w', '3', 'e', '4', 'r'],
        ['z', 's', 'x', 'd', 'c', 'v', 'g', 'b', 'h', 'n', 'j', 'm']
      ]
    }
  },

  // ── NTE ─────────────────────────────────────────────
  {
    file: 'NTE_QWERTY_21Keys.json',
    data: {
      id: 'NTE_QWERTY_21Keys',
      name: 'QWERTY',
      keys: [
        ['q', 'w', 'e', 'r', 't', 'y', 'u'],
        ['a', 's', 'd', 'f', 'g', 'h', 'j'],
        ['z', 'x', 'c', 'v', 'b', 'n', 'm']
      ]
    }
  },
  {
    file: 'NTE_QWERTY_36Keys.json',
    data: {
      id: 'NTE_QWERTY_36Keys',
      name: 'QWERTY',
      keys: [
        ['q', 'Q', 'w', '^e', 'e', 'r', 'R', 't', 'T', 'y', '^u', 'u'],
        ['a', 'A', 's', '^d', 'd', 'f', 'F', 'g', 'G', 'h', '^j', 'j'],
        ['z', 'Z', 'x', '^c', 'c', 'v', 'V', 'b', 'B', 'n', '^m', 'm']
      ]
    }
  },

  // ── WWM ─────────────────────────────────────────────
  {
    file: 'WWM_QWERTY.json',
    data: {
      id: 'WWM_QWERTY',
      name: 'QWERTY',
      keys: [
        ['q', 'Q', 'w', '^e', 'e', 'r', 'R', 't', 'T', 'y', '^u', 'u'],
        ['a', 'A', 's', '^d', 'd', 'f', 'F', 'g', 'G', 'h', '^j', 'j'],
        ['z', 'Z', 'x', '^c', 'c', 'v', 'V', 'b', 'B', 'n', '^m', 'm']
      ]
    }
  },

  // ── BPSR ────────────────────────────────────────────
  {
    file: 'BPSR_QWERTY_61Key.json',
    data: {
      id: 'BPSR_QWERTY_61Key',
      name: 'QWERTY',
      keys: [
        ['1', '!', '2', '@', '3', '4', '$', '5', '%', '6', '^', '7'],
        ['8', '*', '9', '(', '0', 'q', 'Q', 'w', 'W', 'e', 'E', 'r'],
        ['t', 'T', 'y', 'Y', 'u', 'i', 'I', 'o', 'O', 'p', 'P', 'a'],
        ['s', 'S', 'd', 'D', 'f', 'g', 'G', 'h', 'H', 'j', 'J', 'k'],
        ['l', 'L', 'z', 'Z', 'x', 'c', 'C', 'v', 'V', 'b', 'B', 'n'],
        ['m']
      ],
      sustainKey: 'space'
    }
  },
  {
    file: 'BPSR_QWERTY_88Key.json',
    data: {
      id: 'BPSR_QWERTY_88Key',
      name: 'QWERTY',
      keys: [
        ['^1', '^2', '^3'],
        ['^4', '^5', '^6', '^7', '^8', '^9', '^0', '^q', '^w', '^e', '^r', '^t'],
        ['1', '!', '2', '@', '3', '4', '$', '5', '%', '6', '^', '7'],
        ['8', '*', '9', '(', '0', 'q', 'Q', 'w', 'W', 'e', 'E', 'r'],
        ['t', 'T', 'y', 'Y', 'u', 'i', 'I', 'o', 'O', 'p', 'P', 'a'],
        ['s', 'S', 'd', 'D', 'f', 'g', 'G', 'h', 'H', 'j', 'J', 'k'],
        ['l', 'L', 'z', 'Z', 'x', 'c', 'C', 'v', 'V', 'b', 'B', 'n'],
        ['m', '^y', '^u', '^i', '^o', '^p', '^a', '^s', '^d', '^f', '^g', '^h'],
        ['^j']
      ],
      sustainKey: 'space'
    }
  }
];

for (const layout of keyboardLayouts) {
  writeConfigFile(targetRoots, path.join('KeyboardLayouts', layout.file), layout.data);
}

// ==========================================
// 2. GAMES & INSTRUMENTS
// ==========================================

// ── 2.1 Genshin Impact ──────────────────────────────
const genshinDir = 'Genshin Impact';
writeConfigFile(targetRoots, path.join('Games', genshinDir, 'game.json'), {
  id: 'Genshin Impact',
  displayName: 'Genshin Impact',
  shortName: 'Genshin',
  processNames: ['GenshinImpact', 'YuanShen'],
  icon: 'icon.png',
  defaultInstrument: 'Windsong Lyre',
  defaultLayout: 'Genshin_QWERTY'
});
copyImageFile(targetRoots, 'Genshin_Impact.png', genshinDir);

const genshinStandardLayouts = [
  'Genshin_QWERTY', 'Genshin_QWERTZ', 'Genshin_AZERTY',
  'Genshin_DVORAK', 'Genshin_DVORAKLeft', 'Genshin_DVORAKRight', 'Genshin_Colemak'
];

const diatonic21Notes = [
  [72, 74, 76, 77, 79, 81, 83], // C5 D5 E5 F5 G5 A5 B5
  [60, 62, 64, 65, 67, 69, 71], // C4 D4 E4 F4 G4 A4 B4
  [48, 50, 52, 53, 55, 57, 59]  // C3 D3 E3 F3 G3 A3 B3
];

const vintageLyreNotes = [
  [72, 73, 75, 77, 79, 80, 82], // C5 Db5 Eb5 F5 G5 Ab5 Bb5
  [60, 62, 63, 65, 67, 69, 70], // C4 D4 Eb4 F4 G4 A4 Bb4
  [48, 50, 51, 53, 55, 57, 58]  // C3 D3 Eb3 F3 G3 A3 Bb3
];

const twoOctaveNotes = [
  [60, 62, 64, 65, 67, 69, 71], // C4 D4 E4 F4 G4 A4 B4
  [48, 50, 52, 53, 55, 57, 59]  // C3 D3 E3 F3 G3 A3 B3
];

const genshinChordPads = [
  { name: 'C', pitchClasses: [0, 4, 7] },
  { name: 'Dm', pitchClasses: [2, 5, 9] },
  { name: 'Em', pitchClasses: [4, 7, 11] },
  { name: 'F', pitchClasses: [0, 5, 9] },
  { name: 'G', pitchClasses: [2, 7, 11] },
  { name: 'Am', pitchClasses: [0, 4, 9] },
  { name: 'G7', pitchClasses: [2, 5, 7, 11] }
];

// Lyres
writeConfigFile(targetRoots, path.join('Games', genshinDir, 'Instruments', 'Lyres', 'WindsongLyre.json'), {
  id: 'windsong-lyre',
  name: 'Windsong Lyre',
  category: 'Lyres',
  type: 'diatonic21',
  notes: diatonic21Notes,
  supportedLayouts: genshinStandardLayouts
});

writeConfigFile(targetRoots, path.join('Games', genshinDir, 'Instruments', 'Lyres', 'VintageLyre.json'), {
  id: 'vintage-lyre',
  name: 'Vintage Lyre',
  category: 'Lyres',
  type: 'diatonic21',
  notes: vintageLyreNotes,
  supportedLayouts: genshinStandardLayouts
});

// Zithers
writeConfigFile(targetRoots, path.join('Games', genshinDir, 'Instruments', 'Zithers', 'FloralZither.json'), {
  id: 'floral-zither',
  name: 'Floral Zither',
  category: 'Zithers',
  type: 'diatonic21',
  notes: diatonic21Notes,
  supportedLayouts: genshinStandardLayouts
});

writeConfigFile(targetRoots, path.join('Games', genshinDir, 'Instruments', 'Zithers', 'OldZither.json'), {
  id: 'old-zither',
  name: 'Old Zither',
  category: 'Zithers',
  type: 'diatonic21',
  notes: diatonic21Notes,
  supportedLayouts: genshinStandardLayouts
});

// Pianos / Keyboards
writeConfigFile(targetRoots, path.join('Games', genshinDir, 'Instruments', 'Pianos', 'LeapingSpiritPiano.json'), {
  id: 'leaping-spirit-piano',
  name: 'Leaping Spirit Piano',
  category: 'Pianos',
  type: 'diatonic21',
  notes: diatonic21Notes,
  supportedLayouts: genshinStandardLayouts
});

writeConfigFile(targetRoots, path.join('Games', genshinDir, 'Instruments', 'Pianos', 'HarmonicKey.json'), {
  id: 'harmonic-key',
  name: 'Harmonic Key',
  category: 'Pianos',
  type: 'diatonic21',
  notes: diatonic21Notes,
  supportedLayouts: genshinStandardLayouts
});

// Horns
writeConfigFile(targetRoots, path.join('Games', genshinDir, 'Instruments', 'Horns', 'NightwindHorn.json'), {
  id: 'nightwind-horn',
  name: 'Nightwind Horn',
  category: 'Horns',
  type: 'diatonic14',
  notes: twoOctaveNotes,
  supportedLayouts: ['Genshin_QWERTY_Two_Octaves']
});

// Harps / Chord instruments
writeConfigFile(targetRoots, path.join('Games', genshinDir, 'Instruments', 'Harps', 'LingeringEuphonia.json'), {
  id: 'lingering-euphonia',
  name: 'Lingering Euphonia',
  category: 'Harps',
  type: 'diatonic14',
  notes: twoOctaveNotes,
  supportedLayouts: ['Genshin_QWERTY_Melody'],
  chordPads: genshinChordPads
});

// Strings
writeConfigFile(targetRoots, path.join('Games', genshinDir, 'Instruments', 'Strings', 'Ukulele.json'), {
  id: 'ukulele',
  name: 'Ukulele',
  category: 'Strings',
  type: 'diatonic14',
  notes: twoOctaveNotes,
  supportedLayouts: ['Genshin_QWERTY_Melody'],
  chordPads: genshinChordPads
});

// Percussion
writeConfigFile(targetRoots, path.join('Games', genshinDir, 'Instruments', 'Percussion', 'DjemDjemDrum.json'), {
  id: 'djem-djem-drum',
  name: 'Djem Djem Drum',
  category: 'Percussion',
  type: 'drum8',
  notes: [[60, 62, 64, 65, 67, 69, 71, 72]],
  supportedLayouts: ['Genshin_QWERTY_Drums']
});

writeConfigFile(targetRoots, path.join('Games', genshinDir, 'Instruments', 'Percussion', 'DunDun.json'), {
  id: 'dun-dun',
  name: 'Dun Dun',
  category: 'Percussion',
  type: 'drum8',
  notes: [[60, 62, 64, 65, 67, 69, 71, 72]],
  supportedLayouts: ['Genshin_QWERTY_Drums']
});


// ── 2.2 Sky: Children of the Light ──────────────────
const skyDir = 'Sky';
writeConfigFile(targetRoots, path.join('Games', skyDir, 'game.json'), {
  id: 'Sky',
  displayName: 'Sky: Children of the Light',
  shortName: 'Sky',
  processNames: ['Sky'],
  icon: 'icon.png',
  defaultInstrument: 'Harp',
  defaultLayout: 'Sky_QWERTY_15'
});
copyImageFile(targetRoots, 'Sky.png', skyDir);

const skyNotesC1C3 = [
  [24, 26, 28, 29, 31],
  [33, 35, 36, 38, 40],
  [41, 43, 45, 47, 48]
];

const skyNotesC2C4 = [
  [36, 38, 40, 41, 43],
  [45, 47, 48, 50, 52],
  [53, 55, 57, 59, 60]
];

const skyNotesC3C5 = [
  [48, 50, 52, 53, 55],
  [57, 59, 60, 62, 64],
  [65, 67, 69, 71, 72]
];

const skyNotesC4C6 = [
  [60, 62, 64, 65, 67],
  [69, 71, 72, 74, 76],
  [77, 79, 81, 83, 84]
];

const skyNotesC5C7 = [
  [72, 74, 76, 77, 79],
  [81, 83, 84, 86, 88],
  [89, 91, 93, 95, 96]
];

const skyNotesDrum = [
  [60, 62, 64, 65],
  [67, 69, 71, 72]
];

const skyNotesBell = [
  [60, 62, 67, 69],
  [72, 74, 79, 81]
];

const skyNotesHandpan = [
  [50, 57, 60, 62],
  [65, 67, 69, 72]
];

// Sky Bass Instruments
const skyBass = [
  { file: 'Contrabass.json', id: 'sky-contrabass', name: 'Contrabass', notes: skyNotesC1C3 },
  { file: 'Cello.json', id: 'sky-cello', name: 'Cello 𝄐', notes: skyNotesC2C4 },
  { file: 'DuetsCello.json', id: 'sky-duets-cello', name: 'Duets Cello', notes: skyNotesC2C4 }
];
for (const inst of skyBass) {
  writeConfigFile(targetRoots, path.join('Games', skyDir, 'Instruments', 'Bass', inst.file), {
    id: inst.id,
    name: inst.name,
    category: 'Bass',
    type: 'diatonic15',
    notes: inst.notes,
    supportedLayouts: ['Sky_QWERTY_15']
  });
}

// Sky Melody Instruments
const skyMelody = [
  { file: 'Harp.json', id: 'sky-harp', name: 'Harp', notes: skyNotesC3C5 },
  { file: 'FledglingHarp.json', id: 'sky-fledgling-harp', name: 'Fledgling Harp', notes: skyNotesC3C5 },
  { file: 'Guitar.json', id: 'sky-guitar', name: 'Guitar', notes: skyNotesC3C5 },
  { file: 'RhythmGuitar.json', id: 'sky-rhythm-guitar', name: 'Rhythm Guitar', notes: skyNotesC3C5 },
  { file: 'Ukulele.json', id: 'sky-ukulele', name: 'Ukulele', notes: skyNotesC3C5 },
  { file: 'Lute.json', id: 'sky-lute', name: 'Lute', notes: skyNotesC3C5 },
  { file: 'ElectricGuitar.json', id: 'sky-electric-guitar', name: 'Electric Guitar 𝄐', notes: skyNotesC3C5 },
  { file: 'BlueElectricGuitar.json', id: 'sky-blue-electric-guitar', name: 'Blue Electric Guitar', notes: skyNotesC3C5 },
  { file: 'Piano.json', id: 'sky-piano', name: 'Piano', notes: skyNotesC4C6 },
  { file: 'GrandPiano.json', id: 'sky-grand-piano', name: 'Grand Piano', notes: skyNotesC4C6 },
  { file: 'DuetsGrandPiano.json', id: 'sky-duets-grand-piano', name: 'Duets Grand Piano', notes: skyNotesC4C6 },
  { file: 'FledglingUprightPiano.json', id: 'sky-fledgling-upright-piano', name: 'Fledgling Upright Piano', notes: skyNotesC4C6 },
  { file: 'Kalimba.json', id: 'sky-kalimba', name: 'Kalimba', notes: skyNotesC4C6 },
  { file: 'TriumphViolin.json', id: 'sky-triumph-violin', name: 'Triumph Violin 𝄐', notes: skyNotesC4C6 },
  { file: 'VoiceOfAURORA.json', id: 'sky-voice-of-aurora', name: 'Voice of AURORA 𝄐', notes: skyNotesC4C6 },
  { file: 'Xylophone.json', id: 'sky-xylophone', name: 'Xylophone', notes: skyNotesC5C7 },
  { file: 'WinterPiano.json', id: 'sky-winter-piano', name: 'Winter Piano', notes: skyNotesC5C7 }
];
for (const inst of skyMelody) {
  writeConfigFile(targetRoots, path.join('Games', skyDir, 'Instruments', 'Melody', inst.file), {
    id: inst.id,
    name: inst.name,
    category: 'Melody',
    type: 'diatonic15',
    notes: inst.notes,
    supportedLayouts: ['Sky_QWERTY_15']
  });
}

// Sky Percussion Instruments
const skyPercussion = [
  { file: 'Drum.json', id: 'sky-drum', name: 'Drum', notes: skyNotesDrum },
  { file: 'ProphecyDrum.json', id: 'sky-prophecy-drum', name: 'Prophecy Drum', notes: skyNotesDrum },
  { file: 'FortuneDrum.json', id: 'sky-fortune-drum', name: 'Fortune Drum', notes: skyNotesDrum },
  { file: 'Cymbals.json', id: 'sky-cymbals', name: 'Cymbals', notes: skyNotesDrum },
  { file: 'SmallBell.json', id: 'sky-small-bell', name: 'Small Bell', notes: skyNotesBell },
  { file: 'LargeBell.json', id: 'sky-large-bell', name: 'Large Bell', notes: skyNotesBell },
  { file: 'SanctuaryHandpan.json', id: 'sky-sanctuary-handpan', name: 'Sanctuary Handpan', notes: skyNotesHandpan },
  { file: 'TriumphHandpan.json', id: 'sky-triumph-handpan', name: 'Triumph Handpan', notes: skyNotesHandpan }
];
for (const inst of skyPercussion) {
  writeConfigFile(targetRoots, path.join('Games', skyDir, 'Instruments', 'Percussion', inst.file), {
    id: inst.id,
    name: inst.name,
    category: 'Percussion',
    type: 'drum8',
    notes: inst.notes,
    supportedLayouts: ['Sky_QWERTY_8']
  });
}

// Sky Wind Instruments
const skyWinds = [
  { file: 'Horn.json', id: 'sky-horn', name: 'Horn', notes: skyNotesC2C4 },
  { file: 'TriumphSaxophone.json', id: 'sky-triumph-saxophone', name: 'Triumph Saxophone 𝄐', notes: skyNotesC3C5 },
  { file: 'Flute.json', id: 'sky-flute', name: 'Flute', notes: skyNotesC4C6 },
  { file: 'Panflute.json', id: 'sky-panflute', name: 'Panflute', notes: skyNotesC4C6 },
  { file: 'Bugle.json', id: 'sky-bugle', name: 'Bugle', notes: skyNotesC4C6 },
  { file: 'MantaOcarina.json', id: 'sky-manta-ocarina', name: 'Manta Ocarina', notes: skyNotesC4C6 },
  { file: 'Harmonica.json', id: 'sky-harmonica', name: 'Harmonica 𝄐', notes: skyNotesC4C6 },
  { file: 'VesselFlute.json', id: 'sky-vessel-flute', name: 'Vessel Flute', notes: skyNotesC4C6 }
];
for (const inst of skyWinds) {
  writeConfigFile(targetRoots, path.join('Games', skyDir, 'Instruments', 'Winds', inst.file), {
    id: inst.id,
    name: inst.name,
    category: 'Winds',
    type: 'diatonic15',
    notes: inst.notes,
    supportedLayouts: ['Sky_QWERTY_15']
  });
}


// ── 2.3 Wuthering Waves ─────────────────────────────
const wuwaDir = 'Wuthering Waves';
writeConfigFile(targetRoots, path.join('Games', wuwaDir, 'game.json'), {
  id: 'Wuthering Waves',
  displayName: 'Wuthering Waves',
  shortName: 'WuWa',
  processNames: ['Client-Win64-Shipping', 'Wuthering Waves'],
  icon: 'icon.png',
  defaultInstrument: '21-Key Instrument',
  defaultLayout: 'WutheringWaves_QWERTY'
});
copyImageFile(targetRoots, 'Wuthering_Waves.png', wuwaDir);

writeConfigFile(targetRoots, path.join('Games', wuwaDir, 'Instruments', 'Instruments', 'Instrument21Key.json'), {
  id: 'wuwa-instrument-21key',
  name: '21-Key Instrument',
  category: 'Instruments',
  type: 'diatonic21',
  notes: diatonic21Notes,
  supportedLayouts: ['WutheringWaves_QWERTY']
});


// ── 2.4 Roblox ──────────────────────────────────────
const robloxDir = 'Roblox';
writeConfigFile(targetRoots, path.join('Games', robloxDir, 'game.json'), {
  id: 'Roblox',
  displayName: 'Roblox',
  shortName: 'Roblox',
  processNames: ['Roblox Game Client'],
  windowNames: ['Roblox'],
  icon: 'icon.png',
  defaultInstrument: 'Piano (61 Key)',
  defaultLayout: 'Roblox_QWERTY_61Key'
});
copyImageFile(targetRoots, 'Roblox.png', robloxDir);

const chromatic61Notes = [
  [36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 46, 47], // C2-B2
  [48, 49, 50, 51, 52, 53, 54, 55, 56, 57, 58, 59], // C3-B3
  [60, 61, 62, 63, 64, 65, 66, 67, 68, 69, 70, 71], // C4-B4
  [72, 73, 74, 75, 76, 77, 78, 79, 80, 81, 82, 83], // C5-B5
  [84, 85, 86, 87, 88, 89, 90, 91, 92, 93, 94, 95], // C6-B6
  [96]                                             // C7
];

const chromatic88Notes = [
  [21, 22, 23],                                             // A0-B0
  [24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35],         // C1-B1
  [36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 46, 47],         // C2-B2
  [48, 49, 50, 51, 52, 53, 54, 55, 56, 57, 58, 59],         // C3-B3
  [60, 61, 62, 63, 64, 65, 66, 67, 68, 69, 70, 71],         // C4-B4
  [72, 73, 74, 75, 76, 77, 78, 79, 80, 81, 82, 83],         // C5-B5
  [84, 85, 86, 87, 88, 89, 90, 91, 92, 93, 94, 95],         // C6-B6
  [96, 97, 98, 99, 100, 101, 102, 103, 104, 105, 106, 107], // C7-B7
  [108]                                                     // C8
];

writeConfigFile(targetRoots, path.join('Games', robloxDir, 'Instruments', 'Pianos', 'Piano61Key.json'), {
  id: 'roblox-piano-61k',
  name: 'Piano (61 Key)',
  category: 'Pianos',
  type: 'piano61',
  notes: chromatic61Notes,
  supportedLayouts: ['Roblox_QWERTY_61Key'],
  supportsPedals: true
});

writeConfigFile(targetRoots, path.join('Games', robloxDir, 'Instruments', 'Pianos', 'Piano88Key.json'), {
  id: 'roblox-piano-88k',
  name: 'Piano (88 Key)',
  category: 'Pianos',
  type: 'piano88',
  notes: chromatic88Notes,
  supportedLayouts: ['Roblox_QWERTY_88Key'],
  supportsPedals: true
});


// ── 2.5 Heartopia ───────────────────────────────────
const heartopiaDir = 'Heartopia';
writeConfigFile(targetRoots, path.join('Games', heartopiaDir, 'game.json'), {
  id: 'Heartopia',
  displayName: 'Heartopia',
  shortName: 'Heartopia',
  processNames: ['xdt'],
  icon: 'icon.png',
  defaultInstrument: 'Piano (22 Key)',
  defaultLayout: 'Heartopia_QWERTY_22Key'
});
copyImageFile(targetRoots, 'Heartopia.png', heartopiaDir);

const heartopia2RowNotes = [
  [72, 74, 76, 77, 79, 81, 83, 84], // C5 D5 E5 F5 G5 A5 B5 C6
  [60, 62, 64, 65, 67, 69, 71]      // C4 D4 E4 F4 G4 A4 B4
];

const heartopia3RowNotes = [
  [60, 62, 64, 65, 67], // C4 D4 E4 F4 G4
  [69, 71, 72, 74, 76], // A4 B4 C5 D5 E5
  [77, 79, 81, 83, 84]  // F5 G5 A5 B5 C6
];

const heartopia22Notes = [
  [72, 74, 76, 77, 79, 81, 83, 84], // C5 D5 E5 F5 G5 A5 B5 C6
  [60, 62, 64, 65, 67, 69, 71],     // C4 D4 E4 F4 G4 A4 B4
  [48, 50, 52, 53, 55, 57, 59]      // C3 D3 E3 F3 G3 A3 B3
];

const heartopia37Notes = [
  [72, 73, 74, 75, 76, 77, 78, 79, 80, 81, 82, 83, 84], // C5-C6 chromatic
  [60, 61, 62, 63, 64, 65, 66, 67, 68, 69, 70, 71],     // C4-B4 chromatic
  [48, 49, 50, 51, 52, 53, 54, 55, 56, 57, 58, 59]      // C3-B3 chromatic
];

const heartopiaInstruments = [
  { name: 'Bagpipe', category: 'Winds' },
  { name: 'Bamboo Xiao', category: 'Winds' },
  { name: 'Cajon', category: 'Percussion' },
  { name: 'Cello', category: 'Strings' },
  { name: 'Concertina', category: 'Winds' },
  { name: 'Conga', category: 'Percussion' },
  { name: 'Lute', category: 'Strings' },
  { name: 'Lyre', category: 'Strings' },
  { name: 'Mbira', category: 'Percussion' },
  { name: 'Piano', category: 'Pianos' },
  { name: 'Recorder', category: 'Winds' },
  { name: 'Violin', category: 'Strings' },
  { name: 'Wooden Bass', category: 'Strings' }
];

for (const item of heartopiaInstruments) {
  const safeFile = item.name.replace(/\s+/g, '');
  // 2 Row variant
  writeConfigFile(targetRoots, path.join('Games', heartopiaDir, 'Instruments', item.category, `${safeFile}2Row.json`), {
    id: `heartopia-${item.name.toLowerCase().replace(/\s+/g, '-')}-2r`,
    name: `${item.name} (2 Row)`,
    category: item.category,
    type: 'diatonic15',
    notes: heartopia2RowNotes,
    supportedLayouts: ['Heartopia_QWERTY_2Row']
  });

  // 3 Row variant
  writeConfigFile(targetRoots, path.join('Games', heartopiaDir, 'Instruments', item.category, `${safeFile}3Row.json`), {
    id: `heartopia-${item.name.toLowerCase().replace(/\s+/g, '-')}-3r`,
    name: `${item.name} (3 Row)`,
    category: item.category,
    type: 'diatonic15',
    notes: heartopia3RowNotes,
    supportedLayouts: ['Heartopia_QWERTY_3Row']
  });
}

// Special Piano 22k and 37k
writeConfigFile(targetRoots, path.join('Games', heartopiaDir, 'Instruments', 'Pianos', 'Piano22Key.json'), {
  id: 'heartopia-piano-22k',
  name: 'Piano (22 Key)',
  category: 'Pianos',
  type: 'diatonic22',
  notes: heartopia22Notes,
  supportedLayouts: ['Heartopia_QWERTY_22Key']
});

writeConfigFile(targetRoots, path.join('Games', heartopiaDir, 'Instruments', 'Pianos', 'Piano37Key.json'), {
  id: 'heartopia-piano-37k',
  name: 'Piano (37 Key)',
  category: 'Pianos',
  type: 'chromatic37',
  notes: heartopia37Notes,
  supportedLayouts: ['Heartopia_QWERTY_37Key']
});


// ── 2.6 Core Keeper ─────────────────────────────────
const coreKeeperDir = 'Core Keeper';
writeConfigFile(targetRoots, path.join('Games', coreKeeperDir, 'game.json'), {
  id: 'Core Keeper',
  displayName: 'Core Keeper',
  shortName: 'Core Keeper',
  processNames: ['CoreKeeper'],
  icon: 'icon.png',
  defaultInstrument: 'Pocket Piano',
  defaultLayout: 'CoreKeeper_QWERTY'
});
copyImageFile(targetRoots, 'Core_Keeper.png', coreKeeperDir);

const coreKeeperInstruments = [
  {
    file: 'PocketPiano.json',
    category: 'Keyboard',
    id: 'corekeeper-pocket-piano',
    name: 'Pocket Piano',
    notes: [
      [60, 61, 62, 63, 64, 65, 66, 67, 68, 69, 70, 71],
      [48, 49, 50, 51, 52, 53, 54, 55, 56, 57, 58, 59]
    ]
  },
  {
    file: 'Cello.json',
    category: 'Strings',
    id: 'corekeeper-cello',
    name: 'Cello',
    notes: [
      [48, 49, 50, 51, 52, 53, 54, 55, 56, 57, 58, 59],
      [36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 46, 47]
    ]
  },
  {
    file: 'Drumkit.json',
    category: 'Percussion',
    id: 'corekeeper-drumkit',
    name: 'Drumkit',
    notes: [
      [48, 49, 50, 51, 52, 53, 54, 55, 56, 57, 58, 59],
      [36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 46, 47]
    ]
  },
  {
    file: 'Flute.json',
    category: 'Winds',
    id: 'corekeeper-flute',
    name: 'Flute',
    notes: [
      [72, 73, 74, 75, 76, 77, 78, 79, 80, 81, 82, 83],
      [60, 61, 62, 63, 64, 65, 66, 67, 68, 69, 70, 71]
    ]
  },
  {
    file: 'Harp.json',
    category: 'Strings',
    id: 'corekeeper-harp',
    name: 'Harp',
    notes: [
      [60, 61, 62, 63, 64, 65, 66, 67, 68, 69, 70, 71],
      [48, 49, 50, 51, 52, 53, 54, 55, 56, 57, 58, 59]
    ]
  },
  {
    file: 'Ocarina.json',
    category: 'Winds',
    id: 'corekeeper-ocarina',
    name: 'Ocarina',
    notes: [
      [84, 85, 86, 87, 88, 89, 90, 91, 92, 93, 94, 95],
      [72, 73, 74, 75, 76, 77, 78, 79, 80, 81, 82, 83]
    ]
  }
];

for (const inst of coreKeeperInstruments) {
  writeConfigFile(targetRoots, path.join('Games', coreKeeperDir, 'Instruments', inst.category, inst.file), {
    id: inst.id,
    name: inst.name,
    category: inst.category,
    type: 'chromatic24',
    notes: inst.notes,
    supportedLayouts: ['CoreKeeper_QWERTY']
  });
}


// ── 2.7 Harry Potter: Magic Awakened ────────────────
const hpmaDir = 'Harry Potter Magic Awakened';
writeConfigFile(targetRoots, path.join('Games', hpmaDir, 'game.json'), {
  id: 'HPMA',
  displayName: 'Harry Potter: Magic Awakened',
  shortName: 'HPMA',
  processNames: ['launcher'],
  windowNames: ['Harry Potter: Magic Awakened'],
  icon: 'icon.png',
  defaultInstrument: 'Piano',
  defaultLayout: 'HPMA_QWERTY_14Key'
});
copyImageFile(targetRoots, 'HPMA.png', hpmaDir);

writeConfigFile(targetRoots, path.join('Games', hpmaDir, 'Instruments', 'Pianos', 'Piano14Key.json'), {
  id: 'hpma-piano-14k',
  name: 'Piano',
  category: 'Pianos',
  type: 'diatonic14',
  notes: [
    [60, 62, 64, 65, 67, 69, 71], // C4-B4
    [48, 50, 52, 53, 55, 57, 59]  // C3-B3
  ],
  supportedLayouts: ['HPMA_QWERTY_14Key']
});

writeConfigFile(targetRoots, path.join('Games', hpmaDir, 'Instruments', 'Pianos', 'Piano36Key.json'), {
  id: 'hpma-piano-36k',
  name: 'Piano (Professional)',
  category: 'Pianos',
  type: 'chromatic36',
  notes: [
    [72, 73, 74, 75, 76, 77, 78, 79, 80, 81, 82, 83], // C5-B5
    [60, 61, 62, 63, 64, 65, 66, 67, 68, 69, 70, 71], // C4-B4
    [48, 49, 50, 51, 52, 53, 54, 55, 56, 57, 58, 59]  // C3-B3
  ],
  supportedLayouts: ['HPMA_QWERTY_36Key']
});


// ── 2.8 Neverness to Everness ───────────────────────
const nteDir = 'Neverness to Everness';
writeConfigFile(targetRoots, path.join('Games', nteDir, 'game.json'), {
  id: 'NTE',
  displayName: 'Neverness to Everness',
  shortName: 'NTE',
  processNames: ['HTGame'],
  icon: 'icon.png',
  defaultInstrument: 'Piano (21 Key)',
  defaultLayout: 'NTE_QWERTY_21Keys'
});
copyImageFile(targetRoots, 'NTE.png', nteDir);

writeConfigFile(targetRoots, path.join('Games', nteDir, 'Instruments', 'Pianos', 'Piano21Key.json'), {
  id: 'nte-piano-21k',
  name: 'Piano (21 Key)',
  category: 'Pianos',
  type: 'diatonic21',
  notes: [
    [60, 62, 64, 65, 67, 69, 71], // C4-B4
    [48, 50, 52, 53, 55, 57, 59], // C3-B3
    [36, 38, 40, 41, 43, 45, 47]  // C2-B2
  ],
  supportedLayouts: ['NTE_QWERTY_21Keys']
});

writeConfigFile(targetRoots, path.join('Games', nteDir, 'Instruments', 'Pianos', 'Piano36Key.json'), {
  id: 'nte-piano-36k',
  name: 'Piano (36 Key)',
  category: 'Pianos',
  type: 'chromatic36',
  notes: [
    [84, 85, 86, 87, 88, 89, 90, 91, 92, 93, 94, 95], // C6-B6
    [72, 73, 74, 75, 76, 77, 78, 79, 80, 81, 82, 83], // C5-B5
    [60, 61, 62, 63, 64, 65, 66, 67, 68, 69, 70, 71]  // C4-B4
  ],
  supportedLayouts: ['NTE_QWERTY_36Keys']
});


// ── 2.9 Where Winds Meet ────────────────────────────
const wwmDir = 'Where Winds Meet';
writeConfigFile(targetRoots, path.join('Games', wwmDir, 'game.json'), {
  id: 'WWM',
  displayName: 'Where Winds Meet',
  shortName: 'WWM',
  processNames: ['WhereWindsMeet', 'Where Winds Meet', 'yysls', 'yysls_client', 'wwm'],
  icon: 'icon.png',
  defaultInstrument: 'Guqin',
  defaultLayout: 'WWM_QWERTY'
});
copyImageFile(targetRoots, 'WWM.png', wwmDir);

writeConfigFile(targetRoots, path.join('Games', wwmDir, 'Instruments', 'Zithers', 'Guqin.json'), {
  id: 'wwm-guqin',
  name: 'Guqin',
  category: 'Zithers',
  type: 'chromatic36',
  notes: [
    [60, 61, 62, 63, 64, 65, 66, 67, 68, 69, 70, 71], // C4-B4
    [48, 49, 50, 51, 52, 53, 54, 55, 56, 57, 58, 59], // C3-B3
    [36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 46, 47]  // C2-B2
  ],
  supportedLayouts: ['WWM_QWERTY']
});


// ── 2.10 Blue Protocol: Star Resonance ──────────────
const bpsrDir = 'Blue Protocol Star Resonance';
writeConfigFile(targetRoots, path.join('Games', bpsrDir, 'game.json'), {
  id: 'BPSR',
  displayName: 'Blue Protocol: Star Resonance',
  shortName: 'BPSR',
  processNames: ['BPSR', 'StarSEA_STEAM'],
  icon: 'icon.png',
  defaultInstrument: 'Piano (61 Key)',
  defaultLayout: 'BPSR_QWERTY_61Key'
});
copyImageFile(targetRoots, 'BPSR.png', bpsrDir);

writeConfigFile(targetRoots, path.join('Games', bpsrDir, 'Instruments', 'Pianos', 'Piano61Key.json'), {
  id: 'bpsr-piano-61k',
  name: 'Piano (61 Key)',
  category: 'Pianos',
  type: 'piano61',
  notes: chromatic61Notes,
  supportedLayouts: ['BPSR_QWERTY_61Key'],
  supportsPedals: true
});

writeConfigFile(targetRoots, path.join('Games', bpsrDir, 'Instruments', 'Pianos', 'Piano88Key.json'), {
  id: 'bpsr-piano-88k',
  name: 'Piano (88 Key)',
  category: 'Pianos',
  type: 'piano88',
  notes: chromatic88Notes,
  supportedLayouts: ['BPSR_QWERTY_88Key'],
  supportsPedals: true
});

console.log('Defaults generation completed successfully across all target directories!');
