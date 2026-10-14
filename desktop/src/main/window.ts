// Pure window setup, kept free of runtime Electron imports so it can be unit tested.
import type { BrowserWindowConstructorOptions } from 'electron';

/** The Vite dev server (`npm run dev:mock -w web`), for unpackaged runs. */
export const DEFAULT_UI_URL = 'http://localhost:5173';

/** Where the server serves the built web UI (ADR 0009; the server's `WebUi.RequestPath`, the web build's `base`). */
export const SERVER_UI_PATH = '/app/';

/** SPEC §11 background, shown before the UI paints. */
const BACKGROUND_COLOR = '#0E0B14';

/** The web UI the server at `serverBaseUrl` serves. */
export function serverUiUrl(serverBaseUrl: string): URL {
  return new URL(SERVER_UI_PATH, serverBaseUrl);
}

/**
 * Resolves the web UI URL: `MEDIAMAN_UI_URL` (the dev override), else `serverUi` (the packaged app's own server), else
 * the dev server. Only http(s) is accepted.
 */
export function resolveUiUrl(env: Readonly<Record<string, string | undefined>>, serverUi?: URL): URL {
  const raw = env.MEDIAMAN_UI_URL?.trim();
  if (!raw) return serverUi ?? new URL(DEFAULT_UI_URL);

  let url: URL;
  try {
    url = new URL(raw);
  } catch {
    throw new Error(`MEDIAMAN_UI_URL "${raw}" is not a valid URL. Set it to the web UI address, e.g. ${DEFAULT_UI_URL}.`);
  }
  if (!isHttp(url)) {
    throw new Error(
      `MEDIAMAN_UI_URL must use http or https, got "${url.protocol}". Set it to the web UI address, e.g. ${DEFAULT_UI_URL}.`,
    );
  }
  return url;
}

/** The renderer is the shared web UI: isolated, sandboxed, no Node (.claude/rules/desktop.md). */
export function mainWindowOptions(preloadPath: string): BrowserWindowConstructorOptions {
  return {
    width: 1280,
    height: 800,
    minWidth: 800,
    minHeight: 500,
    show: false,
    backgroundColor: BACKGROUND_COLOR,
    title: 'MediaMan',
    webPreferences: {
      preload: preloadPath,
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: true,
      webSecurity: true,
    },
  };
}

/** True when `target` is on the UI's origin, so the window may navigate there. */
export function isSameOrigin(target: string, uiUrl: URL): boolean {
  try {
    return new URL(target).origin === uiUrl.origin;
  } catch {
    return false;
  }
}

/** True for http(s) links, the only ones handed to the system browser. */
export function isExternalWebLink(target: string): boolean {
  try {
    return isHttp(new URL(target));
  } catch {
    return false;
  }
}

function isHttp(url: URL): boolean {
  return url.protocol === 'http:' || url.protocol === 'https:';
}
