import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { afterEach, describe, expect, it } from 'vitest';
import type { MediaVersion, TitleDetail } from '../api/mediaman.schemas';
import { mockTitleDetails, mockTitles, mockVersions } from '../mocks/handlers';
import type { MediamanDesktop } from '../lib/desktop';
import { apiUrl } from '../lib/http';
import { server } from '../mocks/node';
import { createFakeDesktopPlayer } from '../test/fakeDesktopPlayer';
import { renderApp } from '../test/renderApp';

const [arrival, dune, office] = mockTitles.map((t) => t.id) as [string, string, string];

describe('TitleDetail', () => {
  it('shows a movie with its metadata and versions', async () => {
    renderApp(`/titles/${arrival}`);

    expect(await screen.findByRole('heading', { level: 1, name: 'Arrival' })).toBeInTheDocument();
    expect(screen.getByRole('list', { name: 'Details' })).toHaveTextContent('2016');
    expect(screen.getByRole('list', { name: 'Details' })).toHaveTextContent('1h 56m');
    expect(screen.getByRole('list', { name: 'Details' })).toHaveTextContent('7.9/10');
    expect(screen.getByRole('list', { name: 'Genres' })).toHaveTextContent('Drama');
    expect(screen.getByText(/A linguist works with the military/)).toBeInTheDocument();

    const versions = [...screen.getByRole('list', { name: 'Versions' }).children] as HTMLElement[];
    expect(versions).toHaveLength(3);
    expect(versions[0]).toHaveTextContent('Arrival.2016.2160p.UHD.BluRay.x265.HDR10-GROUP.mkv');
    expect(within(must(versions[0])).getByRole('list', { name: 'Quality' })).toHaveTextContent(
      /2160p.*HDR10.*BluRay.*HEVC.*TRUEHD.*7\.1.*GROUP/,
    );
    expect(versions[0]).toHaveTextContent('21.4 GB');
    expect(versions[0]).not.toHaveTextContent('Missing');
    expect(versions[1]).toHaveTextContent('Arrival.2016.1080p.WEB-DL.mkv');
    expect(versions[2]).toHaveTextContent('Missing');
  });

  it('tints the hero from the backdrop and shows the artwork', async () => {
    const detail: TitleDetail = {
      ...must(mockTitleDetails[arrival]),
      backdrop: { url: '/art/backdrop.jpg', dominantColor: '#336699' },
      poster: { url: '/art/poster.jpg' },
    };
    server.use(http.get('*/titles/:titleId', () => HttpResponse.json(detail)));
    renderApp(`/titles/${arrival}`);
    const container = document.body;
    await screen.findByRole('heading', { level: 1, name: 'Arrival' });

    const images = [...container.querySelectorAll('img')].map((img) => img.getAttribute('src'));
    expect(images).toEqual(expect.arrayContaining([apiUrl('/art/backdrop.jpg'), apiUrl('/art/poster.jpg')]));
    expect(container.querySelector<HTMLElement>('[style*="--tint"]')?.style.getPropertyValue('--tint')).toBe('#336699');
  });

  it('ignores a dominant color that is not #RRGGBB', async () => {
    const detail: TitleDetail = {
      ...must(mockTitleDetails[arrival]),
      backdrop: { url: '/art/backdrop.jpg', dominantColor: 'red;background:url(x)' },
    };
    server.use(http.get('*/titles/:titleId', () => HttpResponse.json(detail)));
    renderApp(`/titles/${arrival}`);
    const container = document.body;
    await screen.findByRole('heading', { level: 1, name: 'Arrival' });

    expect(container.querySelector<HTMLElement>('[style*="--tint"]')?.style.getPropertyValue('--tint')).toBe(
      'var(--color-accent)',
    );
  });

  it('says when no file holds a movie', async () => {
    renderApp(`/titles/${dune}`);
    expect(await screen.findByText(/No file in the library holds this movie/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Play' })).not.toBeInTheDocument();
  });

  it('marks the version Play uses, picks another and resets to the best quality', async () => {
    const user = userEvent.setup();
    renderApp(`/titles/${arrival}`);
    await screen.findByRole('heading', { level: 1, name: 'Arrival' });
    const versions = () => [...screen.getByRole('list', { name: 'Versions' }).children] as HTMLElement[];
    const [uhd, web, missing] = versions() as [HTMLElement, HTMLElement, HTMLElement];

    expect(uhd).toHaveTextContent('Plays');
    expect(within(uhd).queryByRole('button')).not.toBeInTheDocument();
    expect(within(missing).queryByRole('button')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Reset to best quality' })).not.toBeInTheDocument();

    await user.click(within(web).getByRole('button', { name: 'Use this version: Arrival.2016.1080p.WEB-DL.mkv' }));

    await waitFor(() => {
      expect(versions()[1]).toHaveTextContent('Plays');
    });
    expect(versions()[0]).not.toHaveTextContent('Plays');
    expect(within(must(versions()[0])).getByRole('button', { name: /^Use this version/ })).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Reset to best quality' }));

    await waitFor(() => {
      expect(versions()[0]).toHaveTextContent('Plays');
    });
    expect(screen.queryByRole('button', { name: 'Reset to best quality' })).not.toBeInTheDocument();
  });

  it("carries an episode's pick across the show at the same quality", async () => {
    const user = userEvent.setup();
    const sent: unknown[] = [];
    server.events.on('request:start', ({ request }) => {
      if (request.method === 'PUT' && request.url.endsWith(`/titles/${office}/preferred-version`)) {
        void request.clone().json().then((body) => sent.push(body));
      }
    });
    renderApp(`/titles/${office}`);
    await user.click(await screen.findByRole('tab', { name: 'Season 2' }));
    const dundies = screen.getByRole('list', { name: 'Versions of The Dundies' });
    expect(within(dundies).getByText('The.Office.S02E01.1080p.mkv').closest('li')).toHaveTextContent('Plays');
    // The Pilot's only file is a single version, so it offers no choice.
    await user.click(screen.getByRole('tab', { name: 'Season 1' }));
    expect(within(screen.getByRole('list', { name: 'Versions of Pilot' })).queryByRole('button')).not.toBeInTheDocument();
    await user.click(screen.getByRole('tab', { name: 'Season 2' }));

    await user.click(screen.getByRole('button', { name: 'Use this version: The.Office.S02E01.720p.mkv' }));

    expect(await screen.findByText(/Episodes play in 720p when there is a version in it/)).toBeInTheDocument();
    const picked = screen.getByRole('list', { name: 'Versions of The Dundies' });
    expect(within(picked).getByText('The.Office.S02E01.720p.mkv').closest('li')).toHaveTextContent('Plays');
    expect(sent).toEqual([{ mediaFileId: must(mockTitleDetails[office]?.seasons?.[2]?.episodes[0]?.versions[0]).mediaFileId }]);
    server.events.removeAllListeners();

    await user.click(screen.getByRole('button', { name: 'Reset to best quality' }));
    await waitFor(() => {
      expect(screen.queryByText(/Episodes play in/)).not.toBeInTheDocument();
    });
  });

  it('says what to do when the choice cannot be saved', async () => {
    const user = userEvent.setup();
    server.use(
      http.put('*/titles/:titleId/preferred-version', () =>
        HttpResponse.json(
          {
            title: 'Pick a profile first.',
            status: 409,
            code: 'profile.required',
            action: 'Choose who is watching, then pick the version again.',
          },
          { status: 409 },
        ),
      ),
    );
    renderApp(`/titles/${arrival}`);

    await user.click(await screen.findByRole('button', { name: 'Use this version: Arrival.2016.1080p.WEB-DL.mkv' }));

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('Pick a profile first.');
    expect(alert).toHaveTextContent('Choose who is watching, then pick the version again.');
  });

  it('marks and picks a file in several parts once', async () => {
    const [uhd, web] = must(mockTitleDetails[arrival]?.playable?.versions) as [MediaVersion, MediaVersion];
    const detail: TitleDetail = {
      ...must(mockTitleDetails[arrival]),
      playable: {
        id: 'p',
        versions: [uhd, { ...uhd, part: 2 }, web, { ...web, part: 2 }],
        selectedMediaFileId: uhd.mediaFileId,
      },
    };
    server.use(http.get('*/titles/:titleId', () => HttpResponse.json(detail)));
    renderApp(`/titles/${arrival}`);

    const versions = await screen.findByRole('list', { name: 'Versions' });
    expect(within(versions).getAllByText('Plays')).toHaveLength(1);
    expect(within(versions).getAllByRole('button', { name: /^Use this version/ })).toHaveLength(1);
  });

  it('offers no choice for a movie with a single version', async () => {
    const detail: TitleDetail = {
      ...must(mockTitleDetails[arrival]),
      playable: { id: 'p', versions: [must(mockTitleDetails[arrival]?.playable?.versions[0])], selectedMediaFileId: undefined },
    };
    server.use(http.get('*/titles/:titleId', () => HttpResponse.json(detail)));
    renderApp(`/titles/${arrival}`);

    const versions = await screen.findByRole('list', { name: 'Versions' });
    expect(versions).not.toHaveTextContent('Plays');
    expect(within(versions).queryByRole('button')).not.toBeInTheDocument();
  });

  it('says that Play works in the desktop app when the UI runs in a browser', async () => {
    const user = userEvent.setup();
    renderApp(`/titles/${arrival}`);

    await user.click(await screen.findByRole('button', { name: 'Play' }));

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('Play works in the MediaMan desktop app.');
    expect(alert).toHaveTextContent('Open this title in the desktop app');
  });

  describe('in the desktop app', () => {
    function installDesktop() {
      const player = createFakeDesktopPlayer();
      const bridge: MediamanDesktop = {
        isDesktop: true,
        platform: 'linux',
        server: { baseUrl: () => Promise.resolve(null) },
        tokens: { get: () => Promise.resolve(null), set: () => Promise.resolve(), clear: () => Promise.resolve() },
        player,
      };
      window.mediamanDesktop = bridge;
      return player;
    }
    afterEach(() => {
      delete window.mediamanDesktop;
    });

    it('plays the version the server picks in MPV and reports its progress', async () => {
      const user = userEvent.setup();
      const player = installDesktop();
      const reports: unknown[] = [];
      server.use(
        http.put('*/playables/:playableId/progress', async ({ params, request }) => {
          reports.push([params.playableId, await request.json()]);
          return HttpResponse.json({ playableId: String(params.playableId), positionSeconds: 0, durationSeconds: 0, watched: false });
        }),
      );
      const playable = must(mockTitleDetails[arrival]?.playable);
      renderApp(`/titles/${arrival}`);

      await user.click(await screen.findByRole('button', { name: 'Play' }));

      expect(await screen.findByText(/^Playing Arrival.2016.2160p.*.mkv in MPV.$/)).toHaveAttribute('role', 'status');
      const options = must(player.plays[0]).options;
      expect(options.url).toBe(apiUrl(`/media-files/${must(mockVersions.selectedFor(playable.id)).mediaFileId}/stream?sid=mock&exp=0&sig=mock`));
      expect(options.startSeconds).toBe(0);

      player.emit({ launchId: player.lastLaunchId(), type: 'progress', positionSeconds: 60, durationSeconds: 6960, paused: true });
      await waitFor(() => {
        expect(reports).toEqual([[playable.id, { positionSeconds: 60, durationSeconds: 6960 }]]);
      });
    });

    it('plays an episode, and offers Play only for episodes with a file', async () => {
      const user = userEvent.setup();
      installDesktop();
      renderApp(`/titles/${office}`);
      const panel = await screen.findByRole('tabpanel', { name: 'Season 1' });

      expect(within(panel).queryByRole('button', { name: 'Play Health Care' })).not.toBeInTheDocument();
      await user.click(within(panel).getByRole('button', { name: 'Play Diversity Day' }));

      expect(await within(panel).findByRole('status')).toHaveTextContent(
        'Playing The.Office.S01E01-E02.720p.WEB-DL.mkv in MPV.',
      );
    });

    it("offers MPV settings when MPV can't start, and says which MPV was picked", async () => {
      const user = userEvent.setup();
      const player = installDesktop();
      player.failNextPlay("MPV couldn't be started: 'mpv' was not found.");
      player.pickMpv('/opt/mpv/mpv');
      renderApp(`/titles/${arrival}`);

      await user.click(await screen.findByRole('button', { name: 'Play' }));

      const alert = await screen.findByRole('alert');
      expect(alert).toHaveTextContent("MPV couldn't start.");
      expect(alert).toHaveTextContent("'mpv' was not found.");
      await user.click(screen.getByRole('button', { name: 'Open MPV settings' }));
      expect(await screen.findByText('MPV is set to /opt/mpv/mpv. Press Play to try again.')).toHaveAttribute('role', 'status');

      await user.click(screen.getByRole('button', { name: 'Play' }));
      expect(await screen.findByText(/in MPV.$/)).toHaveAttribute('role', 'status');
      expect(player.plays).toHaveLength(1);
    });

    it('shows an error after the stream fails on a fresh session too', async () => {
      const user = userEvent.setup();
      const player = installDesktop();
      renderApp(`/titles/${arrival}`);

      await user.click(await screen.findByRole('button', { name: 'Play' }));
      await screen.findByText(/in MPV.$/);
      player.emit({ launchId: player.lastLaunchId(), type: 'ended', reason: 'error', detail: 'loading failed' });
      await waitFor(() => {
        expect(player.plays).toHaveLength(2);
      });
      player.emit({ launchId: player.lastLaunchId(), type: 'ended', reason: 'error', detail: 'loading failed' });

      const alert = await screen.findByRole('alert');
      expect(alert).toHaveTextContent("MPV couldn't play this file.");
      expect(alert).toHaveTextContent('Check that the server is running');
    });
  });

  it('shows season tabs with the first regular season selected and specials last', async () => {
    renderApp(`/titles/${office}`);

    const tabs = await screen.findAllByRole('tab');
    expect(tabs.map((t) => t.textContent)).toEqual(['Season 1', 'Season 2', 'Specials']);
    expect(tabs[0]).toHaveAttribute('aria-selected', 'true');
    expect(tabs[0]).toHaveAttribute('tabindex', '0');
    expect(tabs[1]).toHaveAttribute('tabindex', '-1');

    const panel = screen.getByRole('tabpanel', { name: 'Season 1' });
    const episodes = within(panel).getByRole('list', { name: 'Season 1 episodes' });
    expect(within(episodes).getByRole('heading', { name: '1. Pilot' })).toBeInTheDocument();
  });

  it('lists episodes with stand-ins, multi-episode files and episodes not in the library', async () => {
    renderApp(`/titles/${office}`);
    const panel = await screen.findByRole('tabpanel', { name: 'Season 1' });

    const pilot = within(panel).getByRole('list', { name: 'Versions of Pilot' });
    expect(pilot).toHaveTextContent('The.Office.S01E01-E02.720p.WEB-DL.mkv');
    expect(pilot).toHaveTextContent('Episodes 1–2 in one file');
    expect(within(panel).getByRole('list', { name: 'Versions of Diversity Day' })).toHaveTextContent(
      'Episodes 1–2 in one file',
    );

    const healthCare = must(within(panel).getByRole('heading', { name: '3. Health Care' }).parentElement);
    expect(healthCare).toHaveTextContent('Not in library');
    expect(panel.querySelector('time[datetime="2005-03-24"]')).not.toBeNull();
    expect(panel.querySelectorAll('img')).toHaveLength(0);
  });

  it('switches seasons by click and by arrow keys', async () => {
    const user = userEvent.setup();
    renderApp(`/titles/${office}`);
    const tabs = await screen.findAllByRole('tab');

    await user.click(must(tabs[1]));
    expect(screen.getByRole('tabpanel', { name: 'Season 2' })).toHaveTextContent('The Dundies');

    await user.keyboard('{ArrowRight}');
    expect(screen.getByRole('tab', { name: 'Specials' })).toHaveFocus();
    expect(screen.getByRole('tabpanel', { name: 'Specials' })).toHaveTextContent('The Accountants');

    await user.keyboard('{ArrowRight}');
    expect(screen.getByRole('tab', { name: 'Season 1' })).toHaveFocus();
    expect(screen.getByRole('tabpanel', { name: 'Season 1' })).toHaveTextContent('Pilot');

    await user.keyboard('{End}');
    expect(screen.getByRole('tab', { name: 'Specials' })).toHaveAttribute('aria-selected', 'true');
  });

  it('explains a missing title and links back to the grids', async () => {
    renderApp('/titles/01920000-0000-7000-8000-00000000dead');
    const alert = await screen.findByRole('alert');

    expect(alert).toHaveTextContent('Title not found.');
    expect(alert).toHaveTextContent('Go back to Movies or Shows and pick it again.');
    expect(within(alert).getByRole('link', { name: 'Go to Movies' })).toHaveAttribute('href', '/movies');
    expect(within(alert).getByRole('link', { name: 'Go to Shows' })).toHaveAttribute('href', '/shows');
  });

  it('offers to try again after a server error', async () => {
    const user = userEvent.setup();
    let calls = 0;
    server.use(
      http.get('*/titles/:titleId', () => {
        calls++;
        return calls === 1
          ? HttpResponse.json(
              { title: 'The library database is busy.', status: 500, code: 'db.busy', action: 'Try again in a moment.' },
              { status: 500 },
            )
          : HttpResponse.json(mockTitleDetails[arrival]);
      }),
    );
    renderApp(`/titles/${arrival}`);
    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('The library database is busy.');
    expect(alert).toHaveTextContent('Try again in a moment.');

    await user.click(within(alert).getByRole('button', { name: 'Try again' }));

    expect(await screen.findByRole('heading', { level: 1, name: 'Arrival' })).toBeInTheDocument();
  });
});

function must<T>(value: T | null | undefined): T {
  if (value === null || value === undefined) throw new Error('Expected a value.');
  return value;
}
