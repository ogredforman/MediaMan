import type { QueryClient } from '@tanstack/react-query';
import { redirect } from '@tanstack/react-router';
import { getListProfilesQueryKey, listProfiles } from '../api/mediaman';
import type { ProfileList } from '../api/mediaman.schemas';
import { ApiError } from './http';
import { clearTokens } from './session';

/**
 * Asks the server who's signed in, before a signed-in page loads. Whether the user is signed in is the server's
 * answer, not whether a token is stored, so the dev configuration's auto sign-in keeps working. The profile list
 * answers it and says which profile the session has picked (SPEC §7.11 step 3). It's read from the cache after the
 * first time (cleared on sign-out, updated by a pick), so it runs once, not on every navigation.
 *
 * Throws the router's redirect to sign-in on a `401` (coming back to `href` afterwards) and to setup on
 * `423 setup.required`. Returns undefined on anything else (e.g. the server is unreachable) for the page to show.
 */
export async function probeSession(queryClient: QueryClient, href: string): Promise<ProfileList | undefined> {
  try {
    return await queryClient.query({
      queryKey: getListProfilesQueryKey(),
      queryFn: ({ signal }) => listProfiles({ signal }),
      retry: false,
      staleTime: 'static',
    });
  } catch (error) {
    if (error instanceof ApiError && error.status === 401) {
      // eslint-disable-next-line @typescript-eslint/only-throw-error -- the router's redirect is thrown by design.
      throw redirect({ to: '/sign-in', search: { redirect: href } });
    }
    if (error instanceof ApiError && error.problem.code === 'setup.required') {
      // No account exists, so a stored session is left over (e.g. the data was reset); without clearing it the
      // wizard would send a "signed-in" visitor straight back here.
      clearTokens();
      // eslint-disable-next-line @typescript-eslint/only-throw-error -- the router's redirect is thrown by design.
      throw redirect({ to: '/setup' });
    }
    return undefined;
  }
}
