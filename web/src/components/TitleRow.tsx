import { Link } from '@tanstack/react-router';
import { useId, useRef, useState, type KeyboardEvent } from 'react';
import type { HomeItem } from '../api/mediaman.schemas';
import { artworkSrc } from '../lib/http';
import { episodeLabel, formatPosition } from '../lib/mediaFormat';
import { WatchedBadge, WatchedButton } from './WatchedButton';


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
function TitleCard({
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

/** Where a row's "See all" goes: a link to the full list, labelled for screen readers with the row's name. */
export interface SeeAll {
  to: '/movies' | '/shows';
  search: { genre: string };
}

/**
 * A horizontally scrolling row of cards (Home's rows, and the genre rows on Movies and Shows), with an optional "See
 * all" link by the heading. One card per row is in the tab order (the last one focused), so Tab moves between rows;
 * Left/Right, Home and End move between cards, and focusing a card scrolls it into view.
 */
export function TitleRow({ heading, items, seeAll }: { heading: string; items: HomeItem[]; seeAll?: SeeAll }) {
  const headingId = useId();
  const list = useRef<HTMLUListElement>(null);
  const [active, setActive] = useState(0);
  const wide = items.every((item) => item.episode !== undefined);
  const current = Math.min(active, items.length - 1);

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
      <div className="flex items-baseline gap-4 px-8">
        <h2 id={headingId} className="text-xl font-semibold">
          {heading}
        </h2>
        {seeAll && (
          <Link
            to={seeAll.to}
            search={seeAll.search}
            aria-label={`See all ${heading}`}
            className="rounded-card text-sm text-fg-muted transition-colors duration-(--motion-fast) outline-none hover:text-highlight focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-highlight"
          >
            See all
          </Link>
        )}
      </div>
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
        {items.map((item, index) => (
          <li key={`${item.title.id}:${item.playableId ?? ''}`} className="shrink-0 snap-start">
            <TitleCard
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
