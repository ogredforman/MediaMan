import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import type { LibraryCreateRequest, LibraryUpdateRequest } from '../api/mediaman.schemas';
import { mockLibrary } from '../mocks/handlers';
import { mockLibraries } from '../mocks/libraries';
import { server } from '../mocks/node';
import { renderApp } from '../test/renderApp';

function libraryList() {
  return screen.findByRole('list', { name: 'Library list' });
}

async function card(name: string) {
  return within(await libraryList()).findByRole('listitem', { name });
}

describe('LibrarySettings', () => {
  it('opens from the Settings link and lists each library with its folders and scan state', async () => {
    const user = userEvent.setup();
    mockLibraries.setScan(mockLibrary.id, { state: 'running', phase: 'matching', startedAt: '2026-10-06T12:00:00Z' });
    renderApp();
    await user.click(await screen.findByRole('link', { name: 'Settings' }));

    expect(await screen.findByRole('heading', { name: 'Libraries' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Settings' })).toHaveClass('active');
    const media = await card('Media');
    expect(within(media).getByText('Movies and shows · No scheduled full scans')).toBeInTheDocument();
    expect(within(within(media).getByRole('list', { name: 'Folders in Media' })).getByText('/media')).toBeInTheDocument();
    expect(within(media).getByText('Media: matching titles…')).toBeInTheDocument();
    expect(within(media).getByRole('button', { name: 'Scan Media' })).toBeDisabled();
  });

  it('adds a library with several folders and the default scan interval', async () => {
    const user = userEvent.setup();
    let sent: LibraryCreateRequest | undefined;
    server.use(
      http.post('*/libraries', async ({ request }) => {
        sent = (await request.clone().json()) as LibraryCreateRequest;
        return undefined; // On to the store's handler.
      }),
    );
    renderApp('/settings/libraries');
    await libraryList();

    await user.click(screen.getByRole('button', { name: 'Add library' }));
    expect(screen.getByRole('heading', { name: 'Add a library' })).toHaveFocus();
    await user.type(screen.getByRole('textbox', { name: 'Folder 1' }), '/mnt/films');
    await user.click(screen.getByRole('button', { name: 'Add another folder' }));
    await user.type(screen.getByRole('textbox', { name: 'Folder 2' }), '/mnt/more-films/');
    await user.selectOptions(screen.getByRole('combobox', { name: 'Contains' }), 'Movies');
    expect(screen.getByRole('combobox', { name: 'Full scan' })).toHaveDisplayValue('Every 6 hours');
    await user.click(screen.getByRole('button', { name: 'Add library' }));

    const films = await card('films');
    expect(within(films).getByText('Movies · Full scan every 6 hours')).toBeInTheDocument();
    expect(within(films).getByText('/mnt/more-films')).toBeInTheDocument();
    expect(within(films).getByText('films: scan queued')).toBeInTheDocument();
    expect(sent).toEqual({ contentType: 'movies', rootPaths: ['/mnt/films', '/mnt/more-films/'], scanIntervalMinutes: 360 });
  });

  it('shows each folder error under its folder and keeps it there when an earlier folder is removed', async () => {
    const user = userEvent.setup();
    renderApp('/settings/libraries');
    await libraryList();

    await user.click(screen.getByRole('button', { name: 'Add library' }));
    await user.type(screen.getByRole('textbox', { name: 'Folder 1' }), 'relative/path');
    await user.click(screen.getByRole('button', { name: 'Add another folder' }));
    await user.type(screen.getByRole('textbox', { name: 'Folder 2' }), '/media/movies');
    await user.click(screen.getByRole('button', { name: 'Add library' }));

    const first = await screen.findByText(/Enter the full path to the folder/);
    const second = screen.getByText(/This folder overlaps the library 'Media'/);
    // Each message sits with its own input.
    expect(first.parentElement).toContainElement(screen.getByRole('textbox', { name: 'Folder 1' }));
    expect(second.parentElement).toContainElement(screen.getByRole('textbox', { name: 'Folder 2' }));

    await user.click(screen.getByRole('button', { name: 'Remove folder 1' }));
    expect(screen.queryByText(/Enter the full path to the folder/)).not.toBeInTheDocument();
    const moved = screen.getByText(/This folder overlaps the library 'Media'/);
    expect(moved.parentElement).toContainElement(screen.getByRole('textbox', { name: 'Folder 1' }));

    // Editing the folder clears its error; the server checks it again on save.
    await user.clear(screen.getByRole('textbox', { name: 'Folder 1' }));
    await user.type(screen.getByRole('textbox', { name: 'Folder 1' }), '/srv/shows');
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Add library' }));
    expect(await card('shows')).toBeInTheDocument();
  });

  it('still answers Save when a validation problem names no field the form shows', async () => {
    const user = userEvent.setup();
    server.use(
      http.post('*/libraries', () =>
        HttpResponse.json(
          {
            title: 'Some library details need fixing.',
            status: 400,
            code: 'library.invalid',
            action: 'Fix the highlighted fields, then save.',
            errors: { kind: ['Only External libraries can be added here.'] },
          },
          { status: 400, headers: { 'Content-Type': 'application/problem+json' } },
        ),
      ),
    );
    renderApp('/settings/libraries');
    await libraryList();

    await user.click(screen.getByRole('button', { name: 'Add library' }));
    await user.type(screen.getByRole('textbox', { name: 'Folder 1' }), '/srv/films');
    await user.click(screen.getByRole('button', { name: 'Add library' }));
    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Some library details need fixing. Fix the highlighted fields, then save.',
    );
  });

  it('checks for empty folders before asking the server', async () => {
    const user = userEvent.setup();
    renderApp('/settings/libraries');
    await libraryList();

    await user.click(screen.getByRole('button', { name: 'Add library' }));
    await user.click(screen.getByRole('button', { name: 'Add library' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('Enter the folder that holds your media.');
    expect(screen.getByRole('heading', { name: 'Add a library' })).toBeInTheDocument();
  });

  it('edits a library, sending only what changed', async () => {
    const user = userEvent.setup();
    const sent: LibraryUpdateRequest[] = [];
    server.use(
      http.patch('*/libraries/:libraryId', async ({ request }) => {
        sent.push((await request.clone().json()) as LibraryUpdateRequest);
        return undefined; // On to the store's handler.
      }),
    );
    renderApp('/settings/libraries');
    await user.click(within(await card('Media')).getByRole('button', { name: 'Edit Media' }));

    expect(screen.getByRole('heading', { name: 'Edit Media' })).toHaveFocus();
    const name = screen.getByRole('textbox', { name: 'Name' });
    await user.clear(name);
    await user.type(name, 'Family media');
    await user.selectOptions(screen.getByRole('combobox', { name: 'Full scan' }), 'Every day');
    await user.click(screen.getByRole('button', { name: 'Save' }));

    const edited = await card('Family media');
    expect(within(edited).getByText('Movies and shows · Full scan every day')).toBeInTheDocument();
    expect(sent).toEqual([{ name: 'Family media', scanIntervalMinutes: 1440 }]);
  });

  it("says what to do when a library's folders can't change during its scan", async () => {
    const user = userEvent.setup();
    mockLibraries.setScan(mockLibrary.id, { state: 'running', phase: 'scanning', startedAt: '2026-10-06T12:00:00Z' });
    renderApp('/settings/libraries');
    await user.click(within(await card('Media')).getByRole('button', { name: 'Edit Media' }));

    await user.click(screen.getByRole('button', { name: 'Add another folder' }));
    await user.type(screen.getByRole('textbox', { name: 'Folder 2' }), '/srv/more');
    await user.click(screen.getByRole('button', { name: 'Save' }));

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'The library is being scanned. Wait for the scan to finish, then try again.',
    );
    expect(screen.getByRole('heading', { name: 'Edit Media' })).toBeInTheDocument();
  });

  it('deletes a library only after a confirmation that says the files stay on disk', async () => {
    const user = userEvent.setup();
    renderApp('/settings/libraries');
    const media = await card('Media');

    await user.click(within(media).getByRole('button', { name: 'Delete Media' }));
    expect(within(media).getByText(/Your files stay on disk; nothing in these folders is deleted or changed\./)).toBeInTheDocument();
    await user.click(within(media).getByRole('button', { name: 'Keep library' }));
    expect(within(media).getByRole('button', { name: 'Delete Media' })).toBeInTheDocument();

    await user.click(within(media).getByRole('button', { name: 'Delete Media' }));
    await user.click(within(media).getByRole('button', { name: 'Delete library' }));
    expect(await screen.findByText('No libraries yet. Add one to start building your collection.')).toBeInTheDocument();
  });

  it("explains a delete the account isn't allowed to make", async () => {
    const user = userEvent.setup();
    server.use(
      http.delete('*/libraries/:libraryId', () =>
        HttpResponse.json(
          {
            title: "You don't have access to this.",
            status: 403,
            code: 'auth.forbidden',
            detail: "Your account doesn't have the 'server.control' permission this needs.",
            action: 'Ask an admin to give your role this permission.',
          },
          { status: 403, headers: { 'Content-Type': 'application/problem+json' } },
        ),
      ),
    );
    renderApp('/settings/libraries');
    const media = await card('Media');

    await user.click(within(media).getByRole('button', { name: 'Delete Media' }));
    await user.click(within(media).getByRole('button', { name: 'Delete library' }));

    expect(await within(media).findByRole('alert')).toHaveTextContent(
      "You don't have access to this. Ask an admin to give your role this permission.",
    );
    expect(await card('Media')).toBeInTheDocument();
  });
});
