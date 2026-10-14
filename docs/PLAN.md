# MediaMan — Implementation Plan

> Companion to [`SPEC.md`](./SPEC.md). The spec defines *what* and *why*; this plan defines *order*,
> *scope per stage*, and *exit criteria*. Each stage ends in a runnable, usable state. Don't pull
> later-stage work forward.

**Current stage: Stage 2.5 — Early packaging (Windows)**

Task IDs are `<stage>.<task>` (e.g. `1.5`). A stage inserted between two others gets a dotted ID
(Stage `1.5`, tasks `1.5.1`, ...), and a task inserted after another gets a letter (`1.5.13b`).
Tick tasks `[x]` as they're finished.
`/devflow:next-task` picks the first unchecked task in the current stage; `/devflow:park` adds ideas
to the Parked section at the bottom.

## Background

- MediaMan is a free, self-hosted, all-in-one media server, MPV-based player, torrent client and torrent
  search engine with a streaming-service UI: "Plex with no paywall and torrenting built in".
- Why: today the same experience takes five tools (Plex/Jellyfin, Sonarr, Radarr, Prowlarr, qBittorrent).
  Two earlier attempts proved the idea, but this is a clean rewrite that reuses nothing.
- Stack: .NET 10 / ASP.NET Core minimal APIs, EF Core + SQLite (WAL), ffmpeg/ffprobe as a separate
  process. The web UI uses React + Vite + TypeScript, TanStack Query/Router and Tailwind. The desktop
  app is Electron and drives MPV over JSON IPC with a custom purple Lua OSC.
- Contracts first: `contracts/openapi.yaml` and the module interfaces in SPEC §10.3 are the only
  ways components talk to each other. Every external dependency sits behind an interface with a fake.
- Safety rules that shape the code: external libraries are read-only, the server enforces every
  permission, and the Superadmin can't be locked out. From Stage 3: torrents never leak outside the VPN
  path, and downloaded files are never executed.
- Platforms: Windows and Linux fully supported, macOS best-effort. CI runs on Windows and Linux.
- MVP guardrail (scope): the MVP is Stages 2–6. On Windows, the user adds an external library,
  MediaMan matches it against TMDB with artwork, and the Superadmin signs in and browses a
  Netflix-style UI. A title plays in MPV through the desktop app, resumes, and offers the next episode
  (Stage 2). A title not in the library is found in the catalog, searched across indexers with quality
  chips, and downloaded through the VPN-gated torrent client, then imported automatically (Stages 3–4).
  The same library plays in a browser with direct play, remux or transcode (Stage 5), and is reachable
  safely from outside the LAN, including from the desktop app and a phone browser (Stage 6). Multiple
  users and roles, metadata extras, Docker and automation come after the MVP.
- Licence: AGPL-3.0. The repo stays private until it's "finished".

---

## Stage 0 — Foundations

**Goal:** an empty but correctly wired app that builds, runs, and tests on every target platform.

- [x] **0.1** Initialize the git repo, AGPL-3.0 `LICENSE`, `.gitignore`, `.editorconfig` and the repo layout (SPEC §10.2). Create `server/MediaMan.sln` with every `src/` project, one test project per module (e.g. `tests/MediaMan.Library.Tests`) (xUnit, AwesomeAssertions; see ADR 0001), and `tests/MediaMan.Fakes`, with `server/Directory.Build.props` turning on nullable reference types and warnings as errors. Add a root `package.json` with npm workspaces and the commands AGENTS.md will list: `npm run build` (all parts), `npm test` (all tests), `npm run lint` (ESLint + `dotnet format --verify-no-changes`). Server-only equivalents are `dotnet build server/MediaMan.sln` and `dotnet test server/MediaMan.sln`. Web and desktop join these scripts in 0.5 and 0.6.
- [x] **0.2** Add the domain types (SPEC §6.1) and declare every module interface from SPEC §10.3 in `MediaMan.Core`: `IClock` with a `SystemClock`, `ParsedRelease`, `ProbeResult`, `TitleCandidate` and the rest, declarations only. Add unit tests for any value-type logic (e.g. `ExternalId`, `InfoHash` parsing).
- [x] **0.3** Add the `MediaMan.Data` EF Core DbContext for the full SPEC §6.1 schema (the frozen Phase 0 contract), with SQLite in WAL mode and the initial migration. Test that migrations apply to a temp database and that WAL is on (SPEC §9 resilience).
- [x] **0.4** Create `contracts/openapi.yaml` (OpenAPI 3.1) with shared conventions: the error model, bearer auth scheme, pagination, and the SSE event envelope. Add `GET /health`. Wire the `MediaMan.Server` host (DI, config, SSE endpoint skeleton), and add a conformance-test harness that validates responses against the contract (SPEC §10.1, §12).
- [x] **0.5** Scaffold `web/`: React + Vite + TypeScript (`strict: true`), Tailwind with the theme tokens from SPEC §11, TanStack Router/Query, an API client generated from `contracts/openapi.yaml`, MSW handlers generated from the contract, Vitest + Testing Library, a Playwright smoke test, and ESLint. Join the root build/test/lint scripts: add `web` to the npm workspaces and ESLint to `npm run lint`.
- [x] **0.6** Scaffold `desktop/`: an Electron TypeScript app (`strict: true`; `src/main`, `src/preload`) that loads the web UI, plus the `IPlayer` and `IServerProcess` declarations (SPEC §10.3), `mpv/` placeholders, Vitest and ESLint. Join the root build/test/lint scripts.
- [x] **0.7** Add GitHub Actions CI that runs `npm run build`, `npm run lint` and `npm test` on Windows and Linux (SPEC §12). Start `docs/CODEMAP.md` from the devflow template, describing the projects, workspaces and wiring so far.

**Exit criteria:**
- `npm run build`, `npm run lint` and `npm test` pass locally on Windows and in CI on Windows and Linux.
- The server starts and `GET /health` returns healthy, and its response passes the OpenAPI conformance test.
- The web app renders an empty themed shell against MSW. The Electron app opens a window showing it.

---

## Stage 1 — Prototype (fakes, no real external dependencies)

**Goal:** the complete core loop working end to end, with every external dependency replaced by a
deterministic fake. Validates the data model, architecture, and UI before real integrations add cost
and unpredictability.

- [x] **1.1** Build the prototype-scope fakes in `MediaMan.Fakes` (SPEC §10.4): `FakeClock`, `FakeMetadataProvider` with about 30 JSON fixture titles in `tests/fixtures/metadata/` (remakes, same-name shows, anime with absolute numbering, a show with specials), error injection and query recording, `FakeArtworkProvider`, `FakeMediaProbe` and `FakeFileWatcher`. Torrent, indexer and desktop fakes come with their workstreams.
- [x] **1.2** Add the dev configuration: the server starts with a SQLite path and one media path from config, with no wizard. It registers that folder as an External library and picks fake or real providers per dependency from config (SPEC §8 setup [P]).
- [x] **1.3** Implement `ReleaseParser` for movies and episodes: title, year, season, episode(s), multi-episode ranges, and parent-folder context (SPEC §7.2 step 3, §10.3). Start `tests/corpus/release-names.json` with a corpus-driven test.
- [x] **1.4** Extend `ReleaseParser` with resolution, source, codec, HDR/Dolby Vision, audio, group and anime absolute numbering. Grow the corpus to ≥300 real-world names including junk, and record the accuracy baseline the test enforces (SPEC §8, §12).
- [x] **1.5** Implement the real ffprobe `IMediaProbe` in `MediaMan.Playback` (a process wrapper that parses JSON for container, streams, duration and HDR format). Add integration tests on tiny generated samples, skipped when ffprobe is absent (SPEC §8 [P], §10.4).
- [x] **1.6** Build the scanner in `MediaMan.Library`. It enumerates roots recursively, ignores samples, extras folders and non-video files, and detects new, changed and missing files against stored MediaFiles. It probes new and changed files, marking unreadable ones `NeedsAttention`, and marks missing files `Missing`. Test incremental rescans in per-test temp directories (SPEC §7.2 steps 1–2 and 6).
- [x] **1.7** Build the matcher. It queries `IMetadataProvider`, scores candidates on title similarity, year, kind and episode existence, and links at ≥0.85 confidence. Below that it creates a MatchReview with the top candidates. Provider errors leave files `Pending` with backoff. Add fixture tests for remakes, same-name shows, off-by-one years, specials and the low-confidence path (SPEC §7.2 steps 4–5 and 7, §12).
- [x] **1.8** Import matched metadata: persist Title, Season and Episode data, plus artwork refs (poster, backdrop, stills) from the provider. Create FileLinks, including one per episode for multi-episode files (SPEC §6, §8 metadata [P], §8 multi-episode [P]).
- [x] **1.9** Add the library browse API to the contract and server: list libraries, browse titles (movies/shows), and title detail with seasons, episodes and versions. Add conformance tests (SPEC §7.2, §10.1).
- [x] **1.9b** Add scanning to the library API: trigger a scan (a background worker runs scan → match → import), scan progress over SSE with the current scan state on each Library, and a read-only list of a library's NeedsAttention files with their reason, parsed guess and MatchReview candidates. Add conformance tests and an end-to-end scan test on the fakes (SPEC §7.2, §10.1).
- [x] **1.10** Build the web app shell (navigation and theme) and the Movies/Shows poster grid, with live scan progress (SPEC §11). Add component tests with MSW.
- [x] **1.11** Build the web title detail page: backdrop hero, metadata, season tabs, an episode list with stills, and the versions list (SPEC §8 browsing [P], §11). Add component tests with MSW.
- [x] **1.12** Add the dev-only play endpoint, which launches MPV on the server host for a Playable's file. It's registered only in the dev configuration and returns 404 otherwise (SPEC §8 playback [P], §13). Wire the Play button.
- [x] **1.12b** Add a Playwright test for browse → detail → Play against the server running on fakes (dev configuration with `Providers:Player=Fake`, a temp media folder of placeholder files). The Vite dev server proxies `/api/*` to the server when `MEDIAMAN_SERVER_URL` is set, with `VITE_API_BASE_URL=/api`, so no CORS is needed; `test:e2e` runs both Playwright configs.

**Exit criteria:**
- With the dev configuration pointing at a folder of placeholder files, the server scans it, matches it against `FakeMetadataProvider`, and shows movies and shows (including multi-episode files and a MatchReview) in the web UI.
- Pressing Play on the detail page launches MPV on the local file.
- The parser corpus (≥300 names) passes at the recorded baseline, and all tests pass in CI on Windows and Linux.

---

## Stage 2 — MVP core (real integrations, desktop playback)

**Goal:** swap fakes for real integrations, with just enough polish that the owner uses it for real.
This is the first of the five MVP stages (2–6); later MVP stages build on the contracts set here, so
the tasks below that shape them say what to leave room for.

- [x] **2.1** Implement the real TMDB `IMetadataProvider` with the shipped key (overridable in settings), a 7-day response cache and rate-limit backoff. Test it against recorded HTTP responses through a stub handler, with no network (SPEC §8 metadata [M]).
- [x] **2.2** Cache artwork locally, extract each image's dominant color, and serve cached images from an artwork endpoint. Tint the detail hero from the dominant color (SPEC §8 metadata [M], §11).
- [x] **2.3** Add `MediaMan.Auth` foundations: the per-install key (generated on first run, stored next to the database, HKDF subkeys per purpose) with the `AesGcmSecretProtector` built from it so secrets are encrypted at rest (ADR 0003, ADR 0008), replacing the `UnavailableSecretProtector` placeholder registered in 1.2; Argon2id password hashing; `AccountService.CreateSuperadminAsync` for setup (2.4) and `PermissionsOf`; and a data-layer guard with tests that the Superadmin can't be demoted, deleted, disabled or have its role changed (SPEC §5, §7.11, §12).
- [x] **2.3b** Add sign-in tokens. Contract first: `POST /auth/login` and `POST /auth/refresh` (`x-permission: none`; refresh is part of login, so update the contract's unauthenticated list). JWT access tokens (15 min, HS256 on an `InstallKey` subkey, `sub` + `sid`) and rotating opaque refresh tokens (30 days, sliding), each family persisted as a `Session` row with only the token hash stored, so Stage 6 can list and revoke sessions; reusing a rotated refresh token revokes the session (SPEC §7.11, §9, ADR 0008).
- [x] **2.3c** Enforce permissions. Contract first: `POST /auth/logout` (`authenticated`; revokes the token's session). Wire JwtBearer with `AccessTokens.ValidationParameters`. Enforce the `PermissionMetadata` every route declares (mirroring `x-permission`, PLAN 0.4): every route except `none` rejects requests without a valid token (401) or the permission (403), checking the session isn't revoked and the user isn't disabled; add the table-driven test over every mapped route and a `MediaManFactory` signed-in client for existing tests. Dev configuration only (Debug build, Development, `Dev:MediaPath` set, same gates as dev play): seed a dev Superadmin and `MediaMan:Dev:AutoSignIn` treating unauthenticated requests as it, so local dev runs and `web/e2e/server` keep working until 2.5; test that outside the dev configuration requests still get 401. Add the JwtBearer dependency to ADR 0008 (SPEC §7.11, §9, §12).
- [x] **2.3d** Sign artwork URLs. Contract first. Artwork `<img>` requests can't send a bearer token, so `ArtworkRef.url` carries `?exp=…&sig=…` (HMAC on its own `InstallKey` subkey, not bound to a user; `exp` rounded to the next UTC day plus one so URLs stay stable for a day and browser caching works), and `GET /artwork/{artworkId}` accepts a valid, unexpired signature in place of a bearer token. Test tampered, expired and day-stable URLs with `FakeClock`, and extend the permission test for the signed route (SPEC §9, ADR 0008).
- [x] **2.4** Add the local-network guard and the listen addresses, before anything binds to the LAN. Keep the bind address and scheme handling in one place in the host setup (`ServerBinding`); Stage 6 adds trusted proxies and HTTPS enforcement there. Local mode listens on loopback and Server mode on every interface (`MediaMan:Server:Mode`/`Port` until setup records the mode). Remote access starts off: refuse clients outside loopback, private LAN, link-local and Tailscale ranges, clients with an unknown address, and any request with forwarding headers, with `403` `remote_access.disabled`. Add the safety-critical tests for it (SPEC §5 private until opened, §7.12 step 1, §12).
- [x] **2.4b** Add the first-run setup API: `423 setup.required` on every route but health and setup until a Superadmin exists; one atomic `POST /setup` creates the Superadmin (password ≥12 characters), records the mode and adds the first library with path validation (errors keyed by field for the wizard to show inline), and returns tokens; `409 setup.complete` afterwards. The recorded mode overrides `MediaMan:Server:Mode` and changing it later (`PUT /server/mode`, takes effect on restart) needs `server.control`. The test factory completes setup by default. Add conformance tests and permission-table rows (SPEC §7.1, §8 setup [M]).
- [x] **2.5** Add sign-in with silent token refresh to the web UI. Refresh is single-flight across tabs (a Web Lock, or a localStorage lease where Web Locks is missing, as on plain-http LAN addresses): a refresh token works once, and reusing one revokes the session (ADR 0008). Pages inside the shell sit under an `_app` layout that probes the server and sends a `401` to `/sign-in` (returning to the page afterwards); the shell gets Sign out, and signing out in any tab signs every tab out. The guard follows the server's answer, not a stored token, so dev auto sign-in keeps working (SPEC §7.11, §11).
- [x] **2.5b** Build the web setup wizard (one step per screen with a progress indicator; `setup.invalid` field errors shown inline on their step) and send `423 setup.required` to it. Add a dev-configuration switch that skips seeding the dev account and library, so `web/e2e/server` starts un-set-up on the fakes, and a Playwright test for the setup flow that later specs reuse the session from instead of `Dev:AutoSignIn` (SPEC §7.1, §11, §12).
- [x] **2.6** Add Profiles to the contract and server: a default profile on account creation, create/edit/delete (name and a preset avatar; at most 10, the last can't be deleted), and the pick recorded on the session (`PUT /auth/session/profile`, `Session.activeProfileId`), so WatchState and later profile-scoped data follow the session's profile. Dev auto sign-in gets a session of its own (SPEC §6.1, §7.11 step 3, §8 accounts [M]).
- [x] **2.6b** Build the web profile picker ("Who's watching?") shown after sign-in and setup when the session has no profile, with manage mode (add, rename, change avatar, delete), and Switch profile in the shell. Add MSW handlers, component tests and the picker step to the server e2e setup flow (SPEC §7.11 step 3, §11).
- [x] **2.7** Support multiple libraries, each with several root paths and a content type. Contract first: `POST /libraries`, `GET`/`PATCH`/`DELETE /libraries/{libraryId}` (External only; deleting needs `server.control`, which the Admin preset lacks). Roots are validated as in setup and never overlap each other, another library's, or the data directory; removing a root or a library deletes rows only and a running scan blocks both; adding roots queues a scan (SPEC §5, §8 libraries [M]).
- [x] **2.7b** Build Settings → Libraries in the web UI: list the libraries with their scan state, add and edit (name, content type, several root folders, scan interval) with `library.invalid` field errors inline per root, and delete with a confirmation that says files on disk are kept. Add MSW handlers, component tests and a server e2e step (SPEC §8 libraries [M], §11).
- [x] **2.8** Implement the real `IFileWatcher` for incremental scans and the periodic full scan (default 6 h, driven by `IClock`). Make interrupted scans resume. Add the test that scans a read-only temp folder and proves external libraries are never written (SPEC §5, §7.2 step 8, §9, §12).
- [x] **2.9** Add the "Needs attention" and "Fix match" API: list MatchReviews, run a manual catalog search, relink a file (with season and episode mapping for shows), and persist the choice as a path override that rescans keep (SPEC §7.3).
- [x] **2.10** Build the "Needs attention" inbox (cards with the file name, parsed guess and candidate posters) and the "Fix match" dialog on any title (SPEC §7.3, §11).
- [x] **2.11** Support several versions per Playable: the API exposes them and remembers the profile's preferred version per title (the file and its resolution and HDR format, so a pick carries across a show's episodes), with `PUT`/`DELETE /titles/{titleId}/preferred-version` and `selectedMediaFileId` on each Playable (SPEC §7.4 step 2, §8 parsing [M]).
- [x] **2.11b** Build the detail page's version picker: pick a version of a movie or an episode, mark the one Play uses, and "Reset to best quality"; MSW handlers with state, component tests and a server e2e step (SPEC §8 parsing [M], §11).
- [x] **2.12** Add `POST /playback/sessions` with the full client-capabilities shape in the contract (containers, video and audio codecs, HDR support, maximum bitrate, subtitle formats) and a response that can carry a direct-stream or HLS URL, so Stage 5 browser playback adds a decision path without changing the contract (SPEC §4, §7.5). Implement only the MPV path now: it picks the version and returns a direct-stream URL (with HTTP range support), the resume position (after 2 minutes and before 90%) and external subtitle URLs. Stream and subtitle URLs are signed for the requesting session (`sessionUrl`, 12 h, ADR 0008), so MPV loads them without a token and revoking the session stops them (SPEC §7.4 steps 1–2).
- [x] **2.12b** Add `PUT /playables/{playableId}/progress` (keyed by Playable, with the session's profile, so it survives server restarts and needs no stored playback session): it updates WatchState, marks the Playable watched at ≥90% (watched stays set), and returns the next episode with a playable file (the next number in the season, else the first episode of the next non-special season). Pure `WatchRules` decide it (SPEC §7.4 steps 4–5, §8 playback [M]).
- [x] **2.13** Implement the real `IServerProcess` in Electron (Local mode spawns the server and waits for `/health`) and `FakeServerProcess`. Store tokens securely so the desktop app stays signed in (SPEC §8 accounts [M], §10.3, §10.4). Store the server base URL and tokens per server rather than assuming the local one, so Stage 6 can connect the desktop app to a remote server by URL.
- [x] **2.13b** Make the web UI use the desktop bridge when `window.mediamanDesktop` is present: load the tokens from `mediamanDesktop.tokens` before the first render and write every change back (no localStorage on desktop), and take the API base URL from `mediamanDesktop.server.baseUrl()` (with the server answering the UI's origin, e.g. CORS for the UI origin or the Vite proxy in dev) (SPEC §8 accounts [M], §9).
- [x] **2.14** Implement the real MPV `IPlayer` over JSON IPC and `FakePlayer`, with `IPlayer` contract tests run against both. MPV starts with the stream URL, start position, title and subtitles, reports progress every 10 s and on pause, seek and quit, ends once with eof, quit or the stream error, and `play()` rejects with `PlayerStartError` when MPV can't start (SPEC §7.4 steps 3–4, §10.3, §10.4).
- [x] **2.14b** Play on desktop: a `player` bridge channel; the web Play button creates a playback session, launches MPV through the bridge and reports progress; re-query the stream URL once when it fails; show "Open MPV settings" errors when MPV fails (a native picker for the MPV executable, saved in the desktop settings) (SPEC §7.4 steps 1–4 and 6).
- [x] **2.14c** Remove the dev-only play endpoint: `DevPlayEndpoints`, `IDevPlayer`, `MpvDevPlayer`, their tests, the `devPlay` operation and the MSW handler; the server e2e plays through a desktop bridge stand-in, and in a plain browser Play says to use the desktop app (SPEC §13).
- [x] **2.15** Write the purple Lua OSC and `mpv.conf`: title, a progress bar with chapters, and audio and subtitle menus (SPEC §8 playback [M], §11).
- [x] **2.16** Add "Up next": a cancellable 10 s countdown in the OSC that starts the next episode through the desktop app. It starts at the final credits (named chapters), never before a post-credits scene, and falls back to the end of the file (SPEC §7.4 step 5).
- [x] **2.16b** Add the Resume/Start over choice on the detail page. Contract first: each Playable in `TitleDetail` carries the profile's watch state (`resumeSeconds`, `watched`), so the page offers "Resume from 23:15" and "Start over" before Play (SPEC §7.4 step 5, §11 title detail).
- [x] **2.17** Add per-profile audio and subtitle language preferences, edited in profile settings. Contract first: `Profile` carries `audioLanguages`/`subtitleLanguages` (a `LanguageCode` enum of ISO 639-1 codes, at most 5 each, no repeats), changed through `PATCH /profiles/{profileId}`; `MediaMan.Core.Languages` holds the codes with their ISO 639-2 forms (SPEC §8 playback [M]).
- [x] **2.17b** Apply the language preferences when launching MPV. Contract first: `PlaybackSession` carries the profile's `audioLanguages`/`subtitleLanguages` as match tags (`Languages.MatchTags`: `de` → `de,ger,deu`); the desktop passes them as `--alang`/`--slang`, and the web `playOptions` selects the best-matching non-forced external subtitle (`isDefault`), since MPV's `--slang` doesn't pick among subtitles added after load (SPEC §7.4 step 3, §8 playback [M]).
- [x] **2.18** Add the Home API: the hero pick plus Continue Watching, Next Up, Recently Added and genre rows for the current profile (SPEC §8 browsing [M]).
- [x] **2.19** Build the Home screen: a hero banner (backdrop, title, overview, Play and More info) tinted from the dominant color, and horizontally scrolling, keyboard-navigable rows (SPEC §11).
- [x] **2.20** Add instant library search (an API endpoint plus a search-as-you-type UI) with results in under 100 ms (SPEC §8 browsing [M], §5). Split: this task is the `GET /search` contract, server and tests.
- [x] **2.20b** Build the search UI: a search field in the shell (`/` focuses it) opening a Search screen whose poster results update as you type (`searchLibrary`, previous results kept while the next query loads), a no-match state offering Movies and Shows, the MSW handler, Vitest coverage and a server e2e step (SPEC §8 browsing [M], §11 Search).
- [x] **2.21** Add watched markers and mark watched/unwatched for movies, episodes and whole seasons, on cards and in the episode list (SPEC §8 browsing [M]). Split: this task is the contract and server: `TitleSummary.watched` (every episode with a file outside specials), `PUT /playables/{playableId}/watched` and `PUT /titles/{titleId}/watched` (optional `seasonNumber`); watched drops the resume point and stamps episodes in order for Next Up, unwatched clears the progress.
- [x] **2.21b** Build the watched UI: a watched marker and a Mark watched/unwatched action on poster cards (grids, Home rows, Search), per-episode and per-season actions in the detail page's episode list and one for a movie, refreshing titles, the title, Home and search afterwards; MSW handlers with state, Vitest coverage and a server e2e step (SPEC §8 browsing [M], §11).
- [x] **2.22** Add the server side of the Settings shell. Contract first: `GET /server/mode` (`server.control`), and `GET`/`PUT /settings/metadata` (`settings.manage`) for the TMDB access-token override (stored encrypted with the per-install key, never returned; an empty value clears it; applies from the next TMDB request without a restart) (SPEC §8 metadata, §9, §11).
- [x] **2.22b** Build the Settings shell: Playback (MPV path: current path and its source, Choose and Use default through the desktop bridge; desktop-only note in a browser), Metadata (TMDB key source, set and clear), Server (mode and restart notice, needs `server.control`), and About and licences (version from `/health`, AGPL-3.0 and source link, a hand-written third-party list, TMDB attribution). Hide sections the user lacks the permission for (SPEC §11). Contract first: `GET /account` (`authenticated`) returns the account with its `permissions`; the desktop bridge reports the MPV in use and its source and resets it to the default.
- [x] **2.23** Write the manual MPV test checklist (SPEC §12). Split: this task is the checklist; the run is 2.23c.
- [x] **2.23b** Register the real ffprobe `IMediaProbe` outside the dev configuration (`MediaMan:Tools:FfprobePath`), so a fresh install that isn't the dev configuration can scan; today only `AddMediaManDev` registers it and `ScanCoordinator.IsAvailable` stays false otherwise. Then drop the dev variables from the manual test launch (SPEC §7.2, §10.1, §10.3).
- [x] **2.23c** Run the manual checklist on Windows against a real library, and fix or park what it finds (SPEC §12).
- [x] **2.23d** Never run a non-MPV executable picked as the player: Settings → Playback accepted `notepad.exe` and launched it alongside the error. Decide the pick is MPV without executing it (running `--version` would still launch an arbitrary program): e.g. the file name is `mpv`/`mpv.exe`/`mpv.com`, and on Windows its version resource names mpv; check before saving and before playing, and refuse it with an error that says what to do (finding 3 of the first Windows manual run; SPEC §5, §7.4).
- [x] **2.23e** Fix the two browse/playback logic findings of the first Windows manual run: refresh the browse queries when a scan finishes (Home stayed empty after the first scan until navigating away and back), and offer Up next on an episode that is already watched (a rewatch never got it) (findings 1–2; SPEC §7.4, §8).
- [x] **2.23f** OSC polish from the first Windows manual run: a dark gradient or panel behind the controls, previous/next episode buttons and skip back/forward buttons (findings 4–5; SPEC §11).
- [x] **2.23g** Keep one MPV window and surface server loss in it: Up next loads the next episode into the running MPV (`loadfile`) instead of closing and reopening it, and a lost server shows an OSD message in MPV saying what happened and what to do, not only in the app window (findings 6–7 of the first Windows manual run; SPEC §7.4, §11).

**Exit criteria:**
- On Windows, the Stage 2 part of the MVP guardrail works end to end against real TMDB. A fresh install runs the setup wizard, the Superadmin adds an external library, and it's matched with artwork. Browsing and search work, and a title plays in MPV through the desktop app, resumes, and offers the next episode.
- The owner uses MediaMan as their everyday way to watch their library and is comfortable with it, without hitting issues that send them back to another player or Plex.
- The safety-critical tests in scope (external libraries never written, per-route permissions, Superadmin protection) exist and pass in CI.

---

## Stage 2.5 — Early packaging (Windows)

**Goal:** someone other than the owner installs the Stage 2 app on Windows from one installer, with no
manual dependency installs and no environment variables, and keeps their data across every later update.
Pulled forward from Stage 8 so the app gets real use by others before the torrent stages. Torrent pieces,
ffmpeg (transcoding), Linux and auto-update stay in Stage 8.

- [x] **2.5.1** Publish the server as a self-contained single file for `win-x64` from a Release build (so no dev code ships, SPEC §8 setup [P]), with the jellyfin-ffprobe build alongside it. A packaged server finds its bundled ffprobe by default; the settings override still wins (SPEC §9 bundled tools, §10.1).
- [x] **2.5.2** Let the packaged desktop app find and start its bundled server and MPV without `MEDIAMAN_SERVER_PATH` or `MEDIAMAN_MPV_PATH`; the variables stay as dev overrides. Stop the server it started when the app quits. Add tests through `IServerProcess` and the existing fakes (SPEC §10.3).
- [x] **2.5.2b** Serve the built web UI (`web/dist`) from the server alongside the API, with the app's routes falling back to its main page, and have the packaged desktop load the UI from the server it started instead of the Vite dev server; `MEDIAMAN_UI_URL` stays the dev override. Only the static UI files are reachable signed out; every API endpoint keeps its permission. The publish script puts the built UI beside the server (SPEC §5 one UI everywhere, §10.1).
- [x] **2.5.3** Data survives upgrades: the server copies the database aside before applying pending migrations and says what happened if a migration fails, without starting on a half-migrated database. Add a fixture database for each released version and a test that migrates every fixture to the current schema with its data intact; each release adds its fixture. Add the upgrade guarantee to SPEC §9 (SPEC §9 data portability and resilience).
- [x] **2.5.3b** Make brand assets from the logo (`mediaman-logo.png` in the repo root, a 1408×768 TV with "MM" on a transparent background): move the source into a brand assets folder, crop it to a square master without the padding, and produce the sizes the app needs: a Windows `.ico` (16–256 px), the Electron window and taskbar icon, the web favicon and touch icon, and a wide version for the README. Check that the small sizes stay legible on the dark theme (SPEC §11) and write down how they were made so they can be regenerated.
- [x] **2.5.3c** Show the logo in the app: next to the "MediaMan" wordmark in the shell header, on the setup wizard and sign-in screens, and in Settings → About; set the favicon in `web/index.html` and the desktop window icon. Give it alt text and update the component tests (SPEC §11).
- [x] **2.5.4** Build a Windows installer with electron-builder (NSIS): the desktop app, the single-file server, ffprobe, MPV with the Lua OSC, and the third-party licence notices with source offers (SPEC §9 licensing). The installer, Start menu and desktop shortcuts and the installed app use the 2.5.3b icon. Unsigned for now. Installing over an older version keeps the data directory.
- [x] **2.5.5** Version the app and publish the installer to GitHub Releases from CI when a version tag is pushed (SPEC §12).
- [x] **2.5.6** Write a short Windows install guide (`docs/INSTALL-WINDOWS.md`: download, the SmartScreen warning on an unsigned build, first-run setup, where data lives, backup by copying files, how to update). Run the installer's scriptable checks once as a pre-flight (the unpacked layout, the bundled server, a server-level upgrade, and a silent install, upgrade and uninstall).
- [x] **2.5.6b** Dropped (2026-10-14): no scripted hands-on installer run; the installer is tested through normal use, and what that finds is fixed or parked. The guide wording, small-size icon and port-reuse items stay in Parked.
- [ ] **2.5.7** Remove the default menu bar (File, Edit, …): `Menu.setApplicationMenu(null)` on Windows and Linux; on macOS a minimal role-based menu (app with About and Quit, Edit with undo/cut/copy/paste/select all, Window) so Cmd+Q and copy/paste keep working. Build the template in a pure function in `window.ts` with a unit test (SPEC §11).
- [ ] **2.5.8** Redo the header layout so the logo has room and multiple libraries stop stacking vertically. Brand block: the wide TV (`assets/brand/mediaman-wide.png`, copied to `web/src/assets/logo-wide.png` through `scripts/brand-assets.mjs` `copies`; `Logo` gains a `wide` variant) at about 44–48 px tall beside the wordmark. Replace `ScanStatus` in the header with a "Libraries" dropdown: a disclosure button (`aria-expanded`) with a summary (scanning indicator; an alert when a last scan failed, still announced with `role="alert"`) opening a popover with each library's `ScanStateText` and `ScanLibraryButton`; Esc and outside clicks close it and focus returns to the button. Switch profile and Sign out move into a profile menu with the same pattern. Update the AppShell and ScanStatus tests and add dropdown tests with 1 and 3 libraries (SPEC §11).
- [ ] **2.5.9** Continue/Resume on a show's page. Contract first: optional `TitleDetail.showPlay` for shows, `{ playableId, episode: HomeEpisode, resumeSeconds? }`, the same pick as the Home hero (the episode the profile is part-way through, else its next episode, else the first episode of the first season, specials last). The server fills it in `LibraryQueries.GetTitleAsync` through `HomeRules.Watching` and `HomeRules.HeroPlay` for the session's profile (no profile: the first episode, no resume); tests for an unwatched show, a part-way episode, a finished one and an all-watched show. The show page's hero gets the large button ("Resume S2:E3 from 12:30", "Continue S2:E4", "Play S1:E1") with Start over, reusing `PlayButton` (SPEC §7.4, §11).
- [ ] **2.5.10** Organise Movies and Shows by genre, like Home. Contract first: an optional `genre` filter on `GET /titles`, and `GET /genres?kind=` (`library.view`) listing genres with title counts, most titles first; both in the permission table test and conformance. The pages open on genre rows (each the genre's top titles by rating, in Home's row component, with "See all") under a genre chip bar (All plus each genre); a picked genre shows the full `PosterGrid` filtered by `?genre=` in the URL, so Back works (SPEC §11).
- [ ] **2.5.11** Spike and ADR 0011: play inside the app window instead of a separate MPV window. Prove on Windows, behind a Debug-only flag, that MPV started with `--wid=<BrowserWindow.getNativeWindowHandle()>` draws inside the main window, and that a frameless, transparent, `skipTaskbar` child `BrowserWindow` (`parent: mainWindow`, kept on `getContentBounds()` through move, resize, maximize and fullscreen) sits above it and gets mouse and keyboard input, with minimize/restore and Alt-Tab showing one window and HDR and subtitles still rendering. A native child surface always covers its parent's HTML, hence the overlay window. Record the decision in ADR 0011: Windows and Linux keep MPV as a separate process embedded with `--wid` (Linux forces Electron onto X11 with the `ozone-platform` switch so XWayland covers Wayland); macOS, which can't host another process's view, loads libmpv in-process through a small N-API addon under the same overlay (Stage 8); libmpv isn't used everywhere because of per-platform native builds and an MPV crash taking down the app. Update SPEC §4 (drop the "Embedding MPV" non-goal), §7.4, §10.1, §10.3 and §11, the AGENTS.md hard rule (ffmpeg/ffprobe always separate processes; MPV separate on Windows and Linux, libmpv only through the macOS player addon) and `.claude/rules/desktop.md`, and add the macOS addon task to Stage 8. No production code ships from the spike; if a check fails, stop and decide before 2.5.12.
- [ ] **2.5.12** `IPlayer` gains state and commands (module interface, SPEC §10.3): `PlayOptions.embedIn` (the native window handle; `MpvPlayer` adds `--wid` and drops `--force-window`), `onState` (position, duration, paused, buffering, volume, muted, audio and subtitle tracks with the selected ones, chapters, title, episode neighbours; `time-pos` throttled to about 4 Hz) and `command` (toggle pause, seek to/by, set volume, toggle mute, select audio, select or turn off subtitles, previous/next episode). Progress, credits, Up next and notice semantics stay as they are. `FakePlayer` and the `playerContract` suite cover the new parts; overlay-only bridge channels `playerState` (main → overlay) and `playerCommand` (overlay → main, validated like `parsePlayOptions`), the preload and `web/src/lib/desktop.ts` types follow.
- [ ] **2.5.13** The in-app player OSD in React, replacing the Lua OSC's look: a `/player` route outside the shell rendering `PlayerOverlay` against a `PlayerController` interface (state in, commands out; the desktop one over the bridge) so Stage 5's browser player can reuse it. Top bar with Back and the title (S·E for episodes); a bottom gradient panel with the seek bar (chapter ticks, hover time, buffered range), previous episode, skip −10 s, play/pause, skip +30 s, next episode, time, volume button with a slider (click mutes, wheel adjusts), audio and subtitle menus, and fullscreen; controls and cursor hide after 3 s idle; the Up next card with a countdown and Play now/Cancel; notices (a lost server) as a toast. Keyboard: Space/K, ←/→ (10/30 s), ↑/↓ volume, M, F and double-click fullscreen, Esc (leave fullscreen, then Back), A and C menus, N/P episodes; every control focusable with a visible focus ring. Theme tokens and 150–250 ms motion only. Tests with a fake controller, including fake-timer auto-hide (SPEC §11).
- [ ] **2.5.14** Wire in-window playback end to end and retire the Lua OSC: an overlay-window controller in the main process (injectable window factory; created on play, kept on the main window's content bounds, fullscreen toggled on the main window, hidden with focus returned on every end reason; Back stops playback and leaves the title's page underneath); `PlayerHost` passes `embedIn` and routes Up next, notices and episode neighbours to the overlay through state. Delete `desktop/mpv/osc.lua` and the script-message plumbing (`upNextMessages`, `episodeMessages`, `--script`); keep `mpv.conf`; update packaging (`bundled.ts`, `electron-builder.json`, `package-win.mjs`, `packaging.test.ts`), SPEC §7.4 and §11, CODEMAP and, if anything visible changed, `docs/INSTALL-WINDOWS.md`. Change the exit criterion's "first playback … in MPV" to playback in the app window.

**Exit criteria:**
- A clean Windows machine goes from downloading the installer to first playback of a library title in MPV by following the guide, with no manual dependency installs or environment variables.
- Installing a newer build over an older one keeps the libraries, matches, watch progress and sign-in, and the migration fixture test passes in CI.

---

## Stage 3 — MVP: torrents and the VPN gate

**Goal:** a built-in torrent client that can only talk through the configured VPN path, with
downloads imported into managed libraries automatically. The engine choice is settled first, because
everything else in this stage sits on it.

- [ ] **3.1** Run the torrent engine test build (SPEC §10.1, §13): a throwaway harness, kept out of `npm test` and CI because it uses the real network, that checks MonoTorrent against the requirements. All traffic (peers, trackers, DHT, uTP) must stay on one bound interface or a SOCKS5 proxy, verified by watching the other adapters and by removing the bound one. It must also handle magnet links, hold hundreds of seeding torrents at acceptable CPU and memory, and get throughput comparable to qBittorrent. Record the results and the decision (MonoTorrent, or the qBittorrent-nox sidecar over its Web API) in `docs/adr/0004-torrent-engine.md`, and update SPEC §10.1 and §13 to match.
- [ ] **3.2** Add the torrent and VPN fakes to `MediaMan.Fakes` (SPEC §10.4): `FakeTorrentEngine` (an in-memory state machine with `Advance`/`Complete` that writes declared files into the temp dir and records pause/resume calls and network config), `FakeNetworkInterfaces` (settable adapters) and `FakePublicIpProbe` (settable IPs per route).
- [ ] **3.3** Implement the real `ITorrentEngine` chosen in 3.1 in `MediaMan.Torrents`: start with a `TorrentNetworkConfig` (interface binding or SOCKS5), add by magnet or .torrent, pause, resume, remove with or without data, per-file priority and skip, global and per-torrent speed limits, and state that survives a restart. Add integration tests against a local tracker and seeder on loopback, skipped when they can't run (SPEC §8 torrents [M], §10.4).
- [ ] **3.4** Implement the real `INetworkInterfaces` and `IPublicIpProbe` (leak-check endpoint configurable in settings; choose the default and close that SPEC §13 question), and the VPN gate service. The engine doesn't start without a VPN path or a Superadmin-recorded opt-out. It checks the interface every 5 s and runs the leak check every `leakCheckIntervalSeconds`. A missing interface or a leak pauses every torrent at once, writes an AuditEvent and raises an SSE event; a healthy path resumes them. Add kill-switch and leak tests on the fakes and `FakeClock` (SPEC §5, §7.8, §12).
- [ ] **3.5** Add the torrent and VPN settings API to the contract and server: VPN config (interface or SOCKS5, with credentials encrypted through `ISecretProtector` and never logged), the opt-out that only the Superadmin can record, VPN status over REST and SSE, global speed limits and seeding defaults (ratio 2.0 or 7 days, SPEC §13). Add conformance tests and permission-table rows (SPEC §7.8, §9).
- [ ] **3.6** Add the Downloads API to the contract and server: add by magnet or .torrent (`downloads.create`), list with progress, speed, peers, ETA and seeding status, pause, resume, remove with or without data, per-file priority and skip, and per-torrent limits (`downloads.manage`), with live updates over SSE. Add conformance tests and permission-table rows (SPEC §7.7, §8 torrents [M]).
- [ ] **3.7** Skip and flag dangerous files: on add, files with the SPEC §5 extensions are set to skip and marked `dangerous` on their DownloadFile, and are never opened, executed or imported. Test it with `FakeTorrentEngine` (SPEC §5, §8 torrents [M]).
- [ ] **3.8** Add Managed libraries: the Managed library kind with the download folder and the `Movies/Title (Year)/` and `Shows/Title (Year)/Season NN/` layout, set up through the library API and Settings → Libraries. Extend the read-only test so it proves imports never write to External roots (SPEC §5, §6.1, §8 libraries [M]).
- [ ] **3.9** Import on completion: probe and parse the files, link them to the target title if known (else match as in 7.2), and hardlink into the managed layout right away on the same volume, otherwise move once the seeding limits (driven by `IClock`) are met. Notify the UI over SSE. Test in temp directories with `FakeTorrentEngine` (SPEC §7.7).
- [ ] **3.10** Add "prioritize first episodes" for season packs: the first episodes' files and pieces download first (SPEC §7.7 step 1, §8 torrents [M]).
- [ ] **3.11** Build the Downloads page (active, seeding and completed lists, add by magnet or .torrent, and a remove that confirms and names what will be lost) and the VPN status badge that is always visible in the shell, with a persistent red banner while the gate is closed. Add component tests with MSW (SPEC §11).
- [ ] **3.12** Build Settings → Torrents and VPN (choose an interface or SOCKS5, run the leak check now, speed and seeding limits, and the Superadmin-only opt-out with a plain warning) and the optional VPN step in the setup wizard (SPEC §7.1, §11).

**Exit criteria:**
- On Windows with a real VPN, a magnet added on the Downloads page downloads through the VPN interface only, completes, is imported into a managed library, and plays.
- Disconnecting the VPN pauses every torrent within seconds and shows the red banner; reconnecting resumes them. A simulated leak does the same.
- Kill-switch, leak-check, dangerous-file and external-library tests pass in CI, all on fakes.

---

## Stage 4 — MVP: indexers and "Get"

**Goal:** find any title in the catalog, search torrent sites for it with quality chips, and download it in one click.

- [ ] **4.1** Add the indexer fakes to `MediaMan.Fakes` (SPEC §10.4): `FakeIndexerHttp` serving recorded HTML and JSON fixtures per URL (with Cloudflare-style 403/503, timeouts, and solved and unsolved FlareSolverr challenges) and `FakeDefinitionSource` returning fixture YAML from `tests/fixtures/definitions/`.
- [ ] **4.2** Build the Cardigann definition engine in `MediaMan.Indexers`, part 1: load YAML definitions, settings and login, and build search requests from a `SearchQuery` (keywords, categories, season and episode, and IMDb/TMDB IDs where the definition supports them). Test against fixture definitions (SPEC §8 indexers [M]).
- [ ] **4.3** Cardigann engine, part 2: parse HTML and JSON responses with the definition's selectors and filters into `ReleaseResult`s, including magnet or torrent URL, size, seeders, leechers and date. Test against recorded pages through `FakeIndexerHttp`.
- [ ] **4.4** Add indexer management to the contract and server: import definitions from a URL and update them (choose the default community URL and close that SPEC §13 question); add, configure, enable and disable indexers with credentials encrypted through `ISecretProtector`; and test an indexer to record its health (last success, last error). Requires `indexers.manage`. Add conformance tests and permission-table rows (SPEC §8 indexers [M]).
- [ ] **4.5** Add Torznab indexers (Jackett/Prowlarr) behind `IIndexer`, with fixture tests (SPEC §8 indexers [M]).
- [ ] **4.6** Add optional FlareSolverr per indexer behind `IIndexerHttp`, with fake-driven tests for solved and unsolved challenges (SPEC §8 indexers [M]).
- [ ] **4.7** Build Settings → Indexers (one-click definition import, the indexer list with health, Torznab and FlareSolverr setup) and the optional indexer import step in the setup wizard (SPEC §7.1, §11).
- [ ] **4.8** Add catalog search: a TMDB search API for titles not in the library, the "Not in library" section in search results, and a detail page for catalog-only titles. Show Get on catalog titles and on missing episodes of library titles (SPEC §8 browsing [M], §11).
- [ ] **4.9** Implement `IQualityClassifier`: resolution, dynamic range, codec, source, audio and language from `ParsedRelease`, and the bitrate tier from size ÷ runtime against the editable tier table. Add table-driven tests at the tier boundaries (SPEC §8 quality [M]).
- [ ] **4.10** Build the Get search service (a service, not UI code, so automation can reuse it, SPEC §4) and its API: search every enabled indexer in parallel with a 15 s timeout each, discard releases that don't match the title, season or episode, classify, rank by chip matches then seeders, flag dangerous releases, and report failed sources with reasons. Choosing a result with `downloads.create` starts a Download linked to the title. Add tests on the fakes, conformance tests and permission-table rows (SPEC §7.6).
- [ ] **4.11** Build the Get dialog: chips pre-filled from the last choice or a saved profile, live re-ranking as chips change, quality badges, size, seeders and indexer per result, red dangerous flags, an "n sources failed" notice, and a whole season or chosen episodes for shows. Add component tests with MSW (SPEC §7.6, §11).
- [ ] **4.12** Add saved quality profiles and the last-used chips per user, and Settings → Quality with the editable tier table (SPEC §8 quality [M], §11).
- [ ] **4.13** Add advanced raw search across indexers, with results that download the same way (SPEC §8 indexers [M]).

**Exit criteria:**
- A title not in the library is found through catalog search, Get returns ranked results from real indexers with the chips applied, and one click starts a VPN-gated download that is imported and plays.
- A failing indexer is listed as a failed source while the other results still show.
- All indexer and Get tests run on fixtures in CI, with no network.

---

## Stage 5 — MVP: browser playback

**Goal:** the same library plays in any modern browser, desktop or phone, at the best quality the client supports.

- [ ] **5.1** Implement the stream decision as a pure function: direct play, remux to HLS, or transcode only the unsupported streams, with HDR tone mapping when the client lacks HDR and bitrate reduction to the client's maximum. Add table-driven tests (SPEC §7.5 steps 1–2, §10.4).
- [ ] **5.2** Add `FakeTranscoder` and the ffmpeg `ITranscoder` in `MediaMan.Playback`: remux or transcode to HLS (fMP4) with software encoders, serve segments, restart at the seek point when a seek passes the generated segments, stop idle sessions (a transcode's lifetime is the only server-held playback session; progress stays keyed by Playable, PLAN 2.12b), and keep the transcode cache in a temp folder capped at 10 GB with the oldest evicted first (SPEC §7.5 step 4, §13). Add integration tests on tiny samples, skipped when ffmpeg is absent.
- [ ] **5.3** Detect hardware encoders (NVENC, QSV, VAAPI, AMF, VideoToolbox), prefer them, and fall back to software when a hardware encode fails. Show the choice in Settings → Playback (SPEC §8 playback [M]).
- [ ] **5.4** Add HDR → SDR tone mapping and bitrate limits for remote clients (a per-user maximum as well as the client's own) (SPEC §8 playback [M]).
- [ ] **5.5** Wire the browser path into `POST /playback/sessions`, replacing its `501 playback.player_unsupported`: the web app detects its capabilities (MediaSource and `canPlayType`) and gets a direct or HLS URL (`method: hls`). Contract additions, all additive: an optional `id` on `PlaybackSession` for HLS sessions and `DELETE /playback/sessions/{id}` to stop a transcode, with HLS playlist and segment routes that accept the session-signed URL (`sessionUrl`, ADR 0008) like the direct stream does, since `<video>` and native HLS can't send a bearer token. Progress goes through the same `PUT /playables/{playableId}/progress` as MPV, so resume, watched and Next Up are shared. Add conformance tests (SPEC §7.5).
- [ ] **5.6** Build the HTML5 player with hls.js and controls matching the OSC look: title, a progress bar with chapters, audio and subtitle menus, Resume/Start over, Up next, and keyboard controls. Add component tests (SPEC §11).
- [ ] **5.7** Add browser subtitles: ASS/SSA rendered client-side with JASSUB, text subtitles as WebVTT (the external `.srt` sidecars `SubtitleTrack` lists today are served as-is, so add a WebVTT conversion or rendition for the browser), embedded text subtitles extracted, and PGS burned in only when selected (SPEC §7.5 step 3, §8 playback [M]).
- [ ] **5.8** Route Play by client: the desktop app plays in MPV and a browser plays in the HTML5 player. Add a Playwright test for browse → detail → Play in the browser against fakes.

**Exit criteria:**
- On the LAN, desktop Chrome and Firefox and a phone browser play the owner's library: compatible files direct play, incompatible containers remux, incompatible codecs and HDR transcode with tone mapping, and seeking works.
- Styled subtitles render, and resume, watched and Next Up are shared between the browser and MPV.

---

## Stage 6 — MVP: remote access

**Goal:** reach the server safely from outside the LAN, from the desktop app and from a phone browser.
This stage completes the MVP.

- [ ] **6.1** Add the remote access switch, trusted proxies and HTTPS enforcement. The switch is a Setting only the Superadmin can turn on; while it's off, the 2.4 local-network guard stays in force. While it's on, honour `X-Forwarded-Proto` and `X-Forwarded-For` only from configured trusted proxies, and refuse plain HTTP and direct (unproxied) requests from non-LAN clients with an error that says how to fix it. Add the API for the switch, the trusted proxies and the outside check (one-time code confirmed by a request through the trusted proxy over HTTPS). Add tests (SPEC §7.12, §8 remote access [M], §9, §12).
- [ ] **6.2** Add sign-in rate limiting and lockout per account and per IP in every mode, and fail2ban-style IP bans while remote access is on: the `IpBan` entity, escalating bans for repeated lockouts and bursts of invalid tokens or failed requests, bans persisted across restarts, and the ban list and unban API. Bans use the real client address from 6.1's trusted proxies and never hit a trusted proxy or a local-network address. All timing reads `IClock`; a Superadmin lockout is always temporary. Add tests, including the safety-critical one (SPEC §5, §7.11 steps 2 and 5, §12).
- [ ] **6.3** Add the session list and revocation (API and Settings UI): users see and revoke their own sessions (SPEC §7.11 step 4).
- [ ] **6.4** Let the desktop app connect to a remote server by URL: choose Local or Remote, check `/health`, sign in, and keep per-server tokens. MPV plays the remote stream (SPEC §8 remote access [M]).
- [ ] **6.5** Make the web UI work on phone screens: the shell, Home, detail, player, Get and Downloads at phone widths with touch-sized targets. Add Playwright runs at a mobile viewport (SPEC §3 goal 5).
- [ ] **6.6** Build Settings → Remote access: the switch with its guided setup (Tailscale needs no switch and is recommended; Cloudflare Tunnel and Caddy configure the trusted proxies for you), the outside check, the ban list with unban, and the "Remote access on" badge in the shell. Add a Playwright test for the guided setup (SPEC §7.12, §8 remote access [M], §11).
- [ ] **6.7** Test downloads, the VPN gate, Get, browser playback and remote access by hand on Windows, and fix or park what it finds.

**Exit criteria (MVP complete):**
- Through one of the guided setups, over HTTPS from outside the LAN, a phone browser signs in and plays a title, and the desktop app connected to the remote server plays it in MPV.
- With remote access off, clients outside the local network are refused, even through a tunnel on the same machine.
- Plain HTTP from outside the LAN is refused, and repeated failed sign-ins are locked out without permanently locking out the Superadmin; with remote access on, repeat offenders are IP-banned and can be unbanned in Settings.
- The owner uses MediaMan daily for both watching and getting new titles, without going back to Plex, the *arr tools or qBittorrent.

---

## Stage 7 — Refinement (driven by real use)

**Goal:** fix what real use revealed. Priorities come from the friction log and Parked items,
not from this list. Candidates:

- Measure against the SPEC §9 performance and footprint targets (5,000-file scan, rescan, API p95, first paint) and fix the gaps.
- Matching accuracy against the owner's real library; tune the 0.85 threshold (SPEC §13).
- Torrent engine load with hundreds of seeding torrents, and transcode CPU use on the owner's hardware.
- Accessibility pass: keyboard navigation, focus states, WCAG AA contrast (SPEC §9).
- Linux and macOS (best-effort) manual test runs.

**Exit criteria:** defined at the start of the stage.

---

## Stage 8 — Packaging

**Goal:** easy for someone who isn't the author to install and run, on every supported platform and with
every MVP feature. The Windows installer, release pipeline and upgrade-safe data come from Stage 2.5; this
stage completes them.

- [ ] **8.1** Extend the single-file server publish to linux-x64 and linux-arm64, add jellyfin-ffmpeg per platform next to ffprobe, plus the qBittorrent-nox sidecar if ADR 0004 chose it (SPEC §9 portability, §10.1).
- [ ] **8.2** Add ffmpeg, any torrent sidecar and their licence notices to the Windows installer from 2.5.4, and sign it (SPEC §9 licensing).
- [ ] **8.3** Build a Linux AppImage with the same contents, and smoke-test it on a clean Linux machine.
- [ ] **8.4** Write the README install and first-run guide, growing the Stage 2.5 Windows guide with VPN setup and Linux (SPEC §9 data portability).
- [ ] **8.5** Auto-update in the desktop app from GitHub Releases (electron-updater), keeping the upgrade guarantee from 2.5.3.

**Exit criteria:** a fresh Windows machine goes from download to first playback of a library title, and to a first download through a configured VPN, by following the README with no manual dependency installs. The Linux AppImage does the same on a clean Linux machine.

---

## Later / future (not planned yet)

Each group becomes its own stage once the MVP ships.

- **Accounts and roles:** multiple users, invites, Admin/User presets and custom roles, a configurable default role, TOTP, profile PINs and maximum ratings, the audit log UI, and download requests with the approval queue (SPEC §7.9, §7.10, §7.11).
- **Metadata extras:** AniList matching for anime, fanart.tv logos and backdrops, and OpenSubtitles.
- **Docker:** an image with documented volumes and a gluetun recipe.
- **Automation (parked):** follow shows, watchlists, release upgrades, and stream-while-downloading.

---

## Working agreements

- **One stage at a time.** Finish the exit criteria before starting the next stage.
- **One task per session, one task per commit.**
- **Keep docs true.** If implementation changes a decision, update SPEC.md/PLAN.md or add an ADR in the same change.
- **Definition of done:** code, tests, docs updated if needed, reviewer run, task ticked.

---

## Parked

Ideas and tangents captured mid-work (via `/devflow:park`). Not scheduled. Review them at the start of each stage.
- [ ] Decide the Verify fee status (exemption renewed yearly, sponsorship, pin v32, or another snapshot library) before the first snapshot test; Verify v33+ fails the build without one, see ADR 0001 (parked 2026-10-04; suggested stage: 1)
- [ ] SSE reconnect: honour `Last-Event-ID` on `GET /events` (keep a short replay buffer in `EventBroadcaster`) so clients don't miss events across reconnects (parked 2026-10-05; suggested stage: 1, with 1.9b scan progress)
- [ ] End-to-end test that an unhandled exception returns a contract-valid 500 `Problem` without leaking exception details (parked 2026-10-05; suggested stage: 1)
- [x] CI drift check for generated web code: run `npm run generate -w web` and fail on `git diff --exit-code web/src/api web/src/routeTree.gen.ts` (parked 2026-10-05; suggested stage: 0, with 0.7 CI)
- [x] CI must run `npx playwright install --with-deps chromium` before `npm test` (web e2e smoke) (parked 2026-10-05; suggested stage: 0, with 0.7 CI)
- [ ] Parser corpus misses (baseline 0.986, PLAN 1.4): unpadded scene numbering (`Show.Name.102`), `Episode 1000` absolute numbers outside season folders, uploader tags as groups (`-[YTS.MX]`), `Blade Runner 2049` with no release year, `Complete Series` packs, and number-ending fansub titles (`[Group] Mob Psycho 100`). Fix and raise the baseline (parked 2026-10-06; suggested stage: 7, with matching accuracy)
- [ ] `ParsedRelease` has no SD resolution (480p/576p parse as null) or audio channel count; add them if the quality chips or `IQualityClassifier` need them (parked 2026-10-06; suggested stage: 4, with 4.9)
- [ ] HDR10+ detection in `FfprobeMediaProbe`: it needs per-frame side data (`-show_frames -read_intervals %+#1`), so HDR10+ files probe as `HDR10` for now (parked 2026-10-06; suggested stage: 5, with tone mapping decisions)
- [ ] Per-file scan progress: `scan.progress` only reports phase changes; add processed/total counts within a phase (an `IProgress` from `LibraryScanner`/`LibraryMatcher`) so long first scans show movement (parked 2026-10-06; suggested stage: 2, with 2.8 scans)
- [ ] Movies/Shows grid filters (genre, year, watched, resolution) and sort options (SPEC §11): `GET /titles` needs the query parameters first (parked 2026-10-06; suggested stage: 2, with the browsing polish)
- [ ] UI visual polish (SPEC §11): tasteful modern gradients, revised fonts, a more stylized logo that pops, and a richer navbar than the current plain one; also revisit the Play button's colour, placement and per-episode size (parked 2026-10-07; suggested stage: 2, with the browsing polish)
- [ ] Show runtimes whenever possible: when TMDB sends no `episode_run_time` for a show (e.g. Severance, Twisted Metal, Westworld), derive it from the episodes' own runtimes in season details (e.g. the median), and fall back to the probed file duration for movies and episodes when the provider has none (parked 2026-10-07; suggested stage: 2, with the browsing polish)
- [ ] Prune the artwork cache: delete `<DataDirectory>/cache/artwork` files whose Artwork row is gone (titles rematched or removed), e.g. after each scan's artwork phase (parked 2026-10-07; suggested stage: 2, with 2.9 Fix match)
- [ ] Cache smaller TMDB images: `TmdbOptions.ImageBaseUrl` uses `original` (backdrops can be 4K and several MB); fetch `w780` posters, `w1280` backdrops and `w300` stills, or resize on cache (parked 2026-10-07; suggested stage: 2, with 2.19 Home)
- [ ] Clean up orphaned Titles: a deleted library or removed root leaves its Titles, Seasons, Playables and Artwork rows (hidden, since browsing needs a matched file); prune those with no FileLinks and no WatchState after a grace period, alongside the artwork cache prune (parked 2026-10-09; suggested stage: 2, with 2.9 Fix match)
- [ ] Expose the session's permissions to the web (e.g. on the profile list or a `GET /auth/session`), so the UI can hide actions it can't take, like deleting a library without `server.control` or the Needs attention inbox and Fix match without `library.manage`; today the server's `403` explains them (parked 2026-10-09; suggested stage: 2, before Settings → Users)
- [ ] Path-scoped incremental scans: a watcher change rescans the whole library (only changed files are probed, but every root is walked); pass the changed folders to `LibraryScanner` so large or networked libraries walk only those, and limit missing-file detection to them (parked 2026-10-09; suggested stage: 7, with scan performance)
- [ ] "Ignore this file" in Needs attention (status `Ignored`, kept across rescans) and a way to forget a Fix match override (`DELETE /media-files/{id}/match`) (parked 2026-10-09; suggested stage: 2, with 2.10)
- [ ] Clean up Titles, Seasons and Playables left with no files after a relink or purge; browse hides them, but the rows stay (parked 2026-10-09; suggested stage: 2)
- [ ] Sidecar subtitles in a `Subs/` or `Subtitles/` folder next to the video (common in release packs, often `Subs/<n>_English.srt`); `SidecarSubtitles.Find` only looks beside the file today (parked 2026-10-10; suggested stage: 7)
- [ ] Multi-episode files: Up next from E01 of a `S01E01E02` file offers E02, which plays the same file from the start; seek to the part's start or skip to the episode after (parked 2026-10-10; suggested stage: 2, with 2.12b/2.16)
- [ ] Credits and intro detection on the server, for files without named chapters: compare the audio across a season's episodes (chromaprint through ffmpeg) plus `blackdetect`/scene changes, treat footage after the credits as a post-credits scene, store markers per MediaFile and send them in `PlaybackSession`; Up next then prefers them over chapters (same `upNextAt` rule), and "Skip intro" comes from the same markers. Needs a data-model and contract change and an ADR (which ffmpeg build, chromaprint optional) (parked 2026-10-10; suggested stage: 7, as the first Refinement item once the MVP stages 2–6 ship)
- [ ] Up next reuses the open MPV window (`loadfile` over IPC) instead of relaunching MPV for the next episode, avoiding the window flicker (parked 2026-10-10; suggested stage: 7)
- [ ] Package `desktop/mpv/` (`mpv.conf`, `osc.lua`) outside the asar (e.g. `asarUnpack` or `extraResources`): MPV reads them from disk, and `mpvAssetsIn(app.getAppPath())` points inside the asar once packaged (parked 2026-10-10; suggested stage: 8)
- [ ] Episode progress bars on the detail page (SPEC §11 title detail): needs the recorded position and duration per Playable in `TitleDetail`, beyond 2.16b's `resumeSeconds`/`watched` (parked 2026-10-10; suggested stage: 2, with the Home rows in 2.18/2.19)
- [ ] Pick subtitles across embedded and external tracks together: a selected sidecar (`isDefault`) wins over an embedded track that matches a higher subtitle preference; needs the track list from MPV (`track-list`) after load (parked 2026-10-11; suggested stage: 3)
- [ ] Run `npm run test:perf` (search p95 on 20,000 files) in the release checklist or a dedicated CI job; it is Explicit and left out of `npm test` because wall-clock timing there measures CPU contention (parked 2026-10-12; suggested stage: 2.5, with 2.5.5 releases)
- [ ] Check a new TMDB access token against TMDB before saving it in Settings → Metadata (needs a validation call through `IMetadataProvider`) (parked 2026-10-12; suggested stage: 2 or later)
- [ ] Ship a generated `THIRD-PARTY-NOTICES` file with every distribution (npm and NuGet licences gathered at build time, e.g. `rollup-plugin-license`/`license-checker` and `nuget-license`), linked from About and licences; the in-app list stays hand-written (parked 2026-10-12; suggested stage: 8, packaging)
- [ ] Library folder picker: a Browse button (desktop folder dialog through the bridge) wherever a media folder path is entered (setup wizard, add/edit library) instead of typing the path by hand (parked 2026-10-12; suggested stage: 2)
- [ ] Hide the default Electron File/Edit/View/Window menu bar in the desktop app (parked 2026-10-12; suggested stage: 2)
- [ ] Make the top navigation bar sticky so it stays visible while scrolling (parked 2026-10-12; suggested stage: 2)
- [ ] Packaged desktop: when another MediaMan server already answers on the port (`ServerProcess` reuses it) and serves no UI, the window shows a 404 at `/app/`; detect it and say what to do (free the port or set MEDIAMAN_SERVER_PORT) (parked 2026-10-14; suggested stage: 2.5, with 2.5.6)
- [ ] Screen-only icon crop at 16 and 24 px (the screen and "MM" without the cabinet) if the full TV in `assets/brand/` doesn't read well in the taskbar, tabs or Start menu; "MM" is legible from 24–32 px up on the full TV, and at 24 px on the screen crop (parked 2026-10-14; suggested stage: 2.5, after 2.5.6 install testing)
- [ ] macOS app icon (`.icns` with 512 and 1024 px): the logo source is only 617 px wide, so it needs a larger or vector source first (parked 2026-10-14; suggested stage: 8, with macOS packaging)
- [ ] AGPL §13 source offer in the app: a "Source code" link (https://github.com/ogredforman/MediaMan) and the third-party notices in Settings → About, so users of a networked server can get the source (parked 2026-10-14; suggested stage: 2.5, with 2.5.6, or 8)
- [ ] Harden the packaged Electron app with `@electron/fuses` (turn off `RunAsNode`, so an `ELECTRON_RUN_AS_NODE` set by some terminals can't start MediaMan.exe as plain Node, and `EnableNodeCliInspectArguments`; turn on asar integrity) (parked 2026-10-14; suggested stage: 8, with signing in 8.2)
- [ ] Downgrade guard: refuse to start, saying what to do, when the database holds migrations this build doesn't know (an older MediaMan opened on data a newer one upgraded), instead of running on a schema it doesn't understand; point at the pre-upgrade copies in `backups/` (parked 2026-10-14; suggested stage: 2.5, with 2.5.5 versioning, or 8 with auto-update)
- [ ] Uninstall leaves `%LOCALAPPDATA%\@mediamandesktop-updater\installer.exe` (~190 MB, electron-builder's copy for differential updates, named after the npm package `@mediaman/desktop`): give it a MediaMan name and remove it on uninstall, or keep it on purpose for auto-update (found by the 2.5.6 installer pre-flight) (parked 2026-10-14; suggested stage: 8, with 8.5)
- [ ] The server writes outside its data directory: ASP.NET Core Data Protection keeps keys in `%LOCALAPPDATA%\ASP.NET\DataProtection-Keys`, shared with every ASP.NET Core app on the account. Keep them in the data directory, or check nothing uses Data Protection and keep it out of the user profile (SPEC §9 data portability; pre-flight finding 2) (parked 2026-10-14; suggested stage: 2.5, or 8)
- [ ] Write server logs to the data directory (rolling, a few files), so a packaged install has a log to report problems with: today the server's output only reaches the desktop app's process, though the database upgrade errors say to report the problem with the server log. Lower EF Core command logging to Warning in Release while there (pre-flight finding 3) (parked 2026-10-14; suggested stage: 2.5, or 8)
