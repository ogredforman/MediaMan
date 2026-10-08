import { screen, waitFor } from '@testing-library/react';
import userEvent, { type UserEvent } from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import type { SetupRequest } from '../api/mediaman.schemas';
import { mockTokens } from '../mocks/handlers';
import { server } from '../mocks/node';
import { storedTokens, storeTokens } from '../lib/session';
import { renderApp } from '../test/renderApp';

const problem = (body: Record<string, unknown> & { status: number }) =>
  HttpResponse.json(body, { status: body.status, headers: { 'Content-Type': 'application/problem+json' } });

const setupRequired = () =>
  problem({ title: 'MediaMan is not set up yet.', status: 423, code: 'setup.required', action: 'Run the setup wizard.' });

async function fillAccount(user: UserEvent, password = 'correct horse battery') {
  await user.type(await screen.findByLabelText('Username'), 'owner');
  await user.type(screen.getByLabelText('Password'), password);
  await user.type(screen.getByLabelText('Confirm password'), password);
  await user.click(screen.getByRole('button', { name: 'Next' }));
}

async function reachLibraryStep(user: UserEvent) {
  await fillAccount(user);
  await user.click(await screen.findByLabelText(/My home network/));
  await user.click(screen.getByRole('button', { name: 'Next' }));
  await screen.findByRole('heading', { name: 'Add your first library' });
}

describe('setup wizard', () => {
  it('sends an un-set-up server to the wizard', async () => {
    server.use(http.get('*/libraries', setupRequired));
    const { router } = renderApp('/movies');

    expect(await screen.findByRole('heading', { name: 'Create the owner account' })).toBeInTheDocument();
    expect(router.state.location.pathname).toBe('/setup');
  });

  it('drops a left-over session and shows the wizard, rather than looping back to Home', async () => {
    storeTokens(mockTokens('stale'));
    server.use(http.get('*/libraries', setupRequired));
    const { router } = renderApp('/');

    expect(await screen.findByRole('heading', { name: 'Create the owner account' })).toBeInTheDocument();
    expect(router.state.location.pathname).toBe('/setup');
    expect(storedTokens()).toBeUndefined();
  });

  it("clears a field's error when it's edited, and focuses each new step's heading", async () => {
    const user = userEvent.setup();
    renderApp('/setup');

    await user.click(await screen.findByRole('button', { name: 'Next' }));
    expect(screen.getByText('Enter a username.')).toBeInTheDocument();
    await user.type(screen.getByLabelText('Username'), 'o');
    expect(screen.queryByText('Enter a username.')).not.toBeInTheDocument();

    await user.type(screen.getByLabelText('Password'), 'correct horse battery');
    await user.type(screen.getByLabelText('Confirm password'), 'correct horse battery');
    await user.click(screen.getByRole('button', { name: 'Next' }));
    expect(await screen.findByRole('heading', { name: 'Choose who can connect' })).toHaveFocus();
  });

  it('sends a sign-in attempt on an un-set-up server to the wizard', async () => {
    server.use(http.post('*/auth/login', setupRequired));
    const user = userEvent.setup();
    renderApp('/sign-in');

    await user.type(await screen.findByLabelText('Username'), 'owner');
    await user.type(screen.getByLabelText('Password'), 'anything');
    await user.click(screen.getByRole('button', { name: 'Sign in' }));

    expect(await screen.findByRole('heading', { name: 'Create the owner account' })).toBeInTheDocument();
  });

  it('walks one step per screen, then signs the owner in and lands on Home', async () => {
    let sent: SetupRequest | undefined;
    server.use(
      http.post('*/setup', async ({ request }) => {
        sent = (await request.json()) as SetupRequest;
        return HttpResponse.json({
          accessToken: 'access-setup',
          accessTokenExpiresAt: new Date(Date.now() + 15 * 60_000).toISOString(),
          refreshToken: 'refresh-setup',
          refreshTokenExpiresAt: new Date(Date.now() + 30 * 86_400_000).toISOString(),
        });
      }),
    );
    const user = userEvent.setup();
    const { router } = renderApp('/setup');

    expect(await screen.findByText('Step 1 of 3')).toBeInTheDocument();
    await fillAccount(user);
    expect(await screen.findByText('Step 2 of 3')).toBeInTheDocument();
    expect(screen.getByRole('radio', { name: /This computer only/ })).toBeChecked();
    await user.click(screen.getByRole('button', { name: 'Next' }));

    expect(await screen.findByText('Step 3 of 3')).toBeInTheDocument();
    await user.type(screen.getByLabelText('Folder path'), '/mnt/media/movies');
    await user.selectOptions(screen.getByLabelText('Contains'), 'movies');
    await user.click(screen.getByRole('button', { name: 'Finish setup' }));

    await waitFor(() => { expect(router.state.location.pathname).toBe('/'); });
    expect(await screen.findByRole('heading', { name: 'Home' })).toBeInTheDocument();
    expect(storedTokens()?.accessToken).toBe('access-setup');
    expect(sent).toEqual({
      username: 'owner',
      password: 'correct horse battery',
      mode: 'local',
      library: { path: '/mnt/media/movies', contentType: 'movies' },
    });
  });

  it("checks the account step before moving on", async () => {
    const user = userEvent.setup();
    renderApp('/setup');

    await user.click(await screen.findByRole('button', { name: 'Next' }));
    expect(screen.getByText('Enter a username.')).toBeInTheDocument();
    expect(screen.getByText('Use a password of at least 12 characters.')).toBeInTheDocument();

    await user.type(screen.getByLabelText('Username'), 'owner');
    await user.type(screen.getByLabelText('Password'), 'correct horse battery');
    await user.type(screen.getByLabelText('Confirm password'), 'correct horse batter');
    await user.click(screen.getByRole('button', { name: 'Next' }));

    expect(screen.getByText("The passwords don't match. Type the same password twice.")).toBeInTheDocument();
    expect(screen.getByText('Step 1 of 3')).toBeInTheDocument();
  });

  it("shows an unreadable path on the library step, and keeps the user's choices", async () => {
    server.use(
      http.post('*/setup', () =>
        problem({
          title: 'Some setup fields need fixing.',
          status: 400,
          code: 'setup.invalid',
          errors: { 'library.path': ["MediaMan can't read that folder. Check the path and its permissions."] },
        }),
      ),
    );
    const user = userEvent.setup();
    renderApp('/setup');
    await reachLibraryStep(user);

    await user.type(screen.getByLabelText('Folder path'), '/nope');
    await user.click(screen.getByRole('button', { name: 'Finish setup' }));

    expect(await screen.findByText("MediaMan can't read that folder. Check the path and its permissions.")).toBeInTheDocument();
    expect(screen.getByText('Step 3 of 3')).toBeInTheDocument();
    expect(storedTokens()).toBeUndefined();

    await user.click(screen.getByRole('button', { name: 'Back' }));
    expect(screen.getByRole('radio', { name: /My home network/ })).toBeChecked();
  });

  it('returns to the first step with an error', async () => {
    server.use(
      http.post('*/setup', () =>
        problem({
          title: 'Some setup fields need fixing.',
          status: 400,
          code: 'setup.invalid',
          errors: { username: ['Enter a username of 1 to 64 characters.'], 'library.path': ['Enter a full path.'] },
        }),
      ),
    );
    const user = userEvent.setup();
    renderApp('/setup');
    await reachLibraryStep(user);

    await user.type(screen.getByLabelText('Folder path'), 'relative');
    await user.click(screen.getByRole('button', { name: 'Finish setup' }));

    expect(await screen.findByText('Enter a username of 1 to 64 characters.')).toBeInTheDocument();
    expect(screen.getByText('Step 1 of 3')).toBeInTheDocument();
  });

  it('says when setup already ran and links to sign-in', async () => {
    server.use(
      http.post('*/setup', () =>
        problem({ title: 'MediaMan is already set up.', status: 409, code: 'setup.complete', action: 'Sign in instead.' }),
      ),
    );
    const user = userEvent.setup();
    renderApp('/setup');
    await reachLibraryStep(user);

    await user.type(screen.getByLabelText('Folder path'), '/mnt/media');
    await user.click(screen.getByRole('button', { name: 'Finish setup' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('MediaMan is already set up. Sign in instead.');
    expect(screen.getByRole('link', { name: 'Go to sign in' })).toHaveAttribute('href', '/sign-in');
  });
});
