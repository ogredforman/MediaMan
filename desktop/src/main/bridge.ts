// The main-process side of the preload bridge (src/preload/index.cts): the current server, its tokens and the player.
// Only the web UI's origin may call it, and it only ever acts on the current server.
import type { MpvSetting } from './desktopSettings.js';
import type { EpisodeNeighbours, PlayerCommand, PlayerState, PlayOptions, SubtitleTrack, UpNextOffer } from './player.js';
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
  playerNotice: 'mediaman:player-notice',
  playerChooseMpv: 'mediaman:player-choose-mpv',
  playerMpvSetting: 'mediaman:player-mpv-setting',
  playerUseDefaultMpv: 'mediaman:player-use-default-mpv',
  /** Overlay only: a `PlayerCommand` for the file that plays. */
  playerCommand: 'mediaman:player-command',
} as const;

/** Channels the main process sends on; the preload repeats these too. */
export const bridgeEvents = {
  player: 'mediaman:player-event',
  /** Overlay only: the whole `PlayerState` on every change. */
  playerState: 'mediaman:player-state',
} as const;

/** The parts of an `IpcMainInvokeEvent` the bridge reads. */
export interface BridgeEvent {
  senderFrame: { url: string } | null;
  sender?: BridgeSender;
}

/** The parts of a `WebContents` the bridge sends to. */
export interface BridgeSender {
  send(channel: string, ...args: unknown[]): void;
  isDestroyed(): boolean;
}

export interface IpcMainLike {
  handle(channel: string, listener: (event: BridgeEvent, ...args: unknown[]) => unknown): void;
}

/** What `chooseMpv` did: saved a pick, was cancelled, or refused a file that isn't MPV (never run, never saved). */
export type MpvChoice = { outcome: 'picked'; path: string } | { outcome: 'cancelled' } | { outcome: 'refused'; message: string };

export interface BridgeOptions {
  uiOrigin: string;
  /** The server the desktop app started or connected to; undefined when the UI reaches the API on its own origin. */
  serverUrl: () => string | undefined;
  tokens: Pick<TokenStore, 'get' | 'set' | 'clear'>;
  player: Pick<PlayerHost, 'play' | 'stop' | 'upNext' | 'notice' | 'command' | 'onState'>;
  /**
   * The player overlay window's contents (ADR 0011), the only sender whose commands are taken and the only receiver
   * of the player state; undefined while there is none.
   */
  overlay?: () => BridgeSender | undefined;
  /** Shows the native picker for the MPV executable and saves the choice if it is MPV. */
  chooseMpv: () => Promise<MpvChoice>;
  /** The MPV the next Play uses and where it comes from. */
  mpvSetting: () => Promise<MpvSetting>;
  /** Forgets the picked MPV; resolves to the setting now in use. */
  useDefaultMpv: () => Promise<MpvSetting>;
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
  ipc.handle(
    bridgeChannels.playerNotice,
    guard((_, launchId: unknown, text: unknown) => {
      if (typeof launchId !== 'string' || !isNoticeText(text)) throw new Error("This notice request isn't valid.");
      options.player.notice(launchId, text);
    }),
  );
  options.player.onState((state: PlayerState) => {
    const overlay = options.overlay?.();
    if (overlay && !overlay.isDestroyed()) overlay.send(bridgeEvents.playerState, state);
  });
  ipc.handle(
    bridgeChannels.playerCommand,
    guard((event, value: unknown) => {
      const overlay = options.overlay?.();
      if (!overlay || event.sender !== overlay) throw new Error('Only the player overlay can control playback.');
      const cmd = parsePlayerCommand(value);
      if (!cmd) throw new Error("This player command isn't valid.");
      options.player.command(cmd);
    }),
  );
  ipc.handle(bridgeChannels.playerChooseMpv, guard(() => options.chooseMpv()));
  ipc.handle(bridgeChannels.playerMpvSetting, guard(() => options.mpvSetting()));
  ipc.handle(bridgeChannels.playerUseDefaultMpv, guard(() => options.useDefaultMpv()));
}

/** The renderer's play request, checked: every URL must be on `serverOrigin`, so MPV never opens anything else. */
export function parsePlayOptions(value: unknown, serverOrigin: string): PlayOptions | undefined {
  if (typeof value !== 'object' || value === null) return undefined;
  const v = value as Record<string, unknown>;
  const { url, startSeconds, title, subtitles, audioLanguages, subtitleLanguages, episodes } = v;
  if (!isServerUrl(url, serverOrigin) || typeof title !== 'string') return undefined;
  if (typeof startSeconds !== 'number' || !Number.isFinite(startSeconds) || startSeconds < 0) return undefined;
  if (!Array.isArray(subtitles)) return undefined;
  const tracks: SubtitleTrack[] = [];
  for (const raw of subtitles as unknown[]) {
    const track = parseSubtitle(raw, serverOrigin);
    if (!track) return undefined;
    tracks.push(track);
  }
  const audio = parseLanguageTags(audioLanguages);
  const subtitle = parseLanguageTags(subtitleLanguages);
  if (audio === null || subtitle === null) return undefined;
  const neighbours = parseEpisodes(episodes);
  if (neighbours === null) return undefined;
  return {
    url,
    startSeconds,
    title,
    subtitles: tracks,
    ...(audio === undefined ? {} : { audioLanguages: audio }),
    ...(subtitle === undefined ? {} : { subtitleLanguages: subtitle }),
    ...(neighbours === undefined ? {} : { episodes: neighbours }),
  };
}

/** An episode's neighbours: absent is fine (undefined), otherwise optional display titles like Up next's; else null. */
function parseEpisodes(value: unknown): EpisodeNeighbours | null | undefined {
  if (value === undefined) return undefined;
  if (typeof value !== 'object' || value === null) return null;
  const neighbours: EpisodeNeighbours = {};
  for (const key of ['previous', 'next'] as const) {
    const title = (value as Record<string, unknown>)[key];
    if (title === undefined) continue;
    if (typeof title !== 'string' || title.trim() === '' || title.length > maxUpNextTitle) return null;
    neighbours[key] = title;
  }
  return neighbours;
}

/**
 * Language tags for `--alang`/`--slang`: absent is fine (undefined), otherwise up to 30 lowercase 2–3 letter tags,
 * so none can carry a comma or an option into MPV's command line; anything else is null.
 */
function parseLanguageTags(value: unknown): string[] | null | undefined {
  if (value === undefined) return undefined;
  if (!Array.isArray(value) || value.length > maxLanguageTags) return null;
  const tags: string[] = [];
  for (const tag of value as unknown[]) {
    if (typeof tag !== 'string' || !/^[a-z]{2,3}$/.test(tag)) return null;
    tags.push(tag);
  }
  return tags;
}

// Five preferences of up to six tags each (`no` has the most: `no,nor,nb,nob,nn,nno`).
const maxLanguageTags = 30;

/**
 * The overlay's command, checked and copied field by field: finite numbers only, a volume of 0–100, a seek target
 * from 0, track ids that are positive whole numbers (or null to turn subtitles off).
 */
export function parsePlayerCommand(value: unknown): PlayerCommand | undefined {
  if (typeof value !== 'object' || value === null) return undefined;
  const v = value as Record<string, unknown>;
  switch (v.type) {
    case 'togglePause':
    case 'toggleMute':
      return { type: v.type };
    case 'seekTo':
      return isFiniteNumber(v.seconds) && v.seconds >= 0 ? { type: 'seekTo', seconds: v.seconds } : undefined;
    case 'seekBy':
      return isFiniteNumber(v.seconds) ? { type: 'seekBy', seconds: v.seconds } : undefined;
    case 'setVolume':
      return isFiniteNumber(v.volume) && v.volume >= 0 && v.volume <= 100 ? { type: 'setVolume', volume: v.volume } : undefined;
    case 'selectAudio':
      return isTrackId(v.trackId) ? { type: 'selectAudio', trackId: v.trackId } : undefined;
    case 'selectSubtitle':
      return v.trackId === null || isTrackId(v.trackId) ? { type: 'selectSubtitle', trackId: v.trackId } : undefined;
    case 'episode':
      return v.choice === 'previous' || v.choice === 'next' ? { type: 'episode', choice: v.choice } : undefined;
    default:
      return undefined;
  }
}

function isFiniteNumber(value: unknown): value is number {
  return typeof value === 'number' && Number.isFinite(value);
}

function isTrackId(value: unknown): value is number {
  return typeof value === 'number' && Number.isInteger(value) && value > 0;
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

/** A message for MPV's OSD: one line of display text, nothing that could read as an MPV command or option. */
function isNoticeText(value: unknown): value is string {
  return typeof value === 'string' && value.trim() !== '' && value.length <= maxUpNextTitle;
}

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
