import { mkdir, mkdtemp, rm, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { checkMpvExecutable, readVersionStrings } from './mpvCheck.js';

let dir: string;

beforeEach(async () => {
  dir = await mkdtemp(path.join(os.tmpdir(), 'mediaman-mpvcheck-'));
});
afterEach(async () => {
  await rm(dir, { recursive: true, force: true });
});

function utf16z(text: string): Buffer {
  return Buffer.from(`${text}\0`, 'utf16le');
}

function pad4(buffer: Buffer): Buffer {
  return Buffer.concat([buffer, Buffer.alloc((4 - (buffer.length % 4)) % 4)]);
}

/** One VS_VERSIONINFO-style block, padded to a DWORD boundary. */
function block(key: string, value: Buffer, valueLength: number, isText: boolean, children: Buffer[] = []): Buffer {
  const head = pad4(Buffer.concat([Buffer.alloc(6), utf16z(key)]));
  const body = Buffer.concat([head, pad4(value), ...children]);
  body.writeUInt16LE(body.length, 0);
  body.writeUInt16LE(valueLength, 2);
  body.writeUInt16LE(isText ? 1 : 0, 4);
  return body;
}

function versionInfo(strings: Record<string, string>): Buffer {
  const fixed = Buffer.alloc(52);
  fixed.writeUInt32LE(0xfeef04bd, 0);
  const entries = Object.entries(strings).map(([key, value]) => block(key, utf16z(value), value.length + 1, true));
  const table = block('040904b0', Buffer.alloc(0), 0, true, entries);
  return block('VS_VERSION_INFO', fixed, 52, false, [block('StringFileInfo', Buffer.alloc(0), 0, true, [table])]);
}

/** A minimal PE32+ image with one `.rsrc` section holding `strings` as its version resource, or no resource at all. */
function peImage(strings?: Record<string, string>): Buffer {
  const sectionRva = 0x1000;
  const rawPointer = 0x200;
  let rsrc = Buffer.alloc(0);
  if (strings !== undefined) {
    const version = versionInfo(strings);
    rsrc = Buffer.alloc(0x58 + version.length);
    const directory = (at: number, id: number, target: number) => {
      rsrc.writeUInt16LE(1, at + 14);
      rsrc.writeUInt32LE(id, at + 16);
      rsrc.writeUInt32LE(target, at + 20);
    };
    directory(0x00, 16, 0x8000_0000 + 0x18);
    directory(0x18, 1, 0x8000_0000 + 0x30);
    directory(0x30, 0x409, 0x48);
    rsrc.writeUInt32LE(sectionRva + 0x58, 0x48);
    rsrc.writeUInt32LE(version.length, 0x4c);
    version.copy(rsrc, 0x58);
  }
  const image = Buffer.alloc(rawPointer + rsrc.length);
  image.write('MZ', 0, 'latin1');
  image.writeUInt32LE(0x40, 0x3c);
  image.write('PE\0\0', 0x40, 'latin1');
  image.writeUInt16LE(0x8664, 0x44);
  image.writeUInt16LE(1, 0x46);
  image.writeUInt16LE(0xf0, 0x54);
  const optional = 0x58;
  image.writeUInt16LE(0x20b, optional);
  image.writeUInt32LE(16, optional + 108);
  if (strings !== undefined) {
    image.writeUInt32LE(sectionRva, optional + 112 + 16);
    image.writeUInt32LE(rsrc.length, optional + 112 + 20);
  }
  const section = optional + 0xf0;
  image.write('.rsrc', section, 'latin1');
  image.writeUInt32LE(rsrc.length, section + 8);
  image.writeUInt32LE(sectionRva, section + 12);
  image.writeUInt32LE(rsrc.length, section + 16);
  image.writeUInt32LE(rawPointer, section + 20);
  rsrc.copy(image, rawPointer);
  return image;
}

const mpvStrings = { CompanyName: 'mpv', FileDescription: 'mpv', OriginalFilename: 'mpv.exe', ProductName: 'mpv' };
const notepadStrings = {
  CompanyName: 'Microsoft Corporation',
  FileDescription: 'Notepad',
  InternalName: 'Notepad',
  OriginalFilename: 'NOTEPAD.EXE',
  ProductName: 'Microsoft® Windows® Operating System',
};

async function file(name: string, content: Buffer | string): Promise<string> {
  const full = path.join(dir, name);
  await writeFile(full, content);
  return full;
}

describe('readVersionStrings', () => {
  it('reads the text fields of the version resource', async () => {
    const mpv = await file('mpv.exe', peImage(mpvStrings));

    await expect(readVersionStrings(mpv)).resolves.toEqual(new Map(Object.entries(mpvStrings)));
  });

  it('is undefined for an image without a version resource', async () => {
    await expect(readVersionStrings(await file('mpv.exe', peImage()))).resolves.toBeUndefined();
  });

  it('throws for a file that is not a Windows executable', async () => {
    await expect(readVersionStrings(await file('mpv.exe', '#!/bin/sh\necho hi\n'))).rejects.toThrow();
  });
});

describe('checkMpvExecutable', () => {
  it('accepts MPV on Windows by its name and version resource', async () => {
    await expect(checkMpvExecutable(await file('mpv.exe', peImage(mpvStrings)), 'win32')).resolves.toEqual({ ok: true });
    await expect(checkMpvExecutable(await file('MPV.COM', peImage(mpvStrings)), 'win32')).resolves.toEqual({ ok: true });
  });

  it('refuses another program renamed mpv.exe, without running it', async () => {
    const renamed = await file('mpv.exe', peImage(notepadStrings));

    const check = await checkMpvExecutable(renamed, 'win32');

    expect(check).toEqual({
      ok: false,
      message: `${renamed} isn't MPV (its version information doesn't name mpv), so MediaMan won't run it. Choose the MPV player (mpv.exe) instead.`,
    });
  });

  it('refuses an mpv.exe with no version resource or that is not a program', async () => {
    await expect(checkMpvExecutable(await file('mpv.exe', peImage()), 'win32')).resolves.toMatchObject({ ok: false });
    await mkdir(path.join(dir, 'text'));
    const text = path.join(dir, 'text', 'mpv.exe');
    await writeFile(text, 'not a program');
    await expect(checkMpvExecutable(text, 'win32')).resolves.toMatchObject({ ok: false });
  });

  it('refuses a file not named mpv before reading it', async () => {
    const notepad = await file('notepad.exe', peImage(mpvStrings));

    await expect(checkMpvExecutable(notepad, 'win32')).resolves.toEqual({
      ok: false,
      message: `${notepad} isn't MPV, so MediaMan won't run it. Choose the MPV player (mpv.exe) instead.`,
    });
    await expect(checkMpvExecutable(await file('vlc', 'x'), 'linux')).resolves.toMatchObject({ ok: false });
  });

  it('refuses a missing file and a folder', async () => {
    const missing = path.join(dir, 'mpv');

    await expect(checkMpvExecutable(missing, 'linux')).resolves.toEqual({
      ok: false,
      message: `MPV wasn't found at ${missing}. Choose the MPV player (mpv) instead.`,
    });
    await mkdir(missing);
    await expect(checkMpvExecutable(missing, 'linux')).resolves.toMatchObject({ ok: false });
  });

  it('accepts an mpv file on Linux and macOS by its name', async () => {
    const mpv = await file('mpv', 'binary');

    await expect(checkMpvExecutable(mpv, 'linux')).resolves.toEqual({ ok: true });
    await expect(checkMpvExecutable(mpv, 'darwin')).resolves.toEqual({ ok: true });
  });

  it('accepts a bare mpv looked up on PATH, and nothing else bare', async () => {
    await expect(checkMpvExecutable('mpv', 'linux')).resolves.toEqual({ ok: true });
    await expect(checkMpvExecutable('mpv', 'win32')).resolves.toEqual({ ok: true });
    await expect(checkMpvExecutable('mpv.exe', 'win32')).resolves.toEqual({ ok: true });
    await expect(checkMpvExecutable('notepad', 'win32')).resolves.toMatchObject({ ok: false });
  });
});
