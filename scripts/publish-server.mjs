// Publishes the packaged Windows server (PLAN 2.5.1): the single-file, self-contained Release build from the
// win-x64 publish profile, with the jellyfin-ffprobe build beside it (SPEC §9 bundled tools, §10.1) and its licence,
// and the built web UI in web/, which the server serves under /app/ (PLAN 2.5.2b, ADR 0009).
// The download is pinned by version and SHA-256 in scripts/lib/pins.mjs and cached in dist/.cache; a hash mismatch
// fails the build.
//
//   node scripts/publish-server.mjs   →   dist/server/win-x64/{MediaMan.Server.exe, ffprobe.exe, licenses/, web/}

import { spawnSync } from 'node:child_process';
import { cp, mkdir, readFile, rm, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { cacheRoot, failer, fileNameOf, pinned, root, unzipEntry } from './lib/pinned.mjs';
import { jellyfinFfmpeg } from './lib/pins.mjs';

const project = path.join(root, 'server', 'src', 'MediaMan.Server', 'MediaMan.Server.csproj');
const output = path.join(root, 'dist', 'server', 'win-x64');
const cache = path.join(cacheRoot, jellyfinFfmpeg.name, jellyfinFfmpeg.version);
const webDist = path.join(root, 'web', 'dist');
// The server's WebUi.FolderName and WebUi.RequestPath; the web build's Vite base.
const webFolder = 'web';
const webBase = '/app/';
// Mock mode only (MSW); never registered by the served app.
const devOnlyWebFiles = new Set(['mockServiceWorker.js']);

const fail = failer('publish-server');

await rm(output, { recursive: true, force: true });
const publish = spawnSync(
  'dotnet',
  // DebugType here, not only in the profile, so referenced projects leave no .pdb files behind either.
  ['publish', project, '-p:PublishProfile=win-x64', '-p:DebugType=none', '-o', output],
  { stdio: 'inherit' },
);
if (publish.error) {
  fail(`couldn't run dotnet (${publish.error.message}). Install the .NET SDK from global.json and try again.`);
}
if (publish.status !== 0) {
  fail('dotnet publish failed; see the output above.');
}

// The UI the server serves: built fresh, so it matches this server's contract.
// npm is npm.cmd on Windows, which only a shell runs; the command is a fixed string.
const webBuild = spawnSync('npm run build -w web', { cwd: root, stdio: 'inherit', shell: true });
if (webBuild.error) {
  fail(`couldn't run npm (${webBuild.error.message}). Install Node.js and run npm ci, then try again.`);
}
if (webBuild.status !== 0) {
  fail('building the web UI failed; see the output above.');
}
const indexHtml = await readFile(path.join(webDist, 'index.html'), 'utf8').catch(() =>
  fail('the web build left no web/dist/index.html. Run npm run build -w web and check its output.'),
);
if (!indexHtml.includes(`${webBase}assets/`)) {
  fail(`web/dist/index.html doesn't load its assets from ${webBase}, where the server serves it. Check base in web/vite.config.ts.`);
}
await cp(webDist, path.join(output, webFolder), {
  recursive: true,
  filter: (source) => !devOnlyWebFiles.has(path.basename(source)),
});

const zip = await pinned(jellyfinFfmpeg.zip, cache, fail);
await writeFile(path.join(output, 'ffprobe.exe'), unzipEntry(zip, 'ffprobe.exe', jellyfinFfmpeg.name, fail));
const licences = path.join(output, 'licenses', 'jellyfin-ffmpeg');
await mkdir(licences, { recursive: true });
for (const licence of jellyfinFfmpeg.licences) {
  await writeFile(path.join(licences, fileNameOf(licence)), await pinned(licence, cache, fail));
}

console.log(`Published the server with the web UI and jellyfin-ffprobe ${jellyfinFfmpeg.version} to ${path.relative(root, output)}`);
