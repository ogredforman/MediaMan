import { describe, expect, it } from 'vitest';
import { FakePlayer } from '../fakes/fakePlayer.js';
import { PlayerStartError } from './mpvPlayer.js';
import type { PlayOptions } from './player.js';
import { PlayerHost, type PlayerEvent } from './playerHost.js';

const opts: PlayOptions = { url: 'http://127.0.0.1:8420/media-files/1/stream?sig=x', startSeconds: 0, subtitles: [], title: 'Alien (1979)' };

function setUp(initialPath = 'mpv') {
  let mpvPath = initialPath;
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
  const pick = (path: string) => {
    mpvPath = path;
  };
  return { host, players, paths, events, send, pick };
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

  it('plays the next launch in the same MPV window, so the old quit carries the old id and later events the new one', async () => {
    const { host, players, events, send } = setUp();

    await host.play('a', opts, send);
    await host.play('b', { ...opts, title: 'Aliens (1986)' }, send);
    players[0]?.emitProgress({ positionSeconds: 4, durationSeconds: 100, paused: false });
    players[0]?.emitEnded('eof');

    expect(players).toHaveLength(1);
    expect(players[0]?.launches).toBe(1);
    expect(players[0]?.plays.map((p) => p.title)).toEqual(['Alien (1979)', 'Aliens (1986)']);
    expect(events).toEqual([
      { launchId: 'a', type: 'ended', reason: 'quit' },
      { launchId: 'b', type: 'progress', positionSeconds: 4, durationSeconds: 100, paused: false },
      { launchId: 'b', type: 'ended', reason: 'eof' },
    ]);
  });

  it('opens a new window after the last launch ended, with the next launch id', async () => {
    const { host, players, events, send } = setUp();

    await host.play('a', opts, send);
    players[0]?.emitEnded('error', 'loading failed');
    await host.play('b', opts, send);
    players[0]?.emitEnded('quit');

    expect(players[0]?.launches).toBe(2);
    expect(events).toEqual([
      { launchId: 'a', type: 'ended', reason: 'error', detail: 'loading failed' },
      { launchId: 'b', type: 'ended', reason: 'quit' },
    ]);
  });

  it('stops the current MPV and starts the newly picked one when the MPV path changed', async () => {
    const { host, players, paths, events, send, pick } = setUp('/opt/mpv');

    await host.play('a', opts, send);
    pick('/usr/bin/mpv');
    await host.play('b', opts, send);

    expect(paths).toEqual(['/opt/mpv', '/usr/bin/mpv']);
    expect(players.map((p) => p.playing)).toEqual([false, true]);
    expect(events).toEqual([{ launchId: 'a', type: 'ended', reason: 'quit' }]);
  });

  it('runs overlapping plays one after the other, leaving only the newest playing', async () => {
    const { host, players, events, send } = setUp();

    await Promise.all([host.play('a', opts, send), host.play('b', opts, send)]);

    expect(players).toHaveLength(1);
    expect(players[0]?.plays).toHaveLength(2);
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

  it('forwards the credits, the end of the file, the Up next and episode choices, and shows Up next and notices only on the current launch', async () => {
    const { host, players, events, send } = setUp();
    await host.play('a', opts, send);
    const offer = { title: 'Show S01E02 · Two', seconds: 10 };

    players[0]?.emitCredits();
    players[0]?.emitEndReached();
    host.upNext('old', offer);
    host.upNext('a', offer);
    host.notice('old', 'Old');
    host.notice('a', "Can't reach the MediaMan server.");
    players[0]?.emitUpNextChoice('play');
    players[0]?.emitEpisodeChoice('previous');

    expect(players[0]?.upNexts).toEqual([offer]);
    expect(players[0]?.notices).toEqual(["Can't reach the MediaMan server."]);
    expect(events).toEqual([
      { launchId: 'a', type: 'credits' },
      { launchId: 'a', type: 'endReached' },
      { launchId: 'a', type: 'upNext', choice: 'play' },
      { launchId: 'a', type: 'episode', choice: 'previous' },
    ]);
  });

  it("sends the current file's state to its listeners and its commands to the player, across a newly picked MPV", async () => {
    const { host, players, send, pick } = setUp('/opt/mpv');
    const states: number[] = [];
    host.onState((state) => states.push(state.positionSeconds));

    host.command({ type: 'togglePause' });
    await host.play('a', { ...opts, startSeconds: 5 }, send);
    players[0]?.emitState({ positionSeconds: 6 });
    host.command({ type: 'seekBy', seconds: 30 });
    pick('/usr/bin/mpv');
    await host.play('b', { ...opts, startSeconds: 50 }, send);
    players[0]?.emitState({ positionSeconds: 7 });
    host.command({ type: 'togglePause' });

    expect(states).toEqual([5, 6, 50]);
    expect(players[0]?.commands).toEqual([{ type: 'seekBy', seconds: 30 }]);
    expect(players[1]?.commands).toEqual([{ type: 'togglePause' }]);
  });

  it('stop ends the current launch, and a second stop does nothing', async () => {
    const { host, events, send } = setUp();

    await host.play('a', opts, send);
    await host.stop();
    await host.stop();

    expect(events).toEqual([{ launchId: 'a', type: 'ended', reason: 'quit' }]);
  });
});
