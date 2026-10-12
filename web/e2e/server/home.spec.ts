import { expect, test } from '@playwright/test';

// The setup spec has signed in and scanned the placeholder library. The fakes give no backdrops, so Home has no hero
// here; Recently Added holds every scanned title whatever the other specs play.
test('shows Recently Added on Home and opens a title from it by keyboard', async ({ page }) => {
  await page.goto('/');
  await expect(page.getByRole('heading', { name: 'Home', level: 1 })).toBeAttached();

  const recent = page.getByRole('list', { name: 'Recently Added' });
  const matrix = recent.getByRole('link', { name: /The Matrix/ });
  await expect(matrix).toBeVisible();

  // One card per row is in the tab order; the arrow keys walk the row and Enter opens the focused card.
  const cards = recent.getByRole('link');
  await cards.first().focus();
  const names = await cards.allTextContents();
  const index = names.findIndex((name) => name.includes('The Matrix'));
  for (let i = 0; i < index; i++) await page.keyboard.press('ArrowRight');
  await expect(matrix).toBeFocused();
  await page.keyboard.press('Enter');

  await expect(page.getByRole('heading', { name: 'The Matrix', level: 1 })).toBeVisible();
});
