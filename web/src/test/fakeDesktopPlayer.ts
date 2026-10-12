// A stand-in for the desktop bridge's `player` (desktop/src/preload/index.cts): records launches and lets tests
// send MPV's events. Never starts MPV.
import type { DesktopMpvSetting, DesktopPlayerBridge, DesktopPlayerEvent, DesktopPlayOptions, DesktopPlayResult, DesktopUpNextOffer } from '../lib/desktop';

export interface FakeDesktopPlayer extends DesktopPlayerBridge {
  readonly plays: { launchId: string; options: DesktopPlayOptions }[];
  /** Every "Up next" countdown asked for. */
  readonly upNexts: { launchId: string; offer: DesktopUpNextOffer }[];
  /** The launch id of the latest play. */
  lastLaunchId(): string;
  /** Makes the next `play` answer that MPV couldn't start. */
  failNextPlay(message: string): void;
  /** What the MPV picker answers; null is a cancel. */
  pickMpv(path: string | null): void;
  /** What `mpvSetting` answers until a pick or Use default changes it. */
  setMpvSetting(setting: DesktopMpvSetting): void;
  emit(event: DesktopPlayerEvent): void;
  stopCalls: number;
}

export function createFakeDesktopPlayer(): FakeDesktopPlayer {
  const listeners = new Set<(event: DesktopPlayerEvent) => void>();
  let failure: string | undefined;
  let picked: string | null = null;
  let mpv: DesktopMpvSetting = { path: 'mpv', source: 'default' };
  const fake: FakeDesktopPlayer = {
    plays: [],
    upNexts: [],
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
    chooseMpv() {
      if (picked !== null) mpv = { path: picked, source: 'picked' };
      return Promise.resolve(picked);
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
    setMpvSetting(setting) {
      mpv = setting;
    },
    emit(event) {
      for (const listener of listeners) listener(event);
    },
  };
  return fake;
}
