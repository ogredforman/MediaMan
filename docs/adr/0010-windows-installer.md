# 0010. Windows installer: electron-builder NSIS, pinned official MPV, source archives with each release

- **Status:** Accepted
- **Date:** 2026-10-14
- **PLAN task:** 2.5.4

## Context

Stage 2.5 needs one Windows installer that installs the desktop app, the single-file server with ffprobe and the
web UI (PLAN 2.5.1, 2.5.2b), and MPV with our config and OSC, with no manual dependency installs (SPEC §9 bundled
tools). Installing a newer build over an older one must keep the user's data. MPV and jellyfin-ffprobe are GPL
builds we redistribute, so the distribution must carry their licences and make their corresponding source available
(SPEC §9 licensing). Tests never touch the network (SPEC §10.4); packaging may.

## Options considered

1. **electron-builder, NSIS:** the common Electron packager; one config file, shortcuts, icons, upgrade-in-place
   keyed on the `appId`, and a later path to signing and electron-updater (PLAN 8.2, 8.5). Cons: a large dev
   dependency tree, and build-time downloads of Electron and the NSIS toolset.
2. **Electron Forge with Squirrel.Windows:** installs to a versioned folder and updates itself, but gives little
   control over the install, and the upgrade story is tied to Squirrel.
3. **A hand-written NSIS or WiX installer:** full control, but we'd rebuild what electron-builder already does.

For MPV: the **official mpv release build** (a zip, attached to the mpv GitHub release with its digest) or a
**third-party build** such as shinchiro's (7z archives, newer but untagged, needing a 7z extractor).

## Decision

Use electron-builder (`desktop/electron-builder.json`) to build a **one-click, per-user NSIS installer**: no UAC
prompt, installed in `%LOCALAPPDATA%\Programs\MediaMan`, Start menu and desktop shortcuts, the brand icon on the
installer, shortcuts and exe, and unsigned for now. The `appId` `io.github.ogredforman.mediaman` must never change,
since upgrades find the installed copy by it. The server and MPV are copied outside the asar to
`resources\server` and `resources\mpv`, where `bundledTools` looks.

Ship the **official mpv v0.41.0 MSVC build** (only `mpv.exe` and `vulkan-1.dll`). Every third-party download is pinned
by URL and SHA-256 in `scripts/lib/pins.mjs` and cached in `dist/.cache`; `npm run package:win`
(`scripts/package-win.mjs`) fetches, checks and assembles them. An offline build works with the files already in the
cache, and a hash mismatch fails the build.

`desktop/THIRD-PARTY-NOTICES.md` (installed beside the app with the AGPL `LICENSE.txt`) lists every shipped
component with its version and licence; the GPL builds' licence texts ship beside them. For their source, the
packaging script also collects the upstream source archives of the pinned mpv and jellyfin-ffmpeg tags into
`dist/sources`, and the release (PLAN 2.5.5) attaches them, so the source is available from us as well as upstream.
The packaging script refuses to build when the notices don't name the pinned versions.

## Consequences

- The user's data stays outside the install folder: the server's data directory in `%LOCALAPPDATA%\MediaMan` and the
  app's Electron data in `%APPDATA%\MediaMan` (the packaged app's `productName`; a dev run keeps
  `%APPDATA%\@mediaman\desktop`). Upgrades replace only the install folder, and uninstall doesn't delete app data.
  One-click also means nobody can pick an install folder that holds their data.
- Updating MPV or ffprobe means changing `scripts/lib/pins.mjs` and the notices together.
- The mpv and jellyfin-ffmpeg builds pull in further libraries at build time (Meson subprojects; jellyfin's build
  scripts); their recipes are in the attached source archives, but the dependency sources themselves are not.
- electron-builder downloads Electron, NSIS and 7-Zip into its own cache on the first build (packaging only, never in
  tests). Its dev dependency tree currently carries moderate `npm audit` advisories (build-time only, nothing ships).
- Signing (PLAN 8.2) and auto-update (PLAN 8.5) build on this config.
