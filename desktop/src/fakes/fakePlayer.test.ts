import { describe, expect, it } from 'vitest';
import { FakePlayer } from './fakePlayer.js';
import { contractPlay, describePlayerContract } from './playerContract.js';

describePlayerContract('FakePlayer', () => {
  const player = new FakePlayer();
  return {
    player,
    progress: (p) => {
      player.emitProgress(p);
      return Promise.resolve();
    },
    end: (reason, detail) => {
      player.emitEnded(reason, detail);
      return Promise.resolve();
    },
    reachCredits: () => {
      player.emitProgress({ positionSeconds: 1290, durationSeconds: 1380, paused: false });
      player.emitCredits();
      return Promise.resolve();
    },
    reachEnd: () => {
      player.emitEndReached();
      return Promise.resolve();
    },
    upNexts: () => player.upNexts,
    chooseUpNext: (choice) => {
      player.emitUpNextChoice(choice);
      return Promise.resolve();
    },
    failNextStart: () => {
      player.failNextPlay();
    },
    launches: () => player.plays.length,
  };
});

describe('FakePlayer', () => {
  it('records what it was asked to play', async () => {
    const player = new FakePlayer();
    const play = { ...contractPlay, startSeconds: 95, subtitles: [{ url: 'http://x/sub.srt', language: 'en' }] };

    await player.play(play);

    expect(player.plays).toEqual([play]);
    expect(player.playing).toBe(true);
  });
});
