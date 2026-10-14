// Publishes the packaged Windows server (PLAN 2.5.1): the single-file, self-contained Release build from the
// win-x64 publish profile, with the jellyfin-ffprobe build beside it (SPEC §9 bundled tools, §10.1) and its licence,
// and the built web UI in web/, which the server serves under /app/ (PLAN 2.5.2b, ADR 0009).
// The download is pinned by version and SHA-256 and cached in dist/.cache; a hash mismatch fails the build.
//
//   node scripts/publish-server.mjs   →   dist/server/win-x64/{MediaMan.Server.exe, ffprobe.exe, licenses/, web/}
//
// To update ffprobe, change the version, URLs and hashes below together (each release lists its asset digests).

import { spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { existsSync } from 'node:fs';
import { cp, mkdir, readFile, rm, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { inflateRawSync } from 'node:zlib';

const jellyfinFfmpeg = {
  version: 'v8.1.3-1',
  zip: {
    url: 'https://github.com/jellyfin/jellyfin-ffmpeg/releases/download/v8.1.3-1/jellyfin-ffmpeg_8.1.3-1_portable_win64-clang-gpl.zip',
    sha256: '3691f8b2d4c39c511b5faff1c8c4687bd782dac823e63ba69f0755f7ce1af9ce',
  },
  licences: [
    {
      name: 'LICENSE.md',
      url: 'https://raw.githubusercontent.com/jellyfin/jellyfin-ffmpeg/v8.1.3-1/LICENSE.md',
      sha256: '2e1d16c72fd74e12063776371da757322f8b77589386532f4fd8634bde7de1af',
    },
    {
      name: 'COPYING.GPLv3',
      url: 'https://raw.githubusercontent.com/jellyfin/jellyfin-ffmpeg/v8.1.3-1/COPYING.GPLv3',
      sha256: '8ceb4b9ee5adedde47b31e975c1d90c73ad27b6b165a1dcd80c7c545eb65b903',
    },
  ],
};

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const project = path.join(root, 'server', 'src', 'MediaMan.Server', 'MediaMan.Server.csproj');
const output = path.join(root, 'dist', 'server', 'win-x64');
const cache = path.join(root, 'dist', '.cache', 'jellyfin-ffmpeg', jellyfinFfmpeg.version);
const webDist = path.join(root, 'web', 'dist');
// The server's WebUi.FolderName and WebUi.RequestPath; the web build's Vite base.
const webFolder = 'web';
const webBase = '/app/';
// Mock mode only (MSW); never registered by the served app.
const devOnlyWebFiles = new Set(['mockServiceWorker.js']);

function fail(message) {
  console.error(`publish-server: ${message}`);
  process.exit(1);
}

const sha256 = (bytes) => createHash('sha256').update(bytes).digest('hex');

/** The file from the cache if its hash matches, else downloaded, checked and cached. */
async function pinned({ url, sha256: expected }) {
  const file = path.join(cache, path.basename(new URL(url).pathname));
  if (existsSync(file)) {
    const cached = await readFile(file);
    if (sha256(cached) === expected) {
      return cached;
    }
  }
  console.log(`Downloading ${url}`);
  const response = await fetch(url);
  if (!response.ok) {
    fail(`downloading ${url} failed with HTTP ${response.status}. Check the network and try again.`);
  }
  const bytes = Buffer.from(await response.arrayBuffer());
  const actual = sha256(bytes);
  if (actual !== expected) {
    fail(`${url} has SHA-256 ${actual}, expected ${expected}. Don't ship it; check the pinned version in this script.`);
  }
  await mkdir(cache, { recursive: true });
  await writeFile(file, bytes);
  return bytes;
}

/** One stored or deflated entry from a zip (no Zip64: the jellyfin archives are well under 4 GB). */
function unzipEntry(zip, name) {
  const endOfCentralDirectory = zip.lastIndexOf(Buffer.from([0x50, 0x4b, 0x05, 0x06]));
  if (endOfCentralDirectory < 0) {
    fail('the jellyfin-ffmpeg download is not a zip archive.');
  }
  const entries = zip.readUInt16LE(endOfCentralDirectory + 10);
  let offset = zip.readUInt32LE(endOfCentralDirectory + 16);
  for (let i = 0; i < entries; i++) {
    const method = zip.readUInt16LE(offset + 10);
    const compressedSize = zip.readUInt32LE(offset + 20);
    const nameLength = zip.readUInt16LE(offset + 28);
    const extraLength = zip.readUInt16LE(offset + 30);
    const commentLength = zip.readUInt16LE(offset + 32);
    const localHeader = zip.readUInt32LE(offset + 42);
    const entryName = zip.toString('utf8', offset + 46, offset + 46 + nameLength);
    if (entryName === name) {
      const dataStart = localHeader + 30 + zip.readUInt16LE(localHeader + 26) + zip.readUInt16LE(localHeader + 28);
      const data = zip.subarray(dataStart, dataStart + compressedSize);
      if (method === 0) return data;
      if (method === 8) return inflateRawSync(data);
      fail(`${name} in the jellyfin-ffmpeg zip uses compression method ${method}, which this script can't read.`);
    }
    offset += 46 + nameLength + extraLength + commentLength;
  }
  fail(`${name} isn't in the jellyfin-ffmpeg zip.`);
}

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

const zip = await pinned(jellyfinFfmpeg.zip);
await writeFile(path.join(output, 'ffprobe.exe'), unzipEntry(zip, 'ffprobe.exe'));
const licences = path.join(output, 'licenses', 'jellyfin-ffmpeg');
await mkdir(licences, { recursive: true });
for (const licence of jellyfinFfmpeg.licences) {
  await writeFile(path.join(licences, licence.name), await pinned(licence));
}

console.log(`Published the server with the web UI and jellyfin-ffprobe ${jellyfinFfmpeg.version} to ${path.relative(root, output)}`);
