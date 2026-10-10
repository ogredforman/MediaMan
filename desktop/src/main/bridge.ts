// The main-process side of the preload bridge (src/preload/index.cts): the current server, its tokens and the player.
// Only the web UI's origin may call it, and it only ever acts on the current server.
import type { PlayOptions, SubtitleTrack, UpNextOffer } from './player.js';
import type { PlayerEvent, PlayerHost, PlayResult } from './playerHost.js';
import { parseStoredTokens, type StoredTokens, type TokenStore } from './tokenStore.js';

/** IPC channels; the preload repeats these strings because a sandboxed preload can't import local modules. */
export const bridgeChannels = {
  serverUrl: 'mediaman:server-url',
  tokensGet: 'mediaman:tokens-get',
  tokensSet: 'mediaman:tokens-set',
  tokensClear: 'mediaman:tokens-clear',
  playerPlay: 'mediaman:player-play',
  playerStop: 'mediaman:player-stop',
  playerUpNext: 'mediaman:player-up-next',
  playerChooseMpv: 'mediaman:player-choose-mpv',
} as const;

/** Channels the main process sends on; the preload repeats these too. */
export const bridgeEvents = {
  player: 'mediaman:player-event',
} as const;

/** The parts of an `IpcMainInvokeEvent` the bridge reads. */
export interface BridgeEvent {
  senderFrame: { url: string } | null;
  sender?: { send(channel: string, ...args: unknown[]): void; isDestroyed(): boolean };
}

export interface IpcMainLike {
  handle(channel: string, listener: (event: BridgeEvent, ...args: unknown[]) => unknown): void;
}

export interface BridgeOptions {
  uiOrigin: string;
  /** The server the desktop app started or connected to; undefined when the UI reaches the API on its own origin. */
  serverUrl: () => string | undefined;
  tokens: Pick<TokenStore, 'get' | 'set' | 'clear'>;
  player: Pick<PlayerHost, 'play' | 'stop' | 'upNext'>;
  /** Shows the native picker for the MPV executable and saves the choice; null when cancelled. */
  chooseMpv: () => Promise<string | null>;
}

export function registerBridge(ipc: IpcMainLike, options: BridgeOptions): void {
  const currentServer = (): string => options.serverUrl() ?? options.uiOrigin;
  const guard =
    <T>(handler: (event: BridgeEvent, ...args: unknown[]) => Promise<T> | T) =>
    (event: BridgeEvent, ...args: unknown[]): Promise<T> | T => {
      if (!isFromOrigin(event, options.uiOrigin)) throw new Error('This request did not come from the MediaMan UI.');
      return handler(event, ...args);
    };

  ipc.handle(bridgeChannels.serverUrl, guard(() => options.serverUrl() ?? null));
  ipc.handle(
    bridgeChannels.tokensGet,
    guard(async (): Promise<StoredTokens | null> => (await options.tokens.get(currentServer())) ?? null),
  );
  ipc.handle(
    bridgeChannels.tokensSet,
    guard(async (_, value: unknown) => {
      const tokens = parseStoredTokens(value);
      if (!tokens) throw new Error('These are not valid sign-in tokens.');
      await options.tokens.set(currentServer(), tokens);
    }),
  );
  ipc.handle(bridgeChannels.tokensClear, guard(() => options.tokens.clear(currentServer())));
  ipc.handle(
    bridgeChannels.playerPlay,
    guard((event, launchId: unknown, value: unknown): Promise<PlayResult> => {
      const sender = event.sender;
      if (!sender) throw new Error('This request has no window to report playback to.');
      if (typeof launchId !== 'string' || launchId === '') throw new Error('This play request has no launch id.');
      const opts = parsePlayOptions(value, new URL(currentServer()).origin);
      if (!opts) throw new Error("This play request isn't valid: MPV only plays streams from the current server.");
      return options.player.play(launchId, opts, (playerEvent: PlayerEvent) => {
        if (!sender.isDestroyed()) sender.send(bridgeEvents.player, playerEvent);
      });
    }),
  );
  ipc.handle(bridgeChannels.playerStop, guard(() => options.player.stop()));
  ipc.handle(
    bridgeChannels.playerUpNext,
    guard((_, launchId: unknown, value: unknown) => {
      const offer = parseUpNextOffer(value);
      if (typeof launchId !== 'string' || !offer) throw new Error("This Up next request isn't valid.");
      options.player.upNext(launchId, offer);
    }),
  );
  ipc.handle(bridgeChannels.playerChooseMpv, guard(() => options.chooseMpv()));
}

/** The renderer's play request, checked: every URL must be on `serverOrigin`, so MPV never opens anything else. */
export function parsePlayOptions(value: unknown, serverOrigin: string): PlayOptions | undefined {
  if (typeof value !== 'object' || value === null) return undefined;
  const v = value as Record<string, unknown>;
  const { url, startSeconds, title, subtitles } = v;
  if (!isServerUrl(url, serverOrigin) || typeof title !== 'string') return undefined;
  if (typeof startSeconds !== 'number' || !Number.isFinite(startSeconds) || startSeconds < 0) return undefined;
  if (!Array.isArray(subtitles)) return undefined;
  const tracks: SubtitleTrack[] = [];
  for (const raw of subtitles as unknown[]) {
    const track = parseSubtitle(raw, serverOrigin);
    if (!track) return undefined;
    tracks.push(track);
  }
  return { url, startSeconds, title, subtitles: tracks };
}

/** The renderer's "Up next" countdown, checked: a display title and a whole number of seconds up to a minute. */
export function parseUpNextOffer(value: unknown): UpNextOffer | undefined {
  if (typeof value !== 'object' || value === null) return undefined;
  const { title, seconds } = value as Record<string, unknown>;
  if (typeof title !== 'string' || title.trim() === '' || title.length > maxUpNextTitle) return undefined;
  if (typeof seconds !== 'number' || !Number.isInteger(seconds) || seconds < 1 || seconds > 60) return undefined;
  return { title, seconds };
}

const maxUpNextTitle = 500;

function parseSubtitle(value: unknown, serverOrigin: string): SubtitleTrack | undefined {
  if (typeof value !== 'object' || value === null) return undefined;
  const { url, language, title, isDefault } = value as Record<string, unknown>;
  if (!isServerUrl(url, serverOrigin)) return undefined;
  if (language !== undefined && typeof language !== 'string') return undefined;
  if (title !== undefined && typeof title !== 'string') return undefined;
  if (isDefault !== undefined && typeof isDefault !== 'boolean') return undefined;
  return {
    url,
    ...(language === undefined ? {} : { language }),
    ...(title === undefined ? {} : { title }),
    ...(isDefault === undefined ? {} : { isDefault }),
  };
}

function isServerUrl(value: unknown, serverOrigin: string): value is string {
  if (typeof value !== 'string') return false;
  try {
    const url = new URL(value);
    return (url.protocol === 'http:' || url.protocol === 'https:') && url.origin === serverOrigin;
  } catch {
    return false;
  }
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
