// What the player OSD (components/player) talks to: the playing file's state in, the viewer's actions out. The desktop
// app's controller goes over the preload bridge; Stage 5's browser player will provide one over a <video> element.
import { useSyncExternalStore } from 'react';
import type { DesktopOverlayState, DesktopPlayerBridge, DesktopPlayerCommand } from './desktop';

/**
 * Everything the OSD draws: the player state, whether it fills the screen (Esc leaves fullscreen before it goes Back),
 * the "Up next" countdown on offer (cleared once answered, SPEC §7.4 step 5) and a message such as a lost server.
 */
export type PlayerView = DesktopOverlayState;

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

/**
 * The desktop overlay's controller: state from the bridge's `onState`, commands through its `command`, and Back,
 * fullscreen and Up next through its `overlay` to the main process.
 */
export function createDesktopPlayerController(player: DesktopPlayerBridge): PlayerController {
  const listeners = new Set<() => void>();
  let view: PlayerView | null = null;
  let unsubscribeBridge: (() => void) | undefined;
  return {
    getView: () => view,
    subscribe(callback) {
      listeners.add(callback);
      if (unsubscribeBridge === undefined) {
        unsubscribeBridge = player.onState((state) => {
          view = state;
          for (const listener of listeners) listener();
        });
        // The state may have been sent before this page listened; the main process sends the latest again.
        player.overlay({ type: 'ready' }).catch(logRefused);
      }
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
          player.overlay(action).catch(logRefused);
          return;
        default:
          player.command(action).catch(logRefused);
      }
    },
  };
}

// The main process refuses only what the overlay never sends (a malformed action, or a page that isn't the overlay),
// so there is nothing for the viewer to do about it; it is logged for whoever debugs the page.
function logRefused(error: unknown): void {
  console.error(`The desktop app refused a player request: ${error instanceof Error ? error.message : String(error)}`);
}

/** The controller's current view, re-rendering on every change. */
export function usePlayerView(controller: PlayerController): PlayerView | null {
  return useSyncExternalStore(controller.subscribe, controller.getView);
}
