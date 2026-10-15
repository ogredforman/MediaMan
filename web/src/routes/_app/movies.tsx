import { createFileRoute } from '@tanstack/react-router';
import { GenreBrowser } from '../../components/GenreBrowser';

export const Route = createFileRoute('/_app/movies')({
  // `?genre=` picks a genre's full grid; without it the page shows a row per genre.
  validateSearch: (search: Record<string, unknown>): { genre?: string } =>
    typeof search.genre === 'string' && search.genre !== '' ? { genre: search.genre } : {},
  component: function MoviesPage() {
    const { genre } = Route.useSearch();
    return <GenreBrowser kind="movie" genre={genre} />;
  },
});
