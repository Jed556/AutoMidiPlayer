import type { GameInfo, GameStatus, GameConfigState, InstrumentConfig, InstrumentInputMode } from '../../../shared/types/games.js';
import type { SongSettingsContext } from '../../../shared/types/ipc.js';
import type { PedalBindings } from '../../../shared/types/settings.js';
import { instrumentKeyPairs, instrumentLayoutIds } from '../../../shared/utils/instrumentLayout.js';
import { api } from '../backend/index.js';
import { notificationCenter } from '../ui/notificationCenter.svelte';

class GameStore {
  games = $state<GameInfo[]>([]);
  activeGame = $state<GameInfo | null>(null);
  selectedInstrumentIds = $state<Record<string, string>>({});
  private selectedLayoutIds = $state<Record<string, string>>({});
  private configRefresh: Promise<void> | null = null;
  private configDirty = false;
  private mappingReady: Promise<boolean> = Promise.resolve(false);
  private reportedIssues = new Set<string>();
  private settingsContextListeners: (() => void)[] = [];
  private contextKey = '';
  private statusRevision = 0;
  private latestStatuses: GameStatus[] = [];

  get activeInstrument() {
    return this.getGameInstrument(this.activeGame);
  }

  getGameInstrument(game: GameInfo | null) {
    if (game?.isMissing) return null;
    const selected = game && (this.selectedInstrumentIds[game.id] ?? game.defaultInstrument);
    const match = game?.instruments?.find(i => i.id === selected || i.name === selected);
    return match ?? (game && this.selectedInstrumentIds[game.id] ? null : game?.instruments?.[0] ?? null);
  }

  get activeLayout() {
    return this.getGameLayout(this.activeGame);
  }

  get activeInputMode(): InstrumentInputMode { return this.getInputMode(this.activeGame); }

  getInputMode(game: GameInfo | null, instrument = this.getGameInstrument(game)): InstrumentInputMode {
    if (!game || !instrument) return 'keyboard';
    const key = `${game.id}/${instrument.id}`;
    const saved = this.selectedLayoutIds[`${key}/mode`] as InstrumentInputMode | undefined;
    if (saved && instrumentLayoutIds(instrument, saved).length) return saved;
    const layout = this.selectedLayoutIds[key];
    return (['keyboard', 'chords', 'frets'] as const).find(mode =>
      layout && instrumentLayoutIds(instrument, mode).includes(layout))
      ?? (['keyboard', 'chords', 'frets'] as const).find(mode => instrumentLayoutIds(instrument, mode).length) ?? 'keyboard';
  }

  async setInputMode(mode: InstrumentInputMode) {
    const game = this.activeGame;
    const instrument = this.activeInstrument;
    if (!game || !instrument || !instrumentLayoutIds(instrument, mode).length || mode === this.activeInputMode) return;
    const previous = this.activeInputMode;
    if (game.isRunning && previous !== 'keyboard' && mode !== 'keyboard') {
      if (!await api.playback.setGuitarMode(mode)) throw new Error('Unable to change the game guitar mode.');
    }
    this.selectedLayoutIds[`${game.id}/${instrument.id}/mode`] = mode;
    await Promise.all([
      this.syncActiveInstrumentMapping(),
      api.settings.updateSettings({ selectedLayoutIds: { ...this.selectedLayoutIds } }),
    ]);
  }

  getGameLayout(game: GameInfo | null, instrument = this.getGameInstrument(game)) {
    if (!game || !instrument) return null;
    const mode = this.getInputMode(game, instrument);
    const layouts = this.getInstrumentLayouts(instrument, game, mode);
    const key = `${game.id}/${instrument.id}`;
    const selected = this.selectedLayoutIds[`${key}/${mode}`] ??
      (instrumentLayoutIds(instrument, mode).includes(this.selectedLayoutIds[key]) ? this.selectedLayoutIds[key] : undefined);
    if (selected) return layouts.find(layout => layout.id === selected) ?? null;
    return layouts.find(layout => layout.id === game.defaultLayout || layout.name === game.defaultLayout) ?? layouts[0] ?? null;
  }

  get isInstrumentMissing() {
    const game = this.activeGame;
    return Boolean(game?.isMissing || (game && this.selectedInstrumentIds[game.id] && !this.activeInstrument));
  }

  get isLayoutMissing() {
    const game = this.activeGame;
    const instrument = this.activeInstrument;
    return this.isInstrumentMissing || Boolean(game && instrument
      && instrumentLayoutIds(instrument, this.activeInputMode).length && !this.activeLayout);
  }

  get hasDeletedSelection() {
    return Boolean(this.activeGame?.isMissing || this.isInstrumentMissing || this.isLayoutMissing);
  }

  get gameDeletionTooltip() {
    return this.activeGame?.isMissing ? 'The entry for this game has been deleted.' : '';
  }

  get instrumentDeletionTooltip() {
    return this.gameDeletionTooltip || (this.isInstrumentMissing ? 'The entry for this instrument has been deleted.' : '');
  }

  get layoutDeletionTooltip() {
    return this.instrumentDeletionTooltip || (this.isLayoutMissing ? 'The entry for this keyboard layout has been deleted.' : '');
  }

  get songSettingsContext(): SongSettingsContext | null {
    return this.activeGame && this.activeInstrument
      ? { gameId: this.activeGame.id, instrumentId: this.activeInstrument.id } : null;
  }

  onSettingsContextChange(listener: () => void) {
    this.settingsContextListeners.push(listener);
    return () => { this.settingsContextListeners = this.settingsContextListeners.filter(l => l !== listener); };
  }

  private publishSettingsContext() {
    const context = this.songSettingsContext;
    const contextKey = JSON.stringify(context);
    if (this.contextKey === contextKey) return;
    this.contextKey = contextKey;
    api.songs.setSettingsContext(context);
    this.settingsContextListeners.forEach(listener => listener());
  }
  private statusListeners: ((activeGame: GameInfo, statuses: GameStatus[]) => void)[] = [];

  onGameStatusChange(listener: (activeGame: GameInfo, statuses: GameStatus[]) => void) {
    this.statusListeners.push(listener);
    try {
      if (this.activeGame) listener(this.activeGame, []);
    } catch (e) {
      console.error('[GameState] Initial status listener invocation failed:', e);
    }
    return () => {
      this.statusListeners = this.statusListeners.filter(l => l !== listener);
    };
  }

  private notifyStatusListeners(statuses: GameStatus[] = []) {
    if (!this.activeGame || this.activeGame.isMissing) return;
    for (const listener of this.statusListeners) {
      try {
        listener(this.activeGame, statuses);
      } catch (e) {
        console.error('[GameState] Status listener error:', e);
      }
    }
  }

  async init() {
    try {
      const settings = await api.settings.getSettings();
      this.selectedInstrumentIds = settings.selectedInstrumentIds ?? {};
      this.selectedLayoutIds = settings.selectedLayoutIds ?? {};
      await api.games.onGameStatusChanged((statuses) => {
        this.statusRevision++;
        this.applyGameStatuses(statuses);
      });
      // Finish subscribing before reading the first snapshot so startup changes cannot be lost.
      await api.games.onConfigsChanged(() => { void this.refreshConfigs().catch(console.error); });
      await this.refreshConfigs();
    } catch (e) {
      console.warn('[GameState] Failed to load games from API:', e);
    }
  }

  private applyGameStatuses(statuses: GameStatus[]) {
    this.latestStatuses = statuses;
    const byId = new Map(statuses.map(status => [status.id, status]));
    for (const game of this.games) {
      const status = byId.get(game.id);
      game.isRunning = status?.isRunning ?? false;
      game.isFocused = status?.isFocused ?? false;
      game.pid = status?.pid ?? undefined;
      game.requiresManualFocus = status?.requiresManualFocus ?? false;
    }
    // Sync activeGame
    const activeGame = this.activeGame;
    const activeMatch = activeGame && byId.get(activeGame.id);
    if (activeGame && !activeGame.isMissing) {
      activeGame.isRunning = activeMatch?.isRunning ?? false;
      activeGame.isFocused = activeMatch?.isFocused ?? false;
      activeGame.pid = activeMatch?.pid ?? undefined;
      activeGame.requiresManualFocus = activeMatch?.requiresManualFocus ?? false;
    }
    this.notifyStatusListeners(statuses);
  }

  async focusActiveGame(): Promise<boolean> {
    if (!this.activeGame) return false;
    const pid = this.activeGame.pid;
    if (pid && typeof pid === 'number') {
      return await api.games.focusGame(pid);
    }
    return false;
  }

  setActiveGame(gameId: string) {
    const game = this.games.find(g => g.id === gameId);
    if (game) {
      this.activeGame = game;
      for (const g of this.games) {
        g.isSelected = g.id === gameId;
      }
      api.games.setActiveGame(gameId).catch(err => {
        console.warn('[GameState] Failed to set active game on backend:', err);
      });
      this.syncActiveInstrumentMapping();
      this.notifyStatusListeners();
    }
  }

  async reloadConfigs(): Promise<boolean> {
    try {
      const snapshot = await api.games.reloadConfigs();
      await this.refreshConfigs();
      return snapshot.issues.length === 0;
    } catch (e) {
      console.error('[GameState] reloadConfigs failed:', e);
      throw e;
    }
  }

  async resetConfigs(): Promise<boolean> {
    const snapshot = await api.games.resetConfigs();
    await this.refreshConfigs();
    return snapshot.issues.length === 0;
  }

  refreshConfigs(): Promise<void> {
    this.configDirty = true;
    if (!this.configRefresh) {
      this.configRefresh = (async () => {
        do {
          this.configDirty = false;
          const snapshot = await api.games.getConfigs();
          // A newer event arrived while this read was in flight; read the current snapshot.
          if (this.configDirty) continue;
          this.applyConfigState(snapshot);
          const revision = this.statusRevision;
          const statuses = await api.games.getGameStatuses();
          if (revision === this.statusRevision) this.applyGameStatuses(statuses);
        } while (this.configDirty);
      })().finally(() => { this.configRefresh = null; });
    }
    return this.configRefresh;
  }

  private applyConfigState({ games, issues }: GameConfigState) {
    const previous = this.activeGame;
    const previousInstrument = this.activeInstrument;
    const previousLayout = this.activeLayout;
    this.games = games;
    if (!previous) {
      let recovered = false;
      for (const game of games) {
        const selected = this.selectedInstrumentIds[game.id];
        if (!selected || game.instruments.some(instrument => instrument.id === selected || instrument.name === selected)) continue;
        const defaultInstrument = game.instruments.find(instrument =>
          instrument.id === game.defaultInstrument || instrument.name === game.defaultInstrument);
        if (!defaultInstrument) continue;
        this.selectedInstrumentIds[game.id] = defaultInstrument.id;
        recovered = true;
      }
      if (recovered) {
        void api.settings.updateSettings({ selectedInstrumentIds: { ...this.selectedInstrumentIds } }).catch(console.error);
      }
    }
    if (previous) {
      this.activeGame = games.find(g => g.id === previous.id) ?? {
        ...previous, isMissing: true, displayName: 'Missing Game', name: 'Missing Game', shortName: 'Missing Game',
        iconUrl: undefined, imageFileName: undefined, instruments: [], layouts: [],
        isRunning: false, isFocused: false, pid: undefined,
      };
    } else {
      this.activeGame = games.find(g => g.isSelected) ?? games[0] ?? null;
    }
    for (const game of games) game.isSelected = game.id === this.activeGame?.id;
    const selectionDeleted = (previous && !previous.isMissing && this.activeGame?.isMissing)
      || (previousInstrument && !this.activeInstrument) || (previousLayout && !this.activeLayout);
    if (selectionDeleted) {
      void api.playback.stop().catch(console.error);
    }
    const currentIssues = new Set(issues.map(issue => `${issue.path}:${issue.message}`));
    const newIssues = issues.filter(issue => !this.reportedIssues.has(`${issue.path}:${issue.message}`));
    this.reportedIssues = currentIssues;
    if (newIssues.length) {
      notificationCenter.show({
        type: 'error', title: 'Configuration could not be loaded', durationMs: 10000,
        message: newIssues.map(issue => `${issue.path}: ${issue.message}`).join('\n'),
      });
    }
    this.syncActiveInstrumentMapping();
    this.applyGameStatuses(this.latestStatuses);
  }

  getInstrumentLayouts(instrument: InstrumentConfig | null, game = this.activeGame, mode: InstrumentInputMode | 'all' = this.getInputMode(game, instrument)) {
    const supported = instrumentLayoutIds(instrument, mode);
    return (game?.layouts ?? []).filter(layout => (layout.keys.length > 0 || layout.chordKeys?.length || layout.chordWheel?.positions.length || layout.fretboard?.stringKeys.length) && supported.some(id =>
      id.toLowerCase() === layout.id.toLowerCase() || id.toLowerCase() === layout.name.toLowerCase()));
  }

  async openGamesFolder(): Promise<boolean> {
    return await api.games.openGamesFolder();
  }

  async openLayoutsFolder(): Promise<boolean> {
    return await api.games.openLayoutsFolder();
  }

  async saveLayoutPedalBindings(layoutId: string, bindings: Partial<PedalBindings> | null) {
    if (!await api.games.saveLayoutPedalBindings(layoutId, bindings)) {
      throw new Error('Unable to save pedal bindings.');
    }
    await this.refreshConfigs();
  }

  // Apply selections in order so an older clear cannot overwrite a newer mapping.
  private applyMapping(layout: string, pairs: [number, string][], paths?: string[]) {
    this.mappingReady = this.mappingReady.then(async () => {
      try {
        const applied = await api.games.setGameInstrumentMapping(layout, pairs, paths);
        return applied && layout !== '';
      } catch (error) {
        console.error('[GameState] Failed to apply instrument mapping:', error);
        return false;
      }
    });
    return this.mappingReady;
  }

  async hasPlaybackMapping(): Promise<boolean> {
    let pending: Promise<boolean>;
    let ready: boolean;
    do {
      pending = this.mappingReady;
      ready = await pending;
    } while (pending !== this.mappingReady);
    return ready;
  }

  syncActiveInstrumentMapping(instrumentId?: string, layoutId?: string) {
    if (!this.activeGame || this.activeGame.isMissing) {
      this.publishSettingsContext();
      return this.applyMapping('', []);
    }
    const explicitInstrument = this.activeGame.instruments?.find(i => i.id === instrumentId);
    if (explicitInstrument) this.selectedInstrumentIds[this.activeGame.id] = explicitInstrument.id;
    const inst = this.activeInstrument;
    if (!inst) {
      this.publishSettingsContext();
      return this.applyMapping('', []);
    }
    if (explicitInstrument || this.selectedInstrumentIds[this.activeGame.id] !== inst.id) {
      this.selectedInstrumentIds[this.activeGame.id] = inst.id;
      void api.settings.updateSettings({ selectedInstrumentIds: { ...this.selectedInstrumentIds } }).catch(console.error);
    }
    this.publishSettingsContext();

    const layouts = this.getInstrumentLayouts(inst, this.activeGame, 'all');
    const selectionKey = `${this.activeGame.id}/${inst.id}`;
    if (!instrumentLayoutIds(inst, 'all').length) delete this.selectedLayoutIds[selectionKey];
    const explicitLayout = layouts.find(l => l.id === layoutId || l.name === layoutId);
    if (explicitLayout) {
      const mode = ([this.activeInputMode, 'keyboard', 'chords', 'frets'] as const)
        .find(mode => this.getInstrumentLayouts(inst, this.activeGame, mode).includes(explicitLayout))!;
      this.selectedLayoutIds[`${selectionKey}/mode`] = mode;
      this.selectedLayoutIds[`${selectionKey}/${mode}`] = explicitLayout.id;
      this.selectedLayoutIds[selectionKey] = explicitLayout.id;
    }
    const targetLayout = this.activeLayout;
    if (targetLayout) {
      this.selectedLayoutIds[selectionKey] = targetLayout.id;
      this.selectedLayoutIds[`${selectionKey}/${this.activeInputMode}`] = targetLayout.id;
    }
    if (explicitLayout) {
      void api.settings.updateSettings({ selectedLayoutIds: { ...this.selectedLayoutIds } }).catch(console.error);
    }

    const keyPairs = instrumentKeyPairs(inst, targetLayout);

    const effectiveLayoutName = keyPairs.length || targetLayout?.chordWheel?.positions.length || targetLayout?.fretboard?.stringKeys.length || targetLayout?.chordKeys?.length ? targetLayout!.id : '';
    const configPaths = [this.activeGame.filePath, inst.filePath, targetLayout?.filePath]
      .filter((path): path is string => Boolean(path));
    return this.applyMapping(effectiveLayoutName, keyPairs, configPaths.length ? configPaths : undefined);
  }
}

export const gameStore = new GameStore();

if (typeof window !== 'undefined') {
  (window as any).gameStore = gameStore;
}
