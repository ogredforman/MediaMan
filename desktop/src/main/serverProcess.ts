// The local MediaMan server the desktop app spawns in Local mode (SPEC §10.1, §10.3).
import { spawn } from 'node:child_process';

export interface IServerProcess {
  start(): Promise<{ baseUrl: string }>;
  stop(): Promise<void>;
}

/** The server's default port (`ServerOptions.DefaultPort`). */
export const DEFAULT_SERVER_PORT = 8420;

export interface ServerCommand {
  command: string;
  args: string[];
}

/** A spawned server, narrowed so tests can stand in for a real child process. */
export interface ServerChild {
  kill(signal: 'SIGTERM' | 'SIGKILL'): void;
  /** Called once, when the process exits or fails to start. */
  onExit(listener: (code: number | null, error?: Error) => void): void;
}

export type SpawnServer = (command: ServerCommand, env: NodeJS.ProcessEnv, log: (line: string) => void) => ServerChild;

export interface ServerProcessOptions {
  command: ServerCommand;
  port?: number;
  env?: NodeJS.ProcessEnv;
  /** The web UI's origin, which the spawned server lets call its API (CORS, `MediaMan:AllowedOrigins`). */
  allowedOrigin?: string;
  healthTimeoutMs?: number;
  pollIntervalMs?: number;
  stopGraceMs?: number;
  spawn?: SpawnServer;
  fetch?: typeof fetch;
  log?: (line: string) => void;
}

/**
 * `MEDIAMAN_SERVER_PATH` is the server build to run: a `.dll` runs under `dotnet`, anything else is run directly.
 * Unset means the desktop app doesn't start a server (in development one runs on its own, see docs/LOCAL-DEV.md).
 */
export function resolveServerCommand(env: Readonly<Record<string, string | undefined>>): ServerCommand | undefined {
  const raw = env.MEDIAMAN_SERVER_PATH?.trim();
  if (!raw) return undefined;
  return raw.toLowerCase().endsWith('.dll') ? { command: 'dotnet', args: [raw] } : { command: raw, args: [] };
}

/** `MEDIAMAN_SERVER_PORT`, or the server's default port. */
export function resolveServerPort(env: Readonly<Record<string, string | undefined>>): number {
  const raw = env.MEDIAMAN_SERVER_PORT?.trim();
  if (!raw) return DEFAULT_SERVER_PORT;
  const port = Number(raw);
  if (!Number.isInteger(port) || port < 1 || port > 65535) {
    throw new Error(`MEDIAMAN_SERVER_PORT "${raw}" is not a port. Set it to a number from 1 to 65535, or unset it to use ${String(DEFAULT_SERVER_PORT)}.`);
  }
  return port;
}

/**
 * Starts the server on loopback and resolves once `GET /health` answers. If a MediaMan server already answers on the
 * port, it is used as is and left running on stop, since this app didn't start it.
 */
export class ServerProcess implements IServerProcess {
  private readonly port: number;
  private readonly baseUrl: string;
  private readonly healthTimeoutMs: number;
  private readonly pollIntervalMs: number;
  private readonly stopGraceMs: number;
  private readonly spawnServer: SpawnServer;
  private readonly fetchFn: typeof fetch;
  private readonly log: (line: string) => void;
  private starting: Promise<{ baseUrl: string }> | undefined;
  private child: ServerChild | undefined;
  private exit: Promise<void> | undefined;
  private exited = false;

  constructor(private readonly options: ServerProcessOptions) {
    this.port = options.port ?? DEFAULT_SERVER_PORT;
    this.baseUrl = `http://127.0.0.1:${String(this.port)}`;
    this.healthTimeoutMs = options.healthTimeoutMs ?? 30_000;
    this.pollIntervalMs = options.pollIntervalMs ?? 250;
    this.stopGraceMs = options.stopGraceMs ?? 5_000;
    this.spawnServer = options.spawn ?? spawnChild;
    this.fetchFn = options.fetch ?? fetch;
    this.log =
      options.log ??
      ((line) => {
        console.log(`[server] ${line}`);
      });
  }

  start(): Promise<{ baseUrl: string }> {
    this.starting ??= this.run().catch((err: unknown) => {
      this.starting = undefined;
      throw err;
    });
    return this.starting;
  }

  async stop(): Promise<void> {
    this.starting = undefined;
    const child = this.child;
    const exit = this.exit;
    this.child = undefined;
    if (!child || !exit || this.exited) return;

    child.kill('SIGTERM');
    if (await settlesWithin(exit, this.stopGraceMs)) return;
    this.log(`The server didn't exit within ${String(this.stopGraceMs / 1000)} s; killing it.`);
    child.kill('SIGKILL');
    await exit;
  }

  private async run(): Promise<{ baseUrl: string }> {
    if (await this.isHealthy()) {
      this.log(`Using the MediaMan server already running at ${this.baseUrl}.`);
      return { baseUrl: this.baseUrl };
    }

    let exitDetail: string | undefined;
    this.exited = false;
    const env: NodeJS.ProcessEnv = { ...(this.options.env ?? process.env), MediaMan__Server__Port: String(this.port) };
    if (this.options.allowedOrigin) env.MediaMan__AllowedOrigins__0 = this.options.allowedOrigin;
    const child = this.spawnServer(this.options.command, env, this.log);
    this.child = child;
    this.exit = new Promise<void>((resolve) => {
      child.onExit((code, error) => {
        this.exited = true;
        exitDetail = error ? error.message : `exit code ${code === null ? 'unknown' : String(code)}`;
        resolve();
      });
    });

    const deadline = Date.now() + this.healthTimeoutMs;
    for (;;) {
      if (this.hasExited()) {
        this.child = undefined;
        throw new Error(
          `The MediaMan server stopped before it was ready (${exitDetail ?? 'no detail'}). Check the server output, and that MEDIAMAN_SERVER_PATH points at a working server build.`,
        );
      }
      if (await this.isHealthy()) return { baseUrl: this.baseUrl };
      if (Date.now() >= deadline) {
        await this.stop();
        throw new Error(
          `The MediaMan server didn't answer at ${this.baseUrl}/health within ${String(this.healthTimeoutMs / 1000)} s. Check the server output, or free port ${String(this.port)} (MEDIAMAN_SERVER_PORT picks another).`,
        );
      }
      await delay(this.pollIntervalMs);
    }
  }

  // A method, so the start loop sees the flag the exit listener sets.
  private hasExited(): boolean {
    return this.exited;
  }

  private async isHealthy(): Promise<boolean> {
    try {
      const response = await this.fetchFn(`${this.baseUrl}/health`, { signal: AbortSignal.timeout(2_000) });
      if (!response.ok) return false;
      const body = (await response.json()) as { status?: unknown } | null;
      return body?.status === 'healthy';
    } catch {
      return false;
    }
  }
}

function spawnChild(command: ServerCommand, env: NodeJS.ProcessEnv, log: (line: string) => void): ServerChild {
  const child = spawn(command.command, command.args, { env, stdio: ['ignore', 'pipe', 'pipe'], windowsHide: true });
  const forward = (chunk: Buffer): void => {
    for (const line of chunk.toString().split(/\r?\n/)) if (line.trim()) log(line);
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
