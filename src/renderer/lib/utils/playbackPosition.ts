export interface PlaybackPosition {
  timeMs: number;
  seekId: number;
}

/** Ignore timestamps produced before the latest seek was applied by the engine. */
export class PlaybackPositionGate {
  private latestSeekId = 0;
  private nextId = Date.now();

  requestSeek(): number {
    this.latestSeekId = ++this.nextId;
    return this.latestSeekId;
  }

  accepts(position: PlaybackPosition): boolean {
    return position.seekId >= this.latestSeekId;
  }

  reset() {
    this.latestSeekId = 0;
  }

  cancelRequest(id: number) {
    if (id === this.latestSeekId) this.reset();
  }
}
