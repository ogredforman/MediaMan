import { defineConfig, devices } from '@playwright/test';

const port = 5174;
const ci = process.env.CI !== undefined;

// Runs against the mock-mode dev server (MSW in the browser), so no MediaMan server or network is needed.
export default defineConfig({
  testDir: './e2e',
  // e2e/server runs against the real server instead (playwright.server.config.ts).
  testIgnore: 'server/**',
  forbidOnly: ci,
  retries: ci ? 1 : 0,
  reporter: ci ? 'github' : 'list',
  use: { baseURL: `http://localhost:${String(port)}`, trace: 'on-first-retry' },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
  webServer: {
    command: `npm run dev:mock -- --port ${String(port)} --strictPort`,
    url: `http://localhost:${String(port)}`,
    reuseExistingServer: !ci,
  },
});
