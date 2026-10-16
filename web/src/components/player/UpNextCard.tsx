import { useEffect, useEffectEvent, useState } from 'react';
import type { DesktopUpNextOffer } from '../../lib/desktop';

/** How often the countdown redraws. */
const tickMs = 250;

interface UpNextCardProps {
  offer: DesktopUpNextOffer;
  positionSeconds: number;
  /** The OSD sends the first answer and unmounts the card. */
  onAnswer: (choice: 'play' | 'cancel') => void;
}

/**
 * The "Up next" card (SPEC §7.4 step 5): the next episode and a countdown that plays it when it runs out, with Play now
 * and Cancel. A seek landing more than a second before where the card appeared cancels it; seeking forward doesn't.
 */
export function UpNextCard({ offer, positionSeconds, onAnswer }: UpNextCardProps) {
  const [from] = useState(positionSeconds);
  const [remaining, setRemaining] = useState(offer.seconds);
  const answer = useEffectEvent(onAnswer);

  useEffect(() => {
    const deadline = Date.now() + offer.seconds * 1000;
    const timer = window.setInterval(() => {
      const left = Math.max(0, (deadline - Date.now()) / 1000);
      setRemaining(left);
      if (left === 0) {
        window.clearInterval(timer);
        answer('play');
      }
    }, tickMs);
    return () => {
      window.clearInterval(timer);
    };
  }, [offer.seconds]);

  useEffect(() => {
    if (positionSeconds < from - 1) answer('cancel');
  }, [positionSeconds, from]);

  const share = offer.seconds > 0 ? remaining / offer.seconds : 0;
  return (
    <section aria-label="Up next" className="absolute right-6 bottom-36 w-80 overflow-hidden rounded-card bg-surface/95 shadow-soft">
      <div className="p-4">
        <p className="text-xs font-semibold tracking-wide text-fg-muted uppercase">Up next</p>
        <p className="mt-1 font-semibold">{offer.title}</p>
        <p className="text-sm text-fg-muted tabular-nums">Starting in {Math.ceil(remaining)} s</p>
        <div className="mt-3 flex gap-2">
          <button
            type="button"
            className="rounded-card bg-accent px-4 py-1.5 text-sm font-semibold text-fg transition-colors duration-(--motion-fast) hover:bg-accent/85"
            title="Play now (Enter)"
            onClick={() => { onAnswer('play'); }}
          >
            Play now
          </button>
          <button
            type="button"
            className="rounded-card px-4 py-1.5 text-sm transition-colors duration-(--motion-fast) hover:bg-fg/15"
            title="Cancel (Esc)"
            onClick={() => { onAnswer('cancel'); }}
          >
            Cancel
          </button>
        </div>
      </div>
      <div className="h-1 bg-fg/15">
        <div className="h-full bg-accent transition-[width] duration-(--motion-slow) ease-linear" style={{ width: `${String(share * 100)}%` }} />
      </div>
    </section>
  );
}
