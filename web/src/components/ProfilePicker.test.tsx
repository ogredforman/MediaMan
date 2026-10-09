import { screen, waitFor, within } from '@testing-library/react';
import userEvent, { type UserEvent } from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import { server } from '../mocks/node';
import { mockProfiles } from '../mocks/profiles';
import { renderApp } from '../test/renderApp';

const picker = () => screen.findByRole('heading', { name: "Who's watching?" });
const profileList = () => screen.getByRole('list', { name: 'Profiles' });

async function addProfile(user: UserEvent, name: string, avatar?: string) {
  await user.click(await screen.findByRole('button', { name: 'Add profile' }));
  await user.type(await screen.findByLabelText('Name'), name);
  if (avatar !== undefined) await user.click(screen.getByRole('radio', { name: avatar }));
  await user.click(screen.getByRole('button', { name: 'Save' }));
  await screen.findByRole('heading', { name: 'Manage profiles' });
}

describe('profile picker', () => {
  it('shows when the session has no profile, and goes on to the page asked for once one is picked', async () => {
    mockProfiles.clearPick();
    const user = userEvent.setup();
    const { router } = renderApp('/shows');

    expect(await picker()).toBeInTheDocument();
    expect(router.state.location.search).toEqual({ redirect: '/shows' });
    await user.click(within(profileList()).getByRole('button', { name: 'owner' }));

    expect(await screen.findByRole('heading', { name: 'Shows' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Switch profile (owner)' })).toBeInTheDocument();
  });

  it("shows the server's message and next action when the pick fails", async () => {
    mockProfiles.clearPick();
    server.use(
      http.put('*/auth/session/profile', () =>
        HttpResponse.json(
          { title: 'Profile not found.', status: 404, code: 'profile.not_found', action: 'Reload the profile list.' },
          { status: 404, headers: { 'Content-Type': 'application/problem+json' } },
        ),
      ),
    );
    const user = userEvent.setup();
    renderApp('/');

    await picker();
    await user.click(within(profileList()).getByRole('button', { name: 'owner' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Profile not found. Reload the profile list.');
    expect(screen.getByRole('heading', { name: "Who's watching?" })).toBeInTheDocument();
  });

  it('switches profile from the shell and comes back to the same page', async () => {
    const user = userEvent.setup();
    renderApp('/movies');

    await user.click(await screen.findByRole('link', { name: 'Switch profile (owner)' }));
    await user.click(await screen.findByRole('button', { name: 'Manage profiles' }));
    await addProfile(user, 'Kids', 'Teal');
    await user.click(screen.getByRole('button', { name: 'Done' }));
    await user.click(within(profileList()).getByRole('button', { name: 'Kids' }));

    expect(await screen.findByRole('heading', { name: 'Movies' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Switch profile (Kids)' })).toBeInTheDocument();
  });

  it('adds, renames, recolours and deletes profiles in manage mode', async () => {
    const user = userEvent.setup();
    renderApp('/profiles');

    await user.click(await screen.findByRole('button', { name: 'Manage profiles' }));
    await addProfile(user, 'Sam');
    expect(within(profileList()).getByRole('button', { name: 'Edit Sam' })).toBeInTheDocument();

    await user.click(within(profileList()).getByRole('button', { name: 'Edit Sam' }));
    expect(screen.getByRole('heading', { name: 'Edit profile' })).toBeInTheDocument();
    await user.clear(screen.getByLabelText('Name'));
    await user.type(screen.getByLabelText('Name'), 'Samira');
    await user.click(screen.getByRole('radio', { name: 'Rose' }));
    await user.click(screen.getByRole('button', { name: 'Save' }));
    await user.click(await within(profileList()).findByRole('button', { name: 'Edit Samira' }));
    expect(screen.getByRole('radio', { name: 'Rose' })).toBeChecked();

    await user.click(screen.getByRole('button', { name: 'Delete' }));
    expect(screen.getByText(/Delete Samira\? Its watch history goes too/)).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Delete profile' }));

    await screen.findByRole('heading', { name: 'Manage profiles' });
    await waitFor(() => {
      expect(within(profileList()).queryByRole('button', { name: 'Edit Samira' })).not.toBeInTheDocument();
    });
  });

  it('asks again after the picked profile is deleted', async () => {
    const user = userEvent.setup();
    const { router } = renderApp('/profiles');

    await user.click(await screen.findByRole('button', { name: 'Manage profiles' }));
    await addProfile(user, 'Sam');
    await user.click(screen.getByRole('button', { name: 'Done' }));
    await user.click(within(profileList()).getByRole('button', { name: 'Sam' }));
    await user.click(await screen.findByRole('link', { name: 'Switch profile (Sam)' }));
    await user.click(await screen.findByRole('button', { name: 'Manage profiles' }));
    await user.click(within(profileList()).getByRole('button', { name: 'Edit Sam' }));
    await user.click(screen.getByRole('button', { name: 'Delete' }));
    await user.click(screen.getByRole('button', { name: 'Delete profile' }));
    await screen.findByRole('heading', { name: 'Manage profiles' });

    // Leaving the picker without a pick goes straight back to it.
    await router.navigate({ to: '/movies' });
    await waitFor(() => { expect(router.state.location.pathname).toBe('/profiles'); });
    expect(screen.queryByRole('heading', { name: 'Movies' })).not.toBeInTheDocument();
  });

  it('shows a taken name and a too-long or empty name on the name field', async () => {
    const user = userEvent.setup();
    renderApp('/profiles');

    await user.click(await screen.findByRole('button', { name: 'Manage profiles' }));
    await user.click(screen.getByRole('button', { name: 'Add profile' }));
    await user.click(screen.getByRole('button', { name: 'Save' }));
    expect(screen.getByRole('alert')).toHaveTextContent('Enter a name of 1 to 32 characters.');

    await user.type(screen.getByLabelText('Name'), 'OWNER');
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Save' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('That name is taken. Choose a different name.');
  });

  it("offers no Delete for the account's only profile, and no Add once there are 10", async () => {
    const user = userEvent.setup();
    renderApp('/profiles');

    await user.click(await screen.findByRole('button', { name: 'Manage profiles' }));
    await user.click(screen.getByRole('button', { name: 'Edit owner' }));
    expect(screen.queryByRole('button', { name: 'Delete' })).not.toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Cancel' }));

    for (let i = 2; i <= 10; i++) await addProfile(user, `Viewer ${String(i)}`);
    expect(within(profileList()).getAllByRole('listitem')).toHaveLength(10);
    expect(screen.queryByRole('button', { name: 'Add profile' })).not.toBeInTheDocument();
  });

  it("shows the server's message when there's no room for another profile", async () => {
    server.use(
      http.post('*/profiles', () =>
        HttpResponse.json(
          {
            title: 'No room for another profile.',
            status: 409,
            code: 'profile.limit',
            action: "Delete a profile you don't use, then add this one.",
          },
          { status: 409, headers: { 'Content-Type': 'application/problem+json' } },
        ),
      ),
    );
    const user = userEvent.setup();
    renderApp('/profiles');

    await user.click(await screen.findByRole('button', { name: 'Manage profiles' }));
    await user.click(screen.getByRole('button', { name: 'Add profile' }));
    await user.type(screen.getByLabelText('Name'), 'Guest');
    await user.click(screen.getByRole('button', { name: 'Save' }));

    expect(await screen.findByRole('alert')).toHaveTextContent(
      "No room for another profile. Delete a profile you don't use, then add this one.",
    );
  });
});
