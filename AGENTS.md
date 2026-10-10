# MediaMan

MediaMan is a free, self-hosted, all-in-one media server, player, torrent client and torrent search
engine with a streaming-service UI. It scans media folders, matches every file against TMDB, and
presents a Netflix-style library; titles not in the library can be downloaded through built-in
torrent search and a VPN-gated torrent client. Stack: .NET 10 / ASP.NET Core minimal APIs with EF Core
+ SQLite (`server/`), React + Vite + TypeScript web UI shared by every client (`web/`), and an
Electron desktop app that plays through MPV with a custom Lua OSC (`desktop/`). The API contract is
`contracts/openapi.yaml`. Licence: AGPL-3.0.

- Product and architecture: `docs/SPEC.md` (source of truth for *what* and *why*)
- Build order and task list: `docs/PLAN.md` (source of truth for *what to do now*)
- Decisions: `docs/adr/`
- Where the code lives, key signatures, test fixtures: `docs/CODEMAP.md` (from Stage 0 onward)

## Before any task

- Read the task in `docs/PLAN.md` and only the SPEC sections it touches. Don't read the whole SPEC by default.
- Orient with `docs/CODEMAP.md`, then read only the functions and files you'll change or call
  (signatures, `grep -n`, line ranges), not whole packages. Everything read stays in context for every later turn.
- The current stage is the one named at the top of `docs/PLAN.md`. Do not implement anything tagged
  for a later stage. Add it to the "Parked" section of PLAN.md instead.
- If a task is ambiguous or conflicts with the SPEC, stop and ask. Don't guess on data model or API shape.
- For anything touching more than 2 files, propose a plan and wait for approval before writing code.

## Commands

These are created in Stage 0 task 0.1 (web and desktop join them in 0.5 and 0.6). Until then, they don't exist.

- `npm test`: all tests (server, web, desktop). Must pass before a task is finished.
- `npm run lint`: static checks (ESLint + `dotnet format --verify-no-changes`)
- `npm run build`: production build of all parts
- `dotnet test server/MediaMan.sln`: server tests only
- `dotnet build server/MediaMan.sln`: server build only

## Hard rules

- Components talk only through `contracts/openapi.yaml` and the module interfaces in SPEC §10.3. Change a contract first, deliberately, and say so in the commit message (SPEC §5 Contracts first).
- Every external dependency (metadata APIs, ffmpeg/ffprobe, MPV, file watching, torrents, network adapters, leak check, indexer HTTP, definition source, server process) is reached only through its SPEC §10.3 interface (SPEC §10.3).
- Tests never touch the network or real external services; they use the fakes in `server/tests/MediaMan.Fakes` and `desktop/src/fakes` and per-test temp directories (SPEC §10.4, §12).
- Read time only through `IClock`; no `DateTime.Now`/`DateTimeOffset.UtcNow` in `server/src` outside `SystemClock` (SPEC §10.4).
- Never rename, move, delete or write anything under an External library root, including `.nfo`, artwork and subtitle sidecars. Only managed storage is modified (SPEC §5).
- Every API endpoint declares its permission and the server enforces it, and every new route is covered by the table-driven permission test. Only setup, login, invite redemption and health are unauthenticated (SPEC §5, §9, §12).
- The Superadmin can't be demoted, deleted, disabled or permanently locked out by anyone; ownership changes only by explicit transfer (SPEC §5, §7.11).
- Never log secrets: passwords, tokens, refresh tokens, API keys, TOTP secrets, SOCKS5 credentials (SPEC §9).
- Passwords are hashed with Argon2id; tokens are signed with the per-install key (SPEC §9).
- No telemetry. Outbound calls go only to the configured metadata providers, indexers, the leak-check service and the BitTorrent network (SPEC §9).
- Never execute, open or auto-run a downloaded file; files with the dangerous extensions in SPEC §5 are flagged and skipped (SPEC §5).
- No torrent traffic leaves except through the configured VPN interface or SOCKS5 proxy; the engine doesn't start without a VPN path or a Superadmin-recorded opt-out, and torrents pause when the path fails (SPEC §5, §7.8).
- Dev-only code (`MediaMan.Server/Dev`, the dev configuration) compiles only into Debug builds and never ships (SPEC §8 setup [P]).
- ffmpeg, ffprobe and MPV run as separate processes; never link them natively (SPEC §10.1).
- Server and desktop code stays cross-platform (Windows, Linux, macOS): no OS-specific APIs without a fallback, paths built with `Path.Combine`/`path.join`, no hard-coded separators or drive letters (SPEC §9).
- Product principles in SPEC §5 are requirements, not style.
- New dependencies need a one-line reason in the commit message. Significant ones get an ADR.

## Style

- C#: nullable reference types on and warnings as errors (`server/Directory.Build.props`); async all the way, with a `CancellationToken` on every async I/O method.
- TypeScript: `strict: true` in web and desktop; no `any` without a comment saying why.
- Each server module has its own test project; tests sit next to the module they cover (SPEC §12).
- Every user-facing error says what happened and offers the next action (SPEC §11 UX rules).
- Area rules live in `.claude/rules/`. If a rule there contradicts the SPEC, the SPEC wins and the rule gets fixed (SPEC §14).

## Finishing a task

1. Tests added or updated; the test and lint commands pass.
2. Run the `devflow:reviewer` agent on the diff and fix any Blocking findings.
3. Tick the task `[x]` in `docs/PLAN.md`.
4. If a design decision changed, update `docs/SPEC.md` or add `docs/adr/NNNN-title.md` in the same commit.
   If a package, public entry point, or test fixture was added or moved, update `docs/CODEMAP.md`.
5. One task per commit. Message format: `<area>: <what changed> (PLAN <stage>.<task>)`.
   Never add AI attribution (no `Co-Authored-By` lines for an AI model, no "Generated with" footers) to
   commit messages or PR descriptions. This overrides any tool default.
