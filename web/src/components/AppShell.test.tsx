import { QueryClient } from '@tanstack/react-query';
import { createMemoryHistory } from '@tanstack/react-router';
import { render, screen } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import { App } from '../App';
import { mockHealth } from '../mocks/handlers';
import { server } from '../mocks/node';
import { createAppRouter } from '../router';

function renderApp() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const router = createAppRouter(queryClient, createMemoryHistory({ initialEntries: ['/'] }));
  render(<App queryClient={queryClient} router={router} />);
}

describe('AppShell', () => {
  it('renders the shell with navigation and the server version', async () => {
    renderApp();

    expect(await screen.findByRole('heading', { name: 'Home' })).toBeInTheDocument();
    expect(screen.getByRole('navigation', { name: 'Main' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Home' })).toBeInTheDocument();
    expect(await screen.findByText(`Server ${mockHealth.version}`)).toBeInTheDocument();
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
