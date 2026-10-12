import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { Link } from '@tanstack/react-router';
import { getSearchLibraryQueryKey, searchLibrary } from '../api/mediaman';
import type { LibrarySearchResults } from '../api/mediaman.schemas';
import type { ApiError } from '../lib/http';
import { PosterCard } from './PosterGrid';

const linkClass =
  'rounded-card bg-surface px-4 py-2 shadow-soft transition-colors duration-(--motion-fast) hover:text-highlight focus-visible:outline-2 focus-visible:outline-highlight';

/**
 * Instant library search (SPEC §8 browsing, §11 Search). Each query is its own cache entry, so an answer to a query
 * the user has typed past never shows; the last results stay up while the next query loads.
 */
export function SearchScreen({ query }: { query: string }) {
  const q = query.trim();
  const results = useQuery<LibrarySearchResults, ApiError>({
    queryKey: getSearchLibraryQueryKey({ q }),
    queryFn: ({ signal }) => searchLibrary({ q }, { signal }),
    enabled: q !== '',
    placeholderData: keepPreviousData,
  });
  const titles = q === '' ? [] : (results.data?.titles ?? []);
  // A placeholder answer belongs to an earlier query: show it, but don't call it "no match" for this one.
  const settled = results.isSuccess && !results.isPlaceholderData;

  return (
    <section aria-labelledby="search-heading" className="p-8">
      <h1 id="search-heading" className="text-2xl font-semibold">
        Search
      </h1>

      {q === '' && <p className="mt-6 text-fg-muted">Type a movie or show name to search your library.</p>}

      {q !== '' && results.isPending && (
        <p role="status" className="mt-6 text-fg-muted">
          Searching…
        </p>
      )}

      {titles.length > 0 && !results.isError && (
        <ul
          aria-label="Results"
          aria-busy={results.isFetching}
          className="mt-6 grid grid-cols-[repeat(auto-fill,minmax(10rem,1fr))] gap-6"
        >
          {titles.map((title) => (
            <li key={title.id}>
              <PosterCard title={title} />
            </li>
          ))}
        </ul>
      )}

      {q !== '' && settled && titles.length === 0 && (
        <div className="mt-6 flex flex-col items-start gap-3">
          <p>No titles in your library match “{q}”.</p>
          <p className="text-fg-muted">Check the spelling, or browse instead.</p>
          <div className="flex gap-3">
            <Link to="/movies" className={linkClass}>
              Movies
            </Link>
            <Link to="/shows" className={linkClass}>
              Shows
            </Link>
          </div>
        </div>
      )}

      {q !== '' && results.isError && (
        <div role="alert" className="mt-6 flex flex-col items-start gap-2">
          <p>{results.error.problem.title}</p>
          <p className="text-fg-muted">{results.error.problem.action}</p>
          <button type="button" onClick={() => void results.refetch()} className={linkClass}>
            Try again
          </button>
        </div>
      )}
    </section>
  );
}
