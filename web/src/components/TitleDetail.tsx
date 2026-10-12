import { useQueryClient } from '@tanstack/react-query';
import { Link, useNavigate } from '@tanstack/react-router';
import { useId, useRef, useState, type KeyboardEvent } from 'react';
import {
  getGetTitleQueryKey,
  useClearPreferredVersion,
  useGetTitle,
  useSetPreferredVersion,
} from '../api/mediaman';
import type { Episode, MediaVersion, Season, TitleDetail as Title, VersionPreference } from '../api/mediaman.schemas';
import { useRefetchAfterPlayback } from '../lib/desktopPlayback';
import { heroTint } from '../lib/heroTint';
import { ApiError, artworkSrc } from '../lib/http';
import { formatDuration, formatSize, versionTags } from '../lib/mediaFormat';
import type { FixMatchFile } from '../lib/fixMatch';
import { FixMatchDialog } from './FixMatchDialog';
import { PlayButton } from './PlayButton';
import { buttonClass } from './formClasses';

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

/** Picking the version Play uses, shared by every version list on the page (SPEC §7.4 step 2). */
interface VersionChoice {
  pick: (mediaFileId: string) => void;
  reset: () => void;
  pending: boolean;
  error: ApiError | undefined;
}

function useVersionChoice(titleId: string): VersionChoice {
  const queryClient = useQueryClient();
  // Returning the refetch keeps the mutation pending until the page shows the new choice.
  const onSuccess = () => queryClient.invalidateQueries({ queryKey: getGetTitleQueryKey(titleId) });
  const set = useSetPreferredVersion({ mutation: { onSuccess } });
  const clear = useClearPreferredVersion({ mutation: { onSuccess } });
  const error = set.error ?? clear.error;
  return {
    pick: (mediaFileId) => {
      clear.reset();
      set.mutate({ titleId, data: { mediaFileId } });
    },
    reset: () => {
      set.reset();
      clear.mutate({ titleId });
    },
    pending: set.isPending || clear.isPending,
    error: error instanceof ApiError ? error : undefined,
  };
}

function VersionList({
  versions,
  label,
  sharedWith,
  selectedMediaFileId,
  choice,
}: {
  versions: MediaVersion[];
  label: string;
  sharedWith?: Map<string, number[]>;
  selectedMediaFileId?: string;
  choice?: VersionChoice;
}) {
  // With a single file there is nothing to choose, so the list stays as it was. A file in several parts lists each part,
  // but is marked and picked once, on its first part.
  const picking = choice !== undefined && new Set(versions.map((v) => v.mediaFileId)).size > 1;
  const firstParts = new Set(versions.filter((v, i) => versions.findIndex((o) => o.mediaFileId === v.mediaFileId) === i));
  return (
    <ul aria-label={label} className="flex flex-col gap-2">
      {versions.map((version) => {
        const status = statusLabels[version.status];
        const episodes = sharedWith?.get(version.mediaFileId);
        const selected = version.mediaFileId === selectedMediaFileId;
        const offerChoice = picking && firstParts.has(version);
        return (
          <li
            key={`${version.mediaFileId}-${String(version.part)}`}
            className={`rounded-card bg-surface px-4 py-3 shadow-soft ${offerChoice && selected ? 'ring-2 ring-accent' : ''}`}
          >
            <div className="flex items-center gap-3">
              <p className="min-w-0 flex-1 truncate font-medium" title={version.fileName}>
                {version.fileName}
              </p>
              {offerChoice && selected && (
                <span className="shrink-0 rounded-card bg-accent px-2 py-0.5 text-xs font-semibold text-fg">Plays</span>
              )}
              {offerChoice && !selected && version.status === 'matched' && (
                <button
                  type="button"
                  aria-label={`Use this version: ${version.fileName}`}
                  disabled={choice.pending}
                  onClick={() => {
                    choice.pick(version.mediaFileId);
                  }}
                  className={`${buttonClass} shrink-0 bg-bg px-3 py-1 text-sm text-highlight`}
                >
                  Use this version
                </button>
              )}
            </div>
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

function EpisodeList({ season, choice }: { season: Season; choice: VersionChoice }) {
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
                <VersionList
                  versions={episode.versions}
                  label={`Versions of ${name}`}
                  sharedWith={shared}
                  selectedMediaFileId={episode.selectedMediaFileId}
                  choice={choice}
                />
              )}
              {episode.playableId !== undefined && isPlayable(episode.versions) && (
                <PlayButton
                  playableId={episode.playableId}
                  target={{ name, resumeSeconds: episode.resumeSeconds, watched: episode.watched }}
                />
              )}
            </div>
          </li>
        );
      })}
    </ol>
  );
}

/** Season tabs (WAI-ARIA tabs: arrow keys, Home and End move between seasons). */
function SeasonTabs({ seasons, choice }: { seasons: Season[]; choice: VersionChoice }) {
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
        <EpisodeList season={selected} choice={choice} />
      </div>
    </div>
  );
}

/** A version can be played unless its file is missing; the server picks which one (SPEC §7.4). */
function isPlayable(versions: MediaVersion[]): boolean {
  return versions.some((v) => v.status !== 'missing');
}

const pad = (n: number) => String(n).padStart(2, '0');

/** The title's files that can be relinked (not missing), each once, with the season and episodes it holds. */
function relinkableFiles(title: Title): FixMatchFile[] {
  const files = new Map<string, FixMatchFile>();
  for (const version of title.playable?.versions ?? []) {
    if (version.status !== 'missing') files.set(version.mediaFileId, { mediaFileId: version.mediaFileId, fileName: version.fileName });
  }
  for (const season of orderSeasons(title.seasons ?? [])) {
    for (const [mediaFileId, numbers] of episodesByFile(season.episodes)) {
      const version = season.episodes.flatMap((e) => e.versions).find((v) => v.mediaFileId === mediaFileId);
      if (version === undefined || version.status === 'missing' || files.has(mediaFileId)) continue;
      const episodes = [...numbers].sort((a, b) => a - b);
      const first = episodes[0] ?? 0;
      const last = episodes[episodes.length - 1] ?? 0;
      const label = `S${pad(season.number)}E${pad(first)}${last === first ? '' : `–E${pad(last)}`}`;
      files.set(mediaFileId, { mediaFileId, fileName: version.fileName, label, season: season.number, episodes });
    }
  }
  return [...files.values()];
}

/** "Fix match" on any title (SPEC §7.3): relinks one of its files, then opens the title the file now belongs to. */
function FixMatchButton({ title }: { title: Title }) {
  const [open, setOpen] = useState(false);
  const navigate = useNavigate();
  const files = relinkableFiles(title);
  if (files.length === 0) return null;
  return (
    <>
      <button type="button" onClick={() => { setOpen(true); }} className={`${buttonClass} bg-surface text-fg shadow-soft`}>
        Fix match
      </button>
      {open && (
        <FixMatchDialog
          files={files}
          search={{ query: title.name, year: title.year, kind: title.kind }}
          onClose={() => { setOpen(false); }}
          onMatched={async (match) => {
            setOpen(false);
            await navigate({ to: '/titles/$titleId', params: { titleId: match.titleId } });
          }}
        />
      )}
    </>
  );
}

function Hero({ title }: { title: Title }) {
  const style = heroTint(title.backdrop, title.poster);
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
          <div className="flex flex-wrap items-start gap-3">
            {title.playable && isPlayable(title.playable.versions) && (
              <PlayButton
                playableId={title.playable.id}
                target={{ resumeSeconds: title.playable.resumeSeconds, watched: title.playable.watched }}
              />
            )}
            <FixMatchButton title={title} />
          </div>
        </div>
      </div>
    </div>
  );
}

function qualityNote(preference: VersionPreference): string {
  const quality = [preference.resolution, preference.hdrFormat].filter((part) => part !== undefined).join(' ');
  return quality === ''
    ? 'Episodes play at the quality you picked when there is a version in it, otherwise the best quality.'
    : `Episodes play in ${quality} when there is a version in it, otherwise the best quality.`;
}

/** "Reset to best quality" once the profile has picked a version, and what went wrong saving a choice. */
function VersionChoiceBar({ title, choice }: { title: Title; choice: VersionChoice }) {
  const preference = title.versionPreference;
  const problem = choice.error?.problem;
  return (
    <>
      {preference !== undefined && (
        <div className="flex flex-wrap items-center gap-3">
          <button
            type="button"
            disabled={choice.pending}
            onClick={choice.reset}
            className={`${buttonClass} bg-surface text-fg shadow-soft`}
          >
            Reset to best quality
          </button>
          {title.kind === 'show' && <p className="text-sm text-fg-muted">{qualityNote(preference)}</p>}
        </div>
      )}
      {choice.error !== undefined && (
        <div role="alert" className="flex flex-col gap-1 text-sm">
          <p>{problem?.title ?? "Couldn't save which version plays."}</p>
          {problem?.detail !== undefined && <p className="text-fg-muted">{problem.detail}</p>}
          <p className="text-fg-muted">{problem?.action ?? 'Check that the server is running, then try again.'}</p>
        </div>
      )}
    </>
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

/**
 * A title's detail page: hero, metadata, and the movie's versions or the show's seasons and episodes, with the version
 * Play uses marked and a picker for it (SPEC §11).
 */
export function TitleDetail({ titleId }: { titleId: string }) {
  const title = useGetTitle(titleId);
  const choice = useVersionChoice(titleId);
  useRefetchAfterPlayback(getGetTitleQueryKey(titleId));

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
            <VersionChoiceBar title={data} choice={choice} />
            {data.playable && data.playable.versions.length > 0 ? (
              <VersionList
                versions={data.playable.versions}
                label="Versions"
                selectedMediaFileId={data.playable.selectedMediaFileId}
                choice={choice}
              />
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
            <VersionChoiceBar title={data} choice={choice} />
            <SeasonTabs seasons={data.seasons ?? []} choice={choice} />
          </section>
        )}
      </div>
    </article>
  );
}
