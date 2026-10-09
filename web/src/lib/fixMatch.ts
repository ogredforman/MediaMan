import { useQueryClient, type QueryClient } from '@tanstack/react-query';
import { useRelinkMediaFile, type RelinkMediaFileMutationVariables } from '../api/mediaman';
import type { MediaFileMatch, TitleKind } from '../api/mediaman.schemas';

/** A file to relink, with the season and episodes it's believed to hold (to prefill a show's mapping). */
export interface FixMatchFile {
  mediaFileId: string;
  fileName: string;
  /** Shown in the file picker, e.g. "S01E01–E02". */
  label?: string;
  season?: number;
  episodes?: number[];
}

/** What to search the catalog for first. */
export interface FixMatchSearch {
  query: string;
  year?: number;
  kind?: TitleKind;
}

/** A title the user picked, from a MatchReview candidate or a catalog search. */
export interface MatchPick {
  titleRef: string;
  kind: TitleKind;
  name: string;
  year?: number;
  posterUrl?: string;
  overview?: string;
}

export function pickLabel(pick: { name?: string; year?: number }): string {
  const name = pick.name ?? 'Untitled';
  return pick.year === undefined ? name : `${name} (${String(pick.year)})`;
}

const isAttentionKey = (key: unknown) => typeof key === 'string' && key.startsWith('/libraries/') && key.endsWith('/attention');

/** After a relink: the attention lists, the grids and every title detail may have changed. */
export function invalidateAfterRelink(queryClient: QueryClient) {
  return queryClient.invalidateQueries({
    predicate: ({ queryKey: [first] }) => isAttentionKey(first) || (typeof first === 'string' && first.startsWith('/titles')),
  });
}

/** `PUT /media-files/{id}/match`, then `onMatched` (e.g. navigate), then refreshes what the relink changed. */
export function useRelink(onMatched: (match: MediaFileMatch, variables: RelinkMediaFileMutationVariables) => unknown) {
  const queryClient = useQueryClient();
  return useRelinkMediaFile({
    mutation: {
      onSuccess: async (match, variables) => {
        await onMatched(match, variables);
        await invalidateAfterRelink(queryClient);
      },
    },
  });
}

/** "1", "1-3", "1, 2" or "1–2" → episode numbers in order; undefined if it isn't a list of positive numbers. */
export function parseEpisodes(text: string): number[] | undefined {
  const episodes: number[] = [];
  for (const part of text.split(',').map((p) => p.trim())) {
    const range = /^(\d+)\s*[-–]\s*(\d+)$/.exec(part);
    if (range) {
      const from = Number(range[1]);
      const to = Number(range[2]);
      if (from < 1 || to < from || to - from > 50) return undefined;
      for (let n = from; n <= to; n++) episodes.push(n);
    } else if (/^\d+$/.test(part) && Number(part) >= 1) {
      episodes.push(Number(part));
    } else {
      return undefined;
    }
  }
  return episodes;
}

export function formatEpisodes(episodes: number[] | undefined): string {
  if (episodes === undefined || episodes.length === 0) return '';
  const first = episodes[0] ?? 0;
  const last = episodes[episodes.length - 1] ?? 0;
  const contiguous = episodes.every((n, i) => n === first + i);
  return episodes.length > 1 && contiguous ? `${String(first)}-${String(last)}` : episodes.join(', ');
}
