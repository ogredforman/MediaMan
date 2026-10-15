import { expect, test, type Page } from '@playwright/test';
import { installDesktopStandIn, lastPlayedFileName } from './desktopBridge';

// The setup spec has signed in and scanned the placeholder library; these specs share that server and session.
// Play goes through a stand-in for the desktop app's bridge, so MPV never starts (desktopBridge.ts).
test.describe.configure({ mode: 'serial' });

test.beforeEach(async ({ page }) => {
  await installDesktopStandIn(page);
});

/** The next title detail response, so a test can name the file Play streamed. */
function titleResponse(page: Page) {
  return page.waitForResponse((r) => /\/api\/titles\/[^/]+$/.test(new URL(r.url()).pathname) && r.ok());
}

test('browses to a movie and plays it', async ({ page }) => {
  await page.goto('/');
  await page.getByRole('link', { name: 'Movies' }).click();
  const title = titleResponse(page);
  await page.getByRole('region', { name: 'All movies A–Z' }).getByRole('link', { name: /The Matrix/ }).click();

  await expect(page.getByRole('heading', { name: 'The Matrix', level: 1 })).toBeVisible();
  await page.getByRole('button', { name: 'Play', exact: true }).click();
  // Only shown once the server has opened a playback session and the (stand-in) player has started.
  await expect(page.getByRole('status').filter({ hasText: 'in MPV' })).toHaveText('Playing The Matrix (1999) in MPV.');
  expect(await lastPlayedFileName(page, await title)).toBe('The.Matrix.1999.1080p.mkv');
});

test('browses to a show and plays an episode from a multi-episode file', async ({ page }) => {
  await page.goto('/shows');
  const title = titleResponse(page);
  await page.getByRole('region', { name: 'All shows A–Z' }).getByRole('link', { name: /Breaking Bad/ }).click();

  await expect(page.getByRole('heading', { name: 'Breaking Bad', level: 1 })).toBeVisible();
  await page.getByRole('button', { name: "Play Cat's in the Bag..." }).click();
  await expect(page.getByRole('status').filter({ hasText: 'in MPV' })).toHaveText(
    "Playing Breaking Bad S01E02 · Cat's in the Bag... in MPV.",
  );
  expect(await lastPlayedFileName(page, await title)).toBe('Breaking.Bad.S01E01-E03.720p.mkv');
});

test('picks the version of a movie that Play uses, then resets to the best quality', async ({ page }) => {
  await page.goto('/movies');
  const title = titleResponse(page);
  await page.getByRole('region', { name: 'All movies A–Z' }).getByRole('link', { name: /The Matrix/ }).click();
  const versions = page.getByRole('list', { name: 'Versions', exact: true });
  const version = (fileName: string) => versions.getByRole('listitem').filter({ hasText: fileName });

  await expect(version('The.Matrix.1999.1080p.mkv')).toContainText('Plays');
  await versions.getByRole('button', { name: 'Use this version: The.Matrix.1999.720p.mkv' }).click();
  await expect(version('The.Matrix.1999.720p.mkv')).toContainText('Plays');

  await page.getByRole('button', { name: 'Play', exact: true }).click();
  await expect(page.getByRole('status').filter({ hasText: 'in MPV' })).toHaveText('Playing The Matrix (1999) in MPV.');
  expect(await lastPlayedFileName(page, await title)).toBe('The.Matrix.1999.720p.mkv');

  // Resetting also leaves the shared server as the other specs expect it.
  await page.getByRole('button', { name: 'Reset to best quality' }).click();
  await expect(version('The.Matrix.1999.1080p.mkv')).toContainText('Plays');
  await expect(page.getByRole('button', { name: 'Reset to best quality' })).toHaveCount(0);
});
