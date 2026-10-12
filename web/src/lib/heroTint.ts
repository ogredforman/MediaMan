import type { CSSProperties } from 'react';
import type { ArtworkRef } from '../api/mediaman.schemas';

const hexColor = /^#[0-9a-f]{6}$/i;

/**
 * The `--tint` a hero is tinted with: the first artwork's dominant color (SPEC §11). Anything that isn't `#RRGGBB`
 * falls back to the accent token.
 */
export function heroTint(...artwork: (ArtworkRef | undefined)[]): CSSProperties {
  const color = artwork.find((a) => a?.dominantColor !== undefined)?.dominantColor;
  const tint = color !== undefined && hexColor.test(color) ? color : 'var(--color-accent)';
  return { '--tint': tint } as CSSProperties;
}
