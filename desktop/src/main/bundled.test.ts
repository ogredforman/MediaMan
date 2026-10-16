import path from 'node:path';
import { describe, expect, it } from 'vitest';
import { bundledTools } from './bundled.js';

describe('bundledTools', () => {
  const resources = path.resolve('install', 'resources');

  it('names the Windows executables with .exe', () => {
    expect(bundledTools(resources, 'win32')).toEqual({
      serverPath: path.join(resources, 'server', 'MediaMan.Server.exe'),
      mpvPath: path.join(resources, 'mpv', 'mpv.exe'),
      mpvAssets: { config: path.join(resources, 'mpv', 'mpv.conf') },
    });
  });

  it('names them without an extension elsewhere', () => {
    const tools = bundledTools(resources, 'linux');
    expect(tools.serverPath).toBe(path.join(resources, 'server', 'MediaMan.Server'));
    expect(tools.mpvPath).toBe(path.join(resources, 'mpv', 'mpv'));
  });
});
