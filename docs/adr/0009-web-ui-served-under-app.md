# 0009. The server serves the web UI under /app/; the API stays at the root

- **Status:** Accepted
- **Date:** 2026-10-14
- **PLAN task:** 2.5.2b

## Context

A packaged app can't depend on the Vite dev server, and browsers and phones need a UI from the server itself
(SPEC §5 One UI everywhere, §10.1). So the server serves the built web UI (`web/dist`) next to the API, and the
app's client-side routes need to fall back to its main page so a reload or a shared link works.

The API is mapped at the root (`/titles/{titleId}`, `/search`, `/profiles`, `/settings/metadata`, ...), and several
UI routes have the same paths. With both at the root, reloading `/titles/x` in a browser would reach the API and get
`401` JSON instead of the app. The UI can't decide this by guessing (for example from `Accept: text/html`), and every
API route has to keep its permission (SPEC §5, §9).

## Options considered

1. **UI under `/app/`, API at the root.** The fallback only covers `/app/...`, so it can never answer an API path.
   No contract, signed-URL or test changes. Cons: browser URLs read `/app/titles/...`, and the prefix has to stay
   the same in three places.
2. **API under `/api`, UI at the root.** Matches the dev proxy. Cons: a contract change (`servers`), signed artwork
   and stream URLs and the desktop base URL change, and every server test that uses absolute paths does too.
3. **UI at the root, picking HTML for browser navigations by header.** Fragile: a stream or artwork URL opened in a
   tab would get the app, and new API routes would collide without warning.

## Decision

Option 1. The server serves the built UI from `web/` beside it (or `MediaMan:Web:RootPath`) under `/app/`, as
middleware rather than endpoints. `/` and `/app` redirect to `/app/`, and paths under `/app/` with no file extension
get `index.html`.

## Consequences

- Requests that match an API route never reach the UI middleware. Only the UI's own files are reachable signed out
  and before setup, and the table-driven permission test still covers exactly the API routes. The local-network
  guard runs first, so the UI is refused to outside clients like everything else (SPEC §7.12).
- `/app` has to stay in sync in three places: the server's `WebUi.RequestPath`, the web build's Vite `base` (with
  the router's `basepath` derived from it), and the desktop's `SERVER_UI_PATH`. The publish script checks the built
  `index.html` loads its assets from `/app/`.
- In the web code, route paths and `?redirect=` values stay without the prefix. Navigation goes through the router
  (`to` or `href`) so it adds the prefix, never through `router.history.push`.
- The dev server and the mock and server e2e runs keep the UI at `/`.
- The packaged desktop app loads `http://127.0.0.1:<port>/app/` from the server it starts. The UI and the API share
  that origin, so it needs no CORS. `MEDIAMAN_UI_URL` stays the dev override.
