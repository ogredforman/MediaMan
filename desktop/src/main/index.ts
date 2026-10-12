// Electron main process: one window showing the shared web UI (SPEC §10.1). In Local mode it starts the server first.
import path from 'node:path';
import { app, BrowserWindow, dialog, ipcMain, safeStorage, shell } from 'electron';
import { registerBridge } from './bridge.js';
import { DesktopSettings, executableOf, mpvPathFrom, mpvSettingFrom } from './desktopSettings.js';
import { mpvAssetsIn, MpvPlayer } from './mpvPlayer.js';
import { PlayerHost } from './playerHost.js';
import { resolveServerCommand, resolveServerPort, ServerProcess, type IServerProcess } from './serverProcess.js';
import { safeStorageCipher, TokenStore } from './tokenStore.js';
import { isExternalWebLink, isSameOrigin, mainWindowOptions, resolveUiUrl } from './window.js';

const uiUrl = resolveUiUrl(process.env);
let server: IServerProcess | undefined;
let serverUrl: string | undefined;
let player: PlayerHost | undefined;

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

/** "Open MPV settings" (SPEC §7.4 step 6): the native picker for the MPV executable, saved for the next Play. */
async function chooseMpv(settings: DesktopSettings): Promise<string | null> {
  const options: Electron.OpenDialogOptions = {
    title: 'Choose the MPV player',
    buttonLabel: 'Use this MPV',
    properties: ['openFile'],
    ...(process.platform === 'win32' ? { filters: [{ name: 'Programs', extensions: ['exe'] }] } : {}),
  };
  const parent = BrowserWindow.getFocusedWindow();
  const result = parent ? await dialog.showOpenDialog(parent, options) : await dialog.showOpenDialog(options);
  const picked = result.filePaths[0];
  if (result.canceled || picked === undefined) return null;
  const mpvPath = executableOf(picked, process.platform);
  await settings.setMpvPath(mpvPath);
  return mpvPath;
}

void app.whenReady().then(async () => {
  const tokens = new TokenStore(path.join(app.getPath('userData'), 'servers.json'), safeStorageCipher(safeStorage));
  const settings = new DesktopSettings(path.join(app.getPath('userData'), 'settings.json'));
  player = new PlayerHost(
    (mpvPath) => new MpvPlayer({ mpvPath, assets: mpvAssetsIn(app.getAppPath()) }),
    async () => mpvPathFrom(await settings.get(), process.env),
  );
  registerBridge(ipcMain, {
    uiOrigin: uiUrl.origin,
    serverUrl: () => serverUrl,
    tokens,
    player,
    chooseMpv: () => chooseMpv(settings),
    mpvSetting: async () => mpvSettingFrom(await settings.get(), process.env),
    useDefaultMpv: async () => {
      await settings.clearMpvPath();
      return mpvSettingFrom(await settings.get(), process.env);
    },
  });

  const serverCommand = resolveServerCommand(process.env);
  if (serverCommand) {
    try {
      server = new ServerProcess({
        command: serverCommand,
        port: resolveServerPort(process.env),
        allowedOrigin: uiUrl.origin,
      });
      serverUrl = (await server.start()).baseUrl;
    } catch (err) {
      const message = err instanceof Error ? err.message : String(err);
      dialog.showErrorBox("Can't start the MediaMan server", message);
      app.quit();
      return;
    }
  }

  createMainWindow();
  app.on('activate', () => {
    if (BrowserWindow.getAllWindows().length === 0) createMainWindow();
  });
});

app.on('window-all-closed', () => {
  if (process.platform !== 'darwin') app.quit();
});

// Stop the server this app started before exiting; quitting again once it has stopped goes through.
let serverStopped = false;
app.on('before-quit', (event) => {
  void player?.stop();
  if (!server || serverStopped) return;
  event.preventDefault();
  serverStopped = true;
  void server
    .stop()
    .catch((err: unknown) => {
      console.error(`Could not stop the MediaMan server: ${err instanceof Error ? err.message : String(err)}`);
    })
    .finally(() => {
      app.quit();
    });
});
