// Runs MPV for the web UI's Play button (SPEC §7.4 steps 3–6) and forwards its events, tagged with the launch id the
// UI chose, so the UI can ignore events from a launch it has replaced. One MPV window plays launch after launch. The
// player overlay (ADR 0011) draws what plays, with the Up next countdown and notices this host keeps for it.
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

/** What the overlay draws for the current file: the player's state, the Up next countdown on offer and a notice. */
export interface HostState extends PlayerState {
  upNext?: UpNextOffer;
  notice?: string;
}

/** How long a notice stays up; the web UI repeats it while the problem lasts, so it stays up as long as that. */
export const noticeMs = 12_000;

export interface PlayerHostOptions {
  /** The window MPV draws into (`PlayOptions.embedIn`), read on every Play; undefined opens MPV's own window. */
  embedIn?: () => Buffer | undefined;
}

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
  private readonly stateListeners: ((state: HostState) => void)[] = [];
  private readonly activeListeners: ((active: boolean) => void)[] = [];
  private active = false;
  // The current file's last state and what the overlay shows over it; all cleared when the file ends.
  private state: PlayerState | undefined;
  private offer: UpNextOffer | undefined;
  private noticeText: string | undefined;
  private noticeTimer: ReturnType<typeof setTimeout> | undefined;

  /**
   * @param createPlayer makes a player for an MPV path, so a newly picked MPV is used on the next Play.
   * @param mpvPath the MPV executable to launch, read on every Play.
   */
  constructor(
    private readonly createPlayer: (mpvPath: string) => IPlayer,
    private readonly mpvPath: () => Promise<string>,
    private readonly options: PlayerHostOptions = {},
  ) {}

  /**
   * Plays `opts`, in the MPV window already open when there is one and the MPV path hasn't changed. Any failure to
   * start is MPV's: the UI offers "Open MPV settings".
   */
  play(launchId: string, opts: PlayOptions, send: (event: PlayerEvent) => void): Promise<PlayResult> {
    return this.enqueue(() => this.start(launchId, opts, send));
  }

  /** Offers the "Up next" countdown in the overlay, if `launchId` is still what plays. */
  upNext(launchId: string, offer: UpNextOffer): void {
    if (!this.playing(launchId)) return;
    this.offer = offer;
    this.emitState();
  }

  /** Shows a message over the playback for `noticeMs`, if `launchId` is still what plays. */
  notice(launchId: string, text: string): void {
    if (!this.playing(launchId)) return;
    this.clearNotice();
    this.noticeText = text;
    this.noticeTimer = setTimeout(() => {
      this.noticeTimer = undefined;
      this.noticeText = undefined;
      this.emitState();
    }, noticeMs);
    this.emitState();
  }

  /** The viewer's answer to the countdown in the overlay, reported to the launch it was offered to. */
  answerUpNext(choice: UpNextChoice): void {
    const launch = this.current?.launches;
    if (this.offer === undefined || launch?.length !== 1 || !launch[0]) return;
    this.offer = undefined;
    this.emitState();
    launch[0].send({ launchId: launch[0].launchId, type: 'upNext', choice });
  }

  /** Acts on the file that plays now, whichever launch it is; does nothing when nothing plays. For the overlay. */
  command(cmd: PlayerCommand): void {
    this.current?.player.command(cmd);
  }

  /** The current file's state, Up next and notice on every change, for the overlay; whichever player plays it. */
  onState(cb: (state: HostState) => void): void {
    this.stateListeners.push(cb);
  }

  /**
   * True once a Play has started, false when the last launch has ended, however it ended. A file replaced in the same
   * window (Up next, Previous and Next episode) keeps it true.
   */
  onActive(cb: (active: boolean) => void): void {
    this.activeListeners.push(cb);
  }

  stop(): Promise<void> {
    return this.enqueue(() => this.stopCurrent());
  }

  // Only one launch once the player is idle between calls: a replace ends the old file inside `play`.
  private playing(launchId: string): boolean {
    const launches = this.current?.launches;
    return launches?.length === 1 && launches[0]?.launchId === launchId;
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
    const embedIn = this.options.embedIn?.();
    try {
      await binding.player.play(embedIn === undefined ? opts : { ...opts, embedIn });
      // A file that ended while it was starting (MPV quit at once) leaves nothing to show the overlay for.
      if (binding.launches.includes(launch) && this.current === binding) this.setActive(true);
      return { ok: true };
    } catch (err) {
      binding.launches = binding.launches.filter((l) => l !== launch);
      if (binding.launches.length === 0) this.setActive(false);
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
      // A player stopped for a newly picked MPV ends after the next one is current; that one's file is still to come.
      if (this.current !== binding && this.current !== undefined) return;
      this.state = undefined;
      this.offer = undefined;
      this.clearNotice();
      if (binding.launches.length === 0) this.setActive(false);
    });
    player.onCredits(() => {
      forward({ type: 'credits' });
    });
    player.onEndReached(() => {
      forward({ type: 'endReached' });
    });
    player.onEpisodeChoice((choice) => {
      forward({ type: 'episode', choice });
    });
    player.onState((state) => {
      if (this.current !== binding) return;
      this.state = state;
      this.emitState();
    });
    return binding;
  }

  private emitState(): void {
    if (this.state === undefined) return;
    const state: HostState = {
      ...this.state,
      ...(this.offer === undefined ? {} : { upNext: this.offer }),
      ...(this.noticeText === undefined ? {} : { notice: this.noticeText }),
    };
    for (const listener of this.stateListeners) listener(state);
  }

  private clearNotice(): void {
    if (this.noticeTimer !== undefined) clearTimeout(this.noticeTimer);
    this.noticeTimer = undefined;
    this.noticeText = undefined;
  }

  private setActive(active: boolean): void {
    if (this.active === active) return;
    this.active = active;
    for (const listener of this.activeListeners) listener(active);
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
