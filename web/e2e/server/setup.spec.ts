import { expect, test as setup } from '@playwright/test';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { sessionFile } from './session';

// start-server.mjs makes this folder and starts the server un-set-up on it.
const mediaDir = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..', '.e2e-server', 'Media');

// Runs first (the `setup` project): the wizard creates the Superadmin and the first library, whose scan setup
// queues, then the new session picks the default profile. The other specs start from the session saved here, which
// the server remembers the pick for.
setup('runs first-run setup, picks a profile and lands on Home with the first scan', async ({ page }) => {
  await page.goto('/');
  await expect(page.getByRole('heading', { name: 'Create the owner account' })).toBeVisible();
  await expect(page.getByText('Step 1 of 3')).toBeVisible();

  await page.getByLabel('Username').fill('owner');
  await page.getByLabel('Password', { exact: true }).fill('correct horse battery');
  await page.getByLabel('Confirm password').fill('correct horse battery');
  await page.getByRole('button', { name: 'Next' }).click();

  await expect(page.getByRole('radio', { name: /This computer only/ })).toBeChecked();
  await page.getByRole('button', { name: 'Next' }).click();

  await page.getByLabel('Folder path').fill(mediaDir);
  await page.getByRole('button', { name: 'Finish setup' }).click();

  await expect(page.getByRole('heading', { name: "Who's watching?" })).toBeVisible();
  await page.getByRole('list', { name: 'Profiles' }).getByRole('button', { name: 'owner' }).click();

  await expect(page.getByRole('heading', { name: 'Home' })).toBeVisible();
  const libraries = page.getByRole('list', { name: 'Libraries' });
  await expect(libraries.getByText(/Media: scanned, \d+ linked/)).toBeVisible({ timeout: 30_000 });

  await page.context().storageState({ path: sessionFile });
});
