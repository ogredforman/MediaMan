import { existsSync } from 'node:fs';
import path from 'node:path';
import { describe, expect, it } from 'vitest';
import {
  DEFAULT_UI_URL,
  isExternalWebLink,
  isSameOrigin,
  mainWindowOptions,
  resolveUiUrl,
  serverUiUrl,
  windowIconPath,
} from './window.js';

describe('resolveUiUrl', () => {
  it('defaults to the dev server', () => {
    expect(resolveUiUrl({}).href).toBe(new URL(DEFAULT_UI_URL).href);
    expect(resolveUiUrl({ MEDIAMAN_UI_URL: '  ' }).href).toBe(new URL(DEFAULT_UI_URL).href);
  });

  it('uses MEDIAMAN_UI_URL when set', () => {
    expect(resolveUiUrl({ MEDIAMAN_UI_URL: 'https://media.example:8443/' }).origin).toBe('https://media.example:8443');
  });

  it.each(['file:///etc/passwd', 'javascript:alert(1)', 'not a url'])('rejects %s', (value) => {
    expect(() => resolveUiUrl({ MEDIAMAN_UI_URL: value })).toThrow(/MEDIAMAN_UI_URL/);
  });

  it("uses the UI of the packaged app's own server", () => {
    const serverUi = serverUiUrl('http://127.0.0.1:8420');
    expect(serverUi.href).toBe('http://127.0.0.1:8420/app/');
    expect(resolveUiUrl({}, serverUi).href).toBe('http://127.0.0.1:8420/app/');
    expect(resolveUiUrl({ MEDIAMAN_UI_URL: ' ' }, serverUi).href).toBe('http://127.0.0.1:8420/app/');
  });

  it("lets MEDIAMAN_UI_URL override the server's UI", () => {
    expect(resolveUiUrl({ MEDIAMAN_UI_URL: 'http://localhost:5173' }, serverUiUrl('http://127.0.0.1:8420')).origin).toBe(
      'http://localhost:5173',
    );
  });
});

describe('mainWindowOptions', () => {
  it('isolates and sandboxes the renderer', () => {
    expect(mainWindowOptions('preload.cjs').webPreferences).toMatchObject({
      preload: 'preload.cjs',
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: true,
      webSecurity: true,
    });
  });

  it('sets the window icon only when given one', () => {
    expect(mainWindowOptions('preload.cjs', 'icon.ico').icon).toBe('icon.ico');
    expect(mainWindowOptions('preload.cjs')).not.toHaveProperty('icon');
  });
});

describe('windowIconPath', () => {
  const appPath = path.join('app', 'root');

  it('uses the multi-size .ico on Windows and a PNG elsewhere', () => {
    expect(windowIconPath(appPath, 'win32')).toBe(path.join(appPath, 'assets', 'icon.ico'));
    expect(windowIconPath(appPath, 'linux')).toBe(path.join(appPath, 'assets', 'icon.png'));
    expect(windowIconPath(appPath, 'darwin')).toBe(path.join(appPath, 'assets', 'icon.png'));
  });

  it('finds both icons under the desktop app path', () => {
    const desktopDir = path.join(import.meta.dirname, '..', '..');
    expect(existsSync(windowIconPath(desktopDir, 'win32'))).toBe(true);
    expect(existsSync(windowIconPath(desktopDir, 'linux'))).toBe(true);
  });
});

describe('navigation guard', () => {
  const ui = new URL('http://localhost:5173');

  it('allows the UI origin only', () => {
    expect(isSameOrigin('http://localhost:5173/movies?x=1', ui)).toBe(true);
    expect(isSameOrigin('http://localhost:5174/', ui)).toBe(false);
    expect(isSameOrigin('https://localhost:5173/', ui)).toBe(false);
    expect(isSameOrigin('https://evil.example/', ui)).toBe(false);
    expect(isSameOrigin('garbage', ui)).toBe(false);
  });

  it('hands only http(s) links to the system browser', () => {
    expect(isExternalWebLink('https://www.themoviedb.org/')).toBe(true);
    expect(isExternalWebLink('file:///C:/Windows/system32/calc.exe')).toBe(false);
    expect(isExternalWebLink('magnet:?xt=urn:btih:abc')).toBe(false);
    expect(isExternalWebLink('garbage')).toBe(false);
  });
});
