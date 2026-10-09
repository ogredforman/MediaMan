import { http, HttpResponse } from 'msw';
import type { AttentionFile, CatalogCandidate, MediaFileMatch, MediaFileMatchRequest } from '../api/mediaman.schemas';
import { mockLibrary } from './libraries';

const problem = (body: Record<string, unknown> & { status: number }) =>
  HttpResponse.json(body, { status: body.status, headers: { 'Content-Type': 'application/problem+json' } });

const attentionFileId = (n: number) => `01920000-0000-7000-8000-${String(900 + n).padStart(12, '0')}`;

/** The files needing attention in `mockLibrary` after every `reset`: a doubtful movie, an unmatched episode and an unreadable file. */
export const mockAttentionFiles: AttentionFile[] = [
  {
    mediaFileId: attentionFileId(1),
    path: '/media/Movies/Arrivl.2016.1080p.WEB-DL.mkv',
    fileName: 'Arrivl.2016.1080p.WEB-DL.mkv',
    reason: 'low-confidence',
    parsed: { title: 'Arrivl', year: 2016, episodes: [], resolution: '1080p', source: 'WEB-DL' },
    review: {
      id: '01920000-0000-7000-8000-000000000951',
      reason: 'low-confidence',
      candidates: [
        { titleRef: 'tmdb:movie/329865', score: 0.71, kind: 'movie', name: 'Arrival', year: 2016 },
        { titleRef: 'tmdb:movie/10688', score: 0.52, kind: 'movie', name: 'The Arrival', year: 1996 },
      ],
    },
  },
  {
    mediaFileId: attentionFileId(2),
    path: '/media/Shows/Office/Office.S01E03.mkv',
    fileName: 'Office.S01E03.mkv',
    reason: 'no-match',
    parsed: { title: 'Office', season: 1, episodes: [3] },
    review: { id: '01920000-0000-7000-8000-000000000952', reason: 'no-match', candidates: [] },
  },
  {
    mediaFileId: attentionFileId(3),
    path: '/media/Movies/broken.mkv',
    fileName: 'broken.mkv',
    reason: 'unreadable',
    parsed: { title: 'broken', episodes: [] },
  },
];

// No poster URLs: mock mode makes no outbound requests.
export const mockCatalog: CatalogCandidate[] = [
  { titleRef: 'tmdb:movie/329865', kind: 'movie', name: 'Arrival', year: 2016, overview: 'A linguist works with the military to communicate with alien lifeforms.' },
  { titleRef: 'tmdb:movie/10688', kind: 'movie', name: 'The Arrival', year: 1996 },
  { titleRef: 'tmdb:movie/438631', kind: 'movie', name: 'Dune', year: 2021 },
  { titleRef: 'tmdb:tv/2316', kind: 'show', name: 'The Office', year: 2005, overview: 'A mockumentary on a group of typical office workers.' },
  { titleRef: 'tmdb:tv/2996', kind: 'show', name: 'The Office', year: 2001 },
];

// The `mockTitles` ids in handlers.ts (not imported, as handlers.ts imports this file); other titles land on Arrival.
const titleIds: Record<string, string> = {
  'tmdb:movie/329865': '01920000-0000-7000-8000-000000000101',
  'tmdb:movie/438631': '01920000-0000-7000-8000-000000000102',
  'tmdb:tv/2316': '01920000-0000-7000-8000-000000000201',
  'tmdb:tv/2996': '01920000-0000-7000-8000-000000000201',
};
const fallbackTitleId = '01920000-0000-7000-8000-000000000101';

const invalid = (errors: Record<string, string[]>) =>
  problem({
    title: "That match can't be used.",
    status: 400,
    code: 'match.invalid',
    detail: 'One or more fields are invalid; each one says why.',
    action: 'Fix the highlighted fields, then try again.',
    errors,
  });

/**
 * Files needing attention per library, the catalog search, and relinking with the server's rules: a known title,
 * no season or episodes for a movie, and an episode mapping for a show (sent, or read from the file name).
 * Relinking removes the file from attention. `reset` restores `mockAttentionFiles` in `mockLibrary`.
 */
export function createAttentionStore() {
  let files = new Map<string, AttentionFile[]>();

  const reset = () => {
    files = new Map([[mockLibrary.id, [...mockAttentionFiles]]]);
  };
  reset();

  const find = (mediaFileId: string) => [...files.values()].flat().find((f) => f.mediaFileId === mediaFileId);

  const handlers = [
    http.get('*/libraries/:libraryId/attention', ({ params }) =>
      HttpResponse.json({ items: files.get(String(params.libraryId)) ?? [], pageInfo: {} }),
    ),
    http.get('*/catalog/search', ({ request }) => {
      const search = new URL(request.url).searchParams;
      const query = (search.get('query') ?? '').trim().toLowerCase();
      const kind = search.get('kind');
      if (query === '') {
        return problem({
          title: 'Enter something to search for.',
          status: 400,
          code: 'catalog.invalid',
          action: "Type the title's name, then search.",
          errors: { query: ['Enter the title to search for.'] },
        });
      }
      const words = query.split(/\s+/);
      const items = mockCatalog.filter(
        (c) => (kind === null || c.kind === kind) && words.some((w) => c.name.toLowerCase().includes(w)),
      );
      return HttpResponse.json({ items });
    }),
    http.put('*/media-files/:mediaFileId/match', async ({ params, request }) => {
      const file = find(String(params.mediaFileId));
      if (file === undefined) {
        return problem({
          title: 'File not found.',
          status: 404,
          code: 'media_file.not_found',
          detail: "The file isn't in the library any more.",
          action: 'Reload the page.',
        });
      }
      if (file.reason === 'unreadable') {
        return problem({
          title: "This file can't be matched.",
          status: 409,
          code: 'match.file_unavailable',
          detail: "MediaMan couldn't read the file.",
          action: 'Check that it plays, then scan the library again.',
        });
      }
      const body = (await request.json()) as MediaFileMatchRequest;
      const title = mockCatalog.find((c) => c.titleRef === body.titleRef);
      if (title === undefined) return invalid({ titleRef: ['Pick a title from the search results.'] });
      if (title.kind === 'movie' && (body.season !== undefined || body.episodes !== undefined)) {
        return invalid({ season: ["A movie doesn't have seasons or episodes."] });
      }
      if (title.kind === 'show') {
        const episodes = body.episodes ?? file.parsed?.episodes ?? [];
        if (body.episodes !== undefined && body.season === undefined) return invalid({ season: ['Enter the season.'] });
        if (episodes.length === 0) return invalid({ episodes: ['Enter the episodes this file holds.'] });
      }
      for (const [libraryId, list] of files) files.set(libraryId, list.filter((f) => f !== file));
      const match: MediaFileMatch = {
        mediaFileId: file.mediaFileId,
        titleId: titleIds[title.titleRef] ?? fallbackTitleId,
        status: 'matched',
      };
      return HttpResponse.json(match);
    }),
  ];

  return { handlers, reset };
}

export const mockAttention = createAttentionStore();
