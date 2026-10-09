import { createFileRoute, useRouter } from '@tanstack/react-router';
import { ProfilePicker } from '../components/ProfilePicker';
import { safeRedirect } from '../lib/redirect';
import { probeSession } from '../lib/sessionProbe';
import { useRedirectOnSignOut } from '../lib/useSession';

interface ProfilesSearch {
  /** Where to go after picking: a path on this site. */
  redirect?: string;
}

// "Who's watching?" (SPEC §7.11 step 3, §11): full screen, outside the shell. The `_app` layout sends a session
// without a profile here, and the shell's Switch profile comes here with one.
export const Route = createFileRoute('/profiles')({
  // The router merges this over the raw search, so an unsafe value has to be overwritten, not just left out.
  validateSearch: (search: Record<string, unknown>): ProfilesSearch => ({ redirect: safeRedirect(search.redirect) }),
  beforeLoad: async ({ context, location }) => {
    await probeSession(context.queryClient, location.href);
  },
  component: ProfilesRoute,
});

function ProfilesRoute() {
  const { redirect: target } = Route.useSearch();
  const router = useRouter();
  useRedirectOnSignOut();
  // Back to the picker would only ask again.
  const next = target === undefined || target.startsWith('/profiles') ? '/' : target;
  return <ProfilePicker onPicked={() => { router.history.push(next); }} />;
}
