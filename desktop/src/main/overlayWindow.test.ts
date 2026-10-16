import { describe, expect, it } from 'vitest';
import { FakePlayer } from '../fakes/fakePlayer.js';
import { bridgeEvents } from './bridge.js';
import { OverlayWindow, type MainWindowLike, type OverlayWindowLike } from './overlayWindow.js';
import type { PlayOptions } from './player.js';
import { PlayerHost, type HostState } from './playerHost.js';

const opts: PlayOptions = { url: 'http://127.0.0.1:8420/media-files/1/stream?sig=x', startSeconds: 0, subtitles: [], title: 'Alien (1979)' };

function fakeMain() {
  let bounds = { x: 100, y: 50, width: 1280, height: 760 };
  let fullscreen = false;
  const boundsListeners: (() => void)[] = [];
  const fullscreenListeners: (() => void)[] = [];
  const log: string[] = [];
  const main: MainWindowLike = {
    getContentBounds: () => bounds,
    isFullScreen: () => fullscreen,
    setFullScreen: (on) => {
      log.push(on ? 'enterFullscreen' : 'leaveFullscreen');
      fullscreen = on;
      for (const listener of [...boundsListeners, ...fullscreenListeners]) listener();
    },
    focus: () => log.push('focus'),
    isDestroyed: () => false,
    onBoundsChange: (listener) => boundsListeners.push(listener),
    onFullscreenChange: (listener) => fullscreenListeners.push(listener),
  };
  const move = (next: typeof bounds) => {
    bounds = next;
    for (const listener of boundsListeners) listener();
  };
  return { main, move, log };
}

function fakeOverlay() {
  const sent: unknown[] = [];
  const log: string[] = [];
  let destroyed = false;
  const window: OverlayWindowLike & { bounds?: unknown } = {
    webContents: { send: (channel, state) => void sent.push([channel, state]), isDestroyed: () => destroyed },
    setBounds(bounds) {
      window.bounds = bounds;
    },
    show: () => log.push('show'),
    hide: () => log.push('hide'),
    isDestroyed: () => destroyed,
  };
  const destroy = () => {
    destroyed = true;
  };
  return { window, sent, log, destroy };
}

function setUp() {
  const { main, move, log } = fakeMain();
  const overlays: ReturnType<typeof fakeOverlay>[] = [];
  const overlay = new OverlayWindow(main, () => {
    const made = fakeOverlay();
    overlays.push(made);
    return made.window;
  });
  return { overlay, overlays, move, mainLog: log, main };
}

const state = { title: 'Alien (1979)', positionSeconds: 12 } as HostState;

describe('OverlayWindow', () => {
  it('is created on the first show, over the main window content, and kept there as it moves and resizes', () => {
    const { overlay, overlays, move } = setUp();
    expect(overlays).toHaveLength(0);
    expect(overlay.contents()).toBeUndefined();

    overlay.show();
    expect(overlays[0]?.window.bounds).toEqual({ x: 100, y: 50, width: 1280, height: 760 });
    expect(overlays[0]?.log).toEqual(['show']);
    move({ x: 0, y: 0, width: 1920, height: 1080 });
    expect(overlays[0]?.window.bounds).toEqual({ x: 0, y: 0, width: 1920, height: 1080 });

    overlay.hide();
    overlay.show();
    expect(overlays).toHaveLength(1);
    expect(overlay.contents()).toBe(overlays[0]?.window.webContents);
  });

  it('sends the state with the main window fullscreen, again when that changes and when asked', () => {
    const { overlay, overlays } = setUp();
    overlay.setState(state);
    overlay.show();
    overlay.resend();
    overlay.toggleFullscreen();

    expect(overlays[0]?.sent).toEqual([
      [bridgeEvents.playerState, { ...state, fullscreen: false }],
      [bridgeEvents.playerState, { ...state, fullscreen: true }],
    ]);
  });

  it('hides, leaves fullscreen and gives the keyboard back to the main window, and sends no old state after', () => {
    const { overlay, overlays, mainLog } = setUp();
    overlay.show();
    overlay.toggleFullscreen();
    overlay.setState(state);
    overlay.hide();
    overlay.resend();

    expect(overlays[0]?.log).toEqual(['show', 'hide']);
    expect(mainLog).toEqual(['enterFullscreen', 'leaveFullscreen', 'focus']);
    expect(overlays[0]?.sent).toHaveLength(1);
  });

  it('makes a new overlay when the old one was destroyed', () => {
    const { overlay, overlays } = setUp();
    overlay.show();
    overlays[0]?.destroy();
    expect(overlay.contents()).toBeUndefined();

    overlay.show();
    expect(overlays).toHaveLength(2);
  });

  it('follows PlayerHost: shown while launches play, through a replace, and hidden on every end reason', async () => {
    const player = new FakePlayer();
    const host = new PlayerHost(
      () => player,
      () => Promise.resolve('mpv'),
    );
    const { overlay, overlays } = setUp();
    host.onActive((active) => {
      if (active) overlay.show();
      else overlay.hide();
    });
    host.onState((s) => {
      overlay.setState(s);
    });
    const send = () => undefined;

    const ends: (() => Promise<void>)[] = [
      () => {
        player.emitEnded('eof');
        return Promise.resolve();
      },
      () => {
        player.emitEnded('error', 'loading failed');
        return Promise.resolve();
      },
      () => host.stop(),
    ];
    for (const end of ends) {
      await host.play('a', opts, send);
      await host.play('b', { ...opts, title: 'Aliens (1986)' }, send);
      await end();
    }

    expect(overlays[0]?.log).toEqual(['show', 'hide', 'show', 'hide', 'show', 'hide']);
    expect(overlays[0]?.sent.at(-1)).toEqual([bridgeEvents.playerState, expect.objectContaining({ title: 'Aliens (1986)' })]);
  });
});
