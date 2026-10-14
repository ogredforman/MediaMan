// The real IPlayer: MPV as a separate process in its own window, driven over its JSON IPC (SPEC §7.4, §10.1, §10.3).
import { spawn } from 'node:child_process';
import net from 'node:net';
import os from 'node:os';
import path from 'node:path';
import { checkMpvExecutable, type CheckMpvExecutable } from './mpvCheck.js';
import type { EpisodeChoice, IPlayer, PlayerEndReason, PlayerProgress, PlayOptions, UpNextChoice, UpNextOffer } from './player.js';
import { upNextAt, type Chapter } from './upNext.js';

/** A spawned MPV, narrowed so tests can stand in for a real child process. */
export interface MpvChild {
  kill(signal: 'SIGTERM' | 'SIGKILL'): void;
  /** Called once, when the process exits or fails to start. */
  onExit(listener: (code: number | null, error?: Error) => void): void;
}

export type SpawnMpv = (command: string, args: string[], log: (line: string) => void) => MpvChild;

/** One IPC connection to MPV: newline-delimited JSON both ways. */
export interface MpvConnection {
  write(data: string): void;
  onData(listener: (chunk: string) => void): void;
  close(): void;
}

/** Connects to MPV's IPC server; rejects while MPV hasn't opened it yet. */
export type ConnectMpv = (ipcPath: string) => Promise<MpvConnection>;

export interface MpvPlayerOptions {
  /** The MPV executable; a bare name is looked up on PATH. */
  mpvPath: string;
  progressIntervalMs?: number;
  connectTimeoutMs?: number;
  connectRetryMs?: number;
  stopGraceMs?: number;
  spawn?: SpawnMpv;
  /** Decides the path is MPV without running it, before every launch; defaults to `checkMpvExecutable`. */
  checkExecutable?: CheckMpvExecutable;
  connect?: ConnectMpv;
  /** Builds the IPC endpoint for one launch; defaults to `ipcPathFor` on this platform. */
  ipcPath?: (id: string) => string;
  /** MediaMan's `mpv.conf` and purple OSC, loaded on every launch; left out, MPV runs with its own OSC. */
  assets?: MpvAssets;
  log?: (line: string) => void;
}

/** The config and OSC script in `desktop/mpv/` (SPEC §7.4 step 3, §11). */
export interface MpvAssets {
  config: string;
  script: string;
}

/** The `mpv/` folder under the app's path, or under the resources folder when packaged (`bundledTools`). */
export function mpvAssetsIn(appPath: string): MpvAssets {
  const dir = path.join(appPath, 'mpv');
  return { config: path.join(dir, 'mpv.conf'), script: path.join(dir, 'osc.lua') };
}

/** Thrown by `play()` when MPV can't be started; the UI offers "Open MPV settings" (SPEC §7.4 step 6). */
export class PlayerStartError extends Error {
  override readonly name = 'PlayerStartError';
}

/** `MEDIAMAN_MPV_PATH` (a dev override), else the packaged app's bundled MPV, else `mpv` from PATH. */
export function resolveMpvPath(env: Readonly<Record<string, string | undefined>>, bundledMpv?: string): string {
  const raw = env.MEDIAMAN_MPV_PATH?.trim();
  return raw === undefined || raw === '' ? (bundledMpv ?? 'mpv') : raw;
}

/** A named pipe on Windows, a Unix socket in the temp directory elsewhere. */
export function ipcPathFor(platform: NodeJS.Platform, tempDir: string, id: string): string {
  return platform === 'win32' ? `\\\\.\\pipe\\mediaman-mpv-${id}` : path.join(tempDir, `mediaman-mpv-${id}.sock`);
}

/** MPV's command line for one launch. The URL comes after `--`, so it is never read as an option. */
export function mpvArgs(opts: PlayOptions, ipcPath: string, assets?: MpvAssets): string[] {
  const args = [`--input-ipc-server=${ipcPath}`, '--force-window=yes', '--idle=no', '--keep-open=yes', `--force-media-title=${opts.title}`];
  // The user's own mpv.conf and scripts still load; ours comes after, so it wins where both set an option.
  if (assets) args.push('--osc=no', `--include=${assets.config}`, `--script=${assets.script}`);
  if (opts.startSeconds > 0) args.push(`--start=${String(opts.startSeconds)}`);
  if (opts.audioLanguages?.length) args.push(`--alang=${opts.audioLanguages.join(',')}`);
  if (opts.subtitleLanguages?.length) args.push(`--slang=${opts.subtitleLanguages.join(',')}`);
  args.push('--', opts.url);
  return args;
}

const observed = { 1: 'time-pos', 2: 'duration', 3: 'pause', 4: 'eof-reached', 5: 'chapter-list' } as const;

/** The script messages between the OSC (mpv/osc.lua) and the main process for "Up next". */
export const upNextMessages = { show: 'mediaman-up-next', choice: 'mediaman-up-next-choice' } as const;

/**
 * The script messages for the OSC's Previous and Next episode buttons: `show` names the neighbours' titles (empty for
 * none) once the file loads; `choice` comes back with `previous` or `next`.
 */
export const episodeMessages = { show: 'mediaman-episodes', choice: 'mediaman-episode' } as const;

/** How long a notice (`showNotice`) stays on screen; the web UI repeats it while the problem lasts. */
export const noticeMs = 12_000;

/** One MPV launch, from spawn to exit; it plays one file after another (`item`) in the same window. */
interface Session {
  child: MpvChild;
  exit: Promise<void>;
  exited: boolean;
  exitCode: number | null;
  exitError: Error | undefined;
  connection?: MpvConnection;
  timer?: NodeJS.Timeout;
  paused: boolean;
  stopping: boolean;
  nextRequestId: number;
  positionRequests: Set<number>;
  /** Files sent with `loadfile` whose `start-file` hasn't come yet; the current one starts with the last of them. */
  pendingStarts: number;
  item: Item;
}

/** One file played in a session, from its start to its end or until another file replaces it. */
interface Item {
  opts: PlayOptions;
  /** False for a file loaded into a running MPV until its `start-file`, so the old file's late events are dropped. */
  started: boolean;
  position?: number;
  duration?: number;
  chapters: Chapter[];
  creditsReached: boolean;
  eofReached: boolean;
  seeking: boolean;
  ended: boolean;
}

function newItem(opts: PlayOptions, started: boolean): Item {
  return { opts, started, chapters: [], creditsReached: false, eofReached: false, seeking: false, ended: false };
}

interface MpvMessage {
  event?: string;
  name?: string;
  id?: number;
  data?: unknown;
  reason?: string;
  file_error?: string;
  request_id?: number;
  error?: string;
  args?: unknown[];
}

/**
 * Plays one stream at a time. `play()` resolves once MPV's IPC answers, and rejects with `PlayerStartError` if MPV
 * can't be started. Progress is reported every 10 s and on pause, seek and quit (SPEC §7.4 step 4); `onEnded` fires
 * once per launch, with MPV's file error when the stream couldn't be loaded. MPV keeps the last frame at the end of the
 * file, so "Up next" can show there; `onCredits` fires where `upNextAt` puts the final credits. Playing while a file is
 * open loads the new one into the same MPV window.
 */
export class MpvPlayer implements IPlayer {
  private readonly progressIntervalMs: number;
  private readonly connectTimeoutMs: number;
  private readonly connectRetryMs: number;
  private readonly stopGraceMs: number;
  private readonly spawnMpv: SpawnMpv;
  private readonly checkExecutable: CheckMpvExecutable;
  private readonly connectMpv: ConnectMpv;
  private readonly ipcPath: (id: string) => string;
  private readonly log: (line: string) => void;
  private readonly progressListeners: ((p: PlayerProgress) => void)[] = [];
  private readonly endedListeners: ((reason: PlayerEndReason, detail?: string) => void)[] = [];
  private readonly creditsListeners: (() => void)[] = [];
  private readonly endReachedListeners: (() => void)[] = [];
  private readonly upNextListeners: ((choice: UpNextChoice) => void)[] = [];
  private readonly episodeListeners: ((choice: EpisodeChoice) => void)[] = [];
  private session: Session | undefined;
  private launches = 0;

  constructor(private readonly options: MpvPlayerOptions) {
    this.progressIntervalMs = options.progressIntervalMs ?? 10_000;
    this.connectTimeoutMs = options.connectTimeoutMs ?? 5_000;
    this.connectRetryMs = options.connectRetryMs ?? 100;
    this.stopGraceMs = options.stopGraceMs ?? 3_000;
    this.spawnMpv = options.spawn ?? spawnChild;
    this.checkExecutable = options.checkExecutable ?? ((file) => checkMpvExecutable(file));
    this.connectMpv = options.connect ?? connectSocket;
    this.ipcPath = options.ipcPath ?? ((id) => ipcPathFor(process.platform, os.tmpdir(), id));
    this.log =
      options.log ??
      ((line) => {
        console.log(`[mpv] ${line}`);
      });
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
    const session = this.session;
    if (!session || session.item.ended) return;
    send(session.connection, ['script-message', upNextMessages.show, String(offer.seconds), offer.title]);
  }

  showNotice(text: string): void {
    const session = this.session;
    if (!session || session.item.ended) return;
    // show-text expands `${property}`; `$$` keeps a dollar sign literal.
    send(session.connection, ['show-text', text.replaceAll('$', () => '$$'), String(noticeMs)]);
  }

  async play(opts: PlayOptions): Promise<void> {
    const live = this.session;
    if (live?.connection && !live.exited && !live.stopping && !live.item.ended) {
      this.replace(live, live.connection, opts);
      return;
    }
    await this.stop();
    // Never start a program that isn't MPV, picked, from MEDIAMAN_MPV_PATH or bundled (SPEC §5).
    const check = await this.checkExecutable(this.options.mpvPath);
    if (!check.ok) throw new PlayerStartError(check.message);

    this.launches++;
    const ipcPath = this.ipcPath(`${String(process.pid)}-${String(this.launches)}`);
    const child = this.spawnMpv(this.options.mpvPath, mpvArgs(opts, ipcPath, this.options.assets), this.log);
    const session: Session = {
      child,
      exit: Promise.resolve(),
      exited: false,
      exitCode: null,
      exitError: undefined,
      paused: false,
      stopping: false,
      nextRequestId: 100,
      positionRequests: new Set(),
      pendingStarts: 0,
      item: newItem(opts, true),
    };
    session.exit = new Promise<void>((resolve) => {
      child.onExit((code, error) => {
        session.exited = true;
        session.exitCode = code;
        session.exitError = error;
        this.onExited(session);
        resolve();
      });
    });
    this.session = session;

    const connection = await this.connectWithRetry(session, ipcPath);
    session.connection = connection;
    let buffer = '';
    connection.onData((chunk) => {
      buffer += chunk;
      let newline: number;
      while ((newline = buffer.indexOf('\n')) >= 0) {
        const line = buffer.slice(0, newline).trim();
        buffer = buffer.slice(newline + 1);
        if (line) this.onLine(session, line);
      }
    });
    for (const [id, name] of Object.entries(observed)) send(connection, ['observe_property', Number(id), name]);
    session.timer = setInterval(() => {
      this.report(session);
    }, this.progressIntervalMs);
  }

  /**
   * Loads `opts` into the running MPV instead of opening a new window (SPEC §7.4 step 5). The old file ends here; its
   * own `end-file` is dropped with anything else MPV sends before the new file's `start-file`. The launch options are
   * set as properties first (the `pause` MPV set when holding the last frame included), since `loadfile`'s per-file
   * options moved position in MPV 0.38.
   */
  private replace(session: Session, connection: MpvConnection, opts: PlayOptions): void {
    this.end(session, 'quit');
    session.item = newItem(opts, false);
    session.pendingStarts++;
    session.positionRequests.clear();
    send(connection, ['set_property', 'pause', false]);
    send(connection, ['set_property', 'start', opts.startSeconds > 0 ? String(opts.startSeconds) : 'none']);
    send(connection, ['set_property', 'force-media-title', opts.title]);
    send(connection, ['set_property', 'alang', (opts.audioLanguages ?? []).join(',')]);
    send(connection, ['set_property', 'slang', (opts.subtitleLanguages ?? []).join(',')]);
    send(connection, ['loadfile', opts.url, 'replace']);
  }

  async stop(): Promise<void> {
    const session = this.session;
    if (!session) return;
    session.stopping = true;
    if (!session.exited) {
      if (session.connection) send(session.connection, ['quit']);
      else session.child.kill('SIGTERM');
      if (!(await settlesWithin(session.exit, this.stopGraceMs))) {
        this.log(`MPV didn't quit within ${String(this.stopGraceMs / 1000)} s; killing it.`);
        session.child.kill('SIGKILL');
      }
    }
    await session.exit;
  }

  private async connectWithRetry(session: Session, ipcPath: string): Promise<MpvConnection> {
    const failStart = (detail: string): PlayerStartError =>
      new PlayerStartError(
        `MPV couldn't be started (${detail}). Check that MPV is installed and that the MPV path (${this.options.mpvPath}) points at it.`,
      );
    const deadline = Date.now() + this.connectTimeoutMs;
    for (;;) {
      if (session.exited) {
        if (session.stopping) throw new Error('Playback was stopped before MPV started.');
        throw failStart(session.exitError?.message ?? `it exited with code ${session.exitCode === null ? 'unknown' : String(session.exitCode)}`);
      }
      try {
        const connection = await this.connectMpv(ipcPath);
        if (!hasExited(session)) return connection;
        connection.close();
      } catch {
        // MPV hasn't opened its IPC server yet.
      }
      if (hasExited(session)) continue;
      if (Date.now() >= deadline) {
        session.stopping = true;
        session.child.kill('SIGKILL');
        await session.exit;
        throw failStart(`it didn't answer on ${ipcPath} within ${String(this.connectTimeoutMs / 1000)} s`);
      }
      await delay(this.connectRetryMs);
    }
  }

  private onLine(session: Session, line: string): void {
    let message: MpvMessage;
    try {
      message = JSON.parse(line) as MpvMessage;
    } catch {
      this.log(`Ignoring a line MPV sent that isn't JSON: ${line}`);
      return;
    }

    if (message.request_id !== undefined) {
      if (session.positionRequests.delete(message.request_id)) {
        if (typeof message.data === 'number') session.item.position = message.data;
        this.report(session);
      }
      return;
    }

    const item = session.item;
    if (message.event === 'start-file') {
      // Two quick replaces send two loadfiles: only the second start-file is the current file's.
      if (session.pendingStarts > 0) session.pendingStarts--;
      item.started = session.pendingStarts === 0;
      return;
    }
    // A replaced file's last events (its end-file, its final position) arrive before the new file starts.
    if (!item.started && message.event !== 'client-message' && !(message.event === 'property-change' && message.name === 'pause')) return;

    switch (message.event) {
      case 'property-change':
        if (message.name === 'time-pos') {
          item.position = typeof message.data === 'number' ? message.data : item.position;
          this.checkCredits(session);
        } else if (message.name === 'duration') {
          item.duration = typeof message.data === 'number' ? message.data : item.duration;
          this.checkCredits(session);
        } else if (message.name === 'chapter-list') {
          item.chapters = chaptersOf(message.data);
          this.checkCredits(session);
        } else if (message.name === 'eof-reached') {
          const reached = message.data === true;
          if (reached && !item.eofReached) {
            this.report(session);
            for (const listener of this.endReachedListeners) listener();
          }
          item.eofReached = reached;
        } else if (message.name === 'pause' && typeof message.data === 'boolean' && message.data !== session.paused) {
          session.paused = message.data;
          this.report(session);
        }
        break;
      case 'client-message': {
        const [name, choice] = message.args ?? [];
        if (name === upNextMessages.choice && (choice === 'play' || choice === 'cancel')) {
          for (const listener of this.upNextListeners) listener(choice);
        } else if (name === episodeMessages.choice && (choice === 'previous' || choice === 'next')) {
          for (const listener of this.episodeListeners) listener(choice);
        }
        break;
      }
      case 'file-loaded':
        for (const sub of item.opts.subtitles) {
          send(session.connection, ['sub-add', sub.url, sub.isDefault ? 'select' : 'auto', sub.title ?? sub.language ?? 'External', sub.language ?? '']);
        }
        if (item.opts.episodes) {
          send(session.connection, ['script-message', episodeMessages.show, item.opts.episodes.previous ?? '', item.opts.episodes.next ?? '']);
        }
        break;
      case 'seek':
        item.seeking = true;
        break;
      case 'playback-restart':
        // The seek has landed; ask for the position rather than trusting the order of property changes.
        if (item.seeking && session.connection) {
          item.seeking = false;
          const requestId = session.nextRequestId++;
          session.positionRequests.add(requestId);
          send(session.connection, ['get_property', 'time-pos'], requestId);
        }
        break;
      case 'end-file':
        if (message.reason === 'eof') this.close(session, 'eof');
        else if (message.reason === 'error') this.close(session, 'error', message.file_error ?? 'MPV could not play the stream.');
        else if (message.reason === 'quit' || message.reason === 'stop') this.close(session, 'quit');
        break;
    }
  }

  private onExited(session: Session): void {
    if (session.stopping || session.exitCode === 0) this.close(session, 'quit');
    else {
      const detail = session.exitError?.message ?? `MPV exited with code ${session.exitCode === null ? 'unknown' : String(session.exitCode)}.`;
      this.close(session, 'error', detail);
    }
    if (this.session === session) this.session = undefined;
  }

  /** Ends the current file and stops talking to MPV, which goes on to quit (`--idle=no`). */
  private close(session: Session, reason: PlayerEndReason, detail?: string): void {
    if (!session.connection) return;
    this.end(session, reason, detail);
    clearInterval(session.timer);
    session.connection.close();
  }

  /** Reports the current file's last progress and ends it once; only launches that connected ever end. */
  private end(session: Session, reason: PlayerEndReason, detail?: string): void {
    const item = session.item;
    if (item.ended || !session.connection) return;
    this.report(session);
    item.ended = true;
    for (const listener of this.endedListeners) {
      if (detail === undefined) listener(reason);
      else listener(reason, detail);
    }
  }

  /** Fires `onCredits` once per file, when the position first reaches the final credits. */
  private checkCredits(session: Session): void {
    const item = session.item;
    if (item.creditsReached || item.position === undefined) return;
    const at = upNextAt(item.chapters, item.duration);
    if (at === undefined || item.position < at) return;
    item.creditsReached = true;
    this.report(session);
    for (const listener of this.creditsListeners) listener();
  }

  private report(session: Session): void {
    const item = session.item;
    if (item.ended || item.position === undefined) return;
    const progress: PlayerProgress = {
      positionSeconds: item.position,
      durationSeconds: item.duration ?? 0,
      paused: session.paused,
    };
    for (const listener of this.progressListeners) listener(progress);
  }
}

function chaptersOf(data: unknown): Chapter[] {
  if (!Array.isArray(data)) return [];
  const chapters: Chapter[] = [];
  for (const raw of data as unknown[]) {
    if (typeof raw !== 'object' || raw === null) continue;
    const { title, time } = raw as Record<string, unknown>;
    if (typeof time !== 'number') continue;
    chapters.push(typeof title === 'string' ? { title, time } : { time });
  }
  return chapters;
}

// A function, so the connect loop sees the flag the exit listener sets across awaits.
function hasExited(session: Session): boolean {
  return session.exited;
}

/** Drops URL query strings and fragments from a line MPV printed, since stream URLs carry their session signature (SPEC §9). */
export function redactUrls(line: string): string {
  return line.replace(/(https?:\/\/[^\s?#]+)[?#]\S*/gi, '$1?…');
}

function send(connection: MpvConnection | undefined, command: unknown[], requestId?: number): void {
  if (!connection) return;
  const message = requestId === undefined ? { command } : { command, request_id: requestId };
  connection.write(`${JSON.stringify(message)}\n`);
}

function spawnChild(command: string, args: string[], log: (line: string) => void): MpvChild {
  // Not windowsHide: MPV's window is the point.
  const child = spawn(command, args, { stdio: ['ignore', 'pipe', 'pipe'] });
  const forward = (chunk: Buffer): void => {
    for (const line of chunk.toString().split(/\r?\n/)) if (line.trim()) log(redactUrls(line));
  };
  child.stdout.on('data', forward);
  child.stderr.on('data', forward);
  return {
    kill: (signal) => {
      child.kill(signal);
    },
    onExit: (listener) => {
      let done = false;
      child.once('exit', (code) => {
        if (!done) listener(code);
        done = true;
      });
      child.once('error', (error) => {
        if (!done) listener(null, error);
        done = true;
      });
    },
  };
}

function connectSocket(ipcPath: string): Promise<MpvConnection> {
  return new Promise((resolve, reject) => {
    const socket = net.createConnection(ipcPath);
    socket.once('error', reject);
    socket.once('connect', () => {
      socket.off('error', reject);
      socket.setEncoding('utf8');
      // After connecting, errors end the connection; MPV's exit is reported by the process.
      socket.on('error', () => undefined);
      resolve({
        write: (data) => {
          socket.write(data);
        },
        onData: (listener) => {
          socket.on('data', (chunk: string) => {
            listener(chunk);
          });
        },
        close: () => {
          socket.destroy();
        },
      });
    });
  });
}

function delay(ms: number): Promise<void> {
  return new Promise((resolve) => {
    setTimeout(resolve, ms);
  });
}

async function settlesWithin(promise: Promise<void>, ms: number): Promise<boolean> {
  let timer: NodeJS.Timeout | undefined;
  const timeout = new Promise<false>((resolve) => {
    timer = setTimeout(() => {
      resolve(false);
    }, ms);
  });
  try {
    return await Promise.race([promise.then(() => true as const), timeout]);
  } finally {
    clearTimeout(timer);
  }
}
