import { useEffect, useId, useRef, useState, type ReactNode } from 'react';

/** Can the clicked element take focus itself? Then an outside click leaves focus there. */
function focusesItself(target: EventTarget | null): boolean {
  return target instanceof Element && target.closest('a[href], button, input, select, textarea, [tabindex]') !== null;
}

/**
 * A header disclosure: a button (`aria-expanded`) opening a panel below it. Esc and clicks outside close it; focus
 * goes back to the button unless the click landed on something that takes focus. Tabbing out of it closes it too.
 * Children get `close`.
 */
export function HeaderPopover({
  label,
  button,
  children,
}: {
  /** The button's accessible name. */
  label: string;
  /** What the button shows. */
  button: ReactNode;
  children: (close: () => void) => ReactNode;
}) {
  const [open, setOpen] = useState(false);
  const panelId = useId();
  const root = useRef<HTMLDivElement>(null);
  const trigger = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    if (!open) return;
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key !== 'Escape') return;
      setOpen(false);
      trigger.current?.focus();
    };
    const onClick = (event: MouseEvent) => {
      if (root.current?.contains(event.target as Node)) return;
      setOpen(false);
      if (!focusesItself(event.target)) trigger.current?.focus();
    };
    document.addEventListener('keydown', onKeyDown);
    document.addEventListener('click', onClick, true);
    return () => {
      document.removeEventListener('keydown', onKeyDown);
      document.removeEventListener('click', onClick, true);
    };
  }, [open]);

  return (
    <div
      ref={root}
      className="relative"
      onBlur={(event) => {
        if (event.relatedTarget instanceof Node && !event.currentTarget.contains(event.relatedTarget)) setOpen(false);
      }}
    >
      <button
        ref={trigger}
        type="button"
        aria-label={label}
        aria-expanded={open}
        aria-controls={panelId}
        onClick={() => { setOpen((was) => !was); }}
        className="flex items-center gap-2 rounded-card px-3 py-1 text-sm text-fg-muted transition-colors duration-(--motion-fast) hover:text-fg focus-visible:outline-2 focus-visible:outline-highlight aria-expanded:text-fg"
      >
        {button}
      </button>
      {open && (
        <div
          id={panelId}
          className="absolute right-0 top-full z-20 mt-2 w-max max-w-[min(32rem,90vw)] rounded-card bg-surface p-3 text-sm text-fg-muted shadow-soft"
        >
          {children(() => { setOpen(false); })}
        </div>
      )}
    </div>
  );
}
