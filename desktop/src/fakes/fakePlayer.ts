// FakePlayer (SPEC §10.4): records play calls and lets tests emit progress and ended events; never starts MPV.
import { PlayerStartError } from '../main/mpvPlayer.js';
import type { IPlayer, PlayerEndReason, PlayerProgress, PlayOptions } from '../main/player.js';

export class FakePlayer implements IPlayer {
  readonly plays: PlayOptions[] = [];
  stopCalls = 0;
  playing = false;
  private readonly progressListeners: ((p: PlayerProgress) => void)[] = [];
  private readonly endedListeners: ((reason: PlayerEndReason, detail?: string) => void)[] = [];
  private nextFailure: Error | undefined;

  /** Makes the next `play()` reject with `error`, as when MPV can't be started. */
  failNextPlay(error: Error = new PlayerStartError("MPV couldn't be started (fake).")): void {
    this.nextFailure = error;
  }

  onProgress(cb: (p: PlayerProgress) => void): void {
    this.progressListeners.push(cb);
  }

  onEnded(cb: (reason: PlayerEndReason, detail?: string) => void): void {
    this.endedListeners.push(cb);
  }

  async play(opts: PlayOptions): Promise<void> {
    await this.stop();
    const failure = this.nextFailure;
    if (failure) {
      this.nextFailure = undefined;
      throw failure;
    }
    this.plays.push(opts);
    this.playing = true;
  }

  stop(): Promise<void> {
    this.stopCalls++;
    this.emitEnded('quit');
    return Promise.resolve();
  }

  /** Reports progress, as MPV does every 10 s and on pause, seek and quit. Ignored when nothing plays. */
  emitProgress(progress: PlayerProgress): void {
    if (!this.playing) return;
    for (const listener of this.progressListeners) listener(progress);
  }

  /** Ends the current playback. Ignored when nothing plays, so each play ends once. */
  emitEnded(reason: PlayerEndReason, detail?: string): void {
    if (!this.playing) return;
    this.playing = false;
    for (const listener of this.endedListeners) {
      if (detail === undefined) listener(reason);
      else listener(reason, detail);
    }
  }
}
