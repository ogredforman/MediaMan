// The desktop app's preload bridge (desktop/src/preload/index.cts), present only when the UI runs inside it.
import { setApiBaseUrl } from './http';
import { keepTokensOnDesktop } from './session';

/** The current server's sign-in tokens, kept encrypted by the desktop app's main process. */
export interface DesktopTokenBridge {
  get(): Promise<unknown>;
  set(tokens: unknown): Promise<void>;
  clear(): Promise<void>;
}

/** What MPV plays; every URL must be on the current server. Mirrors the desktop's `PlayOptions`. */
export interface DesktopPlayOptions {
  url: string;
  startSeconds: number;
  title: string;
  subtitles: { url: string; language?: string; title?: string; isDefault?: boolean }[];
  audioLanguages: string[];
  subtitleLanguages: string[];
  /** An episode's neighbours by display title, offered as Previous and Next episode in the player. */
  episodes?: { previous?: string; next?: string };
}

/** The "Up next" countdown the player overlay shows: the next episode's title and the seconds before it starts. */
export interface DesktopUpNextOffer {
  title: string;
  seconds: number;
}

/**
 * Events from MPV, tagged with the `launchId` passed to `play`. `credits` is the start of the final credits (once),
 * `endReached` the end of the file with MPV holding the last frame, and `upNext` the viewer's answer to the countdown.
 */
export type DesktopPlayerEvent =
  | { launchId: string; type: 'progress'; positionSeconds: number; durationSeconds: number; paused: boolean }
  | { launchId: string; type: 'ended'; reason: 'eof' | 'quit' | 'error'; detail?: string }
  | { launchId: string; type: 'credits' }
  | { launchId: string; type: 'endReached' }
  | { launchId: string; type: 'upNext'; choice: 'play' | 'cancel' }
  | { launchId: string; type: 'episode'; choice: 'previous' | 'next' };

/** The MPV the next Play uses: picked in Settings, `MEDIAMAN_MPV_PATH`, bundled, or `mpv` from PATH. Mirrors the desktop's `MpvSetting`. */
export interface DesktopMpvSetting {
  path: string;
  source: 'picked' | 'environment' | 'bundled' | 'default';
}

/** What Choose MPV did; a refused file isn't MPV and was neither run nor saved. Mirrors the desktop's `MpvChoice`. */
export type DesktopMpvChoice = { outcome: 'picked'; path: string } | { outcome: 'cancelled' } | { outcome: 'refused'; message: string };

/** An audio or subtitle track of the playing file; `id` is what the select commands take. Mirrors the desktop's `PlayerTrack`. */
export interface DesktopPlayerTrack {
  id: number;
  language?: string;
  title?: string;
  selected: boolean;
}

/** Everything the player overlay draws, whole on every change. Mirrors the desktop's `PlayerState`. */
export interface DesktopPlayerState {
  positionSeconds: number;
  /** 0 until known. */
  durationSeconds: number;
  /** Where the cached part of the stream ends; 0 until known. */
  bufferedSeconds: number;
  paused: boolean;
  /** Stalled waiting for the network. */
  buffering: boolean;
  /** 0–100. */
  volume: number;
  muted: boolean;
  audioTracks: DesktopPlayerTrack[];
  /** None selected means subtitles are off. */
  subtitleTracks: DesktopPlayerTrack[];
  chapters: { title?: string; time: number }[];
  title: string;
  episodes?: { previous?: string; next?: string };
}

/** What the overlay draws: the player state, the main window's fullscreen, the Up next on offer and a notice. */
export interface DesktopOverlayState extends DesktopPlayerState {
  fullscreen: boolean;
  upNext?: DesktopUpNextOffer;
  notice?: string;
}

/**
 * What the overlay asks of the main process besides player commands: `ready` once it listens (the latest state is
 * sent again), Back (stops playback), fullscreen on the main window, and the answer to Up next.
 */
export type DesktopOverlayAction =
  | { type: 'ready' }
  | { type: 'back' }
  | { type: 'toggleFullscreen' }
  | { type: 'upNext'; choice: 'play' | 'cancel' };

/** What the overlay asks of the player. Mirrors the desktop's `PlayerCommand`. */
export type DesktopPlayerCommand =
  | { type: 'togglePause' }
  | { type: 'seekTo'; seconds: number }
  | { type: 'seekBy'; seconds: number }
  | { type: 'setVolume'; volume: number }
  | { type: 'toggleMute' }
  | { type: 'selectAudio'; trackId: number }
  // A null trackId turns subtitles off.
  | { type: 'selectSubtitle'; trackId: number | null }
  | { type: 'episode'; choice: 'previous' | 'next' };

export type DesktopPlayResult = { ok: true } | { ok: false; error: 'mpvStart'; message: string };

/** MPV, one launch at a time, run by the desktop app's main process. */
export interface DesktopPlayerBridge {
  play(launchId: string, options: DesktopPlayOptions): Promise<DesktopPlayResult>;
  stop(): Promise<void>;
  /** Offers the "Up next" countdown in the player overlay, if `launchId` is still what plays. */
  upNext(launchId: string, offer: DesktopUpNextOffer): Promise<void>;
  /** Shows a message over the playback (e.g. a lost server), if `launchId` is still what plays. */
  notice(launchId: string, text: string): Promise<void>;
  /** The native picker for the MPV executable; saves the pick only when it is MPV. */
  chooseMpv(): Promise<DesktopMpvChoice>;
  /** The MPV the next Play uses and where it comes from. */
  mpvSetting(): Promise<DesktopMpvSetting>;
  /** Forgets the picked MPV; resolves to the setting now in use. */
  useDefaultMpv(): Promise<DesktopMpvSetting>;
  /** Returns the unsubscribe. */
  onEvent(callback: (event: DesktopPlayerEvent) => void): () => void;
  /** Overlay window only (refused from any other): acts on the file that plays. */
  command(command: DesktopPlayerCommand): Promise<void>;
  /** Overlay window only (refused from any other): Back, fullscreen, Up next's answer, or `ready`. */
  overlay(action: DesktopOverlayAction): Promise<void>;
  /** Overlay window only (no other receives it): the whole overlay state on every change. Returns the unsubscribe. */
  onState(callback: (state: DesktopOverlayState) => void): () => void;
}

/** `window.mediamanDesktop`; keep in step with the preload. */
export interface MediamanDesktop {
  isDesktop: true;
  platform: string;
  server: {
    /** The server's base URL, or null when the UI reaches the API on its own origin. */
    baseUrl(): Promise<string | null>;
  };
  tokens: DesktopTokenBridge;
  player: DesktopPlayerBridge;
}

declare global {
  interface Window {
    mediamanDesktop?: MediamanDesktop;
  }
}

export function desktopBridge(): MediamanDesktop | undefined {
  return typeof window === 'undefined' ? undefined : window.mediamanDesktop;
}

/**
 * Inside the desktop app, keeps the tokens in the bridge instead of localStorage and points the API at the bridge's
 * server. Call before the first render; a no-op in a browser.
 */
export async function connectDesktop(): Promise<void> {
  const bridge = desktopBridge();
  if (bridge === undefined) return;
  const [baseUrl] = await Promise.all([bridge.server.baseUrl(), keepTokensOnDesktop(bridge.tokens)]);
  if (baseUrl !== null) setApiBaseUrl(baseUrl);
}
