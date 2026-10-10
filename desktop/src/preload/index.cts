// Preload: the narrow bridge between the web UI and the main process. CommonJS because sandboxed preloads must be.
// Add desktop-only features here deliberately; never expose ipcRenderer or Node APIs wholesale.
import { contextBridge, ipcRenderer } from 'electron';

// Keep in sync with bridgeChannels in src/main/bridge.ts; a sandboxed preload can't import local modules.
const channels = {
  serverUrl: 'mediaman:server-url',
  tokensGet: 'mediaman:tokens-get',
  tokensSet: 'mediaman:tokens-set',
  tokensClear: 'mediaman:tokens-clear',
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
});
