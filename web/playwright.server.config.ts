import { defineConfig, devices } from '@playwright/test';

const webPort = 5175;
const serverPort = 5181;
const ci = process.env.CI !== undefined;

// Runs against the real server on fakes (dev configuration, e2e/server/start-server.mjs) behind the Vite dev
// server, which proxies /api/* to it so the UI and API share an origin. Each run starts from fresh data.
export default defineConfig({
  testDir: './e2e/server',
  forbidOnly: ci,
  retries: ci ? 1 : 0,
  reporter: ci ? 'github' : 'list',
  use: { baseURL: `http://localhost:${String(webPort)}`, trace: 'on-first-retry' },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
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
