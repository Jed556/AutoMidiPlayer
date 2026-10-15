import { defineConfig } from 'vite';
import { svelte } from '@sveltejs/vite-plugin-svelte';
import path from 'path';

const rootDir = import.meta.dirname;
const host = process.env.TAURI_DEV_HOST;

export default defineConfig({
  plugins: [
    svelte({
      configFile: path.resolve(rootDir, 'svelte.config.js')
    })
  ],
  clearScreen: false,
  base: './',
  root: path.resolve(rootDir, 'src/renderer'),
  publicDir: path.resolve(rootDir, 'public'),
  resolve: {
    alias: {
      '$lib': path.resolve(rootDir, 'src/renderer/lib'),
      '$shared': path.resolve(rootDir, 'src/shared')
    }
  },
  build: {
    outDir: path.resolve(rootDir, 'dist/renderer'),
    emptyOutDir: true,
    target: ['es2022', 'chrome105'],
    minify: !process.env.TAURI_ENV_DEBUG ? 'esbuild' : false,
    sourcemap: !!process.env.TAURI_ENV_DEBUG
  },
  server: {
    port: 5173,
    strictPort: true,
    host: host || false,
    hmr: host
      ? {
          protocol: 'ws',
          host,
          port: 5183
        }
      : undefined,
    watch: {
      ignored: ['**/src-tauri/**']
    }
  },
  envPrefix: ['VITE_', 'TAURI_ENV_*']
});
