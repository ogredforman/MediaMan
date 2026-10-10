// The main-process side of the preload bridge (src/preload/index.cts): the current server and its tokens.
// Only the web UI's origin may call it, and it only ever acts on the current server.
import { parseStoredTokens, type StoredTokens, type TokenStore } from './tokenStore.js';

/** IPC channels; the preload repeats these strings because a sandboxed preload can't import local modules. */
export const bridgeChannels = {
  serverUrl: 'mediaman:server-url',
  tokensGet: 'mediaman:tokens-get',
  tokensSet: 'mediaman:tokens-set',
  tokensClear: 'mediaman:tokens-clear',
} as const;

/** The parts of an `IpcMainInvokeEvent` the bridge reads. */
export interface BridgeEvent {
  senderFrame: { url: string } | null;
}

export interface IpcMainLike {
  handle(channel: string, listener: (event: BridgeEvent, ...args: unknown[]) => unknown): void;
}

export interface BridgeOptions {
  uiOrigin: string;
  /** The server the desktop app started or connected to; undefined when the UI reaches the API on its own origin. */
  serverUrl: () => string | undefined;
  tokens: Pick<TokenStore, 'get' | 'set' | 'clear'>;
}

export function registerBridge(ipc: IpcMainLike, options: BridgeOptions): void {
  const currentServer = (): string => options.serverUrl() ?? options.uiOrigin;
  const guard =
    <T>(handler: (...args: unknown[]) => Promise<T> | T) =>
    (event: BridgeEvent, ...args: unknown[]): Promise<T> | T => {
      if (!isFromOrigin(event, options.uiOrigin)) throw new Error('This request did not come from the MediaMan UI.');
      return handler(...args);
    };

  ipc.handle(bridgeChannels.serverUrl, guard(() => options.serverUrl() ?? null));
  ipc.handle(
    bridgeChannels.tokensGet,
    guard(async (): Promise<StoredTokens | null> => (await options.tokens.get(currentServer())) ?? null),
  );
  ipc.handle(
    bridgeChannels.tokensSet,
    guard(async (value: unknown) => {
      const tokens = parseStoredTokens(value);
      if (!tokens) throw new Error('These are not valid sign-in tokens.');
      await options.tokens.set(currentServer(), tokens);
    }),
  );
  ipc.handle(bridgeChannels.tokensClear, guard(() => options.tokens.clear(currentServer())));
}

export function isFromOrigin(event: BridgeEvent, origin: string): boolean {
  const url = event.senderFrame?.url;
  if (!url) return false;
  try {
    return new URL(url).origin === origin;
  } catch {
    return false;
  }
}
