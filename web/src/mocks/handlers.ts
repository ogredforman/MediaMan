import { http, HttpResponse } from 'msw';
import {
  getGetHealthMockHandler,
  getListLibrariesMockHandler,
  getListTitlesMockHandler,
  getMediaManAPIMock,
  getStartLibraryScanMockHandler,
} from '../api/mediaman.msw';
import type { Library, TitleSummary } from '../api/mediaman.schemas';

export const mockHealth = { status: 'healthy', version: '0.0.0-mock' } as const;

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

// Deterministic overrides first (MSW uses the first matching handler), then the generated defaults.
export const handlers = [
  getGetHealthMockHandler(mockHealth),
  mockEvents.handler,
  getListLibrariesMockHandler({ items: [mockLibrary], pageInfo: {} }),
  getStartLibraryScanMockHandler({ ...mockLibrary, scan: { state: 'queued' } }),
  getListTitlesMockHandler(({ request }) => {
    const kind = new URL(request.url).searchParams.get('kind');
    return { items: mockTitles.filter((t) => kind === null || t.kind === kind), pageInfo: {} };
  }),
  ...getMediaManAPIMock(),
];
