// The server and MPV shipped inside a packaged app (SPEC §9 bundled tools, §10.1). The installer (PLAN 2.5.4) copies
// them outside the asar into the resources folder, so they can be run and MPV can read its config and OSC:
//
//   <resources>/server/MediaMan.Server(.exe), ffprobe(.exe), licenses/
//   <resources>/mpv/mpv(.exe) and its files, mpv.conf, osc.lua
//
// Windows MPV only reads a `portable_config/` folder beside the exe, so our `mpv.conf` there is loaded only through
// `mpvArgs`. Kept free of runtime Electron imports so it can be unit tested.
import path from 'node:path';
import { mpvAssetsIn, type MpvAssets } from './mpvPlayer.js';

export interface BundledTools {
  serverPath: string;
  mpvPath: string;
  mpvAssets: MpvAssets;
}

/** Where a packaged app keeps its server and MPV under `resourcesPath` (Electron's `process.resourcesPath`). */
export function bundledTools(resourcesPath: string, platform: NodeJS.Platform): BundledTools {
  const exe = platform === 'win32' ? '.exe' : '';
  return {
    serverPath: path.join(resourcesPath, 'server', `MediaMan.Server${exe}`),
    mpvPath: path.join(resourcesPath, 'mpv', `mpv${exe}`),
    mpvAssets: mpvAssetsIn(resourcesPath),
  };
}
