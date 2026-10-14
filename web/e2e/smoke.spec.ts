import { expect, test } from '@playwright/test';

test('renders the themed shell against the mock API', async ({ page }) => {
  await page.goto('/');

  await expect(page.getByRole('heading', { name: 'Home' })).toBeVisible();
  await expect(page.getByRole('navigation', { name: 'Main' })).toBeVisible();
  await expect(page.getByText('Server 0.0.0-mock')).toBeVisible();
  // Theme token --color-bg (#0E0B14) is applied.
  await expect(page.locator('body')).toHaveCSS('background-color', 'rgb(14, 11, 20)');
});

test('browses the Movies grid and follows a scan live', async ({ page }) => {
  await page.goto('/');

  await page.getByRole('link', { name: 'Movies' }).click();
  const grid = page.getByRole('region', { name: 'Movies' });
  await expect(grid.getByText('Arrival', { exact: true }).last()).toBeVisible();
  await expect(grid.getByText('The Office')).toHaveCount(0);

  // Mock mode plays a scan over the event stream (src/mocks/browser.ts).
  await page.getByRole('button', { name: /^Libraries/ }).click();
  const libraries = page.getByRole('list', { name: 'Libraries' });
  await libraries.getByRole('button', { name: 'Scan Media' }).click();
  await expect(libraries.getByText('Media: matching titles…')).toBeVisible();
  await expect(libraries.getByText('Media: scanned, 3 linked, 0 need attention')).toBeVisible({ timeout: 10_000 });
});
