# MediaMan — Brief

- **Date:** 2026-10-04
- **One-liner:** A free, self-hosted, all-in-one media server, player, torrent client and torrent search engine with a modern streaming-service UI — "Plex with no paywall and torrenting built in."

## Problem

People with local media collections want the polished experience of Netflix or Prime Video, but today that means stitching together Plex/Jellyfin, Sonarr, Radarr, Prowlarr/Jackett and qBittorrent, each with its own UI, config and failure modes. Plex locks features behind a paywall and depends on its cloud. Two earlier attempts (a VLC plugin, and MediaMan2, a Python/MPV app built in a couple of nights) proved the idea but weren't high quality or adaptable enough. MediaMan is a clean rewrite that shares nothing with them: one app that finds, downloads, organizes and plays media, and "just works".

## Goals

1. Point MediaMan at media folders and get a matched, artwork-rich, Netflix-style library (title, cover, backdrop, description) with no manual tagging.
2. Play anything on desktop with high quality (including HDR and styled subtitles), with resume, continue-watching and next-episode.
3. Find content from the catalog (TMDB) and download it with one click via built-in torrent search and client; downloads are identified and imported automatically.
4. Torrent traffic is easy to keep behind any VPN, with a kill switch, and is off by default until a VPN is configured or the user explicitly opts out.
5. Run either locally on one PC, or as a server that others reach on the LAN or over the internet, using the same UI.
6. Multiple accounts and profiles with role-based permissions that admins can configure.

## Non-goals (for now)

- Stream-while-downloading (v1 only lets the user raise priority on the first episode or two). Parked.
- Auto-downloading followed shows or a watchlist (Sonarr/Radarr-style automation). Late stage.
- Built-in WireGuard tunnel management. Parked.
- TV apps (Android TV, webOS, Tizen), native mobile apps, a 10-foot UI. Later.
- Embedding MPV inside the app window (v1 uses a separate MPV window). Parked. (Since taken up: ADR 0011, PLAN 2.5.11.)
- Signed or notarized macOS builds (needs a paid Apple developer account).
- Built-in public exposure of the server: remote access goes through Tailscale, Cloudflare Tunnel or a reverse proxy.
- Anything reused from MediaMan2.

## Users and use

The author first: a home media enthusiast using it daily on a Windows PC, and later on a home server. Then household members and trusted friends, who stream from that server on desktop, phone browsers, and from outside the home. Published publicly for anyone who self-hosts.

## Key decisions

| Decision | Why | Alternatives rejected |
|---|---|---|
| C# (.NET 10) server | Author understands C# well and it's career-relevant; ASP.NET Core is fast and cross-platform; Jellyfin proves C# for media servers | Rust (author knows none, can't review it); Go (author too early in it); Python (author dislikes it); Node (weak torrent engine) |
| TypeScript + React web UI, shared by every client | Author is strong in JS/TS and has used React; one UI whether local or remote | Svelte (author only used it with heavy AI help) |
| Electron desktop app with MPV for playback | MPV plays every codec with the best HDR and subtitle support (proven in MediaMan2 vs VLC); Electron keeps the desktop shell in TypeScript | Tauri (needs Rust for native glue, weak Linux webview video); VLC (more issues in practice) |
| Server decides per client: direct play → remux → transcode | One server serves MPV desktop, browsers, and future phone/TV apps | Desktop-only, or browser-only playback |
| v1 MPV runs in its own fullscreen window, controlled over JSON IPC, with a custom purple Lua controller (OSC) | Robust and simple; author knows Lua | Embedding libmpv in the window (hard; parked) |
| MonoTorrent behind an `ITorrentEngine` interface, validated by a test build first; fallback is a qBittorrent-nox (libtorrent) sidecar | Native C#, MIT, active; the interface means a weak engine can be swapped out | WebTorrent (weak as a full client) |
| Catalog-first downloading: pick a title, click "Get", results ranked by quality profile; raw torrent search as an "Advanced" tab | Removes "which show is this?" guesswork; feels like a streaming app | Torrent-first only |
| Indexers via Cardigann-compatible YAML definitions + Torznab + optional FlareSolverr | Scrapers break constantly; reuses hundreds of community definitions; users add sites without code | Hand-written scrapers per site |
| No site definitions bundled; one-click import of community definitions from a URL on first setup | Keeps the public repo clear of piracy-site lists while still "just working" | Shipping a default site list |
| Two library kinds: read-only external libraries and MediaMan-managed storage | External folders are never modified (MediaMan2's model); managed storage can rename/organize after seeding or hardlink | Always moving files (breaks seeding) |
| Low-confidence matches go to a "Needs attention" inbox with one-click fix | Confident guessing plus easy correction is what makes it "just work" | Silent best guess |
| VPN: bind to an adapter, with auto-pause if it disappears; SOCKS5 option; gluetun Docker recipe; leak check; torrenting off until configured or explicitly accepted | Works with any VPN provider; a VPN reconnect must never leak the user's IP | Provider-specific integrations |
| Permission-based roles: Superadmin (single, protected, transferable only), Admin and User presets, custom roles; admin-configurable default role; invite links carry a role | Owner keeps control; trusting hosts can grant direct downloads | Hard-coded role checks |
| Downloads need `downloads.request` (admin approves) or `downloads.create` (direct) | Supports both cautious and trusting households | Downloads for all or admins only |
| Accounts contain Netflix-style profiles (optional PIN) | Separate watch history per person | One history per account |
| Auth: Argon2 hashes, short-lived access + refresh tokens, revocable device sessions, rate limiting/lockout, optional TOTP, HTTPS required off-LAN | Server is reachable from the internet | Relying on network trust |
| TMDB key shipped with the project, overridable in settings | Zero setup; allowed for non-commercial apps | Requiring every user to register a key |
| AGPL-3.0; repo public on GitHub from v0.2 (October 2026); no contributor agreement until outside contributions are accepted, then a CLA via CLA Assistant | Network-copyleft: hosted forks must publish source. As sole author the owner can relicense freely; a CLA preserves that once others contribute | GPL-3.0 (hosting loophole); MIT (closed forks); Elastic License 2.0 (not OSI open source); DCO (grants no relicensing rights) |
| Quality is chosen per component, not as a whole profile: on "Get", filter chips for resolution (720p/1080p/1440p/2160p), dynamic range (SDR/HDR/Dolby Vision), bitrate tier (Low/Medium/High/Max), codec, source, audio and language update the result list live, ranked by best match then seeders. Users can save any chip combination as a named profile (optional shortcut); last-used choices become their default | Users think in individual traits ("4K, HDR, not huge"), not bundles | Fixed whole-profile presets; required/preferred/blocked scoring rules |
| Bitrate tiers are relative to resolution and derived from file size ÷ runtime | Release names rarely state bitrate; size per hour is always available | Raw bitrate fields |
| Development: Phase 0 freezes contracts (OpenAPI, DB schema, module interfaces), then parallel subagent workstreams in git worktrees with integration checkpoints | Many independent components; contracts let them be built concurrently | Building sequentially |
| Video transcoding (software + hardware) arrives with browser/remote playback, not in the MVP | The MVP client (MPV) needs none; transcoding is the most complex and platform-specific subsystem | Transcoding in the MVP |

## Stack

- **Server:** .NET 10 / ASP.NET Core; EF Core + SQLite; MonoTorrent; ffmpeg (jellyfin-ffmpeg builds) run as a separate process. Distributed as a single-file publish and a Docker image.
- **Web UI:** React + Vite + TypeScript, TanStack Query, Tailwind. Installable as a browser app (PWA). API client generated from OpenAPI.
- **Desktop:** Electron wrapping the web UI; spawns the server in local mode or connects to a remote one; launches MPV (with a Lua OSC) over JSON IPC.
- **Platforms:** Windows and Linux fully supported; macOS best-effort, unsigned (testable on the author's MacBook).
- **Contract:** OpenAPI spec as the source of truth, with server stubs and TS client generated from it.
- **Look:** dark base with a purple tint (~`#0E0B14`), violet accent (~`#8B5CF6`), highlight (~`#C4B5FD`); hero sections tinted from title backdrops; Netflix-style rows with hover previews; fast keyboard navigation.

## External dependencies

- TMDB API (primary metadata)
- fanart.tv API (title logos and backdrops)
- AniList API (anime metadata)
- OpenSubtitles API (subtitles)
- Torrent indexer sites (via Cardigann definitions) and Torznab endpoints (Jackett/Prowlarr)
- Community indexer definition repository (imported by URL)
- FlareSolverr (optional, user-run)
- BitTorrent network: peers, trackers, DHT
- VPN adapter or SOCKS5 proxy (user-provided)
- External IP / leak-check service
- ffmpeg and MPV binaries
- Filesystem, including network (SMB) shares

## Principles

- It just works: sensible defaults, confident automatic matching, and easy correction when it's wrong.
- External libraries are never modified.
- Torrent traffic never leaves the configured VPN path; if the path drops, torrents pause.
- Never execute downloaded files; flag or block dangerous extensions (`.exe`, `.lnk`, `.scr`, etc.).
- One UI everywhere: local and remote use look and behave the same.
- Play directly whenever possible; remux before transcoding.
- Every action is permission-checked on the server; the Superadmin can never be locked out or removed.
- Clean, fast, modern, purple: every screen should feel like a streaming service, not a utility.

## Open questions

- Risk, not a user decision: MonoTorrent may fail the Phase 0 test build (VPN binding of all traffic including DHT and trackers, hundreds of seeding torrents, magnets, throughput). The test build resolves it; on failure, switch to the qBittorrent-nox sidecar behind `ITorrentEngine`. Blocks the torrent-engine workstream.
- Exact size-per-hour boundaries for each bitrate tier at each resolution. Settled in the SPEC; blocks catalog-first ranking.

## Scope guardrail

The MVP is done when, on Windows: the user adds an external library folder, MediaMan scans it and matches it against TMDB with artwork, the Superadmin logs in and browses a Netflix-style UI, and a title plays in the desktop app via MPV, resumes where it left off, and offers the next episode. Torrenting, indexers, multiple users, browser playback and transcoding come in later phases.
