// Starts the MediaMan server in the dev configuration on the fakes for the server e2e run
// (playwright.server.config.ts): a fresh data directory and a media folder of empty placeholder files that match
// the FakeMetadataProvider fixtures (play.spec.ts plays through a desktop bridge stand-in). Nothing is seeded: the
// setup spec creates the Superadmin and adds the folder as the first library through the wizard.
import { spawn } from 'node:child_process';
import { mkdirSync, rmSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const port = process.argv[2] ?? '5181';
const webRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
const repoRoot = path.resolve(webRoot, '..');
const workDir = path.join(webRoot, '.e2e-server');
const dataDir = path.join(workDir, 'data');
const mediaDir = path.join(workDir, 'Media');

const placeholders = [
  ['Movies', 'The.Matrix.1999.1080p.mkv'],
  // A second version of The Matrix for the version picker. The fake probe makes both 1080p, so the server's
  // tie-break on the path keeps the 1080p file as the best quality.
  ['Movies', 'The.Matrix.1999.720p.mkv'],
  ['Movies', 'Inception.2010.2160p.mkv'],
  ['Shows', 'Breaking Bad', 'Season 1', 'Breaking.Bad.S01E01-E03.720p.mkv'],
  ['Shows', 'Breaking Bad', 'Season 1', 'Breaking.Bad.S01E04.1080p.mkv'],
];

rmSync(workDir, { recursive: true, force: true });
mkdirSync(dataDir, { recursive: true });
for (const parts of placeholders) {
  const file = path.join(mediaDir, ...parts);
  mkdirSync(path.dirname(file), { recursive: true });
  writeFileSync(file, '');
}
// An empty folder the libraries spec adds as a second library.
mkdirSync(path.join(workDir, 'Extra'), { recursive: true });

// The dev configuration only exists in Debug builds, so this builds Debug even where CI builds Release.
const server = spawn(
  'dotnet',
  ['run', '--project', path.join(repoRoot, 'server', 'src', 'MediaMan.Server'), '-c', 'Debug', '--no-launch-profile'],
  {
    stdio: 'inherit',
    env: {
      ...process.env,
      ASPNETCORE_ENVIRONMENT: 'Development',
      MediaMan__Server__Port: port,
      MediaMan__DataDirectory: dataDir,
      MediaMan__Dev__MediaPath: mediaDir,
      MediaMan__Dev__LibraryName: 'Media',
      // Start un-set-up: setup.spec.ts runs the wizard on this media folder and the other specs reuse its session.
      MediaMan__Dev__Seed: 'false',
    },
  },
);

for (const signal of ['SIGINT', 'SIGTERM']) {
  process.on(signal, () => server.kill(signal));
}
server.on('exit', (code) => process.exit(code ?? 1));
