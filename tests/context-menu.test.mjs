import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { compile } from 'svelte/compiler';
import {
  computeContextMenuPosition,
  computeSubmenuPosition,
  handleAppContextMenu,
  shouldAllowNativeContextMenu,
} from '../src/renderer/lib/utils/contextMenu.ts';

function makeElement({ tagName = 'DIV', type = '', classes = [], attrs = {}, isContentEditable = false, parent = null } = {}) {
  const el = {
    tagName,
    type,
    isContentEditable,
    parentElement: parent,
    getAttribute(name) {
      return name in attrs ? attrs[name] : null;
    },
    closest(selector) {
      const tokens = selector.split(',').map(s => s.trim());
      let cur = this;
      while (cur) {
        for (const token of tokens) {
          if (token.startsWith('.')) {
            if (cur.classes?.includes(token.slice(1))) return cur;
          } else if (token === '[contenteditable]') {
            if (cur.attrs && 'contenteditable' in cur.attrs) return cur;
          } else if (cur.tagName?.toUpperCase() === token.toUpperCase()) {
            return cur;
          }
        }
        cur = cur.parent;
      }
      return null;
    },
    classes,
    attrs,
    parent,
  };
  return el;
}

test('suppresses native context menu inside the left navigation drawer, including its search input', () => {
  const navDrawer = makeElement({ tagName: 'ASIDE', classes: ['nav-drawer'] });
  const navButton = makeElement({ tagName: 'BUTTON', parent: navDrawer });
  const navSearchInput = makeElement({ tagName: 'INPUT', type: 'text', parent: navDrawer });

  assert.equal(shouldAllowNativeContextMenu(navDrawer), false);
  assert.equal(shouldAllowNativeContextMenu(navButton), false);
  assert.equal(shouldAllowNativeContextMenu(navSearchInput), false);

  let prevented = 0;
  handleAppContextMenu({ defaultPrevented: false, target: navButton, preventDefault: () => { prevented++; } });
  handleAppContextMenu({ defaultPrevented: false, target: navSearchInput, preventDefault: () => { prevented++; } });
  assert.equal(prevented, 2);
});

test('suppresses native context menu on non-editable UI controls and non-text inputs', () => {
  const button = makeElement({ tagName: 'BUTTON' });
  const checkbox = makeElement({ tagName: 'INPUT', type: 'checkbox' });
  const slider = makeElement({ tagName: 'INPUT', type: 'range' });
  const disabledEditable = makeElement({ tagName: 'DIV', attrs: { contenteditable: 'false' }, isContentEditable: false });

  for (const target of [null, button, checkbox, slider, disabledEditable]) {
    let prevented = false;
    handleAppContextMenu({ defaultPrevented: false, target, preventDefault: () => { prevented = true; } });
    assert.equal(prevented, true);
  }
});

test('allows native context menu on editable text controls outside the navigation drawer', () => {
  const textInput = makeElement({ tagName: 'INPUT', type: 'text' });
  const textarea = makeElement({ tagName: 'TEXTAREA' });
  const textNodeInTextarea = { parentElement: textarea };
  const contentEditable = makeElement({ tagName: 'DIV', attrs: { contenteditable: 'true' }, isContentEditable: true });

  for (const target of [textInput, textarea, textNodeInTextarea, contentEditable]) {
    let prevented = false;
    handleAppContextMenu({ defaultPrevented: false, target, preventDefault: () => { prevented = true; } });
    assert.equal(prevented, false);
  }
});

test('skips calling preventDefault again when a custom context menu already handled the event', () => {
  let prevented = false;
  handleAppContextMenu({
    defaultPrevented: true,
    target: makeElement({ tagName: 'DIV' }),
    preventDefault: () => { prevented = true; },
  });
  assert.equal(prevented, false);
});

test('NavigationDrawer and App wire up context menu suppression and compile cleanly', () => {
  const navSource = readFileSync(new URL('../src/renderer/lib/components/Layout/NavigationDrawer.svelte', import.meta.url), 'utf8');
  const appSource = readFileSync(new URL('../src/renderer/App.svelte', import.meta.url), 'utf8');

  assert.match(navSource, /oncontextmenu=\{\(e\) => e\.preventDefault\(\)\}/);
  assert.match(appSource, /window\.addEventListener\('contextmenu', handleAppContextMenu\)/);
  assert.match(appSource, /window\.removeEventListener\('contextmenu', handleAppContextMenu\)/);

  const navCompiled = compile(navSource, { filename: 'NavigationDrawer.svelte', generate: 'client' });
  const appCompiled = compile(appSource, { filename: 'App.svelte', generate: 'client' });
  assert.ok(navCompiled.js.code.length > 0);
  assert.ok(appCompiled.js.code.length > 0);
});

test('computeContextMenuPosition anchors at cursor and flips or clamps near viewport edges', () => {
  const menuSize = { width: 200, height: 260 };
  const viewport = { width: 1280, height: 720 };

  assert.deepEqual(
    computeContextMenuPosition({ x: 300, y: 200 }, menuSize, viewport),
    { left: 300, top: 200, originX: 0, originY: 0 },
  );

  assert.deepEqual(
    computeContextMenuPosition({ x: 1200, y: 200 }, menuSize, viewport),
    { left: 1000, top: 200, originX: 200, originY: 0 },
  );

  assert.deepEqual(
    computeContextMenuPosition({ x: 300, y: 650 }, menuSize, viewport),
    { left: 300, top: 390, originX: 0, originY: 260 },
  );

  assert.deepEqual(
    computeContextMenuPosition({ x: 1240, y: 680 }, menuSize, viewport),
    { left: 1040, top: 420, originX: 200, originY: 260 },
  );

  const clamped = computeContextMenuPosition({ x: 4, y: 3 }, menuSize, { width: 180, height: 220 });
  assert.deepEqual(clamped, { left: 8, top: 8, originX: 0, originY: 0 });
});

test('SongListTable and SongListRow animate context menus and wire Liked Songs actions cleanly', () => {
  const tableSource = readFileSync(new URL('../src/renderer/lib/components/Common/SongListTable.svelte', import.meta.url), 'utf8');
  const rowSource = readFileSync(new URL('../src/renderer/lib/components/Common/SongListRow.svelte', import.meta.url), 'utf8');

  assert.match(tableSource, /import \{ portal \} from '\.\.\/\.\.\/actions\/portal'/);
  assert.match(tableSource, /import \{ createFrameResizeObserver \} from '\.\.\/\.\.\/utils\/frameResizeObserver'/);
  assert.match(tableSource, /portal\(node, document\.body\)/);
  assert.match(tableSource, /introend/);
  assert.match(tableSource, /CustomScrollViewer/);
  assert.match(tableSource, /is-selected/);
  assert.match(tableSource, /z-index: 2147483646/);
  assert.match(tableSource, /\{#each floatingMenu \? \[floatingMenu\] : \[\] as menu \(menu\.key\)\}/);
  assert.match(tableSource, /use:positionContextMenu=\{menu\.pos\}/);
  assert.match(tableSource, /in:contextMenuIn/);
  assert.match(tableSource, /out:contextMenuOut/);
  assert.match(tableSource, /handleToggleLikedSongsAction/);
  assert.match(tableSource, /LIKED_SONGS_PLAYLIST_ID/);

  assert.match(rowSource, /import \{ portal \} from '\.\.\/\.\.\/actions\/portal'/);
  assert.match(rowSource, /portal\(node, document\.body\)/);
  assert.match(rowSource, /z-index: 2147483646/);
  assert.match(rowSource, /use:positionRowMenu/);
  assert.match(rowSource, /in:rowMenuIn/);
  assert.match(rowSource, /out:rowMenuOut/);
  assert.match(rowSource, /ontoggleliked/);
  assert.match(rowSource, /songs\.addToLikedSongs/);
  assert.match(rowSource, /songs\.removeFromLikedSongs/);
  assert.match(rowSource, /songs\.addToPlaylist/);
  assert.match(rowSource, /onremovefromplaylist/);

  const navSource = readFileSync(new URL('../src/renderer/lib/components/Layout/NavigationDrawer.svelte', import.meta.url), 'utf8');
  const songsViewSource = readFileSync(new URL('../src/renderer/lib/views/SongsView.svelte', import.meta.url), 'utf8');
  const dialogSource = readFileSync(new URL('../src/renderer/lib/components/Dialogs/PlaylistDialog.svelte', import.meta.url), 'utf8');

  assert.match(navSource, /playlist-context-menu/);
  assert.match(navSource, /!menu\.playlist\.isBuiltIn/);
  assert.match(songsViewSource, /isCustomPlaylist/);
  assert.match(songsViewSource, /confirmAndDeletePlaylist/);

  const tableCompiled = compile(tableSource, { filename: 'SongListTable.svelte', generate: 'client' });
  const rowCompiled = compile(rowSource, { filename: 'SongListRow.svelte', generate: 'client' });
  const navCompiled = compile(navSource, { filename: 'NavigationDrawer.svelte', generate: 'client' });
  const songsViewCompiled = compile(songsViewSource, { filename: 'SongsView.svelte', generate: 'client' });
  const dialogCompiled = compile(dialogSource, { filename: 'PlaylistDialog.svelte', generate: 'client' });
  assert.ok(tableCompiled.js.code.length > 0);
  assert.ok(rowCompiled.js.code.length > 0);
  assert.ok(navCompiled.js.code.length > 0);
  assert.ok(songsViewCompiled.js.code.length > 0);
  assert.ok(dialogCompiled.js.code.length > 0);
});

test('computeSubmenuPosition opens right when space permits and flips left near right edge', () => {
  const submenuSize = { width: 210, height: 220 };
  const viewport = { width: 1280, height: 720 };

  assert.deepEqual(
    computeSubmenuPosition({ left: 300, right: 500, top: 240, bottom: 274 }, submenuSize, viewport),
    { left: 504, top: 236, originX: 0, originY: 16 },
  );

  assert.deepEqual(
    computeSubmenuPosition({ left: 960, right: 1160, top: 560, bottom: 594 }, submenuSize, viewport),
    { left: 746, top: 492, originX: 210, originY: 80 },
  );
});

test('ActionDialog passes footer snippet directly to ContentDialog and delete playlist hides close button', () => {
  const actionDialogSource = readFileSync(new URL('../src/renderer/lib/components/Dialogs/ActionDialog.svelte', import.meta.url), 'utf8');
  const playlistActionsSource = readFileSync(new URL('../src/renderer/lib/utils/playlistActions.ts', import.meta.url), 'utf8');

  assert.doesNotMatch(actionDialogSource, /\{#if hasButtons\(\)\}\s*\{#snippet footer\(\)\}/);
  assert.match(actionDialogSource, /hideFooter=\{!hasButtons\(\)\}/);

  const actionCompiled = compile(actionDialogSource, { filename: 'ActionDialog.svelte', generate: 'client' });
  assert.match(actionCompiled.js.code, /\bfooter:\s*footer\b|\bfooter,/);

  assert.match(playlistActionsSource, /showCloseButton:\s*false/);
  assert.match(playlistActionsSource, /cancelButton:/);
  assert.match(playlistActionsSource, /confirmButton:/);
});

test('Tauri drag-drop is enabled, wired in App, removed from playbackStore, and playlist-aware in SongsView and NavigationDrawer', () => {
  const tauriConf = JSON.parse(readFileSync(new URL('../src-tauri/tauri.conf.json', import.meta.url), 'utf8'));
  const appSource = readFileSync(new URL('../src/renderer/App.svelte', import.meta.url), 'utf8');
  const playbackSource = readFileSync(new URL('../src/renderer/lib/state/playbackStore.svelte.ts', import.meta.url), 'utf8');
  const navSource = readFileSync(new URL('../src/renderer/lib/components/Layout/NavigationDrawer.svelte', import.meta.url), 'utf8');
  const songsViewSource = readFileSync(new URL('../src/renderer/lib/views/SongsView.svelte', import.meta.url), 'utf8');

  assert.equal(tauriConf.app?.windows?.[0]?.dragDropEnabled, true);
  assert.match(appSource, /onDragDropEvent/);
  assert.match(appSource, /window:file-drop/);
  assert.doesNotMatch(playbackSource, /tauri:\/\/drag-drop/);
  assert.match(navSource, /data-playlist-id=\{playlist\.id\}/);
  assert.match(songsViewSource, /importMidiFilesWithFeedback\(undefined,\s*currentPlaylist\?\.id\)/);

  const appCompiled = compile(appSource, { filename: 'App.svelte', generate: 'client' });
  const navCompiled = compile(navSource, { filename: 'NavigationDrawer.svelte', generate: 'client' });
  const songsViewCompiled = compile(songsViewSource, { filename: 'SongsView.svelte', generate: 'client' });
  assert.ok(appCompiled.js.code.length > 0);
  assert.ok(navCompiled.js.code.length > 0);
  assert.ok(songsViewCompiled.js.code.length > 0);
});
