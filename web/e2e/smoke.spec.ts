import { expect, test } from '@playwright/test';

test('renders the themed shell against the mock API', async ({ page }) => {
  await page.goto('/');

  await expect(page.getByRole('heading', { name: 'Home' })).toBeVisible();
  await expect(page.getByRole('navigation', { name: 'Main' })).toBeVisible();
  await expect(page.getByText('Server 0.0.0-mock')).toBeVisible();
  // Theme token --color-bg (#0E0B14) is applied.
  await expect(page.locator('body')).toHaveCSS('background-color', 'rgb(14, 11, 20)');
});
