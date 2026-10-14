import { Link, useNavigate, useRouterState, useSearch } from '@tanstack/react-router';
import { useEffect, useRef, useState, type ReactNode } from 'react';
import { useGetHealth, useListProfiles, useLogout } from '../api/mediaman';
import { clearTokens } from '../lib/session';
import { useSignedIn } from '../lib/useSession';
import { Logo } from './Logo';
import { AvatarTile } from './ProfilePicker';
import { ScanStatus } from './ScanStatus';

const navLinks = [
  { to: '/', label: 'Home' },
  { to: '/movies', label: 'Movies' },
  { to: '/shows', label: 'Shows' },
  { to: '/attention', label: 'Needs attention' },
  { to: '/settings', label: 'Settings' },
] as const;

export function AppShell({ children }: { children: ReactNode }) {
  return (
    <div className="flex min-h-screen flex-col">
      <header className="flex items-center gap-8 bg-surface px-8 py-4 shadow-soft">
        <div className="flex items-center gap-2">
          <Logo size={32} />
          <span className="text-xl font-bold text-highlight">MediaMan</span>
        </div>
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
        <SearchField />
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

/** Is the user typing somewhere `/` means a slash rather than "go to search"? */
function isTypingTarget(target: EventTarget | null): boolean {
  if (!(target instanceof HTMLElement)) return false;
  return target.isContentEditable || ['INPUT', 'TEXTAREA', 'SELECT'].includes(target.tagName);
}

/**
 * The shell's search field (SPEC §11 Search); `/` focuses it from anywhere. The URL holds the query: the first
 * keystroke elsewhere opens `/search?q=` as a new history entry, and later ones replace it, so Back leaves search.
 * While focused the field keeps its own text, since the URL catches up a render later and fast typing would be lost;
 * otherwise it follows the URL (Back, Forward, leaving search).
 */
function SearchField() {
  const navigate = useNavigate();
  const input = useRef<HTMLInputElement>(null);
  const onSearch = useRouterState({ select: (state) => state.location.pathname === '/search' });
  const q = useSearch({ strict: false, select: (search) => search.q });
  const query = onSearch ? (q ?? '') : '';
  const [text, setText] = useState(query);

  useEffect(() => {
    if (document.activeElement !== input.current) setText(query);
  }, [query]);

  useEffect(() => {
    const focusOnSlash = (event: KeyboardEvent) => {
      if (event.key !== '/' || event.ctrlKey || event.metaKey || event.altKey || isTypingTarget(event.target)) return;
      event.preventDefault();
      input.current?.focus();
    };
    document.addEventListener('keydown', focusOnSlash);
    return () => { document.removeEventListener('keydown', focusOnSlash); };
  }, []);

  return (
    <search className="flex-1">
      <input
        ref={input}
        type="search"
        aria-label="Search the library"
        aria-keyshortcuts="/"
        placeholder="Search movies and shows  ( / )"
        value={text}
        onChange={(event) => {
          setText(event.target.value);
          void navigate({ to: '/search', search: { q: event.target.value }, replace: onSearch || text !== '' });
        }}
        onBlur={() => { setText(query); }}
        className="w-full max-w-sm rounded-card bg-bg px-3 py-1.5 text-fg placeholder:text-fg-muted transition-shadow duration-(--motion-fast) focus-visible:outline-2 focus-visible:outline-highlight"
      />
    </search>
  );
}
