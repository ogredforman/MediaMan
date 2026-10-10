import { describe, expect, it } from 'vitest';
import { formatPosition, formatSize, resolutionLabel, versionTags } from './mediaFormat';

describe('formatPosition', () => {
  it('gives minutes and seconds, with hours from an hour on', () => {
    expect(formatPosition(1395)).toBe('23:15');
    expect(formatPosition(65.9)).toBe('1:05');
    expect(formatPosition(0)).toBe('0:00');
    expect(formatPosition(3725)).toBe('1:02:05');
  });
});

describe('version formatting', () => {
  it('names resolutions by width so scope films keep their class', () => {
    expect(resolutionLabel(3840, 1600)).toBe('2160p');
    expect(resolutionLabel(1920, 800)).toBe('1080p');
    expect(resolutionLabel(1280, 536)).toBe('720p');
    expect(resolutionLabel(720, 480)).toBe('480p');
    expect(resolutionLabel(undefined, undefined)).toBeUndefined();
  });

  it('formats sizes and tags', () => {
    expect(formatSize(512)).toBe('512 B');
    expect(formatSize(900_000_000)).toBe('900.0 MB');
    expect(formatSize(21_400_000_000)).toBe('21.4 GB');
    expect(versionTags({ mediaFileId: 'x', fileName: 'a.mkv', sizeBytes: 1, status: 'matched', part: 1, audioChannels: 2 })).toEqual([
      'Stereo',
    ]);
  });
});
