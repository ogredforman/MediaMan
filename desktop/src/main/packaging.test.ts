// The Windows installer config (PLAN 2.5.4, ADR 0010) must put the server and MPV where the packaged app looks for
// them (`bundledTools`), and must never delete the user's data on uninstall or upgrade.
import { existsSync, readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';
import { bundledTools } from './bundled.js';

const desktop = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');

interface FileSet {
  from: string;
  to: string;
  filter?: string[];
}

interface BuilderConfig {
  appId: string;
  files: string[];
  extraResources: FileSet[];
  extraFiles: FileSet[];
  win: { icon: string };
  nsis: { oneClick: boolean; perMachine: boolean; deleteAppDataOnUninstall: boolean; installerIcon: string };
}

const config = JSON.parse(readFileSync(path.join(desktop, 'electron-builder.json'), 'utf8')) as BuilderConfig;
const resources = path.join('install', 'resources');
const tools = bundledTools(resources, 'win32');

/** Where the installed app gets each file the installer copies outside the asar. */
function shipped(): string[] {
  return config.extraResources.flatMap((set) => {
    if (set.filter) return set.filter.map((file) => path.join(resources, set.to, file));
    // Whole folders from the build, which hold the executables under the names the app looks for.
    if (set.from.endsWith(path.posix.join('server', 'win-x64'))) return [path.join(resources, set.to, path.basename(tools.serverPath))];
    if (set.from.endsWith(path.posix.join('mpv', 'win-x64'))) return [path.join(resources, set.to, path.basename(tools.mpvPath))];
    return [];
  });
}

describe('Windows installer config', () => {
  it('ships the server, MPV and its config where the packaged app looks for them', () => {
    expect(shipped()).toEqual(
      expect.arrayContaining([tools.serverPath, tools.mpvPath, tools.mpvAssets.config]),
    );
  });

  it('ships the bundled MPV config from the desktop sources', () => {
    const mpvSet = config.extraResources.find((set) => set.filter !== undefined);
    expect(mpvSet?.filter).toEqual(['mpv.conf']);
    for (const file of mpvSet?.filter ?? []) expect(existsSync(path.join(desktop, mpvSet?.from ?? '', file))).toBe(true);
  });

  it('installs per user without deleting app data, so upgrades and uninstalls keep it', () => {
    expect(config.nsis.oneClick).toBe(true);
    expect(config.nsis.perMachine).toBe(false);
    expect(config.nsis.deleteAppDataOnUninstall).toBe(false);
  });

  it('keeps the appId upgrades are matched by', () => {
    expect(config.appId).toBe('io.github.ogredforman.mediaman');
  });

  it('ships the licence and third-party notices, and the icon exists', () => {
    for (const set of config.extraFiles) expect(existsSync(path.join(desktop, set.from))).toBe(true);
    expect(config.extraFiles.map((set) => set.to)).toEqual(expect.arrayContaining(['LICENSE.txt', 'THIRD-PARTY-NOTICES.md']));
    expect(existsSync(path.join(desktop, config.win.icon))).toBe(true);
    expect(existsSync(path.join(desktop, config.nsis.installerIcon))).toBe(true);
  });
});
