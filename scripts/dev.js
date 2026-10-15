import { spawn } from 'child_process';
import path from 'path';

const userProfile = process.env.USERPROFILE || '';
const localAppData = process.env.LOCALAPPDATA || '';
const nodeDir = path.dirname(process.execPath);

// Ensure Node, Cargo and pnpm are in PATH even if VS Code environment is stale
const extraPaths = [
  nodeDir,
  'C:\\Program Files\\nodejs',
  path.join(userProfile, '.cargo', 'bin'),
  path.join(localAppData, 'pnpm', 'bin')
];

const env = { ...process.env };

// Handle Windows case-insensitive PATH lookup across cmd/powershell/rust
const pathKey = Object.keys(env).find(k => k.toLowerCase() === 'path') || 'PATH';
const existingPath = env[pathKey] || '';
const newPath = `${extraPaths.join(';')};${existingPath}`;

env.PATH = newPath;
env.Path = newPath;

// Remove Node debugger options so child node processes (Vite, pnpm) don't conflict
delete env.NODE_OPTIONS;

console.log('Starting Auto MIDI Player (Tauri Dev)...');

const child = spawn('pnpm', ['dev'], {
  stdio: 'inherit',
  shell: true,
  env
});

child.on('exit', (code) => {
  process.exit(code ?? 0);
});

child.on('error', (err) => {
  console.error('Failed to start Tauri dev:', err);
  process.exit(1);
});
