import type { MediaVersion } from '../api/mediaman.schemas';

/** `1h 56m`, or `48m` under an hour. */
export function formatDuration(minutes: number): string {
  const h = Math.floor(minutes / 60);
  const m = Math.round(minutes % 60);
  return h > 0 ? `${String(h)}h ${String(m)}m` : `${String(m)}m`;
}

/** A playback position: `23:15`, or `1:02:05` from an hour on. */
export function formatPosition(seconds: number): string {
  const total = Math.max(0, Math.floor(seconds));
  const h = Math.floor(total / 3600);
  const m = Math.floor((total % 3600) / 60);
  const s = String(total % 60).padStart(2, '0');
  return h > 0 ? `${String(h)}:${String(m).padStart(2, '0')}:${s}` : `${String(m)}:${s}`;
}

/** File size in decimal units, e.g. `4.2 GB`. */
export function formatSize(bytes: number): string {
  const units = ['B', 'KB', 'MB', 'GB', 'TB'];
  let value = bytes;
  let unit = 0;
  while (value >= 1000 && unit < units.length - 1) {
    value /= 1000;
    unit++;
  }
  return `${value.toFixed(unit < 2 ? 0 : 1)} ${units[unit] ?? 'B'}`;
}

/** The usual name for a video's resolution, judged by width first so cropped (scope) films still read as 1080p or 2160p. */
export function resolutionLabel(width: number | undefined, height: number | undefined): string | undefined {
  if (width !== undefined) {
    if (width >= 3200) return '2160p';
    if (width >= 1800) return '1080p';
    if (width >= 1200) return '720p';
  }
  if (height !== undefined) return `${String(height)}p`;
  return undefined;
}

function channelLabel(channels: number): string {
  switch (channels) {
    case 1:
      return 'Mono';
    case 2:
      return 'Stereo';
    case 6:
      return '5.1';
    case 8:
      return '7.1';
    default:
      return `${String(channels)} ch`;
  }
}

/** The quality tags shown for a version, most important first. */
export function versionTags(version: MediaVersion): string[] {
  const tags = [
    resolutionLabel(version.width, version.height),
    version.hdrFormat,
    version.source,
    version.videoCodec?.toUpperCase(),
    version.audioCodec?.toUpperCase(),
    version.audioChannels === undefined ? undefined : channelLabel(version.audioChannels),
    version.releaseGroup,
  ];
  return tags.filter((tag): tag is string => tag !== undefined && tag !== '');
}
