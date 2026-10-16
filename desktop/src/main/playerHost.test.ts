import { afterEach, describe, expect, it, vi } from 'vitest';
import { FakePlayer } from '../fakes/fakePlayer.js';
import { PlayerStartError } from './mpvPlayer.js';
import type { PlayOptions } from './player.js';
import { noticeMs, PlayerHost, type HostState, type PlayerEvent } from './playerHost.js';

const opts: PlayOptions = { url: 'http://127.0.0.1:8420/media-files/1/stream?sig=x', startSeconds: 0, subtitles: [], title: 'Alien (1979)' };

function setUp(initialPath = 'mpv', embedIn?: () => Buffer | undefined) {
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
    embedIn ? { embedIn } : {},
  );
  const states: HostState[] = [];
  host.onState((state) => states.push(state));
  const active: boolean[] = [];
  host.onActive((a) => active.push(a));
  const send = (event: PlayerEvent) => {
    events.push(event);
  };
  const pick = (path: string) => {
    mpvPath = path;
  };
  return { host, players, paths, events, send, pick, states, active };
}

describe('PlayerHost', () => {
  afterEach(() => {
    vi.useRealTimers();
  });

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

  it('forwards the credits, the end of the file and the episode choices', async () => {
    const { host, players, events, send } = setUp();
    await host.play('a', { ...opts, episodes: { previous: 'Show S01E01 · One' } }, send);

    players[0]?.emitCredits();
    players[0]?.emitEndReached();
    host.command({ type: 'episode', choice: 'previous' });

    expect(events).toEqual([
      { launchId: 'a', type: 'credits' },
      { launchId: 'a', type: 'endReached' },
      { launchId: 'a', type: 'episode', choice: 'previous' },
    ]);
  });

  it("puts Up next in the current launch's state, sends the overlay's answer to it and clears the offer", async () => {
    const { host, events, send, states } = setUp();
    const offer = { title: 'Show S01E02 · Two', seconds: 10 };
    host.answerUpNext('play');
    await host.play('a', opts, send);

    host.upNext('old', offer);
    expect(states.at(-1)?.upNext).toBeUndefined();
    host.upNext('a', offer);
    expect(states.at(-1)?.upNext).toEqual(offer);
    host.answerUpNext('cancel');
    host.answerUpNext('play');

    expect(states.at(-1)?.upNext).toBeUndefined();
    expect(events).toEqual([{ launchId: 'a', type: 'upNext', choice: 'cancel' }]);
  });

  it("drops the Up next and notice with the file they were for", async () => {
    const { host, players, send, states } = setUp();
    await host.play('a', opts, send);
    host.upNext('a', { title: 'Next', seconds: 10 });
    host.notice('a', 'Lost');

    await host.play('b', { ...opts, title: 'Next' }, send);
    players[0]?.emitState({ positionSeconds: 3 });

    expect(states.at(-1)).toMatchObject({ title: 'Next', positionSeconds: 3 });
    expect(states.at(-1)?.upNext).toBeUndefined();
    expect(states.at(-1)?.notice).toBeUndefined();
  });

  it('shows a notice on the current launch for noticeMs, longer when it is repeated', async () => {
    vi.useFakeTimers();
    const { host, send, states } = setUp();
    await host.play('a', opts, send);

    host.notice('old', 'Old');
    host.notice('a', "Can't reach the MediaMan server.");
    expect(states.at(-1)?.notice).toBe("Can't reach the MediaMan server.");
    vi.advanceTimersByTime(noticeMs - 2000);
    host.notice('a', "Can't reach the MediaMan server.");
    vi.advanceTimersByTime(noticeMs - 1);
    expect(states.at(-1)?.notice).toBe("Can't reach the MediaMan server.");
    vi.advanceTimersByTime(1);

    expect(states.at(-1)?.notice).toBeUndefined();
    expect(states.some((s) => s.notice === 'Old')).toBe(false);
  });

  it('plays into the window it is given', async () => {
    const handle = Buffer.from([1, 2, 3, 4, 0, 0, 0, 0]);
    const { host, players, send } = setUp('mpv', () => handle);

    await host.play('a', opts, send);

    expect(players[0]?.plays).toEqual([{ ...opts, embedIn: handle }]);
  });

  it('is active from a play until the last launch ends, through replaces, and not after a failed start', async () => {
    const { host, players, send, active, pick } = setUp('/opt/mpv');

    await host.play('a', opts, send);
    await host.play('b', opts, send);
    players[0]?.emitEnded('eof');
    await host.play('c', opts, send);
    await host.stop();
    players[0]?.failNextPlay();
    await host.play('d', opts, send);
    await host.play('e', opts, send);
    pick('/usr/bin/mpv');
    await host.play('f', opts, send);

    // A newly picked MPV stops the old one first, so the overlay hides and shows again around it.
    expect(active).toEqual([true, false, true, false, true, false, true]);
  });

  it('stays inactive when the file ends while it is still starting', async () => {
    class EndsAtOnce extends FakePlayer {
      override async play(o: PlayOptions): Promise<void> {
        await super.play(o);
        this.emitEnded('error', 'MPV quit');
      }
    }
    const host = new PlayerHost(
      () => new EndsAtOnce(),
      () => Promise.resolve('mpv'),
    );
    const active: boolean[] = [];
    host.onActive((a) => active.push(a));

    await expect(host.play('a', opts, () => undefined)).resolves.toEqual({ ok: true });

    expect(active).toEqual([]);
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
