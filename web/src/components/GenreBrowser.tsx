import { Link } from '@tanstack/react-router';
import { useListGenres, useListTitles } from '../api/mediaman';
import type { TitleKind } from '../api/mediaman.schemas';
import { kindLabels } from '../lib/titleKinds';
import { PosterGrid } from './PosterGrid';
import { TitleRow } from './TitleRow';

// Each genre row shows this many of its titles, best rated first, as on Home.
const rowSize = 20;

const chipClass =
  'block whitespace-nowrap rounded-full bg-surface px-4 py-1 text-sm text-fg-muted shadow-soft outline-none transition-colors duration-(--motion-fast) hover:text-fg focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-highlight aria-[current=page]:bg-accent aria-[current=page]:text-fg';

/**
 * All, then each genre, most titles first. Each is a URL, so Back works; the router marks the picked one
 * `aria-current="page"`, matching the search exactly so All isn't current under a genre.
 */
function GenreChips({ kind, genres }: { kind: TitleKind; genres: string[] }) {
  const { to } = kindLabels[kind];
  return (
    <nav aria-label="Genres">
      <ul className="flex gap-2 overflow-x-auto px-8 pt-1 pb-2 [scrollbar-width:thin]">
        <li>
          <Link to={to} search={{}} activeOptions={{ exact: true }} className={chipClass}>
            All
          </Link>
        </li>
        {genres.map((genre) => (
          <li key={genre}>
            <Link to={to} search={{ genre }} activeOptions={{ exact: true }} className={chipClass}>
              {genre}
            </Link>
          </li>
        ))}
      </ul>
    </nav>
  );
}

/** One genre's best-rated titles, with "See all" opening the genre's full grid. Nothing until it loads. */
function GenreRow({ kind, genre }: { kind: TitleKind; genre: string }) {
  const titles = useListTitles({ kind, genre, sort: 'rating', limit: rowSize });
  if (titles.isError) {
    return (
      <div role="alert" className="px-8">
        <p>
          {genre}: {titles.error.problem.title}
        </p>
        <p className="text-fg-muted">{titles.error.problem.action}</p>
      </div>
    );
  }
  if (!titles.isSuccess || titles.data.items.length === 0) return null;
  return (
    <TitleRow
      heading={genre}
      items={titles.data.items.map((title) => ({ title }))}
      seeAll={{ to: kindLabels[kind].to, search: { genre } }}
    />
  );
}

/**
 * Movies or Shows (SPEC §11): a genre chip bar over a row per genre, then every title A–Z (so titles without a genre,
 * or outside every row's top, can still be browsed); with a genre picked, that genre's full grid. With no genres to
 * show (no titles yet, or none with a genre) it's the full grid alone, which explains an empty library.
 */
export function GenreBrowser({ kind, genre }: { kind: TitleKind; genre?: string }) {
  const { heading, plural } = kindLabels[kind];
  const genres = useListGenres({ kind });
  const names = genres.data?.items.map((g) => g.name) ?? [];
  const headingId = `${kind}-heading`;

  return (
    <section aria-labelledby={headingId} className="flex flex-col gap-6 py-8">
      <h1 id={headingId} className="px-8 text-2xl font-semibold">
        {heading}
      </h1>
      {names.length > 0 && <GenreChips kind={kind} genres={names} />}

      {genre !== undefined ? (
        <PosterGrid kind={kind} genre={genre} />
      ) : genres.isPending ? (
        <p role="status" className="px-8 text-fg-muted">
          Loading {plural}…
        </p>
      ) : genres.isError ? (
        <div role="alert" className="flex flex-col items-start gap-2 px-8">
          <p>{genres.error.problem.title}</p>
          <p className="text-fg-muted">{genres.error.problem.action}</p>
          <button
            type="button"
            onClick={() => void genres.refetch()}
            className="rounded-card bg-surface px-3 py-1 shadow-soft hover:text-highlight"
          >
            Try again
          </button>
        </div>
      ) : names.length === 0 ? (
        <PosterGrid kind={kind} />
      ) : (
        <div className="flex flex-col gap-8">
          {names.map((name) => (
            <GenreRow key={name} kind={kind} genre={name} />
          ))}
          <section aria-labelledby={`${headingId}-all`} className="flex flex-col gap-3">
            <h2 id={`${headingId}-all`} className="px-8 text-xl font-semibold">
              All {plural} A–Z
            </h2>
            <PosterGrid kind={kind} />
          </section>
        </div>
      )}
    </section>
  );
}
