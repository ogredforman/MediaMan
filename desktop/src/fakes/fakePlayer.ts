// FakePlayer (SPEC §10.4): records play calls and lets tests emit progress and ended events; never starts MPV.
import { PlayerStartError } from '../main/mpvPlayer.js';
import type { EpisodeChoice, IPlayer, PlayerEndReason, PlayerProgress, PlayOptions, UpNextChoice, UpNextOffer } from '../main/player.js';

export class FakePlayer implements IPlayer {
  readonly plays: PlayOptions[] = [];
  /** Every "Up next" countdown shown while something played. */
  readonly upNexts: UpNextOffer[] = [];
  /** Every notice shown while something played. */
  readonly notices: string[] = [];
  stopCalls = 0;
  /** Plays that opened a new window; a play while something plays replaces it in the same window. */
  launches = 0;
  playing = false;
  private readonly progressListeners: ((p: PlayerProgress) => void)[] = [];
  private readonly endedListeners: ((reason: PlayerEndReason, detail?: string) => void)[] = [];
  private readonly creditsListeners: (() => void)[] = [];
  private readonly endReachedListeners: (() => void)[] = [];
  private readonly upNextListeners: ((choice: UpNextChoice) => void)[] = [];
  private readonly episodeListeners: ((choice: EpisodeChoice) => void)[] = [];
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

  onCredits(cb: () => void): void {
    this.creditsListeners.push(cb);
  }

  onEndReached(cb: () => void): void {
    this.endReachedListeners.push(cb);
  }

  onUpNextChoice(cb: (choice: UpNextChoice) => void): void {
    this.upNextListeners.push(cb);
  }

  onEpisodeChoice(cb: (choice: EpisodeChoice) => void): void {
    this.episodeListeners.push(cb);
  }

  showUpNext(offer: UpNextOffer): void {
    if (this.playing) this.upNexts.push(offer);
  }

  showNotice(text: string): void {
    if (this.playing) this.notices.push(text);
  }

  async play(opts: PlayOptions): Promise<void> {
    if (this.playing) {
      // Replaced in the same window: the old file ends first, as MpvPlayer's loadfile does.
      this.emitEnded('quit');
      this.plays.push(opts);
      this.playing = true;
      return;
    }
    await this.stop();
    const failure = this.nextFailure;
    if (failure) {
      this.nextFailure = undefined;
      throw failure;
    }
    this.plays.push(opts);
    this.launches++;
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

  /** Playback entered the final credits. Ignored when nothing plays. */
  emitCredits(): void {
    if (!this.playing) return;
    for (const listener of this.creditsListeners) listener();
  }

  /** Playback reached the end of the file and holds the last frame. Ignored when nothing plays. */
  emitEndReached(): void {
    if (!this.playing) return;
    for (const listener of this.endReachedListeners) listener();
  }

  /** The viewer's answer to the "Up next" countdown. Ignored when nothing plays. */
  emitUpNextChoice(choice: UpNextChoice): void {
    if (!this.playing) return;
    for (const listener of this.upNextListeners) listener(choice);
  }

  /** The viewer chose Previous or Next episode in the OSC. Ignored when nothing plays. */
  emitEpisodeChoice(choice: EpisodeChoice): void {
    if (!this.playing) return;
    for (const listener of this.episodeListeners) listener(choice);
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
