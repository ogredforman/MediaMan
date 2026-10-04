# MediaMan — Implementation Plan

> Companion to [`SPEC.md`](./SPEC.md). The spec defines *what* and *why*; this plan defines *order*,
> *scope per stage*, and *exit criteria*. Each stage ends in a runnable, usable state. Don't pull
> later-stage work forward.

**Current stage: Stage 0 — Foundations**

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
  permission, and the Superadmin can't be locked out. Later: torrents never leak outside the VPN path,
  and downloaded files are never executed.
- Platforms: Windows and Linux fully supported, macOS best-effort. CI runs on Windows and Linux.
- MVP guardrail (scope): on Windows, the user adds an external library, MediaMan matches it against
  TMDB with artwork, and the Superadmin signs in and browses a Netflix-style UI. A title then plays in
  MPV through the desktop app, resumes, and offers the next episode. Torrenting, indexers, multiple
  users, browser playback and transcoding come after the MVP.
- Licence: AGPL-3.0. The repo stays private until it's "finished".

---

## Stage 0 — Foundations

**Goal:** an empty but correctly wired app that builds, runs, and tests on every target platform.

- [x] **0.1** Initialize the git repo, AGPL-3.0 `LICENSE`, `.gitignore`, `.editorconfig` and the repo layout (SPEC §10.2). Create `server/MediaMan.sln` with every `src/` project, one test project per module (e.g. `tests/MediaMan.Library.Tests`) (xUnit, AwesomeAssertions; see ADR 0001), and `tests/MediaMan.Fakes`, with `server/Directory.Build.props` turning on nullable reference types and warnings as errors. Add a root `package.json` with npm workspaces and the commands AGENTS.md will list: `npm run build` (all parts), `npm test` (all tests), `npm run lint` (ESLint + `dotnet format --verify-no-changes`). Server-only equivalents are `dotnet build server/MediaMan.sln` and `dotnet test server/MediaMan.sln`. Web and desktop join these scripts in 0.5 and 0.6.
- [x] **0.2** Add the domain types (SPEC §6.1) and declare every module interface from SPEC §10.3 in `MediaMan.Core`: `IClock` with a `SystemClock`, `ParsedRelease`, `ProbeResult`, `TitleCandidate` and the rest, declarations only. Add unit tests for any value-type logic (e.g. `ExternalId`, `InfoHash` parsing).
- [ ] **0.3** Add the `MediaMan.Data` EF Core DbContext for the full SPEC §6.1 schema (the frozen Phase 0 contract), with SQLite in WAL mode and the initial migration. Test that migrations apply to a temp database and that WAL is on (SPEC §9 resilience).
- [ ] **0.4** Create `contracts/openapi.yaml` (OpenAPI 3.1) with shared conventions: the error model, bearer auth scheme, pagination, and the SSE event envelope. Add `GET /health`. Wire the `MediaMan.Server` host (DI, config, SSE endpoint skeleton), and add a conformance-test harness that validates responses against the contract (SPEC §10.1, §12).
- [ ] **0.5** Scaffold `web/`: React + Vite + TypeScript (`strict: true`), Tailwind with the theme tokens from SPEC §11, TanStack Router/Query, an API client generated from `contracts/openapi.yaml`, MSW handlers generated from the contract, Vitest + Testing Library, a Playwright smoke test, and ESLint. Join the root build/test/lint scripts: add `web` to the npm workspaces and ESLint to `npm run lint`.
- [ ] **0.6** Scaffold `desktop/`: an Electron TypeScript app (`strict: true`; `src/main`, `src/preload`) that loads the web UI, plus the `IPlayer` and `IServerProcess` declarations (SPEC §10.3), `mpv/` placeholders, Vitest and ESLint. Join the root build/test/lint scripts.
- [ ] **0.7** Add GitHub Actions CI that runs `npm run build`, `npm run lint` and `npm test` on Windows and Linux (SPEC §12). Start `docs/CODEMAP.md` from the devflow template, describing the projects, workspaces and wiring so far.

**Exit criteria:**
- `npm run build`, `npm run lint` and `npm test` pass locally on Windows and in CI on Windows and Linux.
- The server starts and `GET /health` returns healthy, and its response passes the OpenAPI conformance test.
- The web app renders an empty themed shell against MSW. The Electron app opens a window showing it.

---

## Stage 1 — Prototype (fakes, no real external dependencies)

**Goal:** the complete core loop working end to end, with every external dependency replaced by a
deterministic fake. Validates the data model, architecture, and UI before real integrations add cost
and unpredictability.

- [ ] **1.1** Build the prototype-scope fakes in `MediaMan.Fakes` (SPEC §10.4): `FakeClock`, `FakeMetadataProvider` with about 30 JSON fixture titles in `tests/fixtures/metadata/` (remakes, same-name shows, anime with absolute numbering, a show with specials), error injection and query recording, `FakeArtworkProvider`, `FakeMediaProbe` and `FakeFileWatcher`. Torrent, indexer and desktop fakes come with their workstreams.
- [ ] **1.2** Add the dev configuration: the server starts with a SQLite path and one media path from config, with no wizard. It registers that folder as an External library and picks fake or real providers per dependency from config (SPEC §8 setup [P]).
- [ ] **1.3** Implement `ReleaseParser` for movies and episodes: title, year, season, episode(s), multi-episode ranges, and parent-folder context (SPEC §7.2 step 3, §10.3). Start `tests/corpus/release-names.json` with a corpus-driven test.
- [ ] **1.4** Extend `ReleaseParser` with resolution, source, codec, HDR/Dolby Vision, audio, group and anime absolute numbering. Grow the corpus to ≥300 real-world names including junk, and record the accuracy baseline the test enforces (SPEC §8, §12).
- [ ] **1.5** Implement the real ffprobe `IMediaProbe` in `MediaMan.Playback` (a process wrapper that parses JSON for container, streams, duration and HDR format). Add integration tests on tiny generated samples, skipped when ffprobe is absent (SPEC §8 [P], §10.4).
- [ ] **1.6** Build the scanner in `MediaMan.Library`. It enumerates roots recursively, ignores samples, extras folders and non-video files, and detects new, changed and missing files against stored MediaFiles. It probes new and changed files, marking unreadable ones `NeedsAttention`, and marks missing files `Missing`. Test incremental rescans in per-test temp directories (SPEC §7.2 steps 1–2 and 6).
- [ ] **1.7** Build the matcher. It queries `IMetadataProvider`, scores candidates on title similarity, year, kind and episode existence, and links at ≥0.85 confidence. Below that it creates a MatchReview with the top candidates. Provider errors leave files `Pending` with backoff. Add fixture tests for remakes, same-name shows, off-by-one years, specials and the low-confidence path (SPEC §7.2 steps 4–5 and 7, §12).
- [ ] **1.8** Import matched metadata: persist Title, Season and Episode data, plus artwork refs (poster, backdrop, stills) from the provider. Create FileLinks, including one per episode for multi-episode files (SPEC §6, §8 metadata [P], §8 multi-episode [P]).
- [ ] **1.9** Add the library API to the contract and server: list libraries, trigger a scan, browse titles (movies/shows), title detail with seasons, episodes and versions, and scan progress over SSE. Add conformance tests (SPEC §7.2, §10.1).
- [ ] **1.10** Build the web app shell (navigation and theme) and the Movies/Shows poster grid, with live scan progress (SPEC §11). Add component tests with MSW.
- [ ] **1.11** Build the web title detail page: backdrop hero, metadata, season tabs, an episode list with stills, and the versions list (SPEC §8 browsing [P], §11). Add component tests with MSW.
- [ ] **1.12** Add the dev-only play endpoint, which launches MPV on the server host for a Playable's file. It's registered only in the dev configuration and returns 404 otherwise (SPEC §8 playback [P], §13). Wire the Play button. Add a Playwright test for browse → detail → Play against the server running on fakes.

**Exit criteria:**
- With the dev configuration pointing at a folder of placeholder files, the server scans it, matches it against `FakeMetadataProvider`, and shows movies and shows (including multi-episode files and a MatchReview) in the web UI.
- Pressing Play on the detail page launches MPV on the local file.
- The parser corpus (≥300 names) passes at the recorded baseline, and all tests pass in CI on Windows and Linux.

---

## Stage 2 — MVP (real integrations, actually usable)

**Goal:** swap fakes for real integrations, with just enough polish that the owner uses it for real.

- [ ] **2.1** Implement the real TMDB `IMetadataProvider` with the shipped key (overridable in settings), a 7-day response cache and rate-limit backoff. Test it against recorded HTTP responses through a stub handler, with no network (SPEC §8 metadata [M]).
- [ ] **2.2** Cache artwork locally, extract each image's dominant color, and serve cached images from an artwork endpoint. Tint the detail hero from the dominant color (SPEC §8 metadata [M], §11).
- [ ] **2.3** Add `MediaMan.Auth`: the Superadmin account with Argon2id hashing, JWT access tokens (15 min) and rotating refresh tokens (30 days, sliding) signed with a per-install key. Add the permission list (SPEC §8) and per-endpoint permission metadata, a table-driven test that every route enforces its permission, and tests that the Superadmin can't be demoted, deleted or locked out (SPEC §5, §7.11, §12).
- [ ] **2.4** Add the first-run setup API: `423 SetupRequired` until setup is done, create the Superadmin (password ≥12 characters), choose the mode, and add the first library with path validation. Local mode binds to `127.0.0.1` and Server mode to the LAN. Changing the mode later needs `server.control` (SPEC §7.1, §8 setup [M]).
- [ ] **2.5** Build the web setup wizard (one step per screen with a progress indicator) and sign-in with silent token refresh. Add a Playwright test for the setup flow (SPEC §7.1, §11, §12).
- [ ] **2.6** Add Profiles (a default profile on account creation, plus create/edit/delete) and the profile picker shown after sign-in. WatchState is scoped to the profile (SPEC §6.1, §7.11 step 3, §8 accounts [M]).
- [ ] **2.7** Support multiple libraries, each with several root paths and a content type. Add the library management API and the Settings → Libraries UI (SPEC §8 libraries [M]).
- [ ] **2.8** Implement the real `IFileWatcher` for incremental scans and the periodic full scan (default 6 h, driven by `IClock`). Make interrupted scans resume. Add the test that scans a read-only temp folder and proves external libraries are never written (SPEC §5, §7.2 step 8, §9, §12).
- [ ] **2.9** Add the "Needs attention" and "Fix match" API: list MatchReviews, run a manual catalog search, relink a file (with season and episode mapping for shows), and persist the choice as a path override that rescans keep (SPEC §7.3).
- [ ] **2.10** Build the "Needs attention" inbox (cards with the file name, parsed guess and candidate posters) and the "Fix match" dialog on any title (SPEC §7.3, §11).
- [ ] **2.11** Support several versions per Playable: the API exposes them and the detail page gets a version picker that remembers the profile's preferred version (SPEC §8 parsing [M], §11).
- [ ] **2.12** Add `POST /playback/sessions` for the MPV client capabilities. It picks the version and returns a direct-stream URL (with HTTP range support), the resume position and external subtitle URLs. Add the progress endpoint that updates WatchState: resume is offered after 2 minutes, a Playable is watched at ≥90%, and the next episode is returned (SPEC §7.4 steps 1–2 and 4–5, §8 playback [M]).
- [ ] **2.13** Implement the real `IServerProcess` in Electron (Local mode spawns the server and waits for `/health`) and `FakeServerProcess`. Store tokens securely so the desktop app stays signed in (SPEC §8 accounts [M], §10.3, §10.4).
- [ ] **2.14** Implement the real MPV `IPlayer` over JSON IPC and `FakePlayer`. Play launches MPV with the stream URL, start position and subtitles, and reports progress every 10 s and on pause, seek and quit. Show "Open MPV settings" errors when MPV fails, and re-query the stream URL once on failure. Remove the dev-only play endpoint (SPEC §7.4 steps 3–4 and 6, §13).
- [ ] **2.15** Write the purple Lua OSC and `mpv.conf`: title, a progress bar with chapters, and audio and subtitle menus (SPEC §8 playback [M], §11).
- [ ] **2.16** Add "Up next": a cancellable 10 s countdown in the OSC that starts the next episode through the desktop app, and the Resume/Start over choice on the detail page (SPEC §7.4 step 5).
- [ ] **2.17** Add per-profile audio and subtitle language preferences, edited in profile settings and applied when launching MPV (SPEC §8 playback [M]).
- [ ] **2.18** Add the Home API: the hero pick plus Continue Watching, Next Up, Recently Added and genre rows for the current profile (SPEC §8 browsing [M]).
- [ ] **2.19** Build the Home screen: a hero banner (backdrop, title, overview, Play and More info) tinted from the dominant color, and horizontally scrolling, keyboard-navigable rows (SPEC §11).
- [ ] **2.20** Add instant library search (an API endpoint plus a search-as-you-type UI) with results in under 100 ms (SPEC §8 browsing [M], §5).
- [ ] **2.21** Add watched markers and mark watched/unwatched for movies, episodes and whole seasons, on cards and in the episode list (SPEC §8 browsing [M]).
- [ ] **2.22** Build the Settings shell: Playback (MPV path), Metadata (TMDB key override), Server (mode, needs `server.control`), and About and licences (SPEC §11).
- [ ] **2.23** Write `docs/SMOKE.md`, the manual MPV smoke checklist (SPEC §12). Run it on Windows against a real library, and fix or park what it finds.

**Exit criteria:**
- On Windows, the brief's scope guardrail works end to end against real TMDB. A fresh install runs the setup wizard, the Superadmin adds an external library, and it's matched with artwork. Browsing and search work, and a title plays in MPV through the desktop app, resumes, and offers the next episode.
- The owner uses MediaMan as their everyday way to watch their library and is comfortable with it, without hitting issues that send them back to another player or Plex.
- The safety-critical tests in scope (external libraries never written, per-route permissions, Superadmin protection) exist and pass in CI.

---

## Stage 3 — Refinement (driven by real use)

**Goal:** fix what real use revealed. Priorities come from the friction log and Parked items,
not from this list. Candidates:

- Measure against the SPEC §9 performance and footprint targets (5,000-file scan, rescan, API p95, first paint) and fix the gaps.
- Matching accuracy against the owner's real library; tune the 0.85 threshold (SPEC §13).
- Accessibility pass: keyboard navigation, focus states, WCAG AA contrast (SPEC §9).
- Linux and macOS (best-effort) smoke runs of `docs/SMOKE.md`.

**Exit criteria:** defined at the start of the stage.

---

## Stage 4 — Packaging

**Goal:** easy for someone who isn't the author to install and run.

- [ ] **4.1** Publish the server as a single file for win-x64, linux-x64 and linux-arm64, with jellyfin-ffmpeg/ffprobe per platform (SPEC §9 portability, §10.1).
- [ ] **4.2** Build a Windows installer (electron-builder NSIS) that bundles the desktop app, the single-file server, ffmpeg/ffprobe, MPV and the third-party licence notices (SPEC §9 licensing).
- [ ] **4.3** Build a Linux AppImage with the same contents, and smoke-test it on a clean Linux machine.
- [ ] **4.4** Write the README install and first-run guide, including where data lives and how to back it up by copying files (SPEC §9 data portability).

**Exit criteria:** a fresh Windows machine goes from download to first playback of a library title by following the README, with no manual dependency installs. The Linux AppImage does the same on a clean Linux machine.

---

## Later / future (not planned yet)

Each group becomes its own stage once the MVP ships.

- **Torrents and VPN:** the MonoTorrent Phase 0 test build first (VPN binding of all traffic including DHT and trackers, hundreds of seeding torrents, magnets, throughput; qBittorrent-nox sidecar fallback, SPEC §13). Then `ITorrentEngine` and its fakes, the VPN gate with kill switch and leak check (SPEC §7.8), the Downloads page, dangerous-file skipping, "prioritize first episodes", import on completion with managed libraries and the hardlink/move layout (SPEC §7.7), and seeding limits.
- **Indexers and Get:** the Cardigann YAML engine with definition import and updates, Torznab, optional FlareSolverr, indexer health, advanced raw search, TMDB catalog search, the "Get" flow with quality chips and bitrate tiers, saved quality profiles, and download requests with the approval queue (SPEC §7.6, §7.9).
- **Accounts and roles:** multiple users, invites, Admin/User presets and custom roles, a configurable default role, the session list and revocation, TOTP, rate limiting and lockout, profile PINs and maximum ratings, and the audit log (SPEC §7.10, §7.11).
- **Browser playback:** the direct/remux/transcode decision, HLS, hardware encoders with software fallback, HDR tone mapping, bitrate limits, JASSUB subtitles, and the HTML5 player (SPEC §7.5).
- **Metadata extras:** AniList matching for anime, fanart.tv logos and backdrops, and OpenSubtitles.
- **Remote access:** HTTPS enforcement behind trusted proxies, guides for Tailscale, Cloudflare Tunnel and Caddy, and the desktop app connecting to a remote server.
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
