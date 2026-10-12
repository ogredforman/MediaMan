import { useState, type ReactNode } from 'react';
import { desktopPlayback, useDesktopPlayback, type DesktopPlayback, type PlaybackFailure } from '../lib/desktopPlayback';
import { ApiError } from '../lib/http';
import { formatPosition } from '../lib/mediaFormat';
import { buttonClass } from './formClasses';

/** What Play starts and the profile's watch state on it; `name` is the episode's, absent for a movie. */
export interface PlayTarget {
  name?: string | undefined;
  resumeSeconds?: number | undefined;
  watched: boolean;
}

/** Play: MPV through the desktop app; a browser can't play until browser playback lands (SPEC §7.4). */
export function PlayButton({ playableId, target }: { playableId: string; target: PlayTarget }) {
  const playback = desktopPlayback();
  return playback ? (
    <DesktopPlayButton playback={playback} playableId={playableId} target={target} />
  ) : (
    <BrowserPlayButton target={target} />
  );
}

/**
 * Play, or "Resume from 23:15" and "Start over" when the profile stopped part-way (SPEC §7.4, §11). Resume starts
 * where the server's session says (`startSeconds` undefined); Start over starts at 0.
 */
function PlayControl({
  target,
  disabled,
  onPlay,
  children,
}: {
  target: PlayTarget;
  disabled: boolean;
  onPlay: (startSeconds?: number) => void;
  children?: ReactNode;
}) {
  const { name, resumeSeconds, watched } = target;
  const of = name === undefined ? '' : ` ${name}`;
  const at = resumeSeconds === undefined ? undefined : formatPosition(resumeSeconds);
  return (
    <div className="flex flex-col items-start gap-2">
      <div className="flex flex-wrap items-center gap-3">
        <button
          type="button"
          aria-label={at === undefined ? `Play${of}` : `Resume${of} from ${at}`}
          disabled={disabled}
          onClick={() => {
            onPlay();
          }}
          className="inline-flex items-center gap-2 rounded-card bg-highlight px-5 py-2 font-semibold text-bg shadow-soft transition-colors hover:bg-fg disabled:opacity-60"
        >
          <svg aria-hidden="true" viewBox="0 0 16 16" fill="currentColor" className="h-4 w-4">
            <path d="M4 2.5v11a.5.5 0 0 0 .76.43l9-5.5a.5.5 0 0 0 0-.86l-9-5.5A.5.5 0 0 0 4 2.5Z" />
          </svg>
          {at === undefined ? 'Play' : `Resume from ${at}`}
        </button>
        {at !== undefined && (
          <button
            type="button"
            aria-label={name === undefined ? 'Start over' : `Start ${name} over`}
            disabled={disabled}
            onClick={() => {
              onPlay(0);
            }}
            className={`${buttonClass} bg-surface text-fg shadow-soft`}
          >
            Start over
          </button>
        )}
        {watched && (
          <span className="inline-flex items-center gap-1 text-sm text-fg-muted">
            <svg aria-hidden="true" viewBox="0 0 16 16" fill="currentColor" className="h-4 w-4">
              <path d="M6.2 11.6 2.9 8.3l1-1 2.3 2.3 5.9-5.9 1 1z" />
            </svg>
            Watched
          </span>
        )}
      </div>
      {children}
    </div>
  );
}

function PlayError({ title, detail, action }: { title: string; detail?: string | undefined; action: string }) {
  return (
    <div role="alert" className="flex flex-col gap-1 text-sm">
      <p>{title}</p>
      {detail !== undefined && <p className="text-fg-muted">{detail}</p>}
      <p className="text-fg-muted">{action}</p>
    </div>
  );
}

/** Plays in MPV through the desktop app; the state is the app's, so only the Playable that started it shows it. */
function DesktopPlayButton({ playback, playableId, target }: { playback: DesktopPlayback; playableId: string; target: PlayTarget }) {
  const state = useDesktopPlayback(playback);
  const [picked, setPicked] = useState<string | null>(null);
  const [pickFailed, setPickFailed] = useState(false);
  const mine = state.status !== 'idle' && state.playableId === playableId;
  return (
    <PlayControl
      target={target}
      disabled={mine && state.status === 'starting'}
      onPlay={(startSeconds) => {
        setPicked(null);
        setPickFailed(false);
        void playback.play(playableId, startSeconds);
      }}
    >
      {mine && state.status === 'starting' && (
        <p role="status" className="text-sm text-fg-muted">
          Starting MPV…
        </p>
      )}
      {mine && state.status === 'playing' && (
        <p role="status" className="text-sm text-fg-muted">
          Playing {state.title} in MPV.
        </p>
      )}
      {mine && state.status === 'failed' && picked !== null && (
        <p role="status" className="text-sm text-fg-muted">
          MPV is set to {picked}. Press Play to try again.
        </p>
      )}
      {mine && state.status === 'failed' && picked === null && (
        <PlaybackFailureAlert
          failure={state.failure}
          onOpenMpvSettings={() => {
            setPickFailed(false);
            playback.chooseMpv().then(setPicked, () => {
              setPickFailed(true);
            });
          }}
        />
      )}
      {mine && state.status === 'failed' && pickFailed && (
        <PlayError title="Couldn't save the MPV choice." action="Press Open MPV settings to try again." />
      )}
    </PlayControl>
  );
}

function PlaybackFailureAlert({ failure, onOpenMpvSettings }: { failure: PlaybackFailure; onOpenMpvSettings: () => void }) {
  if (failure.kind === 'mpv') {
    return (
      <div className="flex flex-col items-start gap-2">
        <PlayError
          title="MPV couldn't start."
          detail={failure.message}
          action="Install MPV, or choose the MPV program in MPV settings, then press Play again."
        />
        <button type="button" className={buttonClass} onClick={onOpenMpvSettings}>
          Open MPV settings
        </button>
      </div>
    );
  }
  if (failure.kind === 'stream') {
    return (
      <PlayError
        title="MPV couldn't play this file."
        detail={failure.detail}
        action="Check that the server is running and the file is still there, then press Play again."
      />
    );
  }
  const problem = failure.error instanceof ApiError ? failure.error.problem : undefined;
  return (
    <PlayError
      title={problem?.title ?? "Couldn't start playback."}
      detail={problem?.detail}
      action={problem?.action ?? 'Check that the server is running, then press Play again.'}
    />
  );
}

/** In a plain browser, Play says where it works instead of doing nothing. */
function BrowserPlayButton({ target }: { target: PlayTarget }) {
  const [pressed, setPressed] = useState(false);
  return (
    <PlayControl
      target={target}
      disabled={false}
      onPlay={() => {
        setPressed(true);
      }}
    >
      {pressed && (
        <PlayError
          title="Play works in the MediaMan desktop app."
          detail="This browser can't play titles yet."
          action="Open this title in the desktop app and press Play there."
        />
      )}
    </PlayControl>
  );
}
