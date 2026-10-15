import { loadUiPreferences, saveUiPreferences, flushUiPreferences } from '../backend/preferences.js';
import { normalizePlayback } from '../utils/sessionPreferences.js';
import type { UiPreferences } from '../../../shared/types/settings.js';
import type { MidiFile, MidiTrack, TransposeMode, KeyChangeMarker } from '../../../shared/types/midi.js';
import type { PlaybackStatus } from '../../../shared/types/playback.js';
import type { GameInfo } from '../../../shared/types/games.js';
import type { SongSettingsContext } from '../../../shared/types/ipc.js';
import { api, isTauri, loadedMidiToMidiFile, type RawLoadedMidiFile } from '../backend/index.js';
import { getKeyNoteName, formatSignedOffset, getTrackColor } from '../utils/musicConstants.js';
import { notificationCenter } from '../ui/notificationCenter.svelte';
import { PlaybackPositionGate } from '../utils/playbackPosition.js';
import { uiStore } from './uiStore.svelte.js';
import { gameStore } from './gameStore.svelte.js';
import { libraryStore } from './libraryStore.svelte.js';
import { settingsStore } from './settingsStore.svelte.js';

class PlaybackStore {
  // ==========================================
  // Core Playback State
  // ==========================================
  status = $state<PlaybackStatus>('Stopped');
  currentTimeSeconds = $state<number>(0);
  playbackSpeed = $state<number>(1.0);
  volume = $state<number>(0.85);
  baseKeyOffset = $state<number>(0);
  transposeMode = $state<TransposeMode>('Smart');
  holdNotes = $state<boolean>(false);
  tapThresholdMs = $state<number>(0);
  autoChord = $state<boolean>(false);
  enableKeyChangeMarkers = $state<boolean>(true);
  isShuffled = $state<boolean>(false);
  loopMode = $state<'off' | 'all' | 'single'>('off');
  isListenMode = $state<boolean>(false);
  synthMode = $state<'adapted' | 'raw'>('adapted');
  trackVolumes = $state<Record<number, number>>({});
  customSoundFontName = $state<string>('Default (General MIDI)');

  // Active playing notes for visualizers (piano keys / card glow / piano roll)
  activeNotes = $state<{ pitch: number; trackId: number }[]>([]);

  // Queue & Active Track
  queue = $state<MidiFile[]>([]);
  activeQueueIndex = $state<number>(-1);
  queuePlaylistId = $state<string | null>(null);
  activeSong = $state<MidiFile | null>(null);
  isChangingSongContext = $state(false);
  private loadedSongContext: SongSettingsContext | null = null;
  private contextRevision = 0;
  private contextRefresh: Promise<void> = Promise.resolve();
  historyStack = $state<MidiFile[]>([]);
  shuffledQueue = $state<MidiFile[]>([]);
  private _songFinishTimer: any = null;
  private _currentLoadedPath: string | null = null;
  private queueWrites: Promise<void> = Promise.resolve();
  private queueWriteError: unknown = null;
  private queueRevision = 0;
  private queueReadId = 0;
  private nextAfterRemovalId: string | null = null;
  private removedActiveSongId: string | null = null;
  private lastSessionSnapshot = '';

  // Derived properties
  get keyOffset() {
    return this.activeSong?.keyOffset ?? 0;
  }

  get keyOffsetBase() {
    // A saved base key is Smart mode's zero point, independent of scale correction.
    return this.activeSong && this.transposeMode === 'Smart' ? this.baseKeyOffset : 0;
  }

  get effectiveKeyOffset() {
    return this.keyOffset + this.keyOffsetBase;
  }

  get activeKeyMarker(): KeyChangeMarker | null {
    if (!this.enableKeyChangeMarkers) return null;
    const timeMs = this.currentTimeSeconds * 1000;
    let active: KeyChangeMarker | null = null;
    for (const marker of this.activeSong?.keyMarkers ?? []) {
      if (marker.timeMs <= timeMs && (!active || marker.timeMs >= active.timeMs)) active = marker;
    }
    return active;
  }

  get activeKeyAdjustment() {
    const marker = this.activeKeyMarker;
    if (marker) return { marker, offset: marker.keyOffset };
    // Smart mode stays visible even when the detected key needs no correction.
    if (this.activeSong && this.transposeMode === 'Smart') {
      return { marker: null, offset: this.effectiveKeyOffset };
    }
    return null;
  }

  isPlaying = $derived(this.status === 'Playing');
  isPaused = $derived(this.status === 'Paused');
  durationSeconds = $derived(
    this.activeSong
      ? (this.activeSong.durationSeconds ?? (this.activeSong.durationMs ? this.activeSong.durationMs / 1000 : 120))
      : 120
  );

  // Dynamic Playability Metrics (matching WPF TrackViewModel)
  totalNotes = $derived.by(() => {
    if (!this.activeSong?.tracks) return 0;
    return this.activeSong.tracks
      .filter(t => !this.isTrackMuted(t.index ?? (t.id ? t.id - 1 : 0)))
      .reduce((sum, t) => sum + (t.noteCount || t.notes?.length || 0), 0);
  });

  accessibleNotes = $derived.by(() => {
    if (!this.activeSong || this.totalNotes === 0) return 0;

    const activeInst = gameStore.activeInstrument;
    const keyOffset = this.effectiveKeyOffset;
    const transpose = this.transposeMode;

    const minMidi = activeInst?.minMidiNote ?? 48;
    const maxMidi = activeInst?.maxMidiNote ?? 83;
    const availableSet = new Set<number>();

    if (activeInst?.keyCount === 21) {
      const intervals = [0, 2, 4, 5, 7, 9, 11];
      for (let oct = 0; oct < 3; oct++) {
        for (const int of intervals) {
          const note = minMidi + oct * 12 + int;
          if (note <= maxMidi) availableSet.add(note);
        }
      }
    } else {
      for (let n = minMidi; n <= maxMidi; n++) {
        availableSet.add(n);
      }
    }

    if (this.activeSong.notes && this.activeSong.notes.length > 0) {
      let accessible = 0;
      for (const note of this.activeSong.notes) {
        const trkIdx = note.trackIndex ?? (note.trackId ? note.trackId - 1 : 0);
        if (this.isTrackMuted(trkIdx)) continue;

        const noteNumber = (note.pitch ?? note.noteNumber ?? 60) + keyOffset;
        if (transpose === 'Ignore') {
          if (availableSet.has(noteNumber)) accessible++;
        } else if (transpose === 'Up' || transpose === 'Down') {
          let folded = noteNumber;
          while (folded < minMidi) folded += 12;
          while (folded > maxMidi) folded -= 12;
          if (availableSet.has(folded)) accessible++;
        } else {
          accessible++;
        }
      }
      return accessible;
    }

    let totalAccessible = 0;
    for (const track of this.activeSong.tracks) {
      const trkIdx = track.index ?? (track.id ? track.id - 1 : 0);
      if (this.isTrackMuted(trkIdx)) continue;
      const count = track.noteCount || track.notes?.length || 0;
      const pct = (track.playablePercentage ?? 100) / 100;
      totalAccessible += Math.round(count * pct);
    }
    return totalAccessible;
  });

  playablePercentage = $derived.by(() => {
    if (this.totalNotes === 0) return 100;
    return Math.min(100, Math.max(0, (this.accessibleNotes / this.totalNotes) * 100));
  });

  getTrackPlayablePercentage(track: MidiTrack): number {
    const total = track.noteCount || track.notes?.length || 0;
    if (total === 0) return 100;

    const activeInst = gameStore.activeInstrument;
    const keyOffset = this.effectiveKeyOffset;
    const transpose = this.transposeMode;

    const minMidi = activeInst?.minMidiNote ?? 48;
    const maxMidi = activeInst?.maxMidiNote ?? 83;
    const availableSet = new Set<number>();

    if (activeInst?.keyCount === 21) {
      const intervals = [0, 2, 4, 5, 7, 9, 11];
      for (let oct = 0; oct < 3; oct++) {
        for (const int of intervals) {
          const note = minMidi + oct * 12 + int;
          if (note <= maxMidi) availableSet.add(note);
        }
      }
    } else {
      for (let n = minMidi; n <= maxMidi; n++) {
        availableSet.add(n);
      }
    }

    const trackNotes = track.notes || this.activeSong?.notes?.filter(n => (n.trackIndex ?? (n.trackId ? n.trackId - 1 : 0)) === track.index) || [];
    if (trackNotes.length > 0) {
      let accessible = 0;
      for (const note of trackNotes) {
        const noteNumber = (note.pitch ?? note.noteNumber ?? 60) + keyOffset;
        if (transpose === 'Ignore') {
          if (availableSet.has(noteNumber)) accessible++;
        } else if (transpose === 'Up' || transpose === 'Down') {
          let folded = noteNumber;
          while (folded < minMidi) folded += 12;
          while (folded > maxMidi) folded -= 12;
          if (availableSet.has(folded)) accessible++;
        } else {
          accessible++;
        }
      }
      return Math.round((accessible / trackNotes.length) * 100);
    }

    return track.playablePercentage ?? 100;
  }

  // Pedals
  pedals = $state({
    enabled: true,
    sustain: false,
    sostenuto: false,
    unaCorda: false,
  });

  supportsPedals = $derived.by(() => {
    if (!gameStore.activeGame) return true;
    const instruments = gameStore.activeGame.instruments;
    if (!instruments || instruments.length === 0) return true;
    return instruments.some(inst => inst.supportsSustain);
  });

  togglePedals() {
    if (!this.supportsPedals) return;
    this.pedals.enabled = !this.pedals.enabled;
    saveUiPreferences({ pedalsEnabled: this.pedals.enabled });
  }

  // Key Change Markers State
  selectedMarkerId = $state<string | null>(null);
  selectedKeyOffset = $state<number>(0);
  selectedMarker = $derived(
    this.activeSong?.keyMarkers?.find(m => m.id === this.selectedMarkerId) ?? null
  );

  private positionGate = new PlaybackPositionGate();
  private pendingSeek: { seconds: number; id: number } | null = null;
  private seekTimer: ReturnType<typeof setTimeout> | null = null;
  private pendingSeekCompletions: (() => void)[] = [];
  isScrubbing = $state(false);

  cancelSeekAnim() {
    this.pendingSeek = null;
    if (this.seekTimer) clearTimeout(this.seekTimer);
    this.seekTimer = null;
    this.positionGate.reset();
    for (const complete of this.pendingSeekCompletions.splice(0)) complete();
  }

  beginScrub() {
    this.isScrubbing = true;
  }

  endScrub() {
    if (!this.isScrubbing) return;
    this.isScrubbing = false;
    void this.seek(this.currentTimeSeconds, { immediate: true });
  }

  async initialize(onProgress?: (status: string) => void) {
    try {
      onProgress?.('startup.interface');
      await uiStore.init();
      onProgress?.('startup.songsAndGames');
      await gameStore.init();
      libraryStore.onSongsUnavailable = async (ids) => {
        await this.prepareSongRemoval(ids);
      };
      await libraryStore.init();
      gameStore.onSettingsContextChange(() => {
        void this.refreshSongSettingsContext().catch(error => {
          notificationCenter.error('Unable to load song settings', String(error));
        });
      });

      onProgress?.('startup.player');
      // Subscribe to backend events
      api.playback.onTimeUpdate((position) => {
        if (!this.positionGate.accepts(position) || this.isScrubbing) return;
        // Ignore stale engine updates if the active song does not match what the engine has loaded
        if (isTauri && this._currentLoadedPath !== this.activeSong?.filePath) return;
        this.currentTimeSeconds = position.timeMs / 1000;
      });

      if (api.playback.onTrackNoteEvent) {
        api.playback.onTrackNoteEvent((trackNotes) => {
          if (!trackNotes || trackNotes.length === 0) {
            this.activeNotes = [];
          } else {
            this.activeNotes = trackNotes.map(n => ({ pitch: n.pitch, trackId: n.trackIndex }));
          }
        });
      }

      api.playback.onPedalState((state) => {
        this.pedals.sustain = state.sustain;
        this.pedals.sostenuto = state.sostenuto;
        this.pedals.unaCorda = state.unaCorda;
      });

      api.playback.onNoteEvent((activePitches) => {
        if (!api.playback.onTrackNoteEvent) {
          if (!activePitches || activePitches.length === 0) {
            this.activeNotes = [];
          } else {
            this.activeNotes = activePitches.map((p, idx) => ({ pitch: p, trackId: (idx % 3) + 1 }));
          }
        }
      });

      api.playback.onStateChanged((status) => {
        this.status = status;
      });

      if (typeof window !== 'undefined' && ('__TAURI_INTERNALS__' in window || '__TAURI__' in window)) {
        import('@tauri-apps/api/event').then(({ listen }) => {
          listen<RawLoadedMidiFile>('playback:song_loaded', (event) => {
            if (event.payload) {
              const loadedSong = loadedMidiToMidiFile(event.payload);
              this._currentLoadedPath = loadedSong.filePath;
              if (this.activeSong && (this.activeSong.id === loadedSong.id || this.activeSong.filePath === loadedSong.filePath)) {
                this.applySongNotes(loadedSong);
                void this.syncTrackSettings().catch(console.error);
              } else {
                this.loadSong(loadedSong);
              }
            }
          });
          listen('playback:finished', () => {
            if (this._songFinishTimer) clearTimeout(this._songFinishTimer);
            this._songFinishTimer = setTimeout(() => {
              this.handleSongFinished().catch(console.error);
            }, 800);
          });
          listen('playback:next', () => {
            this.nextTrack(true).catch(console.error);
          });
          listen('playback:prev', () => {
            this.prevTrack().catch(console.error);
          });
          listen('playback:toggle_play', () => {
            this.togglePlay().catch(console.error);
          });
          listen('playback:speed_up', () => {
            this.increaseSpeed().catch(console.error);
          });
          listen('playback:speed_down', () => {
            this.decreaseSpeed().catch(console.error);
          });
          listen('playback:panic', () => {
            this.stop().catch(console.error);
          });
          listen('queue:updated', () => {
            this.loadQueueFromDb().catch(console.error);
          });
          listen<{ paths: string[] }>('tauri://drag-drop', async (event) => {
            const paths = event.payload?.paths;
            if (Array.isArray(paths) && paths.length > 0) {
              const midPath = paths.find((p: string) => p.endsWith('.mid') || p.endsWith('.midi'));
              if (midPath) {
                try {
                  const imported = await api.playback.loadMidiFile(midPath);
                  if (imported) {
                    this.play(imported);
                    notificationCenter.success(`Loaded MIDI: ${imported.title}`);
                  }
                } catch (e) {
                  console.error('Failed to load dropped MIDI:', e);
                }
              }
            }
          });
        });
      }

      onProgress?.('startup.queue');
      await this.loadQueueFromDb();
      this.loadedSongContext = gameStore.songSettingsContext;

      onProgress?.('startup.preferences');
      await this.restoreSession(await loadUiPreferences());

      // Subscribe to process monitor updates for mid-playback focus loss or process exit
      gameStore.onGameStatusChange((activeGame) => {
        if (activeGame) this.handleGameStatusUpdate(activeGame);
      });
    } catch (e) {
      console.warn('PlaybackState initialize fallback:', e);
    }
  }

  async restoreSession(preferences: UiPreferences) {
    const saved = normalizePlayback(preferences.playback ?? {});
    this.pedals.enabled = preferences.pedalsEnabled ?? true;
    this.volume = saved.volume;
    this.isListenMode = saved.isListenMode;
    this.synthMode = saved.synthMode;
    this.isShuffled = saved.isShuffled;
    this.loopMode = saved.loopMode;
    this.queuePlaylistId = libraryStore.playlists.some(playlist => playlist.id === saved.queuePlaylistId)
      ? saved.queuePlaylistId : null;
    const playlistId = preferences.selectedPlaylistId;
    libraryStore.selectedPlaylistId = libraryStore.playlists.some(playlist => playlist.id === playlistId)
      ? playlistId! : 'songs';

    const byId = new Map(this.queue.map(song => [song.id, song]));
    this.shuffledQueue = saved.shuffledSongIds.flatMap(id => byId.has(id) ? [byId.get(id)!] : []);
    this.syncShuffledQueue();
    const restoredSong = [...this.queue, ...libraryStore.songs].find(song => song.id === saved.activeSongId && !song.isMissing);
    const initialSong = restoredSong ?? this.queue.find(song => !song.isMissing) ?? libraryStore.songs.find(song => !song.isMissing);
    if (initialSong) this.loadSong(initialSong);
    this.currentTimeSeconds = restoredSong ? Math.min(saved.positionSeconds, this.durationSeconds) : 0;
    this.syncQueueIndex();
    // Restore a preview only; the engine loads the song and seeks when Play is pressed.
    this.status = 'Stopped';
    await Promise.all([
      api.playback.setVolume(Math.round(this.volume * 100)),
      api.playback.setListenMode(this.isListenMode),
      api.playback.setSynthMode(this.synthMode),
    ]);
  }

  persistSession(positionSeconds: number, force = false) {
    const playback = {
      activeSongId: this.activeSong?.id ?? null,
      positionSeconds,
      queuePlaylistId: this.queuePlaylistId,
      shuffledSongIds: this.shuffledQueue.map(song => song.id),
      isShuffled: this.isShuffled,
      loopMode: this.loopMode,
      isListenMode: this.isListenMode,
      synthMode: this.synthMode,
      volume: this.volume,
    };
    const snapshot = JSON.stringify(playback);
    if (!force && snapshot === this.lastSessionSnapshot) return;
    this.lastSessionSnapshot = snapshot;
    saveUiPreferences({ playback });
  }

  async flushPersistence() {
    // A final settings write also drains earlier game/layout/settings writes.
    this.persistSession(this.currentTimeSeconds, true);
    await Promise.all([this.queueWrites, this.flushSongSettings(), flushUiPreferences()]);
    if (this.queueWriteError) {
      await this.persistQueue();
      if (this.queueWriteError) throw this.queueWriteError;
    }
  }

  private _saveSettingsTimer: any = null;
  private pendingSongSave: (() => Promise<void>) | null = null;
  private songSettingsWrites: Promise<void> = Promise.resolve();

  async prepareSongRemoval(ids: string[]) {
    await Promise.all([this.queueWrites, this.flushSongSettings()]);
    if (this.queueWriteError) throw this.queueWriteError;
    if (this.activeSong && ids.includes(this.activeSong.id)) {
      await this.stop();
      this.activeSong = null;
    }
  }

  async prepareForUpdate() {
    this.persistSession(this.currentTimeSeconds, true);
    await Promise.all([this.stop(), this.queueWrites, this.flushSongSettings(), flushUiPreferences()]);
    if (this.queueWriteError) {
      await this.persistQueue();
      if (this.queueWriteError) throw this.queueWriteError;
    }
  }

  private flushSongSettings() {
    if (this._saveSettingsTimer) clearTimeout(this._saveSettingsTimer);
    this._saveSettingsTimer = null;
    const save = this.pendingSongSave;
    this.pendingSongSave = null;
    if (save) {
      this.songSettingsWrites = this.songSettingsWrites.then(save, save).catch(error => {
        this.pendingSongSave ??= save;
        throw error;
      });
    }
    return this.songSettingsWrites;
  }

  scheduleAutoSaveSongSettings() {
    const context = this.loadedSongContext ?? gameStore.songSettingsContext;
    if (!this.activeSong?.id || !context || this.isChangingSongContext) return;
    if (JSON.stringify(context) !== JSON.stringify(gameStore.songSettingsContext)) return;
    const song = this.activeSong;
    const scope = { kind: 'instrument' as const, ...context };
    if (this._saveSettingsTimer) {
      clearTimeout(this._saveSettingsTimer);
    }
    let transposeVal = 1;
    if (this.transposeMode === 'Ignore') transposeVal = 0;
    else if (this.transposeMode === 'Smart') transposeVal = 1;
    else if (this.transposeMode === 'Up') transposeVal = 2;
    else if (this.transposeMode === 'Down') transposeVal = 3;

    const keyChangesJson = song.keyMarkers && song.keyMarkers.length > 0
      ? JSON.stringify(song.keyMarkers.map(m => ({
          TimeMs: m.timeMs ?? Math.round((m.time ?? 0) * 1000),
          KeyOffset: m.keyOffset,
          Transpose: m.transpose ?? null,
          Label: m.label || m.noteDisplay || m.key
        })))
      : null;

    const disabledTracks = song.tracks
      ?.filter(t => t.isMuted || (t.index !== undefined && this.getTrackVolume(t.index) === 0))
      .map(t => t.index)
      .join(',') || null;

    const settings = {
      key: song.keyOffset ?? 0,
      baseKey: song.baseKey === null ? null : this.baseKeyOffset,
      transpose: transposeVal,
      disabledTracks,
      keyChanges: keyChangesJson,
      speed: this.playbackSpeed,
      holdNotes: this.holdNotes,
      tapThresholdMs: this.tapThresholdMs,
      autoChord: this.autoChord,
      enableKeyChangeMarkers: this.enableKeyChangeMarkers,
    };
    song.disabledTracks = disabledTracks;
    this.updateSongCopies(song);
    // Snapshot before the debounce: changing songs or instruments cannot redirect this write.
    const snapshot = structuredClone(settings);
    this.pendingSongSave = async () => {
      try {
        const saved = await api.songs.saveSongSettings(song.id, snapshot, scope);
        if (!saved) throw new Error('Song settings could not be saved.');
        console.log('[PlaybackState] Auto-saved song settings for:', song.title);
      } catch (e) {
        console.error('[PlaybackState] Failed to auto-save song settings:', e);
        throw e;
      }
    };
    this._saveSettingsTimer = setTimeout(() => {
      void this.flushSongSettings().catch(() => {});
    }, 400);
  }

  loadSong(song: MidiFile) {
    if (this.isChangingSongContext) return;
    void this.flushSongSettings().catch(error => notificationCenter.error('Unable to save song settings', String(error)));
    this.loadedSongContext = gameStore.songSettingsContext;
    this.cancelSongFinish();
    this.nextAfterRemovalId = null;
    this.removedActiveSongId = null;
    this.cancelSeekAnim();
    this.selectedMarkerId = null;
    const previousSong = this.activeSong;
    this.activeSong = loadedMidiToMidiFile(song);
    if (!this.activeSong.notes.length && previousSong?.id === song.id && previousSong.filePath === song.filePath) {
      this.applySongNotes(previousSong);
    }
    this.applyTrackSettings(this.activeSong);
    this.currentTimeSeconds = 0;
    this.activeNotes = [];
    this.trackVolumes = {};

    if (this.activeSong?.tracks) {
      const total = this.activeSong.tracks.length;
      for (let i = 0; i < total; i++) {
        const t = this.activeSong.tracks[i];
        if (!t.color) {
          t.color = getTrackColor(t.index ?? i, total);
        }
      }
    }

    if (song.baseKeyOffset !== undefined) {
      this.baseKeyOffset = song.baseKeyOffset;
    } else if (song.baseKey !== undefined) {
      this.baseKeyOffset = song.baseKey ?? 0;
    }
    if (song.transposeMode !== undefined) {
      this.transposeMode = song.transposeMode;
    }
    if (song.speed !== undefined) {
      this.playbackSpeed = song.speed;
    }
    if (song.holdNotes !== undefined) {
      this.holdNotes = song.holdNotes;
    }
    this.tapThresholdMs = song.tapThresholdMs ?? 0;
    void this.syncNoteTiming();
    if (song.autoChord !== undefined) {
      this.autoChord = song.autoChord;
    }
    if (song.enableKeyChangeMarkers !== undefined) {
      this.enableKeyChangeMarkers = song.enableKeyChangeMarkers;
    }
    if (song.keyMarkers && song.keyMarkers.length > 0) {
      this.activeSong.keyMarkers = [...song.keyMarkers];
    } else {
      this.activeSong.keyMarkers = [];
    }
    api.playback.setKeyMarkers(this.activeSong.keyMarkers);
    api.playback.setKeyMarkersEnabled(this.enableKeyChangeMarkers);
    void this.loadSongNotes();
  }

  private applySongNotes(parsed: MidiFile) {
    const song = this.activeSong;
    if (!song) return;
    const previousTracks = new Map(song.tracks.map(track => [track.index, track]));
    song.tracks = parsed.tracks.map((track, index) => ({
      ...track,
      color: previousTracks.get(track.index)?.color || track.color || getTrackColor(track.index ?? index, parsed.tracks.length),
      isMuted: previousTracks.get(track.index)?.isMuted ?? track.isMuted,
      muted: previousTracks.get(track.index)?.muted ?? track.muted,
    }));
    this.applyTrackSettings(song);
    song.notes = parsed.notes;
    song.durationSeconds = parsed.durationSeconds;
    song.durationMs = parsed.durationMs;
    song.nativeBpm = parsed.nativeBpm ?? parsed.bpm;
    song.bpm = song.bpmOverride ?? song.nativeBpm;
  }

  private async loadSongNotes() {
    const song = this.activeSong;
    if (!isTauri || !song?.filePath || song.isMissing || song.notes.length || song.tracks.some(track => track.notes?.length)) return;
    try {
      const parsed = await api.songs.readMidiFile(song.filePath);
      // A slower read for a previous selection must not replace the current song.
      if (this.activeSong !== song) return;
      this.applySongNotes(parsed);
    } catch (error) {
      if (this.activeSong === song) notificationCenter.error('Unable to load song notes', String(error));
    }
  }

  async setPlaybackSpeed(speed: number) {
    this.playbackSpeed = speed;
    if (this.activeSong) {
      this.activeSong.speed = speed;
    }
    await api.playback.setSpeed(this.enginePlaybackSpeed);
    this.scheduleAutoSaveSongSettings();
  }

  async increaseSpeed(step = 0.1) {
    const newSpeed = Math.round(Math.min(4.0, this.playbackSpeed + step) * 10) / 10;
    await this.setPlaybackSpeed(newSpeed);
    notificationCenter.info('Playback Speed', `${newSpeed.toFixed(1)}x`);
  }

  async decreaseSpeed(step = 0.1) {
    const newSpeed = Math.round(Math.max(0.1, this.playbackSpeed - step) * 10) / 10;
    await this.setPlaybackSpeed(newSpeed);
    notificationCenter.info('Playback Speed', `${newSpeed.toFixed(1)}x`);
  }

  setHoldNotes(val: boolean) {
    this.holdNotes = val;
    void this.syncNoteTiming();
    if (this.activeSong) {
      this.activeSong.holdNotes = val;
    }
    this.scheduleAutoSaveSongSettings();
  }

  private syncNoteTiming() {
    return api.playback.setNoteTiming(this.holdNotes, this.tapThresholdMs)
      .catch(error => notificationCenter.error('Unable to update note timing', String(error)));
  }

  setTapThresholdMs(value: number) {
    if (!Number.isInteger(value) || value < 0 || value > 10_000) return;
    this.tapThresholdMs = value;
    if (this.activeSong) this.activeSong.tapThresholdMs = value;
    void this.syncNoteTiming();
    this.scheduleAutoSaveSongSettings();
  }

  setAutoChord(val: boolean) {
    this.autoChord = val;
    if (this.activeSong) {
      this.activeSong.autoChord = val;
    }
    this.scheduleAutoSaveSongSettings();
  }

  setKeyChangeMarkersEnabled(val: boolean) {
    this.enableKeyChangeMarkers = val;
    if (this.activeSong) {
      this.activeSong.enableKeyChangeMarkers = val;
    }
    api.playback.setKeyMarkersEnabled(val);
    this.scheduleAutoSaveSongSettings();
  }

  setBaseKeyOffset(offset: number) {
    this.baseKeyOffset = offset;
    if (this.activeSong) {
      this.activeSong.baseKeyOffset = offset;
      this.activeSong.baseKey = offset;
    }
    void this.syncKeyOffset();
    this.scheduleAutoSaveSongSettings();
  }

  setKeyOffset(offset: number) {
    if (!this.activeSong) return;
    this.activeSong.keyOffset = offset;
    void this.syncKeyOffset();
    this.scheduleAutoSaveSongSettings();
  }

  syncKeyOffset() {
    return api.playback.setKeyOffset(this.effectiveKeyOffset);
  }

  setTransposeMode(mode: TransposeMode) {
    this.transposeMode = mode;
    if (this.activeSong) {
      this.activeSong.transposeMode = mode;
    }
    api.playback.setTransposeMode(mode);
    void this.syncKeyOffset();
    this.scheduleAutoSaveSongSettings();
  }

  async setTrackMuted(trackIndex: number, muted: boolean) {
    if (this.activeSong?.tracks) {
      const track = this.activeSong.tracks.find(t => t.index === trackIndex);
      if (track) {
        track.isMuted = muted;
        track.muted = muted;
      }
    }
    // If enabling a track whose volume is currently 0, restore volume to 1.0
    if (!muted && this.getTrackVolume(trackIndex) === 0) {
      this.trackVolumes[trackIndex] = 1.0;
      await api.playback.setTrackVolume(trackIndex, 1.0);
    }
    await api.playback.setTrackMuted(trackIndex, muted);
    this.scheduleAutoSaveSongSettings();
  }

  async setVolume(volume: number) {
    this.volume = Math.max(0, Math.min(2, volume));
    await api.playback.setVolume(Math.round(this.volume * 100));
  }


  async setListenMode(enabled: boolean) {
    if (this.isListenMode === enabled) return;
    this.isListenMode = enabled;
    await api.playback.setListenMode(this.isListenMode);
  }

  async toggleListenMode() {
    const target = !this.isListenMode;

    // If user turns Listen Mode off while currently playing and no game is running,
    // pause immediately so auto-enable doesn't flip it back on mid-playback.
    if (!target && this.isPlaying) {
      const isGameRunning = gameStore.activeGame?.isRunning ?? false;
      if (!isGameRunning) {
        await this.setListenMode(false);
        await this.pause();
        const gameName = gameStore.activeGame?.displayName || 'Game';
        notificationCenter.info(
          'Playback paused',
          `${gameName} is not running.`
        );
        return;
      }
    }

    await this.setListenMode(target);
    notificationCenter.info(
      this.isListenMode ? 'Listen Mode Enabled' : 'Game Mode Enabled',
      this.isListenMode ? 'Synthesizer audio active (keystrokes muted)' : 'Keystroke simulation active (synth muted)'
    );

    if (!target && this.isPlaying) {
      const isGameRunning = gameStore.activeGame?.isRunning ?? false;
      if (isGameRunning) {
        await gameStore.focusActiveGame();
      }
    }
  }

  private _lastActiveGameRunning: boolean | null = null;
  private _lastActiveGameFocused: boolean | null = null;

  handleGameStatusUpdate(activeGame: GameInfo) {
    const isRunning = activeGame?.isRunning ?? false;
    const isFocused = activeGame?.isFocused ?? false;
    const gameName = activeGame?.displayName || 'Game';

    // Mid-playback checks when playing in Game Mode (keystroke simulation):
    if (this.isPlaying && !this.isListenMode) {
      if (this._lastActiveGameRunning === true && !isRunning) {
        this.pause();
        notificationCenter.info('Playback paused', `${gameName} is not running.`);
      } else if (this._lastActiveGameFocused === true && !isFocused) {
        this.pause();
        notificationCenter.info('Playback paused', `${gameName} lost focus.`);
      }
    }

    this._lastActiveGameRunning = isRunning;
    this._lastActiveGameFocused = isFocused;
  }

  async setSynthMode(mode: 'adapted' | 'raw') {
    this.synthMode = mode;
    await api.playback.setSynthMode(mode);
    notificationCenter.info(
      'Synthesizer Mode',
      mode === 'adapted' ? 'Game Preview (Transposed & Snapped)' : 'Full Chromatic (Raw MIDI)'
    );
  }

  async setTrackVolume(trackIndex: number, volume: number) {
    const clamped = Math.max(0, Math.min(1, volume));
    this.trackVolumes[trackIndex] = clamped;
    await api.playback.setTrackVolume(trackIndex, clamped);

    if (clamped === 0) {
      if (this.activeSong?.tracks) {
        const track = this.activeSong.tracks.find(t => t.index === trackIndex);
        if (track) {
          track.isMuted = true;
          track.muted = true;
        }
      }
      await api.playback.setTrackMuted(trackIndex, true);
      this.scheduleAutoSaveSongSettings();
    } else {
      // If volume is raised above 0, re-enable the track if it was disabled/muted
      if (this.activeSong?.tracks) {
        const track = this.activeSong.tracks.find(t => t.index === trackIndex);
        if (track && (track.isMuted || track.muted)) {
          track.isMuted = false;
          track.muted = false;
          await api.playback.setTrackMuted(trackIndex, false);
          this.scheduleAutoSaveSongSettings();
        }
      }
    }
  }

  getTrackVolume(trackIndex: number): number {
    return this.trackVolumes[trackIndex] ?? 1.0;
  }

  isTrackMuted(trackIndex: number): boolean {
    if (this.trackVolumes[trackIndex] === 0) return true;
    if (this.activeSong?.tracks) {
      const track = this.activeSong.tracks.find(t => t.index === trackIndex || t.id === trackIndex + 1 || t.id === trackIndex);
      if (track) return Boolean(track.muted ?? track.isMuted ?? false);
    }
    return false;
  }

  async loadCustomSoundFont(path: string) {
    await api.playback.loadCustomSoundFont(path);
    this.customSoundFontName = path.split(/[/\\]/).pop() || path;
    notificationCenter.success('SoundFont Loaded', `Active: ${this.customSoundFontName}`);
  }

  async resetSoundFont() {
    await api.playback.resetSoundFont();
    this.customSoundFontName = 'Default (General MIDI)';
    notificationCenter.info('SoundFont Reset', 'Default General MIDI SoundFont restored');
  }


  async loadQueueFromDb() {
    const readId = ++this.queueReadId;
    const revision = this.queueRevision;
    try {
      await this.queueWrites;
      const dbQueue = await api.queue.getQueue();
      if (readId === this.queueReadId && revision === this.queueRevision && Array.isArray(dbQueue)) {
        this.queue = dbQueue;
        this.syncShuffledQueue();
        this.syncQueueIndex();
      }
    } catch (e) {
      console.warn('[PlaybackState] Failed to load queue from DB:', e);
    }
  }

  private get playbackQueue(): MidiFile[] {
    return this.isShuffled ? this.shuffledQueue : this.queue;
  }

  private syncQueueIndex() {
    this.activeQueueIndex = this.playbackQueue.findIndex(s => s.id === this.activeSong?.id);
    if (this.activeQueueIndex === -1 && this.removedActiveSongId === this.activeSong?.id) {
      const next = this.playbackQueue.findIndex(s => s.id === this.nextAfterRemovalId);
      this.activeQueueIndex = next === -1 ? this.playbackQueue.length - 1 : next - 1;
    }
  }

  private cancelSongFinish() {
    if (this._songFinishTimer) clearTimeout(this._songFinishTimer);
    this._songFinishTimer = null;
  }

  private persistQueue() {
    const ids = this.queue.map(s => s.id);
    ++this.queueRevision;
    this.queueWrites = this.queueWrites.then(async () => {
      if (!await api.queue.setQueue(ids)) throw new Error('Queue could not be saved.');
      this.queueWriteError = null;
    }).catch(error => {
      this.queueWriteError = error;
      console.error('[PlaybackStore] Queue save failed:', error);
      notificationCenter.error('Unable to save queue', String(error));
    });
    return this.queueWrites;
  }

  setQueueList(songs: MidiFile[], startIndex = 0, playlistId: string | null = null) {
    this.queue = songs.filter((song, index) => songs.findIndex(s => s.id === song.id) === index);
    this.queuePlaylistId = playlistId;
    this.nextAfterRemovalId = null;
    this.removedActiveSongId = null;
    this.historyStack = [];
    this.updateShuffledQueue(this.queue[startIndex]?.id);
    this.syncQueueIndex();
    return this.persistQueue();
  }

  async playPlaylist(songs: MidiFile[], playlistId: string) {
    if (songs.length === 0) return;
    if (this.queuePlaylistId === playlistId && songs.some(s => s.id === this.activeSong?.id)
      && this.queue.some(s => s.id === this.activeSong?.id)) {
      await this.togglePlay();
      return;
    }
    await this.setQueueList(songs, 0, playlistId);
    await this.play(songs[0]);
  }

  async play(song?: MidiFile) {
    if (this.isChangingSongContext) return;
    if (gameStore.hasDeletedSelection) {
      notificationCenter.warning('Playback unavailable', gameStore.layoutDeletionTooltip);
      return;
    }
    const targetSong = song || this.activeSong || this.playbackQueue[0];
    if (!targetSong) return;
    this.cancelSongFinish();

    // Check game running state if not in listen mode (matching WPF StartPlayback / HandleGameNotRunning)
    if (!this.isListenMode) {
      const isGameRunning = gameStore.activeGame?.isRunning ?? false;
      if (!isGameRunning) {
        const gameName = gameStore.activeGame?.displayName || 'Game';
        if (settingsStore.settings.autoEnableListenMode) {
          await this.setListenMode(true);
          notificationCenter.warning(
            'Game not running',
            `${gameName} is not running.\nEnabled Listen Mode.`
          );
        } else {
          notificationCenter.warning(
            'Game not running',
            `${gameName} is not running.`
          );
          return;
        }
      } else {
        await gameStore.focusActiveGame();
      }
    }

    const isNewSong = !this.activeSong || this.activeSong.id !== targetSong.id;
    const isPathLoaded = this._currentLoadedPath === targetSong.filePath;

    if (song || isNewSong) {
      this.cancelSeekAnim();
      this.loadSong(targetSong);
    }
    // A song play adds only that song. Playlist play explicitly replaces the queue.
    if (!this.queue.some(s => s.id === targetSong.id)) {
      this.queuePlaylistId = null;
      await this.addToQueue(targetSong);
    }
    this.syncQueueIndex();

    if (targetSong.filePath && isTauri) {
      if (isNewSong || !isPathLoaded) {
        try {
          await api.playback.loadMidiFile(targetSong.filePath);
          this._currentLoadedPath = targetSong.filePath;
          // Loading clears the engine's markers; apply this song's settings afterwards.
          await api.playback.setKeyMarkers(this.activeSong?.keyMarkers ?? []);
          await api.playback.setKeyMarkersEnabled(this.enableKeyChangeMarkers);
          await this.syncNoteTiming();
          await api.playback.setSpeed(this.enginePlaybackSpeed);
          await this.syncKeyOffset();
          await api.playback.setTransposeMode(this.transposeMode);
          await this.syncTrackSettings();
          // Studio can seek before the engine loads this song. Keep that position on Play.
          if (isNewSong) {
            this.currentTimeSeconds = 0;
          } else if (this.currentTimeSeconds > 0) {
            await this.seek(this.currentTimeSeconds);
          }
        } catch (e) {
          notificationCenter.error('Unable to load song', String(e));
          return;
        }
      }
    }

    if (song && !isNewSong && isPathLoaded) await this.seek(0);

    this.status = 'Playing';
    await api.playback.play(targetSong.id);
  }

  async pause() {
    this.cancelSongFinish();
    this.status = 'Paused';
    await api.playback.pause();
  }

  async togglePlay() {
    if (this.isPlaying) {
      await this.pause();
    } else {
      await this.play();
    }
  }

  async stop() {
    this.cancelSongFinish();
    this.cancelSeekAnim();
    this.status = 'Stopped';
    this.currentTimeSeconds = 0;
    this.activeNotes = [];
    await api.playback.stop();
  }

  async seek(seconds: number, _options?: { immediate?: boolean }) {
    const target = Math.max(0, Math.min(this.durationSeconds, seconds));
    this.currentTimeSeconds = target;
    const id = this.positionGate.requestSeek();
    // A preview has notes without an engine song. An empty engine would acknowledge
    // zero (or a different song's position), overwriting the Studio playhead.
    if (isTauri && this._currentLoadedPath !== this.activeSong?.filePath) return;
    this.pendingSeek = { seconds: target, id };
    const completion = new Promise<void>(resolve => this.pendingSeekCompletions.push(resolve));
    // Collapse bursts of pointer events into one native seek per frame.
    if (!this.seekTimer) {
      this.seekTimer = setTimeout(() => {
        this.seekTimer = null;
        const request = this.pendingSeek;
        const completions = this.pendingSeekCompletions.splice(0);
        this.pendingSeek = null;
        if (!request) {
          completions.forEach(complete => complete());
          return;
        }
        api.playback.seek(Math.round(request.seconds * 1000), request.id).catch(error => {
          this.positionGate.cancelRequest(request.id);
          console.error('[PlaybackStore] Seek failed:', error);
        }).finally(() => completions.forEach(complete => complete()));
      }, 16);
    }
    return completion;
  }

  getAnimatedTime(_isDragging = false): number {
    // Seeking is direct manipulation, not a transition between two positions.
    return this.currentTimeSeconds;
  }

  addToQueue(songs: MidiFile | MidiFile[]) {
    const incoming = Array.isArray(songs) ? songs : [songs];
    const ids = new Set(this.queue.map(s => s.id));
    const added = incoming.filter(song => {
      if (ids.has(song.id)) return false;
      ids.add(song.id);
      return true;
    });
    if (added.length === 0) return this.queueWrites;
    this.queue = [...this.queue, ...added];
    this.syncShuffledQueue();
    this.syncQueueIndex();
    return this.persistQueue();
  }

  playNext(songs: MidiFile | MidiFile[]) {
    const incoming = Array.isArray(songs) ? songs : [songs];
    const currentIsQueued = this.queue.some(s => s.id === this.activeSong?.id);
    const seen = new Set<string>();
    const next = incoming.filter(song => {
      if ((currentIsQueued && song.id === this.activeSong?.id) || seen.has(song.id)) return false;
      seen.add(song.id);
      return true;
    });
    if (next.length === 0) return this.queueWrites;
    const insertNext = (list: MidiFile[]) => {
      const filtered = list.filter(s => !seen.has(s.id));
      const index = filtered.findIndex(s => s.id === this.activeSong?.id);
      const successor = filtered.findIndex(s => s.id === this.nextAfterRemovalId);
      const insertIndex = index !== -1 ? index + 1
        : this.removedActiveSongId === this.activeSong?.id
          ? (successor === -1 ? filtered.length : successor) : 0;
      filtered.splice(insertIndex, 0, ...next);
      return filtered;
    };
    this.queue = insertNext(this.queue);
    if (this.isShuffled) this.shuffledQueue = insertNext(this.shuffledQueue);
    if (this.removedActiveSongId === this.activeSong?.id) this.nextAfterRemovalId = next[0].id;
    this.syncQueueIndex();
    return this.persistQueue();
  }

  removeFromQueue(index: number) {
    const removed = this.queue[index];
    if (!removed) return this.queueWrites;
    return this.removeMultipleFromQueue([removed.id]);
  }

  removeMultipleFromQueue(ids: string[]) {
    const idSet = new Set(ids);
    // If the playing song is removed, Next still visits its former successor.
    const oldList = this.playbackQueue;
    const previousIndex = oldList.findIndex(s => s.id === this.activeSong?.id);
    const remainingBefore = oldList.slice(0, Math.max(0, previousIndex)).filter(s => !idSet.has(s.id)).length;
    this.queue = this.queue.filter(s => !idSet.has(s.id));
    this.shuffledQueue = this.shuffledQueue.filter(s => !idSet.has(s.id));
    this.historyStack = this.historyStack.filter(s => !idSet.has(s.id));
    this.syncQueueIndex();
    if (previousIndex !== -1 && idSet.has(this.activeSong!.id)) {
      this.removedActiveSongId = this.activeSong!.id;
      this.nextAfterRemovalId = oldList.slice(previousIndex + 1).find(s => !idSet.has(s.id))?.id ?? null;
      this.activeQueueIndex = remainingBefore - 1;
    } else if (this.nextAfterRemovalId && idSet.has(this.nextAfterRemovalId)) {
      const nextIndex = oldList.findIndex(s => s.id === this.nextAfterRemovalId);
      this.nextAfterRemovalId = oldList.slice(nextIndex + 1).find(s => !idSet.has(s.id))?.id ?? null;
      this.syncQueueIndex();
    }
    return this.persistQueue();
  }

  clearQueue() {
    this.queue = [];
    this.shuffledQueue = [];
    this.historyStack = [];
    this.queuePlaylistId = null;
    this.nextAfterRemovalId = null;
    this.removedActiveSongId = null;
    this.activeQueueIndex = -1;
    return this.persistQueue();
  }

  reorderQueue(newQueue: MidiFile[]) {
    this.queue = [...newQueue];
    if (this.isShuffled) this.shuffledQueue = [...newQueue];
    this.syncQueueIndex();
    return this.persistQueue();
  }

  private syncShuffledQueue() {
    if (!this.isShuffled) return;
    const byId = new Map(this.queue.map(song => [song.id, song]));
    const retained = this.shuffledQueue.filter(song => byId.has(song.id)).map(song => byId.get(song.id)!);
    const retainedIds = new Set(retained.map(song => song.id));
    this.shuffledQueue = [...retained, ...this.queue.filter(song => !retainedIds.has(song.id))];
  }

  private updateShuffledQueue(firstId = this.activeSong?.id) {
    if (!this.isShuffled) {
      this.shuffledQueue = [];
      return;
    }
    const first = this.queue.find(song => song.id === firstId);
    const otherSongs = this.queue.filter(song => song.id !== firstId);
    for (let i = otherSongs.length - 1; i > 0; i--) {
      const j = Math.floor(Math.random() * (i + 1));
      [otherSongs[i], otherSongs[j]] = [otherSongs[j], otherSongs[i]];
    }
    this.shuffledQueue = first ? [first, ...otherSongs] : otherSongs;
  }

  toggleShuffle() {
    this.isShuffled = !this.isShuffled;
    this.updateShuffledQueue();
    this.syncQueueIndex();
    notificationCenter.info(
      this.isShuffled ? 'Shuffle On' : 'Shuffle Off',
      this.isShuffled ? 'Playing tracks in randomized order' : 'Restored original queue sequence'
    );
  }

  get enginePlaybackSpeed() {
    const nativeBpm = this.activeSong?.nativeBpm || this.activeSong?.bpm || 120;
    return Math.max(0.1, Math.min(5, this.playbackSpeed * ((this.activeSong?.bpmOverride ?? nativeBpm) / nativeBpm)));
  }

  cancelPendingSongSettingsSave() {
    if (this._saveSettingsTimer) clearTimeout(this._saveSettingsTimer);
    this._saveSettingsTimer = null;
    this.pendingSongSave = null;
  }

  async flushPendingSongSettings() {
    await this.flushSongSettings();
  }

  private applyTrackSettings(song: MidiFile) {
    if (song.disabledTracks === undefined) return;
    const disabled = new Set((song.disabledTracks ?? '').split(',').filter(Boolean).map(Number));
    song.tracks.forEach(track => {
      track.isMuted = track.muted = disabled.has(track.index);
    });
  }

  private async syncTrackSettings(resetVolumes = false) {
    for (const track of this.activeSong?.tracks ?? []) {
      if (resetVolumes) await api.playback.setTrackVolume(track.index, 1);
      await api.playback.setTrackMuted(track.index, track.isMuted);
    }
  }

  async refreshSongSettingsContext() {
    const revision = ++this.contextRevision;
    this.isChangingSongContext = true;
    const refresh = async () => {
      try {
        await this.flushSongSettings();
        if (revision !== this.contextRevision) return;
        await libraryStore.reload();
        await this.loadQueueFromDb();
        if (revision !== this.contextRevision) return;
        const song = this.activeSong ? await api.songs.getSongById(this.activeSong.id) : null;
        if (revision !== this.contextRevision) return;
        const songsById = new Map(libraryStore.songs.map(song => [song.id, song]));
        this.historyStack = this.historyStack.flatMap(song => songsById.has(song.id) ? [songsById.get(song.id)!] : []);
        if (song) await this.applyEditedSong(song, true);
        this.loadedSongContext = gameStore.songSettingsContext;
      } finally {
        if (revision === this.contextRevision) this.isChangingSongContext = false;
      }
    };
    const task = this.contextRefresh.then(refresh, refresh);
    this.contextRefresh = task.catch(() => {});
    await task;
  }

  async applyEditedSong(song: MidiFile, resetTrackVolumes = false) {
    this.updateSongCopies(song);
    const update = (entry: MidiFile) => {
      if (entry.id === song.id) {
        const { tracks, notes, ...details } = song;
        Object.assign(entry, details);
      }
    };
    if (this.activeSong?.id === song.id) {
      update(this.activeSong);
      this.baseKeyOffset = song.baseKeyOffset;
      this.transposeMode = song.transposeMode;
      this.playbackSpeed = song.speed;
      this.holdNotes = song.holdNotes;
      this.tapThresholdMs = song.tapThresholdMs ?? 0;
      await this.syncNoteTiming();
      this.autoChord = song.autoChord ?? false;
      this.enableKeyChangeMarkers = song.enableKeyChangeMarkers ?? true;
      if (resetTrackVolumes) this.trackVolumes = {};
      this.applyTrackSettings(this.activeSong);
      await api.playback.setKeyMarkers(this.activeSong.keyMarkers);
      await api.playback.setKeyMarkersEnabled(this.enableKeyChangeMarkers);
      await this.syncTrackSettings(resetTrackVolumes);
      await api.playback.setSpeed(this.enginePlaybackSpeed);
      await this.syncKeyOffset();
      await api.playback.setTransposeMode(this.transposeMode);
    }
  }

  private updateSongCopies(song: MidiFile) {
    const { tracks, notes, ...details } = song;
    const update = (entry: MidiFile) => { if (entry.id === song.id) Object.assign(entry, details); };
    libraryStore.songs.forEach(update);
    Object.values(libraryStore.songsByPlaylist).forEach(songs => songs.forEach(update));
    this.queue.forEach(update);
    this.shuffledQueue.forEach(update);
    this.historyStack.forEach(update);
  }

  cycleLoopMode() {
    if (this.loopMode === 'off') {
      this.loopMode = 'all';
      notificationCenter.info('Loop Mode: All', 'Repeat entire queue');
    } else if (this.loopMode === 'all') {
      this.loopMode = 'single';
      notificationCenter.info('Loop Mode: Single', 'Repeat current track');
    } else {
      this.loopMode = 'off';
      notificationCenter.info('Loop Mode: Off', 'Playback stops at end of queue');
    }
  }

  async handleSongFinished() {
    if (this.loopMode === 'single') {
      await this.seek(0);
      await this.play();
      return;
    }

    const currentList = this.playbackQueue;
    if (currentList.length === 0) {
      await this.stop();
      return;
    }

    if (this.loopMode === 'all') {
      await this.nextTrack(false);
      return;
    }

    // loopMode === 'off'
    if (this.activeQueueIndex >= currentList.length - 1) {
      await this.stop();
    } else {
      await this.nextTrack(false);
    }
  }

  async nextTrack(userInitiated = true) {
    const currentList = this.playbackQueue;
    if (currentList.length === 0) return;

    if (!userInitiated && this.loopMode === 'single') {
      await this.seek(0);
      await this.play();
      return;
    }

    let nextIndex = this.activeQueueIndex + 1;
    if (nextIndex >= currentList.length) {
      if (this.loopMode === 'all') {
        nextIndex = 0;
      } else {
        await this.stop();
        return;
      }
    }

    this.activeQueueIndex = nextIndex;
    const nextSong = currentList[this.activeQueueIndex];
    if (nextSong) {
      if (this.activeSong && this.activeSong.id !== nextSong.id) {
        this.historyStack.push(this.activeSong);
        if (this.historyStack.length > 50) this.historyStack.shift();
      }
      await this.play(nextSong);
    }
  }

  async prevTrack() {
    if (this.currentTimeSeconds > 3.0) {
      await this.seek(0);
      return;
    }

    if (this.historyStack.length > 0) {
      const prevSong = this.historyStack.pop()!;
      const currentList = this.playbackQueue;
      const foundIdx = currentList.findIndex(s => s.id === prevSong.id);
      if (foundIdx !== -1) {
        this.activeQueueIndex = foundIdx;
      }
      await this.play(prevSong);
      return;
    }

    const currentList = this.playbackQueue;
    if (currentList.length === 0) return;

    let prevIndex = this.activeQueueIndex - 1;
    if (prevIndex < 0) {
      prevIndex = this.loopMode === 'all' ? currentList.length - 1 : 0;
    }

    this.activeQueueIndex = prevIndex;
    const prevSong = currentList[this.activeQueueIndex];
    if (prevSong) {
      await this.play(prevSong);
    }
  }

  // Key Marker Methods
  selectMarker(marker: KeyChangeMarker | null) {
    if (!marker) {
      this.selectedMarkerId = null;
      return;
    }
    this.selectedMarkerId = marker.id;
    this.selectedKeyOffset = marker.keyOffset;
  }

  addKeyMarker(timeSeconds?: number, keyOffset?: number) {
    if (!this.activeSong) return;

    let targetTime = timeSeconds ?? this.currentTimeSeconds;
    const offset = keyOffset ?? this.selectedKeyOffset;

    // Snap to nearest note start within 150ms if available
    if (this.activeSong.notes && this.activeSong.notes.length > 0) {
      const nearestNote = this.activeSong.notes
        .filter(n => Math.abs((n.startTime ?? (n.startMs / 1000)) - targetTime) <= 0.15)
        .sort((a, b) => {
          const distA = Math.abs((a.startTime ?? (a.startMs / 1000)) - targetTime);
          const distB = Math.abs((b.startTime ?? (b.startMs / 1000)) - targetTime);
          return distA - distB;
        })[0];
      if (nearestNote) {
        targetTime = nearestNote.startTime ?? (nearestNote.startMs / 1000);
      }
    }

    targetTime = Math.max(0, Number(targetTime.toFixed(2)));

    // Remove any existing marker within 300ms
    const markers = (this.activeSong.keyMarkers || []).filter(
      m => Math.abs((m.time ?? (m.timeMs / 1000)) - targetTime) >= 0.3
    );

    const noteName = getKeyNoteName(offset);
    const offsetDisplay = formatSignedOffset(offset);

    const newMarker: KeyChangeMarker = {
      id: `km-${Date.now()}-${Math.random().toString(36).substring(2, 6)}`,
      timeMs: Math.round(targetTime * 1000),
      time: targetTime,
      key: noteName,
      keyOffset: offset,
      noteDisplay: noteName,
      offsetDisplay
    };

    markers.push(newMarker);
    markers.sort((a, b) => a.timeMs - b.timeMs);

    this.activeSong.keyMarkers = markers;
    this.activeSong.keyChanges = markers.map(m => ({
      id: m.id,
      time: m.time ?? (m.timeMs / 1000),
      key: m.noteDisplay || m.key || 'C'
    }));

    this.selectedMarkerId = newMarker.id;
    this.selectedKeyOffset = offset;

    api.playback.setKeyMarkers(this.activeSong.keyMarkers);
    notificationCenter.success('Key Offset Set', `Set ${noteName} (${offsetDisplay}) at ${targetTime.toFixed(1)}s`);
    this.scheduleAutoSaveSongSettings();
  }

  updateKeyMarker(markerId: string, keyOffset: number) {
    if (!this.activeSong || !this.activeSong.keyMarkers) return;
    const marker = this.activeSong.keyMarkers.find(m => m.id === markerId);
    if (!marker) return;

    const noteName = getKeyNoteName(keyOffset);
    const offsetDisplay = formatSignedOffset(keyOffset);

    marker.keyOffset = keyOffset;
    marker.key = noteName;
    marker.noteDisplay = noteName;
    marker.offsetDisplay = offsetDisplay;

    // Sync with keyChanges
    if (this.activeSong.keyChanges) {
      const kc = this.activeSong.keyChanges.find(k => k.id === markerId);
      if (kc) kc.key = noteName;
    }

    api.playback.setKeyMarkers(this.activeSong.keyMarkers);
    notificationCenter.success('Key Offset Updated', `Updated marker at ${(marker.time ?? (marker.timeMs / 1000)).toFixed(1)}s to ${noteName} (${offsetDisplay})`);
    this.scheduleAutoSaveSongSettings();
  }

  deleteKeyMarker(markerId: string) {
    if (!this.activeSong || !this.activeSong.keyMarkers) return;
    const marker = this.activeSong.keyMarkers.find(m => m.id === markerId);
    const timeStr = marker ? `${(marker.time ?? (marker.timeMs / 1000)).toFixed(1)}s` : '';

    this.activeSong.keyMarkers = this.activeSong.keyMarkers.filter(m => m.id !== markerId);
    if (this.activeSong.keyChanges) {
      this.activeSong.keyChanges = this.activeSong.keyChanges.filter(k => k.id !== markerId);
    }

    if (this.selectedMarkerId === markerId) {
      this.selectedMarkerId = null;
    }

    api.playback.setKeyMarkers(this.activeSong.keyMarkers);
    notificationCenter.info('Marker Removed', timeStr ? `Removed key offset marker at ${timeStr}` : 'Removed key offset marker');
    this.scheduleAutoSaveSongSettings();
  }

  deleteSelectedMarker() {
    if (this.selectedMarkerId) {
      this.deleteKeyMarker(this.selectedMarkerId);
    }
  }

  navigateMarker(direction: 'prev' | 'next') {
    if (!this.activeSong?.keyMarkers || this.activeSong.keyMarkers.length === 0) return;
    const markers = [...this.activeSong.keyMarkers].sort((a, b) => a.timeMs - b.timeMs);
    const current = this.currentTimeSeconds;

    let target: KeyChangeMarker | undefined;
    if (direction === 'prev') {
      const prevs = markers.filter(m => (m.time ?? (m.timeMs / 1000)) < current - 0.1);
      target = prevs.length > 0 ? prevs[prevs.length - 1] : markers[markers.length - 1];
    } else {
      const nexts = markers.filter(m => (m.time ?? (m.timeMs / 1000)) > current + 0.1);
      target = nexts.length > 0 ? nexts[0] : markers[0];
    }

    if (target) {
      const time = target.time ?? (target.timeMs / 1000);
      this.seek(time);
      this.selectedMarkerId = target.id;
      this.selectedKeyOffset = target.keyOffset;
    }
  }

  async autoDetectKeyChanges() {
    if (!this.activeSong) return;
    try {
      const detected = await api.playback.detectKeySignatures(this.activeSong.filePath);
      if (detected && detected.length > 0) {
        const markers: KeyChangeMarker[] = detected.map((m: any, idx: number) => {
          const offset = m.keyOffset ?? 0;
          const noteName = m.label || getKeyNoteName(offset);
          const timeSec = (m.timeMs ?? 0) / 1000;
          return {
            id: `km-${Date.now()}-${idx}`,
            timeMs: m.timeMs ?? 0,
            time: timeSec,
            key: noteName,
            keyOffset: offset,
            transpose: m.transpose,
            noteDisplay: noteName,
            offsetDisplay: formatSignedOffset(offset),
          };
        });

        this.activeSong.keyMarkers = markers;
        this.activeSong.keyChanges = markers.map(m => ({
          id: m.id,
          time: m.time ?? (m.timeMs / 1000),
          key: m.noteDisplay || m.key || 'C'
        }));

        await api.playback.setKeyMarkers(this.activeSong.keyMarkers);
        this.scheduleAutoSaveSongSettings();
        notificationCenter.success('Scan Keys Complete', `Detected ${markers.length} key change${markers.length === 1 ? '' : 's'}.`);
      } else {
        notificationCenter.info('Scan Keys', 'No key signature changes found in song.');
      }
    } catch (e) {
      console.error('[PlaybackState] autoDetectKeyChanges error:', e);
      notificationCenter.error('Scan Failed', 'Failed to scan key changes');
    }
  }

}

export const playbackStore = new PlaybackStore();

if (typeof window !== 'undefined') {
  (window as any).playbackStore = playbackStore;
}
