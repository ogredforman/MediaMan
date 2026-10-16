// The player OSD's icons, drawn in currentColor on a 24×24 grid.
import type { ReactNode } from 'react';

function Icon({ children }: { children: ReactNode }) {
  return (
    <svg aria-hidden="true" viewBox="0 0 24 24" fill="currentColor" className="h-6 w-6">
      {children}
    </svg>
  );
}

export function BackIcon() {
  return (
    <Icon>
      <path d="M15.4 4.6a1 1 0 0 1 0 1.4L9.4 12l6 6a1 1 0 1 1-1.4 1.4l-6.7-6.7a1 1 0 0 1 0-1.4L14 4.6a1 1 0 0 1 1.4 0Z" />
    </Icon>
  );
}

export function PlayIcon() {
  return (
    <Icon>
      <path d="M7 4.8v14.4a1 1 0 0 0 1.5.9l11.3-7.2a1 1 0 0 0 0-1.8L8.5 3.9A1 1 0 0 0 7 4.8Z" />
    </Icon>
  );
}

export function PauseIcon() {
  return (
    <Icon>
      <rect x="6" y="4" width="4" height="16" rx="1" />
      <rect x="14" y="4" width="4" height="16" rx="1" />
    </Icon>
  );
}

export function PreviousIcon() {
  return (
    <Icon>
      <rect x="5" y="5" width="2.5" height="14" rx="1" />
      <path d="M19 6.2v11.6a1 1 0 0 1-1.6.8l-8.3-5.8a1 1 0 0 1 0-1.6l8.3-5.8a1 1 0 0 1 1.6.8Z" />
    </Icon>
  );
}

export function NextIcon() {
  return (
    <Icon>
      <rect x="16.5" y="5" width="2.5" height="14" rx="1" />
      <path d="M5 6.2v11.6a1 1 0 0 0 1.6.8l8.3-5.8a1 1 0 0 0 0-1.6L6.6 5.4A1 1 0 0 0 5 6.2Z" />
    </Icon>
  );
}

/** A circular arrow with the seconds it skips; `back` turns it anticlockwise. */
export function SkipIcon({ seconds, back }: { seconds: number; back: boolean }) {
  return (
    <svg aria-hidden="true" viewBox="0 0 24 24" className="h-7 w-7">
      <g fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" transform={back ? undefined : 'matrix(-1 0 0 1 24 0)'}>
        <path d="M4.5 12a7.5 7.5 0 1 0 2.2-5.3" />
        <path d="M4 3.5v3.7h3.7" />
      </g>
      <text x="12" y="15.3" textAnchor="middle" fontSize="7.5" fontWeight="700" fill="currentColor">
        {seconds}
      </text>
    </svg>
  );
}

export function VolumeIcon({ level }: { level: number }) {
  return (
    <Icon>
      <path d="M4 9.5v5a1 1 0 0 0 1 1h3l4.4 3.7a1 1 0 0 0 1.6-.8V5.6a1 1 0 0 0-1.6-.8L8 8.5H5a1 1 0 0 0-1 1Z" />
      <g fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round">
        {level === 0 ? (
          <path d="m16.5 9.5 5 5m0-5-5 5" />
        ) : (
          <>
            <path d="M16.5 9a4 4 0 0 1 0 6" />
            {level > 50 && <path d="M19 6.5a7.5 7.5 0 0 1 0 11" />}
          </>
        )}
      </g>
    </Icon>
  );
}

export function FullscreenIcon({ exit }: { exit: boolean }) {
  return (
    <Icon>
      <g fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
        {exit ? (
          <path d="M9 4v5H4M15 4v5h5M9 20v-5H4M15 20v-5h5" />
        ) : (
          <path d="M4 9V4h5M20 9V4h-5M4 15v5h5M20 15v5h-5" />
        )}
      </g>
    </Icon>
  );
}
