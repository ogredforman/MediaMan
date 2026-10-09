import { useEffect, useId, useRef, useState, type ReactNode, type SubmitEvent } from 'react';
import { useSearchCatalog } from '../api/mediaman';
import type { MediaFileMatch, MediaFileMatchRequest, SearchCatalogParams, TitleKind } from '../api/mediaman.schemas';
import { formatEpisodes, parseEpisodes, pickLabel, useRelink, type FixMatchFile, type FixMatchSearch, type MatchPick } from '../lib/fixMatch';
import type { ApiError } from '../lib/http';
import { Field, type FieldErrors } from './FormField';
import { buttonClass, inputClass } from './formClasses';

/** Provider poster URLs are loaded straight from the provider; anything that isn't https is left out. */
export function PickPoster({ pick, className }: { pick: { name?: string; posterUrl?: string }; className: string }) {
  const url = pick.posterUrl;
  return (
    <div className={`aspect-[2/3] shrink-0 overflow-hidden rounded-card bg-bg ${className}`}>
      {url?.startsWith('https://') ? (
        <img src={url} alt="" className="h-full w-full object-cover" />
      ) : (
        <div aria-hidden="true" className="flex h-full items-center justify-center p-2 text-center text-xs text-highlight">
          {pick.name}
        </div>
      )}
    </div>
  );
}

const focusable = 'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), [tabindex]:not([tabindex="-1"])';

/** A modal over the page: focus moves to its heading and stays inside; Escape closes it and focus goes back. */
function Modal({ title, onClose, children }: { title: string; onClose: () => void; children: ReactNode }) {
  const headingId = useId();
  const heading = useRef<HTMLHeadingElement>(null);
  const panel = useRef<HTMLDivElement>(null);
  const close = useRef(onClose);
  useEffect(() => {
    close.current = onClose;
  });

  useEffect(() => {
    const opener = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    heading.current?.focus();
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        event.preventDefault();
        close.current();
        return;
      }
      if (event.key !== 'Tab' || panel.current === null) return;
      const items = [...panel.current.querySelectorAll<HTMLElement>(focusable)];
      const first = items[0];
      const last = items[items.length - 1];
      if (first === undefined || last === undefined) return;
      const active = document.activeElement;
      const inside = active !== null && panel.current.contains(active);
      if (event.shiftKey && (!inside || active === first || active === heading.current)) {
        event.preventDefault();
        last.focus();
      } else if (!event.shiftKey && (!inside || active === last)) {
        event.preventDefault();
        first.focus();
      }
    };
    document.addEventListener('keydown', onKeyDown);
    return () => {
      document.removeEventListener('keydown', onKeyDown);
      opener?.focus();
    };
  }, []);

  return (
    <div className="fixed inset-0 z-50 flex items-start justify-center overflow-y-auto bg-bg/80 p-8">
      <div
        ref={panel}
        role="dialog"
        aria-modal="true"
        aria-labelledby={headingId}
        className="flex w-full max-w-3xl flex-col gap-4 rounded-card bg-surface p-6 shadow-soft"
      >
        <div className="flex items-start justify-between gap-4">
          <h2 id={headingId} ref={heading} tabIndex={-1} className="text-xl font-semibold focus:outline-none">
            {title}
          </h2>
          <button type="button" onClick={onClose} className={`${buttonClass} bg-bg text-fg`}>
            Cancel
          </button>
        </div>
        {children}
      </div>
    </div>
  );
}

function ProblemAlert({ error }: { error: ApiError }) {
  const { problem } = error;
  return (
    <div role="alert" className="flex flex-col gap-1 text-sm">
      <p className="text-highlight">{problem.title}</p>
      {problem.detail !== undefined && <p className="text-fg-muted">{problem.detail}</p>}
      {problem.action !== undefined && <p className="text-fg-muted">{problem.action}</p>}
    </div>
  );
}

const kindLabels: Record<TitleKind, string> = { movie: 'Movie', show: 'Show' };

function SearchStep({
  file,
  search,
  onPick,
  pending,
}: {
  file: FixMatchFile;
  search: FixMatchSearch;
  onPick: (pick: MatchPick) => void;
  pending: boolean;
}) {
  const [query, setQuery] = useState(search.query);
  const [year, setYear] = useState(search.year === undefined ? '' : String(search.year));
  const [kind, setKind] = useState<TitleKind | ''>(search.kind ?? '');
  const [errors, setErrors] = useState<FieldErrors>({});
  const initial: SearchCatalogParams | undefined =
    search.query.trim() === '' ? undefined : { query: search.query.trim(), year: search.year, kind: search.kind };
  const [params, setParams] = useState(initial);
  const results = useSearchCatalog(params ?? { query: '' }, { query: { enabled: params !== undefined } });

  const submit = (event: SubmitEvent<HTMLFormElement>) => {
    event.preventDefault();
    const next: FieldErrors = {};
    const trimmed = query.trim();
    if (trimmed === '') next.query = ["Enter the title's name."];
    const yearValue = year.trim() === '' ? undefined : Number(year);
    if (yearValue !== undefined && (!Number.isInteger(yearValue) || yearValue < 1870 || yearValue > 2200)) {
      next.year = ['Enter a year like 2016, or leave it empty.'];
    }
    setErrors(next);
    if (Object.keys(next).length > 0) return;
    setParams({ query: trimmed, year: yearValue, kind: kind === '' ? undefined : kind });
  };

  return (
    <div className="flex flex-col gap-4">
      <p className="truncate text-sm text-fg-muted" title={file.fileName}>
        File: {file.fileName}
      </p>
      <form role="search" aria-label="Search the catalog" onSubmit={submit} className="flex flex-wrap items-end gap-3">
        <div className="min-w-48 flex-1">
          <Field label="Title" field="query" errors={errors}>
            <input value={query} onChange={(e) => { setQuery(e.target.value); }} className={inputClass} />
          </Field>
        </div>
        <div className="w-24">
          <Field label="Year" field="year" errors={errors}>
            <input value={year} inputMode="numeric" onChange={(e) => { setYear(e.target.value); }} className={`${inputClass} w-full`} />
          </Field>
        </div>
        <Field label="Type" field="kind" errors={errors}>
          <select value={kind} onChange={(e) => { setKind(e.target.value as TitleKind | ''); }} className={inputClass}>
            <option value="">Movies and shows</option>
            <option value="movie">Movies</option>
            <option value="show">Shows</option>
          </select>
        </Field>
        <button type="submit" className={`${buttonClass} bg-highlight text-bg`}>
          Search
        </button>
      </form>
      {params !== undefined && results.isFetching && (
        <p role="status" className="text-sm text-fg-muted">
          Searching…
        </p>
      )}
      {results.isError && <ProblemAlert error={results.error} />}
      {results.isSuccess && results.data.items.length === 0 && (
        <p role="status" className="text-sm text-fg-muted">
          Nothing in the catalog matches “{params?.query}”. Try a shorter name, or leave out the year.
        </p>
      )}
      {results.isSuccess && results.data.items.length > 0 && (
        <ul aria-label="Search results" className="flex flex-col gap-2">
          {results.data.items.map((item) => (
            <li key={item.titleRef}>
              <button
                type="button"
                disabled={pending}
                onClick={() => { onPick(item); }}
                className="flex w-full items-start gap-4 rounded-card bg-bg p-3 text-left transition-colors duration-(--motion-fast) hover:text-highlight focus-visible:outline-2 focus-visible:outline-highlight disabled:opacity-50"
              >
                <PickPoster pick={item} className="w-14" />
                <span className="flex min-w-0 flex-col gap-1">
                  <span className="font-medium">{pickLabel(item)}</span>
                  <span className="text-sm text-fg-muted">{kindLabels[item.kind]}</span>
                  {item.overview !== undefined && <span className="line-clamp-2 text-sm text-fg-muted">{item.overview}</span>}
                </span>
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

function MappingStep({
  file,
  pick,
  relink,
  onBack,
}: {
  file: FixMatchFile;
  pick: MatchPick;
  relink: ReturnType<typeof useRelink>;
  onBack: () => void;
}) {
  const [season, setSeason] = useState(file.season === undefined ? '' : String(file.season));
  const [episodes, setEpisodes] = useState(formatEpisodes(file.episodes));
  const [errors, setErrors] = useState<FieldErrors>({});
  const shownFields = ['season', 'episodes'];

  const submit = (event: SubmitEvent<HTMLFormElement>) => {
    event.preventDefault();
    const next: FieldErrors = {};
    const seasonText = season.trim();
    const episodesText = episodes.trim();
    const seasonValue = seasonText === '' ? undefined : Number(seasonText);
    const episodeValues = episodesText === '' ? undefined : parseEpisodes(episodesText);
    if (seasonValue !== undefined && (!Number.isInteger(seasonValue) || seasonValue < 0)) {
      next.season = ['Enter a season number, or 0 for specials.'];
    }
    if (episodesText !== '' && episodeValues === undefined) next.episodes = ['Enter episode numbers like 3, 1-2 or 1, 2.'];
    if (seasonValue !== undefined && episodesText === '') next.episodes = ['Enter the episodes this file holds.'];
    if (seasonValue === undefined && episodesText !== '') next.season = ['Enter the season these episodes are in.'];
    setErrors(next);
    if (Object.keys(next).length > 0) return;
    // Both empty: the server reads the season and episodes from the file name.
    const data: MediaFileMatchRequest =
      seasonValue === undefined ? { titleRef: pick.titleRef } : { titleRef: pick.titleRef, season: seasonValue, episodes: episodeValues };
    relink.mutate(
      { mediaFileId: file.mediaFileId, data },
      {
        onError: (error) => {
          if (error.problem.code === 'match.invalid') setErrors(error.problem.errors ?? {});
        },
      },
    );
  };

  const problemErrors = relink.error?.problem.errors ?? {};
  const onFields =
    relink.error?.problem.code === 'match.invalid' &&
    Object.keys(problemErrors).length > 0 &&
    Object.keys(problemErrors).every((key) => shownFields.includes(key));

  return (
    <form onSubmit={submit} className="flex flex-col gap-4">
      <div className="flex items-start gap-4">
        <PickPoster pick={pick} className="w-16" />
        <div className="flex flex-col gap-1">
          <p className="font-medium">{pickLabel(pick)}</p>
          <p className="text-sm text-fg-muted">
            Which episodes are in <span className="text-fg">{file.fileName}</span>? Leave both empty to use what the file name
            says.
          </p>
        </div>
      </div>
      <div className="flex flex-wrap gap-3">
        <div className="w-28">
          <Field label="Season" field="season" errors={errors} hint="0 for specials">
            <input value={season} inputMode="numeric" onChange={(e) => { setSeason(e.target.value); }} className={`${inputClass} w-full`} />
          </Field>
        </div>
        <div className="w-48">
          <Field label="Episodes" field="episodes" errors={errors} hint="e.g. 3, or 1-2 for two in one file">
            <input value={episodes} onChange={(e) => { setEpisodes(e.target.value); }} className={`${inputClass} w-full`} />
          </Field>
        </div>
      </div>
      {relink.isError && !onFields && <ProblemAlert error={relink.error} />}
      <div className="flex gap-3">
        <button type="button" onClick={onBack} className={`${buttonClass} bg-bg text-fg`}>
          Back to search
        </button>
        <button type="submit" disabled={relink.isPending} className={`${buttonClass} bg-highlight text-bg`}>
          {relink.isPending ? 'Linking…' : 'Link file'}
        </button>
      </div>
    </form>
  );
}

/**
 * "Fix match" (SPEC §7.3): pick the file (when there are several), search the catalog, pick a title and, for a
 * show, confirm the season and episodes; then the file is relinked and the choice kept for its path.
 */
export function FixMatchDialog({
  files,
  search,
  picked,
  onClose,
  onMatched,
}: {
  files: FixMatchFile[];
  search: FixMatchSearch;
  /** Starts at the show's mapping step with this title. */
  picked?: MatchPick;
  onClose: () => void;
  onMatched: (match: MediaFileMatch, pick: MatchPick, file: FixMatchFile) => unknown;
}) {
  const [file, setFile] = useState(files.length === 1 ? files[0] : undefined);
  const [pick, setPick] = useState(picked);
  // The pick and file the mutation was sent for, not the ones in an older render's closure.
  const sent = useRef<{ pick: MatchPick; file: FixMatchFile }>(undefined);
  const relink = useRelink((match) => (sent.current === undefined ? undefined : onMatched(match, sent.current.pick, sent.current.file)));
  useEffect(() => {
    sent.current = file !== undefined && pick !== undefined ? { pick, file } : undefined;
  });

  const choose = (next: MatchPick) => {
    if (file === undefined) return;
    setPick(next);
    relink.reset();
    sent.current = { pick: next, file };
    if (next.kind === 'movie') relink.mutate({ mediaFileId: file.mediaFileId, data: { titleRef: next.titleRef } });
  };

  return (
    <Modal title="Fix match" onClose={onClose}>
      {file === undefined ? (
        <div className="flex flex-col gap-3">
          <p className="text-fg-muted">Which file is matched wrong?</p>
          <ul aria-label="Files" className="flex flex-col gap-2">
            {files.map((f) => (
              <li key={f.mediaFileId}>
                <button
                  type="button"
                  onClick={() => { setFile(f); }}
                  className="flex w-full flex-col rounded-card bg-bg px-4 py-3 text-left transition-colors duration-(--motion-fast) hover:text-highlight focus-visible:outline-2 focus-visible:outline-highlight"
                >
                  <span className="truncate font-medium">{f.fileName}</span>
                  {f.label !== undefined && <span className="text-sm text-fg-muted">{f.label}</span>}
                </button>
              </li>
            ))}
          </ul>
        </div>
      ) : pick?.kind === 'show' ? (
        <MappingStep
          file={file}
          pick={pick}
          relink={relink}
          onBack={() => {
            setPick(undefined);
            relink.reset();
          }}
        />
      ) : (
        <>
          <SearchStep file={file} search={search} onPick={choose} pending={relink.isPending} />
          {relink.isPending && (
            <p role="status" className="text-sm text-fg-muted">
              Linking…
            </p>
          )}
          {relink.isError && <ProblemAlert error={relink.error} />}
        </>
      )}
    </Modal>
  );
}
