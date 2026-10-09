import { createFileRoute, Outlet, redirect } from '@tanstack/react-router';
import { AppShell } from '../components/AppShell';
import { probeSession } from '../lib/sessionProbe';
import { useLiveScanProgress } from '../lib/useLiveScanProgress';
import { useRedirectOnSignOut } from '../lib/useSession';

// Every page inside the shell. A session that hasn't picked a profile goes to the picker first (SPEC §7.11 step 3);
// anything the probe can't answer (e.g. the server is unreachable) is for the shell to show, with its next action.
export const Route = createFileRoute('/_app')({
  beforeLoad: async ({ context, location }) => {
    const profiles = await probeSession(context.queryClient, location.href);
    if (profiles !== undefined && profiles.activeProfileId === undefined) {
      // eslint-disable-next-line @typescript-eslint/only-throw-error -- the router's redirect is thrown by design.
      throw redirect({ to: '/profiles', search: { redirect: location.href } });
    }
  },
  component: AppLayout,
});

function AppLayout() {
  useLiveScanProgress();
  useRedirectOnSignOut();
  return (
    <AppShell>
      <Outlet />
    </AppShell>
  );
}
