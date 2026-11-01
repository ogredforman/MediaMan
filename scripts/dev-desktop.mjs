// Runs the desktop app from this checkout in one command: builds the server (Debug), starts the Vite dev server for the
// web UI, waits for it, then starts the desktop app, which runs the built server itself. Closing the app or pressing
// Ctrl+C stops everything, so no vite is left holding the port.
// MPV is whatever the desktop app already uses: the one picked in its settings, else MEDIAMAN_MPV_PATH, else mpv on PATH.
//
// The dev server gets its own port (8421) and data directory (MediaMan-dev beside the installed app's MediaMan), so it
// never uses an installed app's server or migrates its database.
//
//   npm run dev:desktop   (MEDIAMAN_UI_PORT picks another Vite port; MEDIAMAN_SERVER_PORT and MediaMan__DataDirectory
//                          override the dev server's port and data directory)

import { spawn, spawnSync } from 'node:child_process';
import os from 'node:os';
import path from 'node:path';
import { failer, root } from './lib/pinned.mjs';

const fail = failer('dev-desktop');
const uiPort = process.env.MEDIAMAN_UI_PORT?.trim() || '5173';
const uiUrl = `http://localhost:${uiPort}`;
const serverDll = path.join(root, 'server', 'src', 'MediaMan.Server', 'bin', 'Debug', 'net10.0', 'MediaMan.Server.dll');
const isWindows = process.platform === 'win32';
const serverPort = process.env.MEDIAMAN_SERVER_PORT?.trim() || '8421';
// Where .NET's LocalApplicationData points on each platform, so MediaMan-dev sits beside the installed app's MediaMan.
const localAppData = isWindows
  ? process.env.LOCALAPPDATA || path.join(os.homedir(), 'AppData', 'Local')
  : process.platform === 'darwin'
    ? path.join(os.homedir(), 'Library', 'Application Support')
    : process.env.XDG_DATA_HOME || path.join(os.homedir(), '.local', 'share');
const dataDir = process.env.MediaMan__DataDirectory?.trim() || path.join(localAppData, 'MediaMan-dev');

// Incremental, so it's quick when nothing changed, and the app never runs a stale server.
const build = spawnSync('dotnet', ['build', path.join(root, 'server', 'src', 'MediaMan.Server'), '-c', 'Debug'], {
  cwd: root,
  stdio: 'inherit',
});
if (build.error) fail(`couldn't run dotnet (${build.error.message}). Install the .NET SDK from global.json and try again.`);
if (build.status !== 0) fail('building the server failed; see the output above.');

const children = [];
let stopping = false;

// npm is npm.cmd on Windows, which only a shell runs; the commands are fixed strings.
function start(command, env) {
  const child = spawn(command, { cwd: root, stdio: 'inherit', shell: true, env: { ...process.env, ...env } });
  children.push(child);
  return child;
}

// The shell, npm and vite or electron form a process tree; on Windows killing the shell alone leaves the rest running.
function stopAll(code) {
  if (stopping) return;
  stopping = true;
  for (const child of children) {
    if (child.exitCode !== null || child.pid === undefined) continue;
    if (isWindows) spawnSync('taskkill', ['/pid', String(child.pid), '/t', '/f'], { stdio: 'ignore' });
    else child.kill('SIGTERM');
  }
  process.exit(code);
}

process.on('SIGINT', () => stopAll(0));
process.on('SIGTERM', () => stopAll(0));

const vite = start(`npm run dev -w web -- --port ${uiPort} --strictPort`);
vite.on('exit', (code) => {
  if (stopping) return;
  console.error(`dev-desktop: the web UI dev server stopped (exit ${String(code)}). If port ${uiPort} is in use, end that process or set MEDIAMAN_UI_PORT.`);
  stopAll(1);
});

async function waitForUi(timeoutMs) {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    if (stopping) return false;
    try {
      const response = await fetch(uiUrl);
      if (response.ok) return true;
    } catch {
      // Not listening yet.
    }
    await new Promise((resolve) => setTimeout(resolve, 250));
  }
  return false;
}

if (!(await waitForUi(60_000))) {
  console.error(`dev-desktop: the web UI didn't answer at ${uiUrl} within a minute; see the vite output above.`);
  stopAll(1);
}

console.log(`dev-desktop: dev server on http://127.0.0.1:${serverPort}, data in ${dataDir}`);
const desktop = start('npm start -w desktop', {
  MEDIAMAN_SERVER_PATH: serverDll,
  MEDIAMAN_SERVER_PORT: serverPort,
  MediaMan__DataDirectory: dataDir,
  MEDIAMAN_UI_URL: uiUrl,
  // Set by Electron-based tools (editors, terminals) for their own children; inherited, it starts the app as plain Node.
  ELECTRON_RUN_AS_NODE: undefined,
});
desktop.on('exit', (code) => stopAll(code ?? 0));
