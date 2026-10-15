import { Link } from '@tanstack/react-router';
import { getGetHomeQueryKey, useGetHome } from '../api/mediaman';
import type { HomeHero as Hero, HomeRow } from '../api/mediaman.schemas';
import { useRefetchAfterPlayback } from '../lib/desktopPlayback';
import { heroTint } from '../lib/heroTint';
import { artworkSrc, type ApiError } from '../lib/http';
import { episodeLabel } from '../lib/mediaFormat';
import { buttonClass } from './formClasses';
import { PlayButton } from './PlayButton';
import { EmptyLibrary } from './PosterGrid';
import { TitleRow } from './TitleRow';

const rowLabels: Record<Exclude<HomeRow['kind'], 'genre'>, string> = {
  continueWatching: 'Continue Watching',
  nextUp: 'Next Up',
  recentlyAdded: 'Recently Added',
};

function rowLabel(row: HomeRow): string {
  return row.kind === 'genre' ? (row.genre ?? 'Genre') : rowLabels[row.kind];
}

/** The featured title (SPEC §11 Home): backdrop tinted from its dominant color, logo or title, overview, Play, More info. */
function HomeHero({ hero }: { hero: Hero }) {
  const { title, episode } = hero;
  const facts = [
    title.year === undefined ? undefined : String(title.year),
    title.rating === undefined ? undefined : `${title.rating.toFixed(1)}/10`,
  ].filter((fact): fact is string => fact !== undefined);

  return (
    <section aria-labelledby="hero-heading" className="relative isolate overflow-hidden" style={heroTint(hero.backdrop, title.poster)}>
      {hero.backdrop && (
        <img src={artworkSrc(hero.backdrop)} alt="" className="absolute inset-0 -z-20 h-full w-full object-cover opacity-60" />
      )}
      <div
        aria-hidden="true"
        className="absolute inset-0 -z-10 bg-[linear-gradient(to_top,var(--color-bg)_5%,color-mix(in_srgb,var(--color-bg)_70%,transparent)_55%,color-mix(in_srgb,var(--tint)_35%,transparent))]"
      />
      <div className="flex max-w-3xl flex-col gap-3 px-8 pt-48 pb-10">
        <h2 id="hero-heading" className="text-4xl font-bold">
          {hero.logo ? (
            <img src={artworkSrc(hero.logo)} alt={title.name} className="max-h-28 max-w-md object-contain object-left" />
          ) : (
            title.name
          )}
        </h2>
        {facts.length > 0 && (
          <ul aria-label="Details" className="flex flex-wrap gap-x-4 text-fg-muted">
            {facts.map((fact) => (
              <li key={fact}>{fact}</li>
            ))}
          </ul>
        )}
        {episode && <p className="font-medium text-highlight">{episodeLabel(episode)}</p>}
        {hero.overview !== undefined && <p className="line-clamp-3 text-fg">{hero.overview}</p>}
        <div className="flex flex-wrap items-start gap-3">
          {hero.playableId !== undefined && (
            <PlayButton
              playableId={hero.playableId}
              target={{
                name: episode === undefined ? undefined : episodeLabel(episode),
                resumeSeconds: hero.resumeSeconds,
                watched: false,
              }}
            />
          )}
          <Link
            to="/titles/$titleId"
            params={{ titleId: title.id }}
            aria-label={`More info on ${title.name}`}
            className={`${buttonClass} bg-surface text-fg shadow-soft`}
          >
            More info
          </Link>
        </div>
      </div>
    </section>
  );
}

function LoadError({ error, onRetry }: { error: ApiError; onRetry: () => void }) {
  const { problem } = error;
  return (
    <div role="alert" className="flex flex-col items-start gap-2 px-8">
      <p className="text-lg">{problem.title}</p>
      {problem.detail !== undefined && <p className="text-fg-muted">{problem.detail}</p>}
      <p className="text-fg-muted">{problem.action ?? 'Check that the server is running, then try again.'}</p>
      <button type="button" onClick={onRetry} className="rounded-card bg-surface px-3 py-1 shadow-soft hover:text-highlight">
        Try again
      </button>
    </div>
  );
}

/** Home (SPEC §11): the hero banner, then Continue Watching, Next Up, Recently Added and genre rows, as the server sends them. */
export function HomeScreen() {
  const home = useGetHome();
  useRefetchAfterPlayback(getGetHomeQueryKey());
  const empty = home.isSuccess && home.data.hero === undefined && home.data.rows.length === 0;

  return (
    <section aria-labelledby="home-heading" className="flex flex-col gap-8 pb-12">
      <h1 id="home-heading" className="sr-only">
        Home
      </h1>
      {home.isPending && (
        <p role="status" className="p-8 text-fg-muted">
          Loading Home…
        </p>
      )}
      {home.isError && (
        <div className="pt-8">
          <LoadError
            error={home.error}
            onRetry={() => {
              void home.refetch();
            }}
          />
        </div>
      )}
      {empty && (
        <div className="px-8">
          <EmptyLibrary plural="titles" />
        </div>
      )}
      {home.isSuccess && (
        <>
          {home.data.hero ? <HomeHero hero={home.data.hero} /> : !empty && <div className="pt-2" />}
          {home.data.rows.filter((row) => row.items.length > 0).map((row) => (
            <TitleRow key={row.kind === 'genre' ? `genre:${row.genre ?? ''}` : row.kind} heading={rowLabel(row)} items={row.items} />
          ))}
        </>
      )}
    </section>
  );
}
