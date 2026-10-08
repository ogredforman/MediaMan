import { useQueryClient } from '@tanstack/react-query';
import { useNavigate, useRouterState } from '@tanstack/react-router';
import { useEffect, useRef, useSyncExternalStore } from 'react';
import { isSignedIn, onSessionChange } from './session';

/** Whether this browser holds a session, kept current across tabs. */
export function useSignedIn(): boolean {
  return useSyncExternalStore(onSessionChange, isSignedIn);
}

/**
 * Sends the user to sign-in when the session ends while they're in the app: Sign out here or in another tab, or a
 * `401` a refresh couldn't fix. The query cache goes too, so nothing from the old session is shown again.
 */
export function useRedirectOnSignOut(): void {
  const signedIn = useSignedIn();
  const wasSignedIn = useRef(signedIn);
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const href = useRouterState({ select: (state) => state.location.href });

  useEffect(() => {
    if (wasSignedIn.current && !signedIn) {
      queryClient.clear();
      void navigate({ to: '/sign-in', search: { redirect: href } });
    }
    wasSignedIn.current = signedIn;
  }, [signedIn, href, navigate, queryClient]);
}
