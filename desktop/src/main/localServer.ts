// The server the desktop app starts in Local mode and stops when it quits (SPEC §10.1, §10.3). Kept free of runtime
// Electron imports so the lifecycle can be tested through `IServerProcess` with `FakeServerProcess`.
import type { IServerProcess, ServerCommand } from './serverProcess.js';

export interface LocalServer {
  server: IServerProcess;
  baseUrl: string;
}

/**
 * Starts the server `command` names (`resolveServerCommand`): the dev override or the packaged app's own server.
 * No command means no server to start (in development one runs on its own). A failed start rejects with the server's
 * message, which says what to try.
 */
export async function startLocalServer(
  command: ServerCommand | undefined,
  create: (command: ServerCommand) => IServerProcess,
): Promise<LocalServer | undefined> {
  if (!command) return undefined;
  const server = create(command);
  const { baseUrl } = await server.start();
  return { server, baseUrl };
}

/** The part of Electron's `before-quit` event this needs. */
export interface QuitEvent {
  preventDefault(): void;
}

/**
 * A `before-quit` handler that holds the first quit until the server this app started has stopped, then calls `quit`
 * again; that and any later quit go through. Without a server every quit goes through.
 */
export function stopServerOnQuit(
  server: () => IServerProcess | undefined,
  quit: () => void,
  log: (message: string) => void = console.error,
): (event: QuitEvent) => void {
  let stopping = false;
  return (event) => {
    const running = server();
    if (!running || stopping) return;
    event.preventDefault();
    stopping = true;
    void running
      .stop()
      .catch((err: unknown) => {
        log(`Could not stop the MediaMan server: ${err instanceof Error ? err.message : String(err)}`);
      })
      .finally(quit);
  };
}
