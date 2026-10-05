# 0005. Desktop build: Electron compiled with plain tsc

- **Status:** Accepted
- **Date:** 2026-10-05
- **PLAN task:** 0.6

## Context

The desktop app (SPEC §10.1) is a thin Electron shell. The renderer is the shared web UI, served by Vite in
development and by the server in Local mode. The main process holds only window setup, MPV control (`IPlayer`)
and the local server (`IServerProcess`). Sandboxed preload scripts must be CommonJS.

## Options considered

1. **Plain `tsc`:** the main process is emitted as ESM and the preload (`.cts`) as CommonJS into `desktop/dist/`.
   Pros: no extra tooling, and the output maps 1:1 to the sources. Cons: no main-process HMR.
2. **electron-vite:** bundles main, preload and renderer with HMR. Cons: the renderer is the web workspace's job
   already, and it adds a toolchain and plugin layer for a few hundred lines of main-process code.

## Decision

Use plain `tsc` (`tsconfig.build.json` emits, `tsconfig.json` type-checks sources and tests).
The package is `"type": "module"`, and the preload is `src/preload/index.cts` so it compiles to `index.cjs`.
The window loads the web UI from `MEDIAMAN_UI_URL` (default: the Vite dev server, `http://localhost:5173`).
It stays on that origin, and only http(s) links are handed to the system browser.

`SubtitleTrack` (used by `IPlayer.play`, not defined in the SPEC) is a desktop-internal type `{ url, language?, title?, isDefault? }`, enough for MPV's `sub-add`; it is not part of the HTTP contract.

Dependencies: `electron` (the runtime), plus `typescript`, `vitest` and `@types/node`, pinned the same as `web/`.

## Consequences

- `npm start -w desktop` needs the web UI running (`npm run dev:mock -w web`). Local mode will take the URL from `IServerProcess.start()` instead.
- If `ELECTRON_RUN_AS_NODE` is set (some Electron-based terminals set it), Electron starts as plain Node and fails to import `BrowserWindow`. Unset it before running `npm start`.
- `FakePlayer` and `FakeServerProcess` (SPEC §10.4) arrive with their workstreams, as do packaging and signing (Stage 4).
- Revisit electron-vite if main-process code grows enough that rebuild-and-restart becomes slow.
