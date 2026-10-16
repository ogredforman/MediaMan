// A stand-in for the player OSD's controller: records every action and lets tests set the view.
import type { PlayerAction, PlayerController, PlayerView } from '../lib/playerController';

export interface FakePlayerController extends PlayerController {
  /** Every action the OSD asked for, in order. */
  readonly actions: PlayerAction[];
  /** Merges `changes` into the view (starting from `playerView()` if there is none) and notifies the OSD. */
  update: (changes: Partial<PlayerView>) => void;
}

/** A movie 12 minutes into 1h 30m, playing, with chapters and half the volume. */
export function playerView(overrides: Partial<PlayerView> = {}): PlayerView {
  return {
    positionSeconds: 720,
    durationSeconds: 5400,
    bufferedSeconds: 900,
    paused: false,
    buffering: false,
    volume: 50,
    muted: false,
    audioTracks: [],
    subtitleTracks: [],
    chapters: [
      { title: 'Opening', time: 0 },
      { title: 'Middle', time: 2700 },
    ],
    title: 'Alien (1979)',
    fullscreen: false,
    ...overrides,
  };
}

export function createFakePlayerController(initial: PlayerView | null = playerView()): FakePlayerController {
  const listeners = new Set<() => void>();
  let view = initial;
  const fake: FakePlayerController = {
    actions: [],
    getView: () => view,
    subscribe(callback) {
      listeners.add(callback);
      return () => {
        listeners.delete(callback);
      };
    },
    act(action) {
      fake.actions.push(action);
    },
    update(changes) {
      view = { ...(view ?? playerView()), ...changes };
      for (const listener of listeners) listener();
    },
  };
  return fake;
}
