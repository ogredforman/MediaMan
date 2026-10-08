import { defineConfig, devices } from '@playwright/test';
import { sessionFile } from './e2e/server/session';

const webPort = 5175;
const serverPort = 5181;
const ci = process.env.CI !== undefined;

// Runs against the real server on fakes (dev configuration, e2e/server/start-server.mjs) behind the Vite dev
// server, which proxies /api/* to it so the UI and API share an origin. Each run starts from fresh, un-set-up data:
// the setup project runs the wizard first, and the other specs reuse its signed-in session.
export default defineConfig({
  testDir: './e2e/server',
  forbidOnly: ci,
  retries: ci ? 1 : 0,
  reporter: ci ? 'github' : 'list',
  use: { baseURL: `http://localhost:${String(webPort)}`, trace: 'on-first-retry' },
  projects: [
    { name: 'setup', testMatch: /setup\.spec\.ts/, use: { ...devices['Desktop Chrome'] } },
    {
      name: 'chromium',
      testIgnore: /setup\.spec\.ts/,
      dependencies: ['setup'],
      use: { ...devices['Desktop Chrome'], storageState: sessionFile },
    },
  ],
  webServer: [
    {
      command: `node e2e/server/start-server.mjs ${String(serverPort)}`,
      url: `http://127.0.0.1:${String(serverPort)}/health`,
      // The first run builds the server.
      timeout: 300_000,
      reuseExistingServer: false,
    },
    {
      command: `npx vite --port ${String(webPort)} --strictPort`,
      url: `http://localhost:${String(webPort)}`,
      reuseExistingServer: false,
      env: { MEDIAMAN_SERVER_URL: `http://127.0.0.1:${String(serverPort)}`, VITE_API_BASE_URL: '/api' },
    },
  ],
});
