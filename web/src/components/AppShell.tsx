import { Link, useRouterState } from '@tanstack/react-router';
import type { ReactNode } from 'react';
import { useGetHealth, useListProfiles, useLogout } from '../api/mediaman';
import { clearTokens } from '../lib/session';
import { useSignedIn } from '../lib/useSession';
import { AvatarTile } from './ProfilePicker';
import { ScanStatus } from './ScanStatus';

const navLinks = [
  { to: '/', label: 'Home' },
  { to: '/movies', label: 'Movies' },
  { to: '/shows', label: 'Shows' },
  { to: '/settings', label: 'Settings' },
] as const;

export function AppShell({ children }: { children: ReactNode }) {
  return (
    <div className="flex min-h-screen flex-col">
      <header className="flex items-center gap-8 bg-surface px-8 py-4 shadow-soft">
        <span className="text-xl font-bold text-highlight">MediaMan</span>
        <nav aria-label="Main">
          <ul className="flex gap-4">
            {navLinks.map((link) => (
              <li key={link.to}>
                <Link
                  to={link.to}
                  activeOptions={{ exact: link.to === '/' }}
                  className="rounded-card px-3 py-1 text-fg-muted transition-colors duration-(--motion-fast) hover:text-fg [&.active]:text-fg"
                >
                  {link.label}
                </Link>
              </li>
            ))}
          </ul>
        </nav>
        <ScanStatus />
        <SwitchProfile />
        <SignOutButton />
      </header>
      <main className="flex-1">{children}</main>
      <footer className="px-8 py-3 text-sm text-fg-muted">
        <ServerStatus />
      </footer>
    </div>
  );
}

/** The session's profile; picking another goes through the picker and comes back here (SPEC §7.11 step 3). */
function SwitchProfile() {
  const profiles = useListProfiles().data;
  const profile = profiles?.items.find((p) => p.id === profiles.activeProfileId);
  const href = useRouterState({ select: (state) => state.location.href });
  return (
    <Link
      to="/profiles"
      search={{ redirect: href }}
      aria-label={profile === undefined ? 'Switch profile' : `Switch profile (${profile.name})`}
      className="flex items-center gap-2 rounded-card px-3 py-1 text-sm text-fg-muted transition-colors duration-(--motion-fast) hover:text-fg"
    >
      {profile !== undefined && <AvatarTile name={profile.name} avatar={profile.avatar} size="small" />}
      <span>{profile?.name ?? 'Switch profile'}</span>
    </Link>
  );
}

/** Shown only with a stored session (dev auto sign-in has none to end); the `_app` layout then goes to sign-in. */
function SignOutButton() {
  const signedIn = useSignedIn();
  // Signed out here even when the server can't be reached to revoke the session.
  const signOut = useLogout({ mutation: { onSettled: clearTokens } });
  if (!signedIn) return null;
  return (
    <button
      type="button"
      onClick={() => { signOut.mutate(); }}
      disabled={signOut.isPending}
      className="rounded-card px-3 py-1 text-sm text-fg-muted transition-colors duration-(--motion-fast) hover:text-fg disabled:opacity-50"
    >
      Sign out
    </button>
  );
}

function ServerStatus() {
  const health = useGetHealth();

  if (health.isPending) return <p role="status">Connecting to the server…</p>;
  if (health.isError) {
    return (
      <p role="alert">
        {health.error.problem.title} {health.error.problem.action}
      </p>
    );
  }
  return <p role="status">Server {health.data.version}</p>;
}
