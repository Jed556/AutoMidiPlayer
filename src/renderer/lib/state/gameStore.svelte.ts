import type { GameInfo, GameStatus, GameConfigState, InstrumentConfig } from '../../../shared/types/games.js';
import type { SongSettingsContext } from '../../../shared/types/ipc.js';
import { instrumentKeyPairs } from '../../../shared/utils/instrumentLayout.js';
import { api } from '../backend/index.js';
import { notificationCenter } from '../ui/notificationCenter.svelte';

class GameStore {
  games = $state<GameInfo[]>([]);
  activeGame = $state<GameInfo | null>(null);
  selectedInstrumentIds = $state<Record<string, string>>({});
  private selectedLayoutIds = $state<Record<string, string>>({});
  private configRefresh: Promise<void> | null = null;
  private configDirty = false;
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

  getGameLayout(game: GameInfo | null, instrument = this.getGameInstrument(game)) {
    if (!game || !instrument) return null;
    const layouts = this.getInstrumentLayouts(instrument, game);
    const selected = this.selectedLayoutIds[`${game.id}/${instrument.id}`];
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
      && instrument.supportedLayouts?.length && this.selectedLayoutIds[`${game.id}/${instrument.id}`] && !this.activeLayout);
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
    }
    // Sync activeGame
    const activeGame = this.activeGame;
    const activeMatch = activeGame && byId.get(activeGame.id);
    if (activeGame && !activeGame.isMissing) {
      activeGame.isRunning = activeMatch?.isRunning ?? false;
      activeGame.isFocused = activeMatch?.isFocused ?? false;
      activeGame.pid = activeMatch?.pid ?? undefined;
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

  getInstrumentLayouts(instrument: InstrumentConfig | null, game = this.activeGame) {
    const supported = instrument?.supportedLayouts ?? [];
    return (game?.layouts ?? []).filter(layout => layout.keys.length > 0 && supported.some(id =>
      id.toLowerCase() === layout.id.toLowerCase() || id.toLowerCase() === layout.name.toLowerCase()));
  }

  async openGamesFolder(): Promise<boolean> {
    return await api.games.openGamesFolder();
  }

  async openLayoutsFolder(): Promise<boolean> {
    return await api.games.openLayoutsFolder();
  }

  syncActiveInstrumentMapping(instrumentId?: string, layoutId?: string) {
    if (!this.activeGame || this.activeGame.isMissing) {
      this.publishSettingsContext();
      void api.games.setGameInstrumentMapping('', []).catch(console.error);
      return;
    }
    const explicitInstrument = this.activeGame.instruments?.find(i => i.id === instrumentId);
    if (explicitInstrument) this.selectedInstrumentIds[this.activeGame.id] = explicitInstrument.id;
    const inst = this.activeInstrument;
    if (!inst) {
      this.publishSettingsContext();
      void api.games.setGameInstrumentMapping('', []).catch(console.error);
      return;
    }
    if (explicitInstrument || this.selectedInstrumentIds[this.activeGame.id] !== inst.id) {
      this.selectedInstrumentIds[this.activeGame.id] = inst.id;
      void api.settings.updateSettings({ selectedInstrumentIds: { ...this.selectedInstrumentIds } }).catch(console.error);
    }
    this.publishSettingsContext();

    const layouts = this.getInstrumentLayouts(inst);
    const selectionKey = `${this.activeGame.id}/${inst.id}`;
    const previousLayoutId = this.selectedLayoutIds[selectionKey];
    if (!inst.supportedLayouts?.length) delete this.selectedLayoutIds[selectionKey];
    const explicitLayout = layouts.find(l => l.id === layoutId || l.name === layoutId);
    if (explicitLayout) this.selectedLayoutIds[selectionKey] = explicitLayout.id;
    const targetLayout = this.activeLayout;
    if (targetLayout) this.selectedLayoutIds[selectionKey] = targetLayout.id;
    if (explicitLayout && this.selectedLayoutIds[selectionKey] !== previousLayoutId) {
      void api.settings.updateSettings({ selectedLayoutIds: { ...this.selectedLayoutIds } }).catch(console.error);
    }

    const keyPairs = instrumentKeyPairs(inst, targetLayout);

    const effectiveLayoutName = keyPairs.length ? targetLayout!.id : '';
    const configPaths = [this.activeGame.filePath, inst.filePath, targetLayout?.filePath]
      .filter((path): path is string => Boolean(path));
    api.games.setGameInstrumentMapping(effectiveLayoutName, keyPairs, configPaths.length ? configPaths : undefined).catch(console.error);
  }
}

export const gameStore = new GameStore();

if (typeof window !== 'undefined') {
  (window as any).gameStore = gameStore;
}
