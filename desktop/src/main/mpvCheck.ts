// Decides whether a path is MPV without running it (SPEC §5, §7.4 step 6): asking a file for `--version` would
// already execute an arbitrary program, so the check reads only its name and, on Windows, its version resource.
import { open, stat } from 'node:fs/promises';
import path from 'node:path';

export type MpvCheck = { ok: true } | { ok: false; message: string };

export type CheckMpvExecutable = (file: string) => Promise<MpvCheck>;

/** The version-resource fields that name the program; MPV's Windows builds set them to `mpv`. */
const namingFields = ['ProductName', 'InternalName', 'FileDescription'] as const;

/** Resource sections bigger than this aren't read; MPV's is well under a megabyte. */
const maxResourceBytes = 32 * 1024 * 1024;

/**
 * MPV, or why not. A bare name (looked up on PATH) passes on its name alone. A path must exist and be named `mpv`
 * (`mpv.exe` or `mpv.com` on Windows), and on Windows its version resource must name mpv. Never runs the file.
 * On Linux and macOS there is no version resource, so a program renamed `mpv` passes.
 */
export async function checkMpvExecutable(file: string, platform: NodeJS.Platform = process.platform): Promise<MpvCheck> {
  const paths = platform === 'win32' ? path.win32 : path;
  const name = paths.basename(file);
  const choose = `Choose the MPV player (${platform === 'win32' ? 'mpv.exe' : 'mpv'}) instead.`;
  const bare = name === file;
  if (!isMpvName(name, platform, bare)) {
    return { ok: false, message: `${file} isn't MPV, so MediaMan won't run it. ${choose}` };
  }
  if (bare) return { ok: true };
  try {
    if (!(await stat(file)).isFile()) return { ok: false, message: `${file} isn't a program file. ${choose}` };
  } catch {
    return { ok: false, message: `MPV wasn't found at ${file}. ${choose}` };
  }
  if (platform !== 'win32') return { ok: true };
  let strings: Map<string, string> | undefined;
  try {
    strings = await readVersionStrings(file);
  } catch {
    strings = undefined;
  }
  if (strings !== undefined && namingFields.some((field) => namesMpv(strings.get(field)))) return { ok: true };
  return { ok: false, message: `${file} isn't MPV (its version information doesn't name mpv), so MediaMan won't run it. ${choose}` };
}

function isMpvName(name: string, platform: NodeJS.Platform, bare: boolean): boolean {
  if (platform === 'win32') {
    const lower = name.toLowerCase();
    return lower === 'mpv.exe' || lower === 'mpv.com' || (bare && lower === 'mpv');
  }
  return (platform === 'darwin' ? name.toLowerCase() : name) === 'mpv';
}

function namesMpv(value: string | undefined): boolean {
  return value?.trim().toLowerCase().split(/\s+/)[0] === 'mpv';
}

/**
 * The text fields (`ProductName`, `FileDescription`, …) of a Windows executable's version resource, read from the PE
 * headers and the resource section only; undefined when the file has none. Throws when the file isn't a PE image.
 */
export async function readVersionStrings(file: string): Promise<Map<string, string> | undefined> {
  const handle = await open(file, 'r');
  try {
    const read = async (position: number, length: number): Promise<Buffer> => {
      const buffer = Buffer.alloc(length);
      const { bytesRead } = await handle.read(buffer, 0, length, position);
      if (bytesRead < length) throw new Error('The file ends early.');
      return buffer;
    };

    const dos = await read(0, 64);
    if (dos.toString('latin1', 0, 2) !== 'MZ') throw new Error('Not a Windows executable.');
    const pe = dos.readUInt32LE(0x3c);
    const coff = await read(pe, 24);
    if (coff.toString('latin1', 0, 4) !== 'PE\0\0') throw new Error('Not a Windows executable.');
    const sectionCount = coff.readUInt16LE(6);
    const optionalSize = coff.readUInt16LE(20);
    const optional = await read(pe + 24, optionalSize);
    const magic = optional.readUInt16LE(0);
    const directories = magic === 0x20b ? 112 : magic === 0x10b ? 96 : undefined;
    if (directories === undefined) throw new Error('Unknown PE optional header.');
    if (optional.readUInt32LE(directories - 4) < 3 || optionalSize < directories + 24) return undefined;
    const resourceRva = optional.readUInt32LE(directories + 16);
    if (resourceRva === 0) return undefined;

    const sections = await read(pe + 24 + optionalSize, sectionCount * 40);
    for (let i = 0; i < sectionCount; i++) {
      const at = i * 40;
      const virtualAddress = sections.readUInt32LE(at + 12);
      const rawSize = sections.readUInt32LE(at + 16);
      const rawPointer = sections.readUInt32LE(at + 20);
      const virtualSize = Math.max(sections.readUInt32LE(at + 8), rawSize);
      if (resourceRva < virtualAddress || resourceRva >= virtualAddress + virtualSize) continue;
      if (rawSize > maxResourceBytes) throw new Error('The resource section is too big.');
      const section = await read(rawPointer, rawSize);
      const version = versionResource(section, resourceRva - virtualAddress, virtualAddress);
      return version === undefined ? undefined : stringsOf(version);
    }
    return undefined;
  } finally {
    await handle.close();
  }
}

const rtVersion = 16;
const subdirectory = 0x8000_0000;

/** The RT_VERSION data (first name, first language) from the resource directory at `root` in `section`. */
function versionResource(section: Buffer, root: number, sectionRva: number): Buffer | undefined {
  const entries = (dir: number): { id: number; target: number }[] => {
    const named = section.readUInt16LE(dir + 12);
    const ids = section.readUInt16LE(dir + 14);
    const list: { id: number; target: number }[] = [];
    for (let i = 0; i < named + ids; i++) {
      const at = dir + 16 + i * 8;
      list.push({ id: section.readUInt32LE(at), target: section.readUInt32LE(at + 4) });
    }
    return list;
  };
  const byType = entries(root).find((entry) => entry.id === rtVersion);
  if (byType === undefined || (byType.target & subdirectory) === 0) return undefined;
  const byName = entries(root + (byType.target & ~subdirectory))[0];
  if (byName === undefined || (byName.target & subdirectory) === 0) return undefined;
  const byLanguage = entries(root + (byName.target & ~subdirectory))[0];
  if (byLanguage === undefined || (byLanguage.target & subdirectory) !== 0) return undefined;
  const data = root + byLanguage.target;
  const start = section.readUInt32LE(data) - sectionRva;
  const size = section.readUInt32LE(data + 4);
  if (start < 0 || start + size > section.length) return undefined;
  return section.subarray(start, start + size);
}

interface VersionBlock {
  key: string;
  /** Text for a String block's value; empty otherwise. */
  text: string;
  children: VersionBlock[];
}

/** `StringFileInfo` → string table → `key: value`; the first table wins. */
function stringsOf(version: Buffer): Map<string, string> | undefined {
  const root = parseBlock(version, 0);
  if (root.key !== 'VS_VERSION_INFO') return undefined;
  const table = root.children.find((child) => child.key === 'StringFileInfo')?.children[0];
  if (table === undefined) return undefined;
  return new Map(table.children.map((entry) => [entry.key, entry.text]));
}

function align4(offset: number): number {
  return (offset + 3) & ~3;
}

/** One VS_VERSIONINFO-style block: wLength, wValueLength, wType, the key, the value, then child blocks. */
function parseBlock(data: Buffer, start: number): VersionBlock & { end: number } {
  const length = data.readUInt16LE(start);
  const valueLength = data.readUInt16LE(start + 2);
  const isText = data.readUInt16LE(start + 4) === 1;
  const end = start + length;
  if (length < 6 || end > data.length) throw new Error('A version block is malformed.');
  let keyEnd = start + 6;
  while (keyEnd + 1 < end && data.readUInt16LE(keyEnd) !== 0) keyEnd += 2;
  const key = data.toString('utf16le', start + 6, keyEnd);
  const valueStart = align4(keyEnd + 2);
  const valueBytes = isText ? valueLength * 2 : valueLength;
  const text = isText ? data.toString('utf16le', valueStart, Math.min(valueStart + valueBytes, end)).replace(/\0+$/, '') : '';
  const children: VersionBlock[] = [];
  for (let child = align4(valueStart + valueBytes); child + 6 <= end && data.readUInt16LE(child) !== 0; ) {
    const block = parseBlock(data, child);
    children.push(block);
    child = align4(block.end);
  }
  return { key, text, children, end };
}
