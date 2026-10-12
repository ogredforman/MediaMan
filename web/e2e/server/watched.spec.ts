import { expect, test } from '@playwright/test';

// The setup spec has signed in and scanned the placeholder library, which holds Breaking Bad's first four episodes.
test('marks a season watched on the detail page, then the show unwatched from its card', async ({ page }) => {
  await page.goto('/shows');
  const shows = page.getByRole('region', { name: 'Shows' });
  await shows.getByRole('link', { name: /Breaking Bad/ }).click();
  await expect(page.getByRole('heading', { name: 'Breaking Bad', level: 1 })).toBeVisible();

  const panel = page.getByRole('tabpanel', { name: 'Season 1' });
  await panel.getByRole('button', { name: 'Mark Season 1 watched' }).click();
  await expect(panel.getByRole('button', { name: 'Mark Season 1 unwatched' })).toBeVisible();
  await expect(panel.getByRole('button', { name: "Mark Cat's in the Bag... unwatched" })).toBeVisible();

  await page.goto('/shows');
  const card = shows.getByRole('link', { name: /Breaking Bad/ });
  await expect(card).toContainText('Watched');

  // Leaves the shared server as the other specs expect it.
  await card.hover();
  await shows.getByRole('button', { name: 'Mark Breaking Bad unwatched' }).click();
  await expect(shows.getByRole('button', { name: 'Mark Breaking Bad watched' })).toBeAttached();
  await expect(card).not.toContainText('Watched');
});
