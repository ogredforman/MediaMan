import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import { mockTokens } from '../mocks/handlers';
import { server } from '../mocks/node';
import { storedTokens, storeTokens } from '../lib/session';
import { renderApp } from '../test/renderApp';

const problem = (status: number, code: string, title: string, action: string) =>
  HttpResponse.json({ title, status, code, action }, { status, headers: { 'Content-Type': 'application/problem+json' } });

/** The session probe answers only with the token `login` hands out, like a server with auto sign-in off. */
function requireSignIn() {
  server.use(
    // Falls through to the mock profile store when signed in.
    http.get('*/profiles', ({ request }) =>
      request.headers.get('Authorization') === 'Bearer access-signed-in'
        ? undefined
        : problem(401, 'auth.required', 'Sign in to continue.', 'Sign in again.'),
    ),
  );
}

describe('sign-in', () => {
  it('sends a signed-out visitor to sign-in, then the profile picker, and back to the page they asked for', async () => {
    requireSignIn();
    const user = userEvent.setup();
    const { router } = renderApp('/movies');

    expect(await screen.findByRole('heading', { name: 'Sign in to MediaMan' })).toBeInTheDocument();
    expect(screen.getByRole('img', { name: 'MediaMan logo' })).toHaveAttribute('src', expect.stringContaining('logo'));
    expect(router.state.location.search).toEqual({ redirect: '/movies' });

    await user.type(screen.getByLabelText('Username'), 'owner');
    await user.type(screen.getByLabelText('Password'), 'correct horse battery');
    await user.click(screen.getByRole('button', { name: 'Sign in' }));

    // A new sign-in has no profile picked, so the picker comes first.
    expect(await screen.findByRole('heading', { name: "Who's watching?" })).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'owner' }));
    expect(await screen.findByRole('heading', { name: 'Movies' })).toBeInTheDocument();
    expect(storedTokens()?.accessToken).toBe('access-signed-in');
    expect(screen.getByRole('button', { name: 'Sign out' })).toBeInTheDocument();
  });

  it('keeps the address under /app when the server serves the app there', async () => {
    requireSignIn();
    const user = userEvent.setup();
    const { router } = renderApp('/app/movies', { basepath: '/app' });

    expect(await screen.findByRole('heading', { name: 'Sign in to MediaMan' })).toBeInTheDocument();
    expect(router.history.location.pathname).toBe('/app/sign-in');
    // The redirect is a route path; the router adds /app when it goes back.
    expect(router.state.location.search).toEqual({ redirect: '/movies' });

    await user.type(screen.getByLabelText('Username'), 'owner');
    await user.type(screen.getByLabelText('Password'), 'correct horse battery');
    await user.click(screen.getByRole('button', { name: 'Sign in' }));
    expect(await screen.findByRole('heading', { name: "Who's watching?" })).toBeInTheDocument();
    expect(router.history.location.pathname).toBe('/app/profiles');

    await user.click(screen.getByRole('button', { name: 'owner' }));
    expect(await screen.findByRole('heading', { name: 'Movies' })).toBeInTheDocument();
    expect(router.history.location.pathname).toBe('/app/movies');
  });

  it("shows the server's message and next action when sign-in fails", async () => {
    requireSignIn();
    server.use(
      http.post('*/auth/login', () =>
        problem(401, 'auth.invalid_credentials', 'Wrong username or password.', 'Check them and try again.'),
      ),
    );
    const user = userEvent.setup();
    renderApp('/sign-in');

    await user.type(await screen.findByLabelText('Username'), 'owner');
    await user.type(screen.getByLabelText('Password'), 'wrong');
    await user.click(screen.getByRole('button', { name: 'Sign in' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Wrong username or password. Check them and try again.');
    expect(storedTokens()).toBeUndefined();
  });

  it('signs out and returns to sign-in', async () => {
    requireSignIn();
    let loggedOut = false;
    server.use(
      http.post('*/auth/logout', () => {
        loggedOut = true;
        return new HttpResponse(null, { status: 204 });
      }),
    );
    storeTokens(mockTokens('signed-in'));
    const user = userEvent.setup();
    renderApp('/shows');

    await user.click(await screen.findByRole('button', { name: 'Sign out' }));

    expect(await screen.findByRole('heading', { name: 'Sign in to MediaMan' })).toBeInTheDocument();
    expect(loggedOut).toBe(true);
    expect(storedTokens()).toBeUndefined();
  });

  it('goes to sign-in when another tab signs out', async () => {
    requireSignIn();
    storeTokens(mockTokens('signed-in'));
    renderApp('/');
    await screen.findByRole('heading', { name: 'Home' });

    localStorage.removeItem('mediaman.auth');
    window.dispatchEvent(new StorageEvent('storage', { key: 'mediaman.auth' }));

    expect(await screen.findByRole('heading', { name: 'Sign in to MediaMan' })).toBeInTheDocument();
  });

  it('skips sign-in when already signed in, and never redirects off-site', async () => {
    storeTokens(mockTokens('signed-in'));
    const { router } = renderApp('/sign-in?redirect=%2F%2Fevil.example');

    await waitFor(() => { expect(router.state.location.pathname).toBe('/'); });
    expect(await screen.findByRole('heading', { name: 'Home' })).toBeInTheDocument();
  });

  it('shows no Sign out without a stored session (dev auto sign-in)', async () => {
    renderApp('/');

    expect(await screen.findByRole('heading', { name: 'Home' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Sign out' })).not.toBeInTheDocument();
  });
});
