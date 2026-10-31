import { createFileRoute, useRouter } from '@tanstack/react-router';
import { PasswordChangeForm } from '../components/PasswordChangeForm';
import { safeRedirect } from '../lib/redirect';
import { useRedirectOnSignOut } from '../lib/useSession';

interface ChangePasswordSearch {
  /** Where to go once the password is set and a profile is picked: a path on this site. */
  redirect?: string;
}

// A session that must set a new password (SPEC §7.11) comes here from sign-in or the session probe: full screen,
// outside the shell. Afterwards it carries on to the profile picker, which a new sign-in needs anyway.
export const Route = createFileRoute('/change-password')({
  // The router merges this over the raw search, so an unsafe value has to be overwritten, not just left out.
  validateSearch: (search: Record<string, unknown>): ChangePasswordSearch => ({ redirect: safeRedirect(search.redirect) }),
  component: ChangePasswordRoute,
});

function ChangePasswordRoute() {
  const { redirect: target } = Route.useSearch();
  const router = useRouter();
  useRedirectOnSignOut();
  // Back here would only ask again.
  const next = target === undefined || target.startsWith('/change-password') ? undefined : target;
  return (
    <PasswordChangeForm
      onChanged={() => { void router.navigate({ to: '/profiles', search: { redirect: next } }); }}
    />
  );
}
