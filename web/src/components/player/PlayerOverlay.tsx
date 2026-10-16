import { useCallback, useEffect, useEffectEvent, useRef, useState } from 'react';
import { formatPosition } from '../../lib/mediaFormat';
import { usePlayerView, type PlayerAction, type PlayerController, type PlayerView } from '../../lib/playerController';
import { clampVolume, iconButtonClass, idleMs, skipBackSeconds, skipForwardSeconds, volumeStep } from './controls';
import { NoticeToast } from './NoticeToast';
import { BackIcon, FullscreenIcon, NextIcon, PauseIcon, PlayIcon, PreviousIcon, SkipIcon } from './playerIcons';
import { SeekBar } from './SeekBar';
import { TrackMenu, type TrackMenuKind } from './TrackMenu';
import { UpNextCard } from './UpNextCard';
import { VolumeControl } from './VolumeControl';

/**
 * The player's on-screen controls (SPEC §11), drawn over the video: a top bar with Back and the title, and a bottom
 * panel with the seek bar, transport, time, audio and subtitle menus, volume and fullscreen; the Up next card and
 * notices sit above it and stay up when the controls hide. Controls and the cursor hide after `idleMs` without input
 * while playing and no menu is open. Keys: Space/K play or pause, ←/→ skip, ↑/↓ volume, M mute, F fullscreen, A and C
 * the audio and subtitle menus, N/P next and previous episode, Enter Play now; Esc closes a menu, then cancels Up next,
 * then leaves fullscreen, then goes Back. A double-click toggles fullscreen.
 */
export function PlayerOverlay({ controller }: { controller: PlayerController }) {
  const view = usePlayerView(controller);
  const [idle, setIdle] = useState(false);
  const idleTimer = useRef<number | undefined>(undefined);
  const [menu, setMenu] = useState<TrackMenuKind | null>(null);
  const upNext = useUpNextAnswer(controller, view);
  const [dismissedNotice, setDismissedNotice] = useState<string | undefined>(undefined);
  // A notice that goes away and comes back shows again.
  if (view?.notice === undefined && dismissedNotice !== undefined) setDismissedNotice(undefined);
  const notice = view?.notice !== undefined && view.notice !== dismissedNotice ? view.notice : undefined;

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
    const onDoubleClick = (event: MouseEvent) => {
      if (event.target instanceof Element && event.target.closest('button, input, [role="slider"]')) return;
      controller.act({ type: 'toggleFullscreen' });
    };
    armIdle();
    window.addEventListener('mousemove', wake);
    window.addEventListener('mousedown', wake);
    window.addEventListener('dblclick', onDoubleClick);
    return () => {
      window.clearTimeout(idleTimer.current);
      window.removeEventListener('mousemove', wake);
      window.removeEventListener('mousedown', wake);
      window.removeEventListener('dblclick', onDoubleClick);
    };
  }, [controller, armIdle]);

  /** A key for the open menu, the Up next card or the menu shortcuts; false leaves it to `keyAction`. */
  const overlayKey = (event: KeyboardEvent): boolean => {
    if (event.ctrlKey || event.metaKey || event.altKey || event.repeat) return false;
    const target = event.target;
    const toggleMenu = (kind: TrackMenuKind, tracks: number) => {
      if (tracks === 0) return false;
      setMenu(menu === kind ? null : kind);
      return true;
    };
    switch (event.key) {
      case 'Escape':
        if (menu !== null) {
          setMenu(null);
          return true;
        }
        if (upNext.offer !== undefined) {
          upNext.answer('cancel');
          return true;
        }
        return false;
      case 'Enter':
        if (upNext.offer === undefined || target instanceof HTMLButtonElement || target instanceof HTMLInputElement) return false;
        upNext.answer('play');
        return true;
      case 'a':
      case 'A':
        return toggleMenu('audio', view?.audioTracks.length ?? 0);
      case 'c':
      case 'C':
        return toggleMenu('subtitle', view?.subtitleTracks.length ?? 0);
      default:
        return false;
    }
  };

  const onKey = useEffectEvent((event: KeyboardEvent) => {
    setIdle(false);
    armIdle();
    if (overlayKey(event)) {
      event.preventDefault();
      return;
    }
    const action = keyAction(event, controller.getView());
    if (action === undefined) return;
    event.preventDefault();
    controller.act(action);
  });
  useEffect(() => {
    const listener = (event: KeyboardEvent) => {
      onKey(event);
    };
    window.addEventListener('keydown', listener);
    return () => {
      window.removeEventListener('keydown', listener);
    };
  }, []);

  const act = (action: PlayerAction) => {
    controller.act(action);
  };
  // Paused, or with a menu open, the controls stay up so the viewer sees where they are.
  const hidden = idle && view !== null && !view.paused && menu === null;
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

      {notice !== undefined && <NoticeToast key={notice} text={notice} onDismiss={() => { setDismissedNotice(notice); }} />}
      {view && upNext.offer !== undefined && <UpNextCard offer={upNext.offer} positionSeconds={view.positionSeconds} onAnswer={upNext.answer} />}

      {view && <BottomPanel view={view} act={act} menu={menu} onMenuChange={setMenu} className={fade} />}
    </div>
  );
}

/**
 * The Up next offer still waiting for an answer, and the one way to answer it: the first answer (countdown, button,
 * key or seek) goes to the controller and hides the card until the controller takes the offer away and makes another.
 */
function useUpNextAnswer(controller: PlayerController, view: PlayerView | null) {
  const offered = view?.upNext !== undefined;
  const [answered, setAnswered] = useState(false);
  const [wasOffered, setWasOffered] = useState(offered);
  // The countdown and a click can both answer before the next render; only the first counts.
  const answeredNow = useRef(false);
  if (offered !== wasOffered) {
    setWasOffered(offered);
    if (!offered) setAnswered(false);
  }
  useEffect(() => {
    if (!offered) answeredNow.current = false;
  }, [offered]);
  const answer = useCallback(
    (choice: 'play' | 'cancel') => {
      if (answeredNow.current) return;
      answeredNow.current = true;
      setAnswered(true);
      controller.act({ type: 'upNext', choice });
    },
    [controller],
  );
  return { offer: answered ? undefined : view?.upNext, answer };
}

interface BottomPanelProps {
  view: PlayerView;
  act: (action: PlayerAction) => void;
  menu: TrackMenuKind | null;
  onMenuChange: (menu: TrackMenuKind | null) => void;
  className: string;
}

function BottomPanel({ view, act, menu, onMenuChange, className }: BottomPanelProps) {
  const { previous, next } = view.episodes ?? {};
  const menuOpenChange = (kind: TrackMenuKind) => (open: boolean) => {
    onMenuChange(open ? kind : null);
  };
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
          {view.audioTracks.length > 0 && (
            <TrackMenu
              kind="audio"
              tracks={view.audioTracks}
              open={menu === 'audio'}
              onOpenChange={menuOpenChange('audio')}
              onSelect={(trackId) => { if (trackId !== null) act({ type: 'selectAudio', trackId }); }}
            />
          )}
          {view.subtitleTracks.length > 0 && (
            <TrackMenu
              kind="subtitle"
              tracks={view.subtitleTracks}
              open={menu === 'subtitle'}
              onOpenChange={menuOpenChange('subtitle')}
              onSelect={(trackId) => { act({ type: 'selectSubtitle', trackId }); }}
            />
          )}
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
