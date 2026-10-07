import { Link } from '@tanstack/react-router';
import type { ReactNode } from 'react';
import { useGetHealth } from '../api/mediaman';
import { ScanStatus } from './ScanStatus';

const navLinks = [
  { to: '/', label: 'Home' },
  { to: '/movies', label: 'Movies' },
  { to: '/shows', label: 'Shows' },
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
                  activeOptions={{ exact: true }}
                  className="rounded-card px-3 py-1 text-fg-muted transition-colors duration-(--motion-fast) hover:text-fg [&.active]:text-fg"
                >
                  {link.label}
                </Link>
              </li>
            ))}
          </ul>
        </nav>
        <ScanStatus />
      </header>
      <main className="flex-1">{children}</main>
      <footer className="px-8 py-3 text-sm text-fg-muted">
        <ServerStatus />
      </footer>
    </div>
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
