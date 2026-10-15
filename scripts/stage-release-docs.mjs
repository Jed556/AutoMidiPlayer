import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { documents } from './release-lib.mjs';

// Bundle the same source documents used by the frontend.
await mkdir('src-tauri/resources/docs', { recursive: true });
for (const name of documents) {
  await writeFile(`src-tauri/resources/docs/${name}`, await readFile(name));
}
