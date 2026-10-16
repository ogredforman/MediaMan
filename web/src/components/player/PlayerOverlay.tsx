import { useCallback, useEffect, useRef, useState } from 'react';
import { formatPosition } from '../../lib/mediaFormat';
import { usePlayerView, type PlayerAction, type PlayerController, type PlayerView } from '../../lib/playerController';
import { clampVolume, iconButtonClass, idleMs, skipBackSeconds, skipForwardSeconds, volumeStep } from './controls';
import { BackIcon, FullscreenIcon, NextIcon, PauseIcon, PlayIcon, PreviousIcon, SkipIcon } from './playerIcons';
import { SeekBar } from './SeekBar';
import { VolumeControl } from './VolumeControl';

/**
 * The player's on-screen controls (SPEC §11), drawn over the video: a top bar with Back and the title, and a bottom
 * panel with the seek bar, transport, time, volume and fullscreen. Controls and the cursor hide after `idleMs` without
 * input while playing. Keys: Space/K play or pause, ←/→ skip, ↑/↓ volume, M mute, F fullscreen, Esc leaves fullscreen
 * and then goes Back, N/P next and previous episode; a double-click toggles fullscreen.
 */
export function PlayerOverlay({ controller }: { controller: PlayerController }) {
  const view = usePlayerView(controller);
  const [idle, setIdle] = useState(false);
  const idleTimer = useRef<number | undefined>(undefined);

  const armIdle = useCallback(() => {
    window.clearTimeout(idleTimer.current);
    idleTimer.current = window.setTimeout(() => {
      setIdle(true);
    }, idleMs);
  }, []);

  // Input anywhere in the window counts, including over the video where there is no control to take it.
  useEffect(() => {
    const wake = () => {
      setIdle(false);
      armIdle();
    };
    const onKey = (event: KeyboardEvent) => {
      wake();
      const action = keyAction(event, controller.getView());
      if (action === undefined) return;
      event.preventDefault();
      controller.act(action);
    };
    const onDoubleClick = (event: MouseEvent) => {
      if (event.target instanceof Element && event.target.closest('button, input, [role="slider"]')) return;
      controller.act({ type: 'toggleFullscreen' });
    };
    armIdle();
    window.addEventListener('keydown', onKey);
    window.addEventListener('mousemove', wake);
    window.addEventListener('mousedown', wake);
    window.addEventListener('dblclick', onDoubleClick);
    return () => {
      window.clearTimeout(idleTimer.current);
      window.removeEventListener('keydown', onKey);
      window.removeEventListener('mousemove', wake);
      window.removeEventListener('mousedown', wake);
      window.removeEventListener('dblclick', onDoubleClick);
    };
  }, [controller, armIdle]);

  const act = (action: PlayerAction) => {
    controller.act(action);
  };
  // Paused, the controls stay up so the viewer sees where they are.
  const hidden = idle && view !== null && !view.paused;
  const fade = `transition-opacity duration-(--motion-normal) ${hidden ? 'pointer-events-none opacity-0' : 'opacity-100'}`;

  return (
    <div
      data-controls={hidden ? 'hidden' : 'shown'}
      className={`fixed inset-0 flex flex-col justify-between overflow-hidden text-fg select-none ${hidden ? 'cursor-none' : ''}`}
    >
      <header className={`flex items-center gap-3 bg-linear-to-b from-bg/90 to-bg/0 px-4 pt-4 pb-16 ${fade}`}>
        <button type="button" className={iconButtonClass} aria-label="Back" title="Back (Esc)" onClick={() => { act({ type: 'back' }); }}>
          <BackIcon />
        </button>
        {view && <h1 className="truncate text-lg font-semibold [text-shadow:0_1px_4px_rgb(0_0_0/0.6)]">{view.title}</h1>}
      </header>

      {(view === null || view.buffering) && (
        <p role="status" className="self-center rounded-card bg-bg/80 px-4 py-2 text-sm shadow-soft">
          {view === null ? 'Starting playback…' : 'Buffering…'}
        </p>
      )}

      {view && <BottomPanel view={view} act={act} className={fade} />}
    </div>
  );
}

function BottomPanel({ view, act, className }: { view: PlayerView; act: (action: PlayerAction) => void; className: string }) {
  const { previous, next } = view.episodes ?? {};
  return (
    <div className={`bg-linear-to-t from-bg/95 via-bg/70 to-bg/0 px-6 pt-20 pb-4 ${className}`}>
      <SeekBar
        positionSeconds={view.positionSeconds}
        durationSeconds={view.durationSeconds}
        bufferedSeconds={view.bufferedSeconds}
        chapters={view.chapters}
        onSeek={(seconds) => { act({ type: 'seekTo', seconds }); }}
      />
      <div className="mt-2 flex items-center gap-1">
        {previous !== undefined && (
          <button type="button" className={iconButtonClass} aria-label={`Previous episode: ${previous}`} title={`Previous episode (P): ${previous}`} onClick={() => { act({ type: 'episode', choice: 'previous' }); }}>
            <PreviousIcon />
          </button>
        )}
        <button type="button" className={iconButtonClass} aria-label={`Back ${String(skipBackSeconds)} seconds`} title={`Back ${String(skipBackSeconds)} s (←)`} onClick={() => { act({ type: 'seekBy', seconds: -skipBackSeconds }); }}>
          <SkipIcon seconds={skipBackSeconds} back />
        </button>
        <button type="button" className={`${iconButtonClass} h-12 w-12`} aria-label={view.paused ? 'Play' : 'Pause'} title={view.paused ? 'Play (Space)' : 'Pause (Space)'} onClick={() => { act({ type: 'togglePause' }); }}>
          {view.paused ? <PlayIcon /> : <PauseIcon />}
        </button>
        <button type="button" className={iconButtonClass} aria-label={`Forward ${String(skipForwardSeconds)} seconds`} title={`Forward ${String(skipForwardSeconds)} s (→)`} onClick={() => { act({ type: 'seekBy', seconds: skipForwardSeconds }); }}>
          <SkipIcon seconds={skipForwardSeconds} back={false} />
        </button>
        {next !== undefined && (
          <button type="button" className={iconButtonClass} aria-label={`Next episode: ${next}`} title={`Next episode (N): ${next}`} onClick={() => { act({ type: 'episode', choice: 'next' }); }}>
            <NextIcon />
          </button>
        )}
        <span className="ml-3 text-sm tabular-nums">
          {formatPosition(view.positionSeconds)}
          {view.durationSeconds > 0 && <span className="text-fg-muted"> / {formatPosition(view.durationSeconds)}</span>}
        </span>
        <div className="ml-auto flex items-center gap-1">
          <VolumeControl
            volume={view.volume}
            muted={view.muted}
            onToggleMute={() => { act({ type: 'toggleMute' }); }}
            onSetVolume={(volume) => { act({ type: 'setVolume', volume }); }}
          />
          <button type="button" className={iconButtonClass} aria-label={view.fullscreen ? 'Exit fullscreen' : 'Fullscreen'} title={view.fullscreen ? 'Exit fullscreen (F)' : 'Fullscreen (F)'} onClick={() => { act({ type: 'toggleFullscreen' }); }}>
            <FullscreenIcon exit={view.fullscreen} />
          </button>
        </div>
      </div>
    </div>
  );
}

/** The OSD's keyboard shortcuts; undefined leaves the key to the browser (and to a focused button or slider). */
function keyAction(event: KeyboardEvent, view: PlayerView | null): PlayerAction | undefined {
  if (event.ctrlKey || event.metaKey || event.altKey) return undefined;
  // Holding a key repeats only seeking and volume, never a toggle.
  const repeatable = ['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown'].includes(event.key);
  if (event.repeat && !repeatable) return undefined;
  const target = event.target;
  // A focused button activates on Space itself; a focused volume slider moves on its own arrow keys.
  const onButton = target instanceof HTMLButtonElement;
  const onRange = target instanceof HTMLInputElement;
  switch (event.key) {
    case 'Escape':
      return view?.fullscreen ? { type: 'toggleFullscreen' } : { type: 'back' };
    case 'f':
    case 'F':
      return { type: 'toggleFullscreen' };
  }
  if (view === null) return undefined;
  switch (event.key) {
    case ' ':
      return onButton ? undefined : { type: 'togglePause' };
    case 'k':
    case 'K':
      return { type: 'togglePause' };
    case 'ArrowLeft':
      return onRange ? undefined : { type: 'seekBy', seconds: -skipBackSeconds };
    case 'ArrowRight':
      return onRange ? undefined : { type: 'seekBy', seconds: skipForwardSeconds };
    case 'ArrowUp':
      return onRange ? undefined : { type: 'setVolume', volume: clampVolume(view.volume + volumeStep) };
    case 'ArrowDown':
      return onRange ? undefined : { type: 'setVolume', volume: clampVolume(view.volume - volumeStep) };
    case 'm':
    case 'M':
      return { type: 'toggleMute' };
    case 'n':
    case 'N':
      return view.episodes?.next !== undefined ? { type: 'episode', choice: 'next' } : undefined;
    case 'p':
    case 'P':
      return view.episodes?.previous !== undefined ? { type: 'episode', choice: 'previous' } : undefined;
    default:
      return undefined;
  }
}
