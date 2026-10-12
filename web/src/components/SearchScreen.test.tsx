import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import type { LibrarySearchResults } from '../api/mediaman.schemas';
import { mockSearch } from '../mocks/handlers';
import { server } from '../mocks/node';
import { renderApp } from '../test/renderApp';

function field() {
  return screen.getByRole('searchbox', { name: 'Search the library' });
}

function resultNames(): string[] {
  return within(screen.getByRole('list', { name: 'Results' }))
    .getAllByRole('link')
    .map((link) => within(link).getAllByText(/./)[0]?.textContent ?? '');
}

describe('Search', () => {
  it('focuses the field on / and shows results as you type', async () => {
    const user = userEvent.setup();
    const { router } = renderApp('/');
    await screen.findByRole('heading', { level: 1, name: 'Home' });

    await user.keyboard('/');
    expect(field()).toHaveFocus();
    expect(field()).toHaveValue('');

    await user.keyboard('ar');
    expect(await screen.findByRole('heading', { level: 1, name: 'Search' })).toBeInTheDocument();
    expect(router.state.location.search).toEqual({ q: 'ar' });
    await waitFor(() => { expect(resultNames()).toEqual(['Arrival']); });

    await user.clear(field());
    await user.type(field(), 'the off');
    await waitFor(() => { expect(resultNames()).toEqual(['The Office']); });

    await user.click(screen.getByRole('link', { name: /The Office/ }));
    expect(await screen.findByRole('heading', { level: 1, name: 'The Office' })).toBeInTheDocument();
    expect(field()).toHaveValue('');
  });

  it('leaves / alone while typing in another field', async () => {
    const user = userEvent.setup();
    renderApp('/search?q=dune');
    await waitFor(() => { expect(resultNames()).toEqual(['Dune']); });

    await user.click(field());
    await user.keyboard('/');
    expect(field()).toHaveValue('dune/');
  });

  it('keeps the previous results up while the next query loads', async () => {
    let release: () => void = () => undefined;
    const gate = new Promise<void>((resolve) => { release = resolve; });
    server.use(
      http.get('*/search', async ({ request }) => {
        const q = new URL(request.url).searchParams.get('q') ?? '';
        if (q !== 'arr') await gate;
        return HttpResponse.json<LibrarySearchResults>({ query: q, titles: mockSearch(q) });
      }),
    );
    const user = userEvent.setup();
    renderApp('/search?q=arr');
    await waitFor(() => { expect(resultNames()).toEqual(['Arrival']); });

    await user.clear(field());
    await user.type(field(), 'dune');
    await waitFor(() => { expect(screen.getByRole('list', { name: 'Results' })).toHaveAttribute('aria-busy', 'true'); });
    expect(resultNames()).toEqual(['Arrival']);
    expect(screen.queryByText(/No titles in your library match/)).not.toBeInTheDocument();

    release();
    await waitFor(() => { expect(resultNames()).toEqual(['Dune']); });
  });

  it('offers Movies and Shows when nothing matches', async () => {
    const user = userEvent.setup();
    renderApp('/search?q=zzz');

    expect(await screen.findByText('No titles in your library match “zzz”.')).toBeInTheDocument();
    await user.click(within(screen.getByRole('main')).getByRole('link', { name: 'Shows' }));
    expect(await screen.findByRole('heading', { level: 1, name: 'Shows' })).toBeInTheDocument();
  });

  it('matches regardless of case and accents', async () => {
    renderApp('/search?q=ÁRRIVAL');
    await waitFor(() => { expect(resultNames()).toEqual(['Arrival']); });
  });

  it('asks for a query without searching when the field is blank', async () => {
    let requests = 0;
    server.use(
      http.get('*/search', () => {
        requests++;
        return HttpResponse.json<LibrarySearchResults>({ query: '', titles: [] });
      }),
    );
    renderApp('/search?q=%20');

    expect(await screen.findByText('Type a movie or show name to search your library.')).toBeInTheDocument();
    expect(requests).toBe(0);
  });

  it('says what went wrong and tries again', async () => {
    let fail = true;
    server.use(
      http.get('*/search', ({ request }) => {
        if (fail) {
          return HttpResponse.json(
            { title: 'The search failed.', status: 500, code: 'server.error', action: 'Try again in a moment.' },
            { status: 500 },
          );
        }
        const q = new URL(request.url).searchParams.get('q') ?? '';
        return HttpResponse.json<LibrarySearchResults>({ query: q, titles: mockSearch(q) });
      }),
    );
    const user = userEvent.setup();
    renderApp('/search?q=dune');

    const alert = await screen.findByRole('alert');
    expect(within(alert).getByText('The search failed.')).toBeInTheDocument();
    fail = false;
    await user.click(within(alert).getByRole('button', { name: 'Try again' }));
    await waitFor(() => { expect(resultNames()).toEqual(['Dune']); });
  });
});
