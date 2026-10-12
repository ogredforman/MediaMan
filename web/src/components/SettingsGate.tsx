import { Link } from '@tanstack/react-router';
import type { ReactNode } from 'react';
import type { Permission } from '../api/mediaman.schemas';
import { hasPermission, useAccount } from '../lib/account';

/**
 * A Settings section that needs `permission`: the section once the account has it, else what's wrong and what to do.
 * Cosmetic only; the server refuses the section's requests itself (SPEC §5).
 */
export function SettingsGate({ permission, name, children }: { permission: Permission; name: string; children: ReactNode }) {
  const account = useAccount();
  if (account.isPending) return <p role="status">Loading…</p>;
  if (account.isError) {
    return (
      <p role="alert">
        {account.error.problem.title} {account.error.problem.action}
      </p>
    );
  }
  if (hasPermission(account.data, permission)) return children;
  return (
    <div role="alert" className="flex flex-col gap-2">
      <p>
        You don't have access to {name} settings. Ask an admin to give your role the '{permission}' permission.
      </p>
      <Link to="/settings" className="text-highlight underline">
        Back to Settings
      </Link>
    </div>
  );
}
