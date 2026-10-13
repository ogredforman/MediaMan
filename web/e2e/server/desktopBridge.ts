import type { Page, Response } from '@playwright/test';

/** What the stand-in records per Play; the bridge's full shape is `MediamanDesktop` in src/lib/desktop.ts. */
interface PlayOptions {
  url: string;
}

/**
 * The page globals the stand-in sets. e2e code compiles without the DOM types (and src/lib needs them), so the
 * bridge is typed here only as far as the stand-in uses it.
 */
interface StandInGlobals {
  mediamanDesktop?: unknown;
  /** What the stand-in player was asked to play, in order. */
  __desktopPlays?: PlayOptions[];
}

/**
 * Installs a stand-in for the desktop app's preload bridge (`window.mediamanDesktop`, desktop/src/preload/index.cts)
 * before the UI loads, so Play goes through the real server's playback sessions without MPV. Tokens live in the
 * same localStorage entry the browser uses, so the setup spec's saved session still signs in. The stand-in player
 * records each launch and reports that MPV started.
 */
export async function installDesktopStandIn(page: Page): Promise<void> {
  await page.addInitScript(() => {
    const tokenKey = 'mediaman.auth';
    const scope = globalThis as StandInGlobals;
    const plays: PlayOptions[] = [];
    scope.__desktopPlays = plays;
    scope.mediamanDesktop = {
      isDesktop: true,
      platform: 'e2e',
      server: { baseUrl: () => Promise.resolve(null) },
      tokens: {
        get: () => {
          const raw = localStorage.getItem(tokenKey);
          return Promise.resolve(raw === null ? null : (JSON.parse(raw) as unknown));
        },
        set: (tokens: unknown) => {
          localStorage.setItem(tokenKey, JSON.stringify(tokens));
          return Promise.resolve();
        },
        clear: () => {
          localStorage.removeItem(tokenKey);
          return Promise.resolve();
        },
      },
      player: {
        play: (_launchId: string, options: PlayOptions) => {
          plays.push(options);
          return Promise.resolve({ ok: true });
        },
        stop: () => Promise.resolve(),
        upNext: () => Promise.resolve(),
        notice: () => Promise.resolve(),
        chooseMpv: () => Promise.resolve(null),
        onEvent: () => () => undefined,
      },
    };
  });
}

/** The name of the file the stand-in player was last asked to stream, looked up in the title's API response. */
export async function lastPlayedFileName(page: Page, title: Response): Promise<string | undefined> {
  const url = await page.evaluate(() => (globalThis as StandInGlobals).__desktopPlays?.at(-1)?.url);
  const mediaFileId = url === undefined ? undefined : /\/media-files\/([^/]+)\/stream/.exec(url)?.[1];
  const versions: { mediaFileId: string; fileName: string }[] = [];
  const collect = (value: unknown): void => {
    if (Array.isArray(value)) {
      value.forEach(collect);
    } else if (typeof value === 'object' && value !== null) {
      const v = value as Record<string, unknown>;
      if (typeof v.mediaFileId === 'string' && typeof v.fileName === 'string') {
        versions.push({ mediaFileId: v.mediaFileId, fileName: v.fileName });
      }
      Object.values(v).forEach(collect);
    }
  };
  collect(await title.json());
  return versions.find((v) => v.mediaFileId === mediaFileId)?.fileName;
}
