import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import type { MediaFileMatchRequest } from '../api/mediaman.schemas';
import { mockLibrary } from '../mocks/handlers';
import { server } from '../mocks/node';
import { renderApp } from '../test/renderApp';

const inbox = () => screen.findByRole('list', { name: 'Files in Media that need attention' });
const card = async (fileName: string) => within(await inbox()).findByRole('listitem', { name: fileName });

/** Records the body of every relink request, then lets the mock store answer. */
function recordRelinks() {
  const sent: MediaFileMatchRequest[] = [];
  server.use(
    http.put('*/media-files/:mediaFileId/match', async ({ request }) => {
      sent.push((await request.clone().json()) as MediaFileMatchRequest);
      return undefined;
    }),
  );
  return sent;
}

describe('AttentionInbox', () => {
  it('opens from the nav and shows each file with its guess, reason and suggestions', async () => {
    const user = userEvent.setup();
    renderApp();
    await user.click(await screen.findByRole('link', { name: 'Needs attention' }));

    expect(await screen.findByRole('heading', { level: 1, name: 'Needs attention' })).toBeInTheDocument();
    const movie = await card('Arrivl.2016.1080p.WEB-DL.mkv');
    expect(movie).toHaveTextContent('Looks like: Arrivl (2016) · 1080p · WEB-DL');
    expect(movie).toHaveTextContent("The best match isn't certain.");
    const suggestions = within(movie).getByRole('list', { name: 'Suggestions for Arrivl.2016.1080p.WEB-DL.mkv' });
    expect(within(suggestions).getByRole('button', { name: 'Link to Arrival (2016)' })).toBeInTheDocument();
    expect(within(suggestions).getByRole('button', { name: 'Link to The Arrival (1996)' })).toBeInTheDocument();

    expect(await card('Office.S01E03.mkv')).toHaveTextContent('Looks like: Office · S01E03');

    const broken = await card('broken.mkv');
    expect(broken).toHaveTextContent("MediaMan couldn't read this file.");
    expect(within(broken).queryByRole('button', { name: /Search/ })).not.toBeInTheDocument();
  });

  it('links a suggestion in one click and offers the title', async () => {
    const user = userEvent.setup();
    const sent = recordRelinks();
    renderApp('/attention');

    await user.click(within(await card('Arrivl.2016.1080p.WEB-DL.mkv')).getByRole('button', { name: 'Link to Arrival (2016)' }));

    const status = await screen.findByText('Linked Arrivl.2016.1080p.WEB-DL.mkv to Arrival (2016).');
    expect(status).toBeInTheDocument();
    expect(sent).toEqual([{ titleRef: 'tmdb:movie/329865' }]);
    expect(within(await inbox()).queryByRole('listitem', { name: 'Arrivl.2016.1080p.WEB-DL.mkv' })).not.toBeInTheDocument();

    await user.click(screen.getByRole('link', { name: 'Open Arrival (2016)' }));
    expect(await screen.findByRole('heading', { level: 1, name: 'Arrival' })).toBeInTheDocument();
  });

  it('searches from a card with the guess filled in and maps the show episode', async () => {
    const user = userEvent.setup();
    const sent = recordRelinks();
    renderApp('/attention');

    await user.click(within(await card('Office.S01E03.mkv')).getByRole('button', { name: 'Search for Office.S01E03.mkv' }));
    const dialog = await screen.findByRole('dialog', { name: 'Fix match' });
    expect(within(dialog).getByRole('heading', { name: 'Fix match' })).toHaveFocus();
    expect(within(dialog).getByRole('textbox', { name: 'Title' })).toHaveValue('Office');
    // Mixed library, but the file name has an episode: shows only.
    expect(within(dialog).getByRole('combobox', { name: 'Type' })).toHaveValue('show');

    const results = await within(dialog).findByRole('list', { name: 'Search results' });
    await user.click(within(results).getByRole('button', { name: /The Office \(2005\)/ }));
    expect(within(dialog).getByRole('textbox', { name: /Season/ })).toHaveValue('1');
    expect(within(dialog).getByRole('textbox', { name: /Episodes/ })).toHaveValue('3');
    await user.click(within(dialog).getByRole('button', { name: 'Link file' }));

    expect(await screen.findByText('Linked Office.S01E03.mkv to The Office (2005).')).toBeInTheDocument();
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(sent).toEqual([{ titleRef: 'tmdb:tv/2316', season: 1, episodes: [3] }]);
  });

  it('says when a library has nothing to fix', async () => {
    server.use(http.get('*/libraries/:libraryId/attention', () => HttpResponse.json({ items: [], pageInfo: {} })));
    renderApp('/attention');

    expect(await screen.findByText(`Nothing in ${mockLibrary.name} needs attention.`)).toBeInTheDocument();
  });

  it('shows why a one-click link failed and what to do next', async () => {
    const user = userEvent.setup();
    server.use(
      http.put('*/media-files/:mediaFileId/match', () =>
        HttpResponse.json(
          {
            title: 'The library is being scanned.',
            status: 409,
            code: 'library.scan_running',
            action: 'Wait for the scan to finish, then try again.',
          },
          { status: 409, headers: { 'Content-Type': 'application/problem+json' } },
        ),
      ),
    );
    renderApp('/attention');

    const movie = await card('Arrivl.2016.1080p.WEB-DL.mkv');
    await user.click(within(movie).getByRole('button', { name: 'Link to Arrival (2016)' }));

    expect(await within(movie).findByRole('alert')).toHaveTextContent(
      'The library is being scanned. Wait for the scan to finish, then try again.',
    );
  });
});
