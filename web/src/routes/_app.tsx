import { createFileRoute, Outlet, redirect } from '@tanstack/react-router';
import { listLibraries } from '../api/mediaman';
import { AppShell } from '../components/AppShell';
import { ApiError } from '../lib/http';
import { useLiveScanProgress } from '../lib/useLiveScanProgress';
import { useRedirectOnSignOut } from '../lib/useSession';

// Every page inside the shell. Whether the user is signed in is the server's answer, not whether a token is
// stored, so the dev configuration's auto sign-in keeps working. The probe has its own cache entry (cleared on
// sign-out) so it runs once, not on every navigation, and never races live updates to the shell's libraries.
const sessionProbeKey = ['session-probe'] as const;

export const Route = createFileRoute('/_app')({
  beforeLoad: async ({ context, location }) => {
    try {
      await context.queryClient.query({
        queryKey: sessionProbeKey,
        queryFn: ({ signal }) => listLibraries({ limit: 1 }, { signal }),
        retry: false,
        staleTime: 'static',
      });
    } catch (error) {
      if (error instanceof ApiError && error.status === 401) {
        // eslint-disable-next-line @typescript-eslint/only-throw-error -- the router's redirect is thrown by design.
        throw redirect({ to: '/sign-in', search: { redirect: location.href } });
      }
      // Anything else (e.g. the server is unreachable) is for the shell to show, with its next action.
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
