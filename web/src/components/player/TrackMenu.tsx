import { useEffect, useId, useRef, type KeyboardEvent } from 'react';
import type { DesktopPlayerTrack } from '../../lib/desktop';
import { iconButtonClass } from './controls';
import { AudioIcon, SubtitlesIcon } from './playerIcons';

export type TrackMenuKind = 'audio' | 'subtitle';

const languageNames = new Intl.DisplayNames(['en'], { type: 'language' });

function languageLabel(code: string | undefined): string | undefined {
  if (code === undefined || code === '' || code.toLowerCase() === 'und') return undefined;
  try {
    return languageNames.of(code);
  } catch {
    // Not a language tag Intl knows; show it as MPV reports it.
    return code.toUpperCase();
  }
}

/** "Commentary · English": the track's title, then its language unless the title already says it; else "Track N". */
function trackLabel(track: DesktopPlayerTrack): string {
  const language = languageLabel(track.language);
  let title = track.title?.trim();
  if (title === '' || title?.toLowerCase() === track.language?.toLowerCase()) title = undefined;
  if (title === undefined) return language ?? `Track ${String(track.id)}`;
  return language !== undefined && !title.toLowerCase().includes(language.toLowerCase()) ? `${title} · ${language}` : title;
}

interface TrackMenuProps {
  kind: TrackMenuKind;
  tracks: DesktopPlayerTrack[];
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** A track's id, or null for subtitles Off. */
  onSelect: (trackId: number | null) => void;
}

/**
 * The Audio or Subtitles button and its menu of tracks (Subtitles starts with Off). Opening focuses the selected item;
 * ↑/↓, Home and End move, Enter or Space picks. Clicks outside and tabbing out close it; the OSD closes it on Esc.
 * When it closes with focus inside, focus goes back to the button.
 */
export function TrackMenu({ kind, tracks, open, onOpenChange, onSelect }: TrackMenuProps) {
  const menuId = useId();
  const root = useRef<HTMLDivElement>(null);
  const trigger = useRef<HTMLButtonElement>(null);
  const menu = useRef<HTMLDivElement>(null);
  const wasOpen = useRef(open);
  const label = kind === 'audio' ? 'Audio' : 'Subtitles';
  const items: { id: number | null; label: string; selected: boolean }[] = tracks.map((track) => ({
    id: track.id,
    label: trackLabel(track),
    selected: track.selected,
  }));
  if (kind === 'subtitle') items.unshift({ id: null, label: 'Off', selected: !tracks.some((track) => track.selected) });

  useEffect(() => {
    if (open) {
      const selected = menu.current?.querySelector<HTMLElement>('[aria-checked="true"]');
      (selected ?? menu.current?.querySelector<HTMLElement>('[role="menuitemradio"]'))?.focus();
    } else if (wasOpen.current && (document.activeElement === null || document.activeElement === document.body)) {
      // The focused item went with the menu.
      trigger.current?.focus();
    }
    wasOpen.current = open;
  }, [open]);

  useEffect(() => {
    if (!open) return;
    const onClick = (event: MouseEvent) => {
      if (!root.current?.contains(event.target as Node)) onOpenChange(false);
    };
    document.addEventListener('click', onClick, true);
    return () => {
      document.removeEventListener('click', onClick, true);
    };
  }, [open, onOpenChange]);

  const move = (event: KeyboardEvent) => {
    const all = [...(menu.current?.querySelectorAll<HTMLElement>('[role="menuitemradio"]') ?? [])];
    const at = all.indexOf(document.activeElement as HTMLElement);
    const to = ({ ArrowDown: at + 1, ArrowUp: at - 1, Home: 0, End: all.length - 1 } as Record<string, number | undefined>)[event.key];
    if (to === undefined || all.length === 0) return;
    // The OSD's own ↑/↓ change the volume; inside the menu they move.
    event.preventDefault();
    event.stopPropagation();
    all[(to + all.length) % all.length]?.focus();
  };

  return (
    <div
      ref={root}
      className="relative"
      onBlur={(event) => {
        if (open && event.relatedTarget instanceof Node && !event.currentTarget.contains(event.relatedTarget)) onOpenChange(false);
      }}
    >
      <button
        ref={trigger}
        type="button"
        className={`${iconButtonClass} aria-expanded:bg-fg/15`}
        aria-label={label}
        title={kind === 'audio' ? 'Audio (A)' : 'Subtitles (C)'}
        aria-haspopup="menu"
        aria-expanded={open}
        aria-controls={menuId}
        onClick={() => { onOpenChange(!open); }}
      >
        {kind === 'audio' ? <AudioIcon /> : <SubtitlesIcon />}
      </button>
      {open && (
        <div
          ref={menu}
          id={menuId}
          role="menu"
          aria-label={label}
          tabIndex={-1}
          onKeyDown={move}
          className="absolute right-0 bottom-full z-20 mb-2 flex max-h-[50vh] w-max max-w-[min(24rem,80vw)] flex-col overflow-y-auto rounded-card bg-surface/95 p-2 text-sm shadow-soft"
        >
          <p aria-hidden="true" className="px-3 pt-1 pb-2 text-xs font-semibold tracking-wide text-fg-muted uppercase">{label}</p>
          {items.map((item) => (
            <button
              key={item.id ?? 'off'}
              type="button"
              role="menuitemradio"
              aria-checked={item.selected}
              className="flex items-center gap-3 rounded-card px-3 py-2 text-left transition-colors duration-(--motion-fast) hover:bg-fg/10 aria-checked:text-highlight"
              onClick={() => {
                onSelect(item.id);
                onOpenChange(false);
              }}
            >
              <span aria-hidden="true" className="w-3">{item.selected ? '✓' : ''}</span>
              {item.label}
            </button>
          ))}
        </div>
      )}
    </div>
  );
}
