import { expect, test } from '@playwright/test';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

// start-server.mjs makes both folders; the setup spec added Media as the first library.
const workDir = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..', '.e2e-server');
const mediaDir = path.join(workDir, 'Media');
const extraDir = path.join(workDir, 'Extra');

test('adds, renames and deletes a second library in Settings', async ({ page }) => {
  await page.goto('/');
  await page.getByRole('navigation', { name: 'Main' }).getByRole('link', { name: 'Settings' }).click();
  await expect(page.getByRole('heading', { name: 'Libraries' })).toBeVisible();
  const list = page.getByRole('list', { name: 'Library list' });
  await expect(list.getByRole('listitem', { name: 'Media', exact: true })).toBeVisible();

  // A folder inside another library's root is refused, with the reason under that folder.
  await page.getByRole('button', { name: 'Add library' }).click();
  await page.getByRole('textbox', { name: 'Folder 1' }).fill(path.join(mediaDir, 'Movies'));
  await page.getByRole('combobox', { name: 'Contains' }).selectOption({ label: 'Movies' });
  await page.getByRole('button', { name: 'Add library' }).click();
  await expect(page.getByRole('alert').filter({ hasText: "overlaps the library 'Media'" })).toBeVisible();

  await page.getByRole('textbox', { name: 'Folder 1' }).fill(extraDir);
  await page.getByRole('button', { name: 'Add library' }).click();
  const extra = list.getByRole('listitem', { name: 'Extra', exact: true });
  await expect(extra.getByText('Movies · Full scan every 6 hours')).toBeVisible();
  await expect(extra.getByText(extraDir)).toBeVisible();
  // The new library's first scan runs; folders can't change while it does.
  await expect(extra.getByText(/Extra: scanned, 0 linked/)).toBeVisible({ timeout: 30_000 });

  await extra.getByRole('button', { name: 'Edit Extra' }).click();
  await page.getByRole('textbox', { name: 'Name' }).fill('Extra films');
  await page.getByRole('button', { name: 'Save' }).click();
  const renamed = list.getByRole('listitem', { name: 'Extra films', exact: true });
  await expect(renamed).toBeVisible();

  await renamed.getByRole('button', { name: 'Delete Extra films' }).click();
  await expect(renamed.getByText(/Your files stay on disk/)).toBeVisible();
  await renamed.getByRole('button', { name: 'Delete library' }).click();
  await expect(renamed).toHaveCount(0);
  await expect(list.getByRole('listitem', { name: 'Media', exact: true })).toBeVisible();
});
