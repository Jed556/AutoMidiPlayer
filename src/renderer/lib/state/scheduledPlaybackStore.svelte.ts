import { playbackStore } from './playbackStore.svelte';
import { notificationCenter } from '../ui/notificationCenter.svelte';
import { t } from '../i18n';

type ScheduledAction = 'play' | 'pause';

export interface ScheduledPlaybackRuntime {
  now: () => number;
  isPlaying: () => boolean;
  hasSong: () => boolean;
  play: () => Promise<void>;
  pause: () => Promise<void>;
  interval: (callback: () => void) => ReturnType<typeof setInterval>;
  clearInterval: (id: ReturnType<typeof setInterval>) => void;
}

const runtime: ScheduledPlaybackRuntime = {
  now: () => Date.now(),
  isPlaying: () => playbackStore.isPlaying,
  hasSong: () => playbackStore.hasPlayableSong,
  play: () => playbackStore.play(),
  pause: () => playbackStore.pause(),
  interval: callback => setInterval(callback, 1000),
  clearInterval: id => clearInterval(id),
};

/** App-owned timer; navigation never changes its deadline or captured action. */
export class ScheduledPlaybackStore {
  hour = $state(0);
  minute = $state(0);
  targetAt = $state<number | null>(null);
  action = $state<ScheduledAction>('play');
  remainingSeconds = $state(0);
  executing = $state(false);
  active = $derived(this.targetAt !== null);
  private timer: ReturnType<typeof setInterval> | null = null;
  private inFlight: Promise<void> | null = null;
  private generation = 0;

  constructor(private clock: ScheduledPlaybackRuntime = runtime) {
    this.setToNow();
  }

  setToNow() {
    if (this.active || this.executing) return;
    const now = new Date(this.clock.now());
    this.hour = now.getHours();
    this.minute = now.getMinutes();
  }

  arm(): boolean {
    if (this.active || this.executing || !Number.isInteger(this.hour) || this.hour < 0 || this.hour > 23
      || !Number.isInteger(this.minute) || this.minute < 0 || this.minute > 59) return false;
    const action = this.clock.isPlaying() ? 'pause' : 'play';
    if (action === 'play' && !this.clock.hasSong()) {
      notificationCenter.error(t('instrument.timerFailedTitle'), t('instrument.timerNoSong'));
      return false;
    }
    const now = this.clock.now();
    const target = new Date(now);
    target.setHours(this.hour, this.minute, 0, 0);
    if (target.getTime() <= now) target.setDate(target.getDate() + 1);
    this.action = action;
    this.targetAt = target.getTime();
    this.remainingSeconds = Math.ceil((this.targetAt - now) / 1000);
    const generation = ++this.generation;
    this.timer = this.clock.interval(() => { this.tick(generation); });
    notificationCenter.info(t('instrument.timerStartedTitle'),
      `${t(action === 'play' ? 'instrument.schedulePlay' : 'instrument.schedulePause')} ${this.hour.toString().padStart(2, '0')}:${this.minute.toString().padStart(2, '0')}`);
    return true;
  }

  cancel(notify = true) {
    const wasActive = this.active;
    ++this.generation;
    this.clearTimer();
    this.targetAt = null;
    this.remainingSeconds = 0;
    if (notify && wasActive) notificationCenter.info(t('instrument.timerCancelledTitle'), t('instrument.timerCancelledMsg'));
  }

  /** Reset must finish a pending load/play before stopping and clearing its data. */
  async cancelAndWait() {
    this.cancel(false);
    await this.inFlight;
  }

  private clearTimer() {
    if (this.timer !== null) this.clock.clearInterval(this.timer);
    this.timer = null;
  }

  private tick(generation: number) {
    if (generation !== this.generation || this.targetAt === null) return;
    this.remainingSeconds = Math.max(0, Math.ceil((this.targetAt - this.clock.now()) / 1000));
    if (this.remainingSeconds > 0) return;
    const action = this.action;
    this.clearTimer();
    this.targetAt = null;
    this.executing = true;
    const operation = this.perform(action, generation);
    this.inFlight = operation;
    const completed = () => { if (this.inFlight === operation) this.inFlight = null; };
    void operation.then(completed, completed);
  }

  private async perform(action: ScheduledAction, generation: number) {
    try {
      if (action === 'play') {
        if (!this.clock.hasSong()) throw new Error(t('instrument.timerNoSong'));
        if (!this.clock.isPlaying()) await this.clock.play();
        if (!this.clock.isPlaying()) throw new Error(t('instrument.timerActionFailed'));
      } else if (this.clock.isPlaying()) {
        await this.clock.pause();
      }
      if (generation === this.generation) notificationCenter.success(t('instrument.timerFinishedTitle'),
        t(action === 'play' ? 'instrument.timerFinishedMsg' : 'instrument.timerPausedMsg'));
    } catch (error) {
      if (generation === this.generation) notificationCenter.error(t('instrument.timerFailedTitle'), String(error));
    } finally {
      this.executing = false;
    }
  }
}

export const scheduledPlaybackStore = new ScheduledPlaybackStore();
