import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import type { TitleSummary } from '../api/mediaman.schemas';
import { mockTitles } from '../mocks/handlers';
import { server } from '../mocks/node';
import { renderApp } from '../test/renderApp';

const movie = (n: number, poster?: string): TitleSummary => ({
  id: `01920000-0000-7000-8000-${String(n).padStart(12, '0')}`,
  kind: 'movie',
  name: `Movie ${String(n)}`,
  year: 2000 + n,
  genres: [],
  ...(poster === undefined ? {} : { poster: { url: poster } }),
});

function grid() {
  return screen.findByRole('region', { name: 'Movies' });
}

describe('PosterGrid', () => {
  it('lists only the kind it shows, with posters and stand-ins', async () => {
    let kind: string | null = null;
    server.use(
      http.get('*/titles', ({ request }) => {
        kind = new URL(request.url).searchParams.get('kind');
        return HttpResponse.json({ items: [movie(1, '/art/poster-1.jpg'), movie(2)], pageInfo: {} });
      }),
    );
    renderApp('/movies');
    const region = await grid();

    const cards = await within(region).findAllByRole('listitem');
    expect(cards).toHaveLength(2);
    expect(kind).toBe('movie');
    expect(cards[0]).toHaveTextContent('Movie 12001');
    expect(cards[0]?.querySelector('img')).toHaveAttribute('src', '/art/poster-1.jpg');
    expect(cards[1]).toHaveTextContent('Movie 2');
    expect(cards[1]?.querySelector('img')).toBeNull();
  });

  it('shows the shows on the Shows page', async () => {
    renderApp('/shows');
    const region = await screen.findByRole('region', { name: 'Shows' });
    const show = mockTitles.find((t) => t.kind === 'show');

    expect(await within(region).findByText(show?.name ?? '', { selector: 'p' })).toBeInTheDocument();
    expect(within(region).queryByText('Arrival')).not.toBeInTheDocument();
  });

  it('loads the next page when asked', async () => {
    const user = userEvent.setup();
    server.use(
      http.get('*/titles', ({ request }) => {
        const cursor = new URL(request.url).searchParams.get('cursor');
        return HttpResponse.json(
          cursor === 'page-2'
            ? { items: [movie(2)], pageInfo: {} }
            : { items: [movie(1)], pageInfo: { nextCursor: 'page-2' } },
        );
      }),
    );
    renderApp('/movies');
    const region = await grid();
    await within(region).findByText('Movie 1', { selector: 'p' });

    await user.click(within(region).getByRole('button', { name: 'Load more' }));

    expect(await within(region).findByText('Movie 2', { selector: 'p' })).toBeInTheDocument();
    expect(within(region).queryByRole('button', { name: 'Load more' })).not.toBeInTheDocument();
  });

  it('offers a scan when the library has none of the kind', async () => {
    server.use(http.get('*/titles', () => HttpResponse.json({ items: [], pageInfo: {} })));
    renderApp('/movies');
    const region = await grid();

    expect(await within(region).findByText(/No movies in your library yet/)).toBeInTheDocument();
    expect(within(region).getByRole('button', { name: 'Scan Media' })).toBeInTheDocument();
  });

  it('explains an empty grid when the libraries cannot be loaded', async () => {
    server.use(
      http.get('*/titles', () => HttpResponse.json({ items: [], pageInfo: {} })),
      http.get('*/libraries', () =>
        HttpResponse.json(
          { title: 'The database is busy.', status: 503, code: 'db.busy', action: 'Reload the page in a moment.' },
          { status: 503, headers: { 'Content-Type': 'application/problem+json' } },
        ),
      ),
    );
    renderApp('/movies');

    const alert = await within(await grid()).findByRole('alert');
    expect(alert).toHaveTextContent('The database is busy.');
    expect(alert).toHaveTextContent('Reload the page in a moment.');
  });

  it('says what went wrong and retries', async () => {
    const user = userEvent.setup();
    let fail = true;
    server.use(
      http.get('*/titles', () =>
        fail
          ? HttpResponse.json(
              { title: 'The library is unavailable.', status: 500, code: 'server.error', action: 'Try again.' },
              { status: 500, headers: { 'Content-Type': 'application/problem+json' } },
            )
          : HttpResponse.json({ items: [movie(1)], pageInfo: {} }),
      ),
    );
    renderApp('/movies');
    const region = await grid();

    const alert = await within(region).findByRole('alert');
    expect(alert).toHaveTextContent('The library is unavailable.');
    expect(alert).toHaveTextContent('Try again.');

    fail = false;
    await user.click(within(alert).getByRole('button', { name: 'Try again' }));
    expect(await within(region).findByText('Movie 1', { selector: 'p' })).toBeInTheDocument();
  });
});
