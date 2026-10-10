import { describe, expect, it } from 'vitest';
import { FakePlayer } from '../fakes/fakePlayer.js';
import { PlayerStartError } from './mpvPlayer.js';
import type { PlayOptions } from './player.js';
import { PlayerHost, type PlayerEvent } from './playerHost.js';

const opts: PlayOptions = { url: 'http://127.0.0.1:8420/media-files/1/stream?sig=x', startSeconds: 0, subtitles: [], title: 'Alien (1979)' };

function setUp(mpvPath = 'mpv') {
  const players: FakePlayer[] = [];
  const paths: string[] = [];
  const events: PlayerEvent[] = [];
  const host = new PlayerHost(
    (path) => {
      paths.push(path);
      const player = new FakePlayer();
      players.push(player);
      return player;
    },
    () => Promise.resolve(mpvPath),
  );
  const send = (event: PlayerEvent) => {
    events.push(event);
  };
  return { host, players, paths, events, send };
}

describe('PlayerHost', () => {
  it('plays with the current MPV and tags its events with the launch id', async () => {
    const { host, players, paths, events, send } = setUp('/opt/mpv');

    await expect(host.play('a', opts, send)).resolves.toEqual({ ok: true });
    players[0]?.emitProgress({ positionSeconds: 10, durationSeconds: 100, paused: false });
    players[0]?.emitEnded('error', 'loading failed');

    expect(paths).toEqual(['/opt/mpv']);
    expect(players[0]?.plays).toEqual([opts]);
    expect(events).toEqual([
      { launchId: 'a', type: 'progress', positionSeconds: 10, durationSeconds: 100, paused: false },
      { launchId: 'a', type: 'ended', reason: 'error', detail: 'loading failed' },
    ]);
  });

  it('stops the current launch before the next one, so its quit carries the old id', async () => {
    const { host, players, events, send } = setUp();

    await host.play('a', opts, send);
    await host.play('b', opts, send);
    players[1]?.emitEnded('eof');

    expect(players[0]?.stopCalls).toBeGreaterThan(0);
    expect(events).toEqual([
      { launchId: 'a', type: 'ended', reason: 'quit' },
      { launchId: 'b', type: 'ended', reason: 'eof' },
    ]);
  });

  it('runs overlapping plays one after the other, leaving only the newest playing', async () => {
    const { host, players, events, send } = setUp();

    await Promise.all([host.play('a', opts, send), host.play('b', opts, send)]);

    expect(players.map((p) => p.playing)).toEqual([false, true]);
    expect(events).toEqual([{ launchId: 'a', type: 'ended', reason: 'quit' }]);
  });

  it("answers mpvStart when MPV can't start", async () => {
    const { host, send } = setUp();
    const failing = new FakePlayer();
    failing.failNextPlay(new PlayerStartError("MPV couldn't be started: mpv was not found."));
    const failingHost = new PlayerHost(
      () => failing,
      () => Promise.resolve('mpv'),
    );

    await expect(failingHost.play('a', opts, send)).resolves.toEqual({
      ok: false,
      error: 'mpvStart',
      message: "MPV couldn't be started: mpv was not found.",
    });
    await expect(host.play('b', opts, send)).resolves.toEqual({ ok: true });
  });

  it('stop ends the current launch, and a second stop does nothing', async () => {
    const { host, events, send } = setUp();

    await host.play('a', opts, send);
    await host.stop();
    await host.stop();

    expect(events).toEqual([{ launchId: 'a', type: 'ended', reason: 'quit' }]);
  });
});
