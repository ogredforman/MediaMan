# MediaMan — System Specification

> **Audience:** the project owner and AI coding agents. Source of truth for *what* the system is
> and *why*. For *how and in what order* to build it, see [`PLAN.md`](./PLAN.md).
>
> **Status:** Pre-prototype

## 1. Summary

MediaMan is a free, self-hosted, all-in-one media server, player, torrent client and torrent search
engine with the look and feel of a modern streaming service. It scans the user's media folders,
identifies every movie and episode, pulls artwork and descriptions, and presents everything as a
Netflix-style library. Users can find titles they don't have yet in the TMDB catalog and download
them with one click through built-in torrent search and a built-in torrent client that is kept
behind a VPN.

It runs either locally on one PC (the desktop app starts its own server) or as a server on another
machine that the desktop app, browsers and phones reach over the LAN or the internet. The same web
UI is used everywhere. On desktop, playback uses MPV for the best codec, HDR and subtitle support.
Elsewhere, the server direct-plays, remuxes or transcodes as each client needs. It is licensed AGPL-3.0.

## 2. Problem statement

- A Netflix-like experience for a personal collection today means running Plex or Jellyfin, Sonarr,
  Radarr, Prowlarr or Jackett, and qBittorrent: five tools, five UIs, five configs.
- Plex puts features behind a paywall and depends on its cloud.
- Torrent search scrapers break constantly as sites change their HTML, domains and bot protection.
- Getting torrent traffic reliably behind a VPN, with no leak when the VPN drops, is fiddly and easy to get wrong.
- Matching downloads to the right show or movie needs manual cleanup in most tools.
- The author's earlier attempts (a VLC plugin, and MediaMan2 in Python/MPV) proved the idea but
  weren't high quality or adaptable enough. This is a clean rewrite with nothing reused.

## 3. Goals

1. Adding a media folder produces a matched, artwork-rich library with no manual tagging. At least
   95% of well-named files match correctly, and everything below the confidence threshold lands in "Needs attention".
2. Any file plays on desktop at high quality (HDR, styled subtitles, any codec), with resume,
   continue-watching and next-episode.
3. A title not in the library can be found in the catalog and downloaded with one click.
   Downloads are identified and imported automatically.
4. Torrent traffic can only use the configured VPN path. If that path drops, torrents pause within
   seconds. Torrenting is off until a VPN is configured or the user explicitly opts out.
5. The same UI works locally, over the LAN, and over the internet, on desktop and in phone browsers.
6. Multiple accounts and profiles, with permission-based roles that admins configure.

## 4. Non-goals (for now)

- Stream-while-downloading. v1 only offers "prioritize first episodes".
- Auto-downloading followed shows or watchlists. Late stage.
- Built-in WireGuard tunnel management.
- Native TV apps, native mobile apps, a 10-foot UI.
- Embedding MPV inside the app window. v1 uses a separate MPV window.
- Signed or notarized macOS builds.
- Exposing the server to the internet directly. Remote access is off by default and goes through Tailscale, Cloudflare Tunnel or a reverse proxy (7.12).
- Bundling torrent site definitions in the repo.
- Reusing anything from MediaMan2.

The design must not preclude:
- Streaming while downloading (the torrent engine interface exposes piece priority).
- Automation (search and grab run as services, not UI code).
- TV and native mobile clients (all playback goes through the client-capability negotiation API).
- Embedding MPV later (player control sits behind an interface).

## 5. Product principles

- **It just works.** Defaults are chosen so a new user gets a working library without settings. Matching
  is confident and automatic, and every wrong guess can be fixed in one place with one action.
- **External libraries are read-only.** MediaMan never renames, moves, deletes or writes into an
  external library folder, including sidecar files such as `.nfo`, artwork or subtitles. Only
  managed storage is modified.
- **Torrents never leak.** No torrent traffic (peers, trackers, DHT, PEX, UPnP) leaves except through the
  configured VPN interface or SOCKS5 proxy. When that path is missing or the leak check fails, all
  torrents pause and stay paused until it is restored.
- **Torrenting is opt-in to risk.** The torrent engine does not start until a VPN path is configured or
  the Superadmin explicitly records "run without VPN".
- **Never execute downloads.** MediaMan never executes, opens or auto-runs a downloaded file. Torrents
  containing dangerous extensions (`.exe`, `.msi`, `.bat`, `.cmd`, `.scr`, `.lnk`, `.ps1`, `.vbs`, `.js`,
  `.jar`, `.com`, `.pif`) are flagged in search results and those files are skipped on download.
- **Private until opened.** Running on your own machine needs no security setup. Local mode is reachable only
  from the same PC, and in any mode the server refuses clients outside the local network until the Superadmin
  turns on remote access. Turning it on is one guided flow that configures everything it needs (7.12).
- **One UI everywhere.** The desktop app, browser and remote access use the same web UI and API. Behaviour
  differs only where a platform can't do something, such as MPV playback.
- **Cheapest playback first.** Direct play if the client can decode it, otherwise remux, otherwise
  transcode only the streams that need it.
- **The server enforces permissions.** Every API action checks permissions on the server; the UI hiding
  something is never the only protection.
- **The Superadmin is untouchable.** Exactly one Superadmin exists. It cannot be demoted, deleted,
  disabled or locked out by anyone else. Ownership changes only by explicit transfer from the Superadmin.
- **Feels like a streaming service.** Dark, purple, artwork-first, fast. Every screen should look and feel
  like a streaming service, not a utility: no raw tables where artwork belongs, and UI interactions
  respond within 100 ms.
- **Contracts first.** Components talk only through the OpenAPI contract and the module interfaces in
  10.3. Changing a contract is a deliberate, reviewed change.

## 6. Domain model

### 6.1 Entities

| Entity | Purpose | Key fields |
|---|---|---|
| Library | A set of root folders scanned as one library | id, name, kind (External/Managed), contentType (Movies/Shows/Anime/Mixed), rootPaths[], scanIntervalMinutes |
| MediaFile | A video file found on disk | id, libraryId, path, sizeBytes, mtime, probe (container, streams, duration, HDR format), parsed (ParsedRelease), status (Pending/Matched/NeedsAttention/Ignored/Missing), attentionReason (e.g. unreadable), missingSince |
| Title | A movie or show | id, kind (Movie/Show), name, year, overview, genres[], rating, runtime, externalIds (tmdb, imdb, tvdb, anilist), artwork refs |
| Season | A season of a show | id, titleId, number, name, overview, poster |
| Episode | One episode | id, seasonId, number, absoluteNumber?, name, overview, airDate, still |
| Playable | A movie Title or an Episode: the thing a user watches | id, kind (Movie/Episode), titleId?, episodeId? (exactly one set; ADR 0002) |
| FileLink | Links a MediaFile to the Playables it contains | mediaFileId, playableId, part (for multi-episode files) |
| Artwork | A cached image | id, titleId? / seasonId? / episodeId? (exactly one owner; ADR 0002), type (poster/backdrop/logo/still), url, localPath, dominantColor |
| MatchReview | A low-confidence or failed match awaiting a user | id, mediaFileId, candidates[] (titleRef, score), reason |
| User | An account | id, username, passwordHash, roleId, isSuperadmin, totpSecret?, disabled, createdAt |
| Role | A named bundle of permissions | id, name, permissions[], isPreset |
| Profile | A Netflix-style viewer under an account | id, userId, name, avatar, pinHash?, maxRating?, audioLangPrefs[], subtitleLangPrefs[] |
| Session | A signed-in device | id, userId, deviceName, refreshTokenHash, createdAt, lastSeenAt, expiresAt, revoked |
| Invite | A link to create an account | id, tokenHash, roleId, createdBy, expiresAt, maxUses, uses |
| WatchState | A profile's progress on a Playable | profileId, playableId, positionSeconds, durationSeconds, watched, lastWatchedAt |
| Indexer | A configured torrent search source | id, name, type (Cardigann/Torznab), definitionId?, settings, enabled, useFlareSolverr, health |
| IndexerDefinition | An imported Cardigann YAML definition | id, key, version, yaml, sourceUrl |
| ReleaseResult | A search result (transient, cached briefly) | indexerId, title, magnet/torrentUrl, sizeBytes, seeders, leechers, publishDate, parsed (ParsedRelease), dangerousFiles?, quality (derived) |
| QualityFilter | The chip selection on a "Get" | resolution[], dynamicRange[], bitrateTier[], codec[], source[], audio[], language[] |
| SavedQualityProfile | A named QualityFilter | id, userId, name, filter |
| Download | A torrent being downloaded or seeded | id, infoHash, name, state, progress, savePath, targetTitleId? (FK to Title), requestedBy, seedRatioLimit?, seedTimeLimit? |
| DownloadFile | A file inside a Download | downloadId, path, sizeBytes, priority, skipped, dangerous |
| DownloadRequest | A user asking for a download | id, userId, titleRef, release, filter, status (Pending/Approved/Denied), decidedBy |
| VpnConfig | The torrent network path | mode (Interface/Socks5/None-Accepted), interfaceId?, socksHost/Port/creds?, leakCheckIntervalSeconds |
| Setting | Server-wide key/value (including the remote access switch and trusted proxies) | key, value |
| IpBan | A client address banned after repeated abuse (7.11) | id, address, reason, strikes, bannedUntil, createdAt |
| AuditEvent | A security-relevant action | id, userId, action, target, at |

### 6.2 Relationships

```
Library 1─* MediaFile
MediaFile *─* Playable              (via FileLink; one file can hold several episodes,
                                     one Playable can have several files = versions)
Title(Show) 1─* Season 1─* Episode
Title 1─* Artwork, Season 1─* Artwork, Episode 1─* Artwork
MediaFile 1─0..1 MatchReview
Role 1─* User 1─* Profile 1─* WatchState *─1 Playable
User 1─* Session
Role 1─* Invite
Indexer *─0..1 IndexerDefinition
Download 1─* DownloadFile
Download *─0..1 Title               (the target title, if known)
User 1─* DownloadRequest
User 1─* SavedQualityProfile
```

## 7. Core flows

### 7.1 First run

1. The server starts with an empty database and serves the setup wizard. Every other API call returns `423 SetupRequired`.
2. The user creates the Superadmin (username and password, minimum 12 characters).
3. The user picks a mode. **Local** binds the server to `127.0.0.1` only. **Server** listens on every interface (port 8420 by default) and
   relies on the local-network guard (7.12 step 1), so a changing LAN address needs no reconfiguration.
   Remote access isn't offered here: it starts off, so either mode serves only the local network until it's turned
   on in Settings (7.12).
4. The user adds a first library (path, content type). An unreadable path is shown inline and the step can't be completed until it's fixed.
5. Optional: configure the VPN path and import indexer definitions from a URL. These can be skipped; torrenting stays off.
6. The wizard closes, the first scan starts, and the user lands on Home with scan progress visible.

### 7.2 Library scan and match

1. Enumerate the library's root paths. New, changed (size or mtime) and missing files are detected
   against stored MediaFiles. Non-video files, samples (`sample` in the name and under 300 MB) and
   extras folders (`Extras`, `Featurettes`, `Behind The Scenes`, etc.) are ignored.
2. Probe each new or changed file (`IMediaProbe`): container, streams, duration, HDR format. A file that can't be probed is marked `NeedsAttention` with reason "unreadable".
3. Parse the file name and its parent folders (`ReleaseParser`) into a ParsedRelease: title, year,
   season and episode(s), absolute episode, resolution, source, codec, HDR, audio, group.
4. Search the metadata provider (TMDB, or AniList for anime libraries and anime-detected releases), then
   score the candidates on title similarity, year, kind and episode existence.
5. If the score is at or above the confidence threshold (default 0.85), link the file to the Playable(s)
   and fetch any missing metadata and artwork. Otherwise create a MatchReview with the top candidates.
6. Missing files are marked `Missing`. Their FileLinks are kept for 30 days, so a remounted drive restores them without losing watch state.
7. Provider errors (network, rate limit) retry with backoff. Affected files stay `Pending` and are retried on the next scan, never marked wrong.
8. File watchers trigger incremental scans. Periodic full scans (default every 6 h) cover network shares where file watching is unreliable.

### 7.3 Fix a match

1. The user opens "Needs attention", or "Fix match" on any title.
2. The suggested candidates are shown with posters, and the user can search manually.
3. Choosing a candidate (and for shows, confirming the season and episode mapping) relinks the file. The choice
   is remembered as an override for that path, so rescans keep it.

### 7.4 Play on desktop (MPV)

1. The user presses Play. The client calls `POST /playback/sessions` with its capabilities (`player: mpv`, so it can decode everything).
2. The server picks a version (the profile's preferred version if set, otherwise the highest quality), and returns
   a direct-stream URL, the resume position, and the external subtitle URLs.
3. The desktop app launches MPV via `IPlayer` with the stream URL, start position and subtitles. The purple Lua OSC is loaded.
4. The desktop app reports progress to the server every 10 s and on pause, seek and quit.
5. At 90% or more, the Playable is marked watched. If the next episode exists, an "Up next" countdown (10 s, cancellable) starts it.
6. If MPV fails to start, the UI shows the error with "Open MPV settings". If the stream URL fails, the server is re-queried once before an error is shown.

### 7.5 Play in a browser

1. The client sends its capabilities (supported containers, video and audio codecs, HDR support, maximum bitrate).
2. The server decides: **direct play** if everything is supported, **remux** to HLS (fMP4) if only the container
   is unsupported, **transcode** only the unsupported streams otherwise. HDR is tone-mapped when the client
   lacks HDR, and bitrate is reduced when the client's maximum requires it.
3. The browser plays via HLS. Styled subtitles (ASS) render client-side with JASSUB. Picture subtitles (PGS) are burned in only when selected.
4. A seek beyond the generated segments restarts the transcode at the seek point.

### 7.6 Catalog "Get"

1. The user opens any title from catalog search, including titles not in the library, and presses "Get" (for shows: a whole season, or chosen episodes).
2. The quality chips are pre-filled from the user's last choice or a saved profile.
3. The server searches all enabled indexers in parallel (each with a 15 s timeout), parses the results, discards releases that
   don't match the title, season or episode, and derives quality (bitrate tier = size ÷ runtime against the tier table in 8).
4. Results update live as the chips change. They're ranked by number of chip matches, then seeders. Dangerous releases are flagged in red.
5. With `downloads.create`, choosing a result starts a Download linked to the title. With only
   `downloads.request`, it creates a DownloadRequest for an admin to approve.
6. Indexers that fail are listed as "n sources failed" with the reason, and the remaining results still show.

### 7.7 Download, import, seed

1. The Download starts only if the VPN gate is open (7.8). Dangerous files are skipped. For season packs the user may tick "prioritize first episodes".
2. On completion, the files are probed and parsed, then linked to the target title if known (else matched as in 7.2).
3. Files stay in the managed download folder while seeding. If the download folder and library are on the same volume,
   files are hardlinked into the managed library layout (`Movies/Title (Year)/`, `Shows/Title (Year)/Season NN/`) right away. Otherwise
   they are moved into it once the seeding limits are met.
4. The title appears in the library, and the user is notified in the UI.

### 7.8 VPN gate

1. The torrent engine binds all sockets to the configured interface's address, or routes through SOCKS5.
2. Every 5 s the interface is checked for presence and an address. Every `leakCheckIntervalSeconds` (default 300), the public IP
   seen through the torrent path is compared with the public IP seen through the default route.
3. If the interface is missing, or the two IPs match (a leak), every torrent pauses at once. A persistent red banner
   shows and an AuditEvent is logged.
4. When the path is healthy again, torrents resume automatically.

### 7.9 Download request

1. A user with only `downloads.request` submits a request (7.6).
2. Users with `downloads.approve` see a request badge and the request queue. Approving starts the Download as the approver. Denying records a reason.
3. The requester sees the status on the title page.

### 7.10 Invite and account creation

1. A user with `users.manage` creates an invite (role, expiry, maximum uses). The role can't have more permissions than the inviter has.
2. The invitee opens the link and picks a username and password. A User and a default Profile are created.
3. Expired, used-up or revoked invites fail with a clear message.

### 7.11 Sign in and sessions

1. Username and password (plus TOTP if enabled) return a short-lived access token (15 min) and a rotating refresh token (30 days, sliding).
2. Repeated failures are rate-limited per IP and per account (5 failures → 15 min lockout), in every mode. The Superadmin is rate-limited but can't be permanently locked.
3. The profile picker shows after sign-in. A profile PIN is required if one is set.
4. Users can see and revoke their own sessions; `users.manage` can revoke anyone's except the Superadmin's.
5. While remote access is on, abuse escalates to IP bans, like fail2ban built in. An address that keeps getting
   locked out, or that sends a burst of invalid tokens or failed requests across endpoints, is banned for growing
   periods (1 hour, then 24 hours, then 7 days; strikes expire after 30 days). Banned addresses get `403` on
   everything. Bans persist across restarts and are listed in Settings → Remote access, where the Superadmin and
   `server.control` can lift them. A ban applies only to the real client address resolved through trusted proxies
   (7.12), never to a trusted proxy itself or a local-network address, so the owner can always get in from home.
   All timing reads `IClock`.

### 7.12 Remote access

1. Remote access is off by default. While it's off, the server serves only clients on the local network:
   loopback, private LAN ranges (RFC 1918, IPv4 and IPv6 link-local, IPv6 unique-local) and the Tailscale ranges
   (`100.64.0.0/10`, `fd7a:115c:a1e0::/48`). Any other client gets `403` (`remote_access.disabled`) saying that
   remote access is off and how to turn it on. A request carrying forwarding headers (`Forwarded`,
   `X-Forwarded-For`, `X-Real-IP`, `CF-Connecting-IP`) is treated as outside, so a tunnel or proxy on the same
   machine can't expose the server by accident. A request whose client address is unknown is treated as outside too.
2. Tailscale needs no switch: tailnet devices count as the local network, so Server mode plus Tailscale works as-is.
   It's the recommended option.
3. Only the Superadmin turns remote access on, in Settings → Remote access. That opens a guided setup: pick
   Cloudflare Tunnel or a reverse proxy (Caddy), follow the in-app steps, and the server configures the trusted
   proxies for that choice. From then on, clients outside the local network must arrive through a trusted proxy
   over HTTPS (`X-Forwarded-Proto`); anything else is refused with an error that says how to fix it.
4. The setup ends with an outside check, with no third-party service involved: the UI shows the public URL and a
   one-time code, the user opens it on a device off the local network (e.g. a phone on mobile data), and the server
   confirms it arrived through the trusted proxy over HTTPS.
5. While remote access is on, a "Remote access on" badge is visible to admins in the shell, and IP bans are active
   (7.11 step 5). Turning it off takes effect at once: outside clients are refused again.

## 8. Functional requirements

**Setup and modes**
- [P] The server runs with a SQLite database and a configured media path, without a wizard (dev configuration).
- [M] First-run wizard as in 7.1: create the Superadmin, choose the mode, add a library.
- [M] Local mode binds to `127.0.0.1`; Server mode binds to the LAN. Changing this needs `server.control`.
- [L] Docker image with documented volumes and a gluetun recipe.

**Libraries and scanning**
- [P] Scan a folder recursively, detect video files, and ignore samples, extras and non-video files (7.2).
- [P] Probe files with ffprobe via `IMediaProbe`.
- [P] Incremental rescan: only new and changed files are re-processed; missing files are marked `Missing`.
- [M] Multiple libraries, each with several root paths and a content type.
- [M] File watcher plus periodic full scan.
- [M] External libraries are never written to (enforced by a test that scans a read-only temp folder).
- [M] Managed libraries with the hardlink/move layout (7.7 step 3).

**Parsing and matching**
- [P] `ReleaseParser` extracts title, year, season, episode(s), absolute episode, resolution, source, codec,
  HDR format, audio and group from file and folder names. Passes the release-name test corpus (≥300 cases).
- [P] Match against `IMetadataProvider`, using the confidence threshold and MatchReview (7.2).
- [P] Multi-episode files link to every episode they contain.
- [M] "Needs attention" inbox and "Fix match" with remembered overrides (7.3).
- [M] Several versions per Playable; a version picker on the detail page.
- [L] AniList matching for anime with absolute numbering.

**Metadata and artwork**
- [P] Title, overview, year, genres, rating, poster, backdrop, and season and episode data from TMDB (faked in [P]).
- [M] Real TMDB with the shipped key, overridable in settings; responses cached for 7 days.
- [M] Artwork cached locally, with the dominant color extracted for hero tinting.
- [L] fanart.tv title logos and extra backdrops.
- [L] OpenSubtitles search and download for any Playable.

**Browsing**
- [P] Browse grid of movies and shows, and a detail page (overview, seasons, episodes, versions).
- [M] Home: hero banner, Continue Watching, Recently Added, Next Up, rows by genre.
- [M] Instant search across the library (results as you type).
- [M] Watched markers and mark watched/unwatched.
- [M] Catalog search of TMDB for titles not in the library.

**Playback**
- [P] Pressing Play launches MPV on the local file. Prototype only: the server launches MPV on its own host through a dev-only endpoint.
- [M] The Electron desktop app launches MPV via `IPlayer` with the stream URL, resume position and subtitles (7.4).
- [M] Custom purple Lua OSC for MPV.
- [M] Resume after 2 minutes; watched at 90%; "Up next" with a 10 s countdown.
- [M] Per-profile audio and subtitle language preferences applied at playback.
- [M] Browser playback with direct play, remux and transcode decision (7.5), including hardware encoders (NVENC, QSV, VAAPI, AMF, VideoToolbox) with software fallback.
- [M] HDR → SDR tone mapping and bitrate limits for remote clients.
- [M] JASSUB rendering of styled subtitles in the browser.

**Accounts, roles and profiles**
- [M] Single Superadmin login with refresh tokens; the desktop app stays signed in.
- [M] Profiles with the profile picker.
- [L] Multiple users, invites (7.10), and Admin/User presets plus custom roles built from the permission list.
- [L] Admin-configurable default role for new accounts.
- [M] Session list and revocation, rate limiting and lockout (7.11).
- [L] Optional TOTP.
- [L] Profile PIN and maximum content rating.
- [L] Audit log of security-relevant actions.

Permissions: `library.view`, `library.manage`, `metadata.edit`, `downloads.request`, `downloads.create`,
`downloads.approve`, `downloads.manage`, `indexers.manage`, `users.manage`, `roles.manage`, `settings.manage`,
`server.control`. Superadmin has everything plus protected status. The Admin preset has everything except
`server.control` and deleting libraries, users or content. The User preset has `library.view` and `downloads.request`.

**Torrents and VPN**
- [M] Torrent engine behind `ITorrentEngine`: add by magnet or .torrent, pause, resume, remove (with or without data), per-file priority and skip, global and per-torrent speed limits, seeding ratio and time limits.
- [M] Downloads page: progress, speed, peers, ETA, seeding status.
- [M] "Prioritize first episodes" for season packs.
- [M] VPN gate (7.8): interface binding or SOCKS5, kill switch, leak check, status badge, opt-out recorded only by the Superadmin.
- [M] Dangerous files skipped and flagged.
- [M] Import on completion (7.7).

**Indexers and search**
- [M] Import Cardigann YAML definitions from a URL (one-click on first setup), and update them.
- [M] Torznab indexers (Jackett/Prowlarr).
- [M] Optional FlareSolverr per indexer.
- [M] Advanced raw search across indexers.
- [M] Indexer health shown (last success, last error).

**Catalog "Get" and quality**
- [M] "Get" flow with quality chips (7.6): resolution (720p/1080p/1440p/2160p), dynamic range (SDR/HDR/Dolby Vision), bitrate tier (Low/Medium/High/Max), codec, source, audio, language.
- [M] Saved quality profiles; last-used chips remembered per user.
- [M] Bitrate tiers in GB per hour of runtime (editable in settings):

  | Resolution | Low | Medium | High | Max |
  |---|---|---|---|---|
  | 720p | < 0.7 | 0.7–1.5 | 1.5–3 | > 3 |
  | 1080p | < 1.5 | 1.5–4 | 4–10 | > 10 |
  | 1440p | < 2.5 | 2.5–6 | 6–14 | > 14 |
  | 2160p | < 4 | 4–10 | 10–25 | > 25 |
- [L] Download requests and the approval queue (7.9).

**Remote access**
- [M] Remote access switch, off by default and turned on only by the Superadmin; while off, clients outside the local network (including requests forwarded by a local tunnel or proxy) are refused (7.12).
- [M] HTTPS required for non-LAN clients (behind a reverse proxy, via `X-Forwarded-Proto` from configured trusted proxies).
- [M] Guided remote access setup with the outside check (7.12), and in-app guides for Tailscale, Cloudflare Tunnel and Caddy.
- [M] Escalating IP bans while remote access is on, with a ban list and unban (7.11 step 5).
- [M] Desktop app connects to a remote server by URL.

**Later automation (parked)**
- [L] Follow a show and auto-download new episodes; watchlist movies; upgrade to better releases.
- [L] Stream while downloading.

## 9. Non-functional requirements

- **Portability:** server and desktop run on Windows 10+ and Linux (x64; server also arm64). macOS works best-effort, unsigned.
- **Performance:**
  - A first scan of 5,000 files with warm metadata caches finishes in under 3 minutes on an SSD.
  - An incremental rescan with no changes finishes in under 15 s.
  - The API answers library requests in under 100 ms at p95 on a 20,000-file library.
  - The UI's first contentful paint is under 1.5 s on the LAN.
- **Footprint:** the idle server uses under 300 MB of RAM with 20,000 files and 100 seeding torrents.
- **Resilience:**
  - A crash or power loss never corrupts the database (SQLite WAL mode).
  - Torrents resume with their state after a restart.
  - Interrupted scans resume.
- **Privacy:** no telemetry. Outbound calls go only to the configured metadata providers, indexers, the leak-check service and the BitTorrent network.
- **Security:**
  - Passwords are hashed with Argon2id.
  - Tokens are signed with a per-install key.
  - Secrets the server must read back (TOTP secrets, SOCKS5 credentials) are encrypted at rest with the per-install key (ADR 0003).
  - All endpoints are authenticated except setup, login (including token refresh), invite redemption and health.
  - Rate limiting on authentication, and escalating IP bans while remote access is on (7.11).
  - Clients outside the local network are refused until the Superadmin turns on remote access (7.12).
  - Never logs secrets.
- **Data portability:** a single SQLite file, the per-install key file and an artwork cache folder. Backup and restore are just copying files. Settings export and import as JSON.
- **Accessibility:** keyboard navigable throughout; visible focus states; WCAG AA contrast for text.
- **Bundled tools:** distributions always ship ffmpeg/ffprobe and MPV for their platform; users never install them separately. Their paths can be overridden in settings, but the bundled copies are the default.
- **Licensing:** AGPL-3.0; third-party licences (ffmpeg, MPV, MonoTorrent) are included in distributions.

## 10. Architecture

```
 ┌────────────── Desktop (Electron) ──────────────┐     ┌──── Browser / phone ────┐
 │ web UI (React)  ◄─►  main process  ──► MPV     │     │ web UI (React) + HLS    │
 │                      (spawns server in Local)  │     └───────────┬─────────────┘
 └──────────────────────────┬─────────────────────┘                 │
                            │  HTTPS / REST (OpenAPI) + SSE events  │
 ┌──────────────────────────▼───────────────────────────────────────▼─────────────┐
 │ MediaMan.Server (ASP.NET Core)                                                 │
 │  Auth · Library/Scanner · Parsing · Metadata · Playback/Streaming · Torrents   │
 │  · VPN gate · Indexers/Search · Requests · Settings                            │
 │        │            │             │              │              │              │
 │     SQLite      ffmpeg/ffprobe  MonoTorrent   metadata APIs   indexer sites    │
 └────────────────────────────────────────────────────────────────────────────────┘
```

### 10.1 Technology choices

| Concern | Choice | Notes |
|---|---|---|
| Server | .NET 10, ASP.NET Core minimal APIs | Single-file publish; cross-platform |
| Storage | SQLite via EF Core | WAL mode; migrations in repo |
| Torrent engine | MonoTorrent behind `ITorrentEngine` | Phase 0 test build; fallback is a qBittorrent-nox sidecar via its Web API |
| Media tools | ffmpeg / ffprobe (jellyfin-ffmpeg builds), run as a separate process | Bundled per platform |
| Real-time updates | Server-Sent Events | Scan progress, downloads, VPN status |
| API contract | OpenAPI 3.1 in `contracts/openapi.yaml` | TS client generated; server conformance tested |
| Web UI | React + Vite + TypeScript, TanStack Query, TanStack Router, Tailwind | Installable as a PWA |
| Desktop | Electron (TypeScript) | Local mode spawns the server; MPV over JSON IPC |
| Player | MPV + custom Lua OSC, run as a separate process | Bundled per platform with the desktop app; separate window in v1 |
| Indexers | Cardigann YAML engine + Torznab client | Optional FlareSolverr |
| Metadata | TMDB (primary), AniList (anime), fanart.tv (logos), OpenSubtitles (subs) | |
| Auth | Argon2id, JWT access + rotating refresh tokens, TOTP | |
| Server tests | xUnit v3 (Microsoft.Testing.Platform), AwesomeAssertions, Verify (snapshots; deferred, ADR 0001) | |
| Web tests | Vitest, Testing Library, MSW (mock API from OpenAPI), Playwright (end-to-end) | |
| Licence | AGPL-3.0 | |

### 10.2 Repository layout

```
contracts/
  openapi.yaml                 # API contract (source of truth)
server/
  MediaMan.sln
  src/
    MediaMan.Server/           # host, endpoints, DI wiring, SSE
    MediaMan.Core/             # domain types, module interfaces (10.3), Clock
    MediaMan.Data/             # EF Core DbContext, migrations
    MediaMan.Parsing/          # ReleaseParser (pure)
    MediaMan.Library/          # scanner, matcher, file watcher
    MediaMan.Metadata/         # TMDB, AniList, fanart.tv, OpenSubtitles clients
    MediaMan.Playback/         # sessions, direct/remux/transcode, ffmpeg wrapper
    MediaMan.Auth/             # users, roles, sessions, invites, TOTP
    MediaMan.Torrents/         # ITorrentEngine impls, VPN gate, import
    MediaMan.Indexers/         # Cardigann engine, Torznab, search, quality
  tests/
    MediaMan.<Module>.Tests/   # one per module
    MediaMan.Fakes/            # shared fakes (10.4)
    corpus/release-names.json  # parser test corpus
web/
  src/ (routes/, components/, api/ (generated), theme/)
desktop/
  src/ (main/, preload/), mpv/ (osc.lua, mpv.conf)
docker/
docs/ (BRIEF.md, SPEC.md, PLAN.md)
```

### 10.3 Key interfaces

```csharp
// Time
interface IClock { DateTimeOffset UtcNow { get; } }

// Metadata (TMDB, AniList)
interface IMetadataProvider {
  string Key { get; }                                   // "tmdb", "anilist"
  Task<IReadOnlyList<TitleCandidate>> SearchAsync(TitleQuery q, CancellationToken ct);
  Task<TitleDetails> GetTitleAsync(ExternalId id, CancellationToken ct);
  Task<SeasonDetails> GetSeasonAsync(ExternalId showId, int season, CancellationToken ct);
}
interface IArtworkProvider {                            // fanart.tv
  Task<IReadOnlyList<ArtworkRef>> GetArtworkAsync(ExternalId id, CancellationToken ct);
}
interface IImageDownloader {                            // artwork images by provider URL (ADR 0007)
  Task<byte[]> DownloadAsync(Uri url, CancellationToken ct);
}
interface ISubtitleProvider {                           // OpenSubtitles
  Task<IReadOnlyList<SubtitleCandidate>> SearchAsync(SubtitleQuery q, CancellationToken ct);
  Task<Stream> DownloadAsync(string candidateId, CancellationToken ct);
}

// Media tools
interface IMediaProbe { Task<ProbeResult> ProbeAsync(string path, CancellationToken ct); }
interface ITranscoder {
  Task<StreamSession> StartAsync(StreamPlan plan, CancellationToken ct);   // remux or transcode to HLS
  Task StopAsync(string sessionId, CancellationToken ct);
  IReadOnlyList<HwEncoder> DetectEncoders();
}

// Filesystem events (the filesystem itself is not faked; see 10.4)
interface IFileWatcher { IDisposable Watch(string root, Action<FileChange> onChange); }

// Torrents
interface ITorrentEngine {
  Task StartAsync(TorrentNetworkConfig net, CancellationToken ct);         // binds to interface or SOCKS5
  Task<TorrentHandle> AddAsync(TorrentSource src, AddOptions opts, CancellationToken ct);
  Task PauseAsync(InfoHash h, CancellationToken ct); Task ResumeAsync(InfoHash h, CancellationToken ct);
  Task PauseAllAsync(CancellationToken ct); Task ResumeAllAsync(CancellationToken ct);
  Task RemoveAsync(InfoHash h, bool deleteData, CancellationToken ct);
  Task SetFilePriorityAsync(InfoHash h, int fileIndex, FilePriority p, CancellationToken ct);
  IReadOnlyList<TorrentStatus> GetAll();
  event EventHandler<TorrentEvent> Changed;            // progress, completed, error
}
interface INetworkInterfaces {                          // OS network adapters
  IReadOnlyList<NetInterface> List();
  NetInterface? Get(string id);                         // null if missing or down
}
interface IPublicIpProbe {                              // leak-check service
  Task<IPAddress> GetViaDefaultRouteAsync(CancellationToken ct);
  Task<IPAddress> GetViaTorrentPathAsync(TorrentNetworkConfig net, CancellationToken ct);
}

// Indexers
interface IIndexer {
  string Id { get; }
  Task<IReadOnlyList<ReleaseResult>> SearchAsync(SearchQuery q, CancellationToken ct);
  Task<IndexerHealth> TestAsync(CancellationToken ct);
}
interface IIndexerHttp {                                // all indexer traffic; FlareSolverr behind it
  Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, IndexerHttpOptions o, CancellationToken ct);
}
interface IDefinitionSource {                           // community definition repo
  Task<IReadOnlyList<DefinitionFile>> FetchAsync(Uri source, CancellationToken ct);
}

// Pure services (no fakes needed)
static class ReleaseParser { static ParsedRelease Parse(string fileName, IReadOnlyList<string> parentDirs); }
interface IQualityClassifier { ReleaseQuality Classify(ParsedRelease r, long sizeBytes, TimeSpan? runtime); }
```

```ts
// Desktop (Electron main process)
interface IPlayer {
  play(opts: { url: string; startSeconds: number; subtitles: SubtitleTrack[]; title: string }): Promise<void>;
  onProgress(cb: (p: { positionSeconds: number; durationSeconds: number; paused: boolean }) => void): void;
  onEnded(cb: (reason: 'eof' | 'quit' | 'error', detail?: string) => void): void;
  stop(): Promise<void>;
}
interface IServerProcess {                              // Local mode
  start(): Promise<{ baseUrl: string }>;
  stop(): Promise<void>;
}
```

### 10.4 Fakes

All fakes live in `server/tests/MediaMan.Fakes` (and `desktop/src/fakes` for TS).

- **Clock:** `FakeClock` with a settable `UtcNow` and an `Advance(TimeSpan)` method. All expiry, lockout, scan-interval and seeding-time logic reads `IClock`.
- **TMDB / AniList (`IMetadataProvider`):** `FakeMetadataProvider` loaded from JSON fixtures in `tests/fixtures/metadata/`
  (a fixed set of about 30 movies and shows, including remakes with the same name, anime with absolute numbering, and a show with specials).
  It is deterministic, can inject errors (timeout, 429) per call, and records the queries it receives. The prototype runs against it.
- **fanart.tv (`IArtworkProvider`):** returns fixture artwork URLs, or none.
- **Artwork images (`IImageDownloader`):** `FakeImageDownloader` serves generated solid-color images per URL, can fail per URL, and records the URLs it was asked for.
- **OpenSubtitles (`ISubtitleProvider`):** returns fixture candidates and a fixed `.srt` stream.
- **ffprobe (`IMediaProbe`):** `FakeMediaProbe` maps a file path or name pattern to a fixture ProbeResult, so tests use empty placeholder files.
  A small set of integration tests runs real ffprobe on tiny generated sample files (skipped if ffprobe is absent).
- **ffmpeg (`ITranscoder`):** records StreamPlans and returns a session with fixed segment URLs. The direct/remux/transcode decision is a pure function and tested directly.
- **Filesystem:** not faked. Paths are injected, and tests use per-test temp directories, including a read-only temp directory to prove external libraries are never written.
- **File watcher (`IFileWatcher`):** `FakeFileWatcher` with a `Raise(FileChange)` method.
- **BitTorrent (`ITorrentEngine`):** `FakeTorrentEngine` is an in-memory state machine. Tests step progress manually
  (`Advance(infoHash, percent)`, `Complete(infoHash)`), it writes declared files into the temp dir on completion, and it records pause/resume calls and network config.
  The real MonoTorrent engine has its own integration tests against a local tracker and seeder in the Phase 0 test build.
- **Network adapters (`INetworkInterfaces`):** `FakeNetworkInterfaces` with settable adapters (add, remove, change address) for kill-switch tests.
- **Leak check (`IPublicIpProbe`):** returns settable IPs per route, so tests can simulate a leak by setting them equal.
- **Indexer sites (`IIndexerHttp`):** `FakeIndexerHttp` serves recorded HTML and JSON fixtures per URL, can inject Cloudflare-style 403/503 and timeouts, and never touches the network.
- **Community definitions (`IDefinitionSource`):** returns fixture YAML definitions from `tests/fixtures/definitions/`.
- **FlareSolverr:** behind `IIndexerHttp`; the fake simulates solved and unsolved challenges.
- **VPN:** covered by `INetworkInterfaces` and `IPublicIpProbe` fakes; no VPN is needed in tests.
- **MPV (`IPlayer`):** `FakePlayer` (TS) records `play` calls and lets tests emit progress and ended events. Real MPV is used only in manual and smoke testing.
- **Server process (`IServerProcess`):** `FakeServerProcess` resolves with a fixed URL pointing at the MSW mock API.
- **Web UI vs. API:** MSW handlers generated from `contracts/openapi.yaml` with fixture data, so UI work never waits on the server.

## 11. UI surfaces

Visual language: dark base with a purple tint (`#0E0B14` background, `#17121F` surfaces), violet accent `#8B5CF6`,
highlight `#C4B5FD`. Artwork-first; hero backgrounds are tinted from each title's dominant color. Rounded corners,
soft shadows, subtle motion (150–250 ms). Every interactive element is reachable by keyboard.

- **Setup wizard:** Superadmin, mode, first library, optional VPN and indexer import. One step per screen, with a progress indicator.
- **Profile picker:** avatars in a grid; PIN entry if a PIN is set.
- **Home:** hero banner (backdrop, logo or title, overview, Play and More info), then rows: Continue Watching, Next Up, Recently Added, genres.
- **Movies / Shows:** poster grid with filters (genre, year, watched, resolution) and sort options.
- **Title detail:** backdrop hero, metadata, Play/Resume, version picker, season tabs with an episode list (stills, progress bars), Fix match, Get (for missing episodes or titles).
- **Search:** instant results from the library; a "Not in library" section from the catalog.
- **Get dialog:** quality chips across the top, a live-ranked result list (quality badges, size, seeders, indexer), dangerous-release flags, failed-source notice.
- **Downloads:** active, seeding and completed lists; VPN status badge always visible; requests tab for approvers ([L], with multiple users).
- **Needs attention:** cards with the file name, parsed guess and candidate posters; one-click confirm or search.
- **Player (desktop):** MPV with the purple Lua OSC (title, progress bar with chapters, audio and subtitle menus, Up next).
- **Player (browser):** custom HTML5 player matching the OSC look.
- **Settings:** Libraries, Playback, Torrents and VPN, Indexers, Quality (tier table, saved profiles), Users and roles, Remote access (the switch and guided setup, trusted proxies, the ban list), About and licences.
- **Remote access badge:** while remote access is on, admins see a "Remote access on" badge in the shell (7.12).

UX rules:
- No dead ends: every error says what happened and offers the next action.
- Long work (scans, downloads) shows live progress and never blocks navigation.
- Destructive actions confirm and name what will be lost.

## 12. Quality and testing

- Every module has its own test project. Tests run against fakes by default and never touch the network.
- **Parser:** a corpus-driven test (`tests/corpus/release-names.json`, ≥300 real-world names covering movies, episodes, multi-episode files,
  season packs, anime absolute numbering, HDR and Dolby Vision tags, and junk). Corpus accuracy must not drop below the recorded baseline.
- **Matcher:** fixture-based tests for remakes, same-name shows, years off by one, specials, and the low-confidence → MatchReview path.
- **Safety-critical tests (must exist before the feature ships):**
  - External libraries are never written to.
  - The kill switch pauses all torrents when the adapter disappears or a leak is detected.
  - The engine refuses to start with no VPN path and no recorded opt-out.
  - Dangerous files are skipped.
  - Every endpoint enforces its permission (a table-driven test over all routes).
  - The Superadmin can't be demoted, deleted or locked out.
  - With remote access off, clients outside the local network are refused, including requests forwarded by a tunnel or proxy on the same machine.
  - IP bans never apply to a trusted proxy or a local-network address.
- **API conformance:** server responses are validated against `contracts/openapi.yaml` in integration tests.
- **Web:** component tests with MSW, and Playwright end-to-end tests for the core flows (setup, browse, play request, Get).
- **Desktop:** `IPlayer` contract tests against `FakePlayer`, plus a manual smoke checklist with real MPV per platform.
- CI runs build, lint and tests for server, web and desktop on Windows and Linux.

## 13. Open questions

- **MonoTorrent may fail the Phase 0 test build.** Required: VPN binding of all traffic including DHT and trackers, hundreds of seeding torrents,
  magnet links, throughput. Blocks the torrent workstream; settled by PLAN 3.1. Assumption: MonoTorrent passes; otherwise a qBittorrent-nox sidecar is used behind `ITorrentEngine`.
- **Assumed: match confidence threshold of 0.85.** Blocks matcher tuning. To be tuned against the corpus.
- **Assumed: seeding defaults of ratio 2.0 or 7 days, whichever comes first.** Blocks managed-import timing.
- **Assumed: transcode cache in a temp folder capped at 10 GB, oldest evicted first.** Blocks the browser playback design (PLAN Stage 5).
- **Assumed: the prototype launches MPV from the server host via a dev-only endpoint.** It's removed when the Electron app lands in the MVP.
  Blocks nothing, but must not ship. It is `POST /dev/playables/{id}/play` (`x-dev-only` in the contract), mapped only in the dev configuration of a Debug build,
  and reaches MPV through the dev-only `IDevPlayer` seam in `MediaMan.Server/Dev`, which is not a §10.3 interface.
- **Assumed: the leak check uses a public "what is my IP" endpoint, configurable in settings.** Which service to use by default is undecided. Blocks VPN gate implementation.
- **Assumed: IP ban thresholds** (a ban after 3 lockouts or 50 failed requests from one address within an hour; 1 h, 24 h, 7 days; strikes expire after 30 days). Blocks nothing; tuned with real use.
- **Exact community definitions URL to offer at first setup.** Blocks the indexer import UX.

## 14. Guidance for AI agents

Agent instructions live in [`AGENTS.md`](../AGENTS.md) (imported by `CLAUDE.md`), with area-specific
rules in `.claude/rules/`. If a rule there contradicts this spec, the spec wins and the rule should be fixed.

## 15. Glossary

- **External library:** a user folder MediaMan reads and indexes but never modifies.
- **Managed storage / managed library:** folders MediaMan owns, where downloads land and get organized.
- **Playable:** something watchable: a movie, or a single episode.
- **Version:** one of several files for the same Playable (for example, 1080p and 2160p).
- **ParsedRelease:** structured data extracted from a file or release name.
- **MatchReview / Needs attention:** a file whose match fell below the confidence threshold, awaiting the user.
- **Direct play / remux / transcode:** sending the file as-is / rewrapping it in a new container without re-encoding / re-encoding streams.
- **Catalog:** all titles known to TMDB, whether or not they're in the library.
- **Get:** the catalog-first download flow with quality chips.
- **Quality chips:** independent filters (resolution, dynamic range, bitrate tier, codec, source, audio, language) applied to search results.
- **Bitrate tier:** Low/Medium/High/Max, derived from size ÷ runtime against the per-resolution table.
- **Indexer:** a torrent search source (a Cardigann definition or a Torznab endpoint).
- **Cardigann definition:** a YAML file describing how to search and parse a torrent site (the format used by Jackett and Prowlarr).
- **Torznab:** a standard torrent search API exposed by Jackett and Prowlarr.
- **FlareSolverr:** an optional proxy that solves Cloudflare challenges for indexers.
- **VPN gate / kill switch:** the rule that torrents run only while the VPN path is healthy.
- **OSC:** MPV's on-screen controller (the player UI), customized in Lua.
- **Superadmin:** the single protected owner account.
- **Local mode / Server mode:** server bound to localhost for one PC / bound to the LAN for other devices.
- **Local network:** loopback, private LAN addresses and the Tailscale tailnet (7.12).
- **Remote access:** the Superadmin-only switch that lets clients outside the local network in, through a trusted proxy over HTTPS (7.12).
