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
    expect(within(header).getByRole('img', { name: 'MediaMan logo' })).toHaveAttribute('src', expect.stringContaining('logo'));
    expect(within(header).getByText('MediaMan')).toBeInTheDocument();
    expect(screen.getByRole('navigation', { name: 'Main' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Home' })).toBeInTheDocument();
    expect(await screen.findByText(`Server ${mockHealth.version}`)).toBeInTheDocument();
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
