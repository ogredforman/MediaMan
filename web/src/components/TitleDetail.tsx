import { Link } from '@tanstack/react-router';
import { useId, useRef, useState, type CSSProperties, type KeyboardEvent } from 'react';
import { useDevPlay, useGetTitle } from '../api/mediaman';
import type { Episode, MediaVersion, Season, TitleDetail as Title } from '../api/mediaman.schemas';
import { ApiError, artworkSrc } from '../lib/http';
import { formatDuration, formatSize, versionTags } from '../lib/mediaFormat';

const hexColor = /^#[0-9a-f]{6}$/i;

const statusLabels: Partial<Record<MediaVersion['status'], string>> = {
  missing: 'Missing',
  needsAttention: 'Needs attention',
  pending: 'Pending',
  ignored: 'Ignored',
};

function seasonLabel(season: Season): string {
  return season.number === 0 ? 'Specials' : `Season ${String(season.number)}`;
}

/** Regular seasons in order, then specials. */
function orderSeasons(seasons: Season[]): Season[] {
  return [...seasons].sort((a, b) => (a.number === 0 ? 1 : b.number === 0 ? -1 : a.number - b.number));
}

function Chip({ children }: { children: string }) {
  return <li className="rounded-card bg-bg px-2 py-0.5 text-xs text-highlight">{children}</li>;
}

function VersionList({ versions, label, sharedWith }: { versions: MediaVersion[]; label: string; sharedWith?: Map<string, number[]> }) {
  return (
    <ul aria-label={label} className="flex flex-col gap-2">
      {versions.map((version) => {
        const status = statusLabels[version.status];
        const episodes = sharedWith?.get(version.mediaFileId);
        return (
          <li key={`${version.mediaFileId}-${String(version.part)}`} className="rounded-card bg-surface px-4 py-3 shadow-soft">
            <p className="truncate font-medium" title={version.fileName}>
              {version.fileName}
            </p>
            <div className="mt-2 flex flex-wrap items-center gap-2 text-sm text-fg-muted">
              <ul aria-label="Quality" className="flex flex-wrap gap-2">
                {versionTags(version).map((tag) => (
                  <Chip key={tag}>{tag}</Chip>
                ))}
              </ul>
              <span>{formatSize(version.sizeBytes)}</span>
              {version.durationSeconds !== undefined && <span>{formatDuration(version.durationSeconds / 60)}</span>}
              {episodes !== undefined && episodes.length > 1 && (
                <span>Episodes {formatEpisodeRange(episodes)} in one file</span>
              )}
              {status !== undefined && (
                <span className="rounded-card border border-highlight px-2 py-0.5 text-xs text-highlight">{status}</span>
              )}
            </div>
          </li>
        );
      })}
    </ul>
  );
}

function formatEpisodeRange(numbers: number[]): string {
  const sorted = [...numbers].sort((a, b) => a - b);
  const first = sorted[0] ?? 0;
  const last = sorted[sorted.length - 1] ?? 0;
  const contiguous = last - first === sorted.length - 1;
  return contiguous ? `${String(first)}–${String(last)}` : sorted.join(', ');
}

/** Which episodes of a season each file holds, to label multi-episode files. */
function episodesByFile(episodes: Episode[]): Map<string, number[]> {
  const map = new Map<string, number[]>();
  for (const episode of episodes) {
    for (const version of episode.versions) {
      const list = map.get(version.mediaFileId) ?? [];
      list.push(episode.number);
      map.set(version.mediaFileId, list);
    }
  }
  return map;
}

function formatAirDate(date: string): string {
  const parsed = new Date(`${date}T00:00:00Z`);
  return Number.isNaN(parsed.getTime()) ? date : parsed.toLocaleDateString(undefined, { dateStyle: 'medium', timeZone: 'UTC' });
}

function EpisodeList({ season }: { season: Season }) {
  const shared = episodesByFile(season.episodes);
  if (season.episodes.length === 0) {
    return <p className="text-fg-muted">No episodes are listed for this season yet.</p>;
  }
  return (
    <ol aria-label={`${seasonLabel(season)} episodes`} className="flex flex-col gap-6">
      {season.episodes.map((episode) => {
        const name = episode.name ?? `Episode ${String(episode.number)}`;
        return (
          <li key={episode.id} className="flex flex-col gap-4 sm:flex-row">
            <div className="aspect-video w-full shrink-0 overflow-hidden rounded-card bg-surface shadow-soft sm:w-56">
              {episode.still ? (
                <img src={artworkSrc(episode.still)} alt="" loading="lazy" className="h-full w-full object-cover" />
              ) : (
                <div aria-hidden="true" className="flex h-full items-center justify-center text-2xl text-highlight">
                  {episode.number}
                </div>
              )}
            </div>
            <div className="flex min-w-0 flex-1 flex-col gap-2">
              <h3 className="font-semibold">
                {episode.number}. {name}
              </h3>
              {episode.airDate !== undefined && (
                <p className="text-sm text-fg-muted">
                  <time dateTime={episode.airDate}>{formatAirDate(episode.airDate)}</time>
                </p>
              )}
              {episode.overview !== undefined && <p className="text-fg-muted">{episode.overview}</p>}
              {episode.versions.length === 0 ? (
                <p className="text-sm text-fg-muted">Not in library</p>
              ) : (
                <VersionList versions={episode.versions} label={`Versions of ${name}`} sharedWith={shared} />
              )}
              {episode.playableId !== undefined && isPlayable(episode.versions) && (
                <PlayButton playableId={episode.playableId} label={`Play ${name}`} />
              )}
            </div>
          </li>
        );
      })}
    </ol>
  );
}

/** Season tabs (WAI-ARIA tabs: arrow keys, Home and End move between seasons). */
function SeasonTabs({ seasons }: { seasons: Season[] }) {
  const ordered = orderSeasons(seasons);
  const [selectedId, setSelectedId] = useState(ordered[0]?.id);
  const tabs = useRef(new Map<string, HTMLButtonElement>());
  const baseId = useId();
  const selected = ordered.find((s) => s.id === selectedId) ?? ordered[0];

  if (selected === undefined) {
    return <p className="text-fg-muted">No seasons are listed for this show yet.</p>;
  }

  const select = (index: number) => {
    const season = ordered[(index + ordered.length) % ordered.length];
    if (season === undefined) return;
    setSelectedId(season.id);
    tabs.current.get(season.id)?.focus();
  };

  const onKeyDown = (event: KeyboardEvent, index: number) => {
    const moves: Record<string, number> = { ArrowRight: index + 1, ArrowLeft: index - 1, Home: 0, End: ordered.length - 1 };
    const target = moves[event.key];
    if (target === undefined) return;
    event.preventDefault();
    select(target);
  };

  return (
    <div className="flex flex-col gap-6">
      <div role="tablist" aria-label="Seasons" className="flex flex-wrap gap-2">
        {ordered.map((season, index) => {
          const isSelected = season.id === selected.id;
          return (
            <button
              key={season.id}
              ref={(el) => {
                if (el) tabs.current.set(season.id, el);
                else tabs.current.delete(season.id);
              }}
              type="button"
              role="tab"
              id={`${baseId}-tab-${season.id}`}
              aria-selected={isSelected}
              aria-controls={`${baseId}-panel`}
              tabIndex={isSelected ? 0 : -1}
              onClick={() => {
                setSelectedId(season.id);
              }}
              onKeyDown={(event) => {
                onKeyDown(event, index);
              }}
              className={`rounded-card px-4 py-1.5 transition-colors duration-(--motion-fast) ${
                isSelected ? 'bg-accent text-fg' : 'bg-surface text-fg-muted hover:text-fg'
              }`}
            >
              {seasonLabel(season)}
            </button>
          );
        })}
      </div>
      <div role="tabpanel" id={`${baseId}-panel`} aria-labelledby={`${baseId}-tab-${selected.id}`} tabIndex={0}>
        {selected.overview !== undefined && <p className="mb-6 max-w-3xl text-fg-muted">{selected.overview}</p>}
        <EpisodeList season={selected} />
      </div>
    </div>
  );
}

/** A version can be played unless its file is missing; the server picks which one (SPEC §7.4). */
function isPlayable(versions: MediaVersion[]): boolean {
  return versions.some((v) => v.status !== 'missing');
}

/** A 404 without a specific code means the server has no dev play endpoint, not that the title is gone. */
function playProblem(error: ApiError) {
  if (error.status === 404 && error.problem.code === 'not_found') {
    return {
      title: "Play isn't available on this server.",
      detail: "In the prototype, Play launches MPV through the server's dev configuration, which isn't running.",
      action: 'Start the server with the dev configuration (MediaMan:Dev:MediaPath), then try again.',
    };
  }
  return error.problem;
}

/**
 * Prototype Play (SPEC §8 playback [P]): the dev-only endpoint launches MPV on the server's host.
 * The desktop app replaces it with playback sessions and IPlayer (SPEC §7.4).
 */
function PlayButton({ playableId, label }: { playableId: string; label: string }) {
  const play = useDevPlay();
  const problem = play.error instanceof ApiError ? playProblem(play.error) : undefined;
  return (
    <div className="flex flex-col items-start gap-2">
      <button
        type="button"
        aria-label={label}
        disabled={play.isPending}
        onClick={() => {
          play.mutate({ playableId });
        }}
        className="inline-flex items-center gap-2 rounded-card bg-highlight px-5 py-2 font-semibold text-bg shadow-soft transition-colors hover:bg-fg disabled:opacity-60"
      >
        <svg aria-hidden="true" viewBox="0 0 16 16" fill="currentColor" className="h-4 w-4">
          <path d="M4 2.5v11a.5.5 0 0 0 .76.43l9-5.5a.5.5 0 0 0 0-.86l-9-5.5A.5.5 0 0 0 4 2.5Z" />
        </svg>
        Play
      </button>
      {play.isSuccess && (
        <p role="status" className="text-sm text-fg-muted">
          Playing {play.data.fileName} in MPV.
        </p>
      )}
      {play.isError && (
        <div role="alert" className="flex flex-col gap-1 text-sm">
          <p>{problem?.title ?? "Couldn't start playback."}</p>
          {problem?.detail !== undefined && <p className="text-fg-muted">{problem.detail}</p>}
          <p className="text-fg-muted">{problem?.action ?? 'Check that the server is running, then try again.'}</p>
        </div>
      )}
    </div>
  );
}

function Hero({ title }: { title: Title }) {
  const color = title.backdrop?.dominantColor ?? title.poster?.dominantColor;
  // The tint comes from the artwork (SPEC §11); anything that isn't `#RRGGBB` falls back to the accent token.
  const tint = color !== undefined && hexColor.test(color) ? color : 'var(--color-accent)';
  const style = { '--tint': tint } as CSSProperties;
  const facts = [
    title.year === undefined ? undefined : String(title.year),
    title.runtimeMinutes === undefined ? undefined : formatDuration(title.runtimeMinutes),
    title.rating === undefined ? undefined : `${title.rating.toFixed(1)}/10`,
  ].filter((fact): fact is string => fact !== undefined);

  return (
    <div className="relative isolate overflow-hidden" style={style}>
      {title.backdrop && (
        <img src={artworkSrc(title.backdrop)} alt="" className="absolute inset-0 -z-20 h-full w-full object-cover opacity-60" />
      )}
      <div
        aria-hidden="true"
        className="absolute inset-0 -z-10 bg-[linear-gradient(to_top,var(--color-bg)_5%,color-mix(in_srgb,var(--color-bg)_70%,transparent)_55%,color-mix(in_srgb,var(--tint)_35%,transparent))]"
      />
      <div className="flex items-end gap-8 px-8 pt-40 pb-8">
        <div className="hidden aspect-[2/3] w-44 shrink-0 overflow-hidden rounded-card bg-surface shadow-soft sm:block">
          {title.poster ? (
            <img src={artworkSrc(title.poster)} alt="" className="h-full w-full object-cover" />
          ) : (
            <div aria-hidden="true" className="flex h-full items-center justify-center p-4 text-center text-highlight">
              {title.name}
            </div>
          )}
        </div>
        <div className="flex max-w-3xl flex-col gap-3">
          <h1 id="title-heading" className="text-4xl font-bold">
            {title.name}
          </h1>
          {facts.length > 0 && (
            <ul aria-label="Details" className="flex flex-wrap gap-x-4 text-fg-muted">
              {facts.map((fact) => (
                <li key={fact}>{fact}</li>
              ))}
            </ul>
          )}
          {title.genres.length > 0 && (
            <ul aria-label="Genres" className="flex flex-wrap gap-2">
              {title.genres.map((genre) => (
                <Chip key={genre}>{genre}</Chip>
              ))}
            </ul>
          )}
          {title.overview !== undefined && <p className="text-fg">{title.overview}</p>}
          {title.playable && isPlayable(title.playable.versions) && <PlayButton playableId={title.playable.id} label="Play" />}
        </div>
      </div>
    </div>
  );
}

function LoadError({ error, onRetry }: { error: ApiError; onRetry: () => void }) {
  const { problem } = error;
  return (
    <div role="alert" className="flex flex-col items-start gap-2 p-8">
      <p className="text-lg">{problem.title}</p>
      {problem.detail !== undefined && <p className="text-fg-muted">{problem.detail}</p>}
      {problem.action !== undefined && <p className="text-fg-muted">{problem.action}</p>}
      {error.status === 404 ? (
        <div className="flex gap-3">
          <Link to="/movies" className="rounded-card bg-surface px-3 py-1 shadow-soft hover:text-highlight">
            Go to Movies
          </Link>
          <Link to="/shows" className="rounded-card bg-surface px-3 py-1 shadow-soft hover:text-highlight">
            Go to Shows
          </Link>
        </div>
      ) : (
        <button
          type="button"
          onClick={onRetry}
          className="rounded-card bg-surface px-3 py-1 shadow-soft hover:text-highlight"
        >
          Try again
        </button>
      )}
    </div>
  );
}

/** A title's detail page: hero, metadata, and the movie's versions or the show's seasons and episodes (SPEC §11). */
export function TitleDetail({ titleId }: { titleId: string }) {
  const title = useGetTitle(titleId);

  if (title.isPending) {
    return (
      <p role="status" className="p-8 text-fg-muted">
        Loading title…
      </p>
    );
  }
  if (title.isError) {
    return (
      <LoadError
        error={title.error}
        onRetry={() => {
          void title.refetch();
        }}
      />
    );
  }

  const data = title.data;
  return (
    <article aria-labelledby="title-heading">
      <Hero title={data} />
      <div className="flex flex-col gap-6 px-8 pb-12">
        {data.kind === 'movie' && (
          <section aria-labelledby="versions-heading" className="flex max-w-4xl flex-col gap-3">
            <h2 id="versions-heading" className="text-xl font-semibold">
              Versions
            </h2>
            {data.playable && data.playable.versions.length > 0 ? (
              <VersionList versions={data.playable.versions} label="Versions" />
            ) : (
              <p className="text-fg-muted">No file in the library holds this movie. Scan the library to look for it.</p>
            )}
          </section>
        )}
        {data.kind === 'show' && (
          <section aria-labelledby="seasons-heading" className="flex flex-col gap-3">
            <h2 id="seasons-heading" className="text-xl font-semibold">
              Episodes
            </h2>
            <SeasonTabs seasons={data.seasons ?? []} />
          </section>
        )}
      </div>
    </article>
  );
}
