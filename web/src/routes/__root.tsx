import { createRootRouteWithContext, Outlet } from '@tanstack/react-router';
import { AppShell } from '../components/AppShell';
import type { RouterContext } from '../router';

export const Route = createRootRouteWithContext<RouterContext>()({
  component: RootLayout,
});

function RootLayout() {
  return (
    <AppShell>
      <Outlet />
    </AppShell>
  );
}
