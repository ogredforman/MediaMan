import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import type { SetPasswordRequest } from '../api/mediaman.schemas';
import { mockTokens } from '../mocks/handlers';
import { server } from '../mocks/node';
import { mockSettings } from '../mocks/settings';
import { storedTokens, storeTokens } from '../lib/session';
import { renderApp } from '../test/renderApp';

const problem = (status: number, code: string, title: string, action: string, errors?: Record<string, string[]>) =>
  HttpResponse.json(
    { title, status, code, action, errors },
    { status, headers: { 'Content-Type': 'application/problem+json' } },
  );

const heading = () => screen.findByRole('heading', { name: 'Set a new password' });

describe('the forced password change', () => {
  it('follows sign-in, then carries on to the profile picker and the page asked for', async () => {
    mockSettings.requirePasswordChange();
    server.use(
      http.get('*/profiles', ({ request }) =>
        request.headers.get('Authorization') === null
          ? problem(401, 'auth.required', 'Sign in to continue.', 'Sign in again.')
          : undefined,
      ),
    );
    const user = userEvent.setup();
    const { router } = renderApp('/movies');

    await user.type(await screen.findByLabelText('Username'), 'owner');
    await user.type(screen.getByLabelText('Password'), 'old pass');
    await user.click(screen.getByRole('button', { name: 'Sign in' }));

    expect(await heading()).toBeInTheDocument();
    expect(router.state.location.pathname).toBe('/change-password');
    expect(router.state.location.search).toEqual({ redirect: '/movies' });
    expect(screen.getByText('At least 8 characters.')).toBeInTheDocument();

    await user.type(screen.getByLabelText('New password'), 'eight888');
    await user.type(screen.getByLabelText('Confirm new password'), 'eight888');
    await user.click(screen.getByRole('button', { name: 'Save and continue' }));

    expect(await screen.findByRole('heading', { name: "Who's watching?" })).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'owner' }));
    expect(await screen.findByRole('heading', { name: 'Movies' })).toBeInTheDocument();
  });

  it('is where a shell page goes when the session probe says the password must change', async () => {
    storeTokens(mockTokens('signed-in'));
    mockSettings.requirePasswordChange();
    const { router } = renderApp('/shows');

    expect(await heading()).toBeInTheDocument();
    expect(router.state.location.search).toEqual({ redirect: '/shows' });
  });

  it('is where Profiles goes too', async () => {
    storeTokens(mockTokens('signed-in'));
    mockSettings.requirePasswordChange();
    const { router } = renderApp('/profiles');

    expect(await heading()).toBeInTheDocument();
    expect(router.state.location.search).toEqual({ redirect: '/profiles' });
  });

  it('states the strong rule and checks it before sending', async () => {
    storeTokens(mockTokens('signed-in'));
    mockSettings.setPasswordPolicy({ requirePasswords: true, strongPasswords: true });
    mockSettings.requirePasswordChange();
    let sent = false;
    server.use(http.put('*/account/password', () => { sent = true; return undefined; }));
    const user = userEvent.setup();
    renderApp('/change-password');

    expect(
      await screen.findByText(
        'At least 12 characters, mixing at least 3 of: uppercase letters, lowercase letters, digits and symbols.',
      ),
    ).toBeInTheDocument();
    await user.type(screen.getByLabelText('New password'), 'alllowercase');
    await user.type(screen.getByLabelText('Confirm new password'), 'alllowercase');
    await user.click(screen.getByRole('button', { name: 'Save and continue' }));

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Mix at least 3 of: uppercase letters, lowercase letters, digits and symbols.',
    );
    expect(sent).toBe(false);
  });

  it("says when the two passwords don't match", async () => {
    storeTokens(mockTokens('signed-in'));
    mockSettings.requirePasswordChange();
    const user = userEvent.setup();
    renderApp('/change-password');

    await user.type(await screen.findByLabelText('New password'), 'eight888');
    await user.type(screen.getByLabelText('Confirm new password'), 'eight889');
    await user.click(screen.getByRole('button', { name: 'Save and continue' }));

    expect(await screen.findByRole('alert')).toHaveTextContent("The passwords don't match.");
  });

  it("shows the server's errors.newPassword on the field, with no current password sent", async () => {
    storeTokens(mockTokens('signed-in'));
    mockSettings.requirePasswordChange();
    let sent: SetPasswordRequest | undefined;
    server.use(
      http.put('*/account/password', async ({ request }) => {
        sent = (await request.json()) as SetPasswordRequest;
        return problem(400, 'account.password_invalid', 'Check the password.', 'Choose a password that meets the policy, then save.', {
          newPassword: ['Use a password of at least 12 characters.'],
        });
      }),
    );
    const user = userEvent.setup();
    renderApp('/change-password');

    await user.type(await screen.findByLabelText('New password'), 'eight888');
    await user.type(screen.getByLabelText('Confirm new password'), 'eight888');
    await user.click(screen.getByRole('button', { name: 'Save and continue' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Use a password of at least 12 characters.');
    expect(sent).toEqual({ newPassword: 'eight888' });
    expect(screen.getByRole('heading', { name: 'Set a new password' })).toBeInTheDocument();
  });

  it('can sign out instead', async () => {
    storeTokens(mockTokens('signed-in'));
    mockSettings.requirePasswordChange();
    const user = userEvent.setup();
    renderApp('/shows');

    await heading();
    await user.click(screen.getByRole('button', { name: 'Sign out' }));

    expect(await screen.findByRole('heading', { name: 'Sign in to MediaMan' })).toBeInTheDocument();
    expect(storedTokens()).toBeUndefined();
  });
});
