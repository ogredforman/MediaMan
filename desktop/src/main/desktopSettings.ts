// Desktop-only settings in `<userData>/settings.json`: for now the MPV executable picked in "Open MPV settings"
// (SPEC §7.4 step 6). Server settings live on the server, never here.
import { mkdir, readFile, rename, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { resolveMpvPath } from './mpvPlayer.js';

export interface DesktopSettingsValues {
  /** The MPV executable the user picked; absent means `MEDIAMAN_MPV_PATH` or `mpv` from PATH. */
  mpvPath?: string;
}

export class DesktopSettings {
  private cache: DesktopSettingsValues | undefined;
  private writes: Promise<void> = Promise.resolve();

  constructor(private readonly filePath: string) {}

  /** The saved settings; a missing or unreadable file counts as the defaults. */
  async get(): Promise<DesktopSettingsValues> {
    this.cache ??= await this.load();
    return { ...this.cache };
  }

  async setMpvPath(mpvPath: string): Promise<void> {
    const next = { ...(await this.get()), mpvPath };
    this.cache = next;
    const write = this.writes.then(() => this.save(next));
    this.writes = write.catch(() => undefined);
    await write;
  }

  private async load(): Promise<DesktopSettingsValues> {
    let raw: string;
    try {
      raw = await readFile(this.filePath, 'utf8');
    } catch {
      return {};
    }
    try {
      return parseSettings(JSON.parse(raw));
    } catch {
      return {};
    }
  }

  private async save(values: DesktopSettingsValues): Promise<void> {
    await mkdir(path.dirname(this.filePath), { recursive: true });
    const temp = `${this.filePath}.tmp`;
    await writeFile(temp, JSON.stringify(values, null, 2), 'utf8');
    await rename(temp, this.filePath);
  }
}

function parseSettings(value: unknown): DesktopSettingsValues {
  if (typeof value !== 'object' || value === null) return {};
  const mpvPath = (value as { mpvPath?: unknown }).mpvPath;
  return typeof mpvPath === 'string' && mpvPath.trim() !== '' ? { mpvPath } : {};
}

/** The picked MPV, else `MEDIAMAN_MPV_PATH`, else `mpv` from PATH. */
export function mpvPathFrom(settings: DesktopSettingsValues, env: Readonly<Record<string, string | undefined>>): string {
  const picked = settings.mpvPath?.trim();
  if (picked) return picked;
  return resolveMpvPath(env);
}

/** On macOS a picked `mpv.app` bundle means the executable inside it. */
export function executableOf(picked: string, platform: NodeJS.Platform): string {
  return platform === 'darwin' && picked.endsWith('.app') ? path.join(picked, 'Contents', 'MacOS', 'mpv') : picked;
}
