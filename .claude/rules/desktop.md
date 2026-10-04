---
paths:
  - "desktop/**"
---

# Desktop rules

- The main process drives MPV only through `IPlayer` and the local server only through `IServerProcess`; tests use `FakePlayer` and `FakeServerProcess` from `desktop/src/fakes` (SPEC §10.3, §10.4).
- The renderer is the shared web UI; desktop-only features are exposed through a narrow preload bridge with `contextIsolation` on and `nodeIntegration` off.
- MPV runs as a separate process controlled over JSON IPC; v1 uses its own window, no embedding (SPEC §4, §10.1).
- Tokens are stored with Electron `safeStorage`, never in plain files or localStorage (SPEC §8 accounts [M], §9).
- `mpv/osc.lua` follows the purple theme of SPEC §11 and talks to the main process only through MPV script messages and properties.
- Report playback progress every 10 s and on pause, seek and quit (SPEC §7.4).
