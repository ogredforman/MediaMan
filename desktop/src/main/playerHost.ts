// Runs one MPV launch at a time for the web UI's Play button (SPEC §7.4 steps 3–4 and 6) and forwards its events,
// tagged with the launch id the UI chose, so the UI can ignore events from a launch it has replaced.
import type { IPlayer, PlayerEndReason, PlayOptions } from './player.js';

export type PlayerEvent =
  | { launchId: string; type: 'progress'; positionSeconds: number; durationSeconds: number; paused: boolean }
  | { launchId: string; type: 'ended'; reason: PlayerEndReason; detail?: string };

/** What `play` answers over IPC; a rejected IPC call would lose the error's kind. */
export type PlayResult = { ok: true } | { ok: false; error: 'mpvStart'; message: string };

export class PlayerHost {
  private current: { launchId: string; player: IPlayer } | undefined;
  // Plays and stops run one at a time, so overlapping plays (a re-query racing a click) never leave an MPV behind.
  private queue: Promise<unknown> = Promise.resolve();

  /**
   * @param createPlayer makes the player for one launch, so a newly picked MPV is used on the next Play.
   * @param mpvPath the MPV executable to launch, read on every Play.
   */
  constructor(
    private readonly createPlayer: (mpvPath: string) => IPlayer,
    private readonly mpvPath: () => Promise<string>,
  ) {}

  /** Stops what plays, then starts `opts`. Any failure to start is MPV's: the UI offers "Open MPV settings". */
  play(launchId: string, opts: PlayOptions, send: (event: PlayerEvent) => void): Promise<PlayResult> {
    return this.enqueue(() => this.start(launchId, opts, send));
  }

  stop(): Promise<void> {
    return this.enqueue(() => this.stopCurrent());
  }

  private async start(launchId: string, opts: PlayOptions, send: (event: PlayerEvent) => void): Promise<PlayResult> {
    await this.stopCurrent();
    let player: IPlayer;
    try {
      player = this.createPlayer(await this.mpvPath());
    } catch (err) {
      return { ok: false, error: 'mpvStart', message: messageOf(err) };
    }
    player.onProgress((p) => {
      send({ launchId, type: 'progress', ...p });
    });
    player.onEnded((reason, detail) => {
      send(detail === undefined ? { launchId, type: 'ended', reason } : { launchId, type: 'ended', reason, detail });
    });
    const launch = { launchId, player };
    this.current = launch;
    try {
      await player.play(opts);
      return { ok: true };
    } catch (err) {
      if (this.current === launch) this.current = undefined;
      return { ok: false, error: 'mpvStart', message: messageOf(err) };
    }
  }

  private async stopCurrent(): Promise<void> {
    const launch = this.current;
    this.current = undefined;
    await launch?.player.stop();
  }

  private enqueue<T>(work: () => Promise<T>): Promise<T> {
    const run = this.queue.then(work, work);
    this.queue = run.catch(() => undefined);
    return run;
  }
}

function messageOf(err: unknown): string {
  return err instanceof Error ? err.message : String(err);
}
