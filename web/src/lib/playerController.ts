// What the player OSD (components/player) talks to: the playing file's state in, the viewer's actions out. The desktop
// app's controller goes over the preload bridge; Stage 5's browser player will provide one over a <video> element.
import { useSyncExternalStore } from 'react';
import type { DesktopPlayerBridge, DesktopPlayerCommand, DesktopPlayerState, DesktopUpNextOffer } from './desktop';

/** Everything the OSD draws. */
export interface PlayerView extends DesktopPlayerState {
  /** Whether the player fills the screen; Esc leaves fullscreen before it goes Back. */
  fullscreen: boolean;
  /** The "Up next" countdown on offer; the controller clears it once answered (SPEC §7.4 step 5). */
  upNext?: DesktopUpNextOffer;
  /** A message over the playback, e.g. a lost server. */
  notice?: string;
}

/** What the OSD asks for: a player command, leaving the player (Back), toggling fullscreen, or the answer to Up next. */
export type PlayerAction =
  | DesktopPlayerCommand
  | { type: 'back' }
  | { type: 'toggleFullscreen' }
  | { type: 'upNext'; choice: 'play' | 'cancel' };

export interface PlayerController {
  /** The latest view; null until the player has reported one. Stable between changes. */
  getView: () => PlayerView | null;
  /** Calls back on every change. Returns the unsubscribe. */
  subscribe: (callback: () => void) => () => void;
  act: (action: PlayerAction) => void;
}

/** The desktop overlay's controller: state from the bridge's `onState`, commands through its `command`. */
export function createDesktopPlayerController(player: DesktopPlayerBridge): PlayerController {
  const listeners = new Set<() => void>();
  let view: PlayerView | null = null;
  let unsubscribeBridge: (() => void) | undefined;
  return {
    getView: () => view,
    subscribe(callback) {
      listeners.add(callback);
      unsubscribeBridge ??= player.onState((state) => {
        // The bridge doesn't report fullscreen yet; the overlay window controller does from 2.5.14.
        view = { ...state, fullscreen: false };
        for (const listener of listeners) listener();
      });
      return () => {
        listeners.delete(callback);
        if (listeners.size === 0 && unsubscribeBridge) {
          unsubscribeBridge();
          unsubscribeBridge = undefined;
        }
      };
    },
    act(action) {
      switch (action.type) {
        case 'back':
        case 'toggleFullscreen':
        case 'upNext':
          // The main process's overlay window controller handles these from 2.5.14; nothing opens the overlay before then.
          return;
        default:
          void player.command(action);
      }
    },
  };
}

/** The controller's current view, re-rendering on every change. */
export function usePlayerView(controller: PlayerController): PlayerView | null {
  return useSyncExternalStore(controller.subscribe, controller.getView);
}
