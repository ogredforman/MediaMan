import logoUrl from '../assets/logo.png';

/** The MediaMan logo (a copy of assets/brand/icon-256.png, written by `npm run brand`), square at `size` CSS px. */
export function Logo({ size, className = '' }: { size: number; className?: string }) {
  return <img src={logoUrl} alt="MediaMan logo" width={size} height={size} className={`shrink-0 ${className}`} />;
}
