import { useEffect, useRef, useState, type MouseEvent as ReactMouseEvent } from 'react';
import { formatPosition } from '../../lib/mediaFormat';

interface SeekBarProps {
  positionSeconds: number;
  /** 0 until known; the bar can't seek before then. */
  durationSeconds: number;
  bufferedSeconds: number;
  chapters: { title?: string; time: number }[];
  onSeek: (seconds: number) => void;
}

/**
 * The player's progress bar: played and buffered parts, a tick per chapter, the time (and chapter) under the pointer,
 * and click or drag to seek. ←/→ seek through the OSD's keys; Home and End go to the start and the end.
 */
export function SeekBar({ positionSeconds, durationSeconds, bufferedSeconds, chapters, onSeek }: SeekBarProps) {
  const track = useRef<HTMLDivElement>(null);
  const [hover, setHover] = useState<number | null>(null);
  const [dragging, setDragging] = useState<number | null>(null);
  // Where a drag dropped, shown until the player reports a position other than the one it had then.
  const [dropped, setDropped] = useState<{ seconds: number; from: number } | null>(null);
  const endDrag = useRef<(() => void) | undefined>(undefined);
  const known = durationSeconds > 0;

  useEffect(() => () => endDrag.current?.(), []);

  function timeAt(clientX: number): number {
    const rect = track.current?.getBoundingClientRect();
    if (!rect || rect.width <= 0) return 0;
    return clamp((clientX - rect.left) / rect.width) * durationSeconds;
  }

  function percent(seconds: number): string {
    return `${String(known ? clamp(seconds / durationSeconds) * 100 : 0)}%`;
  }

  function startDrag(event: ReactMouseEvent) {
    if (!known || event.button !== 0) return;
    event.preventDefault();
    setDragging(timeAt(event.clientX));
    const move = (e: MouseEvent) => {
      setDragging(timeAt(e.clientX));
    };
    const up = (e: MouseEvent) => {
      endDrag.current?.();
      const seconds = timeAt(e.clientX);
      setDropped({ seconds, from: positionSeconds });
      onSeek(seconds);
    };
    endDrag.current = () => {
      window.removeEventListener('mousemove', move);
      window.removeEventListener('mouseup', up);
      endDrag.current = undefined;
      setDragging(null);
    };
    window.addEventListener('mousemove', move);
    window.addEventListener('mouseup', up);
  }

  const shown = dragging ?? (dropped?.from === positionSeconds ? dropped.seconds : positionSeconds);
  const pointer = dragging ?? hover;
  const chapter = pointer === null ? undefined : chapters.findLast((c) => c.time <= pointer);

  return (
    <div
      role="slider"
      tabIndex={0}
      aria-label="Seek"
      aria-valuemin={0}
      aria-valuemax={Math.floor(durationSeconds)}
      aria-valuenow={Math.floor(shown)}
      aria-valuetext={known ? `${formatPosition(shown)} of ${formatPosition(durationSeconds)}` : formatPosition(shown)}
      className="group relative cursor-pointer rounded-full py-2"
      onMouseDown={startDrag}
      onMouseMove={(e) => {
        if (known) setHover(timeAt(e.clientX));
      }}
      onMouseLeave={() => {
        setHover(null);
      }}
      onKeyDown={(e) => {
        if (!known) return;
        if (e.key === 'Home') onSeek(0);
        else if (e.key === 'End') onSeek(durationSeconds);
        else return;
        e.preventDefault();
      }}
    >
      <div
        ref={track}
        className="relative h-1 rounded-full bg-fg/25 transition-[height] duration-(--motion-fast) group-hover:h-1.5"
      >
        <div data-part="buffered" className="absolute inset-y-0 left-0 rounded-full bg-fg/35" style={{ width: percent(bufferedSeconds) }} />
        <div data-part="played" className="absolute inset-y-0 left-0 rounded-full bg-accent" style={{ width: percent(shown) }} />
        {known &&
          chapters
            .filter((c) => c.time > 0 && c.time < durationSeconds)
            .map((c) => (
              <div key={c.time} data-part="chapter" className="absolute inset-y-0 w-0.5 bg-bg/80" style={{ left: percent(c.time) }} />
            ))}
        <div
          className="absolute top-1/2 h-3 w-3 -translate-x-1/2 -translate-y-1/2 rounded-full bg-highlight opacity-0 transition-opacity duration-(--motion-fast) group-hover:opacity-100 group-focus-visible:opacity-100"
          style={{ left: percent(shown) }}
        />
      </div>
      {pointer !== null && (
        <div
          role="tooltip"
          className="pointer-events-none absolute bottom-full mb-2 -translate-x-1/2 rounded-card bg-surface px-2 py-1 text-xs whitespace-nowrap text-fg shadow-soft"
          style={{ left: percent(pointer) }}
        >
          {chapter?.title && <span className="mr-2 text-fg-muted">{chapter.title}</span>}
          <span className="tabular-nums">{formatPosition(pointer)}</span>
        </div>
      )}
    </div>
  );
}

function clamp(fraction: number): number {
  return Math.min(1, Math.max(0, fraction));
}
