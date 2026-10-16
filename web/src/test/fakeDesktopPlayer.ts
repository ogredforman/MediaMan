// A stand-in for the desktop bridge's `player` (desktop/src/preload/index.cts): records launches and lets tests
// send MPV's events. Never starts MPV.
import type { DesktopMpvChoice, DesktopMpvSetting, DesktopPlayerBridge, DesktopPlayerCommand, DesktopPlayerState, DesktopPlayerEvent, DesktopPlayOptions, DesktopPlayResult, DesktopUpNextOffer } from '../lib/desktop';

export interface FakeDesktopPlayer extends DesktopPlayerBridge {
  readonly plays: { launchId: string; options: DesktopPlayOptions }[];
  /** Every "Up next" countdown asked for. */
  readonly upNexts: { launchId: string; offer: DesktopUpNextOffer }[];
  /** Every notice shown in MPV. */
  readonly notices: { launchId: string; text: string }[];
  /** The launch id of the latest play. */
  lastLaunchId(): string;
  /** Makes the next `play` answer that MPV couldn't start. */
  failNextPlay(message: string): void;
  /** What the MPV picker answers; null is a cancel. */
  pickMpv(path: string | null): void;
  /** The next Choose MPV refuses the file with `message`, as the desktop does for a file that isn't MPV. */
  refuseNextPick(message: string): void;
  /** What `mpvSetting` answers until a pick or Use default changes it. */
  setMpvSetting(setting: DesktopMpvSetting): void;
  emit(event: DesktopPlayerEvent): void;
  /** Every command the overlay sent, in order. */
  readonly commands: DesktopPlayerCommand[];
  /** Sends a player state to `onState` listeners, as the desktop does to the overlay. */
  emitState(state: DesktopPlayerState): void;
  stopCalls: number;
}

export function createFakeDesktopPlayer(): FakeDesktopPlayer {
  const listeners = new Set<(event: DesktopPlayerEvent) => void>();
  const stateListeners = new Set<(state: DesktopPlayerState) => void>();
  let failure: string | undefined;
  let picked: string | null = null;
  let refusal: string | undefined;
  let mpv: DesktopMpvSetting = { path: 'mpv', source: 'default' };
  const fake: FakeDesktopPlayer = {
    plays: [],
    upNexts: [],
    notices: [],
    commands: [],
    stopCalls: 0,
    play(launchId, options): Promise<DesktopPlayResult> {
      if (failure !== undefined) {
        const message = failure;
        failure = undefined;
        return Promise.resolve({ ok: false, error: 'mpvStart', message });
      }
      fake.plays.push({ launchId, options });
      return Promise.resolve({ ok: true });
    },
    stop() {
      fake.stopCalls++;
      return Promise.resolve();
    },
    upNext(launchId, offer) {
      fake.upNexts.push({ launchId, offer });
      return Promise.resolve();
    },
    notice(launchId, text) {
      fake.notices.push({ launchId, text });
      return Promise.resolve();
    },
    chooseMpv(): Promise<DesktopMpvChoice> {
      if (refusal !== undefined) {
        const message = refusal;
        refusal = undefined;
        return Promise.resolve({ outcome: 'refused', message });
      }
      if (picked === null) return Promise.resolve({ outcome: 'cancelled' });
      mpv = { path: picked, source: 'picked' };
      return Promise.resolve({ outcome: 'picked', path: picked });
    },
    mpvSetting: () => Promise.resolve(mpv),
    useDefaultMpv() {
      mpv = { path: 'mpv', source: 'default' };
      return Promise.resolve(mpv);
    },
    onEvent(callback) {
      listeners.add(callback);
      return () => {
        listeners.delete(callback);
      };
    },
    command(command) {
      fake.commands.push(command);
      return Promise.resolve();
    },
    onState(callback) {
      stateListeners.add(callback);
      return () => {
        stateListeners.delete(callback);
      };
    },
    lastLaunchId() {
      const last = fake.plays.at(-1);
      if (!last) throw new Error('Nothing was played.');
      return last.launchId;
    },
    failNextPlay(message) {
      failure = message;
    },
    pickMpv(path) {
      picked = path;
    },
    refuseNextPick(message) {
      refusal = message;
    },
    setMpvSetting(setting) {
      mpv = setting;
    },
    emit(event) {
      for (const listener of listeners) listener(event);
    },
    emitState(state) {
      for (const listener of stateListeners) listener(state);
    },
  };
  return fake;
}
