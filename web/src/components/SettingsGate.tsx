import { Link } from '@tanstack/react-router';
import type { ReactNode } from 'react';
import type { Permission } from '../api/mediaman.schemas';
import { hasPermission, useAccount } from '../lib/account';

/**
 * A Settings section that needs `permission`, or the Superadmin with `superadminOnly`: the section once the account
 * may use it, else what's wrong and what to do. Cosmetic only; the server refuses the section's requests itself (SPEC §5).
 */
export function SettingsGate({
  permission,
  superadminOnly = false,
  name,
  children,
}: {
  permission?: Permission;
  superadminOnly?: boolean;
  name: string;
  children: ReactNode;
}) {
  const account = useAccount();
  if (account.isPending) return <p role="status">Loading…</p>;
  if (account.isError) {
    return (
      <p role="alert">
        {account.error.problem.title} {account.error.problem.action}
      </p>
    );
  }
  if (superadminOnly ? account.data.isSuperadmin : hasPermission(account.data, permission)) return children;
  return (
    <div role="alert" className="flex flex-col gap-2">
      <p>
        {superadminOnly || permission === undefined
          ? `Only the server's owner can change ${name} settings. Ask them if something here needs changing.`
          : `You don't have access to ${name} settings. Ask an admin to give your role the '${permission}' permission.`}
      </p>
      <Link to="/settings" className="text-highlight underline">
        Back to Settings
      </Link>
    </div>
  );
}
