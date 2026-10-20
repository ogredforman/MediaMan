// Cuts a release (PLAN 2.5.5): bumps every copy of the app version, commits `release: X.Y.Z`, tags it vX.Y.Z and pushes
// main and the tag together. The tag starts .github/workflows/release.yml, which builds and publishes the release.
// Refuses to start unless main is checked out, clean and level with origin/main, and the current schema has its
// migration fixture (the release workflow checks both too, but only after the tag is public).
//
//   npm run release -- X.Y.Z

import { spawnSync } from 'node:child_process';
import { failer, root } from './lib/pinned.mjs';

const semver = /^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$/;
const versionFiles = ['package.json', 'package-lock.json', 'web/package.json', 'desktop/package.json'];

// Between the bump and the commit, a failure puts the version files back so the next run starts from a clean tree.
let bumped = false;
const failed = failer('release');
function fail(message) {
  if (!bumped) failed(message);
  spawnSync('git', ['restore', '--source=HEAD', '--staged', '--worktree', '--', ...versionFiles], { cwd: root });
  failed(`${message}\nThe version files are back as they were.`);
}

/** Runs git and returns its trimmed output; a failure ends the release with `what` and git's own message. */
function git(args, what) {
  const result = spawnSync('git', args, { cwd: root, encoding: 'utf8' });
  if (result.error) fail(`couldn't ${what} (${result.error.message}). Is git on PATH?`);
  if (result.status !== 0) fail(`couldn't ${what}: ${result.stderr.trim()}`);
  return result.stdout.trim();
}

/** Runs a fixed command line (npm is npm.cmd on Windows, which only a shell runs) with its output shown. */
function run(commandLine, what) {
  const result = spawnSync(commandLine, { cwd: root, stdio: 'inherit', shell: true });
  if (result.error) fail(`couldn't ${what} (${result.error.message}).`);
  if (result.status !== 0) fail(`${what} failed; see the output above.`);
}

const version = process.argv[2];
if (version === undefined || !semver.test(version)) {
  fail('usage: npm run release -- X.Y.Z (or X.Y.Z-suffix for a test release), e.g. npm run release -- 0.3.0');
}
const tag = `v${version}`;

const branch = git(['rev-parse', '--abbrev-ref', 'HEAD'], 'read the current branch');
if (branch !== 'main') fail(`releases are cut from main, but ${branch} is checked out. Run git switch main, then try again.`);
if (git(['status', '--porcelain'], 'read the working tree status') !== '') {
  fail('the working tree has uncommitted changes. Commit or stash them, then try again.');
}
if (git(['tag', '--list', tag], 'list the tags') !== '') fail(`${tag} already exists. Pick the next version.`);

git(['fetch', '--quiet', '--tags', 'origin', 'main'], 'fetch origin');
const local = git(['rev-parse', 'refs/heads/main'], 'read main');
const remote = git(['rev-parse', 'refs/remotes/origin/main'], 'read origin/main');
if (local !== remote) {
  fail('main and origin/main differ. Pull or push until they match (git status shows which), then try again.');
}
if (git(['tag', '--list', tag], 'list the tags') !== '') fail(`${tag} already exists on origin. Pick the next version.`);

run(`"${process.execPath}" scripts/version.mjs check-fixture`, 'check the migration fixture');
bumped = true;
run(`npm version ${version} --workspaces --include-workspace-root --no-git-tag-version`, 'set the version');
run(`"${process.execPath}" scripts/version.mjs check`, 'check the version copies');

git(['add', '--', ...versionFiles], 'stage the version files');
git(['commit', '--quiet', '-m', `release: ${version}`], 'commit the release');
bumped = false;
git(['tag', '-a', tag, '-m', `MediaMan ${version}`], `tag ${tag}`);
// --atomic: main and the tag land together, so the workflow's "tag is on main" check never sees one without the other.
const push = spawnSync('git', ['push', '--atomic', 'origin', 'main', tag], { cwd: root, stdio: 'inherit' });
if (push.error || push.status !== 0) {
  fail(`the release commit and ${tag} are made but weren't pushed (see above). Fix that, then run git push --atomic origin main ${tag}.`);
}

const url = git(['remote', 'get-url', 'origin'], 'read the origin URL');
// https://github.com/owner/repo(.git), or an SSH remote (git@github.com: or a ~/.ssh/config alias) ending owner/repo(.git).
const repo = url.match(/^https:\/\/github\.com\/([\w.-]+\/[\w.-]+?)(?:\.git)?$/)?.[1]
  ?? url.match(/^(?:[\w.-]+@)?[\w.-]+:([\w.-]+\/[\w.-]+?)(?:\.git)?$/)?.[1];
console.log(`Pushed ${tag}. The release workflow builds and publishes it${repo ? `: https://github.com/${repo}/actions` : ''}.`);
