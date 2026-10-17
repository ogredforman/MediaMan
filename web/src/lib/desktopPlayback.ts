// Play on desktop (SPEC §7.4): a playback session from the server, MPV through the desktop bridge, every progress
// event reported back, "Up next" from the credits (or the end of the file) when the server names a next episode, and
// the player's Previous and Next episode buttons, and a lost server shown in the player. It lives outside React so leaving the page
// doesn't stop the reports.
import { useQueryClient, type QueryKey } from '@tanstack/react-query';
import { useEffect, useRef, useSyncExternalStore } from 'react';
import { createPlaybackSession, reportWatchProgress } from '../api/mediaman';
import type { NextEpisode, PlaybackSession, SubtitleTrack, WatchProgress, WatchProgressReport } from '../api/mediaman.schemas';
import { desktopBridge, type DesktopMpvChoice, type DesktopPlayerBridge, type DesktopPlayerEvent, type DesktopPlayOptions } from './desktop';
import { ApiError, apiUrl } from './http';

export type PlaybackFailure =
  /** MPV couldn't be started; the UI offers "Open MPV settings". */
  | { kind: 'mpv'; message: string }
  /** The stream failed twice, the second time on a fresh session. */
  | { kind: 'stream'; detail?: string }
  /** The server didn't give a playback session; `error` is usually an `ApiError`. */
  | { kind: 'session'; error: unknown };

export type DesktopPlaybackState =
  | { status: 'idle' }
  | { status: 'starting'; playableId: string }
  | { status: 'playing'; playableId: string; title: string }
  | { status: 'failed'; playableId: string; failure: PlaybackFailure };

export interface PlaybackApi {
  createSession(playableId: string): Promise<PlaybackSession>;
  reportProgress(playableId: string, report: WatchProgressReport): Promise<WatchProgress>;
}

const serverApi: PlaybackApi = {
  createSession: (playableId) => createPlaybackSession({ playableId, capabilities: { player: 'mpv' } }),
  reportProgress: (playableId, report) => reportWatchProgress(playableId, report),
};

interface Launch {
  launchId: string;
  playableId: string;
  /** The stream already failed once and this launch is the re-query (SPEC §7.4 step 6). */
  retry: boolean;
  /** The last position MPV reported, where a re-query resumes. */
  positionSeconds?: number;
  /** The next episode, from the latest progress the server recorded. */
  next?: NextEpisode | undefined;
  /** Up next is shown at most once per launch; after a cancel, MPV closes at the end of the file. */
  upNext: 'none' | 'offered' | 'cancelled';
  /** The episodes before and after this one, from the session, for the player's Previous and Next episode. */
  previousEpisode?: NextEpisode | undefined;
  nextEpisode?: NextEpisode | undefined;
}

/**
 * Shown in MPV while progress reports can't reach the server, so a viewer in fullscreen MPV learns why the stream will
 * stall once its cache runs out (SPEC §11). Reports go every 10 s, also while paused, so it stays up until they land.
 */
export const serverLostNotice =
  "Can't reach the MediaMan server. Playback goes on from what's buffered; check that the server is running, then press Play in MediaMan.";

/** How long "Up next" counts down before the next episode starts (SPEC §7.4 step 5). */
export const upNextSeconds = 10;

export interface DesktopPlayback {
  getState: () => DesktopPlaybackState;
  subscribe: (listener: () => void) => () => void;
  /** Plays from `startSeconds`; without it, where the server's session resumes. Start over passes `0`. */
  play: (playableId: string, startSeconds?: number) => Promise<void>;
  chooseMpv: () => Promise<DesktopMpvChoice>;
  /** Settles once every progress report sent so far has been answered, so a refetch sees the last position. */
  reported: () => Promise<void>;
}

export function createDesktopPlayback(player: DesktopPlayerBridge, api: PlaybackApi = serverApi): DesktopPlayback {
  let state: DesktopPlaybackState = { status: 'idle' };
  const listeners = new Set<() => void>();
  let current: Launch | undefined;
  // Every launch MPV may still report on, so a replaced launch's last position (sent on quit) isn't lost.
  const launches = new Map<string, Launch>();
  // Reports go one at a time, so the server sees them in MPV's order.
  let reports: Promise<unknown> = Promise.resolve();

  const set = (next: DesktopPlaybackState) => {
    state = next;
    for (const listener of listeners) listener();
  };

  const report = (self: Launch, progress: WatchProgressReport) => {
    // A failed report is dropped; the next one (10 s later at most) carries the newer position anyway.
    reports = reports
      .then(() => api.reportProgress(self.playableId, progress))
      .then((recorded) => {
        self.next = recorded.nextEpisode;
      })
      .catch((error: unknown) => {
        if (current === self && error instanceof ApiError && error.problem.code === 'network.unreachable') {
          player.notice(self.launchId, serverLostNotice).catch(() => undefined);
        }
      });
  };

  // MPV reports progress just before the credits and the end of the file, so waiting for the reports gives the
  // server's answer for this episode.
  const offerUpNext = async (self: Launch, atEnd: boolean): Promise<void> => {
    await reports;
    if (current !== self) return;
    if (self.upNext === 'none' && self.next) {
      self.upNext = 'offered';
      await player.upNext(self.launchId, { title: self.next.title, seconds: upNextSeconds });
    } else if (atEnd && self.upNext !== 'offered') {
      // Nothing to play next, or the viewer cancelled it: close MPV at the end, as for a movie.
      await player.stop();
    }
  };

  const launch = async (playableId: string, retry: boolean, positionSeconds?: number): Promise<void> => {
    const self: Launch = { launchId: crypto.randomUUID(), playableId, retry, upNext: 'none' };
    if (positionSeconds !== undefined) self.positionSeconds = positionSeconds;
    current = self;
    launches.set(self.launchId, self);
    set({ status: 'starting', playableId });

    let session: PlaybackSession;
    try {
      session = await api.createSession(playableId);
    } catch (error) {
      launches.delete(self.launchId);
      if (current !== self) return;
      current = undefined;
      set({ status: 'failed', playableId, failure: { kind: 'session', error } });
      return;
    }
    if (current !== self) return;
    self.previousEpisode = session.previousEpisode;
    self.nextEpisode = session.nextEpisode;

    const result = await player.play(self.launchId, playOptions(session, positionSeconds));
    if (!result.ok) launches.delete(self.launchId);
    if (current !== self) return;
    if (result.ok) {
      set({ status: 'playing', playableId, title: session.title });
    } else {
      current = undefined;
      set({ status: 'failed', playableId, failure: { kind: 'mpv', message: result.message } });
    }
  };

  const onEvent = (event: DesktopPlayerEvent) => {
    const self = launches.get(event.launchId);
    if (!self) return;
    switch (event.type) {
      case 'progress':
        self.positionSeconds = event.positionSeconds;
        report(self, {
          positionSeconds: Math.max(0, event.positionSeconds),
          durationSeconds: Math.max(0, event.durationSeconds),
        });
        return;
      case 'credits':
      case 'endReached':
        offerUpNext(self, event.type === 'endReached').catch(() => undefined);
        return;
      case 'upNext':
        if (event.choice === 'cancel') self.upNext = 'cancelled';
        else if (current === self && self.next) void launch(self.next.playableId, false);
        return;
      case 'episode': {
        const target = event.choice === 'previous' ? self.previousEpisode : self.nextEpisode;
        if (current === self && target) void launch(target.playableId, false);
        return;
      }
      case 'ended':
        break;
    }
    launches.delete(self.launchId);
    if (current !== self) return;
    if (event.reason === 'error' && !self.retry) {
      // The stream URL may have expired or the server restarted: ask for a new session once.
      void launch(self.playableId, true, self.positionSeconds);
      return;
    }
    current = undefined;
    set(
      event.reason === 'error'
        ? {
            status: 'failed',
            playableId: self.playableId,
            failure: event.detail === undefined ? { kind: 'stream' } : { kind: 'stream', detail: event.detail },
          }
        : { status: 'idle' },
    );
  };
  player.onEvent(onEvent);

  return {
    getState: () => state,
    subscribe: (listener) => {
      listeners.add(listener);
      return () => {
        listeners.delete(listener);
      };
    },
    play: (playableId, startSeconds) => launch(playableId, false, startSeconds),
    chooseMpv: () => player.chooseMpv(),
    reported: () => reports.then(() => undefined),
  };
}

/** The session as MPV's launch: absolute URLs on the current server, starting at `positionSeconds` (Start over, a re-query). */
export function playOptions(session: PlaybackSession, positionSeconds?: number): DesktopPlayOptions {
  const preferred = preferredSubtitle(session.subtitles, session.subtitleLanguages);
  return {
    url: absolute(session.url),
    startSeconds: positionSeconds ?? session.resumeSeconds ?? 0,
    title: session.title,
    subtitles: session.subtitles.map((s) => ({
      url: absolute(s.url),
      title: s.fileName,
      ...(s.language === undefined ? {} : { language: s.language }),
      ...(s === preferred ? { isDefault: true } : {}),
    })),
    audioLanguages: session.audioLanguages,
    subtitleLanguages: session.subtitleLanguages,
    ...(session.previousEpisode || session.nextEpisode
      ? {
          episodes: {
            ...(session.previousEpisode ? { previous: session.previousEpisode.title } : {}),
            ...(session.nextEpisode ? { next: session.nextEpisode.title } : {}),
          },
        }
      : {}),
  };
}

/**
 * The external subtitle to turn on: the non-forced one whose language comes earliest in the profile's tags (`pt-BR`
 * matches `pt`). MPV's `--slang` only chooses among the file's own tracks, not ones added after it loads.
 */
function preferredSubtitle(subtitles: SubtitleTrack[], tags: string[]): SubtitleTrack | undefined {
  let best: SubtitleTrack | undefined;
  let bestRank = tags.length;
  for (const s of subtitles) {
    if (s.forced || s.language === undefined) continue;
    const rank = tags.indexOf(s.language.toLowerCase().replace(/-.*/, ''));
    if (rank !== -1 && rank < bestRank) {
      best = s;
      bestRank = rank;
    }
  }
  return best;
}

/** `apiUrl` can be relative to the page (e.g. `/api` behind the Vite proxy); MPV needs a full URL. */
function absolute(path: string): string {
  return new URL(apiUrl(path), window.location.href).toString();
}

const shared = new WeakMap<DesktopPlayerBridge, DesktopPlayback>();

/** The app's desktop playback, or null outside the desktop app (no MPV to play with). */
export function desktopPlayback(): DesktopPlayback | null {
  const player = desktopBridge()?.player;
  if (!player) return null;
  let playback = shared.get(player);
  if (!playback) {
    playback = createDesktopPlayback(player);
    shared.set(player, playback);
  }
  return playback;
}

const idle: DesktopPlaybackState = { status: 'idle' };
const noop = () => () => undefined;

export function useDesktopPlayback(playback: DesktopPlayback | null): DesktopPlaybackState {
  return useSyncExternalStore(playback?.subscribe ?? noop, playback?.getState ?? (() => idle));
}

/** Refetches `queryKey` when a desktop launch stops playing, so Resume and Watched show the progress just recorded. */
export function useRefetchAfterPlayback(queryKey: QueryKey) {
  const playback = desktopPlayback();
  const state = useDesktopPlayback(playback);
  const queryClient = useQueryClient();
  const playing = state.status === 'playing' ? state.playableId : undefined;
  const last = useRef<string | undefined>(undefined);
  useEffect(() => {
    const was = last.current;
    last.current = playing;
    if (!playback || was === undefined || was === playing) return;
    void playback.reported().then(() => queryClient.invalidateQueries({ queryKey }));
  }, [playback, playing, queryClient, queryKey]);
}
