import path from 'node:path';
import { describe, expect, it } from 'vitest';
import {
  DEFAULT_SERVER_PORT,
  BUNDLED_SERVER_HINT,
  resolveServerCommand,
  resolveServerPort,
  ServerProcess,
  type ServerChild,
  type ServerCommand,
} from './serverProcess.js';

/** A stand-in child process: tests decide when it exits and whether it ignores SIGTERM. */
class FakeChild implements ServerChild {
  readonly signals: string[] = [];
  ignoreTerm = false;
  private listener: ((code: number | null, error?: Error) => void) | undefined;
  private pendingExit: [number | null, Error | undefined] | undefined;

  kill(signal: 'SIGTERM' | 'SIGKILL'): void {
    this.signals.push(signal);
    if (signal === 'SIGKILL' || !this.ignoreTerm) this.exit(signal === 'SIGKILL' ? null : 0);
  }

  onExit(listener: (code: number | null, error?: Error) => void): void {
    this.listener = listener;
    if (this.pendingExit) this.exit(...this.pendingExit);
  }

  /** Exits now, or as soon as the server process starts listening for the exit. */
  exit(code: number | null, error?: Error): void {
    if (!this.listener) {
      this.pendingExit = [code, error];
      return;
    }
    this.pendingExit = undefined;
    const listener = this.listener;
    this.listener = undefined;
    listener(code, error);
  }
}

/** Serves `/health` only while `healthy` is true; records every URL it was asked for. */
class FakeHealth {
  healthy = false;
  readonly urls: string[] = [];
  readonly fetch = ((input: string | URL | Request) => {
    this.urls.push(input instanceof Request ? input.url : input.toString());
    if (!this.healthy) return Promise.reject(new TypeError('fetch failed'));
    return Promise.resolve(Response.json({ status: 'healthy', version: '0.0.0' }));
  }) as typeof fetch;
}

function setUp(
  options: {
    healthy?: boolean;
    healthyAfterSpawn?: boolean;
    ignoreTerm?: boolean;
    allowedOrigin?: string;
    startFailureHint?: string;
  } = {},
) {
  const health = new FakeHealth();
  health.healthy = options.healthy ?? false;
  const child = new FakeChild();
  child.ignoreTerm = options.ignoreTerm ?? false;
  const spawned: { command: ServerCommand; env: NodeJS.ProcessEnv }[] = [];
  const server = new ServerProcess({
    command: { command: 'dotnet', args: ['MediaMan.Server.dll'] },
    port: 9123,
    env: { PATH: '/bin' },
    ...(options.allowedOrigin === undefined ? {} : { allowedOrigin: options.allowedOrigin }),
    ...(options.startFailureHint === undefined ? {} : { startFailureHint: options.startFailureHint }),
    healthTimeoutMs: 200,
    pollIntervalMs: 5,
    stopGraceMs: 20,
    fetch: health.fetch,
    log: () => undefined,
    spawn: (command, env) => {
      spawned.push({ command, env });
      if (options.healthyAfterSpawn) health.healthy = true;
      return child;
    },
  });
  return { server, health, child, spawned };
}

describe('ServerProcess', () => {
  it('spawns the server on the port and resolves once /health answers', async () => {
    const { server, health, spawned } = setUp({ healthyAfterSpawn: true });

    await expect(server.start()).resolves.toEqual({ baseUrl: 'http://127.0.0.1:9123' });

    expect(spawned).toHaveLength(1);
    expect(spawned[0]?.command).toEqual({ command: 'dotnet', args: ['MediaMan.Server.dll'] });
    expect(spawned[0]?.env).toEqual({ PATH: '/bin', MediaMan__Server__Port: '9123' });
    expect(health.urls.at(-1)).toBe('http://127.0.0.1:9123/health');
  });

  it('lets the UI origin call the spawned server', async () => {
    const { server, spawned } = setUp({ healthyAfterSpawn: true, allowedOrigin: 'http://localhost:5173' });

    await server.start();

    expect(spawned[0]?.env).toEqual({
      PATH: '/bin',
      MediaMan__Server__Port: '9123',
      MediaMan__AllowedOrigins__0: 'http://localhost:5173',
    });
  });

  it('starts once however often start is called', async () => {
    const { server, spawned } = setUp({ healthyAfterSpawn: true });

    await Promise.all([server.start(), server.start()]);

    expect(spawned).toHaveLength(1);
  });

  it('uses a server already answering on the port and leaves it running on stop', async () => {
    const { server, spawned, child } = setUp({ healthy: true });

    await expect(server.start()).resolves.toEqual({ baseUrl: 'http://127.0.0.1:9123' });
    await server.stop();

    expect(spawned).toHaveLength(0);
    expect(child.signals).toEqual([]);
  });

  it('fails with the exit detail when the server stops before it is ready', async () => {
    const { server, child } = setUp();

    const start = server.start();
    child.exit(3);

    await expect(start).rejects.toThrow(/stopped before it was ready \(exit code 3\).*MEDIAMAN_SERVER_PATH/);
  });

  it('says what to try instead when given a hint, as the packaged app does', async () => {
    const { server, child } = setUp({ startFailureHint: BUNDLED_SERVER_HINT });

    const start = server.start();
    child.exit(3);

    await expect(start).rejects.toThrow(`stopped before it was ready (exit code 3). ${BUNDLED_SERVER_HINT}`);
  });

  it('fails when the server can not be launched at all', async () => {
    const { server, child } = setUp();

    const start = server.start();
    child.exit(null, new Error('spawn dotnet ENOENT'));

    await expect(start).rejects.toThrow(/spawn dotnet ENOENT/);
  });

  it('times out, stops the server and names the port when /health never answers', async () => {
    const { server, child } = setUp();

    await expect(server.start()).rejects.toThrow(/didn't answer at http:\/\/127\.0\.0\.1:9123\/health.*port 9123/);
    expect(child.signals).toEqual(['SIGTERM']);
  });

  it('can start again after a failed start', async () => {
    const { server, child, health, spawned } = setUp();
    const first = server.start();
    child.exit(1);
    await expect(first).rejects.toThrow();

    health.healthy = true;

    await expect(server.start()).resolves.toEqual({ baseUrl: 'http://127.0.0.1:9123' });
    expect(spawned).toHaveLength(1);
  });

  it('asks the server to exit on stop', async () => {
    const { server, child } = setUp({ healthyAfterSpawn: true });
    await server.start();

    await server.stop();

    expect(child.signals).toEqual(['SIGTERM']);
  });

  it('kills the server when it ignores the request to exit', async () => {
    const { server, child } = setUp({ healthyAfterSpawn: true, ignoreTerm: true });
    await server.start();

    await server.stop();

    expect(child.signals).toEqual(['SIGTERM', 'SIGKILL']);
  });

  it('does nothing on stop when the server already exited', async () => {
    const { server, child } = setUp({ healthyAfterSpawn: true });
    await server.start();
    child.exit(0);

    await server.stop();

    expect(child.signals).toEqual([]);
  });
});

describe('resolveServerCommand', () => {
  it('is undefined when MEDIAMAN_SERVER_PATH is unset or blank', () => {
    expect(resolveServerCommand({})).toBeUndefined();
    expect(resolveServerCommand({ MEDIAMAN_SERVER_PATH: '  ' })).toBeUndefined();
  });

  it('runs the bundled server without MEDIAMAN_SERVER_PATH, and the variable overrides it', () => {
    const bundled = path.resolve('install', 'resources', 'server', 'MediaMan.Server.exe');
    expect(resolveServerCommand({}, process.cwd(), bundled)).toEqual({ command: bundled, args: [] });
    expect(resolveServerCommand({ MEDIAMAN_SERVER_PATH: '/srv/MediaMan.Server.dll' }, process.cwd(), bundled)).toEqual({
      command: 'dotnet',
      args: ['/srv/MediaMan.Server.dll'],
    });
  });

  it('runs a .dll under dotnet', () => {
    expect(resolveServerCommand({ MEDIAMAN_SERVER_PATH: '/srv/MediaMan.Server.DLL' })).toEqual({
      command: 'dotnet',
      args: ['/srv/MediaMan.Server.DLL'],
    });
  });

  it('runs anything else directly', () => {
    expect(resolveServerCommand({ MEDIAMAN_SERVER_PATH: '/opt/mediaman/MediaMan.Server' })).toEqual({
      command: '/opt/mediaman/MediaMan.Server',
      args: [],
    });
  });

  it('resolves a relative path from where npm was run, not the desktop workspace', () => {
    const root = path.resolve('repo');
    const relative = path.join('server', 'bin', 'MediaMan.Server.dll');
    expect(resolveServerCommand({ MEDIAMAN_SERVER_PATH: relative, INIT_CWD: root }, path.join(root, 'desktop'))).toEqual({
      command: 'dotnet',
      args: [path.join(root, relative)],
    });
  });

  it('resolves a relative path from the working directory outside npm', () => {
    const cwd = path.resolve('somewhere');
    expect(resolveServerCommand({ MEDIAMAN_SERVER_PATH: 'MediaMan.Server' }, cwd)).toEqual({
      command: path.join(cwd, 'MediaMan.Server'),
      args: [],
    });
  });
});

describe('resolveServerPort', () => {
  it('defaults to the server default port', () => {
    expect(resolveServerPort({})).toBe(DEFAULT_SERVER_PORT);
  });

  it('uses MEDIAMAN_SERVER_PORT', () => {
    expect(resolveServerPort({ MEDIAMAN_SERVER_PORT: '9000' })).toBe(9000);
  });

  it.each(['0', '65536', '80.5', 'http'])('rejects %s', (value) => {
    expect(() => resolveServerPort({ MEDIAMAN_SERVER_PORT: value })).toThrow(/MEDIAMAN_SERVER_PORT/);
  });
});
