import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { afterEach, describe, expect, it } from 'vitest';
import type { Home } from '../api/mediaman.schemas';
import type { MediamanDesktop } from '../lib/desktop';
import { apiUrl } from '../lib/http';
import { mockHome, mockTitles } from '../mocks/handlers';
import { server } from '../mocks/node';
import { createFakeDesktopPlayer } from '../test/fakeDesktopPlayer';
import { renderApp } from '../test/renderApp';

const [arrival, dune, office] = mockTitles as [(typeof mockTitles)[0], (typeof mockTitles)[0], (typeof mockTitles)[0]];

function serveHome(home: Home) {
  server.use(http.get('*/home', () => HttpResponse.json(home)));
}

function cardsOf(row: string): HTMLElement[] {
  return within(screen.getByRole('list', { name: row })).getAllByRole('link');
}

describe('HomeScreen', () => {
  it('features the hero title with its overview, Resume on its episode and More info', async () => {
    const user = userEvent.setup();
    renderApp('/');

    const hero = await screen.findByRole('region', { name: 'The Office' });
    expect(screen.getByRole('heading', { level: 1, name: 'Home' })).toBeInTheDocument();
    expect(within(hero).getByText('A mockumentary on a group of typical office workers.')).toBeInTheDocument();
    expect(within(hero).getByText('S01E01 · Pilot')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Resume S01E01 · Pilot from 10:15' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Start S01E01 · Pilot over' })).toBeInTheDocument();

    await user.click(screen.getByRole('link', { name: 'More info on The Office' }));
    expect(await screen.findByRole('heading', { level: 1, name: 'The Office' })).toBeInTheDocument();
  });

  it('shows a plain Play for a movie hero, its logo, and tints it from the backdrop', async () => {
    serveHome({
      hero: {
        title: arrival,
        playableId: '01920000-0000-7000-8000-000000000501',
        backdrop: { url: '/art/backdrop.jpg', dominantColor: '#336699' },
        logo: { url: '/art/logo.png' },
      },
      rows: [],
    });
    renderApp('/');

    const heading = await screen.findByRole('heading', { level: 2, name: 'Arrival' });
    expect(within(heading).getByRole('img', { name: 'Arrival' })).toHaveAttribute('src', apiUrl('/art/logo.png'));
    expect(screen.getByRole('button', { name: 'Play' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Start over' })).not.toBeInTheDocument();
    expect(document.querySelector<HTMLElement>('[style*="--tint"]')?.style.getPropertyValue('--tint')).toBe('#336699');
  });

  it('shows the rows in the order the server sends them, with episodes and progress', async () => {
    serveHome({ ...mockHome, rows: [...mockHome.rows, { kind: 'genre', genre: 'Drama', items: [{ title: arrival }] }] });
    renderApp('/');

    await screen.findByRole('heading', { level: 2, name: 'The Office' });
    expect(screen.getAllByRole('heading', { level: 2 }).map((h) => h.textContent)).toEqual([
      'The Office',
      'Continue Watching',
      'Next Up',
      'Recently Added',
      'Drama',
    ]);
    expect(cardsOf('Continue Watching')[0]).toHaveTextContent('The OfficeS01E01 · Pilot');
    expect(screen.getByRole('progressbar', { name: 'Watched so far' })).toHaveAttribute('aria-valuetext', '10:15 of 23:00');
    expect(cardsOf('Next Up')[0]).toHaveTextContent('S02E01 · The Dundies');
    // Cards without a poster draw the name as a stand-in too, so each card ends with the name and year.
    const recent = cardsOf('Recently Added').map((card) => card.textContent);
    expect(recent).toHaveLength(3);
    [office, dune, arrival].forEach((title, index) => {
      expect(recent[index]).toMatch(new RegExp(`${title.name}${String(title.year)}$`));
    });
  });

  it('moves between cards with the arrow keys, Home and End, with one card per row in the tab order', async () => {
    const user = userEvent.setup();
    renderApp('/');
    await screen.findByRole('list', { name: 'Recently Added' });
    const cards = cardsOf('Recently Added');
    const [first, second, third] = cards as [HTMLElement, HTMLElement, HTMLElement];
    expect(cards.map((card) => card.tabIndex)).toEqual([0, -1, -1]);

    first.focus();
    await user.keyboard('{ArrowLeft}');
    expect(first).toHaveFocus();
    await user.keyboard('{ArrowRight}');
    expect(second).toHaveFocus();
    expect(cards.map((card) => card.tabIndex)).toEqual([-1, 0, -1]);
    await user.keyboard('{End}');
    expect(third).toHaveFocus();
    await user.keyboard('{ArrowRight}');
    expect(third).toHaveFocus();
    await user.keyboard('{Home}');
    expect(first).toHaveFocus();

    await user.keyboard('{Enter}');
    expect(await screen.findByRole('heading', { level: 1, name: 'The Office' })).toBeInTheDocument();
  });

  it('says how to fill an empty library', async () => {
    serveHome({ rows: [] });
    renderApp('/');

    const home = await screen.findByRole('region', { name: 'Home' });
    expect(await within(home).findByText(/No titles in your library yet/)).toBeInTheDocument();
    expect(within(home).getByRole('button', { name: /^Scan/ })).toBeInTheDocument();
  });

  it('shows rows without a hero when no title has a backdrop', async () => {
    serveHome({ rows: mockHome.rows });
    renderApp('/');

    expect(await screen.findByRole('list', { name: 'Recently Added' })).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /^More info/ })).not.toBeInTheDocument();
    expect(screen.queryByText(/No titles in your library yet/)).not.toBeInTheDocument();
  });

  it('says what went wrong and tries again', async () => {
    const user = userEvent.setup();
    server.use(
      http.get(
        '*/home',
        () =>
          HttpResponse.json(
            { title: 'The server had a problem.', status: 500, code: 'server.error', action: 'Try again in a moment.' },
            { status: 500 },
          ),
        { once: true },
      ),
    );
    renderApp('/');

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('The server had a problem.');
    expect(alert).toHaveTextContent('Try again in a moment.');
    await user.click(within(alert).getByRole('button', { name: 'Try again' }));
    expect(await screen.findByRole('heading', { level: 2, name: 'The Office' })).toBeInTheDocument();
  });

  it('says that Play works in the desktop app when the UI runs in a browser', async () => {
    const user = userEvent.setup();
    renderApp('/');

    await user.click(await screen.findByRole('button', { name: 'Resume S01E01 · Pilot from 10:15' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('Play works in the MediaMan desktop app.');
  });

  describe('in the desktop app', () => {
    afterEach(() => {
      delete window.mediamanDesktop;
    });

    it("resumes the hero's episode in MPV and refetches Home when MPV closes", async () => {
      const user = userEvent.setup();
      const player = createFakeDesktopPlayer();
      const bridge: MediamanDesktop = {
        isDesktop: true,
        platform: 'linux',
        server: { baseUrl: () => Promise.resolve(null) },
        tokens: { get: () => Promise.resolve(null), set: () => Promise.resolve(), clear: () => Promise.resolve() },
        player,
      };
      window.mediamanDesktop = bridge;
      let homeFetches = 0;
      server.use(
        http.get('*/home', () => {
          homeFetches++;
          return HttpResponse.json(mockHome);
        }),
      );
      renderApp('/');

      await user.click(await screen.findByRole('button', { name: 'Resume S01E01 · Pilot from 10:15' }));
      expect(await screen.findByText('Playing The.Office.S01E01-E02.720p.WEB-DL.mkv in MPV.')).toHaveAttribute('role', 'status');
      expect(player.plays[0]?.options.startSeconds).toBe(615);

      const before = homeFetches;
      player.emit({ launchId: player.lastLaunchId(), type: 'ended', reason: 'quit' });
      await waitFor(() => {
        expect(homeFetches).toBeGreaterThan(before);
      });
    });
  });
});
