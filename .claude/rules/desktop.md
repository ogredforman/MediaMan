---
paths:
  - "desktop/**"
---

# Desktop rules

- The main process drives MPV only through `IPlayer` and the local server only through `IServerProcess`; tests use `FakePlayer` and `FakeServerProcess` from `desktop/src/fakes` (SPEC §10.3, §10.4).
- The renderer is the shared web UI; desktop-only features are exposed through a narrow preload bridge with `contextIsolation` on and `nodeIntegration` off.
- MPV runs as a separate process controlled over JSON IPC, drawing inside the main window with `--wid` on Windows and Linux; macOS uses libmpv only through its player addon (Stage 8). Player controls are React in a transparent overlay window owned by the main window and kept on its content bounds. On Windows the app runs with Chromium's `disable-direct-composition` switch, or MPV's surface is covered (SPEC §10.1, ADR 0011).
- Tokens are stored with Electron `safeStorage`, never in plain files or localStorage (SPEC §8 accounts [M], §9).
- Player controls, Up next and notices live in the React overlay, not in MPV; MPV in a window of its own keeps its built-in OSC.
- Report playback progress every 10 s and on pause, seek and quit (SPEC §7.4).
