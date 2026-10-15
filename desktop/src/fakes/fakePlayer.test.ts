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
    state: (change) => {
      player.emitState(change);
      return Promise.resolve();
    },
    commandsReceived: () => player.commands.length,
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
    chooseEpisode: (choice) => {
      player.emitEpisodeChoice(choice);
      return Promise.resolve();
    },
    failNextStart: () => {
      player.failNextPlay();
    },
    notices: () => player.notices,
    launches: () => player.launches,
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
