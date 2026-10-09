import { createFileRoute, Link, Outlet } from '@tanstack/react-router';

const sections = [{ to: '/settings/libraries', label: 'Libraries' }] as const;

/** Settings, one section per page (SPEC §11); more sections join as their stages land. */
export const Route = createFileRoute('/_app/settings')({
  component: Settings,
});

function Settings() {
  return (
    <section aria-labelledby="settings-heading" className="flex flex-col gap-6 p-8">
      <h1 id="settings-heading" className="text-2xl font-semibold">
        Settings
      </h1>
      <div className="flex flex-col gap-6 md:flex-row">
        <nav aria-label="Settings" className="md:w-48">
          <ul className="flex gap-2 md:flex-col">
            {sections.map((section) => (
              <li key={section.to}>
                <Link
                  to={section.to}
                  className="block rounded-card px-3 py-1 text-fg-muted transition-colors duration-(--motion-fast) hover:text-fg [&.active]:bg-surface [&.active]:text-fg"
                >
                  {section.label}
                </Link>
              </li>
            ))}
          </ul>
        </nav>
        <div className="flex-1">
          <Outlet />
        </div>
      </div>
    </section>
  );
}
