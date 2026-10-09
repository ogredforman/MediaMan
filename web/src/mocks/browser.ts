import { http, HttpResponse } from 'msw';
import { setupWorker } from 'msw/browser';
import type { ScanState } from '../api/mediaman.schemas';
import { handlers, mockEvents } from './handlers';
import { mockLibraries } from './libraries';

// Mock mode only: a Scan click plays a short scan over the event stream, so live progress can be seen without a server.
const simulatedScan = http.post('*/libraries/:libraryId/scan', ({ params }) => {
  const libraryId = String(params.libraryId);
  const library = mockLibraries.get(libraryId);
  if (library === undefined) return undefined; // The store's handler answers with a 404.
  const startedAt = new Date().toISOString();
  const steps: ScanState[] = [
    { state: 'running', phase: 'scanning', startedAt },
    { state: 'running', phase: 'matching', startedAt },
    { state: 'running', phase: 'importing', startedAt },
    { state: 'running', phase: 'artwork', startedAt },
    {
      state: 'idle',
      lastScan: {
        startedAt,
        finishedAt: new Date(Date.now() + 4000).toISOString(),
        outcome: 'succeeded',
        counts: {
          added: 3,
          changed: 0,
          unchanged: 0,
          restored: 0,
          missing: 0,
          unreadable: 0,
          purged: 0,
          linked: 3,
          needsAttention: 0,
          deferred: 0,
        },
      },
    },
  ];
  steps.forEach((scan, i) => {
    setTimeout(
      () => {
        mockLibraries.setScan(libraryId, scan);
        mockEvents.send('scan.progress', { libraryId, scan });
      },
      1000 * (i + 1),
    );
  });
  mockLibraries.setScan(libraryId, { ...library.scan, state: 'queued' });
  return HttpResponse.json({ ...library, scan: { ...library.scan, state: 'queued' } }, { status: 202 });
});

export const worker = setupWorker(simulatedScan, ...handlers);
