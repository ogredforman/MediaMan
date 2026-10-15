// IPlayer contract tests (SPEC §12): the same behaviour from FakePlayer and the real MpvPlayer driven by a fake MPV.
import { beforeEach, describe, expect, it } from 'vitest';
import type { EpisodeChoice, IPlayer, PlayerEndReason, PlayerProgress, PlayerState, PlayOptions, UpNextChoice, UpNextOffer } from '../main/player.js';

/** The parts of the state a harness can make the player report. */
export type PlayerStateChange = Partial<Pick<PlayerState, 'positionSeconds' | 'durationSeconds' | 'bufferedSeconds' | 'paused' | 'buffering' | 'volume' | 'muted'>>;

/** Drives one player implementation from the outside, the way MPV (or a test) would. */
export interface PlayerHarness {
  player: IPlayer;
  /** Makes the player report `progress`. */
  progress(progress: PlayerProgress): Promise<void>;
  /** Makes the player's state change, the way MPV's properties do. */
  state(change: PlayerStateChange): Promise<void>;
  /** How many commands reached the player (MPV for MpvPlayer). */
  commandsReceived(): number;
  /** Ends the current playback the way MPV does at the end of the file or when the stream fails. */
  end(reason: 'eof' | 'error', detail?: string): Promise<void>;
  /** Makes playback enter the final credits. */
  reachCredits(): Promise<void>;
  /** Makes playback reach the end of the file, holding the last frame. */
  reachEnd(): Promise<void>;
  /** The "Up next" countdowns the player was shown, in order. */
  upNexts(): UpNextOffer[];
  /** The viewer answers the countdown. */
  chooseUpNext(choice: UpNextChoice): Promise<void>;
  /** The viewer picks Previous or Next episode. */
  chooseEpisode(choice: EpisodeChoice): Promise<void>;
  /** The notices the player was shown, in order. */
  notices(): string[];
  failNextStart(): void;
  /** How many times a player window was opened. */
  launches(): number;
}

export const contractPlay: PlayOptions = {
  url: 'http://127.0.0.1:8420/media-files/0190-a/stream?sig=x',
  startSeconds: 0,
  subtitles: [],
  title: 'Alien (1979)',
};

export function describePlayerContract(name: string, makeHarness: () => PlayerHarness): void {
  describe(`IPlayer contract: ${name}`, () => {
    let harness: PlayerHarness;
    let progress: PlayerProgress[];
    let ended: [PlayerEndReason, string | undefined][];
    let marks: string[];
    let states: PlayerState[];

    beforeEach(() => {
      harness = makeHarness();
      progress = [];
      ended = [];
      marks = [];
      states = [];
      harness.player.onState((state) => states.push(state));
      harness.player.onCredits(() => marks.push('credits'));
      harness.player.onEndReached(() => marks.push('endReached'));
      harness.player.onUpNextChoice((choice) => marks.push(choice));
      harness.player.onEpisodeChoice((choice) => marks.push(`episode:${choice}`));
      harness.player.onProgress((p) => progress.push(p));
      harness.player.onEnded((reason, detail) => ended.push([reason, detail]));
    });

    it('reports progress while playing', async () => {
      await harness.player.play(contractPlay);
      await harness.progress({ positionSeconds: 12, durationSeconds: 100, paused: true });

      expect(progress.at(-1)).toEqual({ positionSeconds: 12, durationSeconds: 100, paused: true });
    });

    it('ends once at the end of the file', async () => {
      await harness.player.play(contractPlay);
      await harness.end('eof');
      await harness.player.stop();

      expect(ended).toEqual([['eof', undefined]]);
    });

    it('reports the final credits and the end of the file without ending, after a progress report', async () => {
      await harness.player.play(contractPlay);
      await harness.reachCredits();
      const reportsAtCredits = progress.length;
      await harness.reachEnd();

      expect(marks).toEqual(['credits', 'endReached']);
      expect(reportsAtCredits).toBeGreaterThan(0);
      expect(ended).toEqual([]);
    });

    it('shows Up next over the current playback and reports the choices', async () => {
      await harness.player.play(contractPlay);
      harness.player.showUpNext({ title: 'The Office S01E03 · Health Care', seconds: 10 });
      await harness.chooseUpNext('cancel');
      await harness.chooseUpNext('play');

      expect(harness.upNexts()).toEqual([{ title: 'The Office S01E03 · Health Care', seconds: 10 }]);
      expect(marks).toEqual(['cancel', 'play']);
    });

    it('reports the Previous and Next episode choices', async () => {
      await harness.player.play({ ...contractPlay, episodes: { previous: 'Show S01E01 · One', next: 'Show S01E03 · Three' } });
      await harness.chooseEpisode('next');
      await harness.chooseEpisode('previous');

      expect(marks).toEqual(['episode:next', 'episode:previous']);
    });

    it('shows no Up next when nothing plays', async () => {
      harness.player.showUpNext({ title: 'Next', seconds: 10 });
      await harness.player.play(contractPlay);
      await harness.player.stop();
      harness.player.showUpNext({ title: 'Next', seconds: 10 });

      expect(harness.upNexts()).toEqual([]);
    });

    it('ends with an error and its detail when the stream fails', async () => {
      await harness.player.play(contractPlay);
      await harness.end('error', 'loading failed');

      expect(ended).toEqual([['error', 'loading failed']]);
    });

    it('ends with quit on stop, and stopping again does nothing', async () => {
      await harness.player.play(contractPlay);
      await harness.player.stop();
      await harness.player.stop();

      expect(ended).toEqual([['quit', undefined]]);
    });

    it('stopping before anything plays does nothing', async () => {
      await harness.player.stop();

      expect(ended).toEqual([]);
    });

    it('replaces the current playback in the same window, ending it before the new one reports', async () => {
      await harness.player.play(contractPlay);
      await harness.progress({ positionSeconds: 1300, durationSeconds: 1380, paused: false });
      const events: string[] = [];
      harness.player.onProgress((p) => events.push(`progress:${String(p.positionSeconds)}`));
      harness.player.onEnded((reason) => events.push(`ended:${reason}`));

      await harness.player.play({ ...contractPlay, title: 'Aliens (1986)' });
      await harness.progress({ positionSeconds: 4, durationSeconds: 1500, paused: false });

      expect(ended).toEqual([['quit', undefined]]);
      // The old file's late events (MpvPlayer's fake MPV sends a stray position) never reach the new one.
      const after = events.filter((e) => e !== 'progress:1300');
      expect(after.slice(0, 2)).toEqual(['ended:quit', 'progress:4']);
      expect(new Set(after.slice(1))).toEqual(new Set(['progress:4']));
      expect(harness.launches()).toBe(1);
    });

    it('starts fresh on a replaced playback: its credits are reported again', async () => {
      await harness.player.play(contractPlay);
      await harness.reachCredits();
      await harness.player.play({ ...contractPlay, title: 'Aliens (1986)' });
      await harness.reachCredits();

      expect(marks).toEqual(['credits', 'credits']);
    });

    it('shows a notice over the current playback, and none when nothing plays', async () => {
      harness.player.showNotice('Before');
      await harness.player.play(contractPlay);
      harness.player.showNotice("Can't reach the MediaMan server.");
      await harness.player.stop();
      harness.player.showNotice('After');

      expect(harness.notices()).toEqual(["Can't reach the MediaMan server."]);
    });

    it('plays again after a playback ended', async () => {
      await harness.player.play(contractPlay);
      await harness.end('eof');
      await harness.player.play(contractPlay);
      await harness.progress({ positionSeconds: 3, durationSeconds: 100, paused: false });

      expect(harness.launches()).toBe(2);
      expect(progress.at(-1)).toEqual({ positionSeconds: 3, durationSeconds: 100, paused: false });
    });

    it('reports its state with the title and episode neighbours, and every change', async () => {
      const episodes = { previous: 'Show S01E01 · One', next: 'Show S01E03 · Three' };
      await harness.player.play({ ...contractPlay, title: 'Show S01E02 · Two', episodes });
      await harness.state({ positionSeconds: 30, durationSeconds: 1380, bufferedSeconds: 90, paused: true, volume: 80, muted: true, buffering: true });

      expect(states.at(-1)).toMatchObject({
        positionSeconds: 30,
        durationSeconds: 1380,
        bufferedSeconds: 90,
        paused: true,
        buffering: true,
        volume: 80,
        muted: true,
        title: 'Show S01E02 · Two',
        episodes,
      });
    });

    it('reports no state when nothing plays or after the end', async () => {
      await harness.state({ volume: 10 });
      await harness.player.play(contractPlay);
      await harness.end('eof');
      const count = states.length;
      await harness.state({ volume: 20 });

      expect(count).toBeGreaterThan(0);
      expect(states).toHaveLength(count);
    });

    it("starts a replaced playback's state fresh, with nothing from the old file after it", async () => {
      await harness.player.play(contractPlay);
      await harness.state({ positionSeconds: 1300, durationSeconds: 1380 });
      await harness.player.play({ ...contractPlay, title: 'Aliens (1986)' });
      await settle();
      const first = states.findIndex((s) => s.title === 'Aliens (1986)');

      expect(states[first]).toMatchObject({ positionSeconds: 0, durationSeconds: 0 });
      expect(states.slice(first).every((s) => s.title === 'Aliens (1986)' && s.positionSeconds === 0)).toBe(true);
    });

    it('reports a Previous or Next episode command only for a neighbour it has', async () => {
      await harness.player.play({ ...contractPlay, episodes: { next: 'Show S01E03 · Three' } });
      harness.player.command({ type: 'episode', choice: 'previous' });
      harness.player.command({ type: 'episode', choice: 'next' });

      expect(marks).toEqual(['episode:next']);
    });

    it('ignores commands when nothing plays', async () => {
      harness.player.command({ type: 'togglePause' });
      await harness.player.play(contractPlay);
      await settle();
      harness.player.command({ type: 'togglePause' });
      await harness.player.stop();
      harness.player.command({ type: 'seekBy', seconds: 10 });

      expect(harness.commandsReceived()).toBe(1);
    });

    it("rejects when the player can't start, without ending, and can start afterwards", async () => {
      harness.failNextStart();

      await expect(harness.player.play(contractPlay)).rejects.toThrow(/MPV couldn't be started/);
      expect(ended).toEqual([]);

      await harness.player.play(contractPlay);
      expect(harness.launches()).toBe(1);
    });
  });
}

/** Lets pending promise callbacks and immediates run. */
export async function settle(): Promise<void> {
  for (let i = 0; i < 3; i++) {
    await new Promise<void>((resolve) => {
      setImmediate(resolve);
    });
  }
}
