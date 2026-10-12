import type { ApiError } from '../lib/http';
import { useMarkWatched, type WatchedTarget } from '../lib/watched';
import { buttonClass } from './formClasses';

function CheckIcon({ className }: { className: string }) {
  return (
    <svg aria-hidden="true" viewBox="0 0 16 16" fill="currentColor" className={className}>
      <path d="M6.2 11.6 2.9 8.3l1-1 2.3 2.3 5.9-5.9 1 1z" />
    </svg>
  );
}

/** The watched marker on a poster or still: a check, read out as "Watched". */
export function WatchedBadge() {
  return (
    <span className="absolute top-2 left-2 inline-flex h-6 w-6 items-center justify-center rounded-full bg-highlight text-bg shadow-soft">
      <CheckIcon className="h-4 w-4" />
      <span className="sr-only">Watched</span>
    </span>
  );
}

function WatchedError({ error }: { error: ApiError }) {
  const { problem } = error;
  return (
    <div role="alert" className="flex flex-col gap-1 text-sm">
      <p className="text-highlight">{problem.title}</p>
      {problem.action !== undefined && <p className="text-fg-muted">{problem.action}</p>}
    </div>
  );
}

/**
 * Mark watched or unwatched for the session's profile (SPEC §8 browsing). `name` says what it marks ("Pilot",
 * "Season 1"); `overlay` is the round icon button that sits on a card's poster, shown on hover and on focus (always
 * on touch screens, which can't hover).
 */
export function WatchedButton({
  target,
  watched,
  name,
  overlay = false,
  tabIndex,
}: {
  target: WatchedTarget;
  watched: boolean;
  name: string;
  overlay?: boolean;
  tabIndex?: number;
}) {
  const { mark, isPending, error } = useMarkWatched();
  const label = `Mark ${name} ${watched ? 'unwatched' : 'watched'}`;
  const onClick = () => {
    mark(target, !watched);
  };

  if (overlay) {
    return (
      <>
        <button
          type="button"
          aria-label={label}
          title={label}
          tabIndex={tabIndex}
          disabled={isPending}
          onClick={onClick}
          className="absolute top-2 right-2 inline-flex h-8 w-8 items-center justify-center rounded-full bg-bg/80 text-highlight opacity-0 shadow-soft transition-opacity duration-(--motion-fast) group-focus-within:opacity-100 group-hover:opacity-100 hover:text-fg [@media(hover:none)]:opacity-100 focus-visible:opacity-100 focus-visible:outline-2 focus-visible:outline-highlight disabled:opacity-50"
        >
          {watched ? <span aria-hidden="true">↺</span> : <CheckIcon className="h-4 w-4" />}
        </button>
        {error && (
          <div className="mt-2">
            <WatchedError error={error} />
          </div>
        )}
      </>
    );
  }

  return (
    <div className="flex flex-col items-start gap-2">
      <button
        type="button"
        aria-label={label}
        disabled={isPending}
        onClick={onClick}
        className={`${buttonClass} inline-flex items-center gap-2 bg-surface text-fg shadow-soft`}
      >
        {!watched && <CheckIcon className="h-4 w-4" />}
        {watched ? 'Mark unwatched' : 'Mark watched'}
      </button>
      {error && <WatchedError error={error} />}
    </div>
  );
}
