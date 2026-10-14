import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http } from 'msw';
import { afterEach, describe, expect, it } from 'vitest';
import type { MetadataSettingsRequest } from '../api/mediaman.schemas';
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

  describe('About and licences', () => {
    it('shows the version, the licence and source, the TMDB notice and the third-party list', async () => {
      renderApp('/settings/about');

      expect(await screen.findByText(`Server version ${mockHealth.version}`)).toBeInTheDocument();
      expect(screen.getByRole('link', { name: 'GNU Affero General Public License v3.0' })).toBeInTheDocument();
      expect(screen.getByRole('link', { name: 'source code' })).toHaveAttribute('href', 'https://github.com/ogredforman/MediaMan');
      expect(screen.getByText(/This product uses the TMDB API but is not endorsed or certified by TMDB/)).toBeInTheDocument();
      const list = screen.getByRole('list', { name: 'Third-party software' });
      expect(within(list).getByRole('link', { name: 'mpv' })).toBeInTheDocument();
    });
  });
});
