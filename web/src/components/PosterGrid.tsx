import { useInfiniteQuery, type InfiniteData } from '@tanstack/react-query';
import { getListTitlesQueryKey, listTitles, useListLibraries } from '../api/mediaman';
import type { TitleKind, TitlePage, TitleSummary } from '../api/mediaman.schemas';
import type { ApiError } from '../lib/http';
import { ScanLibraryButton } from './ScanStatus';

const labels: Record<TitleKind, { heading: string; plural: string }> = {
  movie: { heading: 'Movies', plural: 'movies' },
  show: { heading: 'Shows', plural: 'shows' },
};

function PosterCard({ title }: { title: TitleSummary }) {
  return (
    <div>
      <div className="aspect-[2/3] overflow-hidden rounded-card bg-surface shadow-soft">
        {title.poster ? (
          <img src={title.poster.url} alt="" loading="lazy" className="h-full w-full object-cover" />
        ) : (
          <div aria-hidden="true" className="flex h-full items-center justify-center p-4 text-center text-highlight">
            {title.name}
          </div>
        )}
      </div>
      <p className="mt-2 truncate font-medium">{title.name}</p>
      {title.year !== undefined && <p className="text-sm text-fg-muted">{title.year}</p>}
    </div>
  );
}

function EmptyLibrary({ plural }: { plural: string }) {
  const libraries = useListLibraries();
  if (libraries.isPending) return null;
  if (libraries.isError) {
    return (
      <div role="alert" className="mt-6 text-fg-muted">
        <p>
          No {plural} to show, and your libraries couldn't be loaded: {libraries.error.problem.title}
        </p>
        <p>{libraries.error.problem.action}</p>
      </div>
    );
  }
  const items = libraries.data.items;
  const scanning = items.some((library) => library.scan.state !== 'idle');

  if (scanning) {
    return <p className="mt-6 text-fg-muted">A scan is running. Your {plural} will appear here as it finishes.</p>;
  }
  return (
    <div className="mt-6 flex flex-col items-start gap-3 text-fg-muted">
      <p>
        No {plural} in your library yet. Scan to look for new files, or check that a library folder contains {plural}.
      </p>
      <ul className="flex flex-wrap gap-3">
        {items.map((library) => (
          <li key={library.id} className="flex items-center gap-2">
            <ScanLibraryButton library={library} />
          </li>
        ))}
      </ul>
    </div>
  );
}

/** The Movies or Shows poster grid, a page at a time (SPEC §11). */
export function PosterGrid({ kind }: { kind: TitleKind }) {
  const { heading, plural } = labels[kind];
  const titles = useInfiniteQuery<TitlePage, ApiError, InfiniteData<TitlePage>, readonly unknown[], string | undefined>(
    {
      queryKey: [...getListTitlesQueryKey({ kind }), 'infinite'],
      queryFn: ({ pageParam, signal }) =>
        listTitles(pageParam === undefined ? { kind } : { kind, cursor: pageParam }, { signal }),
      initialPageParam: undefined,
      getNextPageParam: (last) => last.pageInfo.nextCursor,
    },
  );
  const items = titles.data?.pages.flatMap((page) => page.items) ?? [];
  const headingId = `${kind}-heading`;

  return (
    <section aria-labelledby={headingId} className="p-8">
      <h1 id={headingId} className="text-2xl font-semibold">
        {heading}
      </h1>

      {titles.isPending && (
        <p role="status" className="mt-6 text-fg-muted">
          Loading {plural}…
        </p>
      )}
      {titles.isSuccess && items.length === 0 && <EmptyLibrary plural={plural} />}

      {items.length > 0 && (
        <ul className="mt-6 grid grid-cols-[repeat(auto-fill,minmax(10rem,1fr))] gap-6">
          {items.map((title) => (
            <li key={title.id}>
              <PosterCard title={title} />
            </li>
          ))}
        </ul>
      )}

      {titles.isError && (
        <div role="alert" className="mt-6 flex flex-col items-start gap-2">
          <p>{titles.error.problem.title}</p>
          <p className="text-fg-muted">{titles.error.problem.action}</p>
          <button
            type="button"
            onClick={() => void (titles.data ? titles.fetchNextPage() : titles.refetch())}
            className="rounded-card bg-surface px-3 py-1 shadow-soft hover:text-highlight"
          >
            Try again
          </button>
        </div>
      )}

      {titles.hasNextPage && !titles.isError && (
        <button
          type="button"
          disabled={titles.isFetchingNextPage}
          onClick={() => void titles.fetchNextPage()}
          className="mt-8 rounded-card bg-surface px-4 py-2 shadow-soft transition-colors duration-(--motion-fast) hover:text-highlight disabled:opacity-50"
        >
          {titles.isFetchingNextPage ? `Loading more ${plural}…` : 'Load more'}
        </button>
      )}
    </section>
  );
}
