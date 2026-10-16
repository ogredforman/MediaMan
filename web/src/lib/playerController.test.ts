import { describe, expect, it } from 'vitest';
import { createFakeDesktopPlayer } from '../test/fakeDesktopPlayer';
import { playerView } from '../test/fakePlayerController';
import { createDesktopPlayerController } from './playerController';

const { fullscreen: _fullscreen, ...state } = playerView();

describe('createDesktopPlayerController', () => {
  it('has no view until the bridge sends a state, then one per state that stays the same between them', () => {
    const player = createFakeDesktopPlayer();
    const controller = createDesktopPlayerController(player);
    let changes = 0;
    controller.subscribe(() => changes++);
    expect(controller.getView()).toBeNull();

    player.emitState(state);
    const first = controller.getView();
    expect(first).toEqual({ ...state, fullscreen: false });
    expect(controller.getView()).toBe(first);

    player.emitState({ ...state, paused: true });
    expect(controller.getView()?.paused).toBe(true);
    expect(changes).toBe(2);
  });

  it('sends player commands over the bridge, and keeps Back and fullscreen off it until the overlay window exists', () => {
    const player = createFakeDesktopPlayer();
    const controller = createDesktopPlayerController(player);
    controller.act({ type: 'seekBy', seconds: 30 });
    controller.act({ type: 'setVolume', volume: 40 });
    controller.act({ type: 'back' });
    controller.act({ type: 'toggleFullscreen' });
    expect(player.commands).toEqual([
      { type: 'seekBy', seconds: 30 },
      { type: 'setVolume', volume: 40 },
    ]);
  });

  it('listens to the bridge only while someone listens to it', () => {
    const player = createFakeDesktopPlayer();
    const controller = createDesktopPlayerController(player);
    const stopA = controller.subscribe(() => undefined);
    const stopB = controller.subscribe(() => undefined);
    stopA();
    player.emitState(state);
    expect(controller.getView()).not.toBeNull();

    stopB();
    player.emitState({ ...state, title: 'Later' });
    expect(controller.getView()?.title).toBe('Alien (1979)');
  });
});
