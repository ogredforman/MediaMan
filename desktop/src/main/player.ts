// MPV playback, driven by the main process over MPV's JSON IPC (SPEC §7.4, §10.3).
// Declarations only; the MPV implementation and FakePlayer arrive with the playback workstream.

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

export interface IPlayer {
  play(opts: PlayOptions): Promise<void>;
  onProgress(cb: (p: PlayerProgress) => void): void;
  onEnded(cb: (reason: PlayerEndReason, detail?: string) => void): void;
  stop(): Promise<void>;
}
