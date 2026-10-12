import { Link } from '@tanstack/react-router';
import { useId, useRef, useState, type KeyboardEvent } from 'react';
import { getGetHomeQueryKey, useGetHome } from '../api/mediaman';
import type { HomeEpisode, HomeHero as Hero, HomeItem, HomeRow } from '../api/mediaman.schemas';
import { useRefetchAfterPlayback } from '../lib/desktopPlayback';
import { heroTint } from '../lib/heroTint';
import { artworkSrc, type ApiError } from '../lib/http';
import { formatPosition } from '../lib/mediaFormat';
import { buttonClass } from './formClasses';
import { PlayButton } from './PlayButton';
import { EmptyLibrary } from './PosterGrid';
import { WatchedBadge, WatchedButton } from './WatchedButton';

const rowLabels: Record<Exclude<HomeRow['kind'], 'genre'>, string> = {
  continueWatching: 'Continue Watching',
  nextUp: 'Next Up',
  recentlyAdded: 'Recently Added',
};

function rowLabel(row: HomeRow): string {
  return row.kind === 'genre' ? (row.genre ?? 'Genre') : rowLabels[row.kind];
}

const pad = (n: number) => String(n).padStart(2, '0');

/** `S01E02 · Pilot`, or `S01E02` when the episode has no name. */
function episodeLabel(episode: HomeEpisode): string {
  const code = `S${pad(episode.seasonNumber)}E${pad(episode.number)}`;
  return episode.name === undefined ? code : `${code} · ${episode.name}`;
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

function Progress({ item }: { item: HomeItem }) {
  if (item.progress === undefined) return null;
  const { positionSeconds, durationSeconds } = item.progress;
  const percent = Math.min(100, (positionSeconds / durationSeconds) * 100);
  return (
    <div
      role="progressbar"
      aria-label="Watched so far"
      aria-valuemin={0}
      aria-valuemax={Math.round(durationSeconds)}
      aria-valuenow={Math.round(Math.min(positionSeconds, durationSeconds))}
      aria-valuetext={`${formatPosition(positionSeconds)} of ${formatPosition(durationSeconds)}`}
      className="absolute inset-x-2 bottom-2 h-1 overflow-hidden rounded-full bg-bg/80"
    >
      <div className="h-full bg-accent" style={{ width: `${String(percent)}%` }} />
    </div>
  );
}

const cardClass =
  'block rounded-card outline-none focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-highlight';
const frameClass =
  'relative overflow-hidden rounded-card bg-surface shadow-soft ring-highlight transition-shadow duration-(--motion-fast) group-hover:ring-2';

/** What a card's Mark watched marks: its movie or episode on Continue Watching and Next Up, else the whole title. */
function watchedAction({ title, playableId, episode }: HomeItem) {
  if (playableId === undefined) return { target: { titleId: title.id }, watched: title.watched, name: title.name };
  const name = episode ? `${title.name} ${episodeLabel(episode)}` : title.name;
  return { target: { playableId }, watched: false, name };
}

/**
 * A card: a poster, or an episode's still in a row of episodes. It opens the title's detail page; Mark watched sits
 * over the art and follows the card in the tab order.
 */
function HomeCard({
  item,
  wide,
  tabIndex,
  onFocus,
  onKeyDown,
}: {
  item: HomeItem;
  wide: boolean;
  tabIndex: number;
  onFocus: () => void;
  onKeyDown: (event: KeyboardEvent) => void;
}) {
  const { title, episode } = item;
  const art = wide ? episode?.still : title.poster;
  return (
    <div className={`group relative ${wide ? 'w-72' : 'w-40'}`}>
      <Link
        to="/titles/$titleId"
        params={{ titleId: title.id }}
        tabIndex={tabIndex}
        onFocus={onFocus}
        onKeyDown={onKeyDown}
        data-card=""
        className={cardClass}
      >
        <div className={`${frameClass} ${wide ? 'aspect-video' : 'aspect-[2/3]'}`}>
          {art ? (
            <img src={artworkSrc(art)} alt="" loading="lazy" className="h-full w-full object-cover" />
          ) : (
            <div aria-hidden="true" className="flex h-full items-center justify-center p-4 text-center text-highlight">
              {title.name}
            </div>
          )}
          {item.playableId === undefined && title.watched && <WatchedBadge />}
          <Progress item={item} />
        </div>
        <p className="mt-2 truncate font-medium">{title.name}</p>
        {episode ? (
          <p className="truncate text-sm text-fg-muted">{episodeLabel(episode)}</p>
        ) : (
          title.year !== undefined && <p className="text-sm text-fg-muted">{title.year}</p>
        )}
      </Link>
      <WatchedButton overlay tabIndex={tabIndex} {...watchedAction(item)} />
    </div>
  );
}

const rowKeys: Record<string, (index: number, last: number) => number> = {
  ArrowLeft: (i) => i - 1,
  ArrowRight: (i) => i + 1,
  Home: () => 0,
  End: (_, last) => last,
};

/**
 * A horizontally scrolling row. One card per row is in the tab order (the last one focused), so Tab moves between
 * rows; Left/Right, Home and End move between cards, and focusing a card scrolls it into view.
 */
function HomeRowSection({ row }: { row: HomeRow }) {
  const headingId = useId();
  const list = useRef<HTMLUListElement>(null);
  const [active, setActive] = useState(0);
  const wide = row.items.every((item) => item.episode !== undefined);
  const current = Math.min(active, row.items.length - 1);

  const onKeyDown = (event: KeyboardEvent, index: number) => {
    const move = rowKeys[event.key];
    if (move === undefined || list.current === null) return;
    const cards = [...list.current.querySelectorAll<HTMLElement>('[data-card]')];
    const next = move(index, cards.length - 1);
    if (next < 0 || next >= cards.length) return;
    event.preventDefault();
    cards[next]?.focus();
  };

  const scroll = (direction: 1 | -1) => {
    const el = list.current;
    if (el !== null) el.scrollBy({ left: direction * el.clientWidth * 0.8, behavior: 'smooth' });
  };

  return (
    <section aria-labelledby={headingId} className="group/row relative flex flex-col gap-3">
      <h2 id={headingId} className="px-8 text-xl font-semibold">
        {rowLabel(row)}
      </h2>
      {/* Keyboard users move with the arrow keys; these are for the mouse, so they stay out of the tab order. */}
      {(['left', 'right'] as const).map((side) => (
        <button
          key={side}
          type="button"
          tabIndex={-1}
          aria-hidden="true"
          onClick={() => {
            scroll(side === 'left' ? -1 : 1);
          }}
          className={`absolute top-10 bottom-12 z-10 w-8 bg-bg/70 text-2xl text-highlight opacity-0 transition-opacity duration-(--motion-normal) group-hover/row:opacity-100 ${side === 'left' ? 'left-0' : 'right-0'}`}
        >
          {side === 'left' ? '‹' : '›'}
        </button>
      ))}
      <ul
        ref={list}
        aria-labelledby={headingId}
        className="flex snap-x snap-mandatory scroll-px-8 gap-4 overflow-x-auto px-8 pt-1 pb-3 [scrollbar-width:thin]"
      >
        {row.items.map((item, index) => (
          <li key={`${item.title.id}:${item.playableId ?? ''}`} className="shrink-0 snap-start">
            <HomeCard
              item={item}
              wide={wide}
              tabIndex={index === current ? 0 : -1}
              onFocus={() => {
                setActive(index);
              }}
              onKeyDown={(event) => {
                onKeyDown(event, index);
              }}
            />
          </li>
        ))}
      </ul>
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
            <HomeRowSection key={row.kind === 'genre' ? `genre:${row.genre ?? ''}` : row.kind} row={row} />
          ))}
        </>
      )}
    </section>
  );
}
