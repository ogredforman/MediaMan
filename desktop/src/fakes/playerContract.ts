// IPlayer contract tests (SPEC §12): the same behaviour from FakePlayer and the real MpvPlayer driven by a fake MPV.
import { beforeEach, describe, expect, it } from 'vitest';
import type { EpisodeChoice, IPlayer, PlayerEndReason, PlayerProgress, PlayOptions, UpNextChoice, UpNextOffer } from '../main/player.js';

/** Drives one player implementation from the outside, the way MPV (or a test) would. */
export interface PlayerHarness {
  player: IPlayer;
  /** Makes the player report `progress`. */
  progress(progress: PlayerProgress): Promise<void>;
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
  failNextStart(): void;
  /** How many times playback actually started. */
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

    beforeEach(() => {
      harness = makeHarness();
      progress = [];
      ended = [];
      marks = [];
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

    it('ends the current playback with quit when another one starts', async () => {
      await harness.player.play(contractPlay);
      await harness.player.play({ ...contractPlay, title: 'Aliens (1986)' });

      expect(ended).toEqual([['quit', undefined]]);
      expect(harness.launches()).toBe(2);
    });

    it('plays again after a playback ended', async () => {
      await harness.player.play(contractPlay);
      await harness.end('eof');
      await harness.player.play(contractPlay);
      await harness.progress({ positionSeconds: 3, durationSeconds: 100, paused: false });

      expect(harness.launches()).toBe(2);
      expect(progress.at(-1)).toEqual({ positionSeconds: 3, durationSeconds: 100, paused: false });
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
