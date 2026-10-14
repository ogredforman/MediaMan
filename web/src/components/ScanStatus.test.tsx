import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import { getListLibrariesMockHandler, getStartLibraryScanMockHandler } from '../api/mediaman.msw';
import type { Library, ScanSummary } from '../api/mediaman.schemas';
import { createEventStream, mockHome, mockLibrary, mockTitles } from '../mocks/handlers';
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

type User = ReturnType<typeof userEvent.setup>;

function librariesButton() {
  return screen.findByRole('button', { name: /^Libraries/ });
}

/** Opens the header's Libraries dropdown and returns its list. */
async function libraries(user: User) {
  await user.click(await librariesButton());
  return screen.getByRole('list', { name: 'Libraries' });
}

function library(id: string, name: string, scan: Library['scan'] = { state: 'idle' }): Library {
  return { ...mockLibrary, id, name, scan };
}

describe('ScanStatus', () => {
  it('shows live scan progress from the event stream and refreshes the titles when the scan finishes', async () => {
    const user = userEvent.setup();
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
    const list = await libraries(user);
    await expect.poll(() => stream.connections).toBe(1);

    const send = (scan: object) => {
      stream.send('scan.progress', { libraryId: mockLibrary.id, scan });
    };
    send({ state: 'running', phase: 'matching', startedAt: '2026-10-06T12:00:00Z' });
    expect(await within(list).findByText('Media: matching titles…')).toBeInTheDocument();
    expect(within(list).getByRole('button', { name: 'Scan Media' })).toBeDisabled();
    expect(screen.getByText(/A scan is running/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Libraries, scanning' })).toHaveAttribute('aria-expanded', 'true');

    send({ state: 'running', phase: 'artwork', startedAt: '2026-10-06T12:00:00Z' });
    expect(await within(list).findByText('Media: fetching artwork…')).toBeInTheDocument();

    send({ state: 'idle', lastScan: succeeded });
    expect(await within(list).findByText('Media: scanned, 2 linked, 1 need attention')).toBeInTheDocument();
    expect(within(list).getByRole('button', { name: 'Scan Media' })).toBeEnabled();
    expect(await screen.findByText(mockTitles[0]?.name ?? '', { selector: 'p' })).toBeInTheDocument();
  });

  it('refreshes Home when the first scan finishes, so it no longer says the library is empty', async () => {
    const stream = createEventStream();
    let homeRequests = 0;
    server.use(
      stream.handler,
      http.get('*/home', () => {
        homeRequests += 1;
        return HttpResponse.json(homeRequests > 1 ? mockHome : { rows: [] });
      }),
    );
    renderApp('/');
    await expect.poll(() => stream.connections).toBe(1);
    await expect.poll(() => homeRequests).toBeGreaterThan(0);
    expect(screen.queryByRole('heading', { name: mockHome.hero?.title.name })).not.toBeInTheDocument();

    stream.send('scan.progress', { libraryId: mockLibrary.id, scan: { state: 'idle', lastScan: succeeded } });

    expect(await screen.findByRole('heading', { name: mockHome.hero?.title.name })).toBeInTheDocument();
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
    const list = await libraries(user);

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
    const list = await libraries(user);

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
    const user = userEvent.setup();
    renderApp();

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Media: the last scan failed. Open Libraries to see why and scan again.',
    );
    const alert = await within(await libraries(user)).findByRole('alert');
    expect(alert).toHaveTextContent('Media: the last scan failed (/media is gone).');
    expect(alert).toHaveTextContent('Scan again, or check the server log.');
  });

  it('opens one library from the header, and Esc closes it and returns focus to the button', async () => {
    const user = userEvent.setup();
    renderApp();
    const button = await librariesButton();
    expect(button).toHaveAttribute('aria-expanded', 'false');
    expect(screen.queryByRole('list', { name: 'Libraries' })).not.toBeInTheDocument();

    const list = await libraries(user);
    expect(button).toHaveAttribute('aria-expanded', 'true');
    expect(within(list).getAllByRole('listitem')).toHaveLength(1);
    expect(within(list).getByRole('button', { name: 'Scan Media' })).toBeEnabled();

    await user.keyboard('{Escape}');
    expect(screen.queryByRole('list', { name: 'Libraries' })).not.toBeInTheDocument();
    expect(button).toHaveAttribute('aria-expanded', 'false');
    expect(button).toHaveFocus();
  });

  it('lists three libraries in one dropdown, summarises scanning and failures, and closes on an outside click', async () => {
    const user = userEvent.setup();
    server.use(
      getListLibrariesMockHandler({
        items: [
          library('lib-movies', 'Movies', { state: 'running', phase: 'importing', startedAt: '2026-10-06T12:00:00Z' }),
          library('lib-shows', 'Shows', { state: 'idle', lastScan: succeeded }),
          library('lib-anime', 'Anime', {
            state: 'idle',
            lastScan: { ...succeeded, outcome: 'failed', error: { code: 'scan.root_missing', detail: '/anime is gone' } },
          }),
        ],
        pageInfo: {},
      }),
    );
    renderApp();

    const button = await screen.findByRole('button', { name: 'Libraries, scanning' });
    expect(screen.getByRole('alert')).toHaveTextContent('Anime: the last scan failed. Open Libraries to see why and scan again.');

    const list = await libraries(user);
    const items = within(list).getAllByRole('listitem');
    expect(items).toHaveLength(3);
    expect(items[0]).toHaveTextContent('Movies: importing metadata…');
    expect(within(list).getByRole('button', { name: 'Scan Movies' })).toBeDisabled();
    expect(items[1]).toHaveTextContent('Shows: scanned, 2 linked, 1 need attention');
    expect(within(list).getByRole('button', { name: 'Scan Shows' })).toBeEnabled();
    expect(within(list).getByRole('alert')).toHaveTextContent('Anime: the last scan failed (/anime is gone).');

    await user.click(screen.getByRole('contentinfo'));
    expect(screen.queryByRole('list', { name: 'Libraries' })).not.toBeInTheDocument();
    expect(button).toHaveFocus();
  });

  it('counts several failed scans in the header summary', async () => {
    const failed: Library['scan'] = {
      state: 'idle',
      lastScan: { ...succeeded, outcome: 'failed', error: { code: 'scan.root_missing', detail: 'gone' } },
    };
    server.use(
      getListLibrariesMockHandler({
        items: [library('a', 'One', failed), library('b', 'Two', failed), library('c', 'Three')],
        pageInfo: {},
      }),
    );
    renderApp();

    expect(await screen.findByRole('alert')).toHaveTextContent('2 library scans failed. Open Libraries to see why and scan again.');
    expect(screen.getByRole('button', { name: 'Libraries' })).toBeInTheDocument();
  });
});
