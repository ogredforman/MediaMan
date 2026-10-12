import { getGetAccountQueryOptions, useGetAccount } from '../api/mediaman';
import type { Account, Permission } from '../api/mediaman.schemas';

// The account changes only when an admin edits its role; a minute is fresh enough, and sign-out clears the cache.
const accountStaleTime = 60_000;

/** `GET /account`: the signed-in account and its permissions, for hiding what it can't use (SPEC §11). */
export function useAccount() {
  return useGetAccount({ query: { staleTime: accountStaleTime } });
}

export function accountQueryOptions() {
  return getGetAccountQueryOptions({ query: { staleTime: accountStaleTime } });
}

/** Cosmetic only: the server enforces every permission itself (SPEC §5). */
export function hasPermission(account: Account | undefined, permission: Permission | undefined): boolean {
  return permission === undefined || (account?.permissions.includes(permission) ?? false);
}
