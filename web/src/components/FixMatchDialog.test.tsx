import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import type { MediaFileMatchRequest } from '../api/mediaman.schemas';
import { formatEpisodes, parseEpisodes } from '../lib/fixMatch';
import { mockTitleDetails, mockTitles } from '../mocks/handlers';
import { server } from '../mocks/node';
import { renderApp } from '../test/renderApp';

const [arrival, dune, office] = mockTitles.map((t) => t.id) as [string, string, string];

const problem = (body: Record<string, unknown> & { status: number }) =>
  HttpResponse.json(body, { status: body.status, headers: { 'Content-Type': 'application/problem+json' } });

async function openFixMatch(titleId: string) {
  const user = userEvent.setup();
  const rendered = renderApp(`/titles/${titleId}`);
  await user.click(await screen.findByRole('button', { name: 'Fix match' }));
  const dialog = await screen.findByRole('dialog', { name: 'Fix match' });
  return { user, dialog, ...rendered };
}

/** Serves Arrival with only its 2160p file and the missing one, so a single file can be relinked. */
function arrivalWithOneFile() {
  const detail = mockTitleDetails[arrival];
  if (detail?.playable === undefined) throw new Error('Expected Arrival to have a playable.');
  const versions = detail.playable.versions.filter((v) => !v.fileName.includes('WEB-DL'));
  server.use(http.get(`*/titles/${arrival}`, () => HttpResponse.json({ ...detail, playable: { ...detail.playable, versions } })));
}

/** Answers every relink with `titleId`, recording what was sent. */
function relinkTo(titleId: string) {
  const sent: MediaFileMatchRequest[] = [];
  server.use(
    http.put('*/media-files/:mediaFileId/match', async ({ request, params }) => {
      sent.push((await request.json()) as MediaFileMatchRequest);
      return HttpResponse.json({ mediaFileId: String(params.mediaFileId), titleId, status: 'matched' });
    }),
  );
  return sent;
}

describe('parseEpisodes', () => {
  it('reads single numbers, ranges and lists', () => {
    expect(parseEpisodes('3')).toEqual([3]);
    expect(parseEpisodes('1-3')).toEqual([1, 2, 3]);
    expect(parseEpisodes('1–2')).toEqual([1, 2]);
    expect(parseEpisodes('1, 4')).toEqual([1, 4]);
    expect(parseEpisodes('0')).toBeUndefined();
    expect(parseEpisodes('3-1')).toBeUndefined();
    expect(parseEpisodes('one')).toBeUndefined();
    expect(formatEpisodes([1, 2])).toBe('1-2');
    expect(formatEpisodes([1, 4])).toBe('1, 4');
  });
});

describe('FixMatchDialog on a title', () => {
  it('relinks a movie to a searched title and opens it', async () => {
    const sent = relinkTo(dune);
    arrivalWithOneFile();
    const { user, dialog } = await openFixMatch(arrival);

    // Only the file that isn't missing can be relinked, so there's no file step.
    expect(within(dialog).getByRole('textbox', { name: 'Title' })).toHaveValue('Arrival');
    expect(within(dialog).getByRole('textbox', { name: 'Year' })).toHaveValue('2016');
    const title = within(dialog).getByRole('textbox', { name: 'Title' });
    await user.clear(title);
    await user.type(title, 'Dune');
    await user.clear(within(dialog).getByRole('textbox', { name: 'Year' }));
    await user.click(within(dialog).getByRole('button', { name: 'Search' }));
    const results = await within(dialog).findByRole('list', { name: 'Search results' });
    await user.click(within(results).getByRole('button', { name: /Dune \(2021\)/ }));

    expect(await screen.findByRole('heading', { level: 1, name: 'Dune' })).toBeInTheDocument();
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(sent).toEqual([{ titleRef: 'tmdb:movie/438631' }]);
  });

  it('asks which file of a show, then prefills its episodes', async () => {
    const sent = relinkTo(office);
    const { user, dialog } = await openFixMatch(office);

    const files = within(dialog).getByRole('list', { name: 'Files' });
    expect(within(files).getAllByRole('button').map((b) => b.textContent)).toEqual([
      'The.Office.S01E01-E02.720p.WEB-DL.mkvS01E01–E02',
      'The.Office.S02E01.720p.mkvS02E01',
      'The.Office.S02E01.1080p.mkvS02E01',
      'The Office S00E01.mkvS00E01',
    ]);
    await user.click(within(files).getByRole('button', { name: /The.Office.S01E01-E02/ }));
    const results = await within(dialog).findByRole('list', { name: 'Search results' });
    await user.click(within(results).getByRole('button', { name: /The Office \(2001\)/ }));

    expect(within(dialog).getByRole('textbox', { name: /Season/ })).toHaveValue('1');
    const episodes = within(dialog).getByRole('textbox', { name: /Episodes/ });
    expect(episodes).toHaveValue('1-2');
    await user.clear(episodes);
    await user.type(episodes, '2-3');
    await user.click(within(dialog).getByRole('button', { name: 'Link file' }));

    await expect.poll(() => sent).toEqual([{ titleRef: 'tmdb:tv/2996', season: 1, episodes: [2, 3] }]);
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('checks the mapping before sending and shows server field errors on their fields', async () => {
    server.use(
      http.put('*/media-files/:mediaFileId/match', () =>
        problem({
          title: "That match can't be used.",
          status: 400,
          code: 'match.invalid',
          action: 'Fix the highlighted fields, then try again.',
          errors: { episodes: ['The Office has no episode 40 in season 1.'] },
        }),
      ),
    );
    const { user, dialog } = await openFixMatch(office);
    await user.click(within(dialog).getByRole('button', { name: /The.Office.S01E01-E02/ }));
    await user.click(within(await within(dialog).findByRole('list', { name: 'Search results' })).getByRole('button', { name: /The Office \(2005\)/ }));

    const episodes = within(dialog).getByRole('textbox', { name: /Episodes/ });
    await user.clear(episodes);
    await user.type(episodes, 'pilot');
    await user.click(within(dialog).getByRole('button', { name: 'Link file' }));
    expect(within(dialog).getByRole('alert')).toHaveTextContent('Enter episode numbers like 3, 1-2 or 1, 2.');

    await user.clear(episodes);
    await user.type(episodes, '40');
    await user.click(within(dialog).getByRole('button', { name: 'Link file' }));
    expect(await within(dialog).findByText('The Office has no episode 40 in season 1.')).toBeInTheDocument();
    expect(within(dialog).queryByText("That match can't be used.")).not.toBeInTheDocument();
  });

  it('says when the provider is down and keeps the search', async () => {
    server.use(
      http.get('*/catalog/search', () =>
        problem({
          title: "The metadata provider didn't answer.",
          status: 503,
          code: 'metadata.unavailable',
          action: 'Search again in a moment.',
        }),
      ),
    );
    arrivalWithOneFile();
    const { dialog } = await openFixMatch(arrival);

    const alert = await within(dialog).findByRole('alert');
    expect(alert).toHaveTextContent("The metadata provider didn't answer.");
    expect(alert).toHaveTextContent('Search again in a moment.');
    expect(within(dialog).getByRole('button', { name: 'Search' })).toBeEnabled();
  });

  it('closes on Escape and gives focus back to Fix match', async () => {
    const { user } = await openFixMatch(arrival);

    await user.keyboard('{Escape}');

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Fix match' })).toHaveFocus();
  });

  it('is not offered for a title with no file', async () => {
    renderApp(`/titles/${dune}`);

    expect(await screen.findByRole('heading', { level: 1, name: 'Dune' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Fix match' })).not.toBeInTheDocument();
  });
});
