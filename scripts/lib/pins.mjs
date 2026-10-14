// The third-party builds the Windows distribution ships (SPEC §9 bundled tools, licensing), pinned by version and
// SHA-256. To update one, change its version, URLs and hashes together (GitHub lists each release asset's digest),
// then the version in desktop/THIRD-PARTY-NOTICES.md; scripts/package-win.mjs refuses to build while they differ.
//
// `source` is the upstream source archive at the same tag. It is attached to each MediaMan release (PLAN 2.5.5) so
// the corresponding source of the GPL builds we redistribute is available from us, not only upstream.

export const jellyfinFfmpeg = {
  name: 'jellyfin-ffmpeg',
  version: 'v8.1.3-1',
  zip: {
    url: 'https://github.com/jellyfin/jellyfin-ffmpeg/releases/download/v8.1.3-1/jellyfin-ffmpeg_8.1.3-1_portable_win64-clang-gpl.zip',
    sha256: '3691f8b2d4c39c511b5faff1c8c4687bd782dac823e63ba69f0755f7ce1af9ce',
  },
  licences: [
    {
      url: 'https://raw.githubusercontent.com/jellyfin/jellyfin-ffmpeg/v8.1.3-1/LICENSE.md',
      sha256: '2e1d16c72fd74e12063776371da757322f8b77589386532f4fd8634bde7de1af',
    },
    {
      url: 'https://raw.githubusercontent.com/jellyfin/jellyfin-ffmpeg/v8.1.3-1/COPYING.GPLv3',
      sha256: '8ceb4b9ee5adedde47b31e975c1d90c73ad27b6b165a1dcd80c7c545eb65b903',
    },
  ],
  source: {
    url: 'https://github.com/jellyfin/jellyfin-ffmpeg/archive/refs/tags/v8.1.3-1.tar.gz',
    sha256: '87bedcefc860cd234cdeddbebdcd3ce45aeedc81d0fbb258ed839ff778be3140',
    name: 'jellyfin-ffmpeg-v8.1.3-1-source.tar.gz',
  },
};

// The official mpv Windows build (MSVC, x86_64): a GPLv2+ build (-Dgpl=true) statically linking FFmpeg and the
// other libraries its Meson subprojects provide. Only mpv.exe and vulkan-1.dll ship; not the .pdb or the .bat files.
export const mpv = {
  name: 'mpv',
  version: 'v0.41.0',
  zip: {
    url: 'https://github.com/mpv-player/mpv/releases/download/v0.41.0/mpv-v0.41.0-x86_64-pc-windows-msvc.zip',
    sha256: '4e197f729f5071c6772f35fffd96e0f36e3e8a044bd9479b136bb09b7c6a80ff',
  },
  files: ['mpv.exe', 'vulkan-1.dll'],
  licences: [
    {
      url: 'https://raw.githubusercontent.com/mpv-player/mpv/v0.41.0/Copyright',
      sha256: 'bfe9ee4cceabcb8ecbfadf208d04156f73d801e6a57369a5606bb8341e204a23',
    },
    {
      url: 'https://raw.githubusercontent.com/mpv-player/mpv/v0.41.0/LICENSE.GPL',
      sha256: 'edaef632cbb643e4e7a221717a6c441a4c1a7c918e6e4d56debc3d8739b233f6',
    },
    {
      url: 'https://raw.githubusercontent.com/mpv-player/mpv/v0.41.0/LICENSE.LGPL',
      sha256: '72b672113d642cbb8ef5dcc76938db801983c56e50b1400ab930f1a64d6dc8d9',
    },
  ],
  source: {
    url: 'https://github.com/mpv-player/mpv/archive/refs/tags/v0.41.0.tar.gz',
    sha256: 'ee21092a5ee427353392360929dc64645c54479aefdb5babc5cfbb5fad626209',
    name: 'mpv-v0.41.0-source.tar.gz',
  },
};
