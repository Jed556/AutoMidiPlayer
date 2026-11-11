import type { MidiInputState } from '$shared/types/midiInput';
import { api } from '../backend';
import { notificationCenter } from '../ui/notificationCenter.svelte';
import { t } from '../i18n';

/** The connection belongs to the native runtime, never to a mounted view. */
export class MidiInputStore {
  state = $state<MidiInputState>({ ports: [], selectedPortId: null, connected: false,
    active: false, starting: false, listenMode: false, outputAvailable: false,
    requiresSession: false, status: 'idle', pitches: [], error: null });
  busy = $state(false);
  refreshing = $state(false);
  private unlisten?: () => void;
  private revision = 0;

  private accept = (state: MidiInputState) => {
    this.revision++;
    if (state.error && state.error !== this.state.error) {
      notificationCenter.error(t('instrument.midiInput'), state.error);
    }
    this.state = state;
  };

  async init() {
    if (this.unlisten) return;
    this.unlisten = api.midiInput.onStateChanged(this.accept);
    const revision = this.revision;
    try {
      const state = await api.midiInput.getState();
      if (revision === this.revision) this.accept(state);
    } catch (error) { this.report(error); }
  }

  private report(error: unknown) {
    const message = error instanceof Error ? error.message : String(error);
    if (message !== this.state.error) notificationCenter.error(t('instrument.midiInput'), message);
    this.state = { ...this.state, status: 'error', error: message };
  }

  private async run(action: () => Promise<MidiInputState>) {
    if (this.busy) return;
    this.busy = true;
    try { this.accept(await action()); }
    catch (error) { this.report(error); }
    finally { this.busy = false; }
  }
  select(portId: string | null) { return this.run(() => api.midiInput.selectPort(portId)); }
  start() { return this.run(() => api.midiInput.start()); }
  stop() { return this.run(() => api.midiInput.stop()); }
  async refresh() {
    this.refreshing = true;
    try { await this.run(() => api.midiInput.refreshPorts()); }
    finally { this.refreshing = false; }
  }
}

export const midiInputStore = new MidiInputStore();
