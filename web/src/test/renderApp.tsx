import { QueryClient } from '@tanstack/react-query';
import { createMemoryHistory } from '@tanstack/react-router';
import { render } from '@testing-library/react';
import { App } from '../App';
import { createAppRouter } from '../router';

/**
 * Renders the whole app (router, query client, shell) at `path`, with no query retries. `basepath` serves it under a
 * path, as the server does (`/app`); `path` then includes it.
 */
export function renderApp(path = '/', { basepath }: { basepath?: string } = {}) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const router = createAppRouter(queryClient, createMemoryHistory({ initialEntries: [path] }), basepath);
  render(<App queryClient={queryClient} router={router} />);
  return { queryClient, router };
}
