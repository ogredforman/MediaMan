import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import { getListLibrariesMockHandler, getStartLibraryScanMockHandler } from '../api/mediaman.msw';
import type { ScanSummary } from '../api/mediaman.schemas';
import { createEventStream, mockLibrary, mockTitles } from '../mocks/handlers';
import { server } from '../mocks/node';
import { renderApp } from '../test/renderApp';

const counts = {
  added: 2,
  changed: 0,
  unchanged: 0,
  restored: 0,
  missing: 0,
  unreadable: 0,
  purged: 0,
  linked: 2,
  needsAttention: 1,
  deferred: 0,
};
const succeeded: ScanSummary = {
  startedAt: '2026-10-06T12:00:00Z',
  finishedAt: '2026-10-06T12:01:00Z',
  outcome: 'succeeded',
  counts,
};

function libraries() {
  return screen.findByRole('list', { name: 'Libraries' });
}

describe('ScanStatus', () => {
  it('shows live scan progress from the event stream and refreshes the titles when the scan finishes', async () => {
    const stream = createEventStream();
    let titleRequests = 0;
    server.use(
      stream.handler,
      http.get('*/titles', () => {
        titleRequests += 1;
        return HttpResponse.json({ items: titleRequests > 1 ? mockTitles.slice(0, 1) : [], pageInfo: {} });
      }),
    );
    renderApp('/movies');
    const list = await libraries();
    await expect.poll(() => stream.connections).toBe(1);

    const send = (scan: object) => {
      stream.send('scan.progress', { libraryId: mockLibrary.id, scan });
    };
    send({ state: 'running', phase: 'matching', startedAt: '2026-10-06T12:00:00Z' });
    expect(await within(list).findByText('Media: matching titles…')).toBeInTheDocument();
    expect(within(list).getByRole('button', { name: 'Scan Media' })).toBeDisabled();
    expect(screen.getByText(/A scan is running/)).toBeInTheDocument();

    send({ state: 'idle', lastScan: succeeded });
    expect(await within(list).findByText('Media: scanned, 2 linked, 1 need attention')).toBeInTheDocument();
    expect(within(list).getByRole('button', { name: 'Scan Media' })).toBeEnabled();
    expect(await screen.findByText(mockTitles[0]?.name ?? '', { selector: 'p' })).toBeInTheDocument();
  });

  it('starts a scan and shows it queued', async () => {
    const user = userEvent.setup();
    let requested: string | undefined;
    server.use(
      getStartLibraryScanMockHandler(({ params }) => {
        requested = String(params.libraryId);
        return { ...mockLibrary, scan: { state: 'queued' } };
      }),
    );
    renderApp();
    const list = await libraries();

    await user.click(within(list).getByRole('button', { name: 'Scan Media' }));

    expect(await within(list).findByText('Media: scan queued')).toBeInTheDocument();
    expect(requested).toBe(mockLibrary.id);
  });

  it('says why scanning is unavailable and what to do', async () => {
    const user = userEvent.setup();
    server.use(
      http.post('*/libraries/:libraryId/scan', () =>
        HttpResponse.json(
          {
            title: "Scanning isn't available.",
            status: 503,
            code: 'scan.unavailable',
            action: 'Configure a metadata provider and ffprobe, then scan again.',
          },
          { status: 503, headers: { 'Content-Type': 'application/problem+json' } },
        ),
      ),
    );
    renderApp();
    const list = await libraries();

    await user.click(within(list).getByRole('button', { name: 'Scan Media' }));

    const alert = await within(list).findByRole('alert');
    expect(alert).toHaveTextContent("Scanning isn't available.");
    expect(alert).toHaveTextContent('Configure a metadata provider and ffprobe, then scan again.');
  });

  it('reports a failed scan with its reason and the next step', async () => {
    server.use(
      getListLibrariesMockHandler({
        items: [
          {
            ...mockLibrary,
            scan: {
              state: 'idle',
              lastScan: {
                ...succeeded,
                outcome: 'failed',
                error: { code: 'scan.root_missing', detail: '/media is gone' },
              },
            },
          },
        ],
        pageInfo: {},
      }),
    );
    renderApp();

    const alert = await within(await libraries()).findByRole('alert');
    expect(alert).toHaveTextContent('Media: the last scan failed (/media is gone).');
    expect(alert).toHaveTextContent('Scan again, or check the server log.');
  });
});
