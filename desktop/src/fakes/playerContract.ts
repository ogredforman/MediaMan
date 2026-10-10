// IPlayer contract tests (SPEC §12): the same behaviour from FakePlayer and the real MpvPlayer driven by a fake MPV.
import { beforeEach, describe, expect, it } from 'vitest';
import type { IPlayer, PlayerEndReason, PlayerProgress, PlayOptions } from '../main/player.js';

/** Drives one player implementation from the outside, the way MPV (or a test) would. */
export interface PlayerHarness {
  player: IPlayer;
  /** Makes the player report `progress`. */
  progress(progress: PlayerProgress): Promise<void>;
  /** Ends the current playback the way MPV does at the end of the file or when the stream fails. */
  end(reason: 'eof' | 'error', detail?: string): Promise<void>;
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

    beforeEach(() => {
      harness = makeHarness();
      progress = [];
      ended = [];
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
