import { createRootRouteWithContext, Outlet } from '@tanstack/react-router';
import { AppShell } from '../components/AppShell';
import { useLiveScanProgress } from '../lib/useLiveScanProgress';
import type { RouterContext } from '../router';

export const Route = createRootRouteWithContext<RouterContext>()({
  component: RootLayout,
});

function RootLayout() {
  useLiveScanProgress();
  return (
    <AppShell>
      <Outlet />
    </AppShell>
  );
}
