import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { afterEach, describe, expect, it } from 'vitest';
import { Permission, type MetadataSettingsRequest, type PasswordPolicy } from '../api/mediaman.schemas';
import type { MediamanDesktop } from '../lib/desktop';
import { mockHealth } from '../mocks/handlers';
import { server } from '../mocks/node';
import { mockSettings } from '../mocks/settings';
import { createFakeDesktopPlayer } from '../test/fakeDesktopPlayer';
import { renderApp } from '../test/renderApp';

async function settingsNav() {
  return within(await screen.findByRole('navigation', { name: 'Settings' }));
}

describe('Settings sections', () => {
  it('shows every section to the Superadmin', async () => {
    renderApp('/settings');

    const nav = await settingsNav();
    expect(await nav.findByRole('link', { name: 'Server' })).toBeInTheDocument();
    expect(nav.getAllByRole('link').map((link) => link.textContent)).toEqual([
      'Libraries',
      'Playback',
      'Metadata',
      'Server',
      'Security',
      'About and licences',
    ]);
    expect(await screen.findByRole('heading', { name: 'Libraries' })).toBeInTheDocument();
  });

  it('hides the sections the account lacks the permission for, and starts on the first it has', async () => {
    mockSettings.setPermissions(['library.view']);
    renderApp('/settings');

    expect(await screen.findByRole('heading', { name: 'Playback' })).toBeInTheDocument();
    const nav = await settingsNav();
    expect(nav.getAllByRole('link').map((link) => link.textContent)).toEqual(['Playback', 'About and licences']);
  });

  it("hides Security from an account that isn't the Superadmin, even with every permission", async () => {
    mockSettings.setPermissions(Object.values(Permission));
    renderApp('/settings/about');

    const nav = await settingsNav();
    expect(await nav.findByRole('link', { name: 'Server' })).toBeInTheDocument();
    expect(nav.queryByRole('link', { name: 'Security' })).not.toBeInTheDocument();
  });

  it('says what to do when a hidden section is opened by its address', async () => {
    mockSettings.setPermissions(['library.view']);
    renderApp('/settings/server');

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent("You don't have access to Server settings.");
    expect(alert).toHaveTextContent("Ask an admin to give your role the 'server.control' permission.");
    expect(within(alert).getByRole('link', { name: 'Back to Settings' })).toBeInTheDocument();
  });

  describe('Playback', () => {
    afterEach(() => {
      delete window.mediamanDesktop;
    });

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

    it('says MPV settings are in the desktop app in a browser', async () => {
      renderApp('/settings/playback');

      expect(await screen.findByText(/The MPV player settings are in the MediaMan desktop app/)).toBeInTheDocument();
      expect(screen.queryByRole('button', { name: 'Choose MPV…' })).not.toBeInTheDocument();
    });

    it("keeps the MPV in use and says why when the picked file isn't MPV", async () => {
      const user = userEvent.setup();
      const player = installDesktop();
      player.refuseNextPick("C:/Windows/notepad.exe isn't MPV, so MediaMan won't run it. Choose the MPV player (mpv.exe) instead.");
      renderApp('/settings/playback');

      expect(await screen.findByText('The default: mpv from your PATH')).toBeInTheDocument();
      await user.click(screen.getByRole('button', { name: 'Choose MPV…' }));

      const alert = await screen.findByRole('alert');
      expect(alert).toHaveTextContent("That file wasn't used: C:/Windows/notepad.exe isn't MPV, so MediaMan won't run it.");
      expect(alert).toHaveTextContent('Press Choose MPV to pick MPV, or keep the MPV above.');
      expect(screen.getByText('The default: mpv from your PATH')).toBeInTheDocument();
    });

    it('names the MPV that comes with the installed app', async () => {
      const player = installDesktop();
      player.setMpvSetting({ path: '/opt/MediaMan/resources/mpv/mpv', source: 'bundled' });
      renderApp('/settings/playback');

      expect(await screen.findByText('The default: the MPV that comes with MediaMan')).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Use default' })).toBeDisabled();
    });

    it('shows the MPV in use, picks another and goes back to the default', async () => {
      const user = userEvent.setup();
      const player = installDesktop();
      player.setMpvSetting({ path: '/opt/mpv/bin/mpv', source: 'environment' });
      renderApp('/settings/playback');

      expect(await screen.findByText('/opt/mpv/bin/mpv')).toBeInTheDocument();
      expect(screen.getByText('From the MEDIAMAN_MPV_PATH environment variable')).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Use default' })).toBeDisabled();

      player.pickMpv('/home/me/mpv');
      await user.click(screen.getByRole('button', { name: 'Choose MPV…' }));
      expect(await screen.findByText('/home/me/mpv')).toBeInTheDocument();
      expect(screen.getByText('Picked in Settings')).toBeInTheDocument();

      await user.click(screen.getByRole('button', { name: 'Use default' }));
      expect(await screen.findByText('The default: mpv from your PATH')).toBeInTheDocument();
      expect(screen.getByText('mpv')).toBeInTheDocument();
    });
  });

  describe('Metadata', () => {
    it('sets and removes the TMDB token override', async () => {
      const user = userEvent.setup();
      const sent: MetadataSettingsRequest[] = [];
      server.use(
        http.put('*/settings/metadata', async ({ request }) => {
          sent.push((await request.clone().json()) as MetadataSettingsRequest);
          return undefined; // On to the store's handler.
        }),
      );
      renderApp('/settings/metadata');

      expect(await screen.findByText('The token that ships with MediaMan')).toBeInTheDocument();
      expect(screen.queryByRole('button', { name: 'Remove my token' })).not.toBeInTheDocument();
      await user.type(screen.getByLabelText('Your TMDB access token'), 'eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJ0In0.c2ln');
      await user.click(screen.getByRole('button', { name: 'Save token' }));

      expect(await screen.findByText('Your own token, set here')).toBeInTheDocument();
      expect(screen.getByLabelText('Your TMDB access token')).toHaveValue('');

      await user.click(screen.getByRole('button', { name: 'Remove my token' }));
      const confirm = screen.getByRole('alertdialog');
      expect(confirm).toHaveTextContent("It's deleted from this server");
      await user.click(within(confirm).getByRole('button', { name: 'Remove token' }));

      expect(await screen.findByText('The token that ships with MediaMan')).toBeInTheDocument();
      expect(sent).toEqual([{ tmdbAccessToken: 'eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJ0In0.c2ln' }, { tmdbAccessToken: '' }]);
    });

    it("shows the server's reason on the field when the token can't be used", async () => {
      const user = userEvent.setup();
      renderApp('/settings/metadata');

      await user.type(await screen.findByLabelText('Your TMDB access token'), '0123456789abcdef0123456789abcdef');
      await user.click(screen.getByRole('button', { name: 'Save token' }));

      expect(await screen.findByRole('alert')).toHaveTextContent("That's the short API key.");
      expect(screen.getByText('The token that ships with MediaMan')).toBeInTheDocument();
    });
  });

  describe('Server', () => {
    it('records a new mode and says the server must restart to use it', async () => {
      const user = userEvent.setup();
      renderApp('/settings/server');

      const local = await screen.findByRole('radio', { name: /This computer only/ });
      expect(local).toBeChecked();
      expect(screen.getByRole('button', { name: 'Save mode' })).toBeDisabled();
      expect(screen.queryByText(/Restart the MediaMan server/)).not.toBeInTheDocument();

      await user.click(screen.getByRole('radio', { name: /My home network/ }));
      await user.click(screen.getByRole('button', { name: 'Save mode' }));

      expect(await screen.findByText(/Restart the MediaMan server to use this mode/)).toBeInTheDocument();
      expect(screen.getByRole('radio', { name: /My home network/ })).toBeChecked();
      expect(screen.getByRole('button', { name: 'Save mode' })).toBeDisabled();
    });
  });

  describe('Security', () => {
    function recordPolicies() {
      const sent: PasswordPolicy[] = [];
      server.use(
        http.put('*/settings/password-policy', async ({ request }) => {
          sent.push((await request.clone().json()) as PasswordPolicy);
          return undefined; // On to the store's handler.
        }),
      );
      return sent;
    }

    it('turns on strong passwords and states the new rule', async () => {
      const user = userEvent.setup();
      const sent = recordPolicies();
      renderApp('/settings/security');

      expect(await screen.findByRole('checkbox', { name: /Require passwords/ })).toBeChecked();
      expect(screen.getByRole('checkbox', { name: /Strong passwords/ })).not.toBeChecked();
      expect(screen.getByText('At least 8 characters.')).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled();

      await user.click(screen.getByRole('checkbox', { name: /Strong passwords/ }));
      expect(screen.getByText(/At least 12 characters, mixing/)).toBeInTheDocument();
      await user.click(screen.getByRole('button', { name: 'Save' }));

      expect(await screen.findByText("Saved. It applies from each account's next sign-in.")).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled();
      expect(sent).toEqual([{ requirePasswords: true, strongPasswords: true }]);
    });

    it('asks before turning passwords off', async () => {
      const user = userEvent.setup();
      const sent = recordPolicies();
      renderApp('/settings/security');

      await user.click(await screen.findByRole('checkbox', { name: /Require passwords/ }));
      await user.click(screen.getByRole('button', { name: 'Save' }));
      const confirm = screen.getByRole('alertdialog');
      expect(confirm).toHaveTextContent('Anyone who can reach this server can then sign in as any account');
      expect(within(confirm).getByRole('button', { name: 'Keep them on' })).toHaveFocus();
      await user.click(within(confirm).getByRole('button', { name: 'Keep them on' }));
      expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
      expect(sent).toEqual([]);

      await user.click(screen.getByRole('button', { name: 'Save' }));
      await user.click(within(screen.getByRole('alertdialog')).getByRole('button', { name: 'Turn passwords off' }));

      expect(await screen.findByText("Saved. It applies from each account's next sign-in.")).toBeInTheDocument();
      expect(screen.getByRole('checkbox', { name: /Require passwords/ })).not.toBeChecked();
      expect(sent).toEqual([{ requirePasswords: false, strongPasswords: false }]);
    });

    it("shows the server's problem when the policy can't be saved", async () => {
      const user = userEvent.setup();
      server.use(
        http.put('*/settings/password-policy', () =>
          HttpResponse.json(
            {
              title: "Passwords can't be turned off while remote access is on.",
              status: 400,
              code: 'bad_request',
              action: 'Turn remote access off first.',
            },
            { status: 400, headers: { 'Content-Type': 'application/problem+json' } },
          ),
        ),
      );
      renderApp('/settings/security');

      await user.click(await screen.findByRole('checkbox', { name: /Require passwords/ }));
      await user.click(screen.getByRole('button', { name: 'Save' }));
      await user.click(within(screen.getByRole('alertdialog')).getByRole('button', { name: 'Turn passwords off' }));

      expect(await screen.findByRole('alert')).toHaveTextContent(
        "Passwords can't be turned off while remote access is on. Turn remote access off first.",
      );
    });

    it("says only the owner can change it when another account opens it by its address", async () => {
      mockSettings.setPermissions(Object.values(Permission));
      renderApp('/settings/security');

      const alert = await screen.findByRole('alert');
      expect(alert).toHaveTextContent("Only the server's owner can change Security settings.");
      expect(within(alert).getByRole('link', { name: 'Back to Settings' })).toBeInTheDocument();
    });
  });

  describe('About and licences', () => {
    it('shows the logo, the version, the licence and source, the TMDB notice and the third-party list', async () => {
      renderApp('/settings/about');

      expect(await screen.findByText(`Server version ${mockHealth.version}`)).toBeInTheDocument();
      const about = screen.getByRole('region', { name: 'About and licences' });
      expect(within(about).getByRole('img', { name: 'MediaMan logo' })).toHaveAttribute('src', expect.stringContaining('logo'));
      expect(screen.getByRole('link', { name: 'GNU Affero General Public License v3.0' })).toBeInTheDocument();
      expect(screen.getByRole('link', { name: 'source code' })).toHaveAttribute('href', 'https://github.com/ogredforman/MediaMan');
      expect(screen.getByText(/This product uses the TMDB API but is not endorsed or certified by TMDB/)).toBeInTheDocument();
      const list = screen.getByRole('list', { name: 'Third-party software' });
      expect(within(list).getByRole('link', { name: 'mpv' })).toBeInTheDocument();
    });
  });
});
