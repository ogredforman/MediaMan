import { act, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import type { TitleSummary } from '../api/mediaman.schemas';
import { server } from '../mocks/node';
import { renderApp } from '../test/renderApp';

const movie = (n: number, genres: string[], rating?: number): TitleSummary => ({
  id: `01920000-0000-7000-8000-${String(n).padStart(12, '0')}`,
  kind: 'movie',
  name: `Movie ${String(n)}`,
  genres,
  watched: false,
  ...(rating === undefined ? {} : { rating }),
});

const library = [movie(1, ['Drama'], 6), movie(2, ['Drama', 'Thriller'], 9), movie(3, ['Thriller']), movie(4, ['Drama'])];

/** A library of four movies, recording each `/titles` query. */
function serveLibrary() {
  const queries: URLSearchParams[] = [];
  server.use(
    http.get('*/genres', () =>
      HttpResponse.json({ items: [{ name: 'Drama', titleCount: 3 }, { name: 'Thriller', titleCount: 2 }] }),
    ),
    http.get('*/titles', ({ request }) => {
      const params = new URL(request.url).searchParams;
      queries.push(params);
      const genre = params.get('genre');
      const items = library.filter((t) => genre === null || t.genres.includes(genre));
      if (params.get('sort') === 'rating') items.sort((a, b) => (b.rating ?? -1) - (a.rating ?? -1));
      return HttpResponse.json({ items, pageInfo: {} });
    }),
  );
  return queries;
}

function chips() {
  return screen.findByRole('navigation', { name: 'Genres' });
}

async function cardNames(region: HTMLElement) {
  const cards = await within(region).findAllByRole('listitem');
  return cards.map((card) => within(card).getAllByText(/^Movie \d+$/)[0]?.textContent);
}

describe('GenreBrowser', () => {
  it('opens on a row per genre, best rated first, under a chip bar with All picked', async () => {
    const queries = serveLibrary();
    renderApp('/movies');

    const bar = await chips();
    expect(within(bar).getAllByRole('link').map((link) => link.textContent)).toEqual(['All', 'Drama', 'Thriller']);
    expect(within(bar).getByRole('link', { name: 'All' })).toHaveAttribute('aria-current', 'page');
    expect(within(bar).getByRole('link', { name: 'Drama' })).not.toHaveAttribute('aria-current');

    expect(await cardNames(await screen.findByRole('region', { name: 'Drama' }))).toEqual(['Movie 2', 'Movie 1', 'Movie 4']);
    expect(await cardNames(await screen.findByRole('region', { name: 'Thriller' }))).toEqual(['Movie 2', 'Movie 3']);
    const all = await screen.findByRole('region', { name: 'All movies A–Z' });
    expect(await cardNames(all)).toEqual(['Movie 1', 'Movie 2', 'Movie 3', 'Movie 4']);
    const drama = queries.find((q) => q.get('genre') === 'Drama');
    expect(Object.fromEntries(drama ?? [])).toEqual({ kind: 'movie', genre: 'Drama', sort: 'rating', limit: '20' });
  });

  it('opens a genre’s full grid from its chip, and Back returns to the rows', async () => {
    const user = userEvent.setup();
    serveLibrary();
    const { router } = renderApp('/movies');

    await user.click(within(await chips()).getByRole('link', { name: 'Thriller' }));

    expect(router.history.location.search).toBe('?genre=Thriller');
    const page = screen.getByRole('region', { name: 'Movies' });
    expect(await within(page).findByText('Movie 3', { selector: 'p' })).toBeInTheDocument();
    expect(within(page).queryByText('Movie 1', { selector: 'p' })).not.toBeInTheDocument();
    expect(within(page).queryByRole('region', { name: 'Thriller' })).not.toBeInTheDocument();
    expect(within(page).queryByRole('region', { name: 'All movies A–Z' })).not.toBeInTheDocument();
    expect(within(await chips()).getByRole('link', { name: 'Thriller' })).toHaveAttribute('aria-current', 'page');
    expect(within(await chips()).getByRole('link', { name: 'All' })).not.toHaveAttribute('aria-current');

    act(() => {
      router.history.back();
    });

    expect(await screen.findByRole('region', { name: 'Thriller' })).toBeInTheDocument();
  });

  it('opens a genre’s full grid from the row’s See all', async () => {
    const user = userEvent.setup();
    serveLibrary();
    const { router } = renderApp('/movies');

    await user.click(await screen.findByRole('link', { name: 'See all Drama' }));

    expect(router.history.location.search).toBe('?genre=Drama');
    const page = screen.getByRole('region', { name: 'Movies' });
    expect(await within(page).findByText('Movie 4', { selector: 'p' })).toBeInTheDocument();
    expect(within(page).queryByText('Movie 3', { selector: 'p' })).not.toBeInTheDocument();
  });

  it('says when a genre in the URL has no titles, and links to all of them', async () => {
    serveLibrary();
    renderApp('/movies?genre=Western');

    expect(await screen.findByText(/No movies in Western any more/)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Browse all movies' })).toHaveAttribute('href', '/movies');
  });

  it('shows the full grid when no title has a genre', async () => {
    server.use(
      http.get('*/genres', () => HttpResponse.json({ items: [] })),
      http.get('*/titles', () => HttpResponse.json({ items: [movie(5, [])], pageInfo: {} })),
    );
    renderApp('/movies');

    const page = await screen.findByRole('region', { name: 'Movies' });
    expect(await within(page).findByText('Movie 5', { selector: 'p' })).toBeInTheDocument();
    expect(screen.queryByRole('navigation', { name: 'Genres' })).not.toBeInTheDocument();
  });

  it('lists the shows’ genres on the Shows page', async () => {
    renderApp('/shows');

    const bar = await chips();
    expect(within(bar).getAllByRole('link').map((link) => link.textContent)).toEqual(['All', 'Comedy']);
    const comedy = await screen.findByRole('region', { name: 'Comedy' });
    expect(await within(comedy).findByText('The Office', { selector: 'p' })).toBeInTheDocument();
  });
});
