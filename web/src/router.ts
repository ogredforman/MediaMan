import type { QueryClient } from '@tanstack/react-query';
import { createRouter, type RouterHistory } from '@tanstack/react-router';
import { routeTree } from './routeTree.gen';

export interface RouterContext {
  queryClient: QueryClient;
}

/**
 * Where the app lives: `/app` in the build the server serves (Vite `base`, ADR 0009), `/` on the dev server. Route
 * paths, `location.href` and `?redirect=` values stay without it; navigate with `to` or `href` so the router adds it.
 */
export const appBasepath: string = import.meta.env.BASE_URL.replace(/\/+$/, '') || '/';

export function createAppRouter(queryClient: QueryClient, history?: RouterHistory, basepath = appBasepath) {
  // The window scrolls; going back (Back on a title, the browser or mouse back, Alt+←) puts each page where it was.
  return createRouter({ routeTree, context: { queryClient }, history, basepath, scrollRestoration: true });
}

export type AppRouter = ReturnType<typeof createAppRouter>;

declare module '@tanstack/react-router' {
  interface Register {
    router: AppRouter;
  }
}
