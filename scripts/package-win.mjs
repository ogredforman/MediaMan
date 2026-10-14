// Builds the unsigned Windows installer (PLAN 2.5.4, ADR 0010): publishes the server with ffprobe and the web UI
// (scripts/publish-server.mjs), builds the desktop app, adds the pinned official MPV build and its licences, collects
// the upstream source archives of the GPL builds we ship, and runs electron-builder (desktop/electron-builder.json).
// Packaging may use the network; every download is pinned by SHA-256 in scripts/lib/pins.mjs and cached in dist/.cache.
//
//   npm run package:win   →   dist/installer/MediaMan-Setup-<version>-x64.exe (and win-unpacked/)
//                             dist/mpv/win-x64/{mpv.exe, vulkan-1.dll, licenses/mpv/}
//                             dist/sources/<name>-<version>-source.tar.gz, attached to each release (PLAN 2.5.5)

import { spawnSync } from 'node:child_process';
import { copyFile, mkdir, readFile, rm, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { cacheRoot, failer, fileNameOf, pinned, root, unzipEntry } from './lib/pinned.mjs';
import { jellyfinFfmpeg, mpv } from './lib/pins.mjs';

const fail = failer('package-win');
const notices = path.join(root, 'desktop', 'THIRD-PARTY-NOTICES.md');
const mpvOutput = path.join(root, 'dist', 'mpv', 'win-x64');
const sourcesOutput = path.join(root, 'dist', 'sources');
const cacheOf = (tool) => path.join(cacheRoot, tool.name, tool.version);

/** Runs a fixed command line (npm is npm.cmd on Windows, which only a shell runs) and fails the build if it fails. */
function run(commandLine, what) {
  const result = spawnSync(commandLine, { cwd: root, stdio: 'inherit', shell: true });
  if (result.error) fail(`couldn't ${what} (${result.error.message}).`);
  if (result.status !== 0) fail(`${what} failed; see the output above.`);
}

// The notices name the exact versions shipped; refuse to build an installer whose notices are out of date.
const noticeText = await readFile(notices, 'utf8').catch(() => fail(`${path.relative(root, notices)} is missing.`));
for (const tool of [jellyfinFfmpeg, mpv]) {
  if (!noticeText.includes(`${tool.name} ${tool.version}`)) {
    fail(`${path.relative(root, notices)} doesn't name ${tool.name} ${tool.version}. Update it to match scripts/lib/pins.mjs.`);
  }
}

run(`"${process.execPath}" scripts/publish-server.mjs`, 'publish the server');
// Fresh, so a module deleted from the sources doesn't ship from an old build. The incremental build info goes too,
// or tsc would think the deleted output is up to date and emit nothing.
await rm(path.join(root, 'desktop', 'dist'), { recursive: true, force: true });
await rm(path.join(root, 'desktop', 'node_modules', '.tmp', 'tsconfig.build.tsbuildinfo'), { force: true });
run('npm run build -w desktop', 'build the desktop app');

await rm(mpvOutput, { recursive: true, force: true });
await mkdir(path.join(mpvOutput, 'licenses', mpv.name), { recursive: true });
const mpvZip = await pinned(mpv.zip, cacheOf(mpv), fail);
for (const file of mpv.files) {
  await writeFile(path.join(mpvOutput, file), unzipEntry(mpvZip, file, mpv.name, fail));
}
for (const licence of mpv.licences) {
  await writeFile(path.join(mpvOutput, 'licenses', mpv.name, fileNameOf(licence)), await pinned(licence, cacheOf(mpv), fail));
}

await mkdir(sourcesOutput, { recursive: true });
for (const tool of [jellyfinFfmpeg, mpv]) {
  await pinned(tool.source, cacheOf(tool), fail);
  await copyFile(path.join(cacheOf(tool), fileNameOf(tool.source)), path.join(sourcesOutput, fileNameOf(tool.source)));
}

run('npm run package:win -w desktop', 'build the installer with electron-builder');
console.log('Built the installer in dist/installer and the source archives for the release in dist/sources.');
