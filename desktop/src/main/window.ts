// Pure window setup, kept free of runtime Electron imports so it can be unit tested.
import path from 'node:path';
import type { BrowserWindowConstructorOptions, Input, MenuItemConstructorOptions } from 'electron';

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

/**
 * The window and taskbar icon under the app path (`desktop/` in dev, the asar when packaged, so the installer's
 * app files must include `assets/**`): the multi-size .ico on Windows, a PNG elsewhere (macOS uses the bundle's icon).
 * Both are copies of assets/brand written by `npm run brand`.
 */
export function windowIconPath(appPath: string, platform: NodeJS.Platform): string {
  return path.join(appPath, 'assets', platform === 'win32' ? 'icon.ico' : 'icon.png');
}

/** The renderer is the shared web UI: isolated, sandboxed, no Node (.claude/rules/desktop.md). */
export function mainWindowOptions(preloadPath: string, iconPath?: string): BrowserWindowConstructorOptions {
  return {
    ...(iconPath === undefined ? {} : { icon: iconPath }),
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

/**
 * The application menu (SPEC §11): none on Windows and Linux, so the window has no menu bar; on macOS a minimal
 * role-based menu so Cmd+Q, Cmd+H and the clipboard shortcuts keep working.
 */
export function applicationMenuTemplate(platform: NodeJS.Platform, appName: string): MenuItemConstructorOptions[] | null {
  if (platform !== 'darwin') return null;
  return [
    {
      label: appName,
      submenu: [
        { role: 'about' },
        { type: 'separator' },
        { role: 'hide' },
        { role: 'hideOthers' },
        { role: 'unhide' },
        { type: 'separator' },
        { role: 'quit' },
      ],
    },
    {
      label: 'Edit',
      submenu: [
        { role: 'undo' },
        { role: 'redo' },
        { type: 'separator' },
        { role: 'cut' },
        { role: 'copy' },
        { role: 'paste' },
        { role: 'selectAll' },
      ],
    },
    { role: 'windowMenu' },
  ];
}

/** Dev shortcuts the default menu used to provide, for unpackaged runs: F12 or Ctrl/Cmd+Shift+I, and Ctrl/Cmd+R. */
export function devShortcut(
  input: Pick<Input, 'type' | 'key' | 'control' | 'meta' | 'shift'>,
): 'devtools' | 'reload' | undefined {
  if (input.type !== 'keyDown') return undefined;
  const mod = input.control || input.meta;
  const key = input.key.toLowerCase();
  if (key === 'f12' || (mod && input.shift && key === 'i')) return 'devtools';
  if (mod && !input.shift && key === 'r') return 'reload';
  return undefined;
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
