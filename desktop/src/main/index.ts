// Electron main process: one window showing the shared web UI (SPEC §10.1).
import path from 'node:path';
import { app, BrowserWindow, shell } from 'electron';
import { isExternalWebLink, isSameOrigin, mainWindowOptions, resolveUiUrl } from './window.js';

const uiUrl = resolveUiUrl(process.env);

function createMainWindow(): void {
  const preloadPath = path.join(import.meta.dirname, '..', 'preload', 'index.cjs');
  const win = new BrowserWindow(mainWindowOptions(preloadPath));

  // Keep the window on the UI origin; other web links open in the system browser.
  win.webContents.on('will-navigate', (event, target) => {
    if (isSameOrigin(target, uiUrl)) return;
    event.preventDefault();
    if (isExternalWebLink(target)) void shell.openExternal(target);
  });
  win.webContents.setWindowOpenHandler(({ url }) => {
    if (isExternalWebLink(url)) void shell.openExternal(url);
    return { action: 'deny' };
  });

  win.once('ready-to-show', () => {
    win.show();
  });
  win.loadURL(uiUrl.toString()).catch((err: unknown) => {
    const message = err instanceof Error ? err.message : String(err);
    console.error(
      `Could not load the web UI at ${uiUrl.toString()}: ${message}. Start it with "npm run dev:mock -w web" or set MEDIAMAN_UI_URL.`,
    );
    win.show();
  });
}

void app.whenReady().then(() => {
  createMainWindow();
  app.on('activate', () => {
    if (BrowserWindow.getAllWindows().length === 0) createMainWindow();
  });
});

app.on('window-all-closed', () => {
  if (process.platform !== 'darwin') app.quit();
});
