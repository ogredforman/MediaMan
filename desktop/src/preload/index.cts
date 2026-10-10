// Preload: the narrow bridge between the web UI and the main process. CommonJS because sandboxed preloads must be.
// Add desktop-only features here deliberately; never expose ipcRenderer or Node APIs wholesale.
import { contextBridge, ipcRenderer } from 'electron';

// Keep in sync with bridgeChannels in src/main/bridge.ts; a sandboxed preload can't import local modules.
const channels = {
  serverUrl: 'mediaman:server-url',
  tokensGet: 'mediaman:tokens-get',
  tokensSet: 'mediaman:tokens-set',
  tokensClear: 'mediaman:tokens-clear',
  playerPlay: 'mediaman:player-play',
  playerStop: 'mediaman:player-stop',
  playerChooseMpv: 'mediaman:player-choose-mpv',
  playerEvent: 'mediaman:player-event',
} as const;

contextBridge.exposeInMainWorld('mediamanDesktop', {
  isDesktop: true,
  platform: process.platform,
  server: {
    /** The server's base URL, or null when the UI reaches the API on its own origin. */
    baseUrl: (): Promise<string | null> => ipcRenderer.invoke(channels.serverUrl) as Promise<string | null>,
  },
  /** The current server's sign-in tokens, kept encrypted by the main process. */
  tokens: {
    get: (): Promise<unknown> => ipcRenderer.invoke(channels.tokensGet),
    set: (tokens: unknown): Promise<void> => ipcRenderer.invoke(channels.tokensSet, tokens) as Promise<void>,
    clear: (): Promise<void> => ipcRenderer.invoke(channels.tokensClear) as Promise<void>,
  },
  /** MPV, one launch at a time. Events carry the `launchId` passed to `play`. */
  player: {
    play: (launchId: string, options: unknown): Promise<unknown> => ipcRenderer.invoke(channels.playerPlay, launchId, options),
    stop: (): Promise<void> => ipcRenderer.invoke(channels.playerStop) as Promise<void>,
    /** The native picker for the MPV executable; resolves to the saved path, or null when cancelled. */
    chooseMpv: (): Promise<string | null> => ipcRenderer.invoke(channels.playerChooseMpv) as Promise<string | null>,
    onEvent: (callback: (event: unknown) => void): (() => void) => {
      const listener = (_: unknown, event: unknown) => {
        callback(event);
      };
      ipcRenderer.on(channels.playerEvent, listener);
      return () => {
        ipcRenderer.removeListener(channels.playerEvent, listener);
      };
    },
  },
});
