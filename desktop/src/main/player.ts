// MPV playback, driven by the main process over MPV's JSON IPC (SPEC §7.4, §10.3).
// MpvPlayer (mpvPlayer.ts) is the real one; tests use FakePlayer from src/fakes.
import type { Chapter } from './upNext.js';

/** An external subtitle MPV loads alongside the stream (`sub-add`). Desktop-internal, not part of the HTTP contract. */
export interface SubtitleTrack {
  url: string;
  language?: string;
  title?: string;
  isDefault?: boolean;
}

export interface PlayOptions {
  url: string;
  startSeconds: number;
  subtitles: SubtitleTrack[];
  title: string;
  /** The profile's audio language tags in preference order (`--alang`), e.g. `de,ger,deu`. */
  audioLanguages?: string[];
  /** The profile's subtitle language tags in preference order (`--slang`); MPV applies them to embedded tracks. */
  subtitleLanguages?: string[];
  /** An episode's neighbours with a file, by display title; the overlay offers them as Previous and Next episode. */
  episodes?: EpisodeNeighbours;
  /**
   * The app window's native handle (`getNativeWindowHandle()`) to draw into with `--wid` (ADR 0011); left out, MPV
   * opens a window of its own. Set only by the main process, never taken from the UI.
   */
  embedIn?: Buffer;
}

export interface EpisodeNeighbours {
  previous?: string;
  next?: string;
}

/** The viewer asked for the previous or the next episode from the overlay. */
export type EpisodeChoice = 'previous' | 'next';

export interface PlayerProgress {
  positionSeconds: number;
  durationSeconds: number;
  paused: boolean;
}

/** An audio or subtitle track of the current file, embedded or added (MPV's `track-list`). */
export interface PlayerTrack {
  /** MPV's track id, what `selectAudio` and `selectSubtitle` take. */
  id: number;
  language?: string;
  title?: string;
  selected: boolean;
}

/** Everything the player overlay draws (ADR 0011); the whole state each time, never a difference. */
export interface PlayerState {
  positionSeconds: number;
  /** 0 until known. */
  durationSeconds: number;
  /** Where the cached part of the stream ends, for the seek bar's buffered range; 0 until known. */
  bufferedSeconds: number;
  paused: boolean;
  /** Playback is stalled waiting for the network. */
  buffering: boolean;
  /** 0–100. */
  volume: number;
  muted: boolean;
  audioTracks: PlayerTrack[];
  /** None selected means subtitles are off. */
  subtitleTracks: PlayerTrack[];
  chapters: Chapter[];
  title: string;
  episodes?: EpisodeNeighbours;
}

/** What the overlay asks of the player. `episode` is reported through `onEpisodeChoice`, if that neighbour exists. */
export type PlayerCommand =
  | { type: 'togglePause' }
  | { type: 'seekTo'; seconds: number }
  | { type: 'seekBy'; seconds: number }
  | { type: 'setVolume'; volume: number }
  | { type: 'toggleMute' }
  | { type: 'selectAudio'; trackId: number }
  // A null trackId turns subtitles off.
  | { type: 'selectSubtitle'; trackId: number | null }
  | { type: 'episode'; choice: EpisodeChoice };

export type PlayerEndReason = 'eof' | 'quit' | 'error';

/** The "Up next" countdown the overlay shows over the playback (SPEC §7.4 step 5); `PlayerHost` keeps it, not the player. */
export interface UpNextOffer {
  /** The next episode's display title. */
  title: string;
  /** Seconds before it starts. */
  seconds: number;
}

/** What the viewer did with the countdown: it ran out or they chose Play now, or they cancelled it. */
export type UpNextChoice = 'play' | 'cancel';

export interface IPlayer {
  /**
   * Plays `opts`. While a file is still open (playing, or holding its last frame) it is replaced in the same window;
   * the old file's last progress and `ended('quit')` always come before any event from the new one.
   */
  play(opts: PlayOptions): Promise<void>;
  onProgress(cb: (p: PlayerProgress) => void): void;
  onEnded(cb: (reason: PlayerEndReason, detail?: string) => void): void;
  /** Playback entered the final credits; once per launch, after a progress report. */
  onCredits(cb: () => void): void;
  /** Playback reached the end and the player holds the last frame; after a progress report. */
  onEndReached(cb: () => void): void;
  onEpisodeChoice(cb: (choice: EpisodeChoice) => void): void;
  /**
   * The current file's state on every change, the position at most about 4 times a second; never before a file
   * starts, after it ends, or from a file another replaced.
   */
  onState(cb: (state: PlayerState) => void): void;
  /** Acts on the current file; does nothing when nothing plays. */
  command(cmd: PlayerCommand): void;
  stop(): Promise<void>;
}
