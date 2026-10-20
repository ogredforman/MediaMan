# MediaMan

A free, self-hosted media server and desktop player: point it at your movies and shows, and it
matches them to TMDB, shows them in a streaming-service style UI, and plays them in an embedded
mpv player with resume and "Up next".

The long-term goal is "Plex with no paywall and torrenting built in": one app instead of the five
it takes today (Plex/Jellyfin, Sonarr, Radarr, Prowlarr, qBittorrent).

<!-- TODO: hero screenshot or 10–20 s GIF: Home → title page → Play → Up next -->

![CI](https://github.com/ogredforman/MediaMan/actions/workflows/ci.yml/badge.svg)
![License: AGPL-3.0](https://img.shields.io/badge/license-AGPL--3.0-blue)

**Status:** work in progress, v0.2 pre-release. Stages 0–2.5 of the [plan](docs/PLAN.md) are done
(library, metadata, accounts, desktop playback, Windows installer). Torrents, indexers, browser
playback and remote access come next.

## What it does today

- Scans your folders and works out movies, shows, seasons and episodes from the file names, with
  no particular folder layout required. Files it can't match land in a "Needs attention" inbox
  with a Fix match dialog.
- Pulls posters, backdrops and metadata from TMDB, cached locally, with the page tinted from the
  artwork's dominant color.
- Home screen with Continue Watching, Next Up, Recently Added and genre rows; instant search.
- Plays in the app window through embedded mpv, with resume, per-profile audio and subtitle
  languages, version picking (e.g. 4K vs 1080p) and an Up next countdown at the credits.
- Accounts and up to 10 profiles per account. External libraries are never written to.
- Installs on Windows from one file, with mpv and ffprobe bundled.

## Architecture

```
 Electron desktop app ──┐
   (mpv embedded,       │  REST + SSE           ┌──────────────────────────────┐
    React UI overlay)   ├──────────────────────▶│ ASP.NET Core server (.NET 10)│
 React web UI ──────────┘   contracts/          │  Library · Metadata · Auth   │
                            openapi.yaml        │  Playback · Parsing          │
                                                │  EF Core + SQLite (WAL)      │
                                                └──────┬──────────────┬────────┘
                                                       │              │
                                                   TMDB API     ffprobe, file system
```

- **Server:** .NET 10, ASP.NET Core minimal APIs, EF Core with SQLite, split into modules
  (`server/src/MediaMan.*`) that only talk through interfaces declared in `MediaMan.Core`.
- **Web UI:** React, Vite, TypeScript (strict), TanStack Router and Query, Tailwind.
- **Desktop:** Electron driving mpv over JSON IPC, with the React player controls drawn on top.

## Engineering highlights

- **Contract first.** [`contracts/openapi.yaml`](contracts/openapi.yaml) is the source of truth for
  the API. The TypeScript client and mock handlers are generated from it, and conformance tests
  check server responses against it.
- **Every external dependency has a fake.** TMDB, ffprobe, mpv, the clock and file watching sit
  behind interfaces with deterministic fakes, so the test suite never touches the network.
- **Release-name parser** tested against a corpus of 300+ real-world file names with an
  enforced accuracy baseline.
- **Auth:** Argon2id password hashing, short-lived JWTs with rotating refresh tokens (reuse revokes
  the session), signed artwork and stream URLs, and a table-driven test that checks the
  permission on every route.
- **Safety rules enforced in tests:** external libraries are read-only, and the server refuses
  non-local clients until remote access is turned on.
- **Decisions written down** as ADRs in [`docs/adr/`](docs/adr/).
- **CI** builds, lints and tests on Windows and Linux; releases build the Windows installer.

## How it's built

I wrote the spec ([`docs/SPEC.md`](docs/SPEC.md)), the staged plan ([`docs/PLAN.md`](docs/PLAN.md)),
the project rules ([`AGENTS.md`](AGENTS.md)) and the architecture decisions. AI coding agents
implement the plan one task at a time, using my [Devflow](https://github.com/ogredforman/Devflow)
workflow: each task gets a plan I approve, tests, lint, and a review against the project's rules
before it's committed.

## Install (Windows)

Download `MediaMan-Setup-<version>-x64.exe` from
[Releases](https://github.com/ogredforman/MediaMan/releases). Full steps, including the
SmartScreen warning for the unsigned installer: [`docs/INSTALL-WINDOWS.md`](docs/INSTALL-WINDOWS.md).

## Development

Requires the .NET 10 SDK and Node.js 22+.

```sh
npm install
npm run build   # server, web and desktop
npm test        # all tests
npm run lint    # ESLint + dotnet format
```

## License

[AGPL-3.0](LICENSE). This product uses the TMDB API but is not endorsed or certified by TMDB.
