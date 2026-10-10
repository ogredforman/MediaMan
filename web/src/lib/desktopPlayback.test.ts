import { describe, expect, it } from 'vitest';
import type { NextEpisode, PlaybackSession, WatchProgressReport } from '../api/mediaman.schemas';
import { createFakeDesktopPlayer } from '../test/fakeDesktopPlayer';
import { createDesktopPlayback, playOptions, type PlaybackApi } from './desktopPlayback';
import { ApiError } from './http';

const session: PlaybackSession = {
  playableId: 'p1',
  mediaFileId: 'f1',
  method: 'directStream',
  url: '/media-files/f1/stream?sid=s&exp=1&sig=a',
  title: 'Alien (1979)',
  resumeSeconds: 300,
  subtitles: [{ url: '/media-files/f1/subtitles/ab?sid=s&exp=1&sig=b', format: 'srt', fileName: 'Alien.en.srt', language: 'en', forced: false }],
};

/** A server answering sessions in order (the last one repeats) and recording progress reports; `server.next` is the next episode it names. */
function fakeApi(...sessions: (PlaybackSession | Error)[]) {
  const reports: [string, WatchProgressReport][] = [];
  const server: { next: NextEpisode | undefined } = { next: undefined };
  const created: string[] = [];
  const api: PlaybackApi = {
    createSession: (playableId) => {
      created.push(playableId);
      const next = sessions.length > 1 ? sessions.shift() : sessions[0];
      return next instanceof Error || next === undefined ? Promise.reject(next ?? new Error('none')) : Promise.resolve(next);
    },
    reportProgress: (playableId, report) => {
      reports.push([playableId, report]);
      return Promise.resolve({ playableId, ...report, watched: false, ...(server.next ? { nextEpisode: server.next } : {}) });
    },
  };
  return { api, reports, created, server };
}

const flush = () => new Promise((resolve) => setTimeout(resolve, 0));

describe('playOptions', () => {
  it('makes the URLs absolute and resumes where the server says', () => {
    expect(playOptions(session)).toEqual({
      url: `${window.location.origin}/media-files/f1/stream?sid=s&exp=1&sig=a`,
      startSeconds: 300,
      title: 'Alien (1979)',
      subtitles: [{ url: `${window.location.origin}/media-files/f1/subtitles/ab?sid=s&exp=1&sig=b`, title: 'Alien.en.srt', language: 'en' }],
    });
    expect(playOptions({ ...session, resumeSeconds: undefined }).startSeconds).toBe(0);
    expect(playOptions(session, 1234).startSeconds).toBe(1234);
  });
});

describe('createDesktopPlayback', () => {
  it('creates a session, plays it in MPV and reports every progress event in order', async () => {
    const player = createFakeDesktopPlayer();
    const { api, reports } = fakeApi(session);
    const playback = createDesktopPlayback(player, api);

    await playback.play('p1');
    const launchId = player.lastLaunchId();
    player.emit({ launchId, type: 'progress', positionSeconds: 310, durationSeconds: 7000, paused: false });
    player.emit({ launchId, type: 'progress', positionSeconds: 320, durationSeconds: 0, paused: true });
    await flush();

    expect(playback.getState()).toEqual({ status: 'playing', playableId: 'p1', title: 'Alien (1979)' });
    expect(reports).toEqual([
      ['p1', { positionSeconds: 310, durationSeconds: 7000 }],
      ['p1', { positionSeconds: 320, durationSeconds: 0 }],
    ]);

    player.emit({ launchId, type: 'ended', reason: 'quit' });
    expect(playback.getState()).toEqual({ status: 'idle' });
  });

  it('re-queries the server once when the stream fails, resuming at the last position', async () => {
    const player = createFakeDesktopPlayer();
    const { api, created } = fakeApi(session, { ...session, url: '/media-files/f1/stream?sid=s&exp=2&sig=c' });
    const playback = createDesktopPlayback(player, api);

    await playback.play('p1');
    const first = player.lastLaunchId();
    player.emit({ launchId: first, type: 'progress', positionSeconds: 900, durationSeconds: 7000, paused: false });
    player.emit({ launchId: first, type: 'ended', reason: 'error', detail: 'loading failed' });
    await flush();

    expect(created).toEqual(['p1', 'p1']);
    expect(player.plays[1]?.options).toMatchObject({ url: expect.stringContaining('sig=c') as unknown, startSeconds: 900 });
    expect(playback.getState().status).toBe('playing');

    player.emit({ launchId: player.lastLaunchId(), type: 'ended', reason: 'error', detail: 'loading failed' });
    await flush();

    expect(created).toHaveLength(2);
    expect(playback.getState()).toEqual({
      status: 'failed',
      playableId: 'p1',
      failure: { kind: 'stream', detail: 'loading failed' },
    });
  });

  it("fails with mpv when MPV can't start, and answers the MPV picker", async () => {
    const player = createFakeDesktopPlayer();
    player.failNextPlay("MPV couldn't be started: mpv was not found.");
    player.pickMpv('/opt/mpv');
    const playback = createDesktopPlayback(player, fakeApi(session).api);

    await playback.play('p1');

    expect(playback.getState()).toEqual({
      status: 'failed',
      playableId: 'p1',
      failure: { kind: 'mpv', message: "MPV couldn't be started: mpv was not found." },
    });
    await expect(playback.chooseMpv()).resolves.toBe('/opt/mpv');
  });

  it('fails with session when the server gives no session, without starting MPV', async () => {
    const player = createFakeDesktopPlayer();
    const error = new ApiError({ title: 'Nothing to play here.', status: 404, code: 'playable.not_found' });
    const playback = createDesktopPlayback(player, fakeApi(error).api);

    await playback.play('p1');

    expect(playback.getState()).toEqual({ status: 'failed', playableId: 'p1', failure: { kind: 'session', error } });
    expect(player.plays).toEqual([]);
  });

  it("follows the newest Play, still reporting the replaced launch's last position", async () => {
    const player = createFakeDesktopPlayer();
    const { api, reports } = fakeApi(session, { ...session, playableId: 'p2', title: 'Aliens (1986)' });
    const playback = createDesktopPlayback(player, api);

    await playback.play('p1');
    const first = player.lastLaunchId();
    await playback.play('p2');
    player.emit({ launchId: first, type: 'progress', positionSeconds: 42, durationSeconds: 7000, paused: false });
    player.emit({ launchId: first, type: 'ended', reason: 'quit' });
    await flush();

    expect(reports).toEqual([['p1', { positionSeconds: 42, durationSeconds: 7000 }]]);
    expect(playback.getState()).toEqual({ status: 'playing', playableId: 'p2', title: 'Aliens (1986)' });
  });

  describe('Up next', () => {
    const episode: PlaybackSession = { ...session, playableId: 'e1', title: 'The Office S01E02 · Diversity Day', resumeSeconds: undefined };
    const nextEpisode: NextEpisode = { playableId: 'e2', seasonNumber: 1, episodeNumber: 3, title: 'The Office S01E03 · Health Care' };

    async function playEpisode(next: NextEpisode | undefined) {
      const player = createFakeDesktopPlayer();
      const fake = fakeApi(episode, { ...episode, playableId: 'e2', title: nextEpisode.title });
      fake.server.next = next;
      const playback = createDesktopPlayback(player, fake.api);
      await playback.play('e1');
      const launchId = player.lastLaunchId();
      player.emit({ launchId, type: 'progress', positionSeconds: 1290, durationSeconds: 1380, paused: false });
      return { player, playback, launchId, ...fake };
    }

    it('counts down at the credits with the next episode the server named, then plays it', async () => {
      const { player, playback, launchId, created } = await playEpisode(nextEpisode);

      player.emit({ launchId, type: 'credits' });
      await flush();
      expect(player.upNexts).toEqual([{ launchId, offer: { title: 'The Office S01E03 · Health Care', seconds: 10 } }]);

      player.emit({ launchId, type: 'upNext', choice: 'play' });
      await flush();

      expect(created).toEqual(['e1', 'e2']);
      expect(player.plays).toHaveLength(2);
      expect(playback.getState()).toEqual({ status: 'playing', playableId: 'e2', title: 'The Office S01E03 · Health Care' });
    });

    it('counts down at the end of the file when the credits were never found, leaving MPV open', async () => {
      const { player, launchId } = await playEpisode(nextEpisode);

      player.emit({ launchId, type: 'endReached' });
      await flush();

      expect(player.upNexts).toHaveLength(1);
      expect(player.stopCalls).toBe(0);
    });

    it('offers once, and after a cancel closes MPV at the end of the file without playing on', async () => {
      const { player, launchId, created } = await playEpisode(nextEpisode);

      player.emit({ launchId, type: 'credits' });
      await flush();
      player.emit({ launchId, type: 'upNext', choice: 'cancel' });
      player.emit({ launchId, type: 'endReached' });
      await flush();

      expect(player.upNexts).toHaveLength(1);
      expect(player.stopCalls).toBe(1);
      expect(created).toEqual(['e1']);
    });

    it('closes MPV at the end of a movie or the last episode, and shows nothing at its credits', async () => {
      const { player, launchId } = await playEpisode(undefined);

      player.emit({ launchId, type: 'credits' });
      await flush();
      expect(player.stopCalls).toBe(0);

      player.emit({ launchId, type: 'endReached' });
      await flush();

      expect(player.upNexts).toEqual([]);
      expect(player.stopCalls).toBe(1);
    });

    it('ignores Up next from a launch the viewer has replaced', async () => {
      const { player, playback, launchId, created } = await playEpisode(nextEpisode);
      await flush();
      await playback.play('e1');

      player.emit({ launchId, type: 'credits' });
      player.emit({ launchId, type: 'upNext', choice: 'play' });
      await flush();

      expect(player.upNexts).toEqual([]);
      expect(created).toEqual(['e1', 'e1']);
    });
  });
});
