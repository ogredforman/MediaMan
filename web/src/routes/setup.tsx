import { createFileRoute, redirect, useRouter } from '@tanstack/react-router';
import { SetupWizard } from '../components/SetupWizard';
import { isSignedIn } from '../lib/session';

// The server sends `423 setup.required` here until a Superadmin exists (SPEC §7.1). A stored session means setup
// already ran, so there's nothing to do.
export const Route = createFileRoute('/setup')({
  beforeLoad: () => {
    // eslint-disable-next-line @typescript-eslint/only-throw-error -- the router's redirect is thrown by design.
    if (isSignedIn()) throw redirect({ to: '/' });
  },
  component: SetupRoute,
});

function SetupRoute() {
  const router = useRouter();
  // Home shows the first library's scan, which setup has queued (SPEC §7.1 step 6).
  return <SetupWizard onComplete={() => { router.history.push('/'); }} />;
}
