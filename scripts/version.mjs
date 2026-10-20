// The app version's checks (PLAN 2.5.5). The version lives in the root package.json; web and desktop carry the same
// one (electron-builder names the installer from desktop/package.json) and server/Directory.Build.props reads it from
// the root, so /health reports it. npm run release -- X.Y.Z (scripts/release.mjs) bumps, commits, tags and pushes; to
// bump every copy, and package-lock.json, by hand:
//
//   npm version X.Y.Z --workspaces --include-workspace-root --no-git-tag-version
//
//   node scripts/version.mjs check                  every copy agrees and is X.Y.Z or X.Y.Z-suffix (npm run lint)
//   node scripts/version.mjs check --tag vX.Y.Z     ...and the tag is v<version> (release workflow)
//   node scripts/version.mjs check-fixture          a migration fixture database exists for the current schema
//                                                   (release workflow and npm run release only: the fixtures
//                                                   come with PLAN 2.5.3)

import { existsSync } from 'node:fs';
import { readdir, readFile } from 'node:fs/promises';
import path from 'node:path';
import { failer, root } from './lib/pinned.mjs';

const fail = failer('version');
const semver = /^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$/;
const workspaces = ['web', 'desktop'];
const migrations = path.join(root, 'server', 'src', 'MediaMan.Data', 'Migrations');
const fixtures = path.join(root, 'server', 'tests', 'fixtures', 'databases');

const readJson = async (file) => JSON.parse(await readFile(path.join(root, file), 'utf8'));

async function check(tag) {
  const version = (await readJson('package.json')).version;
  if (typeof version !== 'string' || !semver.test(version)) {
    fail(`the root package.json version "${String(version)}" isn't X.Y.Z or X.Y.Z-suffix. Set it with npm version.`);
  }
  const lock = await readJson('package-lock.json');
  const copies = [
    ...await Promise.all(workspaces.map(async (ws) => [`${ws}/package.json`, (await readJson(`${ws}/package.json`)).version])),
    ['package-lock.json', lock.version],
    ['package-lock.json (root package)', lock.packages?.['']?.version],
    ...workspaces.map((ws) => [`package-lock.json (${ws})`, lock.packages?.[ws]?.version]),
  ];
  const wrong = copies.filter(([, copy]) => copy !== version);
  if (wrong.length > 0) {
    const list = wrong.map(([file, copy]) => `${file} has ${String(copy)}`).join(', ');
    fail(`the root package.json says ${version} but ${list}. Run npm version ${version} --workspaces --include-workspace-root --no-git-tag-version --allow-same-version.`);
  }
  if (tag !== undefined && tag !== `v${version}`) {
    fail(`the tag ${tag} doesn't match the app version ${version}. Tag the commit v${version}, or bump the version and tag again.`);
  }
  console.log(tag === undefined ? `Version ${version} agrees everywhere.` : `Version ${version} agrees everywhere and with ${tag}.`);
}

/** The newest EF Core migration's id (`<timestamp>_<Name>`), from its file name. */
async function headMigrationId() {
  const ids = (await readdir(migrations))
    .filter((file) => /^\d{14}_\w+\.cs$/.test(file) && !file.endsWith('.Designer.cs'))
    .map((file) => file.slice(0, -'.cs'.length))
    .sort();
  if (ids.length === 0) fail(`no migrations found in ${path.relative(root, migrations)}.`);
  return ids[ids.length - 1];
}

async function checkFixture() {
  const head = await headMigrationId();
  const fixture = path.join(fixtures, `${head}.sql`);
  if (!existsSync(fixture)) {
    fail(`there's no migration fixture for the current schema (${head}). Add ${path.relative(root, fixture)} with npm run fixture:database, so the next release proves it upgrades this one's data, then tag again.`);
  }
  console.log(`The current schema ${head} has its migration fixture.`);
}

const [command, ...args] = process.argv.slice(2);
if (command === 'check') {
  const tagAt = args.indexOf('--tag');
  await check(tagAt >= 0 ? args[tagAt + 1] ?? fail('--tag needs a value, like --tag v0.1.0.') : undefined);
} else if (command === 'check-fixture') {
  await checkFixture();
} else {
  fail('usage: node scripts/version.mjs check [--tag vX.Y.Z] | check-fixture');
}
