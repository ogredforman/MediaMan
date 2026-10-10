import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import type { MediaVersion, TitleDetail } from '../api/mediaman.schemas';
import { mockTitleDetails, mockTitles } from '../mocks/handlers';
import { apiUrl } from '../lib/http';
import { server } from '../mocks/node';
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

  it('plays a movie through the dev play endpoint', async () => {
    const user = userEvent.setup();
    const played: string[] = [];
    server.use(
      http.post('*/dev/playables/:playableId/play', ({ params }) => {
        played.push(String(params.playableId));
        return HttpResponse.json({ mediaFileId: 'f', fileName: 'Arrival.mkv' }, { status: 202 });
      }),
    );
    renderApp(`/titles/${arrival}`);

    await user.click(await screen.findByRole('button', { name: 'Play' }));

    expect(await screen.findByText('Playing Arrival.mkv in MPV.')).toHaveAttribute('role', 'status');
    expect(played).toEqual([must(mockTitleDetails[arrival]?.playable).id]);
  });

  it('plays an episode, and offers Play only for episodes with a file', async () => {
    const user = userEvent.setup();
    renderApp(`/titles/${office}`);
    const panel = await screen.findByRole('tabpanel', { name: 'Season 1' });

    expect(within(panel).queryByRole('button', { name: 'Play Health Care' })).not.toBeInTheDocument();
    await user.click(within(panel).getByRole('button', { name: 'Play Diversity Day' }));

    expect(await within(panel).findByRole('status')).toHaveTextContent(
      'Playing The.Office.S01E01-E02.720p.WEB-DL.mkv in MPV.',
    );
  });

  it('explains that Play needs the dev configuration when the server has no play endpoint', async () => {
    const user = userEvent.setup();
    server.use(
      http.post('*/dev/playables/:playableId/play', () =>
        HttpResponse.json({ title: 'Not Found', status: 404, code: 'not_found' }, { status: 404 }),
      ),
    );
    renderApp(`/titles/${arrival}`);

    await user.click(await screen.findByRole('button', { name: 'Play' }));

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent("Play isn't available on this server.");
    expect(alert).toHaveTextContent('MediaMan:Dev:MediaPath');
  });

  it('shows what to do when MPV cannot start', async () => {
    const user = userEvent.setup();
    server.use(
      http.post('*/dev/playables/:playableId/play', () =>
        HttpResponse.json(
          {
            title: "MPV couldn't start.",
            status: 503,
            code: 'playback.player_unavailable',
            detail: "MPV ('mpv') couldn't be started.",
            action: 'Install MPV, or set MediaMan:Tools:MpvPath to the MPV executable, then try again.',
          },
          { status: 503 },
        ),
      ),
    );
    renderApp(`/titles/${arrival}`);

    await user.click(await screen.findByRole('button', { name: 'Play' }));

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent("MPV couldn't start.");
    expect(alert).toHaveTextContent('MediaMan:Tools:MpvPath');
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
