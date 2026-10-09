import { useInfiniteQuery, type InfiniteData } from '@tanstack/react-query';
import { Link } from '@tanstack/react-router';
import { useState } from 'react';
import { getListAttentionFilesQueryKey, listAttentionFiles, useListLibraries } from '../api/mediaman';
import type { AttentionFile, AttentionFilePage, Library, MatchCandidate, ParsedGuess, TitleKind } from '../api/mediaman.schemas';
import type { ApiError } from '../lib/http';
import { pickLabel, useRelink, type FixMatchFile, type FixMatchSearch, type MatchPick } from '../lib/fixMatch';
import { FixMatchDialog, PickPoster } from './FixMatchDialog';
import { buttonClass } from './formClasses';

const reasons: Record<string, string> = {
  unreadable: "MediaMan couldn't read this file. Check that it plays and isn't still copying, then scan the library again.",
  'no-match': 'No title matched the file name. Search for it.',
  'low-confidence': "The best match isn't certain. Confirm a suggestion or search.",
  ambiguous: 'Several titles match equally well. Pick the right one or search.',
  'episode-not-found': "The show doesn't list this episode. Search again, or set the season and episode.",
};

const pad = (n: number) => String(n).padStart(2, '0');

/** What the parser read, e.g. "Arrivl (2016) · 1080p · WEB-DL" or "Office · S01E03". */
function guessText(parsed: ParsedGuess): string {
  const name = parsed.title === undefined ? undefined : pickLabel({ name: parsed.title, year: parsed.year });
  const first = parsed.episodes[0];
  const last = parsed.episodes[parsed.episodes.length - 1];
  const episode =
    parsed.season !== undefined && first !== undefined && last !== undefined
      ? `S${pad(parsed.season)}E${pad(first)}${last === first ? '' : `–E${pad(last)}`}`
      : parsed.absoluteEpisode !== undefined
        ? `Episode ${String(parsed.absoluteEpisode)}`
        : undefined;
  return [name, episode, parsed.resolution, parsed.source, parsed.hdrFormat, parsed.releaseGroup]
    .filter((part): part is string => part !== undefined)
    .join(' · ');
}

const hasEpisodes = (parsed: ParsedGuess | undefined) =>
  parsed !== undefined && (parsed.episodes.length > 0 || parsed.absoluteEpisode !== undefined);

function kindOf(library: Library, parsed: ParsedGuess | undefined): TitleKind | undefined {
  if (library.contentType === 'movies') return 'movie';
  if (library.contentType === 'shows') return 'show';
  return hasEpisodes(parsed) || parsed?.season !== undefined ? 'show' : undefined;
}

function toPick(candidate: MatchCandidate, fallbackKind: TitleKind | undefined): MatchPick | undefined {
  const kind = candidate.kind ?? fallbackKind;
  return kind === undefined ? undefined : { ...candidate, kind, name: candidate.name ?? candidate.titleRef };
}

export interface Linked {
  fileName: string;
  name: string;
  titleId: string;
}

export interface FixRequest {
  file: FixMatchFile;
  search: FixMatchSearch;
  picked?: MatchPick;
}

function AttentionCard({
  file,
  library,
  onFix,
  onLinked,
}: {
  file: AttentionFile;
  library: Library;
  onFix: (request: FixRequest) => void;
  onLinked: (linked: Linked) => void;
}) {
  const kind = kindOf(library, file.parsed);
  const fixFile: FixMatchFile = {
    mediaFileId: file.mediaFileId,
    fileName: file.fileName,
    season: file.parsed?.season,
    episodes: file.parsed?.episodes,
  };
  const search: FixMatchSearch = { query: file.parsed?.title ?? '', year: file.parsed?.year, kind };
  const candidates = (file.review?.candidates ?? []).flatMap((c) => {
    const pick = toPick(c, kind);
    return pick === undefined ? [] : [pick];
  });
  const unreadable = file.reason === 'unreadable';
  const relink = useRelink((match, { data }) => {
    const pick = candidates.find((c) => c.titleRef === data.titleRef);
    onLinked({ fileName: file.fileName, name: pick === undefined ? data.titleRef : pickLabel(pick), titleId: match.titleId });
  });

  // One click links a movie, or a show when the file name says which episodes; otherwise the mapping is asked for.
  const confirm = (pick: MatchPick) => {
    if (pick.kind === 'show' && !hasEpisodes(file.parsed)) {
      onFix({ file: fixFile, search, picked: pick });
      return;
    }
    relink.mutate({ mediaFileId: file.mediaFileId, data: { titleRef: pick.titleRef } });
  };

  return (
    <li aria-label={file.fileName} className="flex flex-col gap-3 rounded-card bg-surface p-4 shadow-soft">
      <div className="flex flex-col gap-1">
        <p className="truncate font-medium" title={file.path}>
          {file.fileName}
        </p>
        <p className="truncate text-xs text-fg-muted">{file.path}</p>
        {file.parsed !== undefined && guessText(file.parsed) !== '' && (
          <p className="text-sm">
            <span className="text-fg-muted">Looks like: </span>
            {guessText(file.parsed)}
          </p>
        )}
        <p className="text-sm text-fg-muted">{reasons[file.reason] ?? `Needs attention (${file.reason}). Search for the right title.`}</p>
      </div>
      {!unreadable && candidates.length > 0 && (
        <ul aria-label={`Suggestions for ${file.fileName}`} className="flex flex-wrap gap-3">
          {candidates.map((pick) => (
            <li key={pick.titleRef}>
              <button
                type="button"
                aria-label={`Link to ${pickLabel(pick)}`}
                disabled={relink.isPending}
                onClick={() => { confirm(pick); }}
                className="flex w-28 flex-col gap-1 rounded-card p-1 text-left text-sm transition-colors duration-(--motion-fast) hover:text-highlight focus-visible:outline-2 focus-visible:outline-highlight disabled:opacity-50"
              >
                <PickPoster pick={pick} className="w-full" />
                <span className="line-clamp-2">{pickLabel(pick)}</span>
              </button>
            </li>
          ))}
        </ul>
      )}
      {relink.isPending && (
        <p role="status" className="text-sm text-fg-muted">
          Linking…
        </p>
      )}
      {relink.isError && (
        <p role="alert" className="text-sm text-highlight">
          {relink.error.problem.title} {relink.error.problem.action}
        </p>
      )}
      {!unreadable && (
        <button
          type="button"
          aria-label={`Search for ${file.fileName}`}
          onClick={() => { onFix({ file: fixFile, search }); }}
          className={`${buttonClass} self-start bg-bg text-fg`}
        >
          Search
        </button>
      )}
    </li>
  );
}

function LibraryAttention({
  library,
  onFix,
  onLinked,
}: {
  library: Library;
  onFix: (request: FixRequest) => void;
  onLinked: (linked: Linked) => void;
}) {
  const files = useInfiniteQuery<AttentionFilePage, ApiError, InfiniteData<AttentionFilePage>, readonly unknown[], string | undefined>({
    queryKey: [...getListAttentionFilesQueryKey(library.id), 'infinite'],
    queryFn: ({ pageParam, signal }) =>
      listAttentionFiles(library.id, pageParam === undefined ? undefined : { cursor: pageParam }, { signal }),
    initialPageParam: undefined,
    getNextPageParam: (last) => last.pageInfo.nextCursor,
  });
  const items = files.data?.pages.flatMap((page) => page.items) ?? [];
  const headingId = `attention-${library.id}`;

  return (
    <section aria-labelledby={headingId} className="flex flex-col gap-3">
      <h2 id={headingId} className="text-xl font-semibold">
        {library.name}
      </h2>
      {files.isPending && (
        <p role="status" className="text-fg-muted">
          Loading files…
        </p>
      )}
      {files.isError && (
        <div role="alert" className="flex flex-col items-start gap-2">
          <p>
            {files.error.problem.title} {files.error.problem.action}
          </p>
          <button type="button" onClick={() => { void files.refetch(); }} className={`${buttonClass} bg-surface text-fg`}>
            Try again
          </button>
        </div>
      )}
      {files.isSuccess && items.length === 0 && <p className="text-fg-muted">Nothing in {library.name} needs attention.</p>}
      {items.length > 0 && (
        <ul aria-label={`Files in ${library.name} that need attention`} className="grid gap-4 lg:grid-cols-2">
          {items.map((file) => (
            <AttentionCard key={file.mediaFileId} file={file} library={library} onFix={onFix} onLinked={onLinked} />
          ))}
        </ul>
      )}
      {files.hasNextPage && (
        <button
          type="button"
          disabled={files.isFetchingNextPage}
          onClick={() => { void files.fetchNextPage(); }}
          className={`${buttonClass} self-start bg-surface text-fg`}
        >
          {files.isFetchingNextPage ? 'Loading…' : 'Load more'}
        </button>
      )}
    </section>
  );
}

/** "Needs attention" (SPEC §7.3, §11): per library, the files the scan couldn't link, with one-click confirm or search. */
export function AttentionInbox() {
  const libraries = useListLibraries();
  const [fixing, setFixing] = useState<FixRequest>();
  const [linked, setLinked] = useState<Linked>();

  return (
    <section aria-labelledby="attention-heading" className="flex flex-col gap-6 p-8">
      <div className="flex flex-col gap-1">
        <h1 id="attention-heading" className="text-3xl font-semibold">
          Needs attention
        </h1>
        <p className="text-fg-muted">Files the scan couldn't match on its own. Your choice is remembered when the library is scanned again.</p>
      </div>
      {linked !== undefined && (
        <p role="status" className="flex flex-wrap gap-2">
          <span>
            Linked {linked.fileName} to {linked.name}.
          </span>
          <Link to="/titles/$titleId" params={{ titleId: linked.titleId }} className="text-highlight underline">
            Open {linked.name}
          </Link>
        </p>
      )}
      {libraries.isPending && (
        <p role="status" className="text-fg-muted">
          Loading libraries…
        </p>
      )}
      {libraries.isError && (
        <p role="alert">
          {libraries.error.problem.title} {libraries.error.problem.action}
        </p>
      )}
      {libraries.isSuccess && libraries.data.items.length === 0 && (
        <p className="text-fg-muted">
          There are no libraries yet. <Link to="/settings/libraries" className="text-highlight underline">Add one in Settings</Link>.
        </p>
      )}
      {libraries.data?.items.map((library) => (
        <LibraryAttention key={library.id} library={library} onFix={setFixing} onLinked={setLinked} />
      ))}
      {fixing !== undefined && (
        <FixMatchDialog
          files={[fixing.file]}
          search={fixing.search}
          picked={fixing.picked}
          onClose={() => { setFixing(undefined); }}
          onMatched={(match, pick, file) => {
            setFixing(undefined);
            setLinked({ fileName: file.fileName, name: pickLabel(pick), titleId: match.titleId });
          }}
        />
      )}
    </section>
  );
}
