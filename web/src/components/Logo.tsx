import logoUrl from '../assets/logo.png';
import wideLogoUrl from '../assets/logo-wide.png';

/** The wide logo's aspect ratio (assets/brand/mediaman-wide.png is 617×456). */
const wideAspect = 617 / 456;

/**
 * The MediaMan logo, `size` CSS px tall: `square` is a copy of assets/brand/icon-256.png, `wide` (the shell header)
 * of assets/brand/mediaman-wide.png, both written by `npm run brand`.
 */
export function Logo({
  size,
  variant = 'square',
  className = '',
}: {
  size: number;
  variant?: 'square' | 'wide';
  className?: string;
}) {
  const wide = variant === 'wide';
  return (
    <img
      src={wide ? wideLogoUrl : logoUrl}
      alt="MediaMan logo"
      width={wide ? Math.round(size * wideAspect) : size}
      height={size}
      className={`shrink-0 ${className}`}
    />
  );
}
