// The desktop app's preload bridge (desktop/src/preload/index.cts), present only when the UI runs inside it.
import { setApiBaseUrl } from './http';
import { keepTokensOnDesktop } from './session';

/** The current server's sign-in tokens, kept encrypted by the desktop app's main process. */
export interface DesktopTokenBridge {
  get(): Promise<unknown>;
  set(tokens: unknown): Promise<void>;
  clear(): Promise<void>;
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
