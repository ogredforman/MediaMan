# MediaMan third-party notices

MediaMan is free software under the GNU Affero General Public License v3.0 (`LICENSE.txt` beside this file).
Its source is at https://github.com/ogredforman/MediaMan.

This Windows distribution also contains the third-party software below, each under its own licence. Paths are
relative to the install folder.

<!-- Maintainers: keep the versions below in step with scripts/lib/pins.mjs, server/Directory.Packages.props and
     package-lock.json when a shipped dependency changes. -->

## Programs shipped as separate executables

MediaMan runs these as separate processes and doesn't link them (SPEC §10.1).

### mpv v0.41.0 (the video player)

- Files: `resources\mpv\mpv.exe`, `resources\mpv\vulkan-1.dll`
- Build: the official Windows build `mpv-v0.41.0-x86_64-pc-windows-msvc.zip` from
  https://github.com/mpv-player/mpv/releases/tag/v0.41.0, unmodified. It is configured with `-Dgpl=true` and
  statically links FFmpeg, libass, libplacebo, LuaJIT and the other libraries its Meson subprojects provide.
- Licence: GNU General Public License v2 or later, with parts under the LGPL v2.1 or later and other licences
  listed in `Copyright`. Texts: `resources\mpv\licenses\mpv\` (`Copyright`, `LICENSE.GPL`, `LICENSE.LGPL`).
- Source: the corresponding source is attached to each MediaMan release
  (`mpv-v0.41.0-source.tar.gz`, https://github.com/ogredforman/MediaMan/releases) and is available upstream at
  https://github.com/mpv-player/mpv/archive/refs/tags/v0.41.0.tar.gz. The build recipe, including the Meson
  subproject versions, is the `.github/workflows/build.yml` workflow in that source.

### jellyfin-ffmpeg v8.1.3-1 (ffprobe, which reads media file details)

- Files: `resources\server\ffprobe.exe`
- Build: `ffprobe.exe` from the official portable build `jellyfin-ffmpeg_8.1.3-1_portable_win64-clang-gpl.zip` from
  https://github.com/jellyfin/jellyfin-ffmpeg/releases/tag/v8.1.3-1, unmodified.
- Licence: GNU General Public License v3 (a GPL build of FFmpeg with Jellyfin's patches). Texts:
  `resources\server\licenses\jellyfin-ffmpeg\` (`LICENSE.md`, `COPYING.GPLv3`).
- Source: the corresponding source is attached to each MediaMan release
  (`jellyfin-ffmpeg-v8.1.3-1-source.tar.gz`, https://github.com/ogredforman/MediaMan/releases) and is available
  upstream at https://github.com/jellyfin/jellyfin-ffmpeg/archive/refs/tags/v8.1.3-1.tar.gz. The build scripts for
  the portable Windows build are in that source.

## The desktop shell

### Electron 44.5.1 and Chromium

- Files: `MediaMan.exe` and the Electron runtime files beside it.
- Licence: MIT (Electron), `LICENSE.electron.txt`; Chromium and its components under the licences in
  `LICENSES.chromium.html`.
- Source: https://github.com/electron/electron/tree/v44.5.1

## The server (`resources\server\MediaMan.Server.exe`)

A self-contained .NET build, which includes:

| Component | Version | Licence |
| --- | --- | --- |
| .NET runtime and ASP.NET Core (Microsoft) | 10.0 | MIT |
| Microsoft.EntityFrameworkCore.Sqlite | 10.0.12 | MIT |
| Microsoft.AspNetCore.Authentication.JwtBearer | 10.0.12 | MIT |
| Microsoft.IdentityModel.JsonWebTokens | 8.19.2 | MIT |
| Konscious.Security.Cryptography.Argon2 | 1.3.1 | MIT |
| StbImageSharp | 2.30.16 | Unlicense or MIT |
| SQLitePCLRaw (bundle_e_sqlite3, core, provider) | 2.1.12 | Apache-2.0 |
| SQLite (e_sqlite3) | bundled with SQLitePCLRaw | Public domain |

The .NET runtime's own third-party notices are at
https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT and
https://github.com/dotnet/aspnetcore/blob/main/THIRD-PARTY-NOTICES.txt. The Apache-2.0 licence text is at
https://www.apache.org/licenses/LICENSE-2.0.

## The web UI (`resources\server\web\`)

| Component | Version | Licence |
| --- | --- | --- |
| react, react-dom | 19.3.0 | MIT |
| scheduler | 0.28.0 | MIT |
| @tanstack/react-query, @tanstack/query-core | 5.104.1 | MIT |
| @tanstack/react-router | 1.170.41 | MIT |
| @tanstack/router-core | 1.171.34 | MIT |
| @tanstack/history | 1.162.4 | MIT |
| @tanstack/react-store, @tanstack/store | 0.11.2 | MIT |
| use-sync-external-store | 1.7.0 | MIT |
| cookie-es | 3.1.1 | MIT |
| seroval, seroval-plugins | 1.6.8 | MIT |
| isbot | 5.2.2 | Unlicense |

## The MIT licence

The components above marked MIT are each distributed under these terms, with their own copyright holders
(Microsoft Corporation and contributors; .NET Foundation and contributors; Meta Platforms, Inc. and affiliates;
Tanner Linsley; GitHub Inc. (Electron); Keef Aragon (Konscious); StbImageSharpTeam; the cookie-es,
seroval and other package authors):

> Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated
> documentation files (the "Software"), to deal in the Software without restriction, including without limitation
> the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to
> permit persons to whom the Software is furnished to do so, subject to the following conditions:
>
> The above copyright notice and this permission notice shall be included in all copies or substantial portions of
> the Software.
>
> THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE
> WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS
> OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
> OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
