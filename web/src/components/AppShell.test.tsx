import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import { mockHealth } from '../mocks/handlers';
import { server } from '../mocks/node';
import { renderApp } from '../test/renderApp';

describe('AppShell', () => {
  it('renders the shell with the logo, navigation and the server version', async () => {
    renderApp();

    expect(await screen.findByRole('heading', { name: 'Home' })).toBeInTheDocument();
    const header = screen.getByRole('banner');
    expect(within(header).getByRole('img', { name: 'MediaMan logo' })).toHaveAttribute('src', expect.stringContaining('logo-wide'));
    expect(within(header).getByText('MediaMan')).toBeInTheDocument();
    expect(screen.getByRole('navigation', { name: 'Main' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Home' })).toBeInTheDocument();
    expect(await screen.findByText(`Server ${mockHealth.version}`)).toBeInTheDocument();
  });

  it('keeps the header at the top of the viewport over the scrolling page', async () => {
    renderApp();

    await screen.findByRole('heading', { name: 'Home' });
    // jsdom does no layout, so this checks the classes that pin the header (opaque, above the page content), together
    // with the desktop title band above it.
    const top = screen.getByTestId('shell-top');
    expect(top).toHaveClass('sticky', 'top-0', 'z-30', 'bg-surface');
    expect(top).toContainElement(screen.getByRole('banner'));
  });

  it('puts the title band for the window controls above the header, in the flow', async () => {
    renderApp();

    const banner = await screen.findByRole('banner');
    const band = screen.getByTestId('title-bar-strip');
    // theme.css gives the band the height of the OS window controls (none in a browser); the header leaves them no room.
    expect(band.nextElementSibling).toBe(banner);
    expect(band).not.toHaveClass('fixed');
  });

  it("is the desktop window's title bar, with the profile menu's panel still clickable", async () => {
    const user = userEvent.setup();
    renderApp('/movies');

    const header = await screen.findByRole('banner');
    // theme.css makes .title-bar drag the window and its links, buttons, inputs and .no-drag panels clickable.
    expect(header).toHaveClass('title-bar');
    await user.click(await within(header).findByRole('button', { name: /Profile menu/ }));
    expect(within(header).getByRole('link', { name: 'Switch profile' }).closest('.no-drag')).not.toBeNull();
  });

  it('opens the profile menu with Switch profile, closes it on Esc and on an outside click', async () => {
    const user = userEvent.setup();
    renderApp('/movies');
    const button = await screen.findByRole('button', { name: 'Profile menu (owner)' });
    expect(button).toHaveAttribute('aria-expanded', 'false');
    expect(screen.queryByRole('link', { name: 'Switch profile' })).not.toBeInTheDocument();

    await user.click(button);
    expect(button).toHaveAttribute('aria-expanded', 'true');
    expect(screen.getByRole('link', { name: 'Switch profile' })).toBeInTheDocument();
    await user.keyboard('{Escape}');
    expect(screen.queryByRole('link', { name: 'Switch profile' })).not.toBeInTheDocument();
    expect(button).toHaveFocus();

    await user.click(button);
    await user.tab();
    expect(screen.getByRole('link', { name: 'Switch profile' })).toHaveFocus();
    await user.tab();
    expect(screen.queryByRole('link', { name: 'Switch profile' })).not.toBeInTheDocument();

    await user.click(button);
    const search = screen.getByRole('searchbox', { name: 'Search the library' });
    await user.click(search);
    expect(screen.queryByRole('link', { name: 'Switch profile' })).not.toBeInTheDocument();
    expect(search).toHaveFocus();
  });

  it('navigates to Movies and Shows from the keyboard and marks the current page', async () => {
    const user = userEvent.setup();
    renderApp();
    await screen.findByRole('heading', { name: 'Home' });

    screen.getByRole('link', { name: 'Movies' }).focus();
    await user.keyboard('{Enter}');
    expect(await screen.findByRole('heading', { name: 'Movies' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Movies' })).toHaveClass('active');
    expect(screen.getByRole('link', { name: 'Home' })).not.toHaveClass('active');

    await user.click(screen.getByRole('link', { name: 'Shows' }));
    expect(await screen.findByRole('heading', { name: 'Shows' })).toBeInTheDocument();
  });

  it('says what went wrong and what to do when the server returns a problem', async () => {
    server.use(
      http.get('*/health', () =>
        HttpResponse.json(
          {
            title: 'The server is starting up.',
            status: 503,
            code: 'server.starting',
            action: 'Wait a few seconds and reload.',
          },
          { status: 503, headers: { 'Content-Type': 'application/problem+json' } },
        ),
      ),
    );
    renderApp();

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('The server is starting up.');
    expect(alert).toHaveTextContent('Wait a few seconds and reload.');
  });

  it('says the server is unreachable and what to do when the request fails', async () => {
    server.use(http.get('*/health', () => HttpResponse.error()));
    renderApp();

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent("Can't reach the MediaMan server.");
    expect(alert).toHaveTextContent('Check that the server is running');
  });
});
