import { expect, test } from '@playwright/test';

// The setup spec has signed in and scanned the placeholder library, which holds The Matrix.
test('finds a title from the shell search field and opens it', async ({ page }) => {
  await page.goto('/');
  await expect(page.getByRole('heading', { name: 'Home', level: 1 })).toBeAttached();

  await page.keyboard.press('/');
  const field = page.getByRole('searchbox', { name: 'Search the library' });
  await expect(field).toBeFocused();
  await page.keyboard.type('the matr');

  const results = page.getByRole('list', { name: 'Results' });
  const matrix = results.getByRole('link', { name: /The Matrix/ });
  await expect(matrix).toBeVisible();
  await matrix.click();
  await expect(page.getByRole('heading', { name: 'The Matrix', level: 1 })).toBeVisible();

  await page.goto('/search?q=no%20such%20title');
  await expect(page.getByText('No titles in your library match “no such title”.')).toBeVisible();
});
