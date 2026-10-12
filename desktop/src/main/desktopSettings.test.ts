import { mkdtemp, readFile, rm, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { DesktopSettings, executableOf, mpvPathFrom, mpvSettingFrom } from './desktopSettings.js';

let dir: string;
let file: string;

beforeEach(async () => {
  dir = await mkdtemp(path.join(os.tmpdir(), 'mediaman-settings-'));
  file = path.join(dir, 'settings.json');
});
afterEach(async () => {
  await rm(dir, { recursive: true, force: true });
});

describe('DesktopSettings', () => {
  it('starts empty and keeps the picked MPV across instances', async () => {
    const mpv = path.join(dir, 'mpv.exe');
    await expect(new DesktopSettings(file).get()).resolves.toEqual({});

    await new DesktopSettings(file).setMpvPath(mpv);

    await expect(new DesktopSettings(file).get()).resolves.toEqual({ mpvPath: mpv });
    expect(JSON.parse(await readFile(file, 'utf8'))).toEqual({ mpvPath: mpv });
  });

  it('forgets the picked MPV', async () => {
    await new DesktopSettings(file).setMpvPath(path.join(dir, 'mpv.exe'));

    await new DesktopSettings(file).clearMpvPath();

    await expect(new DesktopSettings(file).get()).resolves.toEqual({});
    expect(JSON.parse(await readFile(file, 'utf8'))).toEqual({});
  });

  it.each([['not json'], ['[]'], ['{"mpvPath": 42}'], ['{"mpvPath": "  "}']])(
    'reads %s as the defaults',
    async (content) => {
      await writeFile(file, content, 'utf8');

      await expect(new DesktopSettings(file).get()).resolves.toEqual({});
    },
  );
});

describe('mpvPathFrom', () => {
  it('prefers the picked MPV, then MEDIAMAN_MPV_PATH, then mpv on PATH', () => {
    expect(mpvPathFrom({ mpvPath: '/picked/mpv' }, { MEDIAMAN_MPV_PATH: '/env/mpv' })).toBe('/picked/mpv');
    expect(mpvPathFrom({}, { MEDIAMAN_MPV_PATH: '/env/mpv' })).toBe('/env/mpv');
    expect(mpvPathFrom({}, {})).toBe('mpv');
  });
});

describe('mpvSettingFrom', () => {
  it('names where the MPV in use comes from', () => {
    expect(mpvSettingFrom({ mpvPath: '/picked/mpv' }, { MEDIAMAN_MPV_PATH: '/env/mpv' })).toEqual({ path: '/picked/mpv', source: 'picked' });
    expect(mpvSettingFrom({}, { MEDIAMAN_MPV_PATH: '/env/mpv' })).toEqual({ path: '/env/mpv', source: 'environment' });
    expect(mpvSettingFrom({}, { MEDIAMAN_MPV_PATH: '  ' })).toEqual({ path: 'mpv', source: 'default' });
  });
});

describe('executableOf', () => {
  it('opens an mpv.app bundle on macOS only', () => {
    const bundle = path.join(dir, 'mpv.app');
    const plain = path.join(dir, 'mpv');
    expect(executableOf(bundle, 'darwin')).toBe(path.join(bundle, 'Contents', 'MacOS', 'mpv'));
    expect(executableOf(plain, 'darwin')).toBe(plain);
    expect(executableOf(bundle, 'win32')).toBe(bundle);
  });
});
