import { expect, test } from '@playwright/test';

// The setup spec has signed in and scanned the placeholder library; these specs share that server and session.
test.describe.configure({ mode: 'serial' });

test('browses to a movie and plays it', async ({ page }) => {
  await page.goto('/');

  await page.getByRole('link', { name: 'Movies' }).click();
  const grid = page.getByRole('region', { name: 'Movies' });
  await grid.getByRole('link', { name: /The Matrix/ }).click();

  await expect(page.getByRole('heading', { name: 'The Matrix', level: 1 })).toBeVisible();
  await page.getByRole('button', { name: 'Play', exact: true }).click();
  // Only shown once the server has handed the file to the (fake) player.
  await expect(page.getByRole('status').filter({ hasText: 'in MPV' })).toHaveText('Playing The.Matrix.1999.1080p.mkv in MPV.');
});

test('browses to a show and plays an episode from a multi-episode file', async ({ page }) => {
  await page.goto('/shows');
  await page.getByRole('region', { name: 'Shows' }).getByRole('link', { name: /Breaking Bad/ }).click();

  await expect(page.getByRole('heading', { name: 'Breaking Bad', level: 1 })).toBeVisible();
  await page.getByRole('button', { name: "Play Cat's in the Bag..." }).click();
  await expect(page.getByRole('status').filter({ hasText: 'in MPV' })).toHaveText('Playing Breaking.Bad.S01E01-E03.720p.mkv in MPV.');
});
