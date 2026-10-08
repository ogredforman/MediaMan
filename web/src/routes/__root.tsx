import { createRootRouteWithContext, Outlet } from '@tanstack/react-router';
import type { RouterContext } from '../router';

// Pages inside the shell live under the `_app` layout; sign-in renders on its own.
export const Route = createRootRouteWithContext<RouterContext>()({
  component: Outlet,
});
