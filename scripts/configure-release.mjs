import { readFile, writeFile, mkdir } from 'node:fs/promises';

// The Rust updater reads AMP_UPDATER_PUBLIC_KEY, while Tauri's bundler validates
// signing against plugins.updater.pubkey. Both must receive the same key.
const pubkey = process.env.AMP_UPDATER_PUBLIC_KEY?.trim();
if (!pubkey) throw new Error('Missing AMP_UPDATER_PUBLIC_KEY');
const config = JSON.parse(await readFile('src-tauri/tauri.updater.conf.json', 'utf8'));
config.plugins = { updater: { pubkey } };
await mkdir('dist', { recursive: true });
await writeFile('dist/release-config.json', `${JSON.stringify(config, null, 2)}\n`);
