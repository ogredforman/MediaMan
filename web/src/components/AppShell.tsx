import { Link } from '@tanstack/react-router';
import type { ReactNode } from 'react';
import { useGetHealth } from '../api/mediaman';

export function AppShell({ children }: { children: ReactNode }) {
  return (
    <div className="flex min-h-screen flex-col">
      <header className="flex items-center gap-8 bg-surface px-8 py-4 shadow-soft">
        <span className="text-xl font-bold text-highlight">MediaMan</span>
        <nav aria-label="Main">
          <ul className="flex gap-4">
            <li>
              <Link
                to="/"
                className="rounded-card px-3 py-1 text-fg-muted transition-colors duration-(--motion-fast) hover:text-fg [&.active]:text-fg"
              >
                Home
              </Link>
            </li>
          </ul>
        </nav>
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
