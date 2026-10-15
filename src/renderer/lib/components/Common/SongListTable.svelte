<script lang="ts">
  import { loadUiPreferences, saveUiPreferences } from '../../backend/preferences.js';
  import { onDestroy, onMount, tick } from 'svelte';
  import { playbackStore } from '../../state/playbackStore.svelte';
  import { notificationCenter } from '../../ui/notificationCenter.svelte';
  import { t } from '../../i18n';
  import FluentIcon from './FluentIcon.svelte';
  import CustomScrollViewer from './CustomScrollViewer.svelte';
  import RollingIndex from './RollingIndex.svelte';
  import SongListRow from './SongListRow.svelte';
  import OverflowText from './OverflowText.svelte';
  import type { MidiFile } from '$shared/types/midi';

  interface Props {
    songs: MidiFile[];
    selectedId?: string | null;
    selectedIds?: string[];
    isQueue?: boolean;
    canReorder?: boolean;
    embedInScroll?: boolean;
    scrollContainer?: HTMLElement | null;
    onselect?: (song: MidiFile, selectedSongs?: MidiFile[]) => void;
    onselectionchange?: (selectedIds: string[], selectedSongs: MidiFile[]) => void;
    onplay?: (song: MidiFile, index: number) => void;
    onremove?: (index: number) => void;
    onremoveselected?: (selectedIds: string[], selectedIndices: number[]) => void;
    ondelete?: (ids: string[]) => void;
    onviewinstudio?: (song: MidiFile) => void;
    onedit?: (song: MidiFile) => void;
    onreorder?: (newSongs: MidiFile[]) => void;
  }

  let {
    songs = [],
    selectedId = $bindable(null),
    selectedIds = $bindable([]),
    isQueue = false,
    canReorder = true,
    embedInScroll = false,
    scrollContainer = $bindable(null),
    onselect,
    onselectionchange,
    onplay,
    onremove,
    onremoveselected,
    ondelete,
    onviewinstudio,
    onedit,
    onreorder
  }: Props = $props();

  // Root container ref
  let containerEl = $state<HTMLElement | null>(null);
  let tableBodyEl = $state<HTMLElement | null>(null);
  let internalScrollEl = $state<HTMLElement | null>(null);

  // Active scroll element (either external when embedded or internal)
  let activeScrollEl = $derived(embedInScroll ? scrollContainer : internalScrollEl);

  // ==========================================
  // Virtualization Engine (Spotify-grade)
  // ==========================================
  const ROW_HEIGHT = 56;
  const ROW_GAP = 2;
  const ROW_STEP = ROW_HEIGHT + ROW_GAP; // 58px per row
  const PADDING_TOP = 4;
  const PADDING_BOTTOM = 16;
  const OVERSCAN = 6; // 6 rows above and below viewport

  let effectiveScrollTop = $state(0);
  let viewportHeight = $state(600);
  let scrollRafId = 0;

  function updateVirtualMetrics() {
    const el = activeScrollEl;
    if (!el) return;

    viewportHeight = el.clientHeight || 600;

    if (embedInScroll && tableBodyEl) {
      const containerRect = el.getBoundingClientRect();
      const bodyRect = tableBodyEl.getBoundingClientRect();
      const bodyTopOffset = (bodyRect.top - containerRect.top) + el.scrollTop;
      effectiveScrollTop = Math.max(0, el.scrollTop - bodyTopOffset);
    } else {
      effectiveScrollTop = el.scrollTop;
    }
  }

  function handleScrollEvent() {
    if (scrollRafId) return;
    scrollRafId = requestAnimationFrame(() => {
      scrollRafId = 0;
      updateVirtualMetrics();
    });
  }

  // Calculate visible window
  let startIndex = $derived(
    songs.length === 0 ? 0 : Math.max(0, Math.floor(effectiveScrollTop / ROW_STEP) - OVERSCAN)
  );

  let endIndex = $derived(
    songs.length === 0
      ? 0
      : Math.min(songs.length, Math.ceil((effectiveScrollTop + viewportHeight) / ROW_STEP) + OVERSCAN)
  );

  let topSpacerHeight = $derived(startIndex * ROW_STEP);
  let bottomSpacerHeight = $derived(Math.max(0, (songs.length - endIndex) * ROW_STEP));
  let visibleSongs = $derived(songs.slice(startIndex, endIndex));

  // Connect scroll listener to activeScrollEl
  $effect(() => {
    const el = activeScrollEl;
    if (!el) return;

    updateVirtualMetrics();
    el.addEventListener('scroll', handleScrollEvent, { passive: true });
    window.addEventListener('resize', handleScrollEvent, { passive: true });

    return () => {
      el.removeEventListener('scroll', handleScrollEvent);
      window.removeEventListener('resize', handleScrollEvent);
      if (scrollRafId) {
        cancelAnimationFrame(scrollRafId);
        scrollRafId = 0;
      }
    };
  });

  // Keep scroll metrics updated when songs list or size changes
  $effect(() => {
    if (songs.length >= 0) {
      tick().then(updateVirtualMetrics);
    }
  });

  // ==========================================
  // Selection State
  // ==========================================
  let activeMenuId = $state<string | null>(null);
  let contextMenuPos = $state<{ x: number; y: number } | null>(null);
  let anchorId = $state<string | null>(null);

  let selectedIdSet = $derived(new Set(selectedIds));
  let isMultiSelect = $derived(selectedIds.length > 1);

  // Sync selectedId with selectedIds
  $effect(() => {
    if (selectedId === null) {
      if (selectedIds.length > 0) {
        selectedIds = [];
      }
    } else if (selectedId && !selectedIds.includes(selectedId)) {
      selectedIds = [selectedId];
    }
  });

  function updateSelection(newIds: string[], primarySong: MidiFile | null = null) {
    selectedIds = newIds;
    selectedId = newIds.length > 0
      ? (primarySong ? primarySong.id : newIds[newIds.length - 1])
      : null;
    const currentSelected = songs.filter(s => newIds.includes(s.id));
    if (primarySong) {
      onselect?.(primarySong, currentSelected);
    }
    onselectionchange?.(selectedIds, currentSelected);
  }

  function handleRowClick(e: MouseEvent, song: MidiFile, idx: number) {
    if (wasDragging) {
      wasDragging = false;
      return;
    }
    const isCtrl = e.ctrlKey || e.metaKey;
    const isShift = e.shiftKey;

    if (isShift) {
      e.preventDefault();

      let anchorIdx = anchorId ? songs.findIndex(s => s.id === anchorId) : -1;
      if (anchorIdx === -1) {
        anchorIdx = 0;
        anchorId = songs[0]?.id ?? song.id;
      }

      const min = Math.min(anchorIdx, idx);
      const max = Math.max(anchorIdx, idx);
      const rangeIds = songs.slice(min, max + 1).map(s => s.id);

      if (isCtrl) {
        const unionSet = new Set([...selectedIds, ...rangeIds]);
        updateSelection(Array.from(unionSet), song);
      } else {
        updateSelection(rangeIds, song);
      }
    } else if (isCtrl) {
      if (selectedIds.includes(song.id)) {
        const remaining = selectedIds.filter(id => id !== song.id);
        const nextPrimary = remaining.length > 0
          ? (songs.find(s => s.id === remaining[remaining.length - 1]) ?? null)
          : null;
        updateSelection(remaining, nextPrimary);
      } else {
        updateSelection([...selectedIds, song.id], song);
      }
      anchorId = song.id;
    } else {
      updateSelection([song.id], song);
      anchorId = song.id;
    }
  }

  function handleRowContextMenu(e: MouseEvent, song: MidiFile) {
    e.preventDefault();
    if (!selectedIds.includes(song.id)) {
      updateSelection([song.id], song);
      anchorId = song.id;
    }
    activeMenuId = song.id;
    const menuWidth = 200;
    const menuHeight = 208;
    const x = Math.max(8, Math.min(e.clientX, window.innerWidth - menuWidth - 8));
    const y = Math.max(8, Math.min(e.clientY, window.innerHeight - menuHeight - 8));
    contextMenuPos = { x, y };
  }

  function handleRowDoubleClick(song: MidiFile, index: number) {
    if (onplay) {
      onplay(song, index);
    } else {
      if (playbackStore.activeSong?.id === song.id) {
        playbackStore.togglePlay();
      } else {
        playbackStore.play(song);
      }
    }
  }

  function handlePlayButtonClick(e: MouseEvent, song: MidiFile, index: number) {
    e.stopPropagation();
    updateSelection([song.id], song);
    anchorId = song.id;
    if (playbackStore.activeSong?.id === song.id) {
      playbackStore.togglePlay();
    } else {
      if (onplay) {
        onplay(song, index);
      } else {
        playbackStore.play(song);
      }
    }
  }

  function handleMenuButtonClick(e: MouseEvent, song: MidiFile) {
    e.stopPropagation();
    contextMenuPos = null;
    if (!selectedIds.includes(song.id)) {
      updateSelection([song.id], song);
      anchorId = song.id;
    }
    activeMenuId = activeMenuId === song.id ? null : song.id;
  }

  function closeMenu() {
    activeMenuId = null;
    contextMenuPos = null;
  }

  function handlePlayNextAction(fallbackSong: MidiFile) {
    const targetSongs = isMultiSelect && selectedIds.includes(fallbackSong.id)
      ? songs.filter(s => selectedIds.includes(s.id))
      : [fallbackSong];

    void playbackStore.playNext(targetSongs);
    notificationCenter.info(t('songs.playNext'), `${targetSongs.length} track(s) will play next.`);
    closeMenu();
  }

  function handleAddToQueueAction(fallbackSong: MidiFile) {
    const targetSongs = isMultiSelect && selectedIds.includes(fallbackSong.id)
      ? songs.filter(s => selectedIds.includes(s.id))
      : [fallbackSong];

    void playbackStore.addToQueue(targetSongs);
    notificationCenter.info(
      t('songs.addToQueue'),
      targetSongs.length === 1
        ? `Added "${targetSongs[0].title}" to queue.`
        : `Added ${targetSongs.length} songs to queue.`
    );
    closeMenu();
  }

  function handleDeleteAction(song: MidiFile) {
    ondelete?.(isMultiSelect && selectedIds.includes(song.id) ? [...selectedIds] : [song.id]);
    closeMenu();
  }

  function handleRemoveAction(fallbackSong: MidiFile, idx: number) {
    if (isMultiSelect && selectedIds.includes(fallbackSong.id)) {
      if (onremoveselected) {
        const selIndices = songs
          .map((s, i) => selectedIds.includes(s.id) ? i : -1)
          .filter(i => i !== -1);
        onremoveselected(selectedIds, selIndices);
      } else {
        playbackStore.removeMultipleFromQueue(selectedIds);
        notificationCenter.info(
          t('queue.removedFromQueueTitle'),
          `${selectedIds.length} tracks removed from queue.`
        );
        selectedIds = [];
        selectedId = null;
      }
    } else {
      if (onremove) {
        onremove(idx);
      } else {
        playbackStore.removeFromQueue(idx);
        notificationCenter.info(t('queue.removedFromQueueTitle'), t('queue.removedFromQueueMsg'));
      }
    }
    closeMenu();
  }

  const NOTES = ['C', 'C♯', 'D', 'D♯', 'E', 'F', 'F♯', 'G', 'G♯', 'A', 'A♯', 'B'];
  function getSongKey(song: MidiFile | null): string {
    if (!song) return 'C4';
    const baseKey = song.baseKey ?? (song as any).baseKeyOffset ?? 0;
    const idx = ((baseKey % 12) + 12) % 12;
    const octave = Math.floor(baseKey / 12) + 3;
    return `${NOTES[idx]}${octave}`;
  }

  function formatSongDate(isoOrDate?: string | Date): string {
    if (!isoOrDate) return 'Oct 14, 2024';
    const parsed = new Date(isoOrDate);
    if (isNaN(parsed.getTime())) return 'Oct 14, 2024';
    return parsed.toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' });
  }

  // ==========================================
  // Drag & Drop Row Reordering (Queue Mode)
  // O(1) Mathematical Slot Calculation
  // ==========================================
  let isDragging = $state(false);
  let isDropping = $state(false);
  let draggedIndex = $state<number | null>(null);
  let draggedSong = $state<MidiFile | null>(null);
  let targetSlot = $state<number | null>(null);
  let dragPos = $state<{ x: number; y: number }>({ x: 0, y: 0 });
  let dragOffset = $state<{ x: number; y: number }>({ x: 0, y: 0 });
  let dropTargetPos = $state<{ x: number; y: number }>({ x: 0, y: 0 });
  let previewWidth = $state<number>(0);
  let previewHeight = $state<number>(56);
  let isDragReorderable = $derived(isQueue && canReorder);
  let isDragCommitting = $state(false);

  function getDisplayIndex(songIdx: number): number {
    if (!isDragging || draggedIndex === null || targetSlot === null) {
      return songIdx + 1;
    }
    const from = draggedIndex;
    const to = targetSlot;
    if (songIdx === from) {
      return to + 1;
    }
    if (to < from) {
      if (songIdx >= to && songIdx < from) {
        return songIdx + 2;
      }
    } else if (to > from) {
      if (songIdx > from && songIdx <= to) {
        return songIdx;
      }
    }
    return songIdx + 1;
  }

  let pendingDrag: {
    startX: number;
    startY: number;
    song: MidiFile;
    idx: number;
    rowEl: HTMLElement;
  } | null = null;
  let wasDragging = false;
  const DRAG_THRESHOLD = 5;

  function handleRowPointerDown(e: PointerEvent, song: MidiFile, idx: number) {
    if (!isDragReorderable || e.button !== 0) return;
    if ((e.target as HTMLElement).closest('button, input, select, textarea, [role="menu"], [role="menuitem"], .menu-btn, .row-play-btn')) {
      return;
    }

    const rowEl = e.currentTarget as HTMLElement;
    pendingDrag = {
      startX: e.clientX,
      startY: e.clientY,
      song,
      idx,
      rowEl
    };

    window.addEventListener('pointermove', onWindowPointerMove);
    window.addEventListener('pointerup', onWindowPointerUp);
    window.addEventListener('pointercancel', onWindowPointerUp);
  }

  function onWindowPointerMove(e: PointerEvent) {
    if (pendingDrag && !isDragging) {
      const dist = Math.hypot(e.clientX - pendingDrag.startX, e.clientY - pendingDrag.startY);
      if (dist >= DRAG_THRESHOLD) {
        isDragging = true;
        document.body.classList.add('row-dragging');
        draggedIndex = pendingDrag.idx;
        draggedSong = pendingDrag.song;
        const rect = pendingDrag.rowEl.getBoundingClientRect();
        previewWidth = rect.width;
        previewHeight = rect.height;
        dragOffset = {
          x: pendingDrag.startX - rect.left,
          y: pendingDrag.startY - rect.top
        };
        dragPos = { x: e.clientX, y: e.clientY };
        targetSlot = pendingDrag.idx;
      }
    }

    if (isDragging) {
      dragPos = { x: e.clientX, y: e.clientY };
      updateTargetSlot(e.clientY);
      handleAutoScroll(e.clientY);
    }
  }

  function onWindowPointerUp() {
    document.body.classList.remove('row-dragging');
    window.removeEventListener('pointermove', onWindowPointerMove);
    window.removeEventListener('pointerup', onWindowPointerUp);
    window.removeEventListener('pointercancel', onWindowPointerUp);

    pendingDrag = null;

    if (isDragging) {
      finishDrag();
    }
  }

  function updateTargetSlot(clientY: number) {
    if (draggedIndex === null || songs.length === 0) return;
    const visibleRows = Array.from(
      document.querySelectorAll('.table-body .table-row:not(.is-drag-origin)')
    ) as HTMLElement[];

    if (visibleRows.length === 0) {
      targetSlot = 0;
      return;
    }

    const firstRect = visibleRows[0].getBoundingClientRect();
    const firstVisIdx = parseInt(visibleRows[0].dataset.visIdx ?? '0', 10);
    if (clientY <= firstRect.top + 8) {
      targetSlot = firstVisIdx;
      return;
    }

    const lastRow = visibleRows[visibleRows.length - 1];
    const lastRect = lastRow.getBoundingClientRect();
    if (clientY >= lastRect.bottom - 8) {
      targetSlot = songs.length - 1;
      return;
    }

    // Check if cursor is in an active gap between rows (allotted space)
    for (let i = 1; i < visibleRows.length; i++) {
      const prevRect = visibleRows[i - 1].getBoundingClientRect();
      const currRect = visibleRows[i].getBoundingClientRect();
      if (clientY >= prevRect.bottom && clientY <= currRect.top) {
        targetSlot = parseInt(visibleRows[i].dataset.visIdx ?? `${i}`, 10);
        return;
      }
    }

    // Check each visible row midpoint
    for (let i = 0; i < visibleRows.length; i++) {
      const rect = visibleRows[i].getBoundingClientRect();
      const midY = rect.top + rect.height / 2;
      if (clientY < midY) {
        targetSlot = parseInt(visibleRows[i].dataset.visIdx ?? `${i}`, 10);
        return;
      }
    }

    targetSlot = songs.length - 1;
  }

  function handleAutoScroll(clientY: number) {
    const el = activeScrollEl;
    if (!el) return;
    const rect = el.getBoundingClientRect();
    const topEdge = rect.top + 48;
    const bottomEdge = rect.bottom - 48;

    let speed = 0;
    if (clientY < topEdge && el.scrollTop > 0) {
      speed = -Math.min(18, (topEdge - clientY) * 0.45);
    } else if (clientY > bottomEdge && el.scrollTop < el.scrollHeight - el.clientHeight) {
      speed = Math.min(18, (clientY - bottomEdge) * 0.45);
    }

    if (speed !== 0) {
      el.scrollTop += speed;
    }
  }

  function finishDrag() {
    if (!isDragging || draggedIndex === null || targetSlot === null || !draggedSong) {
      cancelDrag();
      return;
    }

    wasDragging = true;
    setTimeout(() => { wasDragging = false; }, 100);

    const fromIdx = draggedIndex;
    const toSlot = targetSlot;

    const visibleRows = Array.from(
      document.querySelectorAll('.table-body .table-row:not(.is-drag-origin)')
    ) as HTMLElement[];

    let targetX = dragPos.x - dragOffset.x;
    let targetY = dragPos.y - dragOffset.y;

    if (visibleRows.length > 0) {
      const targetRow = visibleRows.find(r => parseInt(r.dataset.visIdx ?? '-1', 10) === toSlot);
      if (targetRow) {
        const r = targetRow.getBoundingClientRect();
        targetX = r.left;
        targetY = r.top - 58;
      } else {
        const r = visibleRows[visibleRows.length - 1].getBoundingClientRect();
        targetX = r.left;
        targetY = r.bottom + 2;
      }
    }

    isDropping = true;
    dropTargetPos = { x: targetX, y: targetY };

    setTimeout(() => {
      commitReorder(fromIdx, toSlot);
    }, 180);
  }

  function commitReorder(fromIdx: number, toSlot: number) {
    document.body.classList.remove('row-dragging');
    if (fromIdx !== toSlot) {
      isDragCommitting = true;
      const reordered = [...songs];
      const [movedItem] = reordered.splice(fromIdx, 1);
      reordered.splice(toSlot, 0, movedItem);

      if (onreorder) {
        onreorder(reordered);
      } else {
        playbackStore.reorderQueue(reordered);
      }

      requestAnimationFrame(() => {
        requestAnimationFrame(() => {
          isDragCommitting = false;
        });
      });
    }

    isDragging = false;
    isDropping = false;
    draggedIndex = null;
    draggedSong = null;
    targetSlot = null;
  }

  function cancelDrag() {
    document.body.classList.remove('row-dragging');
    isDragging = false;
    isDropping = false;
    isDragCommitting = false;
    draggedIndex = null;
    draggedSong = null;
    targetSlot = null;
    pendingDrag = null;
    wasDragging = true;
    setTimeout(() => { wasDragging = false; }, 100);
  }

  // ==========================================
  // Keyboard Navigation & Scroll-Into-View
  // ==========================================
  function ensureIndexVisible(idx: number) {
    const el = activeScrollEl;
    if (!el) return;

    const itemTop = PADDING_TOP + idx * ROW_STEP;
    const itemBottom = itemTop + ROW_STEP;
    const viewTop = effectiveScrollTop;
    const viewBottom = effectiveScrollTop + viewportHeight;

    if (itemTop < viewTop) {
      const targetScroll = embedInScroll && tableBodyEl
        ? Math.max(0, itemTop + (tableBodyEl.offsetTop || 0))
        : itemTop;
      el.scrollTo({ top: targetScroll, behavior: 'instant' });
    } else if (itemBottom > viewBottom) {
      const targetScroll = embedInScroll && tableBodyEl
        ? Math.max(0, itemBottom - viewportHeight + (tableBodyEl.offsetTop || 0) + PADDING_BOTTOM)
        : Math.max(0, itemBottom - viewportHeight + PADDING_BOTTOM);
      el.scrollTo({ top: targetScroll, behavior: 'instant' });
    }
  }

  function handleKeyDown(e: KeyboardEvent) {
    if (!containerEl?.getClientRects().length || document.querySelector('[aria-modal="true"]')) return;
    const target = e.target as HTMLElement | null;
    if (target && (target.tagName === 'INPUT' || target.tagName === 'TEXTAREA' || target.isContentEditable)) {
      return;
    }

    if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'a') {
      if (songs.length > 0) {
        e.preventDefault();
        const allIds = songs.map(s => s.id);
        updateSelection(allIds, songs[0]);
      }
      return;
    }

    if (e.key === 'Escape') {
      if (isDragging) {
        cancelDrag();
        return;
      }
      if (activeMenuId) {
        closeMenu();
      } else if (selectedIds.length > 0) {
        updateSelection([]);
      }
      return;
    }

    if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
      if (songs.length === 0) return;
      const dir = e.key === 'ArrowDown' ? 1 : -1;
      let currentIdx = -1;
      if (selectedId) {
        currentIdx = songs.findIndex(s => s.id === selectedId);
      }
      let nextIdx = currentIdx === -1
        ? (dir === 1 ? 0 : songs.length - 1)
        : currentIdx + dir;

      if (nextIdx >= 0 && nextIdx < songs.length) {
        e.preventDefault();
        const nextSong = songs[nextIdx];
        ensureIndexVisible(nextIdx);

        if (e.shiftKey) {
          let anchorIdx = anchorId ? songs.findIndex(s => s.id === anchorId) : -1;
          if (anchorIdx === -1) {
            anchorIdx = currentIdx !== -1 ? currentIdx : 0;
            anchorId = songs[anchorIdx]?.id ?? nextSong.id;
          }
          const min = Math.min(anchorIdx, nextIdx);
          const max = Math.max(anchorIdx, nextIdx);
          const rangeIds = songs.slice(min, max + 1).map(s => s.id);
          updateSelection(rangeIds, nextSong);
        } else {
          updateSelection([nextSong.id], nextSong);
          anchorId = nextSong.id;
        }
      }
      return;
    }

    if ((e.key === 'Delete' || e.key === 'Backspace') && (isQueue || ondelete) && selectedIds.length > 0) {
      e.preventDefault();
      const firstSelected = songs.find(s => selectedIds.includes(s.id)) ?? songs[0];
      if (isQueue) handleRemoveAction(firstSelected, 0);
      else handleDeleteAction(firstSelected);
    }
  }

  function handleTableBodyClick(e: MouseEvent) {
    const target = e.target as HTMLElement | null;
    if (target && !target.closest('.table-row') && !target.closest('.row-context-menu')) {
      updateSelection([]);
      closeMenu();
    }
  }

  // ==========================================
  // Resizable Column Widths (Spotify Desktop)
  // ==========================================
  interface ColumnWidths {
    album: number;
    key: number;
    bpm: number;
    transpose: number;
    date: number;
  }

  const DEFAULT_WIDTHS: ColumnWidths = {
    album: 130,
    key: 65,
    bpm: 60,
    transpose: 80,
    date: 105
  };

  let columnWidths = $state<ColumnWidths>({ ...DEFAULT_WIDTHS });
  let columnsReady = $state(false);

  $effect(() => {
    let disposed = false;
    let readyFrame = 0;
    loadUiPreferences().then((preferences) => {
      if (disposed) return;
      const saved = preferences.songColumnWidths;
      if (saved) columnWidths = {
        album: Math.max(saved.album ?? DEFAULT_WIDTHS.album, 60),
        key: Math.max(saved.key ?? DEFAULT_WIDTHS.key, 65),
        bpm: Math.max(saved.bpm ?? DEFAULT_WIDTHS.bpm, 60),
        transpose: Math.max(saved.transpose ?? DEFAULT_WIDTHS.transpose, 75),
        date: Math.max(saved.date ?? DEFAULT_WIDTHS.date, 100),
      };
    }).catch(console.error).finally(async () => {
      await tick();
      if (disposed) return;
      readyFrame = requestAnimationFrame(() => {
        // Commit the measured layout and restored widths before enabling motion.
        containerEl?.getBoundingClientRect();
        columnsReady = true;
      });
    });
    return () => {
      disposed = true;
      cancelAnimationFrame(readyFrame);
    };
  });

  function saveWidths() {
    saveUiPreferences({ songColumnWidths: { ...columnWidths } });
  }

  let containerWidth = $state(1000);
  onMount(() => {
    if (containerEl) containerWidth = containerEl.clientWidth;
  });
  let showTranspose = $derived(containerWidth >= 980);
  let showBpm = $derived(containerWidth >= 880);
  let showKey = $derived(containerWidth >= 780);
  let showDate = $derived(containerWidth >= 560);
  let showAlbum = $derived(containerWidth >= 440);

  function getNextVisibleColumn(col: string): 'key' | 'bpm' | 'transpose' | 'date' | null {
    if (col === 'album') {
      if (showKey) return 'key';
      if (showBpm) return 'bpm';
      if (showTranspose) return 'transpose';
      if (showDate) return 'date';
      return null;
    }
    if (col === 'key') {
      if (showBpm) return 'bpm';
      if (showTranspose) return 'transpose';
      if (showDate) return 'date';
      return null;
    }
    if (col === 'bpm') {
      if (showTranspose) return 'transpose';
      if (showDate) return 'date';
      return null;
    }
    if (col === 'transpose') {
      if (showDate) return 'date';
      return null;
    }
    return null;
  }

  let activeResizeCol = $state<string | null>(null);
  let activeResizePartner = $state<string | null>(null);
  let resizeStartX = $state(0);
  let resizeStartColA = $state(0);
  let resizeStartColB = $state(0);
  let isResizing = $state(false);

  function startResize(e: MouseEvent, col: 'title' | 'album' | 'key' | 'bpm' | 'transpose') {
    e.preventDefault();
    e.stopPropagation();

    activeResizeCol = col;
    resizeStartX = e.clientX;
    isResizing = true;

    if (col === 'title') {
      resizeStartColA = 0;
      resizeStartColB = columnWidths.album;
      activeResizePartner = 'album';
    } else {
      const partner = getNextVisibleColumn(col);
      activeResizePartner = partner;
      if (!partner) {
        isResizing = false;
        activeResizeCol = null;
        return;
      }
      resizeStartColA = (columnWidths as any)[col];
      resizeStartColB = (columnWidths as any)[partner];
    }

    document.body.classList.add('col-resizing');
    window.addEventListener('mousemove', handleMouseMove);
    window.addEventListener('mouseup', handleMouseUp);
  }

  function handleMouseMove(e: MouseEvent) {
    if (!activeResizeCol || !isResizing) return;
    const dx = e.clientX - resizeStartX;

    if (activeResizeCol === 'title') {
      columnWidths.album = Math.max(60, Math.min(300, resizeStartColB - dx));
    } else if (activeResizePartner) {
      const total = resizeStartColA + resizeStartColB;
      const minA = activeResizeCol === 'album' ? 60 : 35;
      const minB = activeResizePartner === 'date' ? 65 : 35;
      const maxA = total - minB;
      const newA = Math.max(minA, Math.min(maxA, resizeStartColA + dx));
      (columnWidths as any)[activeResizeCol] = newA;
      (columnWidths as any)[activeResizePartner] = total - newA;
    }
  }

  function handleMouseUp() {
    if (isResizing) {
      isResizing = false;
      activeResizeCol = null;
      activeResizePartner = null;
      document.body.classList.remove('col-resizing');
      saveWidths();
      window.removeEventListener('mousemove', handleMouseMove);
      window.removeEventListener('mouseup', handleMouseUp);
    }
  }

  function resetColumnWidth(e: MouseEvent) {
    e.preventDefault();
    e.stopPropagation();
    columnWidths = { ...DEFAULT_WIDTHS };
    saveWidths();
  }

  onDestroy(() => {
    document.body.classList.remove('col-resizing');
    document.body.classList.remove('row-dragging');
    window.removeEventListener('mousemove', handleMouseMove);
    window.removeEventListener('mouseup', handleMouseUp);
    window.removeEventListener('pointermove', onWindowPointerMove);
    window.removeEventListener('pointerup', onWindowPointerUp);
    window.removeEventListener('pointercancel', onWindowPointerUp);
  });
</script>

<svelte:window onclick={closeMenu} onkeydown={handleKeyDown} onscroll={closeMenu} />

<div
  bind:this={containerEl}
  class="song-list-container"
  class:columns-ready={columnsReady}
  class:embedded={embedInScroll}
  class:is-resizing={isResizing}
  class:is-dragging={isDragging || isDropping}
  class:is-dropping={isDropping}
  class:is-committing={isDragCommitting}
  class:hide-transpose={!showTranspose}
  class:hide-bpm={!showBpm}
  class:hide-key={!showKey}
  class:hide-date={!showDate}
  class:hide-album={!showAlbum}
  bind:clientWidth={containerWidth}
  style="
    --col-album-w: {columnWidths.album}px;
    --col-key-w: {columnWidths.key}px;
    --col-bpm-w: {columnWidths.bpm}px;
    --col-transpose-w: {columnWidths.transpose}px;
    --col-date-w: {columnWidths.date}px;
  "
>
  <!-- Column Headers matching Spotify layout with hover dividers -->
  <div class="table-header" class:is-resizing={isResizing}>
    <div class="col-num">{t('songs.colNum')}</div>

    <div class="col-title">
      <span class="header-col-label"><OverflowText text={t('songs.colTitle')} /></span>
      {#if showAlbum}
        <button
          type="button"
          class="col-resizer"
          aria-label="Resize title column"
          tabindex="-1"
          onmousedown={(e) => startResize(e, 'title')}
          ondblclick={resetColumnWidth}
          title="Drag to resize column (Double-click to reset)"
        >
          <div class="divider-line" class:active={activeResizeCol === 'title'}></div>
        </button>
      {/if}
    </div>

    <div class="col-album">
      <span class="header-col-label"><OverflowText text={t('songs.colAlbum')} /></span>
      {#if showKey || showBpm || showTranspose || showDate}
        <button
          type="button"
          class="col-resizer"
          aria-label="Resize album column"
          tabindex="-1"
          onmousedown={(e) => startResize(e, 'album')}
          ondblclick={resetColumnWidth}
          title="Drag to resize column (Double-click to reset)"
        >
          <div class="divider-line" class:active={activeResizeCol === 'album'}></div>
        </button>
      {/if}
    </div>

    <div class="col-key">
      <span class="header-col-label"><OverflowText text={t('songs.colKey')} /></span>
      {#if showBpm || showTranspose || showDate}
        <button
          type="button"
          class="col-resizer"
          aria-label="Resize key column"
          tabindex="-1"
          onmousedown={(e) => startResize(e, 'key')}
          ondblclick={resetColumnWidth}
          title="Drag to resize column (Double-click to reset)"
        >
          <div class="divider-line" class:active={activeResizeCol === 'key'}></div>
        </button>
      {/if}
    </div>

    <div class="col-bpm">
      <span class="header-col-label"><OverflowText text={t('songs.colBpm')} /></span>
      {#if showTranspose || showDate}
        <button
          type="button"
          class="col-resizer"
          aria-label="Resize bpm column"
          tabindex="-1"
          onmousedown={(e) => startResize(e, 'bpm')}
          ondblclick={resetColumnWidth}
          title="Drag to resize column (Double-click to reset)"
        >
          <div class="divider-line" class:active={activeResizeCol === 'bpm'}></div>
        </button>
      {/if}
    </div>

    <div class="col-transpose">
      <span class="header-col-label"><OverflowText text={t('songs.colTranspose')} /></span>
      {#if showDate}
        <button
          type="button"
          class="col-resizer"
          aria-label="Resize transpose column"
          tabindex="-1"
          onmousedown={(e) => startResize(e, 'transpose')}
          ondblclick={resetColumnWidth}
          title="Drag to resize column (Double-click to reset)"
        >
          <div class="divider-line" class:active={activeResizeCol === 'transpose'}></div>
        </button>
      {/if}
    </div>

    <div class="col-date">
      <span class="header-col-label"><OverflowText text={t('songs.colDateAdded')} /></span>
    </div>

    <div class="col-duration">
      <FluentIcon name="Clock" size={13} class="clock-icon" />
    </div>

    <div class="col-menu"></div>
  </div>

  <!-- Virtualized Rows List Snippet -->
  {#snippet virtualizedRows()}
    {#if songs.length === 0}
      <div class="empty-state">
        <FluentIcon name="MusicNote2" size={32} class="empty-icon" />
        <span class="empty-msg">{t('songs.noTracks')}</span>
      </div>
    {:else}
      <!-- Top virtual spacer to preserve total scroll height -->
      {#if topSpacerHeight > 0}
        <div class="virtual-spacer" style="height: {topSpacerHeight}px;" aria-hidden="true"></div>
      {/if}

      <!-- Render only the ~25 visible rows -->
      {#each visibleSongs as song, relIdx (song.id)}
        {@const absoluteIdx = startIndex + relIdx}
        {@const isCurrentSong = playbackStore.activeSong?.id === song.id}
        {@const isActivelyPlaying = isCurrentSong && playbackStore.isPlaying}
        {@const isSelected = selectedIdSet.has(song.id) || selectedId === song.id}
        {@const isOrigin = isDragging && draggedIndex === absoluteIdx}
        {@const visIdx = isDragging && draggedIndex !== null ? (absoluteIdx < draggedIndex ? absoluteIdx : absoluteIdx - 1) : absoluteIdx}
        {@const showDropBefore = isDragging && !isOrigin && targetSlot === visIdx && targetSlot < songs.length - 1}
        {@const showDropAfter = isDragging && !isOrigin && visIdx === songs.length - 2 && targetSlot === songs.length - 1}

        <SongListRow
          {song}
          index={absoluteIdx}
          visIndex={visIdx}
          displayIndex={getDisplayIndex(absoluteIdx)}
          {isSelected}
          {isCurrentSong}
          isPlaying={playbackStore.isPlaying}
          {isDragging}
          {isOrigin}
          {showDropBefore}
          {showDropAfter}
          isMenuOpen={activeMenuId === song.id && !contextMenuPos}
          {isQueue}
          hasStudioOption={!!onviewinstudio && !isMultiSelect}
          hasEditOption={!!onedit && !isMultiSelect}
          onclick={(e) => handleRowClick(e, song, absoluteIdx)}
          onpointerdown={(e) => handleRowPointerDown(e, song, absoluteIdx)}
          oncontextmenu={(e) => handleRowContextMenu(e, song)}
          ondblclick={() => handleRowDoubleClick(song, absoluteIdx)}
          onplayclick={(e) => handlePlayButtonClick(e, song, absoluteIdx)}
          onmenuclick={(e) => handleMenuButtonClick(e, song)}
          onplaynext={() => handlePlayNextAction(song)}
          onaddtoqueue={() => handleAddToQueueAction(song)}
          onviewinstudio={() => { onviewinstudio?.(song); closeMenu(); }}
          onedit={() => { onedit?.(song); closeMenu(); }}
          onremove={() => handleRemoveAction(song, absoluteIdx)}
          ondelete={ondelete ? () => handleDeleteAction(song) : undefined}
          onclosemenu={closeMenu}
        />
      {/each}

      <!-- Bottom virtual spacer -->
      {#if bottomSpacerHeight > 0}
        <div class="virtual-spacer" style="height: {bottomSpacerHeight}px;" aria-hidden="true"></div>
      {/if}
    {/if}
  {/snippet}

  {#if embedInScroll}
    <div
      bind:this={tableBodyEl}
      class="table-body"
      onclick={handleTableBodyClick}
      role="presentation"
    >
      {@render virtualizedRows()}
    </div>
  {:else}
    <CustomScrollViewer
      class="table-body-viewer"
      contentClass="table-body"
      bind:scrollContainer={internalScrollEl}
      onclick={handleTableBodyClick}
      padding="4px 16px 16px 16px"
      gap="2px"
      fadeHeight={32}
      fadeRampDistance={28}
    >
      <div bind:this={tableBodyEl} class="table-body-inner">
        {@render virtualizedRows()}
      </div>
    </CustomScrollViewer>
  {/if}

  <!-- Floating Drag Preview Card (held by cursor, no duplicate in list) -->
  {#if isDragging && draggedSong}
    <div
      class="drag-preview-card"
      class:is-dropping={isDropping}
      style="
        left: {isDropping ? dropTargetPos.x : (dragPos.x - dragOffset.x)}px;
        top: {isDropping ? dropTargetPos.y : (dragPos.y - dragOffset.y)}px;
        width: {previewWidth}px;
        height: {previewHeight}px;
      "
    >
      <div class="col-num">
        <RollingIndex value={getDisplayIndex(draggedIndex ?? 0)} accent={true} />
      </div>

      <div class="col-title">
        <div class="art-box">
          {#if draggedSong.coverArtUrl}
            <img src={draggedSong.coverArtUrl} alt={draggedSong.title} class="art-img" draggable="false" />
          {:else}
            <FluentIcon name="MusicNote2" size={18} class="art-fallback-icon" />
          {/if}
        </div>
        <div class="title-artist-stack">
          <span class="song-title accent">{draggedSong.title}</span>
          <span class="song-artist">{draggedSong.artist || t('player.unknownArtist')}</span>
        </div>
      </div>

      {#if showAlbum}
        <div class="col-album" title={(draggedSong as any).album || t('songs.singles')}>
          {(draggedSong as any).album || t('songs.singles')}
        </div>
      {/if}

      {#if showKey}
        <div class="col-key">{getSongKey(draggedSong)}</div>
      {/if}

      {#if showBpm}
        <div class="col-bpm">{Math.round(draggedSong.bpm || 120)}</div>
      {/if}

      {#if showTranspose}
        <div class="col-transpose">{draggedSong.transposeMode || 'Smart'}</div>
      {/if}

      {#if showDate}
        <div class="col-date">{formatSongDate(draggedSong.dateAdded)}</div>
      {/if}

      <div class="col-duration">
        {Math.floor((draggedSong.durationSeconds ?? 0) / 60)}:{Math.floor((draggedSong.durationSeconds ?? 0) % 60).toString().padStart(2, '0')}
      </div>

      <div class="col-menu">
        <button type="button" class="menu-btn visible" tabindex="-1">
          <FluentIcon name="MoreHorizontal" size={16} />
        </button>
      </div>
    </div>
  {/if}

  <!-- Floating Right-Click Context Menu -->
  {#if activeMenuId && contextMenuPos}
    {@const activeSong = songs.find(s => s.id === activeMenuId)}
    {#if activeSong}
      {@const activeSongIdx = songs.findIndex(s => s.id === activeMenuId)}
      {@const isTargetPlaying = playbackStore.activeSong?.id === activeSong.id && playbackStore.isPlaying}
      <div
        class="row-context-menu floating"
        style="position: fixed; left: {contextMenuPos.x}px; top: {contextMenuPos.y}px;"
        role="menu"
        tabindex="-1"
        onclick={(e) => e.stopPropagation()}
        onkeydown={(e) => { if (e.key === 'Escape') closeMenu(); }}
      >
        <button
          class="context-item"
          onclick={() => { handleRowDoubleClick(activeSong, activeSongIdx); closeMenu(); }}
        >
          <FluentIcon name="Play" size={14} />
          <span>{isTargetPlaying ? t('player.pause') : t('player.play')}</span>
        </button>

        <button class="context-item" onclick={() => handlePlayNextAction(activeSong)}>
          <FluentIcon name="Next" size={14} />
          <span>{t('songs.playNext')}</span>
        </button>
        <button class="context-item" onclick={() => handleAddToQueueAction(activeSong)}>
          <FluentIcon name="TextBulletListSquare" size={14} />
          <span>{isMultiSelect ? `Add ${selectedIds.length} tracks to Queue` : t('songs.addToQueue')}</span>
        </button>
        {#if onviewinstudio && !isMultiSelect}
          <button class="context-item" onclick={() => { onviewinstudio?.(activeSong); closeMenu(); }}>
            <FluentIcon name="CalendarMonth" size={14} />
            <span>{t('songs.viewInStudio')}</span>
          </button>
        {/if}

        {#if onedit && !isMultiSelect}
          <button
            class="context-item"
            onclick={() => { onedit?.(activeSong); closeMenu(); }}
          >
            <FluentIcon name="Edit" size={14} />
            <span>{t('songs.editDetails')}</span>
          </button>
        {/if}
        {#if ondelete}
          <button class="context-item danger" onclick={() => handleDeleteAction(activeSong)}>
            <FluentIcon name="Delete" size={14} />
            <span>{t('songs.deleteFromLibrary')}</span>
          </button>
        {/if}
        {#if isQueue}
          <button class="context-item danger" onclick={() => handleRemoveAction(activeSong, activeSongIdx)}>
            <FluentIcon name="Delete" size={14} />
            <span>{isMultiSelect ? `Remove ${selectedIds.length} tracks from Queue` : t('songs.removeFromQueue')}</span>
          </button>
        {/if}
      </div>
    {/if}
  {/if}
</div>

<style>
  .song-list-container {
    container-type: inline-size;
    container-name: songtable;
    display: flex;
    flex-direction: column;
    flex: 1;
    min-height: 0;
    width: 100%;
    overflow: hidden;
    user-select: none;
  }

  .song-list-container.embedded {
    overflow: visible;
    flex: none;
    height: auto;
  }

  /* Anchor label tooltips to the visible text, including in column headers. */
  .song-list-container :global(.overflow-text) {
    width: fit-content;
  }

  /* Keep header and row cells in the same flex layout while they collapse.
     Match the lower-right controls: shrink first, then fade the final sliver. */
  .song-list-container :global(:is(.col-album, .col-key, .col-bpm, .col-transpose, .col-date)) {
    transition: width var(--anim-duration, 0.20s) cubic-bezier(0.4, 0, 0.2, 1),
                min-width var(--anim-duration, 0.20s) cubic-bezier(0.4, 0, 0.2, 1),
                max-width var(--anim-duration, 0.20s) cubic-bezier(0.4, 0, 0.2, 1),
                padding-inline var(--anim-duration, 0.20s) cubic-bezier(0.4, 0, 0.2, 1),
                opacity calc(var(--anim-duration, 0.20s) * 0.85) cubic-bezier(0.4, 0, 0.2, 1);
  }

  .song-list-container.hide-transpose :global(.col-transpose),
  .song-list-container.hide-bpm :global(.col-bpm),
  .song-list-container.hide-key :global(.col-key),
  .song-list-container.hide-date :global(.col-date),
  .song-list-container.hide-album :global(.col-album) {
    width: 0;
    min-width: 0;
    max-width: 0;
    padding-inline: 0;
    opacity: 0 !important;
    overflow: hidden !important;
    pointer-events: none;
    transition: width var(--anim-duration, 0.20s) cubic-bezier(0.4, 0, 0.2, 1),
                min-width var(--anim-duration, 0.20s) cubic-bezier(0.4, 0, 0.2, 1),
                max-width var(--anim-duration, 0.20s) cubic-bezier(0.4, 0, 0.2, 1),
                padding-inline var(--anim-duration, 0.20s) cubic-bezier(0.4, 0, 0.2, 1),
                opacity calc(var(--anim-duration, 0.20s) * 0.45) cubic-bezier(0.4, 0, 0.2, 1) calc(var(--anim-duration, 0.20s) * 0.55);
  }

  /* Column dividers should follow the pointer directly during manual resizing. */
  .song-list-container:not(.columns-ready) :global(:is(.col-album, .col-key, .col-bpm, .col-transpose, .col-date)),
  .song-list-container.is-resizing :global(:is(.col-album, .col-key, .col-bpm, .col-transpose, .col-date)),
  :global(.no-animations) .song-list-container :global(:is(.col-album, .col-key, .col-bpm, .col-transpose, .col-date)) {
    transition: none !important;
  }

  @media (prefers-reduced-motion: reduce) {
    .song-list-container :global(:is(.col-album, .col-key, .col-bpm, .col-transpose, .col-date)) {
      transition: none !important;
    }
  }

  /* Table Header */
  .table-header {
    display: flex;
    align-items: stretch;
    height: 36px;
    padding: 0 16px;
    border-bottom: none;
    font-size: 12px;
    color: var(--text-secondary);
    font-weight: 500;
    flex-shrink: 0;
    position: relative;
    background: transparent;
    transition: color var(--anim-normal, 0.20s) ease,
                background-color var(--anim-normal, 0.20s) ease;
  }

  .table-header::after {
    content: '';
    position: absolute;
    bottom: 0;
    left: 16px;
    right: 16px;
    height: 1px;
    background: var(--border-subtle, rgba(255, 255, 255, 0.08));
    pointer-events: none;
  }

  .table-header .col-num,
  .table-header .col-title,
  .table-header .col-album,
  .table-header .col-key,
  .table-header .col-bpm,
  .table-header .col-transpose,
  .table-header .col-date,
  .table-header .col-duration,
  .table-header .col-menu {
    overflow: visible !important;
    position: relative;
    display: flex;
    align-items: center;
    opacity: 1 !important;
    color: inherit;
  }

  .table-header .col-duration {
    justify-content: flex-end;
  }

  .song-list-container.embedded .table-header {
    position: sticky;
    top: 0;
    z-index: 15;
    background: var(--surface-color, #121212);
  }

  .table-header:hover,
  .table-header.is-resizing {
    color: var(--text-primary);
  }

  :global(.table-body-viewer) {
    flex: 1;
    min-height: 0;
    width: 100%;
  }

  .table-body {
    flex: 1;
    min-height: 0;
    padding: 4px 16px 16px 16px;
    display: flex;
    flex-direction: column;
    gap: 2px;
    position: relative;
  }

  .table-body-inner {
    display: flex;
    flex-direction: column;
    gap: 2px;
    position: relative;
    width: 100%;
  }

  .virtual-spacer {
    width: 100%;
    flex-shrink: 0;
    pointer-events: none;
  }

  .song-list-container.embedded .table-body {
    overflow: visible;
    flex: none;
    height: auto;
  }

  /* Column widths */
  .col-num {
    width: 44px;
    min-width: 44px;
    max-width: 44px;
    flex-shrink: 0;
    display: flex;
    align-items: center;
    justify-content: center;
    font-size: 12px;
  }

  .col-title {
    flex: 1;
    min-width: 140px;
    display: flex;
    align-items: center;
    gap: 12px;
    overflow: hidden;
    position: relative;
    box-sizing: border-box;
    padding-right: 12px;
  }

  .col-album {
    width: var(--col-album-w, 130px);
    min-width: var(--col-album-w, 130px);
    max-width: var(--col-album-w, 130px);
    flex-shrink: 0;
    font-size: 12px;
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
    padding: 0 12px;
    opacity: 0.7;
    position: relative;
    box-sizing: border-box;
  }

  .col-key {
    width: var(--col-key-w, 65px);
    min-width: var(--col-key-w, 65px);
    max-width: var(--col-key-w, 65px);
    flex-shrink: 0;
    font-size: 12px;
    opacity: 0.7;
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
    padding: 0 12px;
    position: relative;
    box-sizing: border-box;
  }

  .col-bpm {
    width: var(--col-bpm-w, 60px);
    min-width: var(--col-bpm-w, 60px);
    max-width: var(--col-bpm-w, 60px);
    flex-shrink: 0;
    font-size: 12px;
    opacity: 0.6;
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
    padding: 0 12px;
    position: relative;
    box-sizing: border-box;
  }

  .col-transpose {
    width: var(--col-transpose-w, 80px);
    min-width: var(--col-transpose-w, 80px);
    max-width: var(--col-transpose-w, 80px);
    flex-shrink: 0;
    font-size: 12px;
    opacity: 0.6;
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
    padding: 0 12px;
    position: relative;
    box-sizing: border-box;
  }

  .col-date {
    width: var(--col-date-w, 105px);
    min-width: var(--col-date-w, 105px);
    max-width: var(--col-date-w, 105px);
    flex-shrink: 0;
    font-size: 12px;
    opacity: 0.6;
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
    padding: 0 12px;
    position: relative;
    box-sizing: border-box;
  }

  .col-duration {
    width: 60px;
    min-width: 60px;
    max-width: 60px;
    flex-shrink: 0;
    font-size: 12px;
    opacity: 0.6;
    text-align: right;
    display: flex;
    align-items: center;
    justify-content: flex-end;
    position: relative;
    box-sizing: border-box;
    padding: 0 12px;
  }

  .col-menu {
    width: 44px;
    min-width: 44px;
    max-width: 44px;
    flex-shrink: 0;
    display: flex;
    align-items: center;
    justify-content: center;
    position: relative;
  }

  .col-resizer {
    position: absolute;
    right: -8px;
    top: 0;
    bottom: 0;
    width: 16px;
    cursor: col-resize;
    z-index: 30;
    display: flex;
    align-items: center;
    justify-content: center;
    user-select: none;
    background: transparent;
    border: none;
    padding: 0;
    margin: 0;
    outline: none;
  }

  .divider-line {
    width: 1px;
    height: 16px;
    background: var(--text-tertiary);
    opacity: 0;
    transition: opacity 0.15s ease, background 0.15s ease;
    pointer-events: none;
  }

  .table-header:hover .divider-line,
  .table-header.is-resizing .divider-line {
    opacity: 1;
  }

  .col-resizer:hover .divider-line,
  .divider-line.active {
    background: var(--text-primary);
    opacity: 1;
  }

  .header-col-label {
    min-width: 0;
    width: 100%;
    overflow: hidden;
    white-space: nowrap;
    display: block;
    max-width: 100%;
  }

  :global(body.col-resizing),
  :global(body.col-resizing *) {
    cursor: col-resize !important;
    user-select: none !important;
  }

  :global(body.row-dragging),
  :global(body.row-dragging *) {
    cursor: grabbing !important;
    user-select: none !important;
  }

  /* Empty State */
  .empty-state {
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    padding: 64px 16px;
    color: var(--text-tertiary);
    gap: 12px;
  }

  :global(.empty-icon) {
    opacity: 0.4;
  }

  .empty-msg {
    font-size: 14px;
    font-weight: 500;
  }

  /* Floating Drag Preview Card */
  .drag-preview-card {
    position: fixed;
    z-index: 9999;
    display: flex;
    align-items: center;
    height: 56px;
    background: var(--surface-color, #181818);
    border: 1px solid var(--accent-color, var(--accent, #1DB954));
    border-radius: 6px;
    box-shadow: 0 12px 32px rgba(0, 0, 0, 0.6);
    pointer-events: none;
    transition: transform 0.15s cubic-bezier(0.16, 1, 0.3, 1),
                opacity 0.15s ease;
    will-change: left, top;
  }

  .drag-preview-card.is-dropping {
    transition: left 0.16s cubic-bezier(0.16, 1, 0.3, 1),
                top 0.16s cubic-bezier(0.16, 1, 0.3, 1),
                transform 0.16s ease,
                opacity 0.16s ease;
    opacity: 0.9;
  }

  .drag-preview-card .art-box {
    width: 40px;
    height: 40px;
    border-radius: 4px;
    background: var(--card-bg, #282828);
    display: flex;
    align-items: center;
    justify-content: center;
    overflow: hidden;
    flex-shrink: 0;
  }

  .drag-preview-card .art-img {
    width: 100%;
    height: 100%;
    object-fit: cover;
  }

  .drag-preview-card .title-artist-stack {
    display: flex;
    flex-direction: column;
    justify-content: center;
    min-width: 0;
    overflow: hidden;
    gap: 3px;
  }

  .drag-preview-card .song-title {
    font-size: 13.5px;
    font-weight: 600;
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
  }

  .drag-preview-card .song-title.accent {
    color: var(--accent-color, var(--accent, #1DB954));
  }

  .drag-preview-card .song-artist {
    font-size: 12px;
    color: var(--text-secondary);
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
  }

  /* Right-Click Floating Context Menu */
  .row-context-menu.floating {
    z-index: 10000;
    background: var(--bg-dialog, #282828);
    border: 1px solid var(--border-subtle, rgba(255, 255, 255, 0.1));
    border-radius: 6px;
    box-shadow: 0 8px 24px rgba(0, 0, 0, 0.6);
    padding: 4px;
    min-width: 180px;
    display: flex;
    flex-direction: column;
    gap: 2px;
  }

  .context-item {
    display: flex;
    align-items: center;
    gap: 10px;
    padding: 8px 12px;
    border-radius: 4px;
    background: transparent;
    border: none;
    color: var(--text-primary);
    font-size: 12.5px;
    cursor: pointer;
    text-align: left;
    width: 100%;
    transition: background 0.12s ease;
    user-select: none;
  }

  .context-item:hover {
    background: var(--bg-hover, rgba(255, 255, 255, 0.1));
  }

  .context-item.danger {
    color: #ff5252;
  }

  .context-item.danger:hover {
    background: rgba(255, 82, 82, 0.15);
  }
</style>
