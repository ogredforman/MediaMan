import { useEffect, useEffectEvent } from 'react';
import { noticeMs } from './controls';

/** A message over the playback (e.g. a lost server), up for `noticeMs` or until dismissed. Key it by its text. */
export function NoticeToast({ text, onDismiss }: { text: string; onDismiss: () => void }) {
  const dismiss = useEffectEvent(onDismiss);
  useEffect(() => {
    const timer = window.setTimeout(() => {
      dismiss();
    }, noticeMs);
    return () => {
      window.clearTimeout(timer);
    };
  }, []);

  return (
    <div
      role="alert"
      className="absolute top-20 left-1/2 flex w-max max-w-[min(36rem,90vw)] -translate-x-1/2 items-start gap-3 rounded-card bg-surface/95 px-4 py-3 text-sm shadow-soft"
    >
      <p className="py-1">{text}</p>
      <button type="button" className="shrink-0 rounded-card px-3 py-1 transition-colors duration-(--motion-fast) hover:bg-fg/15" onClick={onDismiss}>
        Dismiss
      </button>
    </div>
  );
}
