import { createFileRoute, Link, Outlet } from "@tanstack/react-router";
import { useAccount } from "../../lib/account";
import { visibleSections } from "../../lib/settingsSections";

/** Settings, one section per page (SPEC §11); sections the account can't use are hidden. */
export const Route = createFileRoute("/_app/settings")({
  component: Settings,
});

function Settings() {
  const account = useAccount();
  return (
    <section
      aria-labelledby="settings-heading"
      className="flex flex-col gap-6 p-8"
    >
      <h1 id="settings-heading" className="text-2xl font-semibold">
        Settings
      </h1>
      <div className="flex flex-col gap-6 md:flex-row">
        <nav aria-label="Settings" className="md:w-48">
          <ul className="flex flex-wrap gap-2 md:flex-col">
            {/* Waits for the account, so permission-gated links don't pop in after the rest. */}
            {!account.isPending &&
              visibleSections(account.data).map((section) => (
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
