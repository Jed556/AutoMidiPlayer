import type { GameInfo } from '$shared/types/games';
import type { MidiInputState } from '$shared/types/midiInput';
import type { ServiceState, SystemSnapshot } from '$shared/types/system';

type Translate = (key: string) => string;
export interface SystemInfoRow { id: string; label: string; value: string; status?: string; tone?: 'good' | 'muted' | 'warning'; detail?: string | null }
const tone = (state: ServiceState): SystemInfoRow['tone'] =>
  state === 'ready' ? 'good' : ['unavailable', 'permissionRequired', 'reconnecting'].includes(state) ? 'warning' : 'muted';

/** Presentation only: all service observations come from existing owners. */
export function systemInfoRows(snapshot: SystemSnapshot | null, failed: boolean, game: GameInfo | null,
  midi: MidiInputState, t: Translate): { environment: SystemInfoRow[]; services: SystemInfoRow[] } {
  const text = (key: string) => t(`systemInfo.${key}`);
  const unknown = text('unknown');
  const env = snapshot?.environment;
  const arch = (value?: string) => value ? ({ x86_64: 'x64', aarch64: 'ARM64', x86: 'x86' }[value] ?? value) : unknown;
  const environment = [
    { id: 'os', label: text('os'), value: env?.osVersion || env?.osName || unknown },
    { id: 'systemArchitecture', label: text('systemArchitecture'), value: arch(env?.systemArchitecture) },
    { id: 'appArchitecture', label: text('appArchitecture'), value: arch(env?.appArchitecture) },
    { id: 'desktopSession', label: text('desktopSession'), value: text(`session.${env?.desktopSession ?? 'unknown'}`) },
  ];
  const native = (id: 'keyboard' | 'audio' | 'tray' | 'mediaControls'): SystemInfoRow => {
    const observation = snapshot?.[id];
    const state = failed ? 'unknown' : observation?.state ?? 'notChecked';
    const statusKey = id === 'mediaControls' && state === 'notChecked' ? 'notInitialized'
      : id === 'tray' && state === 'ready' ? 'available' : state;
    return { id, label: text(id), value: id === 'keyboard'
      ? text(`backend.${snapshot?.keyboard.backend ?? 'unknown'}`) : text(statusKey),
      status: id === 'keyboard' ? text(statusKey) : undefined, tone: tone(state),
      detail: failed ? undefined : observation?.detail };
  };
  const wayland = env?.desktopSession === 'wayland';
  const gameState = !game ? 'inactive' : game.isMissing ? 'unavailable' : game.isRunning ? 'ready' : 'inactive';
  const gameStatus = !game ? 'noneSelected' : game.isMissing ? 'unavailable' : game.isRunning ? 'detected' : 'notDetected';
  const gameRow: SystemInfoRow = { id: 'game', label: text('game'), value: game?.displayName ?? text('noneSelected'),
    status: game ? `${text(gameStatus)} · ${text(wayland ? 'focusUnavailable' : !env ? 'focusUnknown' : game.isFocused ? 'focused' : 'notFocused')}` : text('inactive'), tone: tone(gameState) };
  const device = midi.ports.find(port => port.id === midi.selectedPortId)?.name;
  const midiState: ServiceState = midi.error ? 'unavailable' : midi.starting ? 'checking'
    : midi.active && midi.connected ? 'ready' : 'inactive';
  const inputStatus = midi.error ? 'unavailable' : midi.starting ? 'permissionRequired'
    : !midi.active || !midi.connected ? 'inactive' : midi.status === 'suspended' ? 'suspended'
    : midi.listenMode ? 'listening' : 'playing';
  const midiRow: SystemInfoRow = { id: 'midi', label: text('midi'),
    value: device ?? text(midi.selectedPortId ? 'deviceUnavailable' : 'noneSelected'),
    status: `${text(midi.connected ? 'connected' : 'disconnected')} · ${text(inputStatus)}`,
    tone: tone(midiState), detail: midi.error };
  return { environment, services: [native('keyboard'), gameRow, midiRow, native('audio'), native('tray'), native('mediaControls')] };
}

export interface PollHost {
  document: Pick<Document, 'hidden' | 'addEventListener' | 'removeEventListener'>;
  window: Pick<Window, 'addEventListener' | 'removeEventListener'>;
  setInterval: (callback: () => void, ms: number) => unknown;
  clearInterval: (timer: unknown) => void;
}

/** Scoped to the mounted Settings view. Hidden/in-flight results cannot replace fresh observations. */
export function pollSystemSnapshot(read: () => Promise<SystemSnapshot>, accept: (value: SystemSnapshot) => void,
  fail: () => void, host: PollHost) {
  let disposed = false, busy = false, pending = false, revision = 0;
  let timer: unknown;
  const visible = () => !disposed && !host.document.hidden;
  const refresh = (immediate = true) => {
    if (!visible()) return;
    if (busy) { if (immediate) pending = true; return; }
    busy = true;
    const current = revision;
    void Promise.resolve().then(() => visible() && current === revision ? read() : undefined).then(value => {
      if (value !== undefined && visible() && current === revision) accept(value);
    }, () => { if (visible() && current === revision) fail(); }).finally(() => {
      busy = false;
      if (pending) { pending = false; refresh(); }
    });
  };
  const visibility = () => {
    revision++;
    if (timer !== undefined) host.clearInterval(timer);
    timer = undefined;
    pending = false;
    if (visible()) { refresh(); timer = host.setInterval(() => refresh(false), 1000); }
  };
  const focus = () => refresh();
  host.document.addEventListener('visibilitychange', visibility);
  host.window.addEventListener('focus', focus);
  visibility();
  return { refresh, stop() {
    disposed = true; revision++; pending = false;
    if (timer !== undefined) host.clearInterval(timer);
    host.document.removeEventListener('visibilitychange', visibility);
    host.window.removeEventListener('focus', focus);
  } };
}
