import { http, HttpResponse } from 'msw';
import {
  getCompleteSetupMockHandler,
  getGetHealthMockHandler,
  getListLibrariesMockHandler,
  getListTitlesMockHandler,
  getLoginMockHandler,
  getRefreshTokensMockHandler,
  getMediaManAPIMock,
  getStartLibraryScanMockHandler,
} from '../api/mediaman.msw';
import { mockProfiles } from './profiles';
import type { AuthTokens, Library, MediaVersion, TitleDetail, TitleSummary } from '../api/mediaman.schemas';

export const mockHealth = { status: 'healthy', version: '0.0.0-mock' } as const;

/** Tokens valid for 15 minutes from now (so the client doesn't refresh on every request), tagged to tell them apart. */
export function mockTokens(tag = 'mock'): AuthTokens {
  const now = Date.now();
  return {
    accessToken: `access-${tag}`,
    accessTokenExpiresAt: new Date(now + 15 * 60_000).toISOString(),
    refreshToken: `refresh-${tag}`,
    refreshTokenExpiresAt: new Date(now + 30 * 24 * 60 * 60_000).toISOString(),
  };
}

export const mockLibrary: Library = {
  id: '01920000-0000-7000-8000-000000000001',
  name: 'Media',
  kind: 'external',
  contentType: 'mixed',
  rootPaths: ['/media'],
  scanIntervalMinutes: 0,
  scan: { state: 'idle' },
};

// No poster URLs: mock mode makes no outbound requests, so these render as stand-in cards.
export const mockTitles: TitleSummary[] = [
  { id: '01920000-0000-7000-8000-000000000101', kind: 'movie', name: 'Arrival', year: 2016, genres: ['Drama'] },
  { id: '01920000-0000-7000-8000-000000000102', kind: 'movie', name: 'Dune', year: 2021, genres: ['Science Fiction'] },
  { id: '01920000-0000-7000-8000-000000000201', kind: 'show', name: 'The Office', year: 2005, genres: ['Comedy'] },
];

const [arrival, dune, office] = mockTitles as [TitleSummary, TitleSummary, TitleSummary];
const episodeId = (n: number) => `01920000-0000-7000-8000-${String(300 + n).padStart(12, '0')}`;
const episodePlayableId = (n: number) => `01920000-0000-7000-8000-${String(700 + n).padStart(12, '0')}`;
const fileId = (n: number) => `01920000-0000-7000-8000-${String(400 + n).padStart(12, '0')}`;

/** Detail for each of `mockTitles`: a movie with two versions (one missing), and a show with specials, a multi-episode file and an episode no file holds. */
export const mockTitleDetails: Record<string, TitleDetail> = {
  [arrival.id]: {
    ...arrival,
    overview: 'A linguist works with the military to communicate with alien lifeforms.',
    runtimeMinutes: 116,
    rating: 7.9,
    externalIds: { tmdb: 'movie/329865' },
    playable: {
      id: '01920000-0000-7000-8000-000000000501',
      versions: [
        {
          mediaFileId: fileId(1),
          fileName: 'Arrival.2016.2160p.UHD.BluRay.x265.HDR10-GROUP.mkv',
          sizeBytes: 21_400_000_000,
          status: 'matched',
          part: 1,
          container: 'matroska',
          durationSeconds: 6960,
          width: 3840,
          height: 1600,
          videoCodec: 'hevc',
          hdrFormat: 'HDR10',
          audioCodec: 'truehd',
          audioChannels: 8,
          source: 'BluRay',
          releaseGroup: 'GROUP',
        },
        {
          mediaFileId: fileId(2),
          fileName: 'Arrival (2016) 1080p.mkv',
          sizeBytes: 8_200_000_000,
          status: 'missing',
          part: 1,
          width: 1920,
          height: 800,
          videoCodec: 'h264',
          audioCodec: 'ac3',
          audioChannels: 6,
        },
      ],
    },
  },
  [dune.id]: {
    ...dune,
    runtimeMinutes: 155,
    externalIds: { tmdb: 'movie/438631' },
    playable: { id: '01920000-0000-7000-8000-000000000502', versions: [] },
  },
  [office.id]: {
    ...office,
    overview: 'A mockumentary on a group of typical office workers.',
    externalIds: { tmdb: 'tv/2316' },
    seasons: [
      {
        id: '01920000-0000-7000-8000-000000000600',
        number: 0,
        episodes: [
          {
            id: episodeId(0),
            number: 1,
            name: 'The Accountants',
            playableId: episodePlayableId(0),
            versions: [
              { mediaFileId: fileId(10), fileName: 'The Office S00E01.mkv', sizeBytes: 90_000_000, status: 'matched', part: 1 },
            ],
          },
        ],
      },
      {
        id: '01920000-0000-7000-8000-000000000601',
        number: 1,
        episodes: [
          {
            id: episodeId(1),
            number: 1,
            name: 'Pilot',
            playableId: episodePlayableId(1),
            airDate: '2005-03-24',
            overview: 'A documentary crew arrives at the Scranton branch.',
            versions: [
              { mediaFileId: fileId(11), fileName: 'The.Office.S01E01-E02.720p.WEB-DL.mkv', sizeBytes: 900_000_000, status: 'matched', part: 1, width: 1280, height: 720, source: 'WEB-DL' },
            ],
          },
          {
            id: episodeId(2),
            number: 2,
            name: 'Diversity Day',
            playableId: episodePlayableId(2),
            airDate: '2005-03-29',
            versions: [
              { mediaFileId: fileId(11), fileName: 'The.Office.S01E01-E02.720p.WEB-DL.mkv', sizeBytes: 900_000_000, status: 'matched', part: 2, width: 1280, height: 720, source: 'WEB-DL' },
            ],
          },
          { id: episodeId(3), number: 3, name: 'Health Care', airDate: '2005-04-05', versions: [] },
        ],
      },
      {
        id: '01920000-0000-7000-8000-000000000602',
        number: 2,
        episodes: [
          {
            id: episodeId(4),
            number: 1,
            name: 'The Dundies',
            playableId: episodePlayableId(4),
            versions: [
              { mediaFileId: fileId(12), fileName: 'The.Office.S02E01.mkv', sizeBytes: 400_000_000, status: 'matched', part: 1 },
            ],
          },
        ],
      },
    ],
  },
};

/**
 * A `GET /events` handler whose streams stay open until the client disconnects, plus `send` to push an event to
 * every open stream. Tests make their own with `server.use(stream.handler)`.
 */
export function createEventStream() {
  const encoder = new TextEncoder();
  const streams = new Set<ReadableStreamDefaultController<Uint8Array>>();
  let nextId = 1;

  const handler = http.get('*/events', () => {
    let controller: ReadableStreamDefaultController<Uint8Array> | undefined;
    const body = new ReadableStream<Uint8Array>({
      start(c) {
        controller = c;
        streams.add(c);
        c.enqueue(encoder.encode(': connected\n\n'));
      },
      cancel() {
        if (controller) streams.delete(controller);
      },
    });
    return new HttpResponse(body, { headers: { 'Content-Type': 'text/event-stream', 'Cache-Control': 'no-cache' } });
  });

  return {
    handler,
    get connections() {
      return streams.size;
    },
    send(type: string, data: unknown) {
      const id = String(nextId++);
      const envelope = { id, type, at: new Date().toISOString(), data };
      const frame = encoder.encode(`event: ${type}\nid: ${id}\ndata: ${JSON.stringify(envelope)}\n\n`);
      for (const stream of streams) {
        try {
          stream.enqueue(frame);
        } catch {
          streams.delete(stream); // The client went away without cancelling.
        }
      }
    },
  };
}

export const mockEvents = createEventStream();

/** The versions of a Playable across the mock titles, or undefined for an unknown Playable. */
function mockVersionsOf(playableId: string): MediaVersion[] | undefined {
  for (const detail of Object.values(mockTitleDetails)) {
    if (detail.playable?.id === playableId) return detail.playable.versions;
    for (const episode of (detail.seasons ?? []).flatMap((s) => s.episodes)) {
      if (episode.playableId === playableId) return episode.versions;
    }
  }
  return undefined;
}

// Deterministic overrides first (MSW uses the first matching handler), then the generated defaults.
export const handlers = [
  getGetHealthMockHandler(mockHealth),
  mockEvents.handler,
  getLoginMockHandler(() => {
    mockProfiles.clearPick();
    return mockTokens('signed-in');
  }),
  getRefreshTokensMockHandler(() => mockTokens('refreshed')),
  getCompleteSetupMockHandler(() => {
    mockProfiles.clearPick();
    return mockTokens('setup');
  }),
  ...mockProfiles.handlers,
  getListLibrariesMockHandler({ items: [mockLibrary], pageInfo: {} }),
  getStartLibraryScanMockHandler({ ...mockLibrary, scan: { state: 'queued' } }),
  getListTitlesMockHandler(({ request }) => {
    const kind = new URL(request.url).searchParams.get('kind');
    return { items: mockTitles.filter((t) => kind === null || t.kind === kind), pageInfo: {} };
  }),
  http.get('*/titles/:titleId', ({ params }) => {
    const detail = mockTitleDetails[String(params.titleId)];
    return detail
      ? HttpResponse.json(detail)
      : HttpResponse.json(
          {
            title: 'Title not found.',
            status: 404,
            code: 'title.not_found',
            detail: "This title isn't in the library. It may have been removed or re-matched.",
            action: 'Go back to Movies or Shows and pick it again.',
          },
          { status: 404 },
        );
  }),
  http.post('*/dev/playables/:playableId/play', ({ params }) => {
    const version = mockVersionsOf(String(params.playableId))?.find((v) => v.status !== 'missing');
    return version
      ? HttpResponse.json({ mediaFileId: version.mediaFileId, fileName: version.fileName }, { status: 202 })
      : HttpResponse.json(
          {
            title: 'Nothing to play here.',
            status: 404,
            code: 'playable.not_found',
            detail: "This movie or episode doesn't exist any more. A rescan may have removed it.",
            action: 'Go back to the title and try again.',
          },
          { status: 404 },
        );
  }),
  ...getMediaManAPIMock(),
];
