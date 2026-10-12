import { createFileRoute } from '@tanstack/react-router';
import { SearchScreen } from '../../components/SearchScreen';

export interface SearchParams {
  /** What the user typed in the shell's search field; the field reads it back from here. */
  q?: string;
}

export const Route = createFileRoute('/_app/search')({
  validateSearch: (search: Record<string, unknown>): SearchParams => ({
    q: typeof search.q === 'string' ? search.q : undefined,
  }),
  component: SearchRoute,
});

function SearchRoute() {
  const { q } = Route.useSearch();
  return <SearchScreen query={q ?? ''} />;
}
