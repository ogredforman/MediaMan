import { useGetHealth } from '../api/mediaman';

const sourceUrl = 'https://github.com/ogredforman/MediaMan';

/** Hand-written: what MediaMan ships or runs, and each one's licence. Keep it in step with the dependencies. */
const thirdParty: { name: string; licence: string; url: string }[] = [
  { name: '.NET, ASP.NET Core and Entity Framework Core', licence: 'MIT', url: 'https://github.com/dotnet' },
  { name: 'SQLite', licence: 'Public domain', url: 'https://sqlite.org' },
  { name: 'Microsoft IdentityModel', licence: 'MIT', url: 'https://github.com/AzureAD/azure-activedirectory-identitymodel-extensions-for-dotnet' },
  { name: 'Konscious.Security.Cryptography (Argon2)', licence: 'MIT', url: 'https://github.com/kmaragon/Konscious.Security.Cryptography' },
  { name: 'StbImageSharp', licence: 'Public domain', url: 'https://github.com/StbSharp/StbImageSharp' },
  { name: 'React', licence: 'MIT', url: 'https://react.dev' },
  { name: 'TanStack Query and TanStack Router', licence: 'MIT', url: 'https://tanstack.com' },
  { name: 'Tailwind CSS', licence: 'MIT', url: 'https://tailwindcss.com' },
  { name: 'Electron', licence: 'MIT', url: 'https://www.electronjs.org' },
  { name: 'mpv', licence: 'GPL-2.0-or-later', url: 'https://mpv.io' },
  { name: 'FFmpeg (ffmpeg and ffprobe)', licence: 'LGPL-2.1-or-later or GPL, by build', url: 'https://ffmpeg.org' },
];

const linkClass = 'text-highlight underline';

/** Settings → About and licences: the server's version, MediaMan's licence and source, and third-party notices (SPEC §11). */
export function AboutSettings() {
  const health = useGetHealth();
  return (
    <section aria-labelledby="about-heading" className="flex flex-col gap-4">
      <h2 id="about-heading" className="text-xl font-semibold">
        About and licences
      </h2>
      <div className="flex flex-col gap-2 rounded-card bg-surface p-4">
        <h3 className="font-medium">MediaMan</h3>
        {health.isPending && <p role="status">Loading the server version…</p>}
        {health.isError && <p role="alert">Couldn't reach the server for its version. Check that it's running, then reload.</p>}
        {health.isSuccess && <p>Server version {health.data.version}</p>}
        <p className="text-sm">
          MediaMan is free software under the{' '}
          <a href="https://www.gnu.org/licenses/agpl-3.0.html" target="_blank" rel="noreferrer" className={linkClass}>
            GNU Affero General Public License v3.0
          </a>
          . You can get, change and share its{' '}
          <a href={sourceUrl} target="_blank" rel="noreferrer" className={linkClass}>
            source code
          </a>
          .
        </p>
      </div>
      <div className="flex flex-col gap-2 rounded-card bg-surface p-4">
        <h3 className="font-medium">Metadata</h3>
        <p className="text-sm">
          This product uses the TMDB API but is not endorsed or certified by TMDB. Titles, artwork and episode data come from{' '}
          <a href="https://www.themoviedb.org" target="_blank" rel="noreferrer" className={linkClass}>
            The Movie Database (TMDB)
          </a>
          .
        </p>
      </div>
      <div className="flex flex-col gap-2 rounded-card bg-surface p-4">
        <h3 id="third-party-heading" className="font-medium">
          Third-party software
        </h3>
        <ul aria-labelledby="third-party-heading" className="flex flex-col gap-1 text-sm">
          {thirdParty.map((item) => (
            <li key={item.name}>
              <a href={item.url} target="_blank" rel="noreferrer" className={linkClass}>
                {item.name}
              </a>{' '}
              <span className="text-fg-muted">· {item.licence}</span>
            </li>
          ))}
        </ul>
      </div>
    </section>
  );
}
