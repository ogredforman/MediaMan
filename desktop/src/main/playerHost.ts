// Runs MPV for the web UI's Play button (SPEC §7.4 steps 3–6) and forwards its events, tagged with the launch id the
// UI chose, so the UI can ignore events from a launch it has replaced. One MPV window plays launch after launch.
import type { EpisodeChoice, IPlayer, PlayerCommand, PlayerEndReason, PlayerState, PlayOptions, UpNextChoice, UpNextOffer } from './player.js';

export type PlayerEvent =
  | { launchId: string; type: 'progress'; positionSeconds: number; durationSeconds: number; paused: boolean }
  | { launchId: string; type: 'ended'; reason: PlayerEndReason; detail?: string }
  | { launchId: string; type: 'credits' }
  | { launchId: string; type: 'endReached' }
  | { launchId: string; type: 'upNext'; choice: UpNextChoice }
  | { launchId: string; type: 'episode'; choice: EpisodeChoice };

/** What `play` answers over IPC; a rejected IPC call would lose the error's kind. */
export type PlayResult = { ok: true } | { ok: false; error: 'mpvStart'; message: string };

/** One player and the launches it plays, oldest first: events go to the first, and each `ended` moves on to the next. */
interface Binding {
  player: IPlayer;
  mpvPath: string;
  launches: { launchId: string; send: (event: PlayerEvent) => void }[];
}

export class PlayerHost {
  private current: Binding | undefined;
  // Plays and stops run one at a time, so overlapping plays (a re-query racing a click) never leave an MPV behind.
  private queue: Promise<unknown> = Promise.resolve();
  private readonly stateListeners: ((state: PlayerState) => void)[] = [];

  /**
   * @param createPlayer makes a player for an MPV path, so a newly picked MPV is used on the next Play.
   * @param mpvPath the MPV executable to launch, read on every Play.
   */
  constructor(
    private readonly createPlayer: (mpvPath: string) => IPlayer,
    private readonly mpvPath: () => Promise<string>,
  ) {}

  /**
   * Plays `opts`, in the MPV window already open when there is one and the MPV path hasn't changed. Any failure to
   * start is MPV's: the UI offers "Open MPV settings".
   */
  play(launchId: string, opts: PlayOptions, send: (event: PlayerEvent) => void): Promise<PlayResult> {
    return this.enqueue(() => this.start(launchId, opts, send));
  }

  /** Shows the "Up next" countdown, if `launchId` is still what plays. */
  upNext(launchId: string, offer: UpNextOffer): void {
    this.playing(launchId)?.showUpNext(offer);
  }

  /** Shows a message over the playback in MPV, if `launchId` is still what plays. */
  notice(launchId: string, text: string): void {
    this.playing(launchId)?.showNotice(text);
  }

  /** Acts on the file that plays now, whichever launch it is; does nothing when nothing plays. For the overlay. */
  command(cmd: PlayerCommand): void {
    this.current?.player.command(cmd);
  }

  /** The current file's state on every change, for the overlay; whichever player plays it. */
  onState(cb: (state: PlayerState) => void): void {
    this.stateListeners.push(cb);
  }

  stop(): Promise<void> {
    return this.enqueue(() => this.stopCurrent());
  }

  // Only one launch once the player is idle between calls: a replace ends the old file inside `play`.
  private playing(launchId: string): IPlayer | undefined {
    const launches = this.current?.launches;
    return launches?.length === 1 && launches[0]?.launchId === launchId ? this.current?.player : undefined;
  }

  private async start(launchId: string, opts: PlayOptions, send: (event: PlayerEvent) => void): Promise<PlayResult> {
    let binding: Binding;
    try {
      const mpvPath = await this.mpvPath();
      if (this.current && this.current.mpvPath !== mpvPath) await this.stopCurrent();
      binding = this.current ?? this.bind(this.createPlayer(mpvPath), mpvPath);
    } catch (err) {
      return { ok: false, error: 'mpvStart', message: messageOf(err) };
    }
    this.current = binding;
    const launch = { launchId, send };
    binding.launches.push(launch);
    try {
      await binding.player.play(opts);
      return { ok: true };
    } catch (err) {
      binding.launches = binding.launches.filter((l) => l !== launch);
      return { ok: false, error: 'mpvStart', message: messageOf(err) };
    }
  }

  private bind(player: IPlayer, mpvPath: string): Binding {
    const binding: Binding = { player, mpvPath, launches: [] };
    const forward = (event: DistributiveOmit<PlayerEvent, 'launchId'>) => {
      const launch = binding.launches[0];
      launch?.send({ ...event, launchId: launch.launchId });
    };
    player.onProgress((p) => {
      forward({ type: 'progress', ...p });
    });
    player.onEnded((reason, detail) => {
      forward(detail === undefined ? { type: 'ended', reason } : { type: 'ended', reason, detail });
      binding.launches.shift();
    });
    player.onCredits(() => {
      forward({ type: 'credits' });
    });
    player.onEndReached(() => {
      forward({ type: 'endReached' });
    });
    player.onUpNextChoice((choice) => {
      forward({ type: 'upNext', choice });
    });
    player.onEpisodeChoice((choice) => {
      forward({ type: 'episode', choice });
    });
    player.onState((state) => {
      if (this.current !== binding) return;
      for (const listener of this.stateListeners) listener(state);
    });
    return binding;
  }

  private async stopCurrent(): Promise<void> {
    const binding = this.current;
    this.current = undefined;
    await binding?.player.stop();
  }

  private enqueue<T>(work: () => Promise<T>): Promise<T> {
    const run = this.queue.then(work, work);
    this.queue = run.catch(() => undefined);
    return run;
  }
}

type DistributiveOmit<T, K extends PropertyKey> = T extends unknown ? Omit<T, K> : never;

function messageOf(err: unknown): string {
  return err instanceof Error ? err.message : String(err);
}
