/** Shared constants and classes for the player OSD. */

/** Controls and the cursor hide after this long without a mouse move, click or key while playing. */
export const idleMs = 3000;
/** What ← and the skip back button seek. */
export const skipBackSeconds = 10;
/** What → and the skip forward button seek. */
export const skipForwardSeconds = 30;
/** How much one wheel notch or ↑/↓ changes the volume. */
export const volumeStep = 5;

export function clampVolume(volume: number): number {
  return Math.min(100, Math.max(0, Math.round(volume)));
}

export const iconButtonClass =
  'flex h-10 w-10 shrink-0 items-center justify-center rounded-full text-fg transition-colors duration-(--motion-fast) hover:bg-fg/15';

/** How long a notice (e.g. a lost server) stays up unless dismissed. */
export const noticeMs = 8000;
