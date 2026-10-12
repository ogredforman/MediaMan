import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import { mockTitles } from '../mocks/handlers';
import { server } from '../mocks/node';
import { renderApp } from '../test/renderApp';

const [arrival, dune, office] = mockTitles.map((t) => t.id) as [string, string, string];

function must<T>(value: T | null | undefined): T {
  if (value === null || value === undefined) throw new Error('missing');
  return value;
}

/** The card (list item) holding a title's poster link. */
function cardOf(container: HTMLElement, name: RegExp): HTMLElement {
  return must(within(container).getByRole('link', { name }).closest('li'));
}

describe('Mark watched', () => {
  it('marks a title watched from its poster in a grid, then unwatched', async () => {
    const user = userEvent.setup();
    renderApp('/movies');
    const region = await screen.findByRole('region', { name: 'Movies' });
    await within(region).findByRole('link', { name: /Arrival/ });
    expect(cardOf(region, /Arrival/)).not.toHaveTextContent('Watched');

    await user.click(within(region).getByRole('button', { name: 'Mark Arrival watched' }));
    expect(await within(region).findByRole('button', { name: 'Mark Arrival unwatched' })).toBeInTheDocument();
    expect(within(region).getByRole('link', { name: /Arrival/ })).toHaveTextContent('Watched');

    await user.click(within(region).getByRole('button', { name: 'Mark Arrival unwatched' }));
    expect(await within(region).findByRole('button', { name: 'Mark Arrival watched' })).toBeInTheDocument();
    expect(within(region).getByRole('link', { name: /Arrival/ })).not.toHaveTextContent('Watched');
  });

  it('says what went wrong and what to do when marking fails', async () => {
    const user = userEvent.setup();
    server.use(
      http.put('*/titles/:titleId/watched', () =>
        HttpResponse.json(
          {
            title: 'Pick a profile first.',
            status: 409,
            code: 'profile.required',
            action: 'Choose who is watching, then try again.',
          },
          { status: 409, headers: { 'Content-Type': 'application/problem+json' } },
        ),
      ),
    );
    renderApp('/movies');
    const region = await screen.findByRole('region', { name: 'Movies' });

    await user.click(await within(region).findByRole('button', { name: 'Mark Arrival watched' }));
    const alert = await within(region).findByRole('alert');
    expect(alert).toHaveTextContent('Pick a profile first.');
    expect(alert).toHaveTextContent('Choose who is watching, then try again.');
  });

  it('marks a show watched from a search result once every episode with a file is', async () => {
    const user = userEvent.setup();
    renderApp('/search?q=office');
    const results = await screen.findByRole('list', { name: 'Results' });

    await user.click(await within(results).findByRole('button', { name: 'Mark The Office watched' }));
    expect(await within(results).findByRole('button', { name: 'Mark The Office unwatched' })).toBeInTheDocument();
    expect(within(results).getByRole('link', { name: /The Office/ })).toHaveTextContent('Watched');
  });

  it('marks a Continue Watching episode on Home, taking it off the row, and a title in Recently Added', async () => {
    const user = userEvent.setup();
    renderApp('/');
    const continueWatching = await screen.findByRole('list', { name: 'Continue Watching' });

    await user.click(within(continueWatching).getByRole('button', { name: 'Mark The Office S01E01 · Pilot watched' }));
    await waitFor(() => {
      expect(screen.queryByRole('list', { name: 'Continue Watching' })).not.toBeInTheDocument();
    });

    const recent = screen.getByRole('list', { name: 'Recently Added' });
    await user.click(within(recent).getByRole('button', { name: 'Mark Arrival watched' }));
    expect(await within(recent).findByRole('button', { name: 'Mark Arrival unwatched' })).toBeInTheDocument();
    expect(cardOf(recent, /Arrival/)).toHaveTextContent('Watched');
  });

  it('keeps a Home card and its Mark watched action in the row as one tab stop each', async () => {
    renderApp('/');
    const recent = await screen.findByRole('list', { name: 'Recently Added' });
    const [first, second] = within(recent).getAllByRole('link');

    expect(first).toHaveAttribute('tabindex', '0');
    expect(within(cardOf(recent, /The Office/)).getByRole('button')).toHaveAttribute('tabindex', '0');
    expect(second).toHaveAttribute('tabindex', '-1');
    expect(within(cardOf(recent, /Dune/)).getByRole('button')).toHaveAttribute('tabindex', '-1');
  });

  it("marks a show's episode and season on the detail page", async () => {
    const user = userEvent.setup();
    renderApp(`/titles/${office}`);
    const panel = await screen.findByRole('tabpanel', { name: 'Season 1' });
    expect(within(panel).getByRole('button', { name: 'Mark Season 1 watched' })).toBeInTheDocument();
    expect(within(panel).getByRole('button', { name: 'Mark Diversity Day unwatched' })).toBeInTheDocument();
    expect(within(panel).queryByRole('button', { name: /Health Care/ })).not.toBeInTheDocument();

    // Every Season 1 episode with a file is then watched, so the season offers unwatched.
    await user.click(within(panel).getByRole('button', { name: 'Mark Pilot watched' }));
    expect(await within(panel).findByRole('button', { name: 'Mark Pilot unwatched' })).toBeInTheDocument();
    expect(within(panel).getByRole('button', { name: 'Mark Season 1 unwatched' })).toBeInTheDocument();

    await user.click(within(panel).getByRole('button', { name: 'Mark Season 1 unwatched' }));
    expect(await within(panel).findByRole('button', { name: 'Mark Season 1 watched' })).toBeInTheDocument();
    expect(within(panel).getByRole('button', { name: 'Mark Pilot watched' })).toBeInTheDocument();
    expect(within(panel).getByRole('button', { name: 'Mark Diversity Day watched' })).toBeInTheDocument();
  });

  it('marks a movie on its detail page, and offers nothing for a movie no file holds', async () => {
    const user = userEvent.setup();
    const { router } = renderApp(`/titles/${arrival}`);
    await screen.findByRole('heading', { level: 1, name: 'Arrival' });

    await user.click(screen.getByRole('button', { name: 'Mark Arrival watched' }));
    expect(await screen.findByRole('button', { name: 'Mark Arrival unwatched' })).toBeInTheDocument();

    await router.navigate({ to: '/titles/$titleId', params: { titleId: dune } });
    await screen.findByRole('heading', { level: 1, name: 'Dune' });
    expect(screen.queryByRole('button', { name: /Mark Dune/ })).not.toBeInTheDocument();
  });
});
