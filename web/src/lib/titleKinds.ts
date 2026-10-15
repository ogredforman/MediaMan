import type { TitleKind } from '../api/mediaman.schemas';

/** How the Movies and Shows pages name a kind, and where each lives. */
export const kindLabels: Record<TitleKind, { heading: string; plural: string; to: '/movies' | '/shows' }> = {
  movie: { heading: 'Movies', plural: 'movies', to: '/movies' },
  show: { heading: 'Shows', plural: 'shows', to: '/shows' },
};
