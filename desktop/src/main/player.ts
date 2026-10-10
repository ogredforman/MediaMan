// MPV playback, driven by the main process over MPV's JSON IPC (SPEC §7.4, §10.3).
// MpvPlayer (mpvPlayer.ts) is the real one; tests use FakePlayer from src/fakes.

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
}

export interface PlayerProgress {
  positionSeconds: number;
  durationSeconds: number;
  paused: boolean;
}

export type PlayerEndReason = 'eof' | 'quit' | 'error';

/** The "Up next" countdown shown in the player (SPEC §7.4 step 5). */
export interface UpNextOffer {
  /** The next episode's display title. */
  title: string;
  /** Seconds before it starts. */
  seconds: number;
}

/** What the viewer did with the countdown: it ran out or they chose Play now, or they cancelled it. */
export type UpNextChoice = 'play' | 'cancel';

export interface IPlayer {
  play(opts: PlayOptions): Promise<void>;
  onProgress(cb: (p: PlayerProgress) => void): void;
  onEnded(cb: (reason: PlayerEndReason, detail?: string) => void): void;
  /** Playback entered the final credits; once per launch, after a progress report. */
  onCredits(cb: () => void): void;
  /** Playback reached the end and the player holds the last frame; after a progress report. */
  onEndReached(cb: () => void): void;
  onUpNextChoice(cb: (choice: UpNextChoice) => void): void;
  /** Shows the "Up next" countdown over the current playback; does nothing when nothing plays. */
  showUpNext(offer: UpNextOffer): void;
  stop(): Promise<void>;
}
