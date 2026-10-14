// Electron main process: one window showing the shared web UI (SPEC §10.1). In Local mode it starts the server first:
// the packaged app's own server, or `MEDIAMAN_SERVER_PATH` as a dev override.
import path from 'node:path';
import { app, BrowserWindow, dialog, ipcMain, safeStorage, shell } from 'electron';
import { registerBridge, type MpvChoice } from './bridge.js';
import { bundledTools } from './bundled.js';
import { DesktopSettings, executableOf, mpvPathFrom, mpvSettingFrom } from './desktopSettings.js';
import { startLocalServer, stopServerOnQuit } from './localServer.js';
import { checkMpvExecutable } from './mpvCheck.js';
import { mpvAssetsIn, MpvPlayer } from './mpvPlayer.js';
import { PlayerHost } from './playerHost.js';
import {
  BUNDLED_SERVER_HINT,
  localServerBaseUrl,
  resolveServerCommand,
  resolveServerPort,
  ServerProcess,
  type IServerProcess,
} from './serverProcess.js';
import { safeStorageCipher, TokenStore } from './tokenStore.js';
import { isExternalWebLink, isSameOrigin, mainWindowOptions, resolveUiUrl, serverUiUrl } from './window.js';

// Only a packaged app has the server and MPV in its resources folder; unpackaged runs use the dev overrides or PATH.
const bundled = app.isPackaged ? bundledTools(process.resourcesPath, process.platform) : undefined;
const devServer = Boolean(process.env.MEDIAMAN_SERVER_PATH?.trim());
const serverPort = resolveServerPort(process.env);
const serverBaseUrl = localServerBaseUrl(serverPort);
// A packaged app shows the UI its own server serves (ADR 0009); MEDIAMAN_UI_URL still overrides it, and unpackaged
// runs use the Vite dev server.
const ownServerUi = bundled && !devServer ? serverUiUrl(serverBaseUrl) : undefined;
const uiUrl = resolveUiUrl(process.env, ownServerUi);
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
    const next =
      uiUrl === ownServerUi ? BUNDLED_SERVER_HINT : 'Start it with "npm run dev:mock -w web" or set MEDIAMAN_UI_URL.';
    console.error(`Could not load the web UI at ${uiUrl.toString()}: ${message}. ${next}`);
    win.show();
  });
}

/**
 * "Open MPV settings" (SPEC §7.4 step 6): the native picker for the MPV executable, saved for the next Play once
 * `checkMpvExecutable` says it is MPV. The picked file is never run here.
 */
async function chooseMpv(settings: DesktopSettings): Promise<MpvChoice> {
  const options: Electron.OpenDialogOptions = {
    title: 'Choose the MPV player',
    buttonLabel: 'Use this MPV',
    properties: ['openFile'],
    ...(process.platform === 'win32' ? { filters: [{ name: 'Programs', extensions: ['exe'] }] } : {}),
  };
  const parent = BrowserWindow.getFocusedWindow();
  const result = parent ? await dialog.showOpenDialog(parent, options) : await dialog.showOpenDialog(options);
  const picked = result.filePaths[0];
  if (result.canceled || picked === undefined) return { outcome: 'cancelled' };
  const mpvPath = executableOf(picked, process.platform);
  const check = await checkMpvExecutable(mpvPath);
  if (!check.ok) return { outcome: 'refused', message: check.message };
  await settings.setMpvPath(mpvPath);
  return { outcome: 'picked', path: mpvPath };
}

void app.whenReady().then(async () => {
  const tokens = new TokenStore(path.join(app.getPath('userData'), 'servers.json'), safeStorageCipher(safeStorage));
  const settings = new DesktopSettings(path.join(app.getPath('userData'), 'settings.json'));
  player = new PlayerHost(
    (mpvPath) => new MpvPlayer({ mpvPath, assets: bundled?.mpvAssets ?? mpvAssetsIn(app.getAppPath()) }),
    async () => mpvPathFrom(await settings.get(), process.env, bundled?.mpvPath),
  );
  registerBridge(ipcMain, {
    uiOrigin: uiUrl.origin,
    serverUrl: () => serverUrl,
    tokens,
    player,
    chooseMpv: () => chooseMpv(settings),
    mpvSetting: async () => mpvSettingFrom(await settings.get(), process.env, bundled?.mpvPath),
    useDefaultMpv: async () => {
      await settings.clearMpvPath();
      return mpvSettingFrom(await settings.get(), process.env, bundled?.mpvPath);
    },
  });

  try {
    const local = await startLocalServer(
      resolveServerCommand(process.env, process.cwd(), bundled?.serverPath),
      (command) =>
        (server = new ServerProcess({
          command,
          port: serverPort,
          // A UI on the server's own origin needs no CORS.
          ...(uiUrl.origin === serverBaseUrl ? {} : { allowedOrigin: uiUrl.origin }),
          ...(devServer ? {} : { startFailureHint: BUNDLED_SERVER_HINT }),
        })),
    );
    serverUrl = local?.baseUrl;
  } catch (err) {
    const message = err instanceof Error ? err.message : String(err);
    dialog.showErrorBox("Can't start the MediaMan server", message);
    app.quit();
    return;
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
const stopServer = stopServerOnQuit(
  () => server,
  () => {
    app.quit();
  },
);
app.on('before-quit', (event) => {
  void player?.stop();
  stopServer(event);
});
