// Preload: the narrow bridge between the web UI and the main process. CommonJS because sandboxed preloads must be.
// Add desktop-only features here deliberately; never expose ipcRenderer or Node APIs wholesale.
import { contextBridge } from 'electron';

contextBridge.exposeInMainWorld('mediamanDesktop', {
  isDesktop: true,
  platform: process.platform,
});
