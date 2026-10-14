import { createFileRoute, redirect, useRouter } from '@tanstack/react-router';
import { SignInForm } from '../components/SignInForm';
import { safeRedirect } from '../lib/redirect';
import { isSignedIn } from '../lib/session';

interface SignInSearch {
  /** Where to go after signing in: a path on this site. */
  redirect?: string;
}

export const Route = createFileRoute('/sign-in')({
  // The router merges this over the raw search, so an unsafe value has to be overwritten, not just left out.
  validateSearch: (search: Record<string, unknown>): SignInSearch => ({ redirect: safeRedirect(search.redirect) }),
  beforeLoad: ({ search }) => {
    // eslint-disable-next-line @typescript-eslint/only-throw-error -- the router's redirect is thrown by design.
    if (isSignedIn()) throw redirect({ href: search.redirect ?? '/' });
  },
  component: SignInRoute,
});

function SignInRoute() {
  const { redirect: target } = Route.useSearch();
  const router = useRouter();
  return (
    <SignInForm
      onSignedIn={() => { void router.navigate({ href: target ?? '/' }); }}
      onSetupRequired={() => { void router.navigate({ to: '/setup' }); }}
    />
  );
}
