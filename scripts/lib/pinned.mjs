// Pinned downloads for the packaging scripts (PLAN 2.5.1, 2.5.4). Packaging may use the network; tests never do.
// Every file is pinned by URL and SHA-256 and cached under dist/.cache, so a rebuild (or an offline build with the
// files dropped into the cache) downloads nothing, and a hash mismatch fails the build.

import { createHash } from 'node:crypto';
import { existsSync } from 'node:fs';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { inflateRawSync } from 'node:zlib';

export const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
export const cacheRoot = path.join(root, 'dist', '.cache');

/** Prints `<script>: <message>` and exits with a failure. */
export function failer(script) {
  return (message) => {
    console.error(`${script}: ${message}`);
    process.exit(1);
  };
}

export const sha256 = (bytes) => createHash('sha256').update(bytes).digest('hex');

/** The name a pinned `{ url, name? }` is cached and shipped under: `name`, else the URL's last path segment. */
export const fileNameOf = ({ url, name }) => name ?? path.basename(new URL(url).pathname);

/**
 * The file `{ url, sha256, name? }` from `cacheDir` if its hash matches, else downloaded, checked and cached there
 * under `fileNameOf(spec)`.
 */
export async function pinned(spec, cacheDir, fail) {
  const { url, sha256: expected } = spec;
  const file = path.join(cacheDir, fileNameOf(spec));
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
    fail(`${url} has SHA-256 ${actual}, expected ${expected}. Don't ship it; check the pinned version in scripts/lib/pins.mjs.`);
  }
  await mkdir(cacheDir, { recursive: true });
  await writeFile(file, bytes);
  return bytes;
}

/** One stored or deflated entry from a zip (no Zip64: the archives we unpack are well under 4 GB). */
export function unzipEntry(zip, name, label, fail) {
  const endOfCentralDirectory = zip.lastIndexOf(Buffer.from([0x50, 0x4b, 0x05, 0x06]));
  if (endOfCentralDirectory < 0) {
    fail(`the ${label} download is not a zip archive.`);
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
      fail(`${name} in the ${label} zip uses compression method ${method}, which this script can't read.`);
    }
    offset += 46 + nameLength + extraLength + commentLength;
  }
  fail(`${name} isn't in the ${label} zip.`);
}
